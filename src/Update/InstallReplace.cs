using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;

namespace Stores
{
    enum ReplaceResult
    {
        Ok,
        /// <summary>Nothing was changed: no write permission in the folder.</summary>
        NotWritable,
        /// <summary>Nothing was changed: a program file is in use (another user or computer has it open).</summary>
        InUse,
        /// <summary>Copying failed; the previous files were put back.</summary>
        RolledBack,
        /// <summary>Copying failed and some previous files could not be put back (Error lists them; copies are in BackupDir).</summary>
        RollbackIncomplete
    }

    /// <summary>Replaces the program files of a folder by those of another one: backup to .update-backup first, full rollback on
    /// any error (the idea of apply-update.ps1). Data, settings, logo and backups of the folder are never touched.
    /// Used by the setup (files unpacked from it) and by a freshly downloaded program (InstallOffer).</summary>
    class InstallReplace
    {
        readonly InstallEnv env;
        public string Error = "", BackupDir;
        /// <summary>done, total (files).</summary>
        public Action<int, int> Progress = null;
        /// <summary>Tests: called before each file is replaced.</summary>
        public Action<string> BeforeFile = null;
        /// <summary>How long a file in use is waited for (a closing program, an antivirus).</summary>
        public int WaitInUseMs = 6000;

        public InstallReplace(InstallEnv env) { this.env = env; }

        // ------------------------------------------------------------ what is never touched
        /// <summary>Files that belong to the user, not to the program (path relative to the program folder).</summary>
        public static bool IsProtected(string rel)
        {
            rel = rel.Replace('/', '\\').Trim('\\');
            string first = rel.Split('\\')[0], name = Path.GetFileName(rel), ext = Path.GetExtension(rel).ToLowerInvariant();
            if (Eq(name, "settings.ini") || Eq(name, "errors.log") || Eq(rel, "logo.png")) return true;
            if (ext == ".amr" || ext == ".mdb" || ext == ".accdb" || ext == ".ldb" || ext == ".laccdb" || ext == ".bak") return true;
            return Eq(first, InstallEnv.ComponentsDir) || first.StartsWith(InstallEnv.BackupDirPrefix, StringComparison.Ordinal) || Eq(first, InstallEnv.UpdateBackupDir);
        }

        static bool Eq(string a, string b) { return string.Equals(a, b, StringComparison.OrdinalIgnoreCase); }

        /// <summary>The program files of a folder (relative paths): the exe, the two guides and the manual. Nothing else.</summary>
        public static List<string> ProgramFiles(InstallEnv env, string dir)
        {
            List<string> res = new List<string>();
            foreach (string f in new[] { env.ExeName, InstallEnv.GuideFile, InstallEnv.ServerGuideFile })
                if (File.Exists(Path.Combine(dir, f))) res.Add(f);
            string manual = Path.Combine(dir, InstallEnv.ManualDir);
            if (Directory.Exists(manual))
                foreach (string f in Directory.GetFiles(manual, "*", SearchOption.AllDirectories)) res.Add(f.Substring(dir.TrimEnd('\\').Length + 1));
            return res;
        }

        // ------------------------------------------------------------ checks
        public static bool CanWrite(string dir)
        {
            string d = dir;
            try
            {
                while (!Directory.Exists(d)) { d = Path.GetDirectoryName(d); if (string.IsNullOrEmpty(d)) return false; }
                string probe = Path.Combine(d, ".write_test" + Guid.NewGuid().ToString("N").Substring(0, 6));
                File.WriteAllText(probe, "x"); File.Delete(probe);
                return true;
            }
            catch { return false; }
        }

        /// <summary>The program file can be replaced now (nobody runs it).</summary>
        bool ExeFree(string exe)
        {
            if (!File.Exists(exe)) return true;
            for (int waited = 0; ; waited += 300)
            {
                try
                {
                    ClearReadOnly(exe);
                    using (new FileStream(exe, FileMode.Open, FileAccess.ReadWrite, FileShare.None)) return true;
                }
                catch (IOException) { }
                catch (UnauthorizedAccessException) { }   // Windows answers this for a running program
                if (waited >= WaitInUseMs) return false;
                Thread.Sleep(300);
            }
        }

        static void ClearReadOnly(string f)
        {
            FileAttributes a = File.GetAttributes(f);
            if ((a & FileAttributes.ReadOnly) != 0) File.SetAttributes(f, a & ~FileAttributes.ReadOnly);
        }

        // ------------------------------------------------------------ the replacement
        /// <summary>Copies files (relative paths) from source over target.</summary>
        public ReplaceResult Run(string source, IList<string> files, string target)
        {
            source = InstallEnv.Norm(source); target = InstallEnv.Norm(target);
            List<string> todo = new List<string>();
            foreach (string f in files)
            {
                string rel = f.Replace('/', '\\').Trim('\\');
                if (rel.Length == 0 || IsProtected(rel)) continue;
                if (!InstallEnv.IsUnder(Path.Combine(target, rel), target)) continue;   // «..\» in a zip
                todo.Add(rel);
            }
            if (!CanWrite(target)) { Error = target; return ReplaceResult.NotWritable; }
            if (!ExeFree(env.ExePath(target))) { Error = env.ExePath(target); return ReplaceResult.InUse; }

            List<string> replaced = new List<string>(), created = new List<string>(), dirs = new List<string>();
            BackupDir = Path.Combine(target, InstallEnv.UpdateBackupDir, DateTime.Now.ToString("yyyyMMdd-HHmmss"));
            string temp = null;
            try
            {
                MakeDir(target, dirs);
                foreach (string rel in todo)
                {
                    string dst = Path.Combine(target, rel);
                    if (!File.Exists(dst)) continue;
                    string b = Path.Combine(BackupDir, rel);
                    Directory.CreateDirectory(Path.GetDirectoryName(b));
                    File.Copy(dst, b, true);
                }
                int n = 0;
                foreach (string rel in todo)
                {
                    if (BeforeFile != null) BeforeFile(rel);
                    string dst = Path.Combine(target, rel);
                    MakeDir(Path.GetDirectoryName(dst), dirs);
                    bool had = File.Exists(dst);
                    temp = dst + ".new";
                    Copy(Path.Combine(source, rel), temp);
                    if (had) { Retry(delegate { ClearReadOnly(dst); File.Delete(dst); }); replaced.Add(rel); }
                    else created.Add(rel);
                    Retry(delegate { File.Move(temp, dst); });
                    temp = null;
                    if (Progress != null) Progress(++n, todo.Count);
                }
                return ReplaceResult.Ok;
            }
            catch (Exception ex)
            {
                Error = ex.Message;
                if (temp != null) try { File.Delete(temp); } catch { }
                List<string> lost = new List<string>();
                foreach (string rel in replaced)
                {
                    string dst = Path.Combine(target, rel), b = Path.Combine(BackupDir, rel);
                    try { Retry(delegate { if (File.Exists(dst)) ClearReadOnly(dst); File.Copy(b, dst, true); }); }
                    catch { lost.Add(rel); }
                }
                foreach (string rel in created) try { File.Delete(Path.Combine(target, rel)); } catch { }
                dirs.Reverse();
                foreach (string d in dirs) try { Directory.Delete(d, false); } catch { }
                if (lost.Count == 0) return ReplaceResult.RolledBack;
                Error += "\n" + string.Join("\n", lost.ToArray());
                return ReplaceResult.RollbackIncomplete;
            }
        }

        static void MakeDir(string dir, List<string> made)
        {
            if (Directory.Exists(dir)) return;
            string parent = Path.GetDirectoryName(dir);
            if (!string.IsNullOrEmpty(parent)) MakeDir(parent, made);
            Directory.CreateDirectory(dir);
            made.Add(dir);
        }

        static void Copy(string src, string dst)
        {
            File.Copy(src, dst, true);
            File.SetAttributes(dst, FileAttributes.Normal);
            if (new FileInfo(dst).Length != new FileInfo(src).Length) throw new IOException(dst);
        }

        void Retry(Action a)
        {
            for (int waited = 0; ; waited += 250)
            {
                try { a(); return; }
                catch (IOException) { if (waited >= WaitInUseMs) throw; }
                catch (UnauthorizedAccessException) { if (waited >= WaitInUseMs) throw; }
                Thread.Sleep(250);
            }
        }

        // ------------------------------------------------------------ settings of an old copy in another folder
        /// <summary>A new folder gets the connection settings and the logo of the old copy when it has none.</summary>
        public static void CarrySettings(string oldDir, string newDir)
        {
            foreach (string f in new[] { "settings.ini", "logo.png" })
                try
                {
                    string src = Path.Combine(oldDir, f), dst = Path.Combine(newDir, f);
                    if (File.Exists(src) && !File.Exists(dst)) File.Copy(src, dst);
                }
                catch { }
        }

        // ------------------------------------------------------------ the offline SQL Server files («مكونات»)
        /// <summary>Files of «مكونات» next to the setup that the program folder lacks (missing, or smaller = an interrupted copy).
        /// Empty when the folder does not hold the SQL Server file.</summary>
        public static List<string> MissingComponents(string fromDir, string target)
        {
            List<string> res = new List<string>();
            try
            {
                string src = Path.Combine(fromDir, InstallEnv.ComponentsDir), dst = Path.Combine(target, InstallEnv.ComponentsDir);
                if (!File.Exists(Path.Combine(src, InstallEnv.ComponentsMain)) || InstallEnv.SamePath(src, dst)) return res;
                foreach (string f in Directory.GetFiles(src, "*", SearchOption.AllDirectories))
                {
                    string rel = f.Substring(src.TrimEnd('\\').Length + 1), d = Path.Combine(dst, rel);
                    if (!File.Exists(d) || new FileInfo(d).Length < new FileInfo(f).Length) res.Add(rel);
                }
            }
            catch { }
            return res;
        }

        public static long ComponentsSize(string fromDir, IList<string> files)
        {
            long n = 0;
            foreach (string f in files) try { n += new FileInfo(Path.Combine(fromDir, InstallEnv.ComponentsDir, f)).Length; } catch { }
            return n;
        }

        /// <summary>Copies them (progress: bytes done, bytes total; false = stop). Returns false when stopped.</summary>
        public static bool CopyComponents(string fromDir, IList<string> files, string target, Func<long, long, bool> progress)
        {
            long total = ComponentsSize(fromDir, files), done = 0;
            byte[] buf = new byte[1 << 20];
            foreach (string rel in files)
            {
                string src = Path.Combine(fromDir, InstallEnv.ComponentsDir, rel), dst = Path.Combine(target, InstallEnv.ComponentsDir, rel);
                Directory.CreateDirectory(Path.GetDirectoryName(dst));
                string part = dst + ".part";
                bool stop = false;
                try
                {
                    using (FileStream i = new FileStream(src, FileMode.Open, FileAccess.Read, FileShare.Read))
                    using (FileStream o = File.Create(part))
                    {
                        int n;
                        while ((n = i.Read(buf, 0, buf.Length)) > 0)
                        {
                            o.Write(buf, 0, n); done += n;
                            if (progress != null && !progress(done, total)) { stop = true; break; }
                        }
                    }
                    if (!stop) { if (File.Exists(dst)) { ClearReadOnly(dst); File.Delete(dst); } File.Move(part, dst); }
                }
                catch { try { File.Delete(part); } catch { } throw; }
                if (stop) { try { File.Delete(part); } catch { } return false; }
            }
            return true;
        }
    }
}
