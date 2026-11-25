using System.Collections.Generic;
using System.Data.Sql;
using System.Data;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Windows.Forms;
using System;

namespace Stores
{
    /// <summary>
    /// One-click network setup:
    ///  «السيرفر الرئيسي»  — installs SQL Server Express on this computer, moves the data into it.
    ///  «سيرفر ثانوي»      — finds the main server on the network and connects to it.
    /// No secret is kept in the code (the repository is public): the owner chooses a server password on the
    /// main server and types it once on every other computer. The SQL login password is derived from it with
    /// PBKDF2 and stored on each computer encrypted for its Windows user (DPAPI, Settings.SqlPassword).
    /// </summary>
    static class ServerSetup
    {
        public const string AppLogin = "storesapp";
        public const string DbName = "EngStores";
        public const int MinPasswordLength = 8;
        const string Salt = "EngStores.v2|storesapp";
        const int Iterations = 150000;

        // derived SQL login password for the current setup / connect (never stored in the code)
        static string appPassword;

        /// <summary>SQL login password for the owner's server password: PBKDF2 (HMAC-SHA1, 150,000 rounds, fixed salt;
        /// SHA-1 keeps it working on PCs without .NET 4.7.2),
        /// 24 bytes as URL-safe base64 behind a prefix that gives upper, lower, digit and symbol (SQL Server complexity).</summary>
        public static string DerivePassword(string serverPassword)
        {
            if (serverPassword == null) throw new ArgumentNullException("serverPassword");
            using (Rfc2898DeriveBytes k = new Rfc2898DeriveBytes(Encoding.UTF8.GetBytes(serverPassword.Trim()), Encoding.UTF8.GetBytes(Salt), Iterations))
                return "Es7#" + Convert.ToBase64String(k.GetBytes(24)).Replace('+', '-').Replace('/', '_');
        }

        /// <summary>Sets the server password for the following calls (RunSetupScript, ConnectLocal, Discover, TryServer).</summary>
        public static void UsePassword(string serverPassword) { appPassword = DerivePassword(serverPassword); }

        static string AppPassword()
        {
            if (appPassword == null) throw new InvalidOperationException("The server password has not been entered.");
            return appPassword;
        }

        static string ConnStr(string server) { return Db.BuildSqlConnStr(server, DbName, AppLogin, AppPassword(), true); }

        static void SaveSettings(string server)
        {
            Settings.Mode = "sql";
            Settings.SqlServer = server;
            Settings.SqlDatabase = DbName;
            Settings.SqlUser = AppLogin;
            Settings.SqlPassword = AppPassword();
            Settings.SqlTrust = true;
            Settings.Save();
        }

        static void Pump(Func<bool> done)
        {
            while (!done()) { Application.DoEvents(); Thread.Sleep(100); }
        }

        // ------------------------------------------------------------ main server
        static bool running;

        public static bool RunMain(IWin32Window owner)
        {
            if (running) return false;   // (messages are pumped while it works: a second click on the button)
            running = true;
            try { return SetUpMain(owner); } finally { running = false; }
        }

        /// <summary>One-file setup: everything without questions. Another main server in the network: offered instead. The
        /// data file in use (or a new empty one) goes onto the server; the server password is made here and shown at the end.</summary>
        public static void RunAuto()
        {
            if (running) return;
            if (Settings.Mode == "sql" && Program.TryConnectSaved() == null) return;      // already works
            bool server = CanBeServer;      // 32-bit Windows: everything except being the server itself
            if (IsSqlExpressInstalled()) { RunMain(null); return; }      // set up before: its password is asked, never changed unseen
            List<string> hosts;
            using (WaitForm wf = new WaitForm("جاري البحث عن السيرفر الرئيسي في الشبكة", "يرجى الانتظار… المدة المتوقعة أقل من نصف دقيقة.", null))
            {
                wf.Show(); wf.Step("البحث في الشبكة…", 20, 5);
                try { hosts = FindHosts(delegate(string s) { wf.Detail(s); }); } catch { hosts = new List<string>(); }
                wf.Close();
            }
            hosts.RemoveAll(h => h.StartsWith(Environment.MachineName + ",", StringComparison.OrdinalIgnoreCase));
            if (hosts.Count > 0 && UI.Confirm("وُجد سيرفر رئيسي في الشبكة: " + string.Join("، ", hosts.ConvertAll(h => h.Replace(",1433", "")).ToArray()) +
                                              "\n\nهل تريد ربط هذا الحاسوب به؟" + (server ? "\n(«لا»: يصبح هذا الحاسوب سيرفرا رئيسيا جديدا)" : "\n(«لا»: يعمل هذا الحاسوب ببيانات محلية)")))
            { if (RunSecondary(null)) return; }
            bool isNew = false;
            if (string.IsNullOrEmpty(Settings.DataPath) || !File.Exists(Settings.DataPath))
            {
                // the data an earlier installation worked with (remembered by the cleaner); otherwise one question: an empty
                // server while the company's data lies in some file would look like lost data
                string last = InstallFind.RegValue(InstallEnv.Current.RegKey, "LastData");
                if (last.Length > 0 && File.Exists(last)) { Settings.Mode = "access"; Settings.DataPath = last; Settings.Save(); }
                else if (UI.Confirm("هل لديك بيانات سابقة على هذا الحاسوب (ملف AMR)؟\n\nنعم: اختيار الملف ونقله إلى السيرفر\nلا: البدء ببيانات جديدة فارغة"))
                {
                    string f = Program.AskDataFile();
                    if (f == null) return;
                    Settings.Mode = "access"; Settings.DataPath = f; Settings.Save();
                }
            }
            if (string.IsNullOrEmpty(Settings.DataPath) || !File.Exists(Settings.DataPath))
            {
                string dir = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), path = Path.Combine(dir, "بيانات المخازن.AMR");
                for (int i = 2; File.Exists(path); i++) path = Path.Combine(dir, "بيانات المخازن " + i + ".AMR");
                string err = NewDatabase.Create(path);
                if (err != null) { UI.Warn("تعذر إنشاء البيانات:\n" + err); return; }
                Settings.Mode = "access"; Settings.DataPath = path; Settings.Save();
                isNew = true;
            }
            if (!server)
            {
                UI.Info("ويندوز في هذا الحاسوب 32 بت: البرنامج جاهز ويعمل بملف بيانات على هذا الحاسوب.\nالسيرفر نفسه يحتاج حاسوبا بويندوز 64 بت، ويمكن ربط هذا الحاسوب به لاحقا من «سيرفر ثانوي»." +
                        (isNew ? "\n\nالدخول أول مرة: المستخدم 1 وكلمة المرور 1" : ""));
                return;
            }
            running = true;
            try { SetUpMain(null, NewPassword(), isNew); } finally { running = false; }
        }

        /// <summary>SQL Server 2022 exists for 64-bit Windows only. (Test32: tests.)</summary>
        internal static bool Test32;
        internal static bool CanBeServer { get { return Environment.Is64BitOperatingSystem && !Test32; } }

        /// <summary>«K7MP-4XQ9-2TRW»: easy to read and to type, about 58 random bits.</summary>
        internal static string NewPassword()
        {
            const string chars = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789";      // 32 without the ones that look alike
            byte[] b = new byte[12];
            using (RandomNumberGenerator r = RandomNumberGenerator.Create()) r.GetBytes(b);
            StringBuilder s = new StringBuilder();
            for (int i = 0; i < b.Length; i++) { if (i > 0 && i % 4 == 0) s.Append('-'); s.Append(chars[b[i] & 31]); }
            return s.ToString();
        }

        static bool SetUpMain(IWin32Window owner) { return SetUpMain(owner, null, false); }

        static bool SetUpMain(IWin32Window owner, string autoPassword, bool newData)
        {
            string pw = autoPassword ?? ServerPasswordDialog.AskNew(owner);
            if (pw == null) return false;
            UI.WaitCursor(true);
            try { UsePassword(pw); } finally { UI.WaitCursor(false); }
            pw = null;

            // the data to put on the server: the file the program already uses; ask only when there is none
            string data = !string.IsNullOrEmpty(Settings.DataPath) && File.Exists(Settings.DataPath) ? Settings.DataPath : null;
            if (data == null)
            {
                using (OpenFileDialog d = new OpenFileDialog())
                {
                    d.Title = "اختر ملف البيانات (الداتا) لنقله إلى السيرفر";
                    d.Filter = "ملفات البيانات (*.AMR;*.mdb)|*.AMR;*.mdb|كل الملفات|*.*";
                    if (d.ShowDialog(owner) != DialogResult.OK) return false;
                    data = d.FileName;
                }
            }

            // check this computer, then find the package, before installing anything (and before the Windows permission prompt)
            bool sqlInstalled = IsSqlExpressInstalled();
            string problem = CheckComputer(sqlInstalled);
            if (problem != null) { UI.Warn("لا يمكن تجهيز السيرفر الرئيسي على هذا الحاسوب:\n\n" + problem); return false; }
            string media = null;
            if (!sqlInstalled)
            {
                media = GetPackage(owner);
                if (media == null) return false;
            }

            WaitForm wf = new WaitForm(sqlInstalled ? "جاري إعداد السيرفر الرئيسي" : "جاري تنصيب السيرفر الرئيسي",
                "⚠ لا تغلق البرنامج ولا تطفئ الحاسوب حتى تظهر رسالة الانتهاء.\n" + (sqlInstalled ? "المدة المتوقعة: 1–3 دقائق." : "المدة المتوقعة: 4–10 دقائق حسب سرعة الحاسوب (مرة واحدة فقط)."), null);
            wf.ShowOver(owner);
            wf.Step("بدء التنصيب… اضغط «نعم» إذا ظهرت نافذة صلاحيات ويندوز.", 15, sqlInstalled ? 80 : 320);
            try
            {
                // 1. install / configure SQL Server (elevated PowerShell script embedded in the program)
                string res = RunSetupScript(media, delegate(string s, int sec, int after) { wf.Step(s, sec, after); });
                if (res == null)
                {
                    UI.Warn("يجب الضغط «نعم» في نافذة صلاحيات ويندوز لإكمال التنصيب.\n\n" +
                            "لم يتغير شيء في هذا الحاسوب. اضغط «السيرفر الرئيسي» مرة ثانية.\n" +
                            "إذا طلبت النافذة كلمة سر: فهي كلمة سر مدير هذا الحاسوب (ويندوز)، وليست كلمة سر السيرفر.");
                    return false;
                }
                if (!res.StartsWith("OK"))
                {
                    UI.Warn("لم يكتمل تجهيز السيرفر:\n\n" + res.Substring(Math.Min(4, res.Length)) + "\n\n" +
                            "بياناتك لم تتغير. بعد حل السبب اضغط «السيرفر الرئيسي» مرة ثانية.");
                    return false;
                }
                string[] okParts = res.Split('|');   // "OK|<backup folder>[|reboot]"
                string backupDir = okParts.Length > 1 ? okParts[1] : "";
                bool reboot = okParts.Length > 2 && okParts[2] == "reboot";

                // 2. connect to it
                wf.Step("جاري الاتصال بالسيرفر…", 5, 25);
                string err = Db.InitSql(ConnStr("localhost,1433"));
                if (err != null)
                {
                    UI.Warn("تم التنصيب لكن تعذر الاتصال بالسيرفر:\n" + err + "\n\n" +
                            "أعد تشغيل الحاسوب ثم اضغط «السيرفر الرئيسي» مرة ثانية (لا يُعاد التنصيب، يُكمل فقط).");
                    return false;
                }

                // 3. move the data (unless the server already has data)
                bool hasData = Db.TableExists("item") && Db.I(Db.Scalar("SELECT COUNT(*) FROM item")) > 0;
                bool import = !hasData;
                if (hasData && autoPassword == null)
                    import = UI.Confirm("السيرفر فيه بيانات مسبقا.\nهل تريد استبدالها بالملف المختار؟\n\n(اختر «لا» للإبقاء على بيانات السيرفر الحالية)");
                List<string> report = null;
                if (import && hasData)
                {
                    // what is replaced stays recoverable: without this backup nothing is replaced (the daily clean-up leaves it)
                    wf.Step("جاري حفظ نسخة من بيانات السيرفر الحالية…", 10, 25);
                    Db.Exec("BACKUP DATABASE [" + DbName + "] TO DISK = N'" + DbName + "_before_import_" + DateTime.Now.ToString("yyyyMMdd_HHmmss", System.Globalization.CultureInfo.InvariantCulture) + ".bak' WITH COPY_ONLY, INIT, CHECKSUM");
                }
                if (import)
                {
                    wf.Step("جاري نقل البيانات إلى السيرفر…", 25, 0);
                    report = Migrate.Copy(data, delegate(string t) { wf.Detail("جاري نقل البيانات إلى السيرفر…\n" + t); });
                }
                Db.EnsureAppTables();
                SaveSettings("localhost,1433");
                Db.Log(null, "تجهيز السيرفر الرئيسي", Environment.MachineName);
                wf.Close();
                // a copy kept only for this installation (program on the network / in a read-only folder) is not needed any more
                if (media != null && string.Equals(Path.GetDirectoryName(media), SqlPackage.UserDir, StringComparison.OrdinalIgnoreCase))
                    try { File.Delete(media); } catch { }

                if (autoPassword != null)
                {
                    using (ServerReadyDialog d = new ServerReadyDialog(autoPassword, newData, reboot)) d.ShowDialog(owner);
                    return true;
                }
                UI.Info("✓ هذا الحاسوب أصبح السيرفر الرئيسي.\n\n" +
                        (reboot ? "⚠ أعد تشغيل هذا الحاسوب الآن (طلبه تنصيب SQL Server)، ثم افتح البرنامج.\n\n" : "") +
                        "اسمه على الشبكة: " + Environment.MachineName + "\n" + LocalIPs() + "\n\n" +
                        (report != null ? "تم نقل البيانات (" + report.Count + " جدول) ✓\n" : "") +
                        "النسخة الاحتياطية اليومية في: " + backupDir + "\n\n" +
                        "على كل حاسوب آخر: افتح البرنامج ← «إعدادات الاتصال والسيرفر» ← «سيرفر ثانوي»،\n" +
                        "واكتب كلمة سر السيرفر التي اخترتها الآن (مرة واحدة لكل حاسوب). احتفظ بها في مكان آمن.\n" +
                        (hasData ? "إذا غيّرت كلمة السر عن السابقة: أعد «سيرفر ثانوي» على كل حاسوب آخر.\n" : "") +
                        "مهم: أبقِ هذا الحاسوب شغالا (أوقف وضع السكون)، ولا تستعمل البرنامج القديم بعد الآن.");
                return true;
            }
            catch (Exception ex) { UI.Error(ex); return false; }
            finally { if (!wf.IsDisposed) wf.Close(); }
        }

        // ------------------------------------------------------------ the SQL Server package
        /// <summary>A usable package on this computer's own disk: found by itself, picked or downloaded by the user (asked
        /// every time; nothing is downloaded otherwise). null = the user cancelled.</summary>
        static string GetPackage(IWin32Window owner)
        {
            SqlPackage p = new SqlPackage();
            List<string> found = null;
            UI.WaitCursor(true);
            try { found = InBackground(delegate { return p.Find(); }, null); } finally { UI.WaitCursor(false); }
            string note = null;
            foreach (string f in found)
            {
                string ready;
                note = TakePackage(owner, p, f, out ready);
                if (note == null) return ready;
                if (note == SqlPackage.Cancelled) break;
            }
            string dest = SqlPackage.DownloadPath();
            while (true)
            {
                if (note == SqlPackage.Cancelled) note = null;
                using (SqlPackageDialog d = new SqlPackageDialog(note, SqlPackage.PartSize(dest), p.Size))
                {
                    d.ShowDialog(owner);
                    if (d.Result == SqlPackageDialog.Choice.Cancel) return null;
                    if (d.Result == SqlPackageDialog.Choice.File)
                    {
                        string ready;
                        note = TakePackage(owner, p, d.Picked, out ready);
                        if (note == null) return ready;
                    }
                    else
                    {
                        note = DownloadPackage(owner, p, dest);
                        if (note == null) return dest;
                    }
                }
            }
        }

        /// <summary>Checks a package before the Windows permission prompt; one on the network is copied to this computer first
        /// (the elevated setup does not see mapped network drives). null = usable (ready = the file to install from),
        /// SqlPackage.Cancelled, or why not.</summary>
        static string TakePackage(IWin32Window owner, SqlPackage p, string file, out string ready)
        {
            ready = file;
            string local = SqlPackage.IsLocal(file) ? null : Path.Combine(SqlPackage.UserDir, SqlPackage.FileName);
            WaitForm wf = new WaitForm("جاري فحص حزمة SQL Server", "التأكد أن الملف كامل وسليم قبل التنصيب (أقل من دقيقة عادة).", "إلغاء الفحص");
            wf.ShowOver(owner);
            try
            {
                Share share = new Share();
                string text = SqlPackage.Ltr(file, 60);
                Func<string> work = delegate
                {
                    if (local != null)
                    {
                        string e = p.Copy(file, local, share.Set, delegate { return wf.CancelPressed; });
                        if (e != null) return e;
                    }
                    return p.Check(local ?? file, share.Set, delegate { return wf.CancelPressed; });
                };
                string res = InBackground(work, delegate { wf.Progress((local != null ? "نسخ الملف من الشبكة وفحصه:" : "فحص الملف:") + "\n" + text, share.Get(), -1); });
                if (res == null && local != null) ready = local;
                return res;
            }
            finally { wf.Close(); }
        }

        /// <summary>Downloads the package in a window with percent, speed, remaining time and «إلغاء التنزيل».</summary>
        static string DownloadPackage(IWin32Window owner, SqlPackage p, string dest)
        {
            WaitForm wf = new WaitForm("جاري تنزيل SQL Server من الإنترنت",
                "إذا انقطع الاتصال يُكمل التنزيل من حيث توقف تلقائيا.\nبعد التنزيل يبدأ التنصيب: 4–10 دقائق حسب سرعة الحاسوب.", "إلغاء التنزيل");
            wf.ShowOver(owner);
            try
            {
                object gate = new object();
                long done = SqlPackage.PartSize(dest); double speed = 0; string error = null; int failures = 0;
                Func<string> work = delegate
                {
                    return p.Download(dest, delegate(long d, double s, string e, int f) { lock (gate) { done = d; speed = s; error = e; failures = f; } },
                                      delegate { return wf.CancelPressed; });
                };
                return InBackground(work, delegate { lock (gate) wf.Download(done, p.Size, speed, error, failures, p.MaxFailures); });
            }
            finally { wf.Close(); }
        }

        /// <summary>Share of a work done on another thread.</summary>
        class Share
        {
            double v;
            public void Set(long done, long total) { lock (this) v = total <= 0 ? 0 : (double)done / total; }
            public double Get() { lock (this) return v; }
        }

        /// <summary>Runs work on a background thread and keeps the windows alive meanwhile (tick = about 10 times a second).</summary>
        static T InBackground<T>(Func<T> work, Action tick) where T : class
        {
            T res = null; Exception error = null;
            Thread t = new Thread(delegate() { try { res = work(); } catch (Exception ex) { error = ex; } });
            t.IsBackground = true; t.Start();
            Pump(delegate { if (tick != null) tick(); return !t.IsAlive; });
            if (error != null) throw new InvalidOperationException(error.Message, error);
            return res;
        }

        /// <summary>Text of the setup script embedded in the program.</summary>
        public static string SetupScriptText()
        {
            using (Stream s = typeof(ServerSetup).Assembly.GetManifestResourceStream("setup-server.ps1"))
            using (StreamReader r = new StreamReader(s, Encoding.UTF8))
                return r.ReadToEnd();
        }

        static string PsQuote(string s) { return "'" + (s ?? "").Replace("'", "''") + "'"; }

        /// <summary>The elevated start: a small command (passed on the command line, so nobody can change it) that
        /// copies the setup script into a folder only Administrators/SYSTEM can write (Program Files\EngStores\setup),
        /// checks the copy against the SHA-256 the program computed, and only then runs that copy.</summary>
        public static string BootstrapCommand(string script, string hash, string pwFile, string result, string media, string prog)
        {
            // one plain, readable line (no encoded command): what is run as administrator can be read in the Windows prompt
            return
                "$ErrorActionPreference = 'Stop'; $res = " + PsQuote(result) + "; " +
                "try { " +
                "$pf = $env:ProgramW6432; if (-not $pf) { $pf = $env:ProgramFiles }; " +
                "$dir = Join-Path $pf 'EngStores\\setup'; " +
                "New-Item -ItemType Directory -Force -Path $dir | Out-Null; " +
                "& icacls.exe $dir /setowner '*S-1-5-32-544' /T /C /Q | Out-Null; " +
                "& icacls.exe $dir /reset /T /C /Q | Out-Null; " +
                "& icacls.exe $dir /inheritance:r /grant:r '*S-1-5-32-544:(OI)(CI)F' '*S-1-5-18:(OI)(CI)F' /C /Q | Out-Null; " +
                "if ($LASTEXITCODE -ne 0) { throw 'icacls failed' }; " +
                "$dst = Join-Path $dir 'setup-server.ps1'; " +
                "Copy-Item -LiteralPath " + PsQuote(script) + " -Destination $dst -Force; " +
                "if ((Get-FileHash -LiteralPath $dst -Algorithm SHA256).Hash -ne '" + hash + "') { throw 'setup script changed on disk' }; " +
                "& $dst -PasswordFile " + PsQuote(pwFile) + " -ResultFile $res -MediaFile " + PsQuote(media) + " -ProgressFile " + PsQuote(prog) + "; " +
                "exit $LASTEXITCODE " +
                "} catch { [IO.File]::WriteAllText($res, ('ERR|' + $_.Exception.Message), (New-Object Text.UTF8Encoding $false)); exit 1 }";
        }

        /// <summary>Runs the embedded setup script elevated and hidden. media: the checked package (null when SQL Server is
        /// installed already). Returns "OK|backupDir" (or "OK|backupDir|reboot"), "ERR|message",
        /// or null when the Windows permission prompt was refused.</summary>
        public static string RunSetupScript(string media, Action<string, int, int> progress)
        {
            // hand-over files in this Windows user's own temp folder (other users cannot read or plant files there)
            string work = Path.Combine(Path.GetTempPath(), "EngStoresSetup_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(work);
            string script = Path.Combine(work, "setup-server.ps1");
            string pwFile = Path.Combine(work, "p.tmp");
            string result = Path.Combine(work, "result.txt");
            string prog = Path.Combine(work, "progress.txt");
            byte[] scriptBytes = new UTF8Encoding(true).GetPreamble();
            byte[] body = Encoding.UTF8.GetBytes(SetupScriptText());
            Array.Resize(ref scriptBytes, scriptBytes.Length + body.Length);
            Array.Copy(body, 0, scriptBytes, scriptBytes.Length - body.Length, body.Length);
            File.WriteAllBytes(script, scriptBytes);
            string hash;
            using (SHA256 sha = SHA256.Create()) hash = BitConverter.ToString(sha.ComputeHash(scriptBytes)).Replace("-", "");
            File.WriteAllText(pwFile, AppPassword());
            try
            {
                Process p;
                try
                {
                    // The program is 32-bit, so this starts the 32-bit PowerShell; the script restarts itself in
                    // 64-bit PowerShell (needed to see SQL Server's registry). A "Sysnative" path cannot be used
                    // here: the elevated start is done by a 64-bit Windows service that does not know that path.
                    string boot = BootstrapCommand(script, hash, pwFile, result, media, prog);
                    ProcessStartInfo si = new ProcessStartInfo("powershell.exe",
                        "-NoProfile -ExecutionPolicy Bypass -Command \"" + boot + "\"");
                    si.Verb = "runas"; si.UseShellExecute = true; si.WindowStyle = ProcessWindowStyle.Hidden;
                    p = Process.Start(si);
                }
                catch (System.ComponentModel.Win32Exception ex)
                {
                    if (ex.NativeErrorCode == 1223) return null;   // the user answered "No" in the Windows permission prompt
                    return "ERR|تعذر بدء التنصيب: " + ex.Message;
                }
                string last = "";
                Pump(delegate
                {
                    try { if (File.Exists(prog)) last = File.ReadAllText(prog, Encoding.UTF8); } catch { }
                    // progress file: "<step seconds>|<seconds after>|<text>"
                    string[] parts = last.Split(new char[] { '|' }, 3);
                    int sec, after;
                    if (parts.Length == 3 && int.TryParse(parts[0], out sec) && int.TryParse(parts[1], out after))
                    { if (progress != null && parts[2].Length > 0) progress(parts[2], sec, after); }
                    else if (progress != null && last.Length > 0) progress(last, 0, 0);
                    return p.HasExited;
                });
                return File.Exists(result) ? File.ReadAllText(result, Encoding.UTF8) : "ERR|انتهى التنصيب بدون نتيجة (ربما أُغلقت نافذته).";
            }
            finally
            {
                try { File.Delete(pwFile); } catch { }
                try { Directory.Delete(work, true); } catch { }
            }
        }

        /// <summary>Connects as the program to a server on this computer (used after the main server setup).</summary>
        public static string ConnectLocal() { return Db.InitSql(ConnStr("localhost,1433")); }

        // ------------------------------------------------------------ computer check
        static Microsoft.Win32.RegistryKey Hklm64(string path)
        {
            Microsoft.Win32.RegistryKey b = Microsoft.Win32.RegistryKey.OpenBaseKey(Microsoft.Win32.RegistryHive.LocalMachine,
                Environment.Is64BitOperatingSystem ? Microsoft.Win32.RegistryView.Registry64 : Microsoft.Win32.RegistryView.Default);
            return b.OpenSubKey(path);
        }

        /// <summary>The same test as setup-server.ps1: the SQLEXPRESS service exists.</summary>
        static bool IsSqlExpressInstalled()
        {
            try
            {
                using (Microsoft.Win32.RegistryKey k = Hklm64(@"SYSTEM\CurrentControlSet\Services\MSSQL$SQLEXPRESS")) return k != null;
            }
            catch { return false; }
        }

        /// <summary>null when this computer can become the main server, otherwise the reason (Arabic).</summary>
        static string CheckComputer(bool sqlInstalled)
        {
            if (!CanBeServer) return "SQL Server يحتاج ويندوز 64 بت. اختر حاسوبا آخر ليكون السيرفر الرئيسي.";
            if (sqlInstalled) return null;
            try
            {
                using (Microsoft.Win32.RegistryKey k = Hklm64(@"SOFTWARE\Microsoft\NET Framework Setup\NDP\v4\Full"))
                {
                    int rel = k == null ? 0 : Convert.ToInt32(k.GetValue("Release", 0));
                    if (rel < 461808) return "ويندوز في هذا الحاسوب قديم (يحتاج .NET Framework 4.7.2 أو أحدث). حدّث ويندوز ثم أعد المحاولة.";
                }
            }
            catch { }
            try
            {
                DriveInfo d = new DriveInfo(Path.GetPathRoot(Environment.SystemDirectory));
                if (d.AvailableFreeSpace < 6L * 1024 * 1024 * 1024)
                    return "المساحة الفارغة في القرص " + d.Name + " أقل من 6 غيغابايت (المتوفر " + (d.AvailableFreeSpace / 1024 / 1024 / 1024) + " غيغابايت). فرّغ مساحة ثم أعد المحاولة.";
            }
            catch { }
            return null;
        }

        static string LocalIPs()
        {
            List<string> ips = new List<string>();
            try
            {
                foreach (IPAddress a in Dns.GetHostAddresses(Dns.GetHostName()))
                    if (a.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork && !IPAddress.IsLoopback(a)) ips.Add(a.ToString());
            }
            catch { }
            return ips.Count == 0 ? "" : "عنوانه: " + string.Join("  أو  ", ips.ToArray());
        }

        // ------------------------------------------------------------ secondary
        public static bool RunSecondary(IWin32Window owner)
        {
            WaitForm wf = new WaitForm("جاري البحث عن السيرفر الرئيسي في الشبكة", "يرجى الانتظار… المدة المتوقعة أقل من نصف دقيقة.", null);
            wf.ShowOver(owner);
            wf.Step("البحث في الشبكة…", 20, 5);
            try
            {
                // 1. find the server(s) on the network (SQL Browser) — no password needed for this
                List<string> hosts = FindHosts(delegate(string s) { wf.Detail(s); });
                wf.Close();

                // 2. the server password, once
                string found = hosts.Count == 0 ? null : "وُجد السيرفر الرئيسي: " + string.Join("، ", hosts.ConvertAll(h => h.Replace(",1433", "")).ToArray());
                string pw = ServerPasswordDialog.AskExisting(owner, found, false);
                if (pw == null) return false;
                UI.WaitCursor(true);
                try { UsePassword(pw); } finally { UI.WaitCursor(false); }
                pw = null;

                // 3. connect: wrong password -> ask it again; nothing reachable -> ask the server's name
                string ok = null, typed = null;
                while (ok == null)
                {
                    bool wrongPassword = false;
                    UI.WaitCursor(true);
                    try
                    {
                        foreach (string h in hosts)
                        {
                            Probe r = ProbeServer(h);
                            if (r == Probe.Ok) { ok = h; break; }
                            if (r == Probe.WrongPassword) wrongPassword = true;
                        }
                    }
                    finally { UI.WaitCursor(false); }
                    if (ok != null) break;
                    if (wrongPassword)
                    {
                        pw = ServerPasswordDialog.AskExisting(owner, "كلمة سر السيرفر غير صحيحة.\nاكتبها كما اختيرت على حاسوب «السيرفر الرئيسي» (تُميَّز الأحرف الكبيرة من الصغيرة).", true);
                        if (pw == null) return false;
                        UI.WaitCursor(true);
                        try { UsePassword(pw); } finally { UI.WaitCursor(false); }
                        pw = null;
                        continue;
                    }
                    if (typed != null)
                        UI.Warn("تعذر الاتصال بـ " + typed + ".\nتأكد أن حاسوب السيرفر شغال ومتصل بنفس الشبكة، وأن «السيرفر الرئيسي» تم تجهيزه عليه.");
                    string name = InputDialog.Ask(owner, "سيرفر ثانوي",
                        (hosts.Count == 0 ? "لم يُعثر على السيرفر الرئيسي تلقائيا." : "تعذر الاتصال بالسيرفر الذي وُجد.") +
                        "\nاكتب اسم حاسوب السيرفر الرئيسي أو عنوانه (يظهر في رسالة نهاية تجهيزه):", "");
                    if (string.IsNullOrEmpty(name)) return false;
                    typed = name.Trim();
                    string hn = typed.Replace("\\SQLEXPRESS", "");
                    if (!hn.Contains(",")) hn += ",1433";
                    hosts = new List<string> { hn };
                }
                SaveSettings(ok);
                Db.EnsureAppTables();
                Db.Log(null, "اتصال سيرفر ثانوي", Environment.MachineName + " ← " + ok);
                UI.Info("✓ تم الاتصال بالسيرفر الرئيسي: " + ok.Replace(",1433", "") + "\nسجّل الدخول الآن.");
                return true;
            }
            finally { if (!wf.IsDisposed) wf.Close(); }
        }

        /// <summary>Finds the main server on the network and connects to it (with the password set by UsePassword).
        /// Returns "host,1433" or null.</summary>
        public static string Discover(Action<string> progress, out List<string> hosts)
        {
            hosts = FindHosts(progress);
            foreach (string h in hosts)
            {
                if (progress != null) progress("جاري تجربة الاتصال بـ " + h.Replace(",1433", "") + " …");
                if (TryServer(h)) return h;
            }
            return null;
        }

        /// <summary>The servers that may be the main server: the last one used, then every SQLEXPRESS answering on the network.</summary>
        public static List<string> FindHosts(Action<string> progress)
        {
            List<string> hosts = new List<string>();
            // the last server used first
            if (Settings.Mode == "sql" && !string.IsNullOrEmpty(Settings.SqlServer) && !Settings.SqlServer.StartsWith("localhost")) hosts.Add(Settings.SqlServer);
            // ask the network (SQL Browser answers from the main server)
            DataTable found = null;
            Thread t = new Thread(delegate() { try { found = SqlDataSourceEnumerator.Instance.GetDataSources(); } catch { } });
            t.IsBackground = true; t.Start();
            DateTime until = DateTime.Now.AddSeconds(20);
            Pump(() => !t.IsAlive || DateTime.Now > until);
            if (found != null)
                foreach (DataRow r in found.Rows)
                {
                    string inst = Convert.ToString(r["InstanceName"]);
                    if (!string.Equals(inst, "SQLEXPRESS", StringComparison.OrdinalIgnoreCase)) continue;
                    string h = Convert.ToString(r["ServerName"]) + ",1433";
                    if (!hosts.Contains(h)) hosts.Add(h);
                }
            return hosts;
        }

        public enum Probe { Ok, WrongPassword, Unreachable }

        /// <summary>Tries the server with the current password and tells a wrong password apart from an unreachable server.
        /// On Ok the program stays connected to it.</summary>
        public static Probe ProbeServer(string host)
        {
            System.Data.SqlClient.SqlConnectionStringBuilder b = new System.Data.SqlClient.SqlConnectionStringBuilder(ConnStr(host));
            b.ConnectTimeout = 8; b.Pooling = false;
            try { using (System.Data.SqlClient.SqlConnection c = new System.Data.SqlClient.SqlConnection(b.ConnectionString)) c.Open(); }
            catch (System.Data.SqlClient.SqlException ex)
            {
                // 18456 = login failed: the server answered, so the password is wrong (or the login was never created there)
                return ex.Number == 18456 ? Probe.WrongPassword : Probe.Unreachable;
            }
            catch { return Probe.Unreachable; }
            return TryServer(host) ? Probe.Ok : Probe.Unreachable;
        }

        public static bool TryServer(string host)
        {
            {
                string cs = ConnStr(host);
                System.Data.SqlClient.SqlConnectionStringBuilder b = new System.Data.SqlClient.SqlConnectionStringBuilder(cs);
                b.ConnectTimeout = 8;
                if (Db.InitSql(b.ConnectionString) != null) return false;
                if (!Db.TableExists("item")) { Db.Close(); return false; }
                // keep the normal timeout for daily work
                Db.InitSql(cs);
                return true;
            }
        }
    }
}
