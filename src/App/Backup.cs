using System.Collections.Generic;
using System.Drawing.Drawing2D;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Text;
using System.Windows.Forms;
using System;

namespace Stores
{
    // ------------------------------------------------------------------ backups
    static class Backup
    {
        public static string CopyTo(string folder, string name)
        {
            Directory.CreateDirectory(folder);
            string dst = Path.Combine(folder, name);
            using (FileStream src = new FileStream(Db.DataPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
            using (FileStream o = new FileStream(dst, FileMode.Create, FileAccess.Write))
            {
                src.CopyTo(o);
            }
            return dst;
        }

        /// <summary>Automatic daily backups that are kept: a new one replaces the oldest (owner's request).</summary>
        public const int KeepDays = 10;

        static string done;        // data file and day

        /// <summary>Once a day (program start, sign-in, and while the program stays open): a copy of the data file.</summary>
        public static void DailyAuto()
        {
            if (Db.IsSql || string.IsNullOrEmpty(Db.DataPath)) return;   // the server keeps its own backups
            string key = Db.DataPath.ToLowerInvariant() + "|" + DateTime.Now.ToString("yyyy-MM-dd", UI.Inv);
            if (done == key) return;
            try
            {
                string folder = Settings.BackupDir;
                // the data file's name too: several data files may share one backup folder
                string own = " " + Path.GetFileNameWithoutExtension(Db.DataPath) + Path.GetExtension(Db.DataPath);
                string name = "تلقائي " + DateTime.Now.ToString("yyyy-MM-dd", UI.Inv) + own;
                string path = Path.Combine(folder, name);
                if (!File.Exists(path))
                {
                    // written under another name first: an interrupted copy never counts as today's backup
                    string tmp = CopyTo(folder, name + ".tmp" + System.Diagnostics.Process.GetCurrentProcess().Id);
                    try { if (File.Exists(path)) File.Delete(tmp); else File.Move(tmp, path); }
                    catch (IOException) { try { File.Delete(tmp); } catch { } }
                    Prune(folder, own);
                }
                done = key;
            }
            catch { }
        }

        /// <summary>Deletes the automatic backups beyond the newest ten (never the ones made by hand or before an operation).</summary>
        public static void Prune(string folder, string own)
        {
            string auto = "^" + "تلقائي " + @"\d{4}-\d{2}-\d{2}";
            // this data file's, and those of versions before 1.2.2 (no file name)
            List<string> files = Directory.GetFiles(folder, "تلقائي *").Where(f => Regex.IsMatch(Path.GetFileName(f), auto + "(" + Regex.Escape(own) + @"|\.[A-Za-z]+)$", RegexOptions.IgnoreCase))
                .OrderByDescending(f => Path.GetFileName(f), StringComparer.Ordinal).ToList();
            foreach (string f in files.Skip(KeepDays)) { try { File.Delete(f); } catch { } }
            // left by a copy that was interrupted
            foreach (string f in Directory.GetFiles(folder, "تلقائي *" + ".tmp*"))
                if (Regex.IsMatch(Path.GetFileName(f), auto + @".*\.tmp\d+$") && File.GetLastWriteTime(f) < DateTime.Now.AddHours(-12)) { try { File.Delete(f); } catch { } }
        }

        public static string Now(string tag)
        {
            if (Db.IsSql) return null;
            string name = tag + " " + DateTime.Now.ToString("yyyy-MM-dd HHmmss", UI.Inv) + Path.GetExtension(Db.DataPath);
            return CopyTo(Settings.BackupDir, name);
        }
    }
}
