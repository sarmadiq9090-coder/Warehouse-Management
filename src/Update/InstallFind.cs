using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32;

namespace Stores
{
    /// <summary>One copy of the program found on this computer.</summary>
    class InstallCopy
    {
        public string Dir;
        /// <summary>"" = unknown.</summary>
        public string Version = "";
        /// <summary>registry, process, shortcut or uninstall.</summary>
        public string Source;
    }

    /// <summary>Finds the copies of the program on this computer, in this order: the registry value written by the setup and
    /// by the program at each normal start, a running program of this session, shortcuts, the «Apps» entry.
    /// Compiled into the program and into the setup.</summary>
    static class InstallFind
    {
        public static List<InstallCopy> All(InstallEnv env)
        {
            List<InstallCopy> res = new List<InstallCopy>();
            Add(res, env, RegValue(env.RegKey, "InstallDir"), "registry");
            foreach (KeyValuePair<int, string> p in Running(env)) Add(res, env, Path.GetDirectoryName(p.Value), "process");
            foreach (KeyValuePair<string, string> s in Shortcuts(env)) Add(res, env, Path.GetDirectoryName(s.Value), "shortcut");
            Add(res, env, RegValue(env.UninstallKey, "InstallLocation"), "uninstall");
            return res;
        }

        static void Add(List<InstallCopy> res, InstallEnv env, string dir, string source)
        {
            dir = InstallEnv.Norm(dir);
            if (dir == null) return;
            foreach (InstallCopy c in res) if (InstallEnv.SamePath(c.Dir, dir)) return;
            InstallCopy n = At(env, dir);
            if (n == null) return;
            n.Source = source; res.Add(n);
        }

        /// <summary>The copy in this folder, or null when the program is not there (or the folder may not count: %TEMP%).</summary>
        public static InstallCopy At(InstallEnv env, string dir)
        {
            dir = InstallEnv.Norm(dir);
            if (dir == null) return null;
            if (env.OnlyUnder != null && !InstallEnv.IsUnder(dir, env.OnlyUnder)) return null;
            if (!env.AllowTemp && IsTemp(dir)) return null;
            try { if (!File.Exists(env.ExePath(dir))) return null; } catch { return null; }
            InstallCopy c = new InstallCopy(); c.Dir = dir;
            c.Version = ExeVersion(env.ExePath(dir));
            if (c.Version.Length == 0 && InstallEnv.SamePath(RegValue(env.RegKey, "InstallDir"), dir)) c.Version = RegValue(env.RegKey, "Version");
            return c;
        }

        public static bool IsTemp(string dir) { return InstallEnv.IsUnder(dir, Path.GetTempPath()); }

        /// <summary>A source checkout (built by a developer) is never registered as the installed program.</summary>
        public static bool IsCheckout(string dir)
        {
            string git = Path.Combine(dir, ".git");
            return Directory.Exists(git) || File.Exists(git);
        }

        /// <summary>Nothing was ever set up in this program folder (a freshly downloaded copy).</summary>
        public static bool NeverConfigured(string dir) { return !File.Exists(Path.Combine(dir, "settings.ini")); }

        // ------------------------------------------------------------ versions
        public static Version Parse(string s)
        {
            s = (s ?? "").Trim().TrimStart('v', 'V');
            Version v;
            if (!Version.TryParse(s.IndexOf('.') >= 0 ? s : s + ".0", out v)) return new Version(0, 0, 0, 0);
            return new Version(v.Major, v.Minor, Math.Max(0, v.Build), Math.Max(0, v.Revision));
        }

        /// <summary>&lt; 0: a is older than b. An unknown version counts as the oldest. 1.2 = 1.2.0.</summary>
        public static int Compare(string a, string b) { return Parse(a).CompareTo(Parse(b)); }

        /// <summary>Program.Version of a program file, read from its metadata without running or locking it ("" = not our program).
        /// A helper domain is used because two programs with the same name cannot be loaded for reflection into one domain.</summary>
        public static string ExeVersion(string exe)
        {
            AppDomain d = null;
            try
            {
                string me = typeof(InstallFind).Assembly.Location;
                AppDomainSetup s = new AppDomainSetup();
                s.ApplicationBase = Path.GetDirectoryName(me);
                d = AppDomain.CreateDomain("version", null, s);
                VersionReader r = (VersionReader)d.CreateInstanceFromAndUnwrap(me, typeof(VersionReader).FullName);
                return r.Read(exe) ?? "";
            }
            catch { return ""; }
            finally { if (d != null) try { AppDomain.Unload(d); } catch { } }
        }

        class VersionReader : MarshalByRefObject
        {
            public string Read(string exe)
            {
                Type t = Assembly.ReflectionOnlyLoad(File.ReadAllBytes(exe)).GetType("Stores.Program", false);
                FieldInfo f = t == null ? null : t.GetField("Version", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
                return f == null || !f.IsLiteral ? null : f.GetRawConstantValue() as string;
            }
        }

        // ------------------------------------------------------------ registry
        public static string RegValue(string key, string name)
        {
            try { using (RegistryKey k = Registry.CurrentUser.OpenSubKey(key)) return k == null ? "" : Convert.ToString(k.GetValue(name, "")); }
            catch { return ""; }
        }

        /// <summary>Remembers this folder as the installed program (and shows the new version in «Apps»).</summary>
        public static void Register(InstallEnv env, string dir, string version)
        {
            try
            {
                if (!InstallEnv.SamePath(RegValue(env.RegKey, "InstallDir"), dir) || RegValue(env.RegKey, "Version") != version)
                    using (RegistryKey k = Registry.CurrentUser.CreateSubKey(env.RegKey)) { k.SetValue("InstallDir", dir); k.SetValue("Version", version); }
                if (InstallEnv.SamePath(RegValue(env.UninstallKey, "InstallLocation"), dir) && RegValue(env.UninstallKey, "DisplayVersion") != version)
                    using (RegistryKey k = Registry.CurrentUser.OpenSubKey(env.UninstallKey, true)) if (k != null) k.SetValue("DisplayVersion", version);
            }
            catch { }
        }

        public static void Unregister(InstallEnv env, string dir)
        {
            try { if (InstallEnv.SamePath(RegValue(env.RegKey, "InstallDir"), dir)) Registry.CurrentUser.DeleteSubKeyTree(env.RegKey, false); } catch { }
            try { if (InstallEnv.SamePath(RegValue(env.UninstallKey, "InstallLocation"), dir)) Registry.CurrentUser.DeleteSubKeyTree(env.UninstallKey, false); } catch { }
        }

        // ------------------------------------------------------------ running programs
        /// <summary>Running programs of this Windows session: process id → full path of the program file.</summary>
        public static List<KeyValuePair<int, string>> Running(InstallEnv env)
        {
            List<KeyValuePair<int, string>> res = new List<KeyValuePair<int, string>>();
            int session; using (Process me = Process.GetCurrentProcess()) session = me.SessionId;
            foreach (Process p in Process.GetProcessesByName(Path.GetFileNameWithoutExtension(env.ExeName)))
                using (p)
                {
                    try
                    {
                        if (p.SessionId != session) continue;   // other users are never touched
                        string path = ImagePath(p.Id);
                        if (path == null || (env.OnlyUnder != null && !InstallEnv.IsUnder(path, env.OnlyUnder))) continue;
                        res.Add(new KeyValuePair<int, string>(p.Id, path));
                    }
                    catch { }
                }
            return res;
        }

        /// <summary>Process ids of the program running from this folder (this session; this process itself not counted).</summary>
        public static List<int> RunningIn(InstallEnv env, string dir)
        {
            List<int> res = new List<int>();
            int me; using (Process p = Process.GetCurrentProcess()) me = p.Id;
            foreach (KeyValuePair<int, string> r in Running(env))
                if (r.Key != me && InstallEnv.SamePath(r.Value, env.ExePath(dir))) res.Add(r.Key);
            return res;
        }

        [DllImport("kernel32.dll", SetLastError = true)] static extern IntPtr OpenProcess(uint access, bool inherit, int pid);
        [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)] static extern bool QueryFullProcessImageName(IntPtr h, uint flags, StringBuilder name, ref int size);
        [DllImport("kernel32.dll")] static extern bool CloseHandle(IntPtr h);

        static string ImagePath(int pid)
        {
            IntPtr h = OpenProcess(0x1000, false, pid);   // PROCESS_QUERY_LIMITED_INFORMATION
            if (h == IntPtr.Zero) return null;
            try
            {
                StringBuilder sb = new StringBuilder(1024); int n = sb.Capacity;
                return QueryFullProcessImageName(h, 0, sb, ref n) ? sb.ToString() : null;
            }
            finally { CloseHandle(h); }
        }

        // ------------------------------------------------------------ shortcuts
        /// <summary>Shortcuts to the program: .lnk path → target path.</summary>
        public static List<KeyValuePair<string, string>> Shortcuts(InstallEnv env)
        {
            List<KeyValuePair<string, string>> res = new List<KeyValuePair<string, string>>();
            foreach (string d in env.ShortcutDirs) Scan(res, env, d, 0);
            foreach (string d in env.ShortcutDirsDeep) Scan(res, env, d, 6);
            return res;
        }

        static void Scan(List<KeyValuePair<string, string>> res, InstallEnv env, string dir, int depth)
        {
            if (string.IsNullOrEmpty(dir)) return;
            try
            {
                if (!Directory.Exists(dir)) return;
                foreach (string f in Directory.GetFiles(dir, "*.lnk"))
                {
                    string t = ShellLink.Target(f);
                    if (t.Length == 0) continue;
                    try { if (!string.Equals(Path.GetFileName(t), env.ExeName, StringComparison.OrdinalIgnoreCase)) continue; } catch { continue; }
                    bool have = false;
                    foreach (KeyValuePair<string, string> r in res) if (string.Equals(r.Key, f, StringComparison.OrdinalIgnoreCase)) have = true;
                    if (!have) res.Add(new KeyValuePair<string, string>(f, t));
                }
                if (depth > 0) foreach (string sub in Directory.GetDirectories(dir)) Scan(res, env, sub, depth - 1);
            }
            catch { }   // a folder this user may not read
        }
    }
}
