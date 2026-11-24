using System;
using System.Collections.Generic;
using System.Data.SqlClient;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace Stores
{
    /// <summary>
    /// The server login without a secret in the code: password derivation, the least-privilege roles from
    /// setup-server.ps1, and the program's own work as that login (create tables, migrate, save a document, back up).
    /// Emulates the main server on a LocalDB instance with Windows authentication (no UAC, no real SQLEXPRESS).
    /// Usage: ServerLoginTests.exe <copy.AMR> <outdir> [(localdb)\Instance]
    /// </summary>
    static class ServerLoginTestMain
    {
        static int fails = 0;
        static void Check(bool ok, string what) { Console.WriteLine((ok ? "PASS  " : "FAIL  ") + what); if (!ok) fails++; }

        static object Admin(string host, string db, string sql)
        {
            using (SqlConnection c = new SqlConnection(Db.BuildSqlConnStr(host, db, "", "", true)))
            {
                c.Open();
                using (SqlCommand cmd = new SqlCommand(sql, c)) { cmd.CommandTimeout = 600; return cmd.ExecuteScalar(); }
            }
        }

        static string Fails(Action a)
        {
            try { a(); return null; } catch (Exception ex) { return ex.Message; }
        }

        [STAThread]
        static int Main(string[] args)
        {
            Console.OutputEncoding = System.Text.Encoding.UTF8;
            if (args.Length < 2) { Console.WriteLine("usage: ServerLoginTests.exe <copy.AMR> <outdir> [(localdb)\\Instance]"); return 2; }
            string data = args[0], outDir = args[1];
            string host = args.Length > 2 ? args[2] : @"(localdb)\EngStoresTest";
            if (!host.StartsWith("(localdb)", StringComparison.OrdinalIgnoreCase)) { Console.WriteLine("refusing: this test drops and recreates database EngStores - LocalDB only"); return 2; }
            Directory.CreateDirectory(outDir);
            string db = ServerSetup.DbName, login = ServerSetup.AppLogin;

            // ---- 1. password derivation
            Stopwatch sw = Stopwatch.StartNew();
            string p1 = ServerSetup.DerivePassword("Owner-Pass 2026");
            long ms = sw.ElapsedMilliseconds;
            Console.WriteLine("derive ms: " + ms + "   length " + p1.Length);
            Check(p1 == ServerSetup.DerivePassword("Owner-Pass 2026"), "derivation is deterministic (same on every computer)");
            Check(p1 == ServerSetup.DerivePassword("  Owner-Pass 2026 "), "surrounding spaces are ignored");
            Check(p1 != ServerSetup.DerivePassword("owner-pass 2026"), "different password -> different SQL password (case-sensitive)");
            Check(p1.Length >= 30 && p1.Any(char.IsUpper) && p1.Any(char.IsLower) && p1.Any(char.IsDigit) && p1.Any(ch => !char.IsLetterOrDigit(ch)),
                "derived SQL password is long and meets SQL Server complexity (upper, lower, digit, symbol)");
            Check(!p1.Contains("'") && !p1.Contains(";") && !p1.Contains("\""), "derived password has no quote/semicolon characters");
            Check(ms < 5000, "derivation is quick enough for the UI (" + ms + " ms)");

            // ---- 2. no secret / old key left in the program
            string script = ServerSetup.SetupScriptText();
            Check(!Regex.IsMatch(script, @"EngStores\|") && !Regex.IsMatch(script, @"HMAC", RegexOptions.IgnoreCase), "setup script has no old HMAC key");
            Check(!script.Contains("db_owner ADD"), "setup script no longer makes the login db_owner");
            Match rm = Regex.Match(script, @"\$RoleSql = @'\r?\n(.*?)\r?\n'@", RegexOptions.Singleline);
            Check(rm.Success, "role statements found in setup-server.ps1");
            string roleSql = rm.Groups[1].Value;
            Match lm = Regex.Match(script, "Invoke-Sql \"(IF SUSER_ID\\(N'\\$Login'\\)[^\"]*)\"");
            Check(lm.Success, "login statement found in setup-server.ps1");

            // the elevated start command and the setup script must parse in PowerShell
            string bootFile = Path.Combine(outDir, "bootstrap.ps1"), scriptFile = Path.Combine(outDir, "setup-server.ps1");
            File.WriteAllText(bootFile, ServerSetup.BootstrapCommand(@"C:\Users\O'Brien\AppData\Local\Temp\x\setup-server.ps1", "ABC", @"C:\t\p.tmp", @"C:\t\result.txt", @"D:\برنامج\مكونات\SQLEXPR_x64_ENU.exe", @"C:\t\progress.txt"), new System.Text.UTF8Encoding(true));
            File.WriteAllText(scriptFile, script, new System.Text.UTF8Encoding(true));
            foreach (string f in new string[] { bootFile, scriptFile })
            {
                ProcessStartInfo si = new ProcessStartInfo("powershell.exe", "-NoProfile -NonInteractive -Command \"$e=$null; $t=$null; [void][Management.Automation.Language.Parser]::ParseFile('" + f.Replace("'", "''") + "',[ref]$t,[ref]$e); if ($e.Count) { $e | % { $_.Message + ' line ' + $_.Extent.StartLineNumber }; exit 1 } else { 'parsed' }\"");
                si.UseShellExecute = false; si.RedirectStandardOutput = true; si.CreateNoWindow = true;
                using (Process p = Process.Start(si)) { string o = p.StandardOutput.ReadToEnd(); p.WaitForExit(); Check(p.ExitCode == 0, "PowerShell parses " + Path.GetFileName(f) + ": " + o.Trim()); }
            }

            // ---- 3. emulate the elevated setup (Windows authentication = administrator) on LocalDB
            Admin(host, "master", "IF DB_ID(N'" + db + "') IS NOT NULL BEGIN ALTER DATABASE [" + db + "] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE [" + db + "] END");
            Admin(host, "master", "IF SUSER_ID(N'" + login + "') IS NOT NULL DROP LOGIN [" + login + "]");
            Admin(host, "master", "CREATE DATABASE [" + db + "]");
            Func<string, string> loginSql = delegate(string serverPw)
            {
                return lm.Groups[1].Value.Replace("$Login", login).Replace("$Database", db).Replace("$pw", ServerSetup.DerivePassword(serverPw).Replace("'", "''"));
            };
            Admin(host, "master", loginSql("first-password"));
            Admin(host, db, "CREATE USER [" + login + "] FOR LOGIN [" + login + "]; ALTER ROLE db_owner ADD MEMBER [" + login + "];");  // as an older version left it
            Admin(host, db, roleSql);
            Check(Db.I(Admin(host, db, "SELECT IS_ROLEMEMBER(N'db_owner', N'" + login + "')")) == 0, "re-run removes db_owner from an older install");
            Admin(host, db, roleSql);
            Check(true, "role statements can run again (re-run of «السيرفر الرئيسي»)");
            foreach (string r in new string[] { "db_datareader", "db_datawriter", "db_ddladmin", "db_backupoperator" })
                Check(Db.I(Admin(host, db, "SELECT IS_ROLEMEMBER(N'" + r + "', N'" + login + "')")) == 1, "login is member of " + r);

            // re-run with a new password: the elevated setup resets the login password
            Admin(host, "master", loginSql("Owner-Pass 2026"));
            ServerSetup.UsePassword("first-password");
            Check(ServerSetup.ProbeServer(host) == ServerSetup.Probe.WrongPassword, "old password after a re-run -> «كلمة سر السيرفر غير صحيحة»");
            ServerSetup.UsePassword("wrong password!");
            Check(ServerSetup.ProbeServer(host) == ServerSetup.Probe.WrongPassword, "wrong server password is told apart from an unreachable server");
            ServerSetup.UsePassword("Owner-Pass 2026");
            Check(ServerSetup.ProbeServer(@"(localdb)\NoSuchInstance_EngStores") == ServerSetup.Probe.Unreachable, "unknown server -> unreachable");
            Check(ServerSetup.ProbeServer(host) == ServerSetup.Probe.Unreachable, "right password on an empty database -> not a usable server yet");

            // ---- 4. the program's work as the least-privilege login
            string err = Db.InitSql(Db.BuildSqlConnStr(host, db, login, ServerSetup.DerivePassword("Owner-Pass 2026"), true));
            Check(err == null, "program connects as " + login + " (" + (err ?? "ok") + ")");
            if (err != null) return Done();
            Check(Db.S(Db.Scalar("SELECT SUSER_SNAME()")) == login, "connected as the SQL login, not Windows");
            Check(Db.I(Db.Scalar("SELECT IS_ROLEMEMBER('db_owner')")) == 0 && Db.I(Db.Scalar("SELECT IS_SRVROLEMEMBER('sysadmin')")) == 0, "login is neither db_owner nor sysadmin");
            Check(Fails(delegate { Db.Exec("CREATE DATABASE EngStores_probe_x"); }) != null, "login cannot create databases");
            Check(Fails(delegate { Db.Exec("ALTER LOGIN [" + login + "] WITH PASSWORD = N'Xx1!xxxxxxxxxxxxxxxx', CHECK_POLICY = OFF"); }) != null, "login cannot change its own password without the old one (no ALTER ANY LOGIN)");

            Session.UserNo = 1; Session.UserName = "1";
            sw.Restart();
            List<string> rep = null;
            string merr = Fails(delegate { rep = Migrate.Copy(data, delegate(string s) { }); });
            Check(merr == null, "Migrate.Copy as the login (drop/create tables, bulk copy with KeepIdentity, indexes): " + (merr ?? sw.ElapsedMilliseconds + " ms"));
            if (rep != null) { foreach (string line in rep) Console.WriteLine("  " + line); Check(rep.TrueForAll(l => l.Contains("✓") || l.Contains("غير موجود")), "every table copied with matching row count"); }

            Db.Exec("DROP TABLE app_online");
            Db.Exec("DECLARE @c sysname = (SELECT name FROM sys.default_constraints WHERE parent_object_id = OBJECT_ID('shop') AND COL_NAME(parent_object_id, parent_column_id) = 'proj_no'); " +
                    "IF @c IS NOT NULL EXEC('ALTER TABLE shop DROP CONSTRAINT [' + @c + ']'); " +
                    "IF EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'ix_shop_proj' AND object_id = OBJECT_ID('shop')) DROP INDEX ix_shop_proj ON shop; " +
                    "ALTER TABLE shop DROP COLUMN proj_no;");
            Db.EnsureAppTables();
            Check(Db.SchemaWarning == null, "Db.EnsureAppTables as the login (" + (Db.SchemaWarning ?? "ok") + ")");
            Check(Db.TableExists("app_online") && Db.ColumnExists("shop", "proj_no") && Db.HasProj, "missing table and column re-created by the login");

            Check(ServerSetup.ProbeServer(host) == ServerSetup.Probe.Ok, "secondary: right password -> connected");
            Check(ServerSetup.TryServer(host), "TryServer with the derived password");

            // document save (Db.InTx + sp_getapplock) and delete
            Session.LoadPerms(); Session.LoadMode();
            Cache.LoadAll();
            int st = Cache.DefaultStore;
            int item = Db.I(Db.Scalar("SELECT TOP 1 s.item_no FROM item_stor AS s INNER JOIN item AS i ON i.item_no = s.item_no WHERE s.stor = ? ORDER BY s.item_no DESC", st));
            int person = Db.I(Db.Scalar("SELECT TOP 1 custom_no FROM sale ORDER BY shop_no DESC"));
            double b0 = Cache.Stock(item, st);
            Doc rc = new Doc(); rc.Kind = DocKind.Receipt; rc.Person = person; rc.Store = st; rc.RType = 2;
            rc.Lines.Add(new DocLine { ItemNo = item, Qty = 5, Price = 2.5 });
            string serr = Fails(delegate { Posting.Save(rc); });
            Check(serr == null && Math.Abs(Cache.Stock(item, st) - (b0 + 5)) < 1e-9, "document saved as the login (stock +5) " + (serr ?? ""));
            if (serr == null)
            {
                string derr = Fails(delegate { Posting.Delete(Posting.Load(DocKind.Receipt, rc.Id)); });
                Check(derr == null && Math.Abs(Cache.Stock(item, st) - b0) < 1e-9, "document deleted as the login (stock restored) " + (derr ?? ""));
            }
            Db.Log(null, "اختبار", "ServerLoginTests");
            Check(Db.I(Db.Scalar("SELECT COUNT(*) FROM app_log WHERE details = ?", "ServerLoginTests")) == 1, "log written");

            // year-end closing backup: the same statement as YearClose.Close (relative file -> the server's backup folder)
            string bakName = db + "_closing_test_" + DateTime.Now.ToString("yyyyMMdd_HHmmss") + ".bak";
            string berr = Fails(delegate { Db.Exec("BACKUP DATABASE [" + db + "] TO DISK = N'" + bakName + "' WITH COPY_ONLY, INIT, CHECKSUM"); });
            Check(berr == null, "BACKUP DATABASE as the login (db_backupoperator) " + (berr ?? ""));
            string bakFile = Path.Combine(outDir, db + "_test.bak");
            if (File.Exists(bakFile)) File.Delete(bakFile);
            berr = Fails(delegate { Db.Exec("BACKUP DATABASE [" + db + "] TO DISK = N'" + bakFile.Replace("'", "''") + "' WITH COPY_ONLY, INIT, CHECKSUM"); });
            Check(berr == null && File.Exists(bakFile) && new FileInfo(bakFile).Length > 0, "backup file written: " + bakFile + " " + (berr ?? ""));
            return Done(host, db, login, bakName);
        }

        static int Done() { return Done(null, null, null, null); }

        static int Done(string host, string db, string login, string bakName)
        {
            Db.Close();
            SqlConnection.ClearAllPools();
            if (host != null)
            {
                try
                {
                    string dir = Convert.ToString(Admin(host, "master", "SELECT CAST(SERVERPROPERTY('InstanceDefaultBackupPath') AS nvarchar(4000))"));
                    string f = Path.Combine(dir, bakName);
                    if (bakName != null && File.Exists(f)) File.Delete(f);
                    Admin(host, "master", "ALTER DATABASE [" + db + "] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE [" + db + "]");
                    Admin(host, "master", "DROP LOGIN [" + login + "]");
                    Console.WriteLine("cleaned up: database " + db + ", login " + login);
                }
                catch (Exception ex) { Console.WriteLine("cleanup: " + ex.Message); }
            }
            Console.WriteLine(fails == 0 ? "\nALL TESTS PASSED" : "\nFAILURES: " + fails);
            return fails;
        }
    }
}
