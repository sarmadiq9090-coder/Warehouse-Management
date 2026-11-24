using System;
using System.Collections.Generic;
using System.Data;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Windows.Forms;

namespace Stores
{
    /// <summary>
    /// Write-offs «سندات الإتلاف / شطب المواد»: tables and permission 83 added to a copy of real data; a draft and a sent
    /// write-off move no stock; approval by a permitted user posts it (sale.doc_kind 3, no cost centre) and reduces the balance
    /// by the quantity at FIFO cost; reject; one's own write-off cannot be approved (the administrator can); without the
    /// permission approve / reject are refused; the posted sale cannot be edited or deleted; issue and cost centre reports and
    /// the dashboard figures are unchanged; the item card shows «إتلاف / شطب»; report «الإتلاف حسب السبب» totals; balances
    /// consistent; SQL Server (LocalDB) too.
    /// Usage: WriteOffTests.exe &lt;copy.mdb&gt; &lt;out dir&gt; [&lt;sql instance, e.g. (localdb)\EngStoresTest&gt;]
    /// Screenshots (off-screen) in &lt;out dir&gt;\shots: wo_list.png, wo_new.png, wo_pending.png, wo_posted.png;
    /// the report and the printed write-off as wo_report.html / wo_print.html (tools\HtmlShot.exe renders them to PNG).
    /// </summary>
    static class WriteOffTestsMain
    {
        static int fails = 0;
        static void Check(bool ok, string what) { Console.WriteLine((ok ? "PASS  " : "FAIL  ") + what); if (!ok) fails++; }
        static bool Throws(Action a) { try { a(); return false; } catch (UserError) { return true; } }
        static object Field(object o, string name) { return o.GetType().GetField(name, BindingFlags.NonPublic | BindingFlags.Instance).GetValue(o); }

        [System.Runtime.InteropServices.DllImport("user32.dll")] static extern bool PrintWindow(IntPtr hwnd, IntPtr hdc, uint flags);
        static void Shot(Control c, string path)
        {
            using (Bitmap b = new Bitmap(c.Width, c.Height))
            {
                bool ok;
                using (Graphics g = Graphics.FromImage(b)) { IntPtr dc = g.GetHdc(); ok = PrintWindow(c.Handle, dc, 2); g.ReleaseHdc(dc); }
                if (!ok) c.DrawToBitmap(b, new Rectangle(0, 0, c.Width, c.Height));
                b.Save(path, ImageFormat.Png);
            }
            Console.WriteLine("screenshot: " + path);
        }

        static Form Host(int w, int h)
        {
            Form f = new Form();
            f.StartPosition = FormStartPosition.Manual; f.Location = new Point(-3000, -3000); f.Size = new Size(w, h); f.ShowInTaskbar = false;
            f.RightToLeft = RightToLeft.Yes; f.Font = Theme.Font; f.BackColor = Theme.Bg;
            return f;
        }

        static void As(int user, string name) { Session.UserNo = user; Session.UserName = name; Session.LoadPerms(); }
        static double Bal(int item, int store) { return Db.D(Db.Scalar("SELECT SUM(item_bal) FROM item_stor WHERE item_no = ? AND stor = ?", item, store)); }
        static int N(string sql, params object[] a) { return Db.I(Db.Scalar(sql, a)); }

        static WoDoc NewWo(int store, int reason, string notes, params object[] lines)
        {
            WoDoc d = new WoDoc(); d.Store = store; d.Reason = reason; d.Notes = notes;
            for (int i = 0; i < lines.Length; i += 3) d.Lines.Add(new WoLine { ItemNo = (int)lines[i], Qty = Convert.ToDouble(lines[i + 1]), Reason = (int)lines[i + 2] });
            WriteOffs.Save(d);
            return d;
        }

        /// <summary>Cost of taking qty of an item from its oldest receipt lots (what FIFO must charge).</summary>
        static double FifoCost(int item, double qty)
        {
            double rem = qty, v = 0;
            foreach (DataRow r in Db.Query("SELECT l.qty - l.qty_sale AS av, l.cost FROM shop_menu AS l INNER JOIN shop AS h ON h.shop_no = l.seq " +
                                           "WHERE l.item_no = ? AND l.qty - l.qty_sale > 0.0000001 ORDER BY h.menu_date, l.auto_seq", item).Rows)
            {
                double take = Math.Min(Db.D(r["av"]), rem);
                v += take * Db.D(r["cost"]); rem -= take;
                if (rem <= 1e-9) break;
            }
            return Math.Round(v, 4);
        }

        /// <summary>Everything an issue / cost centre report or the dashboard shows, as one text (must not change with a write-off).</summary>
        static string IssueFigures(int store)
        {
            List<string> s = new List<string>();
            ReportParams p = new ReportParams(); p.From = DateTime.Today.AddYears(-1); p.To = DateTime.Today;
            foreach (string k in new[] { "issues", "issuedetail", "topitems", "bypersons", "byprojects", "monthly" })
            {
                ReportData r = Reports.Get(k).Run(p);
                s.Add(k + ":" + r.Rows.Count + ":" + string.Join(",", r.Summary.Select(kv => kv.Key + "=" + kv.Value).ToArray()));
                if (k == "monthly" || k == "byprojects") foreach (object[] o in r.Rows) s.Add(string.Join("|", o.Select(x => Convert.ToString(x)).ToArray()));
            }
            foreach (KeyValuePair<int, double[]> kv in CostCentres.Totals(p.From, p.ToEx, 0).OrderBy(k => k.Key))
                s.Add("cc" + kv.Key + "=" + string.Join(",", kv.Value.Select(x => Math.Round(x, 4).ToString()).ToArray()));
            s.Add("issueOnly=" + N("SELECT COUNT(*) FROM sale WHERE " + Db.IssueOnly("")));
            s.Add("dashboard=" + N("SELECT COUNT(*) FROM sale WHERE " + Db.NotCountAdj("")));
            return string.Join("\n", s.ToArray());
        }

        static string SumOf(ReportData r, string key) { foreach (KeyValuePair<string, string> kv in r.Summary) if (kv.Key == key) return kv.Value; return null; }

        [STAThread]
        static int Main(string[] args)
        {
            Console.OutputEncoding = System.Text.Encoding.UTF8;
            Application.EnableVisualStyles();
            try { return Run(args); }
            catch (Exception ex) { Console.WriteLine("FAIL  unexpected error: " + ex); return 1; }
        }

        static int Run(string[] args)
        {
            string mdb = args[0], outDir = args[1], sql = args.Length > 2 ? args[2] : null;
            Directory.CreateDirectory(outDir);
            string shots = Path.Combine(outDir, "shots"); Directory.CreateDirectory(shots);
            string err = Db.InitAccess(mdb);
            if (err != null) { Console.WriteLine("open failed: " + err); return 1; }

            // ---- schema: tables + permission row added additively
            Console.WriteLine("wo_head before EnsureAppTables: " + (Db.TableExists("wo_head") ? "exists" : "missing (old file)"));
            Db.EnsureAppTables();
            Check(WriteOffs.Ready && Db.TableExists("wo_head") && Db.TableExists("wo_menu"), "tables wo_head / wo_menu created (" + (Db.SchemaWarning ?? "ok") + ")");
            DataTable pr = Db.Query("SELECT command_name, user1, user2, user3 FROM disply_command WHERE command_no = ?", WriteOffs.PermApprove);
            Check(pr.Rows.Count == 1 && Db.S(pr.Rows[0][0]) == WriteOffs.PermName && Db.B(pr.Rows[0][1]) && !Db.B(pr.Rows[0][2]) && !Db.B(pr.Rows[0][3]),
                  "permission row " + WriteOffs.PermApprove + " «" + WriteOffs.PermName + "»: user1 yes, others no");
            Check(N("SELECT COUNT(*) FROM disply_command WHERE command_no = ?", WriteOffs.PermApprove) == 1 && N("SELECT COUNT(*) FROM disply_command WHERE command_no = ?", Requisitions.PermApprove) == 1,
                  "83 and the requisition permission 82 are separate rows");
            Db.EnsureAppTables();
            Check(Db.SchemaWarning == null && N("SELECT COUNT(*) FROM disply_command WHERE command_no = ?", WriteOffs.PermApprove) == 1, "running EnsureAppTables again is harmless");

            As(1, "1"); Session.Advanced = true;
            Cache.LoadAll();
            Console.WriteLine("recalc fixed (historic mismatches): " + Posting.RecalcBalances());
            Cache.LoadItems();

            // ---- test data: a store with two items that have stock and a known cost
            string tag = DateTime.Now.ToString("HHmmss");
            int store = 0, it1 = 0, it2 = 0;
            foreach (DataRow r in Db.Query("SELECT item_no, stor, item_bal FROM item_stor WHERE item_bal >= 20 ORDER BY item_bal DESC").Rows)
            {
                int it = Db.I(r[0]), st = Db.I(r[1]);
                DataRow ir = Cache.Item(it);
                if (ir == null || Db.B(ir["freez"]) || Requisitions.LastCost(it) <= 0 || FifoCost(it, 5) <= 0) continue;
                if (store == 0) { store = st; it1 = it; continue; }
                if (st == store && it != it1) { it2 = it; break; }
            }
            Check(store > 0 && it1 > 0 && it2 > 0, "found a store with two items in stock with a known cost (store " + store + ", items " + it1 + ", " + it2 + ")");
            double b1 = Bal(it1, store), b2 = Bal(it2, store);
            int sales0 = N("SELECT COUNT(*) FROM sale"), links0 = N("SELECT COUNT(*) FROM sale_menu_1");
            string figures0 = IssueFigures(store);

            // ---- validation
            Check(Throws(delegate { NewWo(0, WriteOffs.Damaged, "", it1, 1, 0); }), "a write-off without a store is refused");
            Check(Throws(delegate { NewWo(store, WriteOffs.Damaged, ""); }), "a write-off without items is refused");
            Check(Throws(delegate { NewWo(store, 0, "", it1, 1, 0); }), "a write-off without a reason is refused");
            Check(Throws(delegate { NewWo(store, WriteOffs.Other, "", it1, 1, 0); }), "reason «أخرى» needs the explanation in the notes");
            Check(Throws(delegate { WoDoc f = new WoDoc(); f.Store = store; f.Date = DateTime.Today.AddDays(3); f.Lines.Add(new WoLine { ItemNo = it1, Qty = 1 }); WriteOffs.Save(f); }), "a future date is refused");

            // ---- draft: nothing moves
            WoDoc a = NewWo(store, WriteOffs.Damaged, "كسر أثناء التحميل " + tag, it1, 2, 0, it1, 1, WriteOffs.Damaged, it1, 2, WriteOffs.Expired, it2, 3, 0);
            WoDoc al = WriteOffs.Load(a.No);
            Check(a.No > 0 && al.Status == WriteOffs.Draft && al.Lines.Count == 3 && al.MenuNo.Length > 0 &&
                  Math.Abs(al.Lines.First(l => l.ItemNo == it1 && l.Reason == 0).Qty - 3) < 1e-9 && al.Lines.Any(l => l.ItemNo == it1 && l.Reason == WriteOffs.Expired && Math.Abs(l.Qty - 2) < 1e-9),
                  "draft saved (no " + al.MenuNo + "): same item + same reason merged (3), a different line reason kept apart (2)");
            Check(Math.Abs(Bal(it1, store) - b1) < 1e-9 && Math.Abs(Bal(it2, store) - b2) < 1e-9 && N("SELECT COUNT(*) FROM sale") == sales0, "draft: balances unchanged, no sale row");
            Check(N("SELECT COUNT(*) FROM app_log WHERE act = ? AND details LIKE ?", "سند إتلاف: مسودة جديدة", "رقم " + al.MenuNo + " %") == 1, "new draft logged");

            // ---- send: still nothing moves
            Check(Throws(delegate { NewWo(store, WriteOffs.Lost, "", it1, b1 + 100, 0).GetHashCode(); WriteOffs.Send(WriteOffs.List(WriteOffs.Draft, "").Rows.Cast<DataRow>().Select(r => Db.I(r["wo_no"])).Max()); }),
                  "sending more than the store balance is refused");
            int big = WriteOffs.List(WriteOffs.Draft, "").Rows.Cast<DataRow>().Select(r => Db.I(r["wo_no"])).Max();
            WriteOffs.Cancel(big);
            WriteOffs.Send(a.No);
            al = WriteOffs.Load(a.No);
            Check(al.Status == WriteOffs.Pending && Math.Abs(Bal(it1, store) - b1) < 1e-9 && N("SELECT COUNT(*) FROM sale") == sales0, "sent: «بانتظار الاعتماد», balances still unchanged");
            Check(N("SELECT COUNT(*) FROM app_log WHERE act = ? AND details LIKE ?", "سند إتلاف: إرسال للاعتماد", "رقم " + al.MenuNo + " %") == 1, "sending logged");
            Check(Throws(delegate { WriteOffs.Save(WriteOffs.Load(a.No)); }), "a write-off waiting for approval cannot be edited");
            Check(Throws(delegate { WriteOffs.Send(a.No); }), "a pending write-off cannot be sent again");

            // ---- permission
            Db.Exec("UPDATE disply_command SET user2 = ?, user3 = ? WHERE command_no = ?", false, true, WriteOffs.PermApprove);
            Db.Exec("UPDATE disply_command SET user2 = ?, user3 = ? WHERE command_no = ?", true, true, Session.IssAdd);
            As(2, "أمين مخزن " + tag);
            Check(!WriteOffs.CanApprove && WriteOffs.CanPrepare && WriteOffs.PendingForMe() == 0, "user 2 without the permission: may prepare, nothing waits for him");
            Check(Throws(delegate { WriteOffs.Approve(a.No); }) && Throws(delegate { WriteOffs.Reject(a.No, "لا"); }), "approve / reject refused without «" + WriteOffs.PermName + "»");
            Check(WriteOffs.Load(a.No).Status == WriteOffs.Pending && Math.Abs(Bal(it1, store) - b1) < 1e-9, "status and balance unchanged after the refusal");
            FlowLayoutPanel noChip = new FlowLayoutPanel();
            Check(!WriteOffsView.AddAlert(noChip, null) && noChip.Controls.Count == 0, "no dashboard chip without the permission");
            // user 2 prepares one that will be rejected
            WoDoc b = NewWo(store, WriteOffs.Lost, "", it2, 1, 0);
            WriteOffs.Send(b.No);

            // ---- approval by a permitted user: posted at FIFO cost
            As(3, "معتمد " + tag);
            Check(WriteOffs.CanApprove && WriteOffs.PendingForMe() == 2, "user 3 with permission 83: two write-offs wait for him (" + WriteOffs.PendingForMe() + ")");
            FlowLayoutPanel chipHost = new FlowLayoutPanel();
            Check(WriteOffsView.AddAlert(chipHost, null) && chipHost.Controls.Count == 1 && chipHost.Controls[0].Text.Contains("بانتظار اعتمادك"), "home screen chip «سندات إتلاف بانتظار اعتمادك»");
            double exp1 = FifoCost(it1, 5), exp2 = FifoCost(it2, 3);
            WriteOffs.Approve(a.No);
            al = WriteOffs.Load(a.No);
            Check(al.Status == WriteOffs.Posted && al.ApprovedBy == "معتمد " + tag && al.ApprovedAt > DateTime.MinValue && al.SaleId > 0 && al.SaleNo.Length > 0,
                  "approved and posted by the permitted user (sale " + al.SaleNo + ")");
            DataRow sh = Db.Query("SELECT doc_kind, proj_no, stor, custom_no, [note] FROM sale WHERE shop_no = ?", al.SaleId).Rows[0];
            Check(Db.I(sh["doc_kind"]) == WriteOffs.SaleKind && sh["proj_no"] is DBNull && Db.I(sh["stor"]) == store && Db.I(sh["custom_no"]) == 0,
                  "posted as sale.doc_kind 3, no cost centre, no recipient: «" + Db.S(sh["note"]) + "»");
            Check(Math.Abs(Bal(it1, store) - (b1 - 5)) < 1e-6 && Math.Abs(Bal(it2, store) - (b2 - 3)) < 1e-6, "balances reduced by exactly the written-off quantities (5 and 3)");
            Check(N("SELECT COUNT(*) FROM sale_menu WHERE seq = ?", al.SaleId) == 3 && al.Lines.All(l => l.SaleLine > 0), "one sale line per write-off line, linked back");
            double fifo = Db.D(Db.Scalar("SELECT SUM(x.qty * s.cost) FROM (sale_menu AS m INNER JOIN sale_menu_1 AS x ON x.seq_sale = m.auto_seq) INNER JOIN shop_menu AS s ON s.auto_seq = x.seq_shop WHERE m.seq = ?", al.SaleId));
            Check(al.Value > 0 && Math.Abs(al.Value - fifo) < 0.01 && Math.Abs(al.Value - (exp1 + exp2)) < 0.01,
                  "value = FIFO cost of the oldest lots (" + UI.Num(al.Value) + " = " + UI.Num(exp1) + " + " + UI.Num(exp2) + ")");
            Check(Math.Abs(al.Lines.Where(l => l.ItemNo == it2).Sum(l => l.Cost) - exp2) < 0.01, "line cost of the second item = its FIFO cost");
            Check(N("SELECT COUNT(*) FROM app_log WHERE act = ? AND user_name = ? AND details LIKE ?", "سند إتلاف: اعتماد وترحيل", "معتمد " + tag, "رقم " + al.MenuNo + " %") == 1, "approval logged with the approver's name");
            Check(Throws(delegate { WriteOffs.Approve(a.No); }) && Throws(delegate { WriteOffs.Cancel(a.No); }) && Throws(delegate { WriteOffs.Save(WriteOffs.Load(a.No)); }),
                  "a posted write-off cannot be approved again, cancelled or edited");
            Check(Throws(delegate { Posting.Delete(Posting.Load(DocKind.Issue, al.SaleId)); }) && Throws(delegate { Posting.Save(Posting.Load(DocKind.Issue, al.SaleId)); }),
                  "the posted sale cannot be deleted or edited through the issue screen");
            Check(Posting.Neighbor(DocKind.Issue, al.SaleId - 1, 1) != al.SaleId && Posting.FindByNo(DocKind.Issue, al.SaleNo) != al.SaleId, "the posted sale is not reachable as an issue (navigation / find by number)");
            Check(Posting.RecalcBalances() == 0, "balances consistent after posting (RecalcBalances() == 0)");

            // ---- reject, then edit → draft, cancel
            Check(Throws(delegate { WriteOffs.Reject(b.No, "  "); }), "reject needs a reason");
            WriteOffs.Reject(b.No, "المادة موجودة في المخزن الآخر");
            WoDoc bl = WriteOffs.Load(b.No);
            Check(bl.Status == WriteOffs.Rejected && bl.RejectReason == "المادة موجودة في المخزن الآخر" && bl.SaleId == 0 && Math.Abs(Bal(it2, store) - (b2 - 3)) < 1e-6,
                  "rejected with the reason; nothing posted");
            Check(N("SELECT COUNT(*) FROM app_log WHERE act = ? AND details LIKE ?", "سند إتلاف: رفض", "رقم " + bl.MenuNo + " %") == 1, "rejection logged");
            As(2, "أمين مخزن " + tag);
            WriteOffs.Save(bl);
            Check(WriteOffs.Load(b.No).Status == WriteOffs.Draft && N("SELECT COUNT(*) FROM app_log WHERE act = ? AND details LIKE ?", "سند إتلاف: إعادة إلى مسودة", "رقم " + bl.MenuNo + " %") == 1,
                  "a rejected write-off edited again becomes a draft (logged)");
            WriteOffs.Cancel(b.No);
            Check(WriteOffs.Load(b.No).Status == WriteOffs.Cancelled && N("SELECT COUNT(*) FROM app_log WHERE act = ? AND details LIKE ?", "سند إتلاف: إلغاء", "رقم " + bl.MenuNo + " %") == 1, "cancelled (logged)");

            // ---- own approval: refused for an approver, allowed for the administrator
            As(3, "معتمد " + tag);
            WoDoc c = NewWo(store, WriteOffs.Obsolete, "", it1, 1, 0);
            WriteOffs.Send(c.No);
            Check(Throws(delegate { WriteOffs.Approve(c.No); }) && Throws(delegate { WriteOffs.Reject(c.No, "x"); }), "an approver cannot approve or reject his own write-off");
            Check(WriteOffs.List(-2, "").Select("wo_no = " + c.No).Length == 0 && WriteOffs.PendingForMe() == 0, "«بانتظار اعتمادك» does not list one's own write-off");
            As(1, "1");
            WoDoc d = NewWo(store, WriteOffs.Expired, "دفعة منتهية", it2, 2, 0);
            WriteOffs.Send(d.No);
            WriteOffs.Approve(c.No);
            WriteOffs.Approve(d.No);
            Check(WriteOffs.Load(c.No).Status == WriteOffs.Posted && WriteOffs.Load(d.No).Status == WriteOffs.Posted, "the administrator approves another's and his own write-off");
            Check(Math.Abs(Bal(it1, store) - (b1 - 6)) < 1e-6 && Math.Abs(Bal(it2, store) - (b2 - 5)) < 1e-6, "balances after three posted write-offs: −6 and −5");

            // ---- reports: issue / cost centre figures unchanged, item card shows the new kind
            Check(IssueFigures(store) == figures0, "issue reports, cost centre reports, dashboard figures unchanged by the write-offs");
            ReportParams cp = new ReportParams(); cp.ItemNo = it2; cp.From = DateTime.Today; cp.To = DateTime.Today; cp.Store = store;
            ReportData card = Reports.ItemCard(cp);
            List<object[]> woRows = card.Rows.Where(o => Convert.ToString(o[1]) == "إتلاف / شطب").ToList();
            Check(woRows.Count == 2 && Math.Abs(woRows.Sum(o => Convert.ToDouble(o[6])) - 5) < 1e-9 && woRows.All(o => Convert.ToString(o[3]) == "سند إتلاف"),
                  "item card: two «إتلاف / شطب» movements out (3 + 2)");
            Check(Reports.KindText(9) == "إتلاف / شطب" && N("SELECT COUNT(*) FROM (" + Reports.MvSql("1=1") + ") AS m WHERE k = 9") == 5, "movement SQL: kind 9 for each posted write-off line (5)");

            // ---- report «الإتلاف حسب السبب»
            ReportDef rd = Reports.Get("writeoffs");
            Check(rd != null && rd.Group == "المخزون" && rd.Title == "الإتلاف حسب السبب" && (rd.P & RP.Dates) != 0, "report «الإتلاف حسب السبب» registered in Reports.All");
            ReportParams rp = new ReportParams(); rp.From = DateTime.Today.AddDays(-1); rp.Store = store;
            ReportData rr = rd.Run(rp);
            double total = new[] { a.No, c.No, d.No }.Sum(no => WriteOffs.Load(no).Value);
            Func<string, object[]> byR = delegate(string name) { return rr.Rows.FirstOrDefault(o => Convert.ToString(o[1]) == name); };
            object[] rDam = byR("تالف"), rExp = byR("منتهي الصلاحية"), rObs = byR("تقادم");
            Check(rr.Rows.Count == 3 && rr.Rows.All(o => o.Length == rr.Cols.Count) && byR("مفقود") == null, "by reason: 3 reasons (rejected «مفقود» not counted)");
            Check(rDam != null && Convert.ToDouble(rDam[2]) == 1 && Convert.ToDouble(rDam[3]) == 2 && Math.Abs(Convert.ToDouble(rDam[4]) - 6) < 1e-9, "«تالف»: 1 document, 2 lines, quantity 6");
            Check(rExp != null && Convert.ToDouble(rExp[2]) == 2 && Math.Abs(Convert.ToDouble(rExp[4]) - 4) < 1e-9, "«منتهي الصلاحية»: 2 documents (line reason + document reason), quantity 4");
            Check(rObs != null && Math.Abs(Convert.ToDouble(rObs[4]) - 1) < 1e-9, "«تقادم»: quantity 1");
            double sumCost = rr.Rows.Sum(o => Convert.ToDouble(o[5]));
            Check(Math.Abs(sumCost - total) < 0.01 && SumOf(rr, "مجموع الكلفة") == UI.Num(rr.Rows.Sum(o => Convert.ToDouble(o[5]))) && SumOf(rr, "عدد سندات الإتلاف") == "3",
                  "by reason: total cost = sum of the posted values (" + UI.Num(total) + "), 3 documents");
            Check(Math.Abs(rr.Rows.Sum(o => Convert.ToDouble(o[6])) - 100) < 0.5, "by reason: shares add up to 100%");
            rp.Flag = true;
            ReportData rf = rd.Run(rp);
            Check(rf.Rows.Count == 5 && Math.Abs(rf.Rows.Sum(o => Convert.ToDouble(o[9])) - total) < 0.01 && rf.Rows.All(o => o.Length == rf.Cols.Count), "detail: 5 lines with the same total cost");
            rp.Flag = false; rp.ItemNo = it2;
            Check(Math.Abs(rd.Run(rp).Rows.Sum(o => Convert.ToDouble(o[4])) - 5) < 1e-9, "filter by item: quantity 5 for the second item");
            File.WriteAllText(Path.Combine(outDir, "wo_report.html"), Html.Render(rr));

            // ---- print
            ReportData pd = WriteOffs.Print(WriteOffs.Load(a.No));
            Check(pd.Signatures && pd.SigLabels.Length == 3 && pd.SigLabels[0].StartsWith("أعدّه") && pd.SigLabels[1].StartsWith("اعتمده") && pd.SigLabels[2] == "أمين المخزن" &&
                  pd.Rows.Count == 3 && pd.Info.Any(kv => kv.Key == "المعتمد") && pd.Info.Any(kv => kv.Key == "سبب الإتلاف") && Math.Abs(pd.Rows.Sum(o => Convert.ToDouble(o[7])) - WriteOffs.Load(a.No).Value) < 0.01,
                  "printed write-off: lines with cost, approver, signatures «أعدّه / اعتمده / أمين المخزن»");
            File.WriteAllText(Path.Combine(outDir, "wo_print.html"), Html.Render(pd));

            // ---- users screen lists the new permission
            using (UsersView uv = new UsersView())
            {
                CheckedListBox cl2 = (CheckedListBox)Field(uv, "perms");
                Check(cl2.Items.Cast<object>().Any(o => Convert.ToString(o) == WriteOffs.PermName), "Users screen lists «" + WriteOffs.PermName + "»");
            }
            Check(Posting.RecalcBalances() == 0, "balances consistent (RecalcBalances() == 0)");

            // ---- screenshots: list for the approver, a new one, a pending one, a posted one, the report
            As(2, "أمين مخزن " + tag);
            WoDoc e = NewWo(store, WriteOffs.Damaged, "تلف بسبب الرطوبة", it1, 2, 0, it2, 1, WriteOffs.Expired);
            WriteOffs.Send(e.No);
            As(1, "1");
            using (Form f = Host(1280, 640))
            {
                WriteOffsView lv = new WriteOffsView(null); lv.Dock = DockStyle.Fill;
                f.Controls.Add(lv); f.Show(); Application.DoEvents(); f.Refresh();
                DataGridView g = (DataGridView)Field(lv, "grid");
                Check(UI.Sel((ComboBox)Field(lv, "cmbStatus")) == -2 && g.Rows.Count >= 1, "approver opens the list on «بانتظار اعتمادك» (" + g.Rows.Count + " rows)");
                Shot(f, Path.Combine(shots, "wo_list.png"));
                f.Close();
            }
            using (Form f = Host(1280, 640))
            {
                WriteOffView ev = new WriteOffView(null, e.No); ev.Dock = DockStyle.Fill;
                f.Controls.Add(ev); f.Show(); Application.DoEvents(); f.Refresh();
                Check(((Button)Field(ev, "btnApprove")).Visible && ((Button)Field(ev, "btnReject")).Visible && !((Button)Field(ev, "btnSave")).Visible, "pending write-off: approver sees «اعتماد وترحيل / رفض», no editing");
                Shot(f, Path.Combine(shots, "wo_pending.png"));
                f.Close();
            }
            using (Form f = Host(1280, 640))
            {
                WriteOffView ev = new WriteOffView(null, a.No); ev.Dock = DockStyle.Fill;
                f.Controls.Add(ev); f.Show(); Application.DoEvents(); f.Refresh();
                Check(!((Button)Field(ev, "btnApprove")).Visible && !((Button)Field(ev, "btnSave")).Visible && ((Button)Field(ev, "btnPrint")).Visible, "posted write-off: read only, printable");
                Shot(f, Path.Combine(shots, "wo_posted.png"));
                f.Close();
            }
            using (Form f = Host(1280, 640))
            {
                WriteOffView ev = new WriteOffView(null, 0); ev.Dock = DockStyle.Fill;
                f.Controls.Add(ev); f.Show(); Application.DoEvents();
                UI.SetSel((ComboBox)Field(ev, "cmbStore"), store);
                ev.AddLine(it1, 4); ev.AddLine(it2, 1);
                ((ItemPicker)Field(ev, "item")).Value = it2;
                Application.DoEvents(); f.Refresh();
                Check(((Button)Field(ev, "btnSend")).Visible && ((DataGridView)Field(ev, "grid")).Rows.Count == 2, "new write-off screen: lines and «إرسال للاعتماد»");
                Shot(f, Path.Combine(shots, "wo_new.png"));
                ev.GetType().GetField("dirty", BindingFlags.NonPublic | BindingFlags.Instance).SetValue(ev, false);
                f.Close();
            }
            // ---- SQL Server: migration copies the tables, the whole flow works there
            if (sql != null)
            {
                int nWo = N("SELECT COUNT(*) FROM wo_head"), nWl = N("SELECT COUNT(*) FROM wo_menu");
                Db.Close();
                const string dbName = "EngStoresWriteOffTest";
                err = Db.InitSql(Db.BuildSqlConnStr(sql, "master", "", "", true));
                if (err != null) { Console.WriteLine("sql open failed: " + err); return 1; }
                Db.Exec("IF DB_ID('" + dbName + "') IS NOT NULL BEGIN ALTER DATABASE " + dbName + " SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE " + dbName + " END");
                Db.Exec("CREATE DATABASE " + dbName);
                err = Db.InitSql(Db.BuildSqlConnStr(sql, dbName, "", "", true));
                if (err != null) { Console.WriteLine("sql open failed: " + err); return 1; }
                List<string> rep = Migrate.Copy(mdb, delegate(string s) { });
                Check(rep.TrueForAll(l => l.Contains("✓") || l.Contains("غير موجود")), "migration: every table row count matches");
                Check(N("SELECT COUNT(*) FROM wo_head") == nWo && N("SELECT COUNT(*) FROM wo_menu") == nWl && N("SELECT COUNT(*) FROM disply_command WHERE command_no = 83") == 1,
                      "migration copies wo_head (" + nWo + "), wo_menu (" + nWl + ") and permission 83");
                Db.EnsureAppTables(); Cache.LoadAll(); As(1, "1");
                Check(WriteOffs.Ready && Db.SchemaWarning == null, "SQL Server mode: write-offs ready");
                string sqlFigures = IssueFigures(store);
                double s1 = Bal(it1, store);
                WriteOffs.Approve(e.No);                 // prepared by user 2 on the Access copy
                WoDoc el = WriteOffs.Load(e.No);
                Check(el.Status == WriteOffs.Posted && Math.Abs(Bal(it1, store) - (s1 - 2)) < 1e-6 && el.Value > 0 &&
                      Db.I(Db.Scalar("SELECT doc_kind FROM sale WHERE shop_no = ?", el.SaleId)) == WriteOffs.SaleKind, "SQL: approval posts (doc_kind 3) and reduces the balance, value " + UI.Num(el.Value));
                As(3, "معتمد " + tag);
                WoDoc g2 = NewWo(store, WriteOffs.Lost, "", it2, 1, 0);
                WriteOffs.Send(g2.No);
                Check(Throws(delegate { WriteOffs.Approve(g2.No); }), "SQL: own approval refused");
                As(1, "1");
                WriteOffs.Reject(g2.No, "غير موجود فعلا");
                Check(WriteOffs.Load(g2.No).Status == WriteOffs.Rejected, "SQL: reject");
                Check(IssueFigures(store) == sqlFigures, "SQL: issue / cost centre / dashboard figures unchanged");
                ReportParams sp = new ReportParams(); sp.From = DateTime.Today.AddDays(-1); sp.Store = store;
                ReportData sr = Reports.Get("writeoffs").Run(sp);
                Check(SumOf(sr, "عدد سندات الإتلاف") == "4" && Math.Abs(sr.Rows.Sum(o => Convert.ToDouble(o[5])) - (total + el.Value)) < 0.01, "SQL: report «الإتلاف حسب السبب» (4 documents, total cost)");
                Check(Posting.RecalcBalances() == 0, "SQL: balances consistent");
                Db.Close();
                Db.InitSql(Db.BuildSqlConnStr(sql, "master", "", "", true));
                Db.Exec("ALTER DATABASE " + dbName + " SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE " + dbName);
                Db.Close();
                err = Db.InitAccess(mdb);
                Db.EnsureAppTables(); Cache.LoadAll(); As(1, "1");
            }

            Check(Posting.RecalcBalances() == 0, "balances consistent at the end");
            Console.WriteLine(fails == 0 ? "\nALL TESTS PASSED" : "\nFAILURES: " + fails);
            Db.Close();
            return fails;
        }
    }
}
