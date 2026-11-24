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

        // ------------------------------------------------------------ progress window
        // Shows: what is happening, a progress bar, elapsed time, the expected remaining time and a "do not close" note.
        // Each step comes with its expected duration and the expected duration of everything after it; the bar and the
        // remaining time are computed from these and from the real elapsed time (the bar never goes backwards).
        class WaitForm : Form
        {
            Label lblTitle, lblStep, lblTime, lblNote;
            ProgressBar pb;
            System.Windows.Forms.Timer tmr;
            DateTime start = DateTime.Now, stepStart = DateTime.Now;
            string step = "", detail = null;
            int stepSec, afterSec, shown;

            public WaitForm(string title, string note)
            {
                Text = Program.AppName; RightToLeft = RightToLeft.Yes; Font = Theme.Font; Icon = Theme.AppIcon; BackColor = Color.White;
                FormBorderStyle = FormBorderStyle.FixedDialog; ControlBox = false; StartPosition = FormStartPosition.CenterScreen;
                ClientSize = new Size(560, 214); TopMost = true; ShowInTaskbar = true;
                Panel band = new Panel(); band.Bounds = new Rectangle(0, 0, 560, 5); band.BackColor = Theme.Accent; Controls.Add(band);
                lblTitle = L(new Rectangle(16, 14, 528, 30), Theme.FontBold, Theme.Primary); lblTitle.Text = title;
                lblStep = L(new Rectangle(16, 46, 528, 46), Theme.Font, Color.FromArgb(45, 45, 45));
                pb = new ProgressBar(); pb.Bounds = new Rectangle(16, 98, 528, 24); pb.Maximum = 1000;
                pb.Style = ProgressBarStyle.Marquee; pb.MarqueeAnimationSpeed = 30; Controls.Add(pb);
                lblTime = L(new Rectangle(16, 128, 528, 26), Theme.FontBold, Theme.Primary);
                lblNote = L(new Rectangle(16, 160, 528, 44), Theme.Font, Color.FromArgb(176, 64, 0)); lblNote.Text = note;
                tmr = new System.Windows.Forms.Timer(); tmr.Interval = 500; tmr.Tick += delegate { Draw(); }; tmr.Start();
                FormClosed += delegate { tmr.Stop(); tmr.Dispose(); };
                Draw();
            }
            Label L(Rectangle r, Font f, Color c)
            {
                Label l = new Label(); l.Bounds = r; l.Font = f; l.ForeColor = c; l.TextAlign = ContentAlignment.MiddleCenter; Controls.Add(l); return l;
            }

            /// <summary>Step without a time estimate (moving bar + elapsed time only).</summary>
            public void Set(string text) { Step(text, 0, 0); }

            /// <summary>New step: expected seconds for it and for everything after it.</summary>
            public void Step(string text, int seconds, int secondsAfter)
            {
                if (text != step || seconds != stepSec || secondsAfter != afterSec)
                { step = text; detail = null; stepSec = seconds; afterSec = secondsAfter; stepStart = DateTime.Now; }
                Draw(); Application.DoEvents();
            }

            /// <summary>Changes the text under the current step without restarting its timing.</summary>
            public void Detail(string text) { detail = text; Draw(); Application.DoEvents(); }

            void Draw()
            {
                double el = (DateTime.Now - start).TotalSeconds;
                double inStep = (DateTime.Now - stepStart).TotalSeconds;
                bool slow = stepSec > 0 && inStep > stepSec;
                lblStep.Text = (detail ?? step) + (slow ? "\nهذه الخطوة أبطأ من المعتاد على هذا الحاسوب — هذا طبيعي، يرجى الانتظار." : "");
                if (stepSec <= 0 && afterSec <= 0)
                {
                    if (pb.Style != ProgressBarStyle.Marquee) pb.Style = ProgressBarStyle.Marquee;
                    lblTime.Text = "الوقت المنقضي: " + Clock(el);
                    return;
                }
                double left = Math.Max(stepSec - inStep, Math.Min(stepSec, Math.Max(10, stepSec * 0.15)));
                double remaining = left + afterSec;
                int target = (int)(1000 * el / (el + remaining));
                if (target > 990) target = 990;
                if (target > shown) shown = target;
                if (pb.Style != ProgressBarStyle.Continuous) pb.Style = ProgressBarStyle.Continuous;
                pb.Value = shown;
                lblTime.Text = (shown / 10) + "٪     •     الوقت المنقضي: " + Clock(el) + "     •     المتبقي المتوقع: " + Remaining(remaining);
            }
            static string Clock(double s) { int t = (int)s; return (t / 60) + ":" + (t % 60).ToString("00"); }
            static string Remaining(double s)
            {
                if (s < 60) return "أقل من دقيقة";
                int n = (int)Math.Ceiling(s / 60);
                return "نحو " + (n == 1 ? "دقيقة" : n == 2 ? "دقيقتين" : n <= 10 ? n + " دقائق" : n + " دقيقة");
            }
        }

        static void Pump(Func<bool> done)
        {
            while (!done()) { Application.DoEvents(); Thread.Sleep(100); }
        }

        // ------------------------------------------------------------ main server
        public static bool RunMain(IWin32Window owner)
        {
            string pw = ServerPasswordDialog.AskNew(owner);
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

            // check this computer before installing anything
            string media = Path.Combine(Settings.AppDir, "مكونات", "SQLEXPR_x64_ENU.exe");
            bool sqlInstalled = IsSqlExpressInstalled();
            string problem = CheckComputer(sqlInstalled, File.Exists(media));
            if (problem != null) { UI.Warn("لا يمكن تجهيز السيرفر الرئيسي على هذا الحاسوب:\n\n" + problem); return false; }

            WaitForm wf = new WaitForm(sqlInstalled ? "جاري إعداد السيرفر الرئيسي — العملية تعمل الآن" : "جاري تنصيب السيرفر الرئيسي — التنصيب يعمل الآن",
                "⚠ لا تغلق البرنامج ولا تطفئ الحاسوب حتى تظهر رسالة الانتهاء.\n" + (sqlInstalled ? "المدة المتوقعة: 1–3 دقائق." : "المدة المتوقعة: 10–20 دقيقة (مرة واحدة فقط)."));
            wf.Show(owner);
            wf.Step("بدء التنصيب… اضغط «نعم» إذا ظهرت نافذة صلاحيات ويندوز.", 15, sqlInstalled ? 95 : 635);
            try
            {
                // 1. install / configure SQL Server (elevated PowerShell script embedded in the program)
                string res = RunSetupScript(media, delegate(string s, int sec, int after) { wf.Step(s, sec, after); });
                if (res == null) { UI.Warn("يجب الضغط «نعم» في نافذة صلاحيات ويندوز لإكمال التنصيب."); return false; }
                if (!res.StartsWith("OK")) { UI.Warn("لم يكتمل تجهيز السيرفر:\n\n" + res.Substring(Math.Min(4, res.Length))); return false; }
                string backupDir = res.Length > 3 ? res.Substring(3) : "";

                // 2. connect to it
                wf.Step("جاري الاتصال بالسيرفر…", 5, 25);
                string err = Db.InitSql(ConnStr("localhost,1433"));
                if (err != null) { UI.Warn("تم التنصيب لكن تعذر الاتصال بالسيرفر:\n" + err); return false; }

                // 3. move the data (unless the server already has data)
                bool hasData = Db.TableExists("item") && Db.I(Db.Scalar("SELECT COUNT(*) FROM item")) > 0;
                bool import = !hasData;
                if (hasData)
                    import = UI.Confirm("السيرفر فيه بيانات مسبقا.\nهل تريد استبدالها بالملف المختار؟\n\n(اختر «لا» للإبقاء على بيانات السيرفر الحالية)");
                List<string> report = null;
                if (import)
                {
                    wf.Step("جاري نقل البيانات إلى السيرفر…", 25, 0);
                    report = Migrate.Copy(data, delegate(string t) { wf.Detail("جاري نقل البيانات إلى السيرفر…\n" + t); });
                }
                Db.EnsureAppTables();
                SaveSettings("localhost,1433");
                Db.Log(null, "تجهيز السيرفر الرئيسي", Environment.MachineName);
                wf.Close();

                UI.Info("✓ هذا الحاسوب أصبح السيرفر الرئيسي.\n\n" +
                        "اسمه على الشبكة: " + Environment.MachineName + "\n" + LocalIPs() + "\n\n" +
                        (report != null ? "تم نقل البيانات (" + report.Count + " جدول) ✓\n" : "") +
                        "النسخة الاحتياطية اليومية في: " + backupDir + "\n\n" +
                        "على كل حاسوب آخر: افتح البرنامج ← «إعدادات الاتصال والسيرفر» ← «سيرفر ثانوي»،\n" +
                        "واكتب كلمة سر السيرفر التي اخترتها الآن (مرة واحدة لكل حاسوب). احتفظ بها في مكان آمن.\n" +
                        (hasData ? "إذا غيّرت كلمة السر عن السابقة: أعد «سيرفر ثانوي» على كل حاسوب آخر.\n" : "") +
                        "مهم: أبقِ هذا الحاسوب شغالا، ولا تستعمل البرنامج القديم بعد الآن.");
                return true;
            }
            catch (Exception ex) { UI.Error(ex); return false; }
            finally { if (!wf.IsDisposed) wf.Close(); }
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
            return
                "$ErrorActionPreference = 'Stop'\n" +
                "$res = " + PsQuote(result) + "\n" +
                "try {\n" +
                "  $pf = $env:ProgramW6432; if (-not $pf) { $pf = $env:ProgramFiles }\n" +
                "  $dir = Join-Path $pf 'EngStores\\setup'\n" +
                "  New-Item -ItemType Directory -Force -Path $dir | Out-Null\n" +
                "  & icacls.exe $dir /setowner '*S-1-5-32-544' /T /C /Q | Out-Null\n" +
                "  & icacls.exe $dir /reset /T /C /Q | Out-Null\n" +
                "  & icacls.exe $dir /inheritance:r /grant:r '*S-1-5-32-544:(OI)(CI)F' '*S-1-5-18:(OI)(CI)F' /C /Q | Out-Null\n" +
                "  if ($LASTEXITCODE -ne 0) { throw 'icacls failed' }\n" +
                "  $dst = Join-Path $dir 'setup-server.ps1'\n" +
                "  Copy-Item -LiteralPath " + PsQuote(script) + " -Destination $dst -Force\n" +
                "  if ((Get-FileHash -LiteralPath $dst -Algorithm SHA256).Hash -ne '" + hash + "') { throw 'setup script changed on disk' }\n" +
                "  & $dst -PasswordFile " + PsQuote(pwFile) + " -ResultFile $res -MediaFile " + PsQuote(media) + " -ProgressFile " + PsQuote(prog) + "\n" +
                "  exit $LASTEXITCODE\n" +
                "} catch { [IO.File]::WriteAllText($res, ('ERR|' + $_.Exception.Message), (New-Object Text.UTF8Encoding $false)); exit 1 }\n";
        }

        /// <summary>Runs the embedded setup script elevated and hidden. Returns "OK|backupDir", "ERR|message",
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
                        "-NoProfile -ExecutionPolicy Bypass -WindowStyle Hidden -EncodedCommand " + Convert.ToBase64String(Encoding.Unicode.GetBytes(boot)));
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

        static bool IsSqlExpressInstalled()
        {
            try
            {
                using (Microsoft.Win32.RegistryKey k = Hklm64(@"SOFTWARE\Microsoft\Microsoft SQL Server\Instance Names\SQL"))
                    return k != null && k.GetValue("SQLEXPRESS") != null;
            }
            catch { return false; }
        }

        /// <summary>null when this computer can become the main server, otherwise the reason (Arabic).</summary>
        static string CheckComputer(bool sqlInstalled, bool mediaBundled)
        {
            if (!Environment.Is64BitOperatingSystem) return "SQL Server يحتاج ويندوز 64 بت. اختر حاسوبا آخر ليكون السيرفر الرئيسي.";
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
            if (!mediaBundled)
            {
                try { Dns.GetHostAddresses("download.microsoft.com"); }
                catch { return "حزمة SQL Server غير موجودة في مجلد «مكونات» بجانب البرنامج، ولا يوجد اتصال بالإنترنت لتنزيلها.\nانسخ مجلد البرنامج كاملا (مع مجلد مكونات) أو اتصل بالإنترنت."; }
            }
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
            WaitForm wf = new WaitForm("جاري البحث عن السيرفر الرئيسي في الشبكة", "يرجى الانتظار… المدة المتوقعة أقل من نصف دقيقة.");
            wf.Show(owner);
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
