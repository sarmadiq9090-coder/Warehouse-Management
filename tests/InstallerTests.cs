using System;
using System.CodeDom.Compiler;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.IO.Compression;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Windows.Forms;
using Microsoft.CSharp;
using Microsoft.Win32;
using Stores.Installer;

namespace Stores
{
    // The setup program (installer\*.cs) and its shared code (src\Update\Install*.cs, ProgramExit, ExitListener, ShellLink).
    // Works only in its own temp folder, on HKCU\Software\EngStoresTest, with a dummy program it compiles and starts itself:
    // never on the real Desktop, Start menu, «Apps» entry or a real program.
    // Usage: InstallerTests.exe [<copy of data.mdb>]     (with data: also the exit request inside the real main window)
    //        InstallerTests.exe --shots <out dir>        (pictures of every page of the setup, Arabic and English)
    //        InstallerTests.exe --manual docs\manual\img (the pictures of the setup in the user manual)
    static class InstallerTestsMain
    {
        static int fails = 0;
        static void Check(bool ok, string what) { Console.WriteLine((ok ? "PASS  " : "FAIL  ") + what); if (!ok) fails++; }

        const string DummyExe = "EngStoresSetupDummy.exe", Old = "1.1.0", New = "1.2.5";
        static string root;
        static readonly List<Process> started = new List<Process>();

        [STAThread]
        static int Main(string[] args)
        {
            Console.OutputEncoding = Encoding.UTF8;
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            root = Path.Combine(Path.GetTempPath(), "EngStoresInstallerTests-مخازن-" + Guid.NewGuid().ToString("N").Substring(0, 6));
            Directory.CreateDirectory(root);
            root = InstallEnv.Norm(root);   // without 8.3 names (GitHub: RUNNER~1)
            InstallEnv env = Env();
            InstallEnv.Current = env;
            try
            {
                if (args.Length > 1 && args[0] == "--shots") { Shots(env, args[1]); return 0; }
                if (args.Length > 1 && args[0] == "--manual") { ManualShots(env, args[1]); return 0; }
                Versions(env);
                Paths(env);
                CommandLine();
                FreshInstall(env);
                UpdateOver(env);
                Rollback(env);
                Locked(env);
                Moved(env);
                Finding(env);
                Closing(env);
                SilentMode(env);
                Components(env);
                NeverConfigured(env);
                Uninstall(env);
                if (args.Length > 0) Listener(args[0]);
            }
            catch (Exception ex) { Console.WriteLine("ERROR " + ex); fails++; }
            finally
            {
                foreach (Process p in started) try { if (!p.HasExited) p.Kill(); } catch { }
                Thread.Sleep(300);
                try { Registry.CurrentUser.DeleteSubKeyTree(@"Software\EngStoresTest\Unit" + Pid, false); } catch { }
                try
                {
                    bool empty;
                    using (RegistryKey k = Registry.CurrentUser.OpenSubKey(@"Software\EngStoresTest")) empty = k != null && k.SubKeyCount == 0;
                    if (empty) Registry.CurrentUser.DeleteSubKey(@"Software\EngStoresTest", false);
                }
                catch { }
                try { Directory.Delete(root, true); } catch { }
            }
            Console.WriteLine(fails == 0 ? "ALL PASSED" : fails + " FAILED");
            return fails == 0 ? 0 : 1;
        }

        // ------------------------------------------------------------ helpers
        static int Pid { get { using (Process p = Process.GetCurrentProcess()) return p.Id; } }

        static InstallEnv Env()
        {
            InstallEnv e = InstallEnv.Create(root);
            e.ExeName = DummyExe;
            e.RegKey = @"Software\EngStoresTest\Unit" + Pid + @"\Setup";
            e.UninstallKey = @"Software\EngStoresTest\Unit" + Pid + @"\Uninstall";
            return e;
        }

        static string P(params string[] parts) { string r = root; foreach (string p in parts) r = Path.Combine(r, p); return r; }

        static void Put(string path, string text) { Directory.CreateDirectory(Path.GetDirectoryName(path)); File.WriteAllText(path, text, Encoding.UTF8); }

        static string Get(string path) { return File.Exists(path) ? File.ReadAllText(path, Encoding.UTF8) : null; }

        static readonly Dictionary<string, byte[]> dummies = new Dictionary<string, byte[]>();

        /// <summary>A small program with Stores.Program.Version, named as the program. Arguments: listen (answers the exit request),
        /// refuse (refuses the first one); without: an old program that cannot be asked. Ends by itself after 90 seconds.</summary>
        static byte[] Dummy(string version)
        {
            byte[] b;
            if (dummies.TryGetValue(version, out b)) return b;
            string src =
                "using System; using System.Threading; using System.Diagnostics; namespace Stores { class Program { public const string Version = \"" + version + "\";" +
                "static void Main(string[] a) { if (a.Length == 0) { Thread.Sleep(90000); return; }" +
                "int pid = Process.GetCurrentProcess().Id;" +
                "EventWaitHandle r = new EventWaitHandle(false, EventResetMode.AutoReset, @\"" + ProgramExit.RefusedEvent(0).TrimEnd('0') + "\" + pid);" +
                "EventWaitHandle e = new EventWaitHandle(false, EventResetMode.AutoReset, @\"" + ProgramExit.ExitEvent(0).TrimEnd('0') + "\" + pid);" +
                "if (!e.WaitOne(90000)) return; if (a[0] == \"refuse\") { r.Set(); e.WaitOne(90000); } } } }";
            string exe = P("build", Guid.NewGuid().ToString("N"), DummyExe);
            Directory.CreateDirectory(Path.GetDirectoryName(exe));
            CompilerParameters cp = new CompilerParameters(new[] { "System.dll" }, exe); cp.GenerateExecutable = true; cp.CompilerOptions = "/target:winexe /platform:x86";
            using (CSharpCodeProvider c = new CSharpCodeProvider())
            {
                CompilerResults r = c.CompileAssemblyFromSource(cp, src);
                if (r.Errors.HasErrors) throw new Exception(r.Errors[0].ToString());
            }
            return dummies[version] = File.ReadAllBytes(exe);
        }

        /// <summary>A zip as scripts\package.ps1 makes it.</summary>
        static byte[] Zip(string version)
        {
            MemoryStream ms = new MemoryStream();
            using (ZipArchive z = new ZipArchive(ms, ZipArchiveMode.Create, true, Encoding.UTF8))
            {
                Entry(z, DummyExe, Dummy(version));
                Entry(z, InstallEnv.GuideFile, Encoding.UTF8.GetBytes("guide " + version));
                Entry(z, InstallEnv.ServerGuideFile, Encoding.UTF8.GetBytes("server " + version));
                Entry(z, "docs/manual/index.html", Encoding.UTF8.GetBytes("manual " + version));
                Entry(z, "docs/manual/img/a.png", Encoding.UTF8.GetBytes("png " + version));
                // must never be written even if somebody packs them by mistake
                Entry(z, "settings.ini", Encoding.UTF8.GetBytes("evil"));
                Entry(z, "data.AMR", Encoding.UTF8.GetBytes("evil"));
                Entry(z, "logo.png", Encoding.UTF8.GetBytes("evil"));
                Entry(z, "../outside.txt", Encoding.UTF8.GetBytes("evil"));
            }
            return ms.ToArray();
        }

        static void Entry(ZipArchive z, string name, byte[] data) { using (Stream s = z.CreateEntry(name).Open()) s.Write(data, 0, data.Length); }

        static SetupEngine Engine(InstallEnv env, string version)
        {
            byte[] zip = Zip(version);
            string setup = P("download", "Setup.exe");
            if (!File.Exists(setup)) Put(setup, "this is the setup file");
            SetupEngine e = new SetupEngine(env, version, delegate { return new MemoryStream(zip); }, setup);
            e.WaitInUseMs = 600;
            return e;
        }

        /// <summary>An old installation with user files, as it looks after months of use.</summary>
        static string OldCopy(string name, string version)
        {
            string d = P(name);
            Directory.CreateDirectory(d);
            File.WriteAllBytes(Path.Combine(d, DummyExe), Dummy(version));
            Put(Path.Combine(d, InstallEnv.GuideFile), "guide " + version);
            Put(Path.Combine(d, @"docs\manual\index.html"), "manual " + version);
            Put(Path.Combine(d, "settings.ini"), "Language=ar\r\nDataPath=D:\\x.AMR");
            Put(Path.Combine(d, "errors.log"), "log");
            Put(Path.Combine(d, "logo.png"), "my logo");
            Put(Path.Combine(d, "بيانات المخازن.AMR"), "data");
            Put(Path.Combine(d, "other.mdb"), "mdb");
            Put(Path.Combine(d, @"نسخ احتياطية - مخازن\2026.AMR"), "backup");
            Put(Path.Combine(d, @"مكونات\SQLEXPR_x64_ENU.exe"), "sql");
            return d;
        }

        static bool UserFilesKept(string d)
        {
            return Get(Path.Combine(d, "settings.ini")) == "Language=ar\r\nDataPath=D:\\x.AMR" && Get(Path.Combine(d, "errors.log")) == "log" &&
                   Get(Path.Combine(d, "logo.png")) == "my logo" && Get(Path.Combine(d, "بيانات المخازن.AMR")) == "data" && Get(Path.Combine(d, "other.mdb")) == "mdb" &&
                   Get(Path.Combine(d, @"نسخ احتياطية - مخازن\2026.AMR")) == "backup" && Get(Path.Combine(d, @"مكونات\SQLEXPR_x64_ENU.exe")) == "sql";
        }

        static Process Start(string dir, string arg)
        {
            ProcessStartInfo si = new ProcessStartInfo(Path.Combine(dir, DummyExe), arg); si.UseShellExecute = false; si.WorkingDirectory = root;
            Process p = Process.Start(si); started.Add(p);
            for (int i = 0; i < 50; i++)
            {
                Thread.Sleep(100);
                if (arg.Length == 0 ? i > 4 : ProgramExit.CanAsk(p.Id)) break;
            }
            return p;
        }

        static void ClearReg(InstallEnv env)
        {
            Registry.CurrentUser.DeleteSubKeyTree(env.RegKey, false); Registry.CurrentUser.DeleteSubKeyTree(env.UninstallKey, false);
        }

        static void ClearLinks(InstallEnv env) { foreach (string d in new[] { env.DesktopDir, env.StartMenuDir }) if (Directory.Exists(d)) Directory.Delete(d, true); }

        static int Links(string dir) { return Directory.Exists(dir) ? Directory.GetFiles(dir, "*.lnk").Length : 0; }

        // ------------------------------------------------------------ tests
        static void Versions(InstallEnv env)
        {
            Check(InstallFind.Compare("1.2.0", "1.2.1") < 0, "version: 1.2.0 older than 1.2.1");
            Check(InstallFind.Compare("1.9.0", "1.10.0") < 0, "version: 1.9.0 older than 1.10.0 (numbers, not text)");
            Check(InstallFind.Compare("1.2", "1.2.0") == 0 && InstallFind.Compare("v1.2.0", "1.2.0.0") == 0, "version: 1.2 = 1.2.0 = v1.2.0");
            Check(InstallFind.Compare("1.3.0", "1.2.9") > 0, "version: 1.3.0 newer than 1.2.9");
            Check(InstallFind.Compare("", "0.1") < 0, "version: unknown counts as older");
            string d = OldCopy("v", "1.2.0"), d2 = OldCopy("v2", "1.4.7");
            Check(InstallFind.At(env, d).Version == "1.2.0" && InstallFind.At(env, d2).Version == "1.4.7", "version read from two program files with the same name (metadata, not run)");
            File.Delete(Path.Combine(d, DummyExe));   // not locked
            Put(Path.Combine(d, DummyExe), "not a program");
            Check(InstallFind.At(env, d).Version == "", "a file that is no program: version unknown");
            Check(InstallFind.ExeVersion(Application.ExecutablePath) == Program.Version, "the real Program.Version is readable (" + Program.Version + ")");
        }

        static void Paths(InstallEnv env)
        {
            Check(InstallReplace.IsProtected("settings.ini") && InstallReplace.IsProtected("errors.log") && InstallReplace.IsProtected("LOGO.png") &&
                  InstallReplace.IsProtected("x.AMR") && InstallReplace.IsProtected(@"sub\x.mdb") && InstallReplace.IsProtected("x.accdb") &&
                  InstallReplace.IsProtected(@"نسخ احتياطية\x.txt") && InstallReplace.IsProtected(@"نسخ احتياطية - مخازن\x") && InstallReplace.IsProtected(@"مكونات\x.exe"), "protected files");
            Check(!InstallReplace.IsProtected(DummyExe) && !InstallReplace.IsProtected("docs/manual/img/logo.png") && !InstallReplace.IsProtected(InstallEnv.GuideFile), "program files are not protected");
            Check(InstallEnv.SamePath(@"C:\A\b\", @"c:\a\B") && InstallEnv.IsUnder(@"C:\A\b\c", @"c:\a\b") && !InstallEnv.IsUnder(@"C:\A\bc", @"C:\A\b"), "path comparison");
            InstallEnv real = InstallEnv.Create(null);
            Check(InstallFind.IsTemp(root) && InstallFind.At(real, root) == null && !real.AllowTemp, "a copy under %TEMP% is no installation for the real setup");
            string data = P("pf"); Put(Path.Combine(data, "x.txt"), "x");
            Check(SetupForm.ProgramFolder(env, data) == Path.Combine(data, "Inventory Management"), "a chosen folder with other files gets a sub-folder");
            Check(SetupForm.ProgramFolder(env, P("v2")) == P("v2"), "a chosen folder with the program is taken as it is");
        }

        static void CommandLine()
        {
            Setup.Args a = Setup.Args.Parse(new[] { "/S", @"/D=C:\My", "folder", "x" });
            Check(a.Silent && a.Dir == @"C:\My folder x" && !a.Bad, "command line: /D= with spaces and without quotes");
            Check(Setup.Args.Parse(new[] { "/uninstall", "/S" }).Uninstall && Setup.Args.Parse(new[] { "/nonsense" }).Bad && Setup.Args.Parse(new[] { "/D=relative" }).Bad, "command line: switches, unknown switch, relative folder");
        }

        static void FreshInstall(InstallEnv env)
        {
            ClearReg(env); ClearLinks(env);
            string d = P("fresh", "Inventory Management");
            SetupEngine e = Engine(env, New);
            int files = 0; e.Progress = delegate(long a, long b, string w) { files = (int)b; };
            Check(e.CopyFiles(d) == SetupCode.Ok, "fresh install: files copied (" + e.Error + ")");
            e.Finish(d, null, true, false);
            Check(InstallFind.At(env, d).Version == New && Get(Path.Combine(d, @"docs\manual\img\a.png")) == "png " + New && Get(Path.Combine(d, InstallEnv.ServerGuideFile)) != null, "fresh install: exe, guides, manual");
            Check(files == 6 && File.Exists(Path.Combine(d, "Uninstall.exe")), "fresh install: the uninstaller");
            Check(!File.Exists(Path.Combine(d, "settings.ini")) && !File.Exists(Path.Combine(d, "data.AMR")) && !File.Exists(Path.Combine(d, "logo.png")) && !File.Exists(P("fresh", "outside.txt")), "fresh install: data-like and outside entries of the zip are ignored");
            Check(!Directory.Exists(Path.Combine(d, InstallEnv.UpdateBackupDir)), "fresh install: no backup folder");
            Check(InstallFind.RegValue(env.RegKey, "InstallDir") == d && InstallFind.RegValue(env.RegKey, "Version") == New, "fresh install: registry value");
            Check(InstallFind.RegValue(env.UninstallKey, "InstallLocation") == d && InstallFind.RegValue(env.UninstallKey, "UninstallString").Contains("Uninstall.exe\" /uninstall"), "fresh install: «Apps» entry");
            string lnk = Path.Combine(env.DesktopDir, "نظام إدارة المخازن.lnk");
            Check(ShellLink.Target(lnk) == env.ExePath(d), "fresh install: Desktop shortcut (Arabic name and folder) → " + ShellLink.Target(lnk));
            Check(Links(env.StartMenuDir) == 1, "fresh install: Start menu shortcut");
            e.Finish(d, null, true, false);
            Check(Links(env.DesktopDir) == 1 && Links(env.StartMenuDir) == 1, "installed again: no second shortcuts");
        }

        static void UpdateOver(InstallEnv env)
        {
            ClearReg(env); ClearLinks(env);
            string d = OldCopy("old copy", Old);
            File.SetAttributes(Path.Combine(d, @"docs\manual\index.html"), FileAttributes.ReadOnly);
            SetupEngine e = Engine(env, New);
            Check(e.CopyFiles(d) == SetupCode.Ok, "update: files copied (" + e.Error + ")");
            e.Finish(d, null, true, true);
            Check(InstallFind.At(env, d).Version == New && Get(Path.Combine(d, @"docs\manual\index.html")) == "manual " + New && Get(Path.Combine(d, InstallEnv.GuideFile)) == "guide " + New, "update: exe, manual (read-only too) and guide replaced");
            Check(UserFilesKept(d), "update: settings, log, logo, data, backups and «مكونات» unchanged");
            Check(Get(Path.Combine(e.BackupDir, @"docs\manual\index.html")) == "manual " + Old && InstallFind.ExeVersion(Path.Combine(e.BackupDir, DummyExe)) == Old, "update: old files kept in .update-backup");
            Check(Directory.GetFiles(d, "*.new", SearchOption.AllDirectories).Length == 0, "update: no left-over temporary files");
            Check(File.Exists(Path.Combine(env.DesktopDir, "Inventory Management.lnk")), "update: English shortcut name");
        }

        static void Rollback(InstallEnv env)
        {
            string d = OldCopy("rollback", Old);
            SetupEngine e = Engine(env, New);
            e.BeforeFile = delegate(string rel) { if (rel.EndsWith("a.png")) throw new IOException("disk full"); };
            Check(e.CopyFiles(d) == SetupCode.Failed && e.Error.Contains("disk full"), "error in the middle: reported");
            Check(InstallFind.At(env, d).Version == Old && Get(Path.Combine(d, @"docs\manual\index.html")) == "manual " + Old && Get(Path.Combine(d, InstallEnv.GuideFile)) == "guide " + Old, "error in the middle: the old version is back");
            Check(!File.Exists(Path.Combine(d, InstallEnv.ServerGuideFile)) && !File.Exists(Path.Combine(d, "Uninstall.exe")) && !Directory.Exists(Path.Combine(d, @"docs\manual\img")), "error in the middle: new files and folders removed again");
            Check(UserFilesKept(d), "error in the middle: user files unchanged");
            string n = P("rollback new", "x");
            e = Engine(env, New); e.BeforeFile = delegate(string rel) { if (rel.EndsWith("a.png")) throw new IOException("x"); };
            Check(e.CopyFiles(n) == SetupCode.Failed && !Directory.Exists(P("rollback new")), "failed fresh install leaves no folder");
        }

        static void Locked(InstallEnv env)
        {
            string d = OldCopy("locked", Old);
            SetupEngine e = Engine(env, New);
            using (new FileStream(Path.Combine(d, @"docs\manual\index.html"), FileMode.Open, FileAccess.Read, FileShare.Read))
                Check(e.CopyFiles(d) == SetupCode.Failed, "a locked file: reported (" + e.Error.Replace("\n", " ") + ")");
            Check(InstallFind.At(env, d).Version == Old && Get(Path.Combine(d, @"docs\manual\index.html")) == "manual " + Old && UserFilesKept(d), "a locked file: rollback leaves the old version intact");
            e = Engine(env, New);
            using (new FileStream(Path.Combine(d, DummyExe), FileMode.Open, FileAccess.Read, FileShare.Read))
                Check(e.CopyFiles(d) == SetupCode.ProgramOpen, "the exe in use (another account / computer): refused");
            Check(InstallFind.At(env, d).Version == Old && Get(Path.Combine(d, InstallEnv.GuideFile)) == "guide " + Old && !File.Exists(Path.Combine(d, "Uninstall.exe")), "the exe in use: nothing changed");
        }

        static void Moved(InstallEnv env)
        {
            ClearReg(env); ClearLinks(env);
            string o = OldCopy("from", Old), third = OldCopy("third", Old), d = P("to");
            string menu = Path.Combine(env.StartMenuDir, "Company", "Stores.lnk"), other = Path.Combine(env.DesktopDir, "third.lnk");
            ShellLink.Create(menu, env.ExePath(o), "x"); ShellLink.Create(other, env.ExePath(third), "x");
            SetupEngine e = Engine(env, New);
            Check(e.CopyFiles(d) == SetupCode.Ok, "another folder: installed");
            e.Finish(d, o, true, false);
            Check(Get(Path.Combine(d, "settings.ini")) == Get(Path.Combine(o, "settings.ini")) && Get(Path.Combine(d, "logo.png")) == "my logo", "another folder: connection settings and logo carried over");
            Check(InstallFind.At(env, o).Version == Old && UserFilesKept(o), "another folder: the old copy stays as it is");
            Check(ShellLink.Target(menu) == env.ExePath(d) && Links(env.StartMenuDir) == 0, "another folder: the shortcut to the old place points to the new one (no second one)");
            Check(ShellLink.Target(other) == env.ExePath(third), "another folder: shortcuts of other copies unchanged");
            Check(Links(env.DesktopDir) == 2, "another folder: Desktop icon created");
            Put(Path.Combine(d, "settings.ini"), "mine"); InstallReplace.CarrySettings(o, d);
            Check(Get(Path.Combine(d, "settings.ini")) == "mine", "carrying settings never overwrites");
        }

        static void Finding(InstallEnv env)
        {
            ClearReg(env); ClearLinks(env);
            string a = OldCopy("find a", "1.0.1"), b = OldCopy("find b", "1.0.2"), c = OldCopy("find c", "1.0.3"), u = OldCopy("find u", "1.0.4");
            Check(InstallFind.All(env).Count == 0, "nothing registered, running or linked: nothing found");
            using (RegistryKey k = Registry.CurrentUser.CreateSubKey(env.RegKey)) k.SetValue("InstallDir", a);
            using (RegistryKey k = Registry.CurrentUser.CreateSubKey(env.UninstallKey)) k.SetValue("InstallLocation", u);
            ShellLink.Create(Path.Combine(env.DesktopDir, "اختصار.lnk"), env.ExePath(c), "");
            ShellLink.Create(Path.Combine(env.DesktopDir, "again.lnk"), env.ExePath(a), "");
            ShellLink.Create(Path.Combine(env.DesktopDir, "notepad.lnk"), Path.Combine(Environment.SystemDirectory, "notepad.exe"), "");
            ShellLink.Create(Path.Combine(env.StartMenuDir, "deep", "gone.lnk"), P("gone", DummyExe), "");
            Process p = Start(b, "");
            List<InstallCopy> f = InstallFind.All(env);
            string list = ""; foreach (InstallCopy x in f) list += x.Source + "=" + x.Version + " ";
            Check(list == "registry=1.0.1 process=1.0.2 shortcut=1.0.3 uninstall=1.0.4 ", "found in order, each once: " + list);
            Check(InstallFind.RunningIn(env, b).Count == 1 && InstallFind.RunningIn(env, a).Count == 0, "running copy: only in its own folder");
            p.Kill(); p.WaitForExit();
            InstallEnv other = Env(); other.OnlyUnder = P("elsewhere");
            Check(InstallFind.All(other).Count == 0, "copies outside the test folder are never seen");
            Put(Path.Combine(a, ".git"), "gitdir: x");
            Check(InstallFind.IsCheckout(a) && !InstallFind.IsCheckout(b), "source checkouts are recognised");
        }

        static void Closing(InstallEnv env)
        {
            string d = OldCopy("closing", Old);
            Process p = Start(d, "listen");
            Check(ProgramExit.Ask(p.Id, 5000, null) == ExitAnswer.Closed && p.HasExited, "new program: exits when asked");
            p = Start(d, "refuse");
            Check(ProgramExit.Ask(p.Id, 5000, null) == ExitAnswer.Refused && !p.HasExited, "new program: the user cancels → refused, still running");
            Check(Engine(env, New).CopyFiles(d) == SetupCode.ProgramOpen && InstallFind.At(env, d).Version == Old, "while it runs nothing is replaced");
            Check(ProgramExit.Ask(p.Id, 5000, null) == ExitAnswer.Closed, "new program: asked again → exits");
            p = Start(d, "");
            Check(!ProgramExit.CanAsk(p.Id) && ProgramExit.Ask(p.Id, 1000, null) == ExitAnswer.NoListener && !p.HasExited, "old program: cannot be asked, is not killed");
            Check(ProgramExit.Kill(p.Id) && p.HasExited && ProgramExit.Ask(p.Id, 100, null) == ExitAnswer.Closed, "old program: ended after confirmation");
            Check(Engine(env, New).CopyFiles(d) == SetupCode.Ok, "after it closed the replacement works");
        }

        static void SilentMode(InstallEnv env)
        {
            ClearReg(env); ClearLinks(env);
            Setup.Args a = Setup.Args.Parse(new[] { "/S", "/NODESKTOP" });
            SetupEngine e = Engine(env, New);
            Check(Setup.Silent(e, a) == SetupCode.Ok && InstallFind.At(env, env.DefaultDir).Version == New, "silent: fresh install into the default folder");
            Check(Links(env.DesktopDir) == 0 && Links(env.StartMenuDir) == 1, "silent: /NODESKTOP");
            Check(Setup.Silent(e, a) == SetupCode.SameInstalled, "silent: same version → code 7");
            Check(Setup.Silent(Engine(env, "1.2.4"), a) == SetupCode.NewerInstalled && InstallFind.At(env, env.DefaultDir).Version == New, "silent: newer installed → code 3, no downgrade");
            a.Reinstall = true;
            Check(Setup.Silent(e, a) == SetupCode.Ok && Setup.Silent(Engine(env, "1.2.4"), a) == SetupCode.NewerInstalled, "silent: /REINSTALL same version only");
            Process p = Start(env.DefaultDir, "");
            Check(Setup.Silent(Engine(env, "1.3.0"), a) == SetupCode.ProgramOpen && !p.HasExited, "silent: an old open program is not killed → code 4");
            p.Kill(); p.WaitForExit();
            p = Start(env.DefaultDir, "listen");
            Check(Setup.Silent(Engine(env, "1.3.0"), a) == SetupCode.Ok && InstallFind.At(env, env.DefaultDir).Version == "1.3.0", "silent: a new open program is asked to exit, then updated");
            Check(InstallFind.RegValue(env.UninstallKey, "DisplayVersion") == "1.3.0", "silent: «Apps» shows the new version");
            InstallFind.Register(env, env.DefaultDir, "1.3.1");
            Check(InstallFind.RegValue(env.UninstallKey, "DisplayVersion") == "1.3.1" && InstallFind.RegValue(env.RegKey, "Version") == "1.3.1", "the program (after an in-app update) refreshes the version");
        }

        static void Components(InstallEnv env)
        {
            string d = P("comp");
            SetupEngine e = Engine(env, New);
            e.CopyFiles(d);
            Check(InstallReplace.MissingComponents(e.SetupDir, d).Count == 0, "no «مكونات» next to the setup: nothing to copy");
            string main = P("download", "مكونات", "SQLEXPR_x64_ENU.exe");
            Put(main, new string('x', 3000000)); Put(P("download", "مكونات", "sub", "note.txt"), "n");
            Check(InstallReplace.MissingComponents(e.SetupDir, d).Count == 2, "«مكونات»: two files missing");
            long last = 0; e.Progress = delegate(long a, long b, string w) { if (w == "components") last = a * 100 / b; };
            Check(e.CopyComponents(d) == SetupCode.Ok && last == 100 && new FileInfo(Path.Combine(d, @"مكونات\SQLEXPR_x64_ENU.exe")).Length == new FileInfo(main).Length, "«مكونات» copied with progress");
            Check(InstallReplace.MissingComponents(e.SetupDir, d).Count == 0, "«مكونات»: same size → skipped");
            Put(Path.Combine(d, @"مكونات\SQLEXPR_x64_ENU.exe"), "short");
            Check(InstallReplace.MissingComponents(e.SetupDir, d).Count == 1, "«مكونات»: an interrupted copy is completed");
            e.SkipComponents = delegate { return true; };
            Check(e.CopyComponents(d) == SetupCode.Cancelled && Get(Path.Combine(d, @"مكونات\SQLEXPR_x64_ENU.exe")) == "short" && Directory.GetFiles(Path.Combine(d, "مكونات"), "*.part").Length == 0, "«مكونات»: skipping leaves no partial file");
            Directory.Delete(P("download", "مكونات"), true);
        }

        static void NeverConfigured(InstallEnv env)
        {
            ClearReg(env); ClearLinks(env);
            string inst = OldCopy("installed", Old), fresh = P("Downloads", "EngStores-1.2.5");
            Directory.CreateDirectory(fresh); File.WriteAllBytes(Path.Combine(fresh, DummyExe), Dummy(New));
            string exe = Path.Combine(fresh, DummyExe); string[] no = new string[0];
            Check(InstallOffer.Candidate(env, no, exe, New) == null, "fresh folder, nothing installed: no question");
            InstallFind.Register(env, inst, Old);
            InstallCopy c = InstallOffer.Candidate(env, no, exe, New);
            Check(c != null && c.Dir == inst && c.Version == Old, "fresh folder, older copy installed: offered");
            Check(InstallOffer.Candidate(env, new[] { "/data=x.mdb" }, exe, New) == null, "started with a data file: no question");
            Check(InstallOffer.Candidate(env, no, Path.Combine(fresh, "InstallerTests.exe"), New) == null, "a test program: no question");
            Check(InstallOffer.Candidate(env, no, exe, Old) == null && InstallOffer.Candidate(env, no, exe, "1.0") == null, "same or newer installed: no question");
            Check(InstallOffer.Candidate(env, no, Path.Combine(inst, DummyExe), New) == null, "a configured folder (settings.ini): no question");
            Put(Path.Combine(fresh, ".git"), "x");
            Check(InstallOffer.Candidate(env, no, exe, New) == null, "a source checkout: no question");
            File.Delete(Path.Combine(fresh, ".git"));
            InstallEnv real = Env(); real.AllowTemp = false;
            Check(InstallOffer.Candidate(real, no, exe, New) == null, "opened inside a zip (%TEMP%): no question");
            InstallOffer.Decline(env, fresh, New);
            Check(InstallOffer.Candidate(env, no, exe, New) == null, "«لا»: not asked again for this folder and version");
            Check(InstallOffer.Candidate(env, no, exe, "1.2.6") != null, "«لا»: a later version asks again");
            // the replacement itself (the same code the program runs)
            Put(Path.Combine(fresh, InstallEnv.GuideFile), "guide " + New); Put(Path.Combine(fresh, @"docs\manual\index.html"), "manual " + New); Put(Path.Combine(fresh, "notes.txt"), "x");
            List<string> files = InstallReplace.ProgramFiles(env, fresh);
            InstallReplace r = new InstallReplace(env);
            Check(files.Count == 3 && r.Run(fresh, files, inst) == ReplaceResult.Ok && InstallFind.At(env, inst).Version == New && UserFilesKept(inst) && !File.Exists(Path.Combine(inst, "notes.txt")), "replaced from the fresh folder: program files only");
        }

        static void Uninstall(InstallEnv env)
        {
            ClearReg(env); ClearLinks(env);
            string d = OldCopy("uninstall", Old), other = OldCopy("uninstall other", Old);
            ShellLink.Create(Path.Combine(env.DesktopDir, "other.lnk"), env.ExePath(other), "");
            SetupEngine e = Engine(env, New);
            e.CopyFiles(d); e.Finish(d, null, true, false);
            Process p = Start(d, "");
            Check(e.Uninstall(d) == SetupCode.ProgramOpen && File.Exists(env.ExePath(d)), "uninstall: refused while the program runs");
            p.Kill(); p.WaitForExit();
            Check(e.Uninstall(d) == SetupCode.Ok, "uninstall: done (" + e.Error + ")");
            Check(!File.Exists(env.ExePath(d)) && !File.Exists(Path.Combine(d, "Uninstall.exe")) && !Directory.Exists(Path.Combine(d, "docs")) && !File.Exists(Path.Combine(d, InstallEnv.GuideFile)) && !Directory.Exists(Path.Combine(d, InstallEnv.UpdateBackupDir)), "uninstall: program files removed");
            Check(UserFilesKept(d), "uninstall: settings, data, logo, backups and «مكونات» kept");
            Check(Links(env.StartMenuDir) == 0 && Links(env.DesktopDir) == 1 && File.Exists(env.ExePath(other)), "uninstall: its shortcuts removed, other copies untouched");
            Check(InstallFind.RegValue(env.RegKey, "InstallDir") == "" && InstallFind.RegValue(env.UninstallKey, "InstallLocation") == "", "uninstall: registry entries removed");
            Check(e.Uninstall(d) == SetupCode.NotInstalled, "uninstall: not installed → code 10");
            string n = P("uninstall clean");
            e.CopyFiles(n); e.Finish(n, null, false, false);
            Check(e.Uninstall(n) == SetupCode.Ok && !Directory.Exists(n), "uninstall: a folder without user files disappears");
        }

        // ------------------------------------------------------------ the exit request in the real main window
        class FakeView : UserControl, IView
        {
            public bool Allow;
            public bool CanLeave() { return Allow; }
        }

        static void Pump(int ms) { DateTime end = DateTime.Now.AddMilliseconds(ms); while (DateTime.Now < end) { Application.DoEvents(); Thread.Sleep(15); } }

        static void Listener(string mdb)
        {
            string err = Db.InitAccess(mdb); if (err != null) { Check(false, "data file: " + err); return; }
            Db.EnsureAppTables(); Branding.Load();
            Session.UserNo = 1; Session.UserName = "Test"; Session.LoadPerms(); Session.SetMode(false);
            Cache.LoadAll();
            ExitListener.Start();
            Check(ProgramExit.CanAsk(Pid), "listener: the running program can be asked");
            EventWaitHandle ask = EventWaitHandle.OpenExisting(ProgramExit.ExitEvent(Pid)), refused = EventWaitHandle.OpenExisting(ProgramExit.RefusedEvent(Pid));
            MainForm m = new MainForm(); m.StartPosition = FormStartPosition.Manual; m.WindowState = FormWindowState.Normal; m.Location = new Point(-4000, -4000); m.ShowInTaskbar = false;
            m.Show(); Pump(1500);
            FakeView v = new FakeView(); m.Show((Control)v);
            m.Hide();   // next to the clock
            ask.Set(); Pump(800);
            Check(!m.IsDisposed && m.Visible && refused.WaitOne(0), "listener: unsaved document, «إلغاء» → the window is shown, nothing closed, the setup is told");
            Form dlg = new Form(); dlg.StartPosition = FormStartPosition.Manual; dlg.Location = new Point(-4000, -4000); dlg.ShowInTaskbar = false;
            v.Allow = true;
            dlg.Shown += delegate { ask.Set(); Pump(600); dlg.Close(); };
            dlg.ShowDialog(m);
            Check(!m.IsDisposed && refused.WaitOne(0), "listener: an open dialog → refused, not closed under it");
            ask.Set(); Pump(800);
            Check(m.IsDisposed && !refused.WaitOne(0), "listener: nothing unsaved → exits the normal way");
            if (!m.IsDisposed) m.Dispose();
            Db.Close();
        }

        // ------------------------------------------------------------ pictures of the pages
        [DllImport("user32.dll")] static extern bool PrintWindow(IntPtr h, IntPtr hdc, uint flags);

        static void Shot(SetupForm f, string path)
        {
            Pump(250);
            using (Bitmap b = new Bitmap(f.Width, f.Height))
            {
                using (Graphics g = Graphics.FromImage(b)) { IntPtr h = g.GetHdc(); try { PrintWindow(f.Handle, h, 2); } finally { g.ReleaseHdc(h); } }
                b.Save(path, ImageFormat.Png);
            }
            Console.WriteLine(path);
        }

        static SetupForm Open(SetupEngine e, string dir, bool uninstall)
        {
            SetupForm f = new SetupForm(e, dir, uninstall);
            f.StartPosition = FormStartPosition.Manual; f.Location = new Point(-4000, -4000); f.ShowInTaskbar = false;
            f.Show();
            return f;
        }

        /// <summary>The pictures are published: no real folder or user name in them.</summary>
        static string Neutral(string path)
        {
            if (InstallEnv.IsUnder(path, P("Programs"))) return @"C:\Users\User\AppData\Local\Programs" + path.Substring(P("Programs").Length);
            return InstallEnv.IsUnder(path, root) ? "D:" + path.Substring(root.Length) : path;
        }

        static void ManualShots(InstallEnv env, string outDir)
        {
            Directory.CreateDirectory(outDir);
            SetupForm.ShowPath = Neutral;
            Tx.En = false;
            SetupEngine e = Engine(env, "1.2.1");
            using (SetupForm f = Open(e, null, false)) Shot(f, Path.Combine(outDir, "setup-new.png"));
            string d = OldCopy("Inventory Management", "1.2.0");
            InstallFind.Register(env, d, "1.2.0");
            using (SetupForm f = Open(e, null, false)) Shot(f, Path.Combine(outDir, "setup-update.png"));
            Process p = Start(d, "");
            using (SetupForm f = Open(e, null, false)) { f.Show(f.OpenPage); Shot(f, Path.Combine(outDir, "setup-open.png")); }
            p.Kill(); p.WaitForExit();
        }

        static void Shots(InstallEnv env, string outDir)
        {
            Directory.CreateDirectory(outDir);
            SetupForm.ShowPath = Neutral;
            string shown = OldCopy("Inventory Management", "1.2.0");
            foreach (bool english in new[] { false, true })
            {
                Tx.En = english;
                string s = english ? "_en.png" : "_ar.png";
                ClearReg(env); ClearLinks(env);
                SetupEngine e = Engine(env, "1.2.1");
                using (SetupForm f = Open(e, null, false)) Shot(f, Path.Combine(outDir, "01-new" + s));
                InstallFind.Register(env, shown, "1.2.0");
                using (SetupForm f = Open(e, null, false)) { Shot(f, Path.Combine(outDir, "02-update" + s)); f.OtherFolder(); Shot(f, Path.Combine(outDir, "14-other-folder" + s)); }
                string second = OldCopy("Copy 2", "1.1.0");
                ShellLink.Create(Path.Combine(env.DesktopDir, "x.lnk"), env.ExePath(second), "");
                Put(P("download", "مكونات", "SQLEXPR_x64_ENU.exe"), new string('x', 2500000));
                if (File.Exists(Path.Combine(shown, @"مكونات\SQLEXPR_x64_ENU.exe"))) Directory.Delete(Path.Combine(shown, "مكونات"), true);
                Process p = Start(shown, "listen");
                using (SetupForm f = Open(e, null, false))
                {
                    Shot(f, Path.Combine(outDir, "03-update-several-running-components" + s));
                    f.OldProgram = true; f.Show(f.OpenPage); Shot(f, Path.Combine(outDir, "07-open-old-program" + s));
                }
                p.Kill(); p.WaitForExit();
                Directory.Delete(P("download", "مكونات"), true); ClearLinks(env);
                p = Start(shown, "refuse");
                using (SetupForm f = Open(e, null, false))
                {
                    f.Show(f.OpenPage); Pump(1200); Shot(f, Path.Combine(outDir, "06-open-refused" + s));
                }
                p.Kill(); p.WaitForExit();
                using (SetupForm f = Open(Engine(env, "1.2.0"), null, false)) Shot(f, Path.Combine(outDir, "04-same" + s));
                using (SetupForm f = Open(Engine(env, "1.1.5"), null, false)) Shot(f, Path.Combine(outDir, "05-newer-installed" + s));
                using (SetupForm f = Open(e, null, false))
                {
                    f.Show(f.ProgressPage); Shot(f, Path.Combine(outDir, "08-progress" + s));
                    f.Show(f.DonePage); Shot(f, Path.Combine(outDir, "09-done" + s));
                    e.Error = ""; f.FailCode = SetupCode.Failed; f.Show(f.FailedPage); Shot(f, Path.Combine(outDir, "10-failed" + s));
                    f.Show(f.NotWritablePage); Shot(f, Path.Combine(outDir, "11-not-writable" + s));
                }
                using (SetupForm f = Open(e, shown, true))
                {
                    Shot(f, Path.Combine(outDir, "12-uninstall" + s));
                    f.Show(f.UninstalledPage); Shot(f, Path.Combine(outDir, "13-uninstalled" + s));
                }
            }
        }
    }
}
