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
    static class Program
    {
        public const string AppName = "Inventory Management Software";
        /// <summary>Arabic name shown under the English one.</summary>
        public const string AppNameAr = "نظام إدارة المخازن";
        /// <summary>Company name shown on screens and reports (a setting, see Branding).</summary>
        public static string Company { get { return Branding.Company; } }
        /// <summary>Set by «تسجيل الخروج»: after the main window closes, the login screen shows again.</summary>
        public static bool LogoutRequested;
        public const string Version = "1.2.3";

        [STAThread]
        static void Main(string[] args)
        {
            SingleInstance.Mark();
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.ThreadException += delegate(object s, System.Threading.ThreadExceptionEventArgs e) { UI.Error(e.Exception); };
            AppDomain.CurrentDomain.UnhandledException += delegate(object s, UnhandledExceptionEventArgs e)
            {
                Exception ex = e.ExceptionObject as Exception;
                if (ex != null) UI.Error(ex);
            };

            Settings.Load(args);
            Lang.English = Settings.Get("Language", "") == "en";   // messages before Lang.Init
            if (InstallOffer.AtStart(args)) return;   // a fresh download replaced the older installed copy and started it
            if (!SingleInstance.Acquire()) return;   // already open: its window is shown
            ExitListener.Start();        // the setup of a newer version can ask this program to exit
            foreach (string a in args)   // started by the one-file setup: its language
                if (a.StartsWith("/lang=", StringComparison.OrdinalIgnoreCase) && Settings.Get("Language", "").Length == 0) Lang.SetDefault(a.Substring(6).Equals("en", StringComparison.OrdinalIgnoreCase));
            Lang.Init();                 // first start: asks for the language (before any data exists)
            if (Array.Exists(args, a => a.Equals("/server", StringComparison.OrdinalIgnoreCase))) ServerSetup.RunAuto();   // the one-file setup: the server, without questions
            if (!Connect()) return;
            PrepareData();
            InstallOffer.Register(args); // the setup finds this folder

            // «تسجيل الخروج» returns to the login screen
            do
            {
                LogoutRequested = false;
                DialogResult login;
                do { using (LoginForm lf = new LoginForm()) login = lf.ShowDialog(); } while (login == DialogResult.Retry);   // Retry = language switched
                if (login != DialogResult.OK) { Db.Close(); return; }
                Lang.LoadUser();
                Backup.DailyAuto();
                Cache.LoadAll();
                Db.Heartbeat();
                do { MainForm.Reopen = false; Application.Run(new MainForm()); } while (MainForm.Reopen);   // language switched
                Db.SignOff();
            } while (LogoutRequested);
            Db.Close();
            SingleInstance.Release();
        }

        /// <summary>Everything a freshly connected data file needs before sign-in (start-up, and after «تغيير البيانات»
        /// on the login screen).</summary>
        public static void PrepareData()
        {
            Backup.DailyAuto();          // backup before any change to the data file
            Db.EnsureAppTables();
            // «المستلم = مركز الكلفة»: issues without a cost centre (old data, or saved by the old program) get the
            // cost centre of their recipient (first time: backup + full conversion; later: only the new documents)
            UI.WaitCursor(true);
            try { CostCentres.ConvertAtStart(); } finally { UI.WaitCursor(false); }
            UI.WaitCursor(true); try { Valuation.RepairAtStart(); } finally { UI.WaitCursor(false); }   // receipt lots without a cost get one (first time: backup), see Valuation
            Branding.Load();
        }

        /// <summary>Connects using the saved settings; asks for a connection when it fails.</summary>
        public static bool Connect()
        {
            string err = TryConnectSaved();
            if (err == null) return true;
            if (err.Length > 0) UI.Warn("تعذر الاتصال بالبيانات:\n" + Settings.Describe() + "\n\n" + err);
            using (ConnectionForm f = new ConnectionForm())
            {
                return f.ShowDialog() == DialogResult.OK;
            }
        }

        /// <summary>null = connected, "" = nothing configured, otherwise the error.</summary>
        public static string TryConnectSaved()
        {
            if (Settings.Mode == "sql")
            {
                if (string.IsNullOrEmpty(Settings.SqlServer)) return "";
                UI.WaitCursor(true);
                try { return Db.InitSql(Settings.SqlConnStr()); }
                finally { UI.WaitCursor(false); }
            }
            string path = Settings.DataPath;
            if (string.IsNullOrEmpty(path)) return "";
            if (!File.Exists(path)) return "ملف البيانات غير موجود.";
            return Db.InitAccess(path);
        }

        public static string AskDataFile()
        {
            using (OpenFileDialog d = new OpenFileDialog())
            {
                d.Title = "اختر ملف بيانات المخازن";
                d.Filter = "ملفات البيانات (*.AMR;*.mdb)|*.AMR;*.mdb|كل الملفات|*.*";
                if (!string.IsNullOrEmpty(Settings.DataPath))
                {
                    try { d.InitialDirectory = Path.GetDirectoryName(Settings.DataPath); } catch { }
                }
                return d.ShowDialog() == DialogResult.OK ? d.FileName : null;
            }
        }
    }
}
