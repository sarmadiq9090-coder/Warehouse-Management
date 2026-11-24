using System;
using System.Diagnostics;
using System.IO;
using System.Windows.Forms;

namespace Stores
{
    /// <summary>The illustrated user manual «دليل الاستخدام» (docs/manual/index.html + img/), shipped in the release zip
    /// next to the program. Opened in the default browser from «الإعدادات» or with F1; when the folder is missing
    /// (a copy of the .exe alone) the copy on GitHub is opened instead.</summary>
    static class Manual
    {
        /// <summary>Path of the manual relative to the program folder (the same inside the repository and the zip).</summary>
        public const string RelPath = @"docs\manual\index.html";

        public static string LocalPath { get { return Path.Combine(Settings.AppDir, RelPath); } }

        /// <summary>The manual on GitHub (empty when the program has no repository set).</summary>
        public static string OnlineUrl
        {
            get { return Updater.Repo.Length == 0 ? "" : "https://github.com/" + Updater.Repo + "/blob/main/" + RelPath.Replace('\\', '/'); }
        }

        public static void Open(IWin32Window owner)
        {
            string target = File.Exists(LocalPath) ? LocalPath : OnlineUrl;
            if (target.Length == 0)
            {
                UI.Warn("دليل الاستخدام غير موجود بجانب البرنامج:\n" + LocalPath + "\n\nانسخ مجلد docs من ملف الإصدار (zip) إلى مجلد البرنامج.");
                return;
            }
            try { Process.Start(target); }
            catch (Exception ex) { UI.Warn("تعذر فتح دليل الاستخدام:\n" + target + "\n\n" + ex.Message); }
        }
    }
}
