using System;
using System.IO;

namespace Stores
{
    /// <summary>«لا توجد لدي بيانات»: creates a new, empty data file from the empty template embedded in the program
    /// (tests/demo/empty-template.mdb — the real structure, no rows), with an administrator user 1 / password 1,
    /// full permissions for that user, and one store «المخزن الرئيسي».</summary>
    static class NewDatabase
    {
        static readonly object[][] Commands =
        {
            // command numbers of the original program (see Session); user1 = administrator gets everything
            new object[] { 11, "إدخال مخزني - عرض" }, new object[] { 12, "إدخال مخزني - تعديل" }, new object[] { 13, "إدخال مخزني - حذف" }, new object[] { 14, "إدخال مخزني - إضافة" },
            new object[] { 21, "إخراج مخزني - عرض" }, new object[] { 22, "إخراج مخزني - تعديل" }, new object[] { 23, "إخراج مخزني - حذف" }, new object[] { 24, "إخراج مخزني - إضافة" },
            new object[] { 61, "نقل مخزني" }, new object[] { 71, "إعداد النظام" }, new object[] { 74, "ملف الجهات" }, new object[] { 75, "ملف المواد" }, new object[] { 81, "التقارير" }
        };

        /// <summary>Writes the new file and connects to it. Returns null on success, otherwise the error text.</summary>
        public static string Create(string path)
        {
            if (File.Exists(path)) return "يوجد ملف بهذا الاسم. اختر اسما آخر.";
            using (Stream s = typeof(NewDatabase).Assembly.GetManifestResourceStream("empty-template.mdb"))
            {
                if (s == null) return "قالب البيانات غير موجود في البرنامج.";
                using (FileStream f = File.Create(path)) s.CopyTo(f);
            }
            string err = Db.InitAccess(path);
            if (err != null) return err;
            Db.EnsureAppTables();
            Db.Exec("INSERT INTO pass (pass_no, user_name, pass) VALUES (?, ?, ?)", 1, "1", "1");
            Db.SetSetting(LoginForm.MustChangeKey + "1", "1");   // the known starting password must be changed at the first sign-in
            foreach (object[] c in Commands)
                Db.Exec("INSERT INTO disply_command (command_no, command_name, user1, user2, user3, user4, user5) VALUES (?, ?, ?, ?, ?, ?, ?)",
                        c[0], c[1], true, false, false, false, false);
            Db.Exec("INSERT INTO stor (stor_name) VALUES (?)", "المخزن الرئيسي");
            Db.Log(null, "إنشاء بيانات جديدة", Path.GetFileName(path));
            return null;
        }
    }
}
