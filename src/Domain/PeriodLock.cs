using System.Data.Common;
using System.Data;
using System.Globalization;
using System;

namespace Stores
{
    /// <summary>Monthly period lock «قفل الفترات الشهرية», on top of the year closing (YearClose).
    /// A user with the permission «قفل وفتح الفترات الشهرية» (disply_command 84) locks all documents up to the end of
    /// a month (setting locked_through) and can unlock again; both are logged with before/after (AuditTrail).
    /// The effective limit is max(closed_through, locked_through): a document dated on or before it can not be
    /// added, changed or deleted, exactly like a closed year.</summary>
    static class PeriodLock
    {
        public const int Perm = 84;
        public const string PermName = "قفل وفتح الفترات الشهرية";
        public const string Key = "locked_through";
        public const string ActLock = "قفل الفترات", ActUnlock = "فتح قفل الفترات";
        static readonly CultureInfo Inv = CultureInfo.InvariantCulture;
        static readonly string[] Months = { "", "كانون الثاني", "شباط", "آذار", "نيسان", "أيار", "حزيران", "تموز", "آب", "أيلول", "تشرين الأول", "تشرين الثاني", "كانون الأول" };

        // values of the last Limit() read (Error / ShortText use them: they are called inside a transaction)
        static DateTime readClosed = DateTime.MinValue, readLocked = DateTime.MinValue;

        public static string MonthName(int month) { return month >= 1 && month <= 12 ? Months[month] : ""; }
        /// <summary>«آذار 2025».</summary>
        public static string MonthName(DateTime d) { return MonthName(d.Month) + " " + d.Year; }

        public static bool CanManage { get { return Session.Can(Perm); } }

        /// <summary>Last locked day (end of the locked month), or DateTime.MinValue when no month is locked.
        /// Read directly (not Db.GetSetting): if the database can not be read the save must fail.</summary>
        public static DateTime LockedThrough()
        {
            object o = Db.Scalar("SELECT v FROM app_settings WHERE k = ?", Key);
            return Parse(o);
        }

        static DateTime Parse(object o)
        {
            DateTime d;
            string s = o == null || o is DBNull ? "" : Convert.ToString(o);
            return DateTime.TryParseExact(s, "yyyy-MM-dd", Inv, DateTimeStyles.None, out d) ? d : DateTime.MinValue;
        }

        /// <summary>The effective limit max(year closing, month lock). Read it BEFORE the transaction.</summary>
        public static DateTime Limit()
        {
            readClosed = YearClose.ClosedThrough();
            readLocked = LockedThrough();
            return readLocked > readClosed ? readLocked : readClosed;
        }

        public static bool IsLocked(DateTime date) { DateTime l = Limit(); return l != DateTime.MinValue && date.Date <= l; }

        /// <summary>The refusal for a document dated in a locked period (uses the values of the last Limit()).</summary>
        public static UserError Error(DateTime date)
        {
            if (readClosed != DateTime.MinValue && date.Date <= readClosed) return YearClose.Error(date);
            if (readLocked == DateTime.MinValue) return YearClose.Error(date);
            return new UserError("شهر " + MonthName(date) + " مقفل.\nلا يمكن إضافة أو تعديل أو حذف سند بتاريخ " + UI.Date(date) + ".\n" +
                                 "السندات مقفلة حتى نهاية شهر " + MonthName(readLocked) + " (" + UI.Date(readLocked) + ").\n\n" +
                                 "إذا كان التعديل ضروريا: يفتح صاحب الصلاحية القفل من «الإعدادات ← قفل الفترات الشهرية».");
        }

        /// <summary>Short status for the document screen («شهر آذار 2025 مقفل» / «سنة 2024 مقفلة»), after IsLocked.</summary>
        public static string ShortText(DateTime date)
        {
            if (readClosed != DateTime.MinValue && date.Date <= readClosed) return "سنة " + date.Year + " مقفلة";
            return "شهر " + MonthName(date) + " مقفل";
        }

        /// <summary>Text of the current state for the settings card and the lock window.</summary>
        public static string StatusText()
        {
            DateTime l = LockedThrough();
            return l == DateTime.MinValue ? "لا يوجد قفل شهري." : "السندات مقفلة حتى نهاية شهر " + MonthName(l) + " (" + UI.Date(l) + ").";
        }

        // ------------------------------------------------------------ lock / unlock
        static void Demand()
        {
            if (!CanManage) throw new UserError("قفل الفترات وفتحها يحتاج صلاحية «" + PermName + "».");
        }

        public static DateTime MonthEnd(int year, int month) { return new DateTime(year, month, 1).AddMonths(1).AddDays(-1); }

        /// <summary>Locks every document up to the end of the month (also used to move the lock back to an earlier month).
        /// Returns the last locked day.</summary>
        public static DateTime LockThrough(int year, int month)
        {
            Demand();
            if (month < 1 || month > 12 || year < 1990 || year > 2200) throw new UserError("اختر الشهر والسنة.");
            DateTime end = MonthEnd(year, month);
            if (end > MonthEnd(DateTime.Today.Year, DateTime.Today.Month)) throw new UserError("لا يمكن قفل شهر " + MonthName(end) + " لأنه لم يبدأ بعد.");
            DateTime closed = YearClose.ClosedThrough(), old = LockedThrough();
            if (closed != DateTime.MinValue && end <= closed)
                throw new UserError("شهر " + MonthName(end) + " ضمن سنة مقفلة مسبقا (إقفال السنة حتى " + UI.Date(closed) + ").");
            if (end == old) throw new UserError("السندات مقفلة حتى نهاية شهر " + MonthName(end) + " مسبقا.");
            Write(end, old, ActLock, "السندات حتى نهاية شهر " + MonthName(end) + " (" + UI.Date(end) + ") مقفلة" +
                                     (old != DateTime.MinValue ? "، وكان القفل حتى " + UI.Date(old) : ""));
            return end;
        }

        /// <summary>Removes the month lock (the year closing stays). Returns the day it was locked through (MinValue = nothing to unlock).</summary>
        public static DateTime Unlock()
        {
            Demand();
            DateTime old = LockedThrough();
            if (old == DateTime.MinValue) return old;
            DateTime closed = YearClose.ClosedThrough();
            Write(DateTime.MinValue, old, ActUnlock, "كان القفل حتى نهاية شهر " + MonthName(old) + " (" + UI.Date(old) + ")" +
                                                   (closed != DateTime.MinValue ? "، وإقفال السنة باق حتى " + UI.Date(closed) : ""));
            return old;
        }

        static void Write(DateTime value, DateTime old, string act, string details)
        {
            string v = value == DateTime.MinValue ? "" : value.ToString("yyyy-MM-dd", Inv);
            Db.InTx(delegate(DbTransaction tx)
            {
                if (Db.X(tx, "UPDATE app_settings SET v = ? WHERE k = ?", v, Key) == 0)
                    Db.X(tx, "INSERT INTO app_settings (k, v) VALUES (?, ?)", Key, v);
                int id = AuditTrail.Log(tx, act, details);
                AuditTrail.Detail(tx, id, "app_settings", Key, "قفل الفترات حتى",
                                  old == DateTime.MinValue ? "" : UI.Date(old), value == DateTime.MinValue ? "" : UI.Date(value));
            });
        }

        /// <summary>Adds the permission row (command 84) once; the administrator (user1) has it.</summary>
        public static void EnsurePermission()
        {
            try
            {
                if (!Db.TableExists("disply_command")) return;
                if (Db.I(Db.Scalar("SELECT COUNT(*) FROM disply_command WHERE command_no = ?", Perm)) > 0) return;
                Db.Exec("INSERT INTO disply_command (command_no, command_name, user1, user2, user3, user4, user5) VALUES (?, ?, ?, ?, ?, ?, ?)",
                        Perm, PermName, true, false, false, false, false);
            }
            catch (Exception ex) { Db.SchemaWarning = "disply_command " + Perm + ": " + ex.Message; }
        }
    }
}
