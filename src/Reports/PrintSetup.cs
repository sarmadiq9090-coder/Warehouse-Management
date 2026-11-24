using System.Collections.Generic;
using System.Data;
using System.Diagnostics;
using System.Drawing;
using System.Globalization;
using System.IO.Compression;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Windows.Forms;
using System;

namespace Stores
{
    // ================================================================== printing helpers
    /// <summary>The report engine prints its own page header/footer ("about:blank", date…) from the
    /// Internet Explorer page settings of the Windows user. Before the first print of a session we put
    /// clean values there (report title + page x / y), and put the user's own values back when the program closes.</summary>
    static class PrintSetup
    {
        const string Key = @"Software\Microsoft\Internet Explorer\PageSetup";
        static Dictionary<string, object> saved;
        static readonly string[][] Ours = {
            new[] { "header", "" }, new[] { "footer", "&w&b&b&p / &P" },
            new[] { "margin_top", "0.45" }, new[] { "margin_bottom", "0.45" }, new[] { "margin_left", "0.35" }, new[] { "margin_right", "0.35" },
            new[] { "Print_Background", "yes" } };

        public static void Apply()
        {
            if (saved != null) return;   // once per session: later changes made in «إعداد الصفحة» are kept
            try
            {
                using (Microsoft.Win32.RegistryKey k = Microsoft.Win32.Registry.CurrentUser.CreateSubKey(Key))
                {
                    saved = new Dictionary<string, object>();
                    foreach (string[] v in Ours) { saved[v[0]] = k.GetValue(v[0]); k.SetValue(v[0], v[1], Microsoft.Win32.RegistryValueKind.String); }
                }
                Application.ApplicationExit += delegate { Restore(); };
            }
            catch { }
        }

        static void Restore()
        {
            if (saved == null) return;
            try
            {
                using (Microsoft.Win32.RegistryKey k = Microsoft.Win32.Registry.CurrentUser.CreateSubKey(Key))
                    foreach (KeyValuePair<string, object> kv in saved)
                    {
                        if (kv.Value == null) k.DeleteValue(kv.Key, false);
                        else k.SetValue(kv.Key, kv.Value);
                    }
            }
            catch { }
        }

        public static void Print(WebBrowser wb) { Apply(); wb.ShowPrintDialog(); }
        public static void Preview(WebBrowser wb) { Apply(); wb.ShowPrintPreviewDialog(); }
        public static void PageSetup(WebBrowser wb) { Apply(); wb.ShowPageSetupDialog(); }

        /// <summary>Zoom of the report text on screen (kept for the session).</summary>
        public static int ZoomPct = 100;
        public static void ApplyZoom(WebBrowser wb)
        {
            try { if (wb.Document != null && wb.Document.Body != null) wb.Document.Body.Style = "zoom:" + ZoomPct + "%"; } catch { }
        }
        public static void ZoomBy(WebBrowser wb, int delta)
        {
            ZoomPct = Math.Max(50, Math.Min(200, ZoomPct + delta));
            ApplyZoom(wb);
        }
    }
}
