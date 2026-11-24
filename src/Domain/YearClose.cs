using System.Collections.Generic;
using System.Data;
using System.Diagnostics;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Windows.Forms;
using System;

namespace Stores
{
    /// <summary>Manual year-end closing («إقفال السنة»), done by the administrator only.
    /// Closing year Y: (1) a backup copy of the data, (2) the year-end stock count (as of 31/12/Y) saved as an Excel
    /// archive file, (3) documents dated up to 31/12/Y can no longer be added, changed or deleted.
    /// Balances are never moved: they continue into the next year automatically. The administrator can reopen.</summary>
    static class YearClose
    {
        static readonly CultureInfo Inv = CultureInfo.InvariantCulture;
        const string Key = "closed_through";

        /// <summary>Last closed day (31/12 of the last closed year), or DateTime.MinValue when no year is closed.</summary>
        public static DateTime ClosedThrough()
        {
            DateTime d;
            // read directly (not Db.GetSetting, which hides errors): if the database cannot be read, the save
            // must fail rather than silently ignore a closed year
            object o = Db.Scalar("SELECT v FROM app_settings WHERE k = ?", Key);
            string s = o == null || o is DBNull ? "" : Convert.ToString(o);
            return DateTime.TryParseExact(s, "yyyy-MM-dd", Inv, DateTimeStyles.None, out d) ? d : DateTime.MinValue;
        }

        public static int ClosedYear() { DateTime d = ClosedThrough(); return d == DateTime.MinValue ? 0 : d.Year; }

        public static bool IsClosed(DateTime date) { DateTime c = ClosedThrough(); return c != DateTime.MinValue && date.Date <= c; }

        public static UserError Error(DateTime date)
        {
            return new UserError("سنة " + date.Year + " مقفلة.\nلا يمكن إضافة أو تعديل أو حذف سند بتاريخ " + UI.Date(date) + ".\n\n" +
                                 "إذا كان التعديل ضروريا: يعيد المدير فتح السنة من «الإعدادات ← إقفال السنة».");
        }

        public static string ArchiveOverride;   // tests
        public static string ArchiveDir
        {
            get { return ArchiveOverride ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "مخازن الشركة الهندسية - إقفال السنوات"); }
        }

        /// <summary>Years that can be closed now: after the last closed year, before the current year, and having documents.</summary>
        public static List<int> ClosableYears()
        {
            int first = ClosedYear() + 1;
            object min = Db.Scalar("SELECT MIN(menu_date) FROM sale");
            object min2 = Db.Scalar("SELECT MIN(menu_date) FROM shop");
            int dataFirst = DateTime.Today.Year - 1;
            if (min is DateTime) dataFirst = Math.Min(dataFirst, ((DateTime)min).Year);
            if (min2 is DateTime) dataFirst = Math.Min(dataFirst, ((DateTime)min2).Year);
            if (first < dataFirst) first = dataFirst;
            List<int> l = new List<int>();
            for (int y = DateTime.Today.Year - 1; y >= first; y--) l.Add(y);
            return l;
        }

        /// <summary>Closes the year. Returns the archive file. Throws UserError with a clear message on problems.</summary>
        public static string Close(int year, bool backup, bool archive)
        {
            if (!Session.IsAdmin) throw new UserError("إقفال السنة للمدير فقط.");
            DateTime end = new DateTime(year, 12, 31);
            if (end >= DateTime.Today) throw new UserError("لا يمكن إقفال سنة " + year + " قبل انتهائها.");
            if (ClosedThrough() >= end) throw new UserError("سنة " + year + " مقفلة مسبقا.");

            // 1. backup
            if (backup)
            {
                if (Db.IsSql)
                    Db.Exec("BACKUP DATABASE [" + Settings.SqlDatabase.Replace("]", "]]") + "] TO DISK = N'" + Settings.SqlDatabase.Replace("'", "''") +
                            "_closing_" + year + "_" + DateTime.Now.ToString("yyyyMMdd_HHmmss", Inv) + ".bak' WITH COPY_ONLY, INIT, CHECKSUM");
                else Backup.Now("قبل إقفال سنة " + year);
            }

            // 2. year-end stock count, kept as an archive file
            string file = null;
            if (archive)
            {
                ReportParams p = new ReportParams(); p.From = end; p.To = end;
                ReportData r = Reports.Get("stock").Run(p);
                r.Title = "جرد نهاية سنة " + year + " (الأرصدة بتاريخ " + UI.Date(end) + ")";
                Directory.CreateDirectory(ArchiveDir);
                file = Path.Combine(ArchiveDir, "جرد نهاية سنة " + year + ".xlsx");
                Excel.Write(file, r.Title, r.Subtitle, r.Cols.Select(c => c.Header).ToList(), r.Cols.Select(c => c.Kind).ToList(), r.Rows, r.Cols.Select(c => c.Total).ToList());
            }

            // 3. lock the period
            Db.SaveSetting(Key, end.ToString("yyyy-MM-dd", Inv));
            Db.Log(null, "إقفال السنة", "سنة " + year + " — السندات حتى " + UI.Date(end) + " مقفلة" + (file != null ? " — الأرشيف: " + Path.GetFileName(file) : ""));
            return file;
        }

        /// <summary>Reopens the last closed year (the one before it stays closed).</summary>
        public static int Reopen()
        {
            if (!Session.IsAdmin) throw new UserError("إعادة فتح السنة للمدير فقط.");
            int y = ClosedYear();
            if (y == 0) return 0;
            Db.SaveSetting(Key, IsYearWithData(y - 1) ? new DateTime(y - 1, 12, 31).ToString("yyyy-MM-dd", Inv) : "");
            Db.Log(null, "إعادة فتح سنة", "سنة " + y + " — يمكن التعديل على سنداتها");
            return y;
        }

        static bool IsYearWithData(int y)
        {
            DateTime a = new DateTime(y, 1, 1);
            return Db.I(Db.Scalar("SELECT COUNT(*) FROM sale WHERE menu_date < ?", a.AddYears(1))) + Db.I(Db.Scalar("SELECT COUNT(*) FROM shop WHERE menu_date < ?", a.AddYears(1))) > 0;
        }
    }
}
