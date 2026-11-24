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
    // ------------------------------------------------------------------ settings
    static class Settings
    {
        static Dictionary<string, string> values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        static string overrideData;

        public static string AppDir { get { return Path.GetDirectoryName(Application.ExecutablePath); } }
        static string FilePath { get { return Path.Combine(AppDir, "settings.ini"); } }

        public static void Load(string[] args)
        {
            try
            {
                if (File.Exists(FilePath))
                {
                    foreach (string line in File.ReadAllLines(FilePath, Encoding.UTF8))
                    {
                        int i = line.IndexOf('=');
                        if (i <= 0 || line.TrimStart().StartsWith("#")) continue;
                        values[line.Substring(0, i).Trim()] = line.Substring(i + 1).Trim();
                    }
                }
            }
            catch { }
            foreach (string a in args)
            {
                if (a.StartsWith("/data=", StringComparison.OrdinalIgnoreCase)) overrideData = a.Substring(6).Trim('"');
            }
        }

        public static void Save()
        {
            try
            {
                List<string> lines = new List<string>();
                lines.Add("# اعدادات برنامج " + Program.AppName);
                foreach (KeyValuePair<string, string> kv in values) lines.Add(kv.Key + "=" + kv.Value);
                File.WriteAllLines(FilePath, lines.ToArray(), Encoding.UTF8);
            }
            catch (Exception ex) { UI.Warn("تعذر حفظ ملف الاعدادات: " + ex.Message); }
        }

        public static string Get(string key, string def)
        {
            string v;
            return values.TryGetValue(key, out v) && v.Length > 0 ? v : def;
        }

        public static void Set(string key, string value) { values[key] = value ?? ""; }

        public static string DataPath
        {
            get { return overrideData ?? Get("DataPath", null); }
            set { if (overrideData != null) overrideData = value; else Set("DataPath", value); }
        }

        public static string BackupDir
        {
            get
            {
                string d = Get("BackupDir", null);
                if (!string.IsNullOrEmpty(d)) return d;
                if (Db.IsSql || string.IsNullOrEmpty(Db.DataPath)) return Path.Combine(AppDir, "نسخ احتياطية");
                return Path.Combine(Path.GetDirectoryName(Db.DataPath), "نسخ احتياطية - مخازن");
            }
        }

        // --- connection: "access" (file) or "sql" (SQL Server)
        public static string Mode { get { return overrideData != null ? "access" : Get("Mode", "access"); } set { Set("Mode", value); } }
        public static string SqlServer { get { return Get("SqlServer", ""); } set { Set("SqlServer", value); } }
        public static string SqlDatabase { get { return Get("SqlDatabase", "EngStores"); } set { Set("SqlDatabase", value); } }
        public static string SqlUser { get { return Get("SqlUser", ""); } set { Set("SqlUser", value); } }
        public static bool SqlTrust { get { return Get("SqlTrustCert", "0") == "1"; } set { Set("SqlTrustCert", value ? "1" : "0"); } }

        /// <summary>The password is stored encrypted for the current Windows user (DPAPI).</summary>
        public static string SqlPassword
        {
            get
            {
                string enc = Get("SqlPassword", "");
                if (enc.Length == 0) return "";
                try
                {
                    byte[] b = System.Security.Cryptography.ProtectedData.Unprotect(Convert.FromBase64String(enc), null,
                        System.Security.Cryptography.DataProtectionScope.CurrentUser);
                    return Encoding.UTF8.GetString(b);
                }
                catch { return ""; }
            }
            set
            {
                if (string.IsNullOrEmpty(value)) { Set("SqlPassword", ""); return; }
                byte[] b = System.Security.Cryptography.ProtectedData.Protect(Encoding.UTF8.GetBytes(value), null,
                    System.Security.Cryptography.DataProtectionScope.CurrentUser);
                Set("SqlPassword", Convert.ToBase64String(b));
            }
        }

        public static string SqlConnStr() { return Db.BuildSqlConnStr(SqlServer, SqlDatabase, SqlUser, SqlPassword, SqlTrust); }

        public static string Describe()
        {
            return Mode == "sql" ? "السيرفر: " + SqlServer + " / " + SqlDatabase : "الملف: " + DataPath;
        }
    }
}
