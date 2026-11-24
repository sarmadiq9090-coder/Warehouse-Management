using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Text;

namespace Stores
{
    // Physical stock count «الجرد الفعلي» on a copy of the data:
    // create a count, enter quantities, variance numbers, post → balances = counted, item card kinds,
    // issue / receipt reports unchanged, RecalcBalances() = 0, posting twice / editing adjustments / closed year refused.
    // Usage: StockCountTests.exe <copy.mdb> <outdir>     or     StockCountTests.exe --sql "(localdb)\Name" <copy.mdb> <outdir>
    static class StockCountTestsMain
    {
        static int fails;
        static void Check(bool ok, string what) { Console.WriteLine((ok ? "PASS  " : "FAIL  ") + what); if (!ok) fails++; }
        static bool Refused(Action a, out string msg)
        {
            try { a(); msg = ""; return false; }
            catch (UserError e) { msg = e.Message.Split('\n')[0]; return true; }
        }

        static string Dump(ReportData r)
        {
            StringBuilder sb = new StringBuilder();
            foreach (object[] row in r.Rows) { foreach (object o in row) sb.Append(Convert.ToString(o, System.Globalization.CultureInfo.InvariantCulture)).Append('|'); sb.Append('\n'); }
            foreach (KeyValuePair<string, string> kv in r.Summary) sb.Append(kv.Key).Append('=').Append(kv.Value).Append('\n');
            return sb.ToString();
        }

        static Dictionary<string, string> Snapshot(string[] keys)
        {
            Dictionary<string, string> d = new Dictionary<string, string>();
            foreach (string k in keys)
            {
                ReportParams p = new ReportParams(); p.From = DateTime.Today.AddYears(-2); p.To = DateTime.Today;
                d[k] = Dump(Reports.Get(k).Run(p));
            }
            return d;
        }

        [STAThread]
        static int Main(string[] args)
        {
            Console.OutputEncoding = Encoding.UTF8;
            string err;
            if (args[0] == "--sql")
            {
                err = Db.InitSql(Db.BuildSqlConnStr(args[1], "master", "", "", true));
                if (err != null) { Console.WriteLine("sql open failed: " + err); return 1; }
                Db.Exec("IF DB_ID('EngStoresCountTest') IS NOT NULL BEGIN ALTER DATABASE EngStoresCountTest SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE EngStoresCountTest END");
                Db.Exec("CREATE DATABASE EngStoresCountTest");
                err = Db.InitSql(Db.BuildSqlConnStr(args[1], "EngStoresCountTest", "", "", true));
                if (err != null) { Console.WriteLine("sql open failed: " + err); return 1; }
                Session.UserNo = 1; Session.UserName = "1";
                List<string> rep = Migrate.Copy(args[2], delegate(string s) { });
                Check(rep.TrueForAll(l => l.Contains("✓") || l.Contains("غير موجود")), "migration to SQL Server: every table matches");
            }
            else
            {
                err = Db.InitAccess(args[0]);
                if (err != null) { Console.WriteLine("open failed: " + err); return 1; }
            }
            Console.WriteLine("mode: " + (Db.IsSql ? "SQL Server" : "Access"));
            Db.EnsureAppTables();
            Check(Db.HasCount, "count tables + shop.doc_kind created (" + (Db.SchemaWarning ?? "ok") + ")");
            if (!Db.HasCount) return 1;
            Session.UserNo = 1; Session.UserName = "1"; Session.LoadPerms(); Session.Advanced = true;
            Cache.LoadAll();
            int proj = 0;
            Db.InTx(delegate(System.Data.Common.DbTransaction tx)
            {
                proj = Db.Insert(tx, "INSERT INTO project (proj_code, proj_name, client, status, start_date, notes) VALUES (?, ?, ?, ?, ?, ?)", "CNT-1", "مشروع اختبار الجرد", "جهة", 1, DateTime.Today, DBNull.Value);
            });
            Cache.LoadProjects();
            Check(Posting.CcIsRecipient(DocKind.Issue), "a cost centre is required for issues (adjustments must not need one)");
            Console.WriteLine("recalc fixed (historic mismatches): " + Posting.RecalcBalances());
            Cache.LoadItems();

            // ---- choose a store and an item type with enough stock
            DataRow top = Db.Query("SELECT TOP 1 s.stor, s.item_no FROM item_stor AS s INNER JOIN item AS i ON i.item_no = s.item_no WHERE s.item_bal >= 10 AND i.[type] IS NOT NULL ORDER BY s.item_bal DESC").Rows[0];
            int st = Db.I(top["stor"]);
            int type = Db.I(Cache.Item(Db.I(top["item_no"]))["type"]);
            int expected = 0;
            foreach (DataRow r in Db.Query("SELECT item_no, SUM(item_bal) AS b FROM item_stor WHERE stor = ? GROUP BY item_no", st).Rows)
            {
                DataRow ir = Cache.Item(Db.I(r["item_no"]));
                if (ir != null && Db.I(ir["type"]) == type && Math.Abs(Db.D(r["b"])) > 0.0000001) expected++;
            }
            Console.WriteLine("store " + st + " (" + Cache.Store(st) + "), type " + type + " (" + Cache.Type(type) + "), items with balance: " + expected);

            string[] keys = { "issues", "issuedetail", "topitems", "bypersons", "byprojects", "receipts", "receiptdetail", "monthly", "purchreturns" };
            Dictionary<string, string> before = Snapshot(keys);

            // ---- create
            int id = StockCount.Create(DateTime.Today, st, type, "لجنة الاختبار", false);
            CountDoc c = StockCount.Load(id);
            Check(c != null && c.Lines.Count == expected && c.Status == 0, "count created: " + (c == null ? 0 : c.Lines.Count) + " items (expected " + expected + "), open");
            bool booksOk = c.Lines.All(l => Math.Abs(l.Book - Cache.Stock(l.ItemNo, st)) < 0.0001) && c.Lines.All(l => !l.Counted.HasValue);
            Check(booksOk, "book quantity of every line = current balance, nothing counted yet");

            ReportData sheet = CountReports.Sheet(c, false);
            Check(sheet.Rows.Count == c.Lines.Count && !sheet.Cols.Any(x => x.Header.Contains("رصيد") || x.Header.Contains("دفتري")), "blind count sheet: " + sheet.Rows.Count + " rows, no book quantity column");
            List<string> names = sheet.Rows.Select(r => Convert.ToString(r[2])).ToList();
            Check(names.SequenceEqual(names.OrderBy(s => s)), "sheet sorted by item name");
            ReportData sheet2 = CountReports.Sheet(c, true);
            List<string> codes = sheet2.Rows.Select(r => Convert.ToString(r[1])).ToList();
            Check(codes.SequenceEqual(codes.OrderBy(s => s, StringComparer.Ordinal)), "sheet sorted by item code");

            // ---- enter quantities: A shortage 2, B surplus 3, C equal, D added by hand (not in the list)
            List<CountLine> big = c.Lines.Where(l => l.Book >= 5).OrderByDescending(l => l.Book).ToList();
            CountLine A = big[0], B = big[1], C = big[2];
            double bookA = A.Book, bookB = B.Book, bookC = C.Book;
            A.Counted = bookA - 2; A.Dirty = true;
            B.Counted = bookB + 3; B.Dirty = true;
            C.Counted = bookC; C.Dirty = true;
            int dItem = Db.I(Db.Scalar("SELECT TOP 1 i.item_no FROM item AS i WHERE i.[type] <> ? AND NOT EXISTS (SELECT 1 FROM item_stor s WHERE s.item_no = i.item_no AND s.stor = ? AND s.item_bal <> 0) ORDER BY i.item_no", type, st));
            CountLine D = new CountLine(); D.ItemNo = dItem; D.Added = true; D.Counted = 4; D.Dirty = true;
            c.Lines.Add(D);
            double bookD0 = Cache.Stock(dItem, st);
            StockCount.SaveCounts(c);
            Check(D.Id > 0 && Math.Abs(D.Book - bookD0) < 1e-9, "added item saved with its current balance as book (" + Cache.ItemName(dItem) + ", " + bookD0 + ")");
            CountDoc c2 = StockCount.Load(id);
            CountLine a2 = c2.Lines.First(l => l.ItemNo == A.ItemNo), b2 = c2.Lines.First(l => l.ItemNo == B.ItemNo), d2 = c2.Lines.First(l => l.ItemNo == dItem);
            Check(a2.Counted == bookA - 2 && b2.Counted == bookB + 3 && d2.Counted == 4 && d2.Added && c2.Lines.Count(l => l.Counted.HasValue) == 4, "counted quantities saved and reloaded (4 counted)");
            // change one quantity and save again (only changed lines are written)
            a2.Counted = bookA - 2.5; a2.Dirty = true; StockCount.SaveCounts(c2);
            c = StockCount.Load(id);
            Check(c.Lines.First(l => l.ItemNo == A.ItemNo).Counted == bookA - 2.5, "corrected quantity saved");

            // ---- variance
            ReportData v = CountReports.Variance(c, false);
            Func<int, object[]> rowOf = delegate(int item) { return v.Rows.First(r => Convert.ToString(r[1]) == Cache.ItemCode(item) && Convert.ToString(r[2]) == Cache.ItemName(item)); };
            object[] ra = rowOf(A.ItemNo), rb = rowOf(B.ItemNo), rc = rowOf(C.ItemNo);
            Check(Math.Abs(Convert.ToDouble(ra[6]) + 2.5) < 1e-9 && Math.Abs(Convert.ToDouble(ra[7]) - 2.5) < 1e-9 && ra[8] == null, "variance A: diff −2.5, shortage 2.5");
            Check(Math.Abs(Convert.ToDouble(rb[6]) - 3) < 1e-9 && Math.Abs(Convert.ToDouble(rb[8]) - 3) < 1e-9 && rb[7] == null, "variance B: diff +3, surplus 3");
            Check(Math.Abs(Convert.ToDouble(rc[6])) < 1e-9 && Convert.ToString(rc[9]) == "مطابق", "variance C: matches");
            double dDiff = 4 - bookD0;
            double totShort = v.Rows.Where(r => r[7] != null).Sum(r => Convert.ToDouble(r[7])), totSur = v.Rows.Where(r => r[8] != null).Sum(r => Convert.ToDouble(r[8]));
            Check(Math.Abs(totShort - (2.5 + (dDiff < 0 ? -dDiff : 0))) < 1e-9 && Math.Abs(totSur - (3 + (dDiff > 0 ? dDiff : 0))) < 1e-9, "variance totals: shortage " + totShort + ", surplus " + totSur);
            Check(v.Rows.Count(r => Convert.ToString(r[9]) == "لم يُعد") == c.Lines.Count - 4, "not counted items listed as «لم يُعد»: " + (c.Lines.Count - 4));
            Check(CountReports.Variance(c, true).Rows.Count == (Math.Abs(dDiff) > 1e-9 ? 3 : 2), "variance «differences only» lists only items with a difference");

            // ---- permission, closed year
            string msg = "";
            Session.UserNo = 6; Session.UserName = "x"; Session.LoadPerms();
            Check(!StockCount.CanPost && Refused(delegate { StockCount.Post(id); }, out msg), "user without setup permission cannot post: " + msg);
            Session.UserNo = 1; Session.UserName = "1"; Session.LoadPerms();
            int idOld = StockCount.Create(new DateTime(DateTime.Today.Year - 1, 6, 1), st, type, null, false);
            Db.SetSetting("closed_through", new DateTime(DateTime.Today.Year - 1, 12, 31).ToString("yyyy-MM-dd"));
            Check(Refused(delegate { StockCount.Post(idOld); }, out msg), "count dated in a closed year cannot be posted: " + msg);
            Check(Refused(delegate { StockCount.Create(new DateTime(DateTime.Today.Year - 1, 7, 1), st, 0, null, false); }, out msg), "count cannot be created in a closed year");
            Db.SetSetting("closed_through", "");
            StockCount.Delete(idOld);
            Check(StockCount.Load(idOld) == null, "open count deleted");

            // ---- post
            double balA = Cache.Stock(A.ItemNo, st), balC = Cache.Stock(C.ItemNo, st);
            c = StockCount.Post(id);
            Check(c.Posted && c.SaleId > 0 && c.ShopId > 0, "posted: shortage issue " + c.SaleNo + ", surplus receipt " + c.ShopNo);
            DataRow sh = Db.Query("SELECT doc_kind, proj_no, custom_no, stor, [note] FROM sale WHERE shop_no = ?", c.SaleId).Rows[0];
            Check(Db.I(sh["doc_kind"]) == StockCount.AdjKind && sh["proj_no"] is DBNull && Db.I(sh["stor"]) == st, "shortage = sale with doc_kind 2, no project: «" + Db.S(sh["note"]) + "»");
            DataRow rh = Db.Query("SELECT doc_kind, shop_type, stor FROM shop WHERE shop_no = ?", c.ShopId).Rows[0];
            Check(Db.I(rh["doc_kind"]) == StockCount.AdjKind && Db.I(rh["shop_type"]) == 2 && Db.I(rh["stor"]) == st, "surplus = shop (receipt, type local) with doc_kind 2");
            Check(Math.Abs(Cache.Stock(A.ItemNo, st) - (bookA - 2.5)) < 1e-9, "balance A = counted (" + Cache.Stock(A.ItemNo, st) + ")");
            Check(Math.Abs(Cache.Stock(B.ItemNo, st) - (bookB + 3)) < 1e-9, "balance B = counted (" + Cache.Stock(B.ItemNo, st) + ")");
            Check(Math.Abs(Cache.Stock(C.ItemNo, st) - balC) < 1e-9, "balance C unchanged");
            Check(Math.Abs(Cache.Stock(dItem, st) - 4) < 1e-9, "balance D (added item) = counted 4");
            int untouched = c.Lines.Where(l => !l.Counted.HasValue).Take(50).Count(l => Math.Abs(Cache.Stock(l.ItemNo, st) - l.Book) > 1e-9);
            Check(untouched == 0, "not counted items keep their balance");
            double fifo = Db.D(Db.Scalar("SELECT SUM(x.qty) FROM sale_menu_1 AS x INNER JOIN sale_menu AS m ON x.seq_sale = m.auto_seq WHERE m.seq = ?", c.SaleId));
            Check(fifo > 0 && fifo <= 2.5 + Math.Max(0, -dDiff) + 1e-9, "shortage linked to receipt lots by FIFO (" + fifo + ")");
            Check(Posting.RecalcBalances() == 0, "Posting.RecalcBalances() = 0 after posting");

            // ---- item card
            ReportParams cp = new ReportParams(); cp.From = DateTime.Today.AddDays(-1); cp.To = DateTime.Today; cp.Store = st;
            cp.ItemNo = A.ItemNo; ReportData cardA = Reports.ItemCard(cp);
            cp.ItemNo = B.ItemNo; ReportData cardB = Reports.ItemCard(cp);
            object[] la = cardA.Rows.LastOrDefault(r => Convert.ToString(r[1]) == "تسوية جرد (نقص)");
            object[] lb = cardB.Rows.LastOrDefault(r => Convert.ToString(r[1]) == "تسوية جرد (زيادة)");
            Check(la != null && Math.Abs(Convert.ToDouble(la[6]) - 2.5) < 1e-9 && Convert.ToString(la[3]) == "الجرد الفعلي", "item card A shows «تسوية جرد (نقص)» 2.5");
            Check(lb != null && Math.Abs(Convert.ToDouble(lb[5]) - 3) < 1e-9, "item card B shows «تسوية جرد (زيادة)» 3");
            Check(Math.Abs(Convert.ToDouble(cardA.Rows.Last()[7]) - (bookA - 2.5)) < 1e-9, "item card A running balance ends at the counted quantity");

            // ---- reports and navigation do not see adjustments as issues / receipts
            Dictionary<string, string> after = Snapshot(keys);
            foreach (string k in keys) Check(before[k] == after[k], "report «" + Reports.Get(k).Title + "» unchanged by the adjustments");
            Check(Posting.FindByNo(DocKind.Issue, c.SaleNo) == 0 && Posting.FindByNo(DocKind.PurchaseReturn, c.SaleNo) == 0, "shortage document not reachable as an issue / purchase return");
            Check(Posting.FindByNo(DocKind.Receipt, c.ShopNo) == 0 && Posting.FindByNo(DocKind.Return, c.ShopNo) == 0, "surplus document not reachable as a receipt / return");

            // ---- locked
            Check(Refused(delegate { StockCount.Post(id); }, out msg), "posting twice refused: " + msg);
            CountDoc c3 = StockCount.Load(id); c3.Lines[0].Counted = 1; c3.Lines[0].Dirty = true;
            Check(Refused(delegate { StockCount.SaveCounts(c3); }, out msg), "posted count is read-only: " + msg);
            Check(Refused(delegate { StockCount.Delete(id); }, out msg), "posted count cannot be deleted");
            int somePerson = Db.I(Db.Scalar("SELECT TOP 1 custom_no FROM sale WHERE custom_no > 0 ORDER BY shop_no DESC"));
            Check(Refused(delegate { Posting.Delete(Posting.Load(DocKind.Issue, c.SaleId)); }, out msg) && msg.Contains("تسوية جرد"), "shortage document cannot be deleted: " + msg);
            Check(Refused(delegate { Doc x = Posting.Load(DocKind.Issue, c.SaleId); x.Person = somePerson; x.Project = proj; Posting.Save(x); }, out msg) && msg.Contains("تسوية جرد"), "shortage document cannot be edited");
            Check(Refused(delegate { Doc x = Posting.Load(DocKind.Receipt, c.ShopId); x.Person = somePerson; Posting.Save(x); }, out msg) && msg.Contains("تسوية جرد"), "surplus document cannot be edited");
            Check(Refused(delegate { Posting.Delete(Posting.Load(DocKind.Receipt, c.ShopId)); }, out msg) && msg.Contains("تسوية جرد"), "surplus document cannot be deleted");
            Check(Math.Abs(Cache.Stock(A.ItemNo, st) - (bookA - 2.5)) < 1e-9, "balances unchanged by the refused actions");

            // ---- normal documents still work (project rule still applies to real issues)
            Doc iss = new Doc(); iss.Kind = DocKind.Issue; iss.Store = st; iss.Person = somePerson;
            iss.Lines.Add(new DocLine { ItemNo = A.ItemNo, Qty = 1 });
            Check(Refused(delegate { Posting.Save(iss); }, out msg), "normal issue without project still refused: " + msg);
            iss.Project = proj; Posting.Save(iss);
            Check(Math.Abs(Cache.Stock(A.ItemNo, st) - (bookA - 3.5)) < 1e-9, "normal issue after the count works");
            Posting.Delete(Posting.Load(DocKind.Issue, iss.Id));

            // ---- a count with no differences posts without documents
            int idEq = StockCount.Create(DateTime.Today, st, type, null, false);
            CountDoc ce = StockCount.Load(idEq);
            CountLine e0 = ce.Lines.First(l => l.ItemNo == A.ItemNo); e0.Counted = e0.Book; e0.Dirty = true;
            StockCount.SaveCounts(ce);
            ce = StockCount.Post(idEq);
            Check(ce.Posted && ce.SaleId == 0 && ce.ShopId == 0, "count without differences: posted, no documents");
            Check(StockCount.List().Rows.Count >= 2, "count list: " + StockCount.List().Rows.Count + " counts");
            Check(Posting.RecalcBalances() == 0, "Posting.RecalcBalances() = 0 at the end");
            int logs = Db.I(Db.Scalar("SELECT COUNT(*) FROM app_log WHERE act LIKE ?", "%جرد%"));
            Check(logs >= 6, "actions logged (" + logs + ")");

            Console.WriteLine(fails == 0 ? "\nALL TESTS PASSED" : "\nFAILURES: " + fails);
            Db.Close();
            return fails;
        }
    }
}
