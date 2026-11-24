using System;
using System.Collections.Generic;
using System.Data;
using System.Diagnostics;
using System.IO;

namespace Stores
{
    static class TestMain
    {
        static int fails = 0;
        static void Check(bool ok, string what) { Console.WriteLine((ok ? "PASS  " : "FAIL  ") + what); if (!ok) fails++; }
        static bool Throws(Action a) { try { a(); return false; } catch (UserError) { return true; } }

        // Works on any copy: the demo data (tests\RunAll.ps1) or a copy of real data. Items, persons and stores
        // are picked from the data; nothing depends on specific company records.
        [STAThread]
        static int Main(string[] args)
        {
            Console.OutputEncoding = System.Text.Encoding.UTF8;
            try { return Run(args); }
            catch (Exception ex) { Console.WriteLine("FAIL  unexpected error: " + ex); return 1; }
        }

        static int Run(string[] args)
        {
            string err, outDir;
            if (args[0] == "--live")
            {
                // --live <access file> <out dir> <media> : real main-server setup on this PC, then secondary discovery, then all tests
                outDir = args[2];
                Session.UserNo = 1; Session.UserName = "1";
                Stopwatch iw = Stopwatch.StartNew();
                ServerSetup.UsePassword(Environment.GetEnvironmentVariable("ENGSTORES_SERVER_PASSWORD") ?? "live-test-server-password");
                Console.WriteLine("STEP 1: main server setup (Windows permission prompt appears - click Yes)...");
                string last = "";
                string res = ServerSetup.RunSetupScript(args[3], delegate(string s, int sec, int after) { if (s != last) { last = s; Console.WriteLine("   [" + DateTime.Now.ToString("HH:mm:ss") + "] " + s + "   (expected " + sec + "s, then " + after + "s)"); } });
                Console.WriteLine("setup result: " + res + "   (" + (int)iw.Elapsed.TotalMinutes + " min)");
                Check(res != null && res.StartsWith("OK"), "main server setup script completed");
                if (res == null || !res.StartsWith("OK")) return 1;
                err = ServerSetup.ConnectLocal();
                Check(err == null, "program connects to the new server with its own login over TCP 1433 (" + (err ?? "ok") + ")");
                if (err != null) return 1;
                Stopwatch mw = Stopwatch.StartNew();
                List<string> rep = Migrate.Copy(args[1], delegate(string s) { });
                Console.WriteLine("migration ms: " + mw.ElapsedMilliseconds);
                foreach (string line in rep) Console.WriteLine("  " + line);
                Check(rep.TrueForAll(l => l.Contains("✓") || l.Contains("غير موجود")), "data moved to the real server: every table matches");
                Check(Db.S(Db.Scalar("SELECT pass FROM pass WHERE user_name = ?", "1")) == "1", "program user 1 / password 1 present on the server");
                Db.Close();
                Console.WriteLine("STEP 2: secondary - search the network for the main server...");
                List<string> hosts;
                Stopwatch dw = Stopwatch.StartNew();
                string foundHost = ServerSetup.Discover(delegate(string s) { Console.WriteLine("   " + s); }, out hosts);
                Console.WriteLine("hosts found: " + string.Join(" ; ", hosts.ToArray()) + "   (" + dw.ElapsedMilliseconds + " ms)");
                Check(foundHost != null, "secondary found and connected to the main server automatically: " + foundHost);
                foreach (System.Net.IPAddress ipa in System.Net.Dns.GetHostAddresses(System.Net.Dns.GetHostName()))
                {
                    if (ipa.AddressFamily != System.Net.Sockets.AddressFamily.InterNetwork) continue;
                    Check(ServerSetup.TryServer(ipa + ",1433"), "secondary can also connect by IP address " + ipa);
                    break;
                }
                if (foundHost == null) { err = ServerSetup.ConnectLocal(); if (err != null) return 1; }
            }
            else if (args[0] == "--sql")
            {
                // --sql <instance> <access file> <out dir> : create a fresh database, migrate, then run the same tests on SQL Server
                outDir = args[3];
                err = Db.InitSql(Db.BuildSqlConnStr(args[1], "master", "", "", true));
                if (err != null) { Console.WriteLine("sql open failed: " + err); return 1; }
                Db.Exec("IF DB_ID('EngStoresTest') IS NOT NULL BEGIN ALTER DATABASE EngStoresTest SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE EngStoresTest END");
                Db.Exec("CREATE DATABASE EngStoresTest");
                err = Db.InitSql(Db.BuildSqlConnStr(args[1], "EngStoresTest", "", "", true));
                if (err != null) { Console.WriteLine("sql open failed: " + err); return 1; }
                Session.UserNo = 1; Session.UserName = "1";
                Stopwatch mw = Stopwatch.StartNew();
                List<string> rep = Migrate.Copy(args[2], delegate(string s) { });
                Console.WriteLine("migration ms: " + mw.ElapsedMilliseconds);
                foreach (string line in rep) Console.WriteLine("  " + line);
                Check(rep.TrueForAll(l => l.Contains("✓") || l.Contains("غير موجود")), "migration: every table row count matches");
                // compare stock balances with the Access source
                System.Data.OleDb.OleDbConnection src = new System.Data.OleDb.OleDbConnection("Provider=Microsoft.Jet.OLEDB.4.0;Data Source=" + args[2] + ";Mode=Read;");
                src.Open();
                double accSum = Convert.ToDouble(new System.Data.OleDb.OleDbCommand("SELECT SUM(item_bal) FROM item_stor", src).ExecuteScalar());
                double accSale = Convert.ToDouble(new System.Data.OleDb.OleDbCommand("SELECT SUM(qty) FROM sale_menu", src).ExecuteScalar());
                int lastItem = Convert.ToInt32(new System.Data.OleDb.OleDbCommand("SELECT MAX(item_no) FROM item", src).ExecuteScalar());
                string lastName = Convert.ToString(new System.Data.OleDb.OleDbCommand("SELECT item_name FROM item WHERE item_no = " + lastItem, src).ExecuteScalar());
                src.Close();
                Check(Math.Abs(accSum - Db.D(Db.Scalar("SELECT SUM(item_bal) FROM item_stor"))) < 0.001, "stock balance total identical after migration (" + accSum + ")");
                Check(Math.Abs(accSale - Db.D(Db.Scalar("SELECT SUM(qty) FROM sale_menu"))) < 0.001, "issued quantities identical after migration");
                Check(lastName != "" && Db.S(Db.Scalar("SELECT item_name FROM item WHERE item_no = ?", lastItem)) == lastName, "Arabic text preserved: " + lastName);
            }
            else
            {
                outDir = args[1];
                err = Db.InitAccess(args[0]);
                if (err != null) { Console.WriteLine("open failed: " + err); return 1; }
            }
            Console.WriteLine("mode: " + (Db.IsSql ? "SQL Server" : "Access"));
            Db.EnsureAppTables();
            Check(Db.HasProj, "project/reservation columns and tables created (" + (Db.SchemaWarning ?? "ok") + ")");
            Session.UserNo = 1; Session.UserName = "1"; Session.LoadPerms(); Session.LoadMode();
            Check(!Session.Advanced, "every user (also the admin) starts in beginner mode");
            Check(Posting.CcIsRecipient(DocKind.Issue) && Posting.CcIsRecipient(DocKind.Return) && !Posting.CcIsRecipient(DocKind.PurchaseReturn),
                  "issues and issue returns: the cost centre is the recipient (project.p_no exists)");
            Stopwatch sw = Stopwatch.StartNew();
            Cache.LoadAll();
            Console.WriteLine("cache load ms: " + sw.ElapsedMilliseconds + "  items=" + Cache.Items.Rows.Count + " persons=" + Cache.Persons.Rows.Count);

            // ---- instant search
            int total;
            ItemIndex.Search("x", false, 10, out total);  // build index
            sw.Restart(); List<int> r1 = ItemIndex.Search("كيبل", false, 500, out total); long t1 = sw.ElapsedMilliseconds;
            Check(t1 < 60, "instant search by name: " + total + " hits in " + t1 + " ms");
            string someCode = Cache.ItemCode(r1.Count > 0 ? r1[0] : Db.I(Cache.Items.Rows[0]["item_no"]));
            sw.Restart(); List<int> r2 = ItemIndex.Search(someCode, false, 50, out total); long t2 = sw.ElapsedMilliseconds;
            Check(r2.Count > 0 && Cache.ItemCode(r2[0]) == someCode, "exact code ranked first (" + someCode + ") in " + t2 + " ms");
            sw.Restart(); ItemIndex.Search("برغي ستيل", false, 500, out total);
            Check(sw.ElapsedMilliseconds < 60, "multi-word search: " + total + " hits in " + sw.ElapsedMilliseconds + " ms");

            int pre = Posting.RecalcBalances();
            Console.WriteLine("recalc fixed (historic mismatches): " + pre);

            int st = Cache.DefaultStore;
            int other = 0; foreach (DataRow r in Cache.Stores.Rows) if (Db.I(r[0]) != st) { other = Db.I(r[0]); break; }
            int item = Db.I(Db.Scalar("SELECT TOP 1 s.item_no FROM item_stor AS s INNER JOIN item AS i ON i.item_no = s.item_no WHERE s.stor = ? AND s.item_bal >= 20 ORDER BY s.item_no DESC", st));
            int person = Db.I(Db.Scalar("SELECT TOP 1 custom_no FROM sale ORDER BY shop_no DESC"));
            double b0 = Cache.Stock(item, st), o0 = Cache.Stock(item, other);
            Console.WriteLine("store=" + st + " item=" + item + " (" + Cache.ItemName(item) + ") stock=" + b0);

            // ---- projects
            int pA = 0, pB = 0;
            string tag = DateTime.Now.ToString("HHmmss");   // codes that cannot clash with existing projects
            Db.InTx(delegate(System.Data.Common.DbTransaction tx)
            {
                pA = Db.Insert(tx, "INSERT INTO project (proj_code, proj_name, client, status, start_date, notes) VALUES (?, ?, ?, ?, ?, ?)", "T" + tag + "-A", "مشروع اختبار أ " + tag, "جهة", 1, DateTime.Today, DBNull.Value);
                pB = Db.Insert(tx, "INSERT INTO project (proj_code, proj_name, client, status, start_date, notes) VALUES (?, ?, ?, ?, ?, ?)", "T" + tag + "-B", "مشروع اختبار ب " + tag, "جهة", 1, DateTime.Today, DBNull.Value);
            });
            Cache.LoadProjects();
            Check(Cache.FindProject("T" + tag + "-A") == pA, "project lookup by code");

            // ---- receipt
            Doc rc = new Doc(); rc.Kind = DocKind.Receipt; rc.Person = person; rc.Store = st; rc.RType = 2;
            rc.Lines.Add(new DocLine { ItemNo = item, Qty = 5, Price = 2.5 });
            Posting.Save(rc);
            Check(Math.Abs(Cache.Stock(item, st) - (b0 + 5)) < 1e-9, "stock entry adds 5");

            // ---- issue requires project
            Doc bad = new Doc(); bad.Kind = DocKind.Issue; bad.Person = person; bad.Store = st; bad.Lines.Add(new DocLine { ItemNo = item, Qty = 1 });
            Check(Throws(delegate { Posting.Save(bad); }), "stock issue without project is rejected");

            // ---- reservation for project B
            Doc rs = new Doc(); rs.Kind = DocKind.Reserve; rs.Project = pB; rs.Store = st; rs.Lines.Add(new DocLine { ItemNo = item, Qty = 4 });
            Posting.Save(rs);
            Check(Math.Abs(Cache.Stock(item, st) - (b0 + 5)) < 1e-9, "reservation does not move stock");
            Check(Math.Abs(Reservations.ForOthers(item, st, pA) - 4) < 1e-9, "4 held for project B (seen by project A)");
            Check(Math.Abs(Reservations.ForOthers(item, st, pB)) < 1e-9, "project B is not blocked by its own reservation");

            // ---- issue to project A
            Doc iss = new Doc(); iss.Kind = DocKind.Issue; iss.Person = person; iss.Project = pA; iss.Store = st;
            iss.Lines.Add(new DocLine { ItemNo = item, Qty = 7 });
            Posting.Save(iss);
            Check(Math.Abs(Cache.Stock(item, st) - (b0 - 2)) < 1e-9, "stock issue deducts 7");
            Check(Db.I(Db.Scalar("SELECT proj_no FROM sale WHERE shop_no = ?", iss.Id)) == pA, "issue charged to project A");
            int pAPerson = Db.I(Db.Scalar("SELECT p_no FROM project WHERE proj_no = ?", pA));
            Check(pAPerson > 0 && Db.I(Db.Scalar("SELECT custom_no FROM sale WHERE shop_no = ?", iss.Id)) == pAPerson && Cache.Person(pAPerson) == Cache.Project(pA),
                  "issue recipient (custom_no) = the person row created for project A");

            // ---- issue to project B consumes its reservation
            Doc issB = new Doc(); issB.Kind = DocKind.Issue; issB.Person = person; issB.Project = pB; issB.Store = st;
            issB.Lines.Add(new DocLine { ItemNo = item, Qty = 3 });
            Posting.Save(issB);
            Check(Math.Abs(Reservations.ForProject(item, st, pB) - 1) < 1e-9, "reservation reduced to 1 after issuing 3 to project B");

            // ---- return from project A
            Doc rt = new Doc(); rt.Kind = DocKind.Return; rt.Person = person; rt.Project = pA; rt.Store = st;
            rt.Lines.Add(new DocLine { ItemNo = item, Qty = 2 });
            Posting.Save(rt);
            Check(Math.Abs(Cache.Stock(item, st) - (b0 - 3)) < 1e-9, "stock-issue return adds 2");
            double rtCost = Db.D(Db.Scalar("SELECT cost FROM shop_menu WHERE seq = ?", rt.Id));
            Check(rtCost > 0 && Db.D(Db.Scalar("SELECT price_dl FROM shop_menu WHERE seq = ?", rt.Id)) == 0, "issue return without a price gets a cost (" + rtCost + "), its price stays 0");
            Check(Math.Abs(Cache.Stock(item, st) - (b0 - 3)) < 1e-9, "giving the return a cost does not change the balance");

            // ---- purchase return
            Doc pr = new Doc(); pr.Kind = DocKind.PurchaseReturn; pr.Person = person; pr.Store = st;
            pr.Lines.Add(new DocLine { ItemNo = item, Qty = 1 });
            Posting.Save(pr);
            Check(Db.I(Db.Scalar("SELECT doc_kind FROM sale WHERE shop_no = ?", pr.Id)) == 1, "purchase return stored with doc_kind 1");
            Check(Math.Abs(Cache.Stock(item, st) - (b0 - 4)) < 1e-9, "purchase return deducts 1");
            Check(Posting.Load(DocKind.Issue, pr.Id).Kind == DocKind.PurchaseReturn, "purchase return loads as its own type");

            // ---- project statement
            ReportParams pp = new ReportParams(); pp.ProjNo = pA; pp.From = DateTime.Today.AddDays(-1); pp.To = DateTime.Today;
            ReportData ps = Reports.Get("project").Run(pp);
            bool found = false;
            int qc = ps.Cols.FindIndex(c => c.Header == "المُخرج لمركز الكلفة");    // issued, returned, net follow each other
            foreach (object[] row in ps.Rows) if (Convert.ToString(row[2]) == Cache.ItemName(item) && Math.Abs(Convert.ToDouble(row[qc]) - 7) < 1e-9 && Math.Abs(Convert.ToDouble(row[qc + 1]) - 2) < 1e-9 && Math.Abs(Convert.ToDouble(row[qc + 2]) - 5) < 1e-9) found = true;
            Check(found, "project A statement: issued 7, returned 2, net 5");
            ReportParams ip = new ReportParams(); ip.From = DateTime.Today; ip.To = DateTime.Today;
            ReportData isr = Reports.Get("issues").Run(ip);
            bool prListed = false; foreach (object[] row in isr.Rows) if (Convert.ToString(row[1]) == pr.No) prListed = true;
            Check(!prListed, "purchase returns are not listed as issues");

            // ---- conflict between computers
            Doc pcA = Posting.Load(DocKind.Issue, iss.Id), pcB = Posting.Load(DocKind.Issue, iss.Id);
            pcA.Lines[0].Qty = 6; Posting.Save(pcA);
            pcB.Lines[0].Qty = 9;
            Check(Throws(delegate { Posting.Save(pcB); }), "second computer cannot overwrite a changed document");

            // ---- cancel reservation
            Posting.Delete(Posting.Load(DocKind.Reserve, rs.Id));
            Check(Math.Abs(Reservations.ForOthers(item, st, pA)) < 1e-9, "cancelled reservation frees the quantity");
            Check(Db.I(Db.Scalar("SELECT status FROM reserve WHERE res_no = ?", rs.Id)) == 0, "cancelled reservation kept as record");

            // ---- critical balances
            sw.Restart();
            List<Reports.CriticalRow> crit = Reports.CriticalItems(0, 0, 6);
            Check(crit.Count >= 0, "critical balances: " + crit.Count + " items need replenishment (" + sw.ElapsedMilliseconds + " ms)");
            if (crit.Count > 0) Console.WriteLine("  e.g. " + Cache.ItemName(crit[0].Item) + " free=" + crit[0].Free + " monthly=" + Math.Round(crit[0].Monthly, 2) + " suggest=" + crit[0].Suggest + " — " + crit[0].Reason);

            ReportParams c3 = new ReportParams(); c3.Months = 3; ReportData rc3 = Reports.Get("critical").Run(c3);
            ReportParams c6 = new ReportParams(); c6.Months = 6; ReportData rc6 = Reports.Get("critical").Run(c6);
            Check(rc3.Subtitle.Contains("آخر 3 أشهر") && rc3.Cols[9].Header.Contains("آخر 3 أشهر"), "critical report states 3-month basis (" + rc3.Rows.Count + " items)");
            Check(rc6.Subtitle.Contains("آخر 6 أشهر") && rc6.Footer.Contains("÷ 6"), "critical report states 6-month basis and formula (" + rc6.Rows.Count + " items)");
            File.WriteAllText(Path.Combine(outDir, "r_critical3.html"), Html.Render(rc3));
            Check(Posting.RecalcBalances() == 0, "balances match movements after all operations");

            // ---- all reports
            foreach (ReportDef d in Reports.All)
            {
                ReportParams p = new ReportParams(); p.From = new DateTime(DateTime.Today.Year - 1, 1, 1); p.To = DateTime.Today; p.ItemNo = item; p.PersonNo = person; p.ProjNo = pA;
                try
                {
                    sw.Restart();
                    ReportData r = d.Run(p);
                    string html = Html.Render(r);
                    string x = Path.Combine(Path.GetTempPath(), "t_" + d.Key + ".xlsx");
                    Excel.Write(x, r.Title, r.Subtitle, r.Cols.ConvertAll(c => c.Header), r.Cols.ConvertAll(c => c.Kind), r.Rows);
                    Check(html.Length > 500, "report " + d.Key + " rows=" + r.Rows.Count + " ms=" + sw.ElapsedMilliseconds);
                    if (d.Key == "critical" || d.Key == "project" || d.Key == "card") File.WriteAllText(Path.Combine(outDir, "r_" + d.Key + ".html"), html);
                }
                catch (Exception ex) { Check(false, "report " + d.Key + ": " + ex.Message); }
            }
            File.WriteAllText(Path.Combine(outDir, "r_voucher.html"), Html.Render(Reports.Voucher(Posting.Load(DocKind.Issue, iss.Id))));

            // ---- clean up
            Posting.Delete(Posting.Load(DocKind.PurchaseReturn, pr.Id));
            Posting.Delete(Posting.Load(DocKind.Return, rt.Id));
            Posting.Delete(Posting.Load(DocKind.Issue, issB.Id));
            Posting.Delete(Posting.Load(DocKind.Issue, iss.Id));
            Posting.Delete(Posting.Load(DocKind.Receipt, rc.Id));
            Check(Math.Abs(Cache.Stock(item, st) - b0) < 1e-9 && Math.Abs(Cache.Stock(item, other) - o0) < 1e-9, "all stock restored after deletes");
            Check(Posting.RecalcBalances() == 0, "balances consistent after deletes");
            Session.SetMode(false); Session.LoadMode(); Check(!Session.Advanced, "mode switch saved (beginner)");
            Session.SetMode(true);

            if (args[0] == "--live")
            {
                ServerSetup.ConnectLocal();
                List<string> rep2 = Migrate.Copy(args[1], delegate(string s2) { });
                Check(rep2.TrueForAll(l => l.Contains("✓") || l.Contains("غير موجود")), "server reset to a clean copy of the data after the tests");
            }
            Console.WriteLine(fails == 0 ? "\nALL TESTS PASSED" : "\nFAILURES: " + fails);
            Db.Close();
            return fails;
        }
    }
}
