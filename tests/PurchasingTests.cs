using System;
using System.Collections.Generic;
using System.Data;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Windows.Forms;

namespace Stores
{
    // Purchasing features on a COPY of the data: shelf location «الموقع» (item dialog → stock report, search, count sheet
    // order, issue voucher), min / max per store + lead time (reorder point and max rules in the critical report),
    // purchase requests (from the critical report rows, partial / full receipt linking, edit / delete of receipts,
    // permissions, balances untouched), and Migrate to SQL Server keeping the new columns and tables.
    // Usage: PurchasingTests.exe <copy.mdb> <outdir> [(localdb)\Instance]   (SQL part: database EngStoresPurchasingTest, LocalDB only)
    static class PurchasingTestsMain
    {
        static int fails, passes;
        static void Check(bool ok, string what) { Console.WriteLine((ok ? "PASS  " : "FAIL  ") + what); if (ok) passes++; else fails++; }
        static bool Refused(Action a) { try { a(); return false; } catch (UserError) { return true; } }

        static object F(object o, string name) { return o.GetType().GetField(name, BindingFlags.NonPublic | BindingFlags.Instance).GetValue(o); }

        /// <summary>Sets location / min / max of one store and the lead time through the real item dialog and saves it.</summary>
        static bool SetPlan(int item, int st, string bin, double min, double max, int lead)
        {
            using (ItemDialog d = new ItemDialog(item))
            {
                DataGridView g = (DataGridView)F(d, "stores");
                foreach (DataGridViewRow r in g.Rows)
                    if (Db.I(r.Cells["stor"].Value) == st)
                    {
                        r.Cells["bin"].Value = bin;
                        r.Cells["min"].Value = min > 0 ? (object)min : null;
                        r.Cells["max"].Value = max > 0 ? (object)max : null;
                    }
                ((TextBox)F(d, "txtLead")).Text = lead > 0 ? lead.ToString() : "";
                return (bool)typeof(ItemDialog).GetMethod("SaveItem", BindingFlags.NonPublic | BindingFlags.Instance).Invoke(d, null);
            }
        }

        static int Col(ReportData r, string header) { return r.Cols.FindIndex(c => c.Header == header); }
        static object[] RowOf(ReportData r, int item) { string n = Cache.ItemName(item); return r.Rows.FirstOrDefault(x => Convert.ToString(x[2]) == n); }

        static Doc Receipt(int st, int person, params object[] itemQty)
        {
            Doc d = new Doc(); d.Kind = DocKind.Receipt; d.Store = st; d.Person = person; d.RType = 2; d.Date = DateTime.Today; d.Note = "اختبار طلبات الشراء";
            for (int i = 0; i < itemQty.Length; i += 2) { DocLine l = new DocLine(); l.ItemNo = (int)itemQty[i]; l.Qty = Convert.ToDouble(itemQty[i + 1]); d.Lines.Add(l); }
            Posting.Save(d);
            return d;
        }

        static double Rec(PReq q, int item) { PReqLine l = q.Lines.FirstOrDefault(x => x.ItemNo == item); return l == null ? -1 : l.Received; }

        [STAThread]
        static int Main(string[] args)
        {
            Console.OutputEncoding = Encoding.UTF8;
            if (args.Length < 2) { Console.WriteLine("usage: PurchasingTests.exe <copy.mdb> <outdir> [(localdb)\\Instance]"); return 2; }
            string copy = args[0], outDir = args[1], sql = args.Length > 2 ? args[2] : null;
            Directory.CreateDirectory(outDir);
            string err = Db.InitAccess(copy);
            if (err != null) { Console.WriteLine("open failed: " + err); return 1; }

            // ---- 1. additive schema on an old file
            bool had = Db.ColumnExists("item_stor", "bin") || Db.ColumnExists("item_stor", "min_qty") || Db.ColumnExists("item", "lead_days") || Db.TableExists("preq") || Db.ColumnExists("shop", "preq_no");
            Console.WriteLine("old file already had purchasing columns: " + had);
            int items0 = Db.I(Db.Scalar("SELECT COUNT(*) FROM item")), stor0 = Db.I(Db.Scalar("SELECT COUNT(*) FROM item_stor")), shop0 = Db.I(Db.Scalar("SELECT COUNT(*) FROM shop"));
            Db.EnsureAppTables();
            Check(Db.HasBin && Db.HasMinMax && Db.HasPreq && Db.SchemaWarning == null, "columns item_stor.bin/min_qty/max_qty, item.lead_days, shop.preq_no and tables preq/preq_menu added (" + (Db.SchemaWarning ?? "ok") + ")");
            Db.EnsureAppTables();
            Check(Db.SchemaWarning == null && Db.HasPreq, "second start: nothing to add, no warning");
            Check(Db.I(Db.Scalar("SELECT COUNT(*) FROM item")) == items0 && Db.I(Db.Scalar("SELECT COUNT(*) FROM item_stor")) == stor0 && Db.I(Db.Scalar("SELECT COUNT(*) FROM shop")) == shop0,
                  "existing rows untouched (" + items0 + " items, " + stor0 + " item_stor, " + shop0 + " receipts)");
            Check(Db.I(Db.Scalar("SELECT COUNT(*) FROM item_stor WHERE bin IS NOT NULL OR min_qty IS NOT NULL OR max_qty IS NOT NULL")) == 0, "new columns empty on old rows (old behaviour kept)");

            Session.UserNo = 1; Session.UserName = "1"; Session.LoadPerms(); Session.Advanced = true;
            Cache.LoadAll();
            Console.WriteLine("recalc fixed (historic mismatches): " + Posting.RecalcBalances());
            Cache.LoadItems();
            int st = Cache.DefaultStore;
            // three active items with stock in the store, unique name and code (the dialog would otherwise ask)
            List<int> cand = new List<int>();
            foreach (DataRow r in Db.Query("SELECT s.item_no FROM item_stor AS s INNER JOIN item AS i ON i.item_no = s.item_no WHERE s.stor = ? AND s.item_bal >= 20 AND i.freez = False AND i.item_code IS NOT NULL ORDER BY s.item_no DESC", st).Rows)
            {
                int it = Db.I(r[0]);
                if (Db.I(Db.Scalar("SELECT COUNT(*) FROM item WHERE item_code = ?", Cache.ItemCode(it))) != 1) continue;
                if (Db.I(Db.Scalar("SELECT COUNT(*) FROM item WHERE item_name = ?", Cache.ItemName(it))) != 1) continue;
                cand.Add(it); if (cand.Count == 60) break;
            }
            // A: an item issued in the last 6 months, so the reorder point uses a real daily average
            HashSet<int> issued = new HashSet<int>();
            foreach (DataRow r in Db.Query("SELECT l.item_no FROM sale AS h INNER JOIN sale_menu AS l ON h.shop_no = l.seq WHERE " + Db.IssueOnly("h") + " AND h.menu_date >= ? AND h.stor = ? GROUP BY l.item_no", DateTime.Today.AddMonths(-6), st).Rows) issued.Add(Db.I(r[0]));
            int ia = cand.FindIndex(x => issued.Contains(x)); if (ia > 0) { int t0 = cand[ia]; cand.RemoveAt(ia); cand.Insert(0, t0); }
            int A = cand[0], B = cand[1], C = cand[2], D = cand[3];
            Console.WriteLine("store " + st + " (" + Cache.Store(st) + "), items A=" + A + " B=" + B + " C=" + C + " D=" + D);

            // ---- 2. shelf location through the item dialog
            double stA = Cache.Stock(A, st), stC = Cache.Stock(C, st);
            double openA = Db.D(Db.Scalar("SELECT item_bal_f FROM item_stor WHERE item_no = ? AND stor = ?", A, st));
            object typeA = Db.Scalar("SELECT [type] FROM item WHERE item_no = ?", A), groupA = Db.Scalar("SELECT [group] FROM item WHERE item_no = ?", A);
            Check(SetPlan(A, st, "C-01", stA + 10, stA + 100, 10), "item dialog saves location + min + max + lead time (A)");
            Check(SetPlan(B, st, "A-02", 0, 0, 0), "item dialog saves location only (B)");
            Check(SetPlan(C, st, "B-05", stC + 5, 0, 0), "item dialog saves location + min (C)");
            Check(Db.S(Db.Scalar("SELECT bin FROM item_stor WHERE item_no = ? AND stor = ?", A, st)) == "C-01" && Math.Abs(Db.D(Db.Scalar("SELECT min_qty FROM item_stor WHERE item_no = ? AND stor = ?", A, st)) - (stA + 10)) < 1e-9 &&
                  Math.Abs(Db.D(Db.Scalar("SELECT max_qty FROM item_stor WHERE item_no = ? AND stor = ?", A, st)) - (stA + 100)) < 1e-9 && Db.I(Db.Scalar("SELECT lead_days FROM item WHERE item_no = ?", A)) == 10,
                  "stored: bin C-01, min " + (stA + 10) + ", max " + (stA + 100) + ", lead 10 days");
            Check(Math.Abs(Cache.Stock(A, st) - stA) < 1e-9 && Math.Abs(Db.D(Db.Scalar("SELECT item_bal_f FROM item_stor WHERE item_no = ? AND stor = ?", A, st)) - openA) < 1e-9, "balance and opening balance unchanged by the dialog");
            Check(Db.I(Db.Scalar("SELECT [type] FROM item WHERE item_no = ?", A)) == Db.I(typeA) && Db.I(Db.Scalar("SELECT [group] FROM item WHERE item_no = ?", A)) == Db.I(groupA),
                  "type " + Db.I(typeA) + " and group kept when the dialog saves");
            Check(Cache.Bin(A, st) == "C-01" && Cache.Bin(B, st) == "A-02" && Cache.Bin(A, 0).Contains("C-01") && Cache.Bin(D, st) == "", "Cache.Bin(item, store) for the search dropdown");
            using (ItemDialog d2 = new ItemDialog(A))
            {
                DataGridView g = (DataGridView)F(d2, "stores");
                DataGridViewRow r = g.Rows.Cast<DataGridViewRow>().First(x => Db.I(x.Cells["stor"].Value) == st);
                Check(Db.S(r.Cells["bin"].Value) == "C-01" && Math.Abs(Db.D(r.Cells["max"].Value) - (stA + 100)) < 1e-9 && ((TextBox)F(d2, "txtLead")).Text == "10", "dialog reopens with the saved values");
            }

            ReportParams sp = new ReportParams(); sp.Store = st; sp.To = DateTime.Today;
            ReportData stock = Reports.Get("stock").Run(sp);
            int ci = Col(stock, "الموقع");
            object[] ra = RowOf(stock, A);
            Check(ci >= 0 && ra != null && Convert.ToString(ra[ci]) == "C-01", "stock report: «الموقع» column shows C-01 for A");
            File.WriteAllText(Path.Combine(outDir, "stock.html"), Html.Render(stock));
            sp.Text = "a-02";
            ReportData found = Reports.Get("stock").Run(sp);
            // the search is by part of the text: B by its location, plus any item whose name / code contains «a-02»
            Check(found.Rows.Any(x => Convert.ToString(x[2]) == Cache.ItemName(B)) && found.Rows.Count(x => Convert.ToString(x[ci]) != "") == 1 &&
                  found.Rows.All(x => Convert.ToString(x[ci]) == "A-02" || UI.Norm(Convert.ToString(x[1]) + " " + Convert.ToString(x[2])).Contains("a-02")),
                  "stock report search box finds the item by its location (A-02 → B; " + found.Rows.Count + " rows with names containing it)");
            sp.Text = Cache.ItemCode(A);
            Check(Reports.Get("stock").Run(sp).Rows.Any(x => Convert.ToString(x[2]) == Cache.ItemName(A)), "search by code still works");

            int cnt = StockCount.Create(DateTime.Today, st, 0, "اختبار الموقع", false);
            CountDoc cd = StockCount.Load(cnt);
            ReportData sheet = CountReports.Sheet(cd, true);
            int sb = Col(sheet, "الموقع");
            List<string> firstBins = sheet.Rows.Take(3).Select(x => Convert.ToString(x[sb])).ToList();
            Check(sb >= 0 && firstBins.SequenceEqual(new[] { "A-02", "B-05", "C-01" }), "count sheet: «الموقع» column, sorted by location first (" + string.Join(", ", firstBins.ToArray()) + ")");
            List<string> rest = sheet.Rows.Skip(3).Select(x => Convert.ToString(x[1])).ToList();
            Check(rest.SequenceEqual(rest.OrderBy(s => s, StringComparer.Ordinal)) && sheet.Rows.Skip(3).All(x => Convert.ToString(x[sb]) == ""), "then the items without location by code (" + rest.Count + ")");
            Check(sheet.Rows.Count == cd.Lines.Count && !sheet.Cols.Any(x => x.Header.Contains("دفتري")), "still a blind sheet with every line");
            File.WriteAllText(Path.Combine(outDir, "count_sheet.html"), Html.Render(sheet));
            StockCount.Delete(cnt);

            Doc iss = new Doc(); iss.Kind = DocKind.Issue; iss.Store = st; iss.No = "test"; iss.Person = 0;
            foreach (int it in new[] { A, D }) { DocLine l = new DocLine(); l.ItemNo = it; l.Qty = 1; iss.Lines.Add(l); }
            ReportData vo = Reports.Voucher(iss);
            int vb = Col(vo, "الموقع");
            Check(vb >= 0 && Convert.ToString(vo.Rows[0][vb]) == "C-01" && Convert.ToString(vo.Rows[1][vb]) == "", "issue voucher: «الموقع» column (C-01 / empty)");
            File.WriteAllText(Path.Combine(outDir, "voucher.html"), Html.Render(vo));
            iss.Kind = DocKind.Receipt;
            Check(Col(Reports.Voucher(iss), "الموقع") < 0, "receipt voucher: no location column");

            // ---- 3. reorder point / max rules on a constructed example (6 months: 180 issued → 30 a month, 1 a day)
            Reports.CriticalRow e1 = Reports.Evaluate(30, 0, 180, 5, 6, 0, 10, 0, 15);
            Check(e1 == null, "ROP = 1 × 15 + 10 = 25; free 30 > 25 → not critical");
            Reports.CriticalRow e2 = Reports.Evaluate(20, 0, 180, 5, 6, 0, 10, 0, 15);
            Check(e2 != null && Math.Abs(e2.Rop - 25) < 1e-9 && e2.Suggest == 40 && e2.Basis.StartsWith(Reports.BasisTwoMonths), "free 20 ≤ ROP 25 → suggested 2 × 30 − 20 = 40 (two months rule, no max)");
            Reports.CriticalRow e3 = Reports.Evaluate(20, 0, 180, 5, 6, 0, 10, 100, 15);
            Check(e3 != null && e3.Suggest == 80 && e3.Basis == Reports.BasisMax, "max 100 → suggested 100 − 20 = 80, basis «" + Reports.BasisMax + "»");
            Reports.CriticalRow e4 = Reports.Evaluate(20, 5, 180, 5, 6, 0, 10, 100, 15);
            Check(e4 != null && e4.Free == 15 && e4.Suggest == 85, "reservation 5 → free 15 → suggested 85");
            Reports.CriticalRow e5 = Reports.Evaluate(20, 0, 90, 5, 3, 0, 10, 0, 15);
            Check(e5 != null && Math.Abs(e5.Rop - 25) < 1e-9 && e5.Suggest == 40, "3-month average gives the same numbers (90 ÷ 3)");
            Reports.CriticalRow e6 = Reports.Evaluate(40, 0, 180, 5, 6, 0, 0, 0, 45);
            Check(e6 != null && Math.Abs(e6.Rop - 45) < 1e-9 && e6.Suggest == 20, "lead time only: ROP 45, free 40 → suggested 60 − 40 = 20");
            Reports.CriticalRow e7 = Reports.Evaluate(10, 0, 30, 5, 6, 0, 50, 0, 30);
            Check(e7 != null && Math.Abs(e7.Rop - 55) < 1e-9 && e7.Suggest == 45 && e7.Basis.Contains("حتى نقطة الطلب"), "ROP 5 × 30 / 30 + 50 = 55 above two months → topped up to the ROP: 45");
            Reports.CriticalRow e8 = Reports.Evaluate(20, 0, 180, 5, 6, 0, 0, 0, 0);
            Check(e8 != null && e8.Suggest == 40 && e8.Reason == "يكفي أقل من شهر", "nothing set: old rule unchanged (less than a month → 40)");
            Reports.CriticalRow e9 = Reports.Evaluate(20, 0, 180, 5, 6, 0, 0, 100, 0);
            Check(e9 != null && e9.Suggest == 80 && e9.Basis == Reports.BasisMax, "max only: old trigger, suggestion from the max (80)");
            Check(Reports.Evaluate(5, 0, 0, 0, 6, 8, 0, 0, 0) != null && Reports.Evaluate(5, 0, 0, 0, 6, 8, 0, 0, 0).Suggest == 3, "old reorder level 8, stock 5 → 3 (unchanged)");

            // the same rule on the real data (item A in the store, item C with min only)
            ReportParams cp = new ReportParams(); cp.Store = st; cp.Months = 6;
            ReportData crit = Reports.Get("critical").Run(cp);
            List<Reports.CriticalRow> rows = (List<Reports.CriticalRow>)crit.Tag;
            Check(rows.Count == crit.Rows.Count && Col(crit, "أساس الاقتراح") >= 0 && crit.Footer.Contains("نقطة إعادة الطلب = المتوسط اليومي × مدة التوريد + الحد الأدنى"),
                  "critical report: «أساس الاقتراح» column, rule stated in the footer, rows = " + rows.Count);
            double usedA = Db.D(Db.Scalar("SELECT SUM(l.qty) FROM sale AS h INNER JOIN sale_menu AS l ON h.shop_no = l.seq WHERE " + Db.IssueOnly("h") + " AND h.menu_date >= ? AND h.stor = ? AND l.item_no = ?", DateTime.Today.AddMonths(-6), st, A));
            double resA = Reservations.Get(A, 0).Where(x => x.Stor == st).Sum(x => x.Outstanding);
            Reports.CriticalRow ca = rows.FirstOrDefault(x => x.Item == A), cc = rows.FirstOrDefault(x => x.Item == C);
            double ropA = usedA / 6 / 30 * 10 + stA + 10, sugA = Math.Ceiling(stA + 100 - (stA - resA) - 1e-9);
            Check(ca != null && Math.Abs(ca.Rop - ropA) < 1e-6 && ca.Suggest == sugA && ca.Basis == Reports.BasisMax,
                  "A: ROP " + Math.Round(ropA, 3) + " (issued " + usedA + " in 6 months × 10 days + min), suggested max − free = " + sugA);
            object[] rowA = RowOf(crit, A);
            Check(rowA != null && Convert.ToString(rowA[Col(crit, "أساس الاقتراح")]) == Reports.BasisMax && Convert.ToInt32(rowA[Col(crit, "مدة التوريد (يوم)")]) == 10, "A's line shows the basis and the lead time");
            Check(cc != null && cc.Basis.StartsWith(Reports.BasisTwoMonths) && cc.Rop == stC + 5, "C (min only): ROP = min, two-months basis");
            ReportParams c3 = new ReportParams(); c3.Store = st; c3.Months = 3; ReportData crit3 = Reports.Get("critical").Run(c3);
            Check(crit3.Cols[9].Header.Contains("آخر 3 أشهر") && crit3.Footer.Contains("÷ 3"), "3 / 6 month averaging choice kept");
            File.WriteAllText(Path.Combine(outDir, "critical.html"), Html.Render(crit));

            // ---- 4. purchase request from the critical report rows (the rows shown after filtering: A and C)
            List<Reports.CriticalRow> shown = rows.Where(x => x.Item == A || x.Item == C).ToList();
            double balBefore = Cache.Stock(A, st);
            int sk;
            int id = PurchaseRequests.CreateFromCritical(shown, st, false, out sk);
            PReq q = PurchaseRequests.Load(id);
            Check(q != null && q.Status == PurchaseRequests.Draft && q.Store == st && q.Lines.Count == 2 && q.No.Length > 0, "draft request " + (q == null ? "" : q.No) + " created with 2 lines");
            Check(q.Lines.All(l => l.Qty == shown.First(x => x.Item == l.ItemNo).Suggest && l.Received == 0), "quantities = suggested quantities (A " + ca.Suggest + ", C " + cc.Suggest + ")");
            Check(Math.Abs(Cache.Stock(A, st) - balBefore) < 1e-9 && Posting.RecalcBalances() == 0, "a request is not a stock movement: balances unchanged");
            int id2 = PurchaseRequests.CreateFromCritical(shown, st, true, out sk);
            Check(id2 == 0 && sk == 2, "again with «skip items already requested»: nothing new (2 skipped)");
            Check(PurchaseRequests.OnOrder(st).ContainsKey(A) && Math.Abs(PurchaseRequests.OnOrder(st)[A] - ca.Suggest) < 1e-9, "on order for A = " + ca.Suggest);
            ReportParams op = new ReportParams(); op.Store = st;
            Check(!Reports.Get("openpreq").Run(op).Rows.Any(x => Convert.ToString(x[1]) == q.No), "open requests report: drafts hidden by default");
            op.Flag = true;
            Check(Reports.Get("openpreq").Run(op).Rows.Count(x => Convert.ToString(x[1]) == q.No) == 2, "… and shown with «إظهار المسودات»");
            int uNo = Session.UserNo; Session.UserNo = 0; Session.LoadPerms();
            Check(Refused(delegate { PurchaseRequests.Save(PurchaseRequests.Load(id)); }) && Refused(delegate { PurchaseRequests.SetStatus(id, PurchaseRequests.Sent); }), "without receipt-add permission: cannot edit or send");
            Session.UserNo = uNo; Session.LoadPerms();

            PurchaseRequests.SetStatus(id, PurchaseRequests.Sent);
            Check(PurchaseRequests.Load(id).Status == PurchaseRequests.Sent, "sent (status 1)");
            double qA = ca.Suggest, qC = cc.Suggest, half = Math.Floor(qA / 2);
            ReportData open1 = Reports.Get("openpreq").Run(op);
            Check(open1.Rows.Where(x => Convert.ToString(x[1]) == q.No).Sum(x => Convert.ToDouble(x[12])) == qA + qC, "open report: remaining " + (qA + qC));

            // ---- 5. receipts linked to the request
            int person = Db.I(Db.Scalar("SELECT TOP 1 custom_no FROM shop ORDER BY shop_no DESC"));
            Doc r1 = Receipt(st, person, A, half, D, 1);
            DataTable of = PurchaseRequests.OpenFor(st, r1.Lines.Select(l => l.ItemNo));
            Check(of.Rows.Cast<DataRow>().Any(x => Db.I(x["preq_no"]) == id && Db.I(x["n"]) == 1), "receipt 1: the request is offered (1 common item)");
            PurchaseRequests.Link(r1.Id, id);
            q = PurchaseRequests.Load(id);
            Check(Db.I(Db.Scalar("SELECT preq_no FROM shop WHERE shop_no = ?", r1.Id)) == id && Rec(q, A) == half && Rec(q, C) == 0 && q.Status == PurchaseRequests.Partial,
                  "partial receipt: A received " + half + " of " + qA + ", status «" + PurchaseRequests.StatusName(q.Status) + "»");
            Doc r2 = Receipt(st, person, A, qA - half, C, qC);
            PurchaseRequests.Link(r2.Id, id);
            q = PurchaseRequests.Load(id);
            Check(Rec(q, A) == qA && Rec(q, C) == qC && q.Status == PurchaseRequests.Done, "full receipt: everything received, status «" + PurchaseRequests.StatusName(q.Status) + "»");
            Check(!Reports.Get("openpreq").Run(op).Rows.Any(x => Convert.ToString(x[1]) == q.No), "completed request leaves the open requests report");
            Check(PurchaseRequests.OpenFor(st, new[] { A, C }).Rows.Cast<DataRow>().All(x => Db.I(x["preq_no"]) != id), "completed request is no longer offered");
            Check(Refused(delegate { PurchaseRequests.Save(PurchaseRequests.Load(id)); }), "completed request is read-only");

            Doc r2e = Posting.Load(DocKind.Receipt, r2.Id);
            r2e.Lines.First(l => l.ItemNo == C).Qty = qC - 1;
            Posting.Save(r2e);
            PurchaseRequests.AfterReceiptSave(null, r2e);     // the DocView hook: already linked → re-applied, no question
            q = PurchaseRequests.Load(id);
            Check(Rec(q, C) == qC - 1 && q.Status == PurchaseRequests.Partial, "receipt edited (C − 1) → received " + (qC - 1) + ", back to «مستلم جزئيا»");
            Posting.Delete(Posting.Load(DocKind.Receipt, r2.Id));
            PurchaseRequests.RecalcAll();
            q = PurchaseRequests.Load(id);
            Check(Rec(q, A) == half && Rec(q, C) == 0 && q.Status == PurchaseRequests.Partial, "receipt 2 deleted → recomputed: A " + half + ", C 0");
            Posting.Delete(Posting.Load(DocKind.Receipt, r1.Id));
            PurchaseRequests.RecalcAll();
            q = PurchaseRequests.Load(id);
            Check(q.Lines.All(l => l.Received == 0) && q.Status == PurchaseRequests.Sent, "all receipts deleted → nothing received, back to «مُرسل»");
            Check(Refused(delegate { PurchaseRequests.Delete(id); }), "a sent request cannot be deleted (only cancelled)");
            Doc r3 = Receipt(st, person, A, 2);
            PurchaseRequests.Link(r3.Id, id);
            q = PurchaseRequests.Load(id);
            q.Lines.First(l => l.ItemNo == A).Qty = 1;
            Check(Refused(delegate { PurchaseRequests.Save(q); }), "ordered quantity cannot go below what was received");
            q = PurchaseRequests.Load(id); q.Lines.RemoveAll(l => l.ItemNo == A);
            Check(Refused(delegate { PurchaseRequests.Save(q); }), "a received line cannot be removed");
            q = PurchaseRequests.Load(id); q.Lines.First(l => l.ItemNo == C).Qty = qC + 3;
            PReqLine extra = new PReqLine(); extra.ItemNo = B; extra.Qty = 7; q.Lines.Add(extra);
            PurchaseRequests.Save(q);
            q = PurchaseRequests.Load(id);
            Check(q.Lines.Count == 3 && q.Lines.First(l => l.ItemNo == C).Qty == qC + 3 && Rec(q, A) == 2, "edit: line changed and added, received kept");
            File.WriteAllText(Path.Combine(outDir, "request.html"), Html.Render(PurchaseReports.Request(q)));
            Check(PurchaseReports.Request(q).Signatures && PurchaseReports.Request(q).Footer.Contains(r3.No), "printed request: signatures and the linked receipt number");
            File.WriteAllText(Path.Combine(outDir, "openpreq.html"), Html.Render(Reports.Get("openpreq").Run(op)));
            DataTable list = PurchaseRequests.List(false);
            Check(list.Rows.Cast<DataRow>().Any(x => Db.I(x["preq_no"]) == id && Db.S(x["status_name"]) == "مستلم جزئيا"), "list of requests: status shown");
            // a draft can be deleted; a cancelled request is kept
            PReq dr = new PReq(); dr.Store = st; PReqLine dl = new PReqLine(); dl.ItemNo = D; dl.Qty = 4; dr.Lines.Add(dl); dl = new PReqLine(); dl.ItemNo = D; dl.Qty = 1; dr.Lines.Add(dl);
            int did = PurchaseRequests.Save(dr);
            Check(PurchaseRequests.Load(did).Lines.Count == 1 && PurchaseRequests.Load(did).Lines[0].Qty == 5 && Convert.ToInt32(PurchaseRequests.Load(did).No) == Convert.ToInt32(PurchaseRequests.Load(id).No) + 1, "manual request: same item merged (5), next number");
            PurchaseRequests.Delete(did);
            Check(PurchaseRequests.Load(did) == null, "draft deleted");
            Check(Posting.RecalcBalances() == 0, "balances match movements after all operations");

            // ---- 6. Migrate to SQL Server keeps the new columns and tables
            if (sql != null)
            {
                if (!sql.StartsWith("(localdb)", StringComparison.OrdinalIgnoreCase)) { Console.WriteLine("refusing: SQL part runs on LocalDB only"); return 2; }
                string binC = Cache.Bin(C, st); double maxA = Db.D(Db.Scalar("SELECT max_qty FROM item_stor WHERE item_no = ? AND stor = ?", A, st));
                int nPreq = Db.I(Db.Scalar("SELECT COUNT(*) FROM preq")), nLines = Db.I(Db.Scalar("SELECT COUNT(*) FROM preq_menu")), nLinked = Db.I(Db.Scalar("SELECT COUNT(*) FROM shop WHERE preq_no IS NOT NULL"));
                double recA = Rec(q, A);
                Db.Close();
                const string name = "EngStoresPurchasingTest";
                err = Db.InitSql(Db.BuildSqlConnStr(sql, "master", "", "", true));
                if (err != null) { Console.WriteLine("sql open failed: " + err); return 1; }
                Db.Exec("IF DB_ID('" + name + "') IS NOT NULL BEGIN ALTER DATABASE " + name + " SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE " + name + " END");
                Db.Exec("CREATE DATABASE " + name);
                err = Db.InitSql(Db.BuildSqlConnStr(sql, name, "", "", true));
                if (err != null) { Console.WriteLine("sql open failed: " + err); return 1; }
                List<string> rep = Migrate.Copy(copy, delegate(string s) { });
                Check(rep.TrueForAll(l => l.Contains("✓") || l.Contains("غير موجود")), "migration to SQL Server: every table matches");
                Check(rep.Any(l => l.StartsWith("preq:")) && rep.Any(l => l.StartsWith("preq_menu:")), "preq and preq_menu are copied");
                Db.EnsureAppTables();
                Check(Db.HasBin && Db.HasMinMax && Db.HasPreq && Db.SchemaWarning == null, "SQL: new columns and tables present (" + (Db.SchemaWarning ?? "ok") + ")");
                Cache.LoadAll();
                Check(Cache.Bin(C, st) == binC && Math.Abs(Db.D(Db.Scalar("SELECT max_qty FROM item_stor WHERE item_no = ? AND stor = ?", A, st)) - maxA) < 1e-9 && Db.I(Db.Scalar("SELECT lead_days FROM item WHERE item_no = ?", A)) == 10,
                      "SQL: location, max and lead time copied");
                Check(Db.I(Db.Scalar("SELECT COUNT(*) FROM preq")) == nPreq && Db.I(Db.Scalar("SELECT COUNT(*) FROM preq_menu")) == nLines && Db.I(Db.Scalar("SELECT COUNT(*) FROM shop WHERE preq_no IS NOT NULL")) == nLinked,
                      "SQL: " + nPreq + " requests, " + nLines + " lines, " + nLinked + " linked receipts");
                PReq qs = PurchaseRequests.Load(id);
                Check(qs != null && qs.Lines.Count == 3 && Rec(qs, A) == recA && qs.Status == PurchaseRequests.Partial, "SQL: request loads with received quantities and status");
                PurchaseRequests.RecalcAll();
                Check(Rec(PurchaseRequests.Load(id), A) == recA, "SQL: recompute from linked receipts gives the same");
                ReportParams sq = new ReportParams(); sq.Store = st; sq.Text = "a-02";
                Check(Reports.Get("stock").Run(sq).Rows.Count == found.Rows.Count, "SQL: stock report location search");
                ReportParams cq = new ReportParams(); cq.Store = st; cq.Months = 6;
                Reports.CriticalRow cs = ((List<Reports.CriticalRow>)Reports.Get("critical").Run(cq).Tag).FirstOrDefault(x => x.Item == A);
                Check(cs != null && cs.Basis == Reports.BasisMax && Math.Abs(cs.Rop - ropA) < 1e-6, "SQL: critical report uses min / max / lead time");
                Doc r4 = Receipt(st, person, C, 1);
                PurchaseRequests.Link(r4.Id, id);
                Check(Rec(PurchaseRequests.Load(id), C) == 1, "SQL: linking a new receipt works");
                PReq ns = new PReq(); ns.Store = st; PReqLine nl = new PReqLine(); nl.ItemNo = D; nl.Qty = 2; ns.Lines.Add(nl);
                int nid = PurchaseRequests.Save(ns);
                Check(Convert.ToInt32(PurchaseRequests.Load(nid).No) == Convert.ToInt32(qs.No) + 1, "SQL: request numbering");
                Check(Reports.Get("openpreq").Run(op).Rows.Count > 0 && PurchaseRequests.List(true).Rows.Count == nPreq + 1, "SQL: open requests report and list");
                Db.Close();
                err = Db.InitSql(Db.BuildSqlConnStr(sql, "master", "", "", true));
                if (err == null) Db.Exec("ALTER DATABASE " + name + " SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE " + name);
            }

            Console.WriteLine();
            Console.WriteLine(fails == 0 ? "ALL " + passes + " PASSED" : fails + " FAILED, " + passes + " passed");
            return fails == 0 ? 0 : 1;
        }
    }
}
