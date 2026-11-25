using System;
using System.Collections.Generic;
using System.Data;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading;

namespace Stores
{
    /// <summary>
    /// The real main-server installation from the package, from scratch, without internet — in Windows Sandbox only
    /// (tests\sandbox\make-sandbox.ps1 prepares and starts it). It really installs SQL Server, so it refuses to run on a
    /// normal computer.
    /// Usage: ServerInstallCheck.exe &lt;demo.mdb&gt; &lt;result file&gt;      (next to «مكونات», like the program on a copied flash drive)
    /// </summary>
    static class ServerInstallCheckMain
    {
        static int fails; static string log;
        static void Line(string s) { string t = DateTime.Now.ToString("HH:mm:ss") + "  " + s; Console.WriteLine(t); File.AppendAllText(log, t + "\r\n", Encoding.UTF8); }
        static void Check(bool ok, string what) { Line((ok ? "PASS  " : "FAIL  ") + what); if (!ok) fails++; }

        static bool Internet()
        {
            foreach (string u in new[] { "http://www.msftconnecttest.com/connecttest.txt", "https://download.microsoft.com" })
                try { HttpWebRequest r = (HttpWebRequest)WebRequest.Create(u); r.Timeout = 6000; using (r.GetResponse()) return true; }
                catch (WebException ex) { if (ex.Response != null) return true; }
                catch { }
            return false;
        }

        [STAThread]
        static int Main(string[] args)
        {
            Console.OutputEncoding = Encoding.UTF8;
            if (args.Length < 2) { Console.WriteLine("usage: ServerInstallCheck <demo.mdb> <result file>"); return 2; }
            if (!string.Equals(Environment.UserName, "WDAGUtilityAccount", StringComparison.OrdinalIgnoreCase)) { Console.WriteLine("only inside Windows Sandbox"); return 3; }
            log = args[1];
            try { Run(args[0]); }
            catch (Exception ex) { Check(false, "unexpected error: " + ex); }
            Line(fails == 0 ? "ALL PASSED" : fails + " FAILED");
            File.WriteAllText(log + ".done", fails.ToString());
            return fails == 0 ? 0 : 1;
        }

        static void Run(string demo)
        {
            Line("computer: " + Environment.OSVersion.VersionString + ", .NET " + Environment.Version + ", free " + new DriveInfo("C").AvailableFreeSpace / 1048576 + " MB");
            Check(!Internet(), "there is no internet");
            Check(Process.GetProcessesByName("sqlservr").Length == 0 && !Directory.Exists(@"C:\Program Files\Microsoft SQL Server\MSSQL16.SQLEXPRESS"), "SQL Server is not installed");
            SqlPackage p = new SqlPackage();
            List<string> found = p.Find();
            Check(found.Count > 0, "package found: " + string.Join(" ; ", found.ToArray()));
            if (found.Count == 0) return;
            Check(p.Check(found[0], delegate { }, delegate { return false; }) == null, "package checked (size, SHA-256)");

            const string pw = "Test-Pass-12345";
            ServerSetup.UsePassword(pw);
            Stopwatch sw = Stopwatch.StartNew(); string last = "";
            string res = ServerSetup.RunSetupScript(found[0], delegate(string s, int sec, int after) { if (s != last) { last = s; Line("   [" + (int)sw.Elapsed.TotalSeconds + " s] " + s); } });
            Check(res != null && res.StartsWith("OK"), "installed in " + (int)sw.Elapsed.TotalSeconds + " s: " + res);
            if (res == null || !res.StartsWith("OK")) { Summary(); return; }

            string err = ServerSetup.ConnectLocal();
            if (err != null)
                foreach (string s in new[] { "localhost,1433", "127.0.0.1,1433", "tcp:::1,1433", Environment.MachineName + ",1433", "np:localhost", @".\SQLEXPRESS" })
                    try
                    {
                        using (System.Data.SqlClient.SqlConnection c = new System.Data.SqlClient.SqlConnection(Db.BuildSqlConnStr(s, "master", "", "", true))) c.Open();
                        Line("   " + s + ": connected (Windows login)");
                    }
                    catch (Exception ex) { Line("   " + s + ": " + ex.Message); }
            Check(err == null, "connected as the program: " + (err ?? Convert.ToString(Db.Scalar("SELECT @@VERSION")).Split('\n')[0].Trim()));
            if (err != null) return;
            List<string> rep = Migrate.Copy(demo, delegate { });
            Db.EnsureAppTables(); Cache.LoadAll(); Session.UserNo = 1; Session.UserName = "اختبار"; Session.LoadPerms();
            Check(rep.Count > 5 && Db.I(Db.Scalar("SELECT COUNT(*) FROM item")) > 0, "data moved to the server: " + rep.Count + " tables, " + Db.I(Db.Scalar("SELECT COUNT(*) FROM item")) + " items");
            Posting.RecalcBalances();
            int[] st = Cache.Stores.Rows.Cast<DataRow>().Select(r => Db.I(r["stor_no"])).ToArray();
            int item = Db.I(Cache.Items.Rows[0]["item_no"]);
            Doc d = new Doc(); d.Kind = DocKind.Receipt; d.Date = DateTime.Today; d.Person = Db.I(Cache.Persons.Rows[0]["p_no"]); d.Store = st[0];
            d.Lines.Add(new DocLine { ItemNo = item, Qty = 5, Store = st[0] }); if (st.Length > 1) d.Lines.Add(new DocLine { ItemNo = item, Qty = 3, Store = st[1] });
            Posting.Save(d);
            Check(Posting.Load(DocKind.Receipt, d.Id).Lines.Count == d.Lines.Count && Posting.RecalcBalances() == 0, "receipt No. " + d.No + " saved on the server, balances right");
            Db.Close();

            Check(ServerSetup.ProbeServer("127.0.0.1,1433") == ServerSetup.Probe.Ok, "another computer's way: TCP 1433 with the server password");
            Db.Close();
            Settings.Mode = "sql"; Settings.SqlServer = "localhost,1433"; Settings.SqlDatabase = "EngStores"; Settings.SqlUser = "storesapp"; Settings.SqlPassword = ServerSetup.DerivePassword(pw); Settings.SqlTrust = true;
            err = Program.TryConnectSaved();
            Check(err == null, "the program starts again with its saved settings, without any network: " + (err ?? "connected"));
            Db.Close();
            List<string> hosts; string h = ServerSetup.Discover(null, out hosts);
            Line("   discovery in the network: " + (h ?? "none") + " (" + string.Join(", ", hosts.ToArray()) + ")   — informative: the sandbox has no network");
            Db.Close();
            ServerSetup.UsePassword("wrong-password-1");
            Check(ServerSetup.ProbeServer("localhost,1433") == ServerSetup.Probe.WrongPassword, "a wrong server password is refused");
            ServerSetup.UsePassword(pw);

            string dir = res.Split('|')[1];
            Process.Start(new ProcessStartInfo("schtasks.exe", "/run /tn \"EngStores Daily Backup\"") { UseShellExecute = false, CreateNoWindow = true }).WaitForExit();
            DateTime end = DateTime.Now.AddSeconds(180); bool bak = false;
            while (!bak && DateTime.Now < end) { Thread.Sleep(1500); try { bak = Directory.GetFiles(dir, "EngStores*.bak").Length > 0; } catch { } }
            Check(bak, "daily backup task makes a backup in " + dir);
            Check(!File.Exists(@"C:\Program Files\EngStores\setup\" + SqlPackage.FileName) && !Directory.Exists(@"C:\Program Files\EngStores\setup\extract"), "installation files are cleaned up");

            sw.Restart();
            res = ServerSetup.RunSetupScript(null, delegate { });
            Check(res != null && res.StartsWith("OK"), "pressed again on an installed server: " + (int)sw.Elapsed.TotalSeconds + " s, " + res);
            Check(ServerSetup.ConnectLocal() == null && Db.I(Db.Scalar("SELECT COUNT(*) FROM shop WHERE shop_no = ?", d.Id)) == 1, "the data is still there");
            Db.Close();
        }

        static void Summary()
        {
            try
            {
                foreach (string f in Directory.GetFiles(@"C:\Program Files\Microsoft SQL Server\160\Setup Bootstrap\Log", "Summary*.txt", SearchOption.AllDirectories).Take(1))
                    Line(File.ReadAllText(f));
            }
            catch (Exception ex) { Line("no setup summary: " + ex.Message); }
        }
    }
}
