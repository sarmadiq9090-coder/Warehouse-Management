using System.Collections.Generic;
using System.Drawing.Drawing2D;
using System.Drawing;
using System.Globalization;
using System.IO;
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

        public static void DailyAuto()
        {
            if (Db.IsSql) return;   // the server keeps its own backups
            try
            {
                string folder = Settings.BackupDir;
                string name = "تلقائي " + DateTime.Now.ToString("yyyy-MM-dd", UI.Inv) + Path.GetExtension(Db.DataPath);
                if (File.Exists(Path.Combine(folder, name))) return;
                CopyTo(folder, name);
                // keep the 30 most recent automatic backups
                List<string> files = new List<string>(Directory.GetFiles(folder, "تلقائي *"));
                files.Sort();
                for (int i = 0; i < files.Count - 30; i++) { try { File.Delete(files[i]); } catch { } }
            }
            catch { }
        }

        public static string Now(string tag)
        {
            if (Db.IsSql) return null;
            string name = tag + " " + DateTime.Now.ToString("yyyy-MM-dd HHmmss", UI.Inv) + Path.GetExtension(Db.DataPath);
            return CopyTo(Settings.BackupDir, name);
        }
    }
}
