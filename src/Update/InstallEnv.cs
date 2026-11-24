using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;

namespace Stores
{
    /// <summary>Names and places the installer works with (registry keys, shortcut folders, program file name).
    /// Everything is a field so the tests work on their own folders and keys and never on the real Desktop, Start menu
    /// or registry entries. The environment variable ENGSTORES_SETUP_TEST = a folder does the same for whole programs
    /// (setup and program started by hand): everything outside that folder is then ignored.
    /// Compiled into the program and into the setup (installer\build-setup.ps1): no other class of the program is used.</summary>
    class InstallEnv
    {
        public const string TestVariable = "ENGSTORES_SETUP_TEST";
        /// <summary>The files that belong to the program (besides the exe and docs\manual).</summary>
        public const string GuideFile = "دليل التشغيل.txt";
        public const string ServerGuideFile = "إعداد الخادم المجاني.txt";
        public const string ManualDir = @"docs\manual";
        /// <summary>The offline SQL Server files next to the setup (flash drive) and next to the program.</summary>
        public const string ComponentsDir = "مكونات";
        public const string ComponentsMain = "SQLEXPR_x64_ENU.exe";
        public const string BackupDirPrefix = "نسخ احتياطية";
        public const string UpdateBackupDir = ".update-backup";
        public const string UninstallExe = "Uninstall.exe";

        public string ExeName = "Inventory Management.exe";
        /// <summary>HKCU sub-key with InstallDir and Version (written by the setup and by the program at each normal start).</summary>
        public string RegKey = @"Software\EngStores\Setup";
        /// <summary>HKCU sub-key of «Apps» in the Windows settings.</summary>
        public string UninstallKey = @"Software\Microsoft\Windows\CurrentVersion\Uninstall\EngStores";
        /// <summary>Where the setup creates its shortcuts.</summary>
        public string DesktopDir, StartMenuDir;
        /// <summary>Folders searched for shortcuts that point to the program (Deep: with their sub-folders).</summary>
        public List<string> ShortcutDirs = new List<string>(), ShortcutDirsDeep = new List<string>();
        public string DefaultDir;
        /// <summary>Not null: only installations under this folder are seen (tests).</summary>
        public string OnlyUnder;
        /// <summary>Copies under %TEMP% (opened inside a zip) are not installations, except in tests.</summary>
        public bool AllowTemp;

        static InstallEnv current;
        public static InstallEnv Current
        {
            get { if (current == null) current = Create(Environment.GetEnvironmentVariable(TestVariable)); return current; }
            set { current = value; }
        }

        public static InstallEnv Create(string testRoot)
        {
            InstallEnv e = new InstallEnv();
            if (!string.IsNullOrEmpty(testRoot))
            {
                testRoot = Path.GetFullPath(testRoot);
                e.OnlyUnder = testRoot; e.AllowTemp = true;
                e.RegKey = @"Software\EngStoresTest\Setup"; e.UninstallKey = @"Software\EngStoresTest\Uninstall";
                e.DesktopDir = Path.Combine(testRoot, "Desktop"); e.StartMenuDir = Path.Combine(testRoot, "StartMenu");
                e.DefaultDir = Path.Combine(testRoot, "Programs", "Inventory Management");
                e.ShortcutDirs.Add(e.DesktopDir); e.ShortcutDirsDeep.Add(e.StartMenuDir);
                return e;
            }
            e.DesktopDir = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
            e.StartMenuDir = Environment.GetFolderPath(Environment.SpecialFolder.Programs);
            e.DefaultDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", "Inventory Management");
            e.ShortcutDirs.Add(e.DesktopDir);
            e.ShortcutDirs.Add(Environment.GetFolderPath(Environment.SpecialFolder.CommonDesktopDirectory));
            string pinned = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), @"Microsoft\Internet Explorer\Quick Launch\User Pinned");
            e.ShortcutDirs.Add(Path.Combine(pinned, "TaskBar")); e.ShortcutDirs.Add(Path.Combine(pinned, "StartMenu"));
            e.ShortcutDirsDeep.Add(Environment.GetFolderPath(Environment.SpecialFolder.StartMenu));
            e.ShortcutDirsDeep.Add(Environment.GetFolderPath(Environment.SpecialFolder.CommonStartMenu));
            return e;
        }

        // ------------------------------------------------------------ paths
        /// <summary>Full path without a trailing separator (null when it is not a path).</summary>
        public static string Norm(string dir)
        {
            if (string.IsNullOrEmpty(dir)) return null;
            try
            {
                string p = Path.GetFullPath(dir.Trim().Trim('"'));
                string root = Path.GetPathRoot(p);
                if (p.IndexOf('~') >= 0)   // 8.3 names (%TEMP% of some accounts)
                {
                    StringBuilder sb = new StringBuilder(1024);
                    int n = GetLongPathName(p, sb, sb.Capacity);
                    if (n > 0 && n < sb.Capacity) p = sb.ToString();
                }
                return p.Length > root.Length ? p.TrimEnd('\\', '/') : p;
            }
            catch { return null; }
        }

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        static extern int GetLongPathName(string path, StringBuilder longPath, int size);

        public static bool SamePath(string a, string b)
        {
            a = Norm(a); b = Norm(b);
            return a != null && b != null && string.Equals(a, b, StringComparison.OrdinalIgnoreCase);
        }

        public static bool IsUnder(string path, string dir)
        {
            path = Norm(path); dir = Norm(dir);
            if (path == null || dir == null) return false;
            return SamePath(path, dir) || path.StartsWith(dir.TrimEnd('\\') + "\\", StringComparison.OrdinalIgnoreCase);
        }

        public string ExePath(string dir) { return Path.Combine(dir, ExeName); }
    }
}
