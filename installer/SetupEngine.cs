using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Text;
using Microsoft.Win32;

namespace Stores.Installer
{
    /// <summary>Exit codes of «Inventory Management Setup.exe» (silent mode and the elevated helper).</summary>
    static class SetupCode
    {
        public const int Ok = 0;
        /// <summary>Copying failed, the previous version was restored.</summary>
        public const int Failed = 1;
        public const int BadArguments = 2;
        /// <summary>The installed version is newer: nothing was changed.</summary>
        public const int NewerInstalled = 3;
        /// <summary>The program is open and did not exit: nothing was changed.</summary>
        public const int ProgramOpen = 4;
        public const int NotWritable = 5;
        public const int Cancelled = 6;
        /// <summary>The same version is installed already (use /REINSTALL): nothing was changed.</summary>
        public const int SameInstalled = 7;
        /// <summary>Copying failed and some old files could not be restored (copies are in .update-backup).</summary>
        public const int RollbackIncomplete = 8;
        /// <summary>The program was installed, but the files of «مكونات» could not be copied.</summary>
        public const int ComponentsFailed = 9;
        /// <summary>Uninstall: the program is not in that folder.</summary>
        public const int NotInstalled = 10;
    }

    /// <summary>The work of the setup without any window: unpack, replace (InstallReplace), shortcuts, registry, uninstall.</summary>
    class SetupEngine
    {
        public readonly InstallEnv Env;
        /// <summary>Version of the program inside this setup.</summary>
        public readonly string Version;
        /// <summary>Opens the embedded zip.</summary>
        readonly Func<Stream> payload;
        /// <summary>The running setup: copied as Uninstall.exe; «مكونات» is looked for next to it.</summary>
        public readonly string SetupExe;
        public string Error = "", BackupDir = "";
        /// <summary>done, total, what ("files" or "components").</summary>
        public Action<long, long, string> Progress;
        /// <summary>Asked while «مكونات» is copied: true = skip the rest.</summary>
        public Func<bool> SkipComponents;
        /// <summary>Tests: called before each file is replaced.</summary>
        public Action<string> BeforeFile = null;
        public int WaitInUseMs = 6000;

        public SetupEngine(InstallEnv env, string version, Func<Stream> payload, string setupExe)
        {
            Env = env; Version = version; this.payload = payload; SetupExe = setupExe;
        }

        public string SetupDir { get { return string.IsNullOrEmpty(SetupExe) ? "" : Path.GetDirectoryName(SetupExe); } }

        // ------------------------------------------------------------ install / update
        /// <summary>Files only: unpack to %TEMP%, replace with backup and rollback. Also run elevated (Setup /files).</summary>
        public int CopyFiles(string target)
        {
            string stage = Path.Combine(Path.GetTempPath(), "EngStoresSetup-" + Guid.NewGuid().ToString("N").Substring(0, 10));
            try
            {
                List<string> files = Unpack(stage);
                if (!File.Exists(Env.ExePath(stage))) { Error = Env.ExeName; return SetupCode.Failed; }
                string un = Path.Combine(target, InstallEnv.UninstallExe);
                if (!string.IsNullOrEmpty(SetupExe) && File.Exists(SetupExe) && !InstallEnv.SamePath(SetupExe, un))
                {
                    File.Copy(SetupExe, Path.Combine(stage, InstallEnv.UninstallExe), true);
                    files.Add(InstallEnv.UninstallExe);
                }
                InstallReplace r = new InstallReplace(Env);
                r.WaitInUseMs = WaitInUseMs; r.BeforeFile = BeforeFile;
                r.Progress = delegate(int d, int t) { if (Progress != null) Progress(d, t, "files"); };
                ReplaceResult res = r.Run(stage, files, target);
                Error = r.Error; BackupDir = r.BackupDir ?? "";
                switch (res)
                {
                    case ReplaceResult.Ok: return SetupCode.Ok;
                    case ReplaceResult.NotWritable: return SetupCode.NotWritable;
                    case ReplaceResult.InUse: return SetupCode.ProgramOpen;
                    case ReplaceResult.RollbackIncomplete: return SetupCode.RollbackIncomplete;
                    default: return SetupCode.Failed;
                }
            }
            catch (Exception ex) { Error = ex.Message; return SetupCode.Failed; }
            finally { try { Directory.Delete(stage, true); } catch { } }
        }

        /// <summary>«مكونات» next to the setup → program folder.</summary>
        public int CopyComponents(string target)
        {
            try
            {
                List<string> files = InstallReplace.MissingComponents(SetupDir, target);
                if (files.Count == 0) return SetupCode.Ok;
                bool all = InstallReplace.CopyComponents(SetupDir, files, target, delegate(long d, long t)
                {
                    if (Progress != null) Progress(d, t, "components");
                    return SkipComponents == null || !SkipComponents();
                });
                return all ? SetupCode.Ok : SetupCode.Cancelled;
            }
            catch (Exception ex) { Error = ex.Message; return SetupCode.ComponentsFailed; }
        }

        /// <summary>After the files: settings of the old copy (another folder), registry, shortcuts. oldDir may be null.</summary>
        public void Finish(string target, string oldDir, bool desktop, bool english)
        {
            if (oldDir != null && !InstallEnv.SamePath(oldDir, target)) InstallReplace.CarrySettings(oldDir, target);
            WriteUninstallEntry(target);
            InstallFind.Register(Env, target, Version);
            Shortcuts(target, oldDir, desktop, english);
        }

        List<string> Unpack(string stage)
        {
            List<string> files = new List<string>();
            using (Stream s = payload())
            using (ZipArchive z = new ZipArchive(s, ZipArchiveMode.Read, false, Encoding.UTF8))
                foreach (ZipArchiveEntry e in z.Entries)
                {
                    string rel = e.FullName.Replace('/', '\\');
                    if (rel.EndsWith("\\") || InstallReplace.IsProtected(rel)) continue;
                    string dst = Path.Combine(stage, rel);
                    if (!InstallEnv.IsUnder(dst, stage)) continue;
                    Directory.CreateDirectory(Path.GetDirectoryName(dst));
                    e.ExtractToFile(dst, true);
                    files.Add(rel);
                }
            return files;
        }

        // ------------------------------------------------------------ shortcuts
        public static string ShortcutName(bool english) { return english ? "Inventory Management" : "نظام إدارة المخازن"; }

        void Shortcuts(string target, string oldDir, bool desktop, bool english)
        {
            string exe = Env.ExePath(target);
            List<KeyValuePair<string, string>> found = InstallFind.Shortcuts(Env);
            bool onDesktop = false, inMenu = false;
            foreach (KeyValuePair<string, string> s in found)
            {
                bool here = InstallEnv.SamePath(s.Value, exe);
                if (!here && oldDir != null && InstallEnv.SamePath(s.Value, Env.ExePath(oldDir)))
                    try { ShellLink.Retarget(s.Key, exe); here = true; } catch { }   // the common Desktop may be read-only
                if (!here) continue;
                foreach (string d in Env.ShortcutDirsDeep) if (InstallEnv.IsUnder(s.Key, d)) inMenu = true;
                if (!InMenu(s.Key)) onDesktop |= !s.Key.Contains(@"\Quick Launch\");
            }
            if (desktop && !onDesktop) Link(Env.DesktopDir, exe, english);
            if (!inMenu) Link(Env.StartMenuDir, exe, english);
        }

        bool InMenu(string lnk)
        {
            foreach (string d in Env.ShortcutDirsDeep) if (InstallEnv.IsUnder(lnk, d)) return true;
            return false;
        }

        static void Link(string dir, string exe, bool english)
        {
            if (string.IsNullOrEmpty(dir)) return;
            try
            {
                string name = ShortcutName(english), lnk = Path.Combine(dir, name + ".lnk");
                for (int i = 2; File.Exists(lnk); i++) lnk = Path.Combine(dir, name + " (" + i + ").lnk");   // another copy's shortcut stays
                ShellLink.Create(lnk, exe, "Inventory Management Software • نظام إدارة المخازن");
            }
            catch { }
        }

        // ------------------------------------------------------------ «Apps» in the Windows settings
        void WriteUninstallEntry(string target)
        {
            try
            {
                string un = Path.Combine(target, InstallEnv.UninstallExe);
                long size = 0;
                foreach (string f in InstallReplace.ProgramFiles(Env, target)) try { size += new FileInfo(Path.Combine(target, f)).Length; } catch { }
                using (RegistryKey k = Registry.CurrentUser.CreateSubKey(Env.UninstallKey))
                {
                    k.SetValue("DisplayName", "نظام إدارة المخازن • Inventory Management Software");
                    k.SetValue("DisplayVersion", Version);
                    k.SetValue("DisplayIcon", Env.ExePath(target));
                    k.SetValue("Publisher", "Inventory Management Software");
                    k.SetValue("InstallLocation", target);
                    k.SetValue("InstallDate", DateTime.Today.ToString("yyyyMMdd"));
                    k.SetValue("UninstallString", "\"" + un + "\" /uninstall");
                    k.SetValue("QuietUninstallString", "\"" + un + "\" /uninstall /S");
                    k.SetValue("EstimatedSize", (int)(size / 1024), RegistryValueKind.DWord);
                    k.SetValue("NoModify", 1, RegistryValueKind.DWord);
                    k.SetValue("NoRepair", 1, RegistryValueKind.DWord);
                }
            }
            catch { }
        }

        // ------------------------------------------------------------ uninstall
        /// <summary>Removes the program files and its shortcuts. Data, settings, logo, backups and «مكونات» stay.</summary>
        public int Uninstall(string dir)
        {
            dir = InstallEnv.Norm(dir);
            if (dir == null || (!File.Exists(Env.ExePath(dir)) && !File.Exists(Path.Combine(dir, InstallEnv.UninstallExe)))) return SetupCode.NotInstalled;
            if (InstallFind.RunningIn(Env, dir).Count > 0) return SetupCode.ProgramOpen;
            if (!InstallReplace.CanWrite(dir)) return SetupCode.NotWritable;

            string exe = Env.ExePath(dir);
            // the exe first: if it is in use nothing else is removed
            try { if (File.Exists(exe)) { File.SetAttributes(exe, FileAttributes.Normal); File.Delete(exe); } }
            catch (Exception ex) { Error = ex.Message; return SetupCode.ProgramOpen; }
            foreach (KeyValuePair<string, string> s in InstallFind.Shortcuts(Env))
                if (InstallEnv.SamePath(s.Value, exe)) try { File.Delete(s.Key); } catch { }
            foreach (string f in InstallReplace.ProgramFiles(Env, dir))
                if (!InstallReplace.IsProtected(f)) Delete(Path.Combine(dir, f));
            Delete(Path.Combine(dir, InstallEnv.UninstallExe));
            RemoveTree(Path.Combine(dir, InstallEnv.UpdateBackupDir));   // copies of old program files
            foreach (string d in new[] { Path.Combine(dir, InstallEnv.ManualDir, "img"), Path.Combine(dir, InstallEnv.ManualDir), Path.Combine(dir, "docs"), dir })
                try { if (Directory.Exists(d) && Directory.GetFileSystemEntries(d).Length == 0) Directory.Delete(d, false); } catch { }
            InstallFind.Unregister(Env, dir);
            return SetupCode.Ok;
        }

        static void Delete(string f)
        {
            for (int i = 0; i < 10; i++)
            {
                try { if (File.Exists(f)) { File.SetAttributes(f, FileAttributes.Normal); File.Delete(f); } return; }
                catch { System.Threading.Thread.Sleep(200); }
            }
        }

        static void RemoveTree(string dir)
        {
            try
            {
                if (!Directory.Exists(dir)) return;
                foreach (string f in Directory.GetFiles(dir)) if (!InstallReplace.IsProtected(Path.GetFileName(f))) Delete(f);
                foreach (string d in Directory.GetDirectories(dir)) RemoveTree(d);
                if (Directory.GetFileSystemEntries(dir).Length == 0) Directory.Delete(dir, false);
            }
            catch { }
        }
    }
}
