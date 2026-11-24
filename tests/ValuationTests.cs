using System;
using System.Collections.Generic;
using System.Data;
using System.Diagnostics;
using System.IO;
using System.Linq;

namespace Stores
{
    /// <summary>
    /// Stock valuation (IAS 2, FIFO, see src/Domain/Valuation.cs): value after receipts at different costs and partial
    /// issues, an issue return coming back at the cost it went out at, a receipt without a price taking the last known
    /// cost, value at past dates, store values after a transfer, the reports «تقييم المخزون بتاريخ» and «جرد أرصدة المواد»
    /// (value column), the repair of old lots without a cost, and balances staying consistent (RecalcBalances() == 0).
    /// Usage: ValuationTests.exe &lt;copy.mdb&gt; &lt;out dir&gt; [&lt;sql instance, e.g. (localdb)\EngStoresTest&gt;]
    /// With a SQL instance the copy is moved to a new database there, the same tests run, and the database is dropped.
    /// </summary>
    static class ValuationTestsMain
    {
        static int fails = 0;
        static void Check(bool ok, string what) { Console.WriteLine((ok ? "PASS  " : "FAIL  ") + what); if (!ok) fails++; }
        static bool Near(double a, double b) { return Math.Abs(a - b) < 0.001; }
        static string N(double v) { return Math.Round(v, 4).ToString(System.Globalization.CultureInfo.InvariantCulture); }
        static int ColOf(ReportData r, string header) { for (int i = 0; i < r.Cols.Count; i++) if (r.Cols[i].Header == header) return i; return -1; }

        [STAThread]
        static int Main(string[] args)
        {
            Console.OutputEncoding = System.Text.Encoding.UTF8;
            try { return Run(args); }
            catch (Exception ex) { Console.WriteLine("FAIL  unexpected error: " + ex); return 1; }
        }

        static int Run(string[] args)
        {
            string mdb = args[0], outDir = args[1], sql = args.Length > 2 ? args[2] : null;
            Directory.CreateDirectory(outDir);
            Settings.Set("BackupDir", Path.Combine(outDir, "backups"));
            string err = Db.InitAccess(mdb);
            if (err != null) { Console.WriteLine("open failed: " + err); return 1; }
            Db.EnsureAppTables();
            const string dbName = "EngStoresValuationTest";
            if (sql != null)
            {
                Db.Close();
                err = Db.InitSql(Db.BuildSqlConnStr(sql, "master", "", "", true));
                if (err != null) { Console.WriteLine("sql open failed: " + err); return 1; }
                Db.Exec("IF DB_ID('" + dbName + "') IS NOT NULL BEGIN ALTER DATABASE " + dbName + " SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE " + dbName + " END");
                Db.Exec("CREATE DATABASE " + dbName);
                err = Db.InitSql(Db.BuildSqlConnStr(sql, dbName, "", "", true));
                if (err != null) { Console.WriteLine("sql open failed: " + err); return 1; }
                Stopwatch mw = Stopwatch.StartNew();
                List<string> rep = Migrate.Copy(mdb, delegate(string s) { });
                Check(rep.TrueForAll(l => l.Contains("✓") || l.Contains("غير موجود")), "migration to SQL Server: every table row count matches (" + mw.ElapsedMilliseconds + " ms)");
                Db.EnsureAppTables();
            }
            Console.WriteLine("mode: " + (Db.IsSql ? "SQL Server" : "Access"));
            Session.UserNo = 1; Session.UserName = "1"; Session.LoadPerms();
            Cache.LoadAll();
            try { Scenario(outDir); }
            finally
            {
                if (sql != null)
                {
                    Db.Close();
                    Db.InitSql(Db.BuildSqlConnStr(sql, "master", "", "", true));
                    Db.Exec("ALTER DATABASE " + dbName + " SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE " + dbName);
                    Console.WriteLine("test database " + dbName + " dropped");
                }
                Db.Close();
            }
            Console.WriteLine(fails == 0 ? "\nALL TESTS PASSED" : "\nFAILURES: " + fails);
            return fails;
        }

        static Doc Receipt(DateTime d, int person, int st, int item, double qty, double price)
        {
            Doc x = new Doc(); x.Kind = DocKind.Receipt; x.RType = 2; x.Date = d; x.Person = person; x.Store = st;
            x.Lines.Add(new DocLine { ItemNo = item, Qty = qty, Price = price });
            Posting.Save(x); return x;
        }

        static Doc Issue(DateTime d, int proj, int st, int item, double qty)
        {
            Doc x = new Doc(); x.Kind = DocKind.Issue; x.Date = d; x.Project = proj; x.Store = st;
            x.Lines.Add(new DocLine { ItemNo = item, Qty = qty });
            Posting.Save(x); return x;
        }

        static Doc Return(DateTime d, int proj, int st, int item, double qty)
        {
            Doc x = new Doc(); x.Kind = DocKind.Return; x.Date = d; x.Project = proj; x.Store = st;
            x.Lines.Add(new DocLine { ItemNo = item, Qty = qty });   // returns have no price on screen
            Posting.Save(x); return x;
        }

        static double LotCost(Doc d) { return Db.D(Db.Scalar("SELECT cost FROM shop_menu WHERE seq = ?", d.Id)); }

        /// <summary>Σ (qty − qty_sale) × cost over the item's lots: the value as the perpetual FIFO links see it.</summary>
        static double LinkedValue(int item) { return Db.D(Db.Scalar("SELECT SUM((qty - qty_sale) * cost) FROM shop_menu WHERE item_no = ?", item)); }

        static double IssueCost(Doc d)
        {
            return Db.D(Db.Scalar("SELECT SUM(x.qty * s.cost) FROM (sale_menu AS m INNER JOIN sale_menu_1 AS x ON x.seq_sale = m.auto_seq) INNER JOIN shop_menu AS s ON s.auto_seq = x.seq_shop WHERE m.seq = ?", d.Id));
        }

        static void Scenario(string outDir)
        {
            int pre = Posting.RecalcBalances();
            Console.WriteLine("recalc fixed (historic mismatches): " + pre);
            int maxSeq0 = Db.I(Db.Scalar("SELECT MAX(auto_seq) FROM shop_menu"));
            double sumBal0 = Db.D(Db.Scalar("SELECT SUM(item_bal) FROM item_stor")), sumLotQty0 = Db.D(Db.Scalar("SELECT SUM(qty) FROM shop_menu"));

            // ---- a new item, so its history is exactly what this test makes
            string tag = DateTime.Now.ToString("HHmmss");
            int st = Cache.DefaultStore, st2 = 0;
            foreach (DataRow r in Cache.Stores.Rows) if (Db.I(r[0]) != st) { st2 = Db.I(r[0]); break; }
            if (st == 0) st = Db.I(Cache.Stores.Rows[0][0]);
            int item = 0, pA = 0, pB = 0;
            Db.InTx(delegate(System.Data.Common.DbTransaction tx)
            {
                item = Db.Insert(tx, "INSERT INTO item (item_name, item_code, [group], [type], item_bal1, item_bal, item_price_dl1, item_bal_ret, qty_f, sale_pr, tslsl, freez) VALUES (?, ?, NULL, NULL, 0, 0, 0, 0, 0, 0, 0, ?)",
                                 "مادة اختبار التقييم " + tag, "VAL-" + tag, false);
                pA = Db.Insert(tx, "INSERT INTO project (proj_code, proj_name, client, status, start_date, notes) VALUES (?, ?, ?, ?, ?, ?)", "V" + tag + "-A", "مركز كلفة تقييم أ " + tag, "", 1, DateTime.Today, DBNull.Value);
                pB = Db.Insert(tx, "INSERT INTO project (proj_code, proj_name, client, status, start_date, notes) VALUES (?, ?, ?, ?, ?, ?)", "V" + tag + "-B", "مركز كلفة تقييم ب " + tag, "", 1, DateTime.Today, DBNull.Value);
            });
            Cache.LoadAll();
            int supplier = Db.I(Db.Scalar("SELECT TOP 1 custom_no FROM shop WHERE shop_type <> 3 ORDER BY shop_no DESC"));
            if (supplier == 0) supplier = Db.I(Cache.Persons.Rows[0]["p_no"]);
            Console.WriteLine("item " + item + " store " + st + " / " + st2 + " cost centres " + pA + ", " + pB);

            DateTime b = DateTime.Today.AddDays(-30);
            DateTime d1 = b, d2 = b.AddDays(2), d3 = b.AddDays(4), d4 = b.AddDays(6), d5 = b.AddDays(8), d6 = b.AddDays(10), d7 = b.AddDays(12), d8 = b.AddDays(14);

            // ---- receipts at different costs, a partial issue
            Doc r1 = Receipt(d1, supplier, st, item, 10, 2.0);
            Doc r2 = Receipt(d2, supplier, st, item, 10, 3.0);
            Doc i1 = Issue(d3, pA, st, item, 12);
            Check(Near(IssueCost(i1), 26), "FIFO issue of 12: 10 × 2 + 2 × 3 = 26 (" + N(IssueCost(i1)) + ")");
            Valuation.ItemValue v3 = Valuation.ItemAt(d3, item);
            Check(Near(v3.Qty, 8) && Near(v3.Value, 24) && Near(v3.UnitCost, 3), "value after the partial issue: 8 left at 3 = 24 (" + N(v3.Qty) + " / " + N(v3.Value) + ")");
            Doc r3 = Receipt(d4, supplier, st, item, 5, 4.0);
            Check(Near(Valuation.ItemAt(d4, item).Value, 44), "receipt of 5 at 4: value 8 × 3 + 5 × 4 = 44");

            // ---- an issue return comes back at the cost it went out at (26 / 12 per unit)
            Doc rt = Return(d5, pA, st, item, 2);
            double back = LotCost(rt);
            Check(Near(back, 2.1667), "issue return without a price gets the cost it went out at: " + N(back) + " (26 / 12)");
            Check(Near(Db.D(Db.Scalar("SELECT price_dl FROM shop_menu WHERE seq = ?", rt.Id)), 0), "the return's price field stays as entered (0)");
            Valuation.ItemValue v5 = Valuation.ItemAt(d5, item);
            Check(Near(v5.Qty, 15) && Near(v5.Value, 2 * 2.1667 + 20 + 24), "value after the return: 15 units = " + N(v5.Value));

            Doc i2 = Issue(d6, pA, st, item, 9);
            Check(Near(IssueCost(i2), 8 * 3 + 4), "second issue of 9: 8 × 3 + 1 × 4 = 28 (" + N(IssueCost(i2)) + ")");
            Valuation.ItemValue v6 = Valuation.ItemAt(d6, item);
            Check(Near(v6.Qty, 6) && Near(v6.Value, 16 + 2 * 2.1667), "value after the second issue: 4 × 4 + 2 × 2.1667 = " + N(v6.Value));
            Check(Near(v6.Value, LinkedValue(item)), "value = what the FIFO links leave in the lots (" + N(LinkedValue(item)) + ")");

            // ---- a receipt without a price (beginner mode) takes the last known receipt cost
            Doc rb = Receipt(d7, supplier, st, item, 3, 0);
            Check(Near(LotCost(rb), 4) && Near(Db.D(Db.Scalar("SELECT price_dl FROM shop_menu WHERE seq = ?", rb.Id)), 0), "receipt without a price gets the last known cost 4 (price stays 0)");
            double v7 = Valuation.ItemAt(d7, item).Value;
            Check(Near(v7, 16 + 2 * 2.1667 + 12), "value with the beginner receipt: " + N(v7));

            // ---- saving stays quick: a beginner receipt and an issue return of 20 real items (each line looks up a cost)
            {
                List<int> items20 = new List<int>();
                foreach (DataRow r in Db.Query("SELECT TOP 20 l.item_no FROM shop_menu AS l WHERE l.cost > 0 GROUP BY l.item_no ORDER BY l.item_no DESC").Rows) items20.Add(Db.I(r[0]));
                Doc big = new Doc(); big.Kind = DocKind.Receipt; big.RType = 2; big.Date = DateTime.Today; big.Person = supplier; big.Store = st;
                foreach (int it in items20) big.Lines.Add(new DocLine { ItemNo = it, Qty = 1 });
                Doc bigR = new Doc(); bigR.Kind = DocKind.Return; bigR.Date = DateTime.Today; bigR.Project = pB; bigR.Store = st;
                foreach (int it in items20) bigR.Lines.Add(new DocLine { ItemNo = it, Qty = 1 });
                Stopwatch bw = Stopwatch.StartNew(); Posting.Save(big); long msR = bw.ElapsedMilliseconds;
                bw.Restart(); Posting.Save(bigR); long msT = bw.ElapsedMilliseconds;
                int priced = Db.I(Db.Scalar("SELECT COUNT(*) FROM shop_menu WHERE seq = ? AND cost > 0", big.Id)) + Db.I(Db.Scalar("SELECT COUNT(*) FROM shop_menu WHERE seq = ? AND cost > 0", bigR.Id));
                Check(priced == 2 * items20.Count && msR < 5000 && msT < 10000, "20-line receipt without prices saved in " + msR + " ms, 20-line return in " + msT + " ms, every line got a cost");
                Posting.Delete(Posting.Load(DocKind.Return, bigR.Id));
                Posting.Delete(Posting.Load(DocKind.Receipt, big.Id));
            }

            // ---- value at past dates
            Check(Near(Valuation.ItemAt(b.AddDays(-1), item).Qty, 0) && Near(Valuation.ItemAt(b.AddDays(-1), item).Value, 0), "before the first receipt: nothing");
            Check(Near(Valuation.ItemAt(d1, item).Value, 20) && Near(Valuation.ItemAt(d2, item).Value, 50), "past dates: 20 after the first receipt, 50 after the second");
            Check(Near(Valuation.ItemAt(d3.AddDays(1), item).Value, 24), "past date between documents (day after the issue): 24");

            // ---- transfer: stores share the item's FIFO unit cost, total unchanged
            Doc tr = null;
            if (st2 > 0)
            {
                tr = new Doc(); tr.Kind = DocKind.Transfer; tr.Date = d8; tr.Store = st; tr.Store2 = st2;
                tr.Lines.Add(new DocLine { ItemNo = item, Qty = 4 });
                Posting.Save(tr);
                Valuation.ItemValue v8 = Valuation.ItemAt(d8, item);
                Check(Near(v8.Value, v7) && Near(v8.StoreQty(st), 5) && Near(v8.StoreQty(st2), 4) && Near(v8.StoreValue(st) + v8.StoreValue(st2), v8.Value),
                      "transfer of 4: total value unchanged, store values " + N(v8.StoreValue(st)) + " + " + N(v8.StoreValue(st2)) + " = " + N(v8.Value));
            }

            // ---- a return from a cost centre that never received the item: the FIFO unit cost of the stock on hand
            double fifoUnit = Math.Round(v7 / 9, 4);
            Doc rx = Return(d8, pB, st, item, 1);
            Check(Near(LotCost(rx), fifoUnit), "return that cannot be traced gets the FIFO unit cost on hand: " + N(LotCost(rx)) + " (" + N(fifoUnit) + ")");

            // ---- editing a return keeps a traced cost
            Doc rtE = Posting.Load(DocKind.Return, rt.Id); rtE.Lines[0].Qty = 3; Posting.Save(rtE);
            Check(Near(LotCost(rt), 2.1667), "edited return (3 units) still at the issue cost: " + N(LotCost(rt)));
            rtE = Posting.Load(DocKind.Return, rt.Id); rtE.Lines[0].Qty = 2; Posting.Save(rtE);

            // ---- reports
            ReportParams p = new ReportParams(); p.To = DateTime.Today; p.From = p.To; p.Text = "VAL-" + tag;
            ReportData vr = Reports.Get("valuation").Run(p);
            int cq = ColOf(vr, "الكمية"), cu = ColOf(vr, "متوسط كلفة الوحدة"), cv = ColOf(vr, "القيمة");
            double now = Valuation.ItemAt(DateTime.Today, item).Value;
            Check(vr.Rows.Count == 1 && cq > 0 && cu > 0 && cv > 0 && Near(Convert.ToDouble(vr.Rows[0][cq]), 10) && Near(Convert.ToDouble(vr.Rows[0][cv]), Math.Round(now, 2)),
                  "report «تقييم المخزون بتاريخ»: qty 10, value " + (vr.Rows.Count > 0 ? Convert.ToString(vr.Rows[0][cv]) : "-"));
            Check(vr.Cols[cv].Total && vr.Cols[cq].Total && vr.Rows.All(o => o.Length == vr.Cols.Count), "valuation report: value and quantity columns have totals, rows match columns");
            if (st2 > 0)
            {
                ReportParams ps = new ReportParams(); ps.To = DateTime.Today; ps.Text = p.Text; ps.Store = st2;
                ReportData vs = Reports.Get("valuation").Run(ps);
                Check(vs.Rows.Count == 1 && Near(Convert.ToDouble(vs.Rows[0][cq]), 4), "valuation report with a store filter: 4 in the second store");
            }
            ReportParams pp = new ReportParams(); pp.To = d3; pp.Text = p.Text;
            ReportData vpast = Reports.Get("valuation").Run(pp);
            Check(vpast.Rows.Count == 1 && Near(Convert.ToDouble(vpast.Rows[0][cv]), 24) && vpast.Title.Contains(UI.Date(d3)), "valuation report at a past date: 24");
            ReportData sr = Reports.Get("stock").Run(p);
            int sv = ColOf(sr, "القيمة");
            Check(sv > 0 && sr.Rows.Count == 1 && Near(Convert.ToDouble(sr.Rows[0][sv]), Math.Round(now, 2)) && sr.Summary.Any(s => s.Key.Contains("قيمة المخزون")),
                  "stock balance report has the value column and total");
            Stopwatch sw = Stopwatch.StartNew();
            ReportData all = Reports.Get("valuation").Run(new ReportParams());
            long ms = sw.ElapsedMilliseconds;
            File.WriteAllText(Path.Combine(outDir, "r_valuation.html"), Html.Render(all));
            string xl = Path.Combine(outDir, "r_valuation.xlsx");
            Excel.Write(xl, all.Title, all.Subtitle, all.Cols.Select(c => c.Header).ToList(), all.Cols.Select(c => c.Kind).ToList(), all.Rows, all.Cols.Select(c => c.Total).ToList());
            Check(File.Exists(xl) && new FileInfo(xl).Length > 1000, "valuation of all items: " + all.Rows.Count + " rows in " + ms + " ms, Excel export written");
            Console.WriteLine("  " + string.Join("  |  ", all.Summary.Select(s => s.Key + ": " + s.Value).ToArray()));
            Check(Posting.RecalcBalances() == 0, "balances match the movements (RecalcBalances() == 0)");

            // ---- repair of old lots without a cost (as left by the old program / earlier versions)
            double costRt = LotCost(rt), costRb = LotCost(rb);
            Db.Exec("UPDATE shop_menu SET cost = 0 WHERE seq = ? OR seq = ?", rt.Id, rb.Id);
            Valuation.RepairResult rr = Valuation.RepairZeroCosts(maxSeq0);
            Check(rr.Returns == 1 && rr.Receipts == 1 && Near(LotCost(rt), costRt) && Near(LotCost(rb), costRb),
                  "repair fills the zeroed return and receipt with the same costs as a save (" + rr + ")");
            Check(rr.LogFile != null && File.Exists(rr.LogFile) && File.ReadAllText(rr.LogFile).Contains("VAL-" + tag), "repair writes every changed line to a log file: " + rr.LogFile);
            Check(Db.I(Db.Scalar("SELECT COUNT(*) FROM app_log WHERE act = ?", "تعبئة كلفة الدفعات بدون كلفة")) >= 1, "repair is logged in app_log");

            // full repair at program start (first time: backup on Access), then nothing more to do
            int zeroBefore = Db.I(Db.Scalar("SELECT COUNT(*) FROM shop_menu WHERE cost = 0"));
            sw.Restart();
            Valuation.RepairResult ra = Valuation.RepairAtStart();
            Console.WriteLine("  start repair: " + (ra == null ? "nothing" : ra.ToString()) + " in " + sw.ElapsedMilliseconds + " ms; lots without cost before: " + zeroBefore);
            int zeroAfter = Db.I(Db.Scalar("SELECT COUNT(*) FROM shop_menu WHERE cost = 0"));
            Check(ra != null && zeroAfter == zeroBefore - ra.Total, "start repair filled " + (ra == null ? 0 : ra.Total) + " old lots, " + zeroAfter + " remain (items with no known cost at all)");
            Check(Db.I(Db.Scalar("SELECT COUNT(*) FROM shop_menu AS l WHERE l.cost = 0 AND EXISTS (SELECT 1 FROM shop_menu AS z WHERE z.item_no = l.item_no AND z.cost > 0)")) == 0,
                  "no lot without a cost left for an item that has a known cost");
            if (!Db.IsSql)
                Check(Directory.Exists(Settings.BackupDir) && Directory.GetFiles(Settings.BackupDir, "قبل تعبئة كلفة الدفعات*").Length == (zeroBefore > 0 ? 1 : 0), "Access: a backup was taken before the first repair");
            Check(Valuation.RepairAtStart() == null, "second start: nothing new to repair");
            Check(Near(Db.D(Db.Scalar("SELECT SUM(qty) FROM shop_menu")) - sumLotQty0, 10 + 10 + 5 + 2 + 3 + 1), "repair never changes a quantity (lot quantities = before + this test's)");
            Check(Posting.RecalcBalances() == 0, "balances still match the movements after the repair");

            // ---- clean up: delete newest first, every quantity back
            Posting.Delete(Posting.Load(DocKind.Return, rx.Id));
            if (tr != null) Posting.Delete(Posting.Load(DocKind.Transfer, tr.Id));
            Posting.Delete(Posting.Load(DocKind.Receipt, rb.Id));
            Posting.Delete(Posting.Load(DocKind.Issue, i2.Id));
            Posting.Delete(Posting.Load(DocKind.Return, rt.Id));
            Posting.Delete(Posting.Load(DocKind.Receipt, r3.Id));
            Posting.Delete(Posting.Load(DocKind.Issue, i1.Id));
            Posting.Delete(Posting.Load(DocKind.Receipt, r2.Id));
            Posting.Delete(Posting.Load(DocKind.Receipt, r1.Id));
            Cache.LoadAll();
            Check(Near(Valuation.ItemAt(DateTime.Today, item).Qty, 0) && Near(Cache.StockAll(item), 0), "after deleting the test documents the item is empty again");
            Check(Near(Db.D(Db.Scalar("SELECT SUM(item_bal) FROM item_stor")), sumBal0) && Posting.RecalcBalances() == 0, "all balances back to the start, consistent");
        }
    }
}
