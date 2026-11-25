using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Windows.Forms;

namespace Stores.Installer
{
    /// <summary>«Inventory Management Setup.exe». Built by installer\build-setup.bat (the program files are embedded as one zip).
    ///
    ///   (no arguments)              the window: install, or replace the installed copy
    ///   /S [/D=folder]              silent install / update. Without /D: the first copy found, else the default folder.
    ///                               A running new program is asked to exit (60 s); an old one is never killed.
    ///     /REINSTALL                also when the same version is installed (a newer one is never replaced silently)
    ///     /NODESKTOP                no Desktop shortcut
    ///     /ACCEPTSQLTERMS           one-file setup: accepts Microsoft's terms for SQL Server Express (without it: the program only)
    ///   /uninstall [/S] [/D=folder] removes the program (also: the file is named Uninstall.exe)
    ///   /lang=ar | /lang=en         language (default: the Windows display language)
    ///   /D= must be the last argument; the folder may contain spaces.
    ///   Exit codes: class SetupCode (0 = done).</summary>
    static class Setup
    {
        internal static string Log = Path.Combine(Path.GetTempPath(), "EngStoresSetup.log");

        internal static void L(string m)
        {
            try { File.AppendAllText(Log, DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss ") + m + "\r\n", Encoding.UTF8); } catch { }
        }

        internal static string PayloadVersion
        {
            get
            {
                object[] a = typeof(Setup).Assembly.GetCustomAttributes(typeof(AssemblyInformationalVersionAttribute), false);
                return a.Length == 0 ? "0.0" : ((AssemblyInformationalVersionAttribute)a[0]).InformationalVersion;
            }
        }

        internal static Stream OpenPayload() { return typeof(Setup).Assembly.GetManifestResourceStream("payload.zip"); }

        /// <summary>Command line.</summary>
        internal class Args
        {
            public bool Silent, Uninstall, Reinstall, NoDesktop, FilesOnly, Temp, Bad, AcceptTerms;
            public string Dir, Lang;

            public static Args Parse(string[] args)
            {
                Args r = new Args();
                for (int i = 0; i < args.Length; i++)
                {
                    string a = args[i], u = a.ToUpperInvariant();
                    if (u == "/S" || u == "/SILENT") r.Silent = true;
                    else if (u == "/UNINSTALL") r.Uninstall = true;
                    else if (u == "/REINSTALL") r.Reinstall = true;
                    else if (u == "/NODESKTOP") r.NoDesktop = true;
                    else if (u == "/ACCEPTSQLTERMS") r.AcceptTerms = true;
                    else if (u == "/FILES") r.FilesOnly = true;      // internal: the elevated helper
                    else if (u == "/TEMP") r.Temp = true;            // internal: the uninstaller runs from a copy in %TEMP%
                    else if (u.StartsWith("/LANG=")) r.Lang = a.Substring(6).ToLowerInvariant();
                    else if (u.StartsWith("/D="))
                    {
                        // «/D=C:\My folder» without quotes arrives as several arguments
                        StringBuilder sb = new StringBuilder(a.Substring(3));
                        while (i + 1 < args.Length && !args[i + 1].StartsWith("/")) sb.Append(' ').Append(args[++i]);
                        r.Dir = InstallEnv.Norm(sb.ToString());
                        if (r.Dir == null || !Path.IsPathRooted(sb.ToString().Trim().Trim('"'))) r.Bad = true;
                    }
                    else r.Bad = true;
                }
                return r;
            }
        }

        [STAThread]
        static int Main(string[] argv)
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            string exe = Application.ExecutablePath;
            Args a = Args.Parse(argv);
            Tx.En = a.Lang == "en" || (a.Lang != "ar" && !CultureInfo.InstalledUICulture.TwoLetterISOLanguageName.Equals("ar") && !CultureInfo.CurrentUICulture.TwoLetterISOLanguageName.Equals("ar"));
            if (string.Equals(Path.GetFileName(exe), InstallEnv.UninstallExe, StringComparison.OrdinalIgnoreCase)) a.Uninstall = true;
            if (a.Bad) { L("bad arguments: " + string.Join(" ", argv)); if (a.Silent) return SetupCode.BadArguments; }

            SetupEngine en = new SetupEngine(InstallEnv.Current, PayloadVersion, OpenPayload, exe);
            if (a.Lang == null) InstalledLanguage(en.Env, a, exe);
            if (a.FilesOnly) return a.Dir == null ? SetupCode.BadArguments : Helper(en, a.Dir);
            if (a.Uninstall) return Uninstall(en, a, exe);
            if (a.Silent) return Silent(en, a);

            bool first;
            using (Mutex m = new Mutex(true, @"Local\EngStores.SetupRunning" + (en.Env.OnlyUnder == null ? "" : ".Test"), out first))
            {
                if (!first) { Tx.Info(Tx.S("برنامج التنصيب مفتوح في نافذة أخرى.", "The setup is already open in another window.")); return SetupCode.Cancelled; }
                using (SetupForm f = new SetupForm(en, a.Bad ? null : a.Dir)) { Application.Run(f); return f.Code; }
            }
        }

        /// <summary>The language chosen in the installed program counts more than the Windows one.</summary>
        static void InstalledLanguage(InstallEnv env, Args a, string exe)
        {
            string dir = a.Dir ?? (a.Uninstall ? Path.GetDirectoryName(exe) : InstallFind.RegValue(env.RegKey, "InstallDir"));
            try
            {
                foreach (string l in File.ReadAllLines(Path.Combine(dir, "settings.ini"), Encoding.UTF8))
                    if (l.StartsWith("Language=", StringComparison.OrdinalIgnoreCase)) Tx.En = l.Substring(9).Trim() == "en";
            }
            catch { }
        }

        // ------------------------------------------------------------ silent
        internal static int Silent(SetupEngine en, Args a)
        {
            List<InstallCopy> found = a.Dir != null ? new List<InstallCopy>() : InstallFind.All(en.Env);
            string dir = a.Dir ?? (found.Count > 0 ? found[0].Dir : en.Env.DefaultDir);
            InstallCopy old = a.Dir != null ? InstallFind.At(en.Env, dir) : (found.Count > 0 ? found[0] : null);
            L("silent install " + en.Version + " → " + dir + (old == null ? "" : " (installed: " + old.Version + ")"));
            if (old != null)
            {
                int c = InstallFind.Compare(old.Version, en.Version);
                if (c > 0) return End(SetupCode.NewerInstalled);
                if (c == 0 && !a.Reinstall) return End(SetupCode.SameInstalled);
            }
            foreach (int pid in InstallFind.RunningIn(en.Env, dir))
                if (ProgramExit.Ask(pid, 60000, null) != ExitAnswer.Closed) return End(SetupCode.ProgramOpen);
            int code = en.CopyFiles(dir);
            if (code != SetupCode.Ok) { L(en.Error); return End(code); }
            long ps, pn; bool inside = PackageInside.Find(en.SetupExe, out ps, out pn);
            int comp = inside && !a.AcceptTerms ? SetupCode.Ok : en.CopyComponents(dir);
            if (comp != SetupCode.Ok) L(en.Error);
            en.Finish(dir, null, !a.NoDesktop, Tx.En);
            return End(comp);
        }

        static int End(int code) { L("exit code " + code); return code; }

        /// <summary>Elevated helper: only the files (shortcuts and registry belong to the normal user).</summary>
        static int Helper(SetupEngine en, string dir)
        {
            int code = en.CopyFiles(dir);
            if (code == SetupCode.Ok) code = en.CopyComponents(dir);
            if (code != SetupCode.Ok) L("helper: " + en.Error);
            return code;
        }

        // ------------------------------------------------------------ uninstall
        static int Uninstall(SetupEngine en, Args a, string exe)
        {
            string dir = a.Dir ?? (a.Temp ? null : Path.GetDirectoryName(exe));
            if (dir == null) return SetupCode.BadArguments;
            if (!a.Temp && InstallEnv.IsUnder(exe, dir))
            {
                // the file that runs cannot remove itself: continue from a copy in %TEMP%
                string tmp = Path.Combine(Path.GetTempPath(), "EngStoresUninstall-" + Guid.NewGuid().ToString("N").Substring(0, 8));
                Directory.CreateDirectory(tmp);
                string copy = Path.Combine(tmp, "EngStoresUninstall.exe");
                File.Copy(exe, copy, true);
                ProcessStartInfo si = new ProcessStartInfo(copy, "/uninstall /temp /lang=" + (Tx.En ? "en" : "ar") + (a.Silent ? " /S" : "") + " \"/D=" + dir + "\"");
                si.UseShellExecute = false; si.WorkingDirectory = tmp;
                Process.Start(si);
                return SetupCode.Ok;
            }
            int code;
            if (a.Silent)
            {
                L("silent uninstall " + dir);
                code = SetupCode.Ok;
                foreach (int pid in InstallFind.RunningIn(en.Env, dir))
                    if (ProgramExit.Ask(pid, 60000, null) != ExitAnswer.Closed) code = SetupCode.ProgramOpen;
                if (code == SetupCode.Ok) { Thread.Sleep(800); code = en.Uninstall(dir); }
                End(code);
            }
            else using (SetupForm f = new SetupForm(en, dir, true)) { Application.Run(f); code = f.Code; }
            if (a.Temp) DeleteSelfLater(exe);
            return code;
        }

        static void DeleteSelfLater(string exe)
        {
            try
            {
                string dir = Path.GetDirectoryName(exe);
                if (!InstallFind.IsTemp(dir)) return;
                ProcessStartInfo si = new ProcessStartInfo("cmd.exe", "/c ping 127.0.0.1 -n 4 >nul & del /f /q \"" + exe + "\" & rd \"" + dir + "\"");
                si.UseShellExecute = false; si.CreateNoWindow = true; si.WorkingDirectory = Path.GetTempPath();
                Process.Start(si);
            }
            catch { }
        }
    }

    /// <summary>The two languages of the setup.</summary>
    static class Tx
    {
        public static bool En;
        public static string S(string ar, string en) { return En ? en : ar; }
        static MessageBoxOptions Opt { get { return En ? 0 : MessageBoxOptions.RtlReading | MessageBoxOptions.RightAlign; } }
        public static string Title { get { return S("تنصيب نظام إدارة المخازن", "Inventory Management Setup"); } }
        public static void Info(string m) { MessageBox.Show(m, Title, MessageBoxButtons.OK, MessageBoxIcon.Information, MessageBoxDefaultButton.Button1, Opt); }
        public static void Warn(string m) { MessageBox.Show(m, Title, MessageBoxButtons.OK, MessageBoxIcon.Warning, MessageBoxDefaultButton.Button1, Opt); }
        public static bool Confirm(string m) { return MessageBox.Show(m, Title, MessageBoxButtons.YesNo, MessageBoxIcon.Question, MessageBoxDefaultButton.Button2, Opt) == DialogResult.Yes; }
    }
}
