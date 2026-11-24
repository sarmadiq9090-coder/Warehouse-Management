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
    // ================================================================== report zoom
    /// <summary>Zoom of reports on screen. (Printing is done by ReportPages / PrintPreviewForm, not by the browser control.)</summary>
    static class PrintSetup
    {
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
