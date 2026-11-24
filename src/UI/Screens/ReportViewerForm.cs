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
    // ================================================================== report window (vouchers, item card, full screen)
    class ReportViewerForm : Form
    {
        WebBrowser wb; ReportData data;

        public ReportViewerForm(ReportData r)
        {
            data = r;
            Text = r.Title;
            RightToLeft = RightToLeft.Yes;
            Font = Theme.Font;
            Icon = Theme.AppIcon;
            Size = new Size(1100, 760);
            StartPosition = FormStartPosition.CenterParent;
            ShowInTaskbar = false;
            KeyPreview = true;
            FlowLayoutPanel bar = UI.Flow(); bar.BackColor = Theme.Card; bar.Padding = new Padding(6);
            Button print = UI.Btn("طباعة  (Ctrl+P)", Theme.Primary, 140), prev = UI.BtnLight("معاينة", 90), xl = UI.BtnLight("تصدير Excel", 120),
                   setup = UI.BtnLight("إعداد الصفحة", 120), zin = UI.BtnLight("تكبير +", 80), zout = UI.BtnLight("تصغير −", 80), close = UI.BtnLight("إغلاق", 90);
            bar.Controls.AddRange(new Control[] { print, prev, xl, setup, zin, zout, close });
            wb = new WebBrowser(); wb.Dock = DockStyle.Fill; wb.IsWebBrowserContextMenuEnabled = false; wb.AllowWebBrowserDrop = false; wb.ScriptErrorsSuppressed = true;
            wb.DocumentCompleted += delegate { PrintSetup.ApplyZoom(wb); };
            Controls.Add(wb); Controls.Add(bar);
            Lang.Apply(this);   // English first: it may recreate handles the theme then paints
            ThemePainter.Attach(this);
            wb.DocumentText = Html.Render(r);
            print.Click += delegate { PrintSetup.Print(wb); };
            prev.Click += delegate { PrintSetup.Preview(wb); };
            setup.Click += delegate { PrintSetup.PageSetup(wb); };
            xl.Click += delegate { Excel.Export(this, data); };
            zin.Click += delegate { PrintSetup.ZoomBy(wb, 10); };
            zout.Click += delegate { PrintSetup.ZoomBy(wb, -10); };
            close.Click += delegate { Close(); };
        }

        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            if (keyData == Keys.Escape) { Close(); return true; }
            if (keyData == (Keys.Control | Keys.P)) { PrintSetup.Print(wb); return true; }
            if (keyData == (Keys.Control | Keys.Add) || keyData == (Keys.Control | Keys.Oemplus)) { PrintSetup.ZoomBy(wb, 10); return true; }
            if (keyData == (Keys.Control | Keys.Subtract) || keyData == (Keys.Control | Keys.OemMinus)) { PrintSetup.ZoomBy(wb, -10); return true; }
            return base.ProcessCmdKey(ref msg, keyData);
        }

        /// <summary>Shows an already produced report in a large window.</summary>
        public static void ShowFull(IWin32Window owner, ReportData r)
        {
            if (r == null) { UI.Warn("اعرض التقرير أولا."); return; }
            using (ReportViewerForm f = new ReportViewerForm(r)) { f.WindowState = FormWindowState.Maximized; f.ShowDialog(owner); }
        }

        public static void ShowVoucher(IWin32Window owner, Doc d)
        {
            using (ReportViewerForm f = new ReportViewerForm(Reports.Voucher(d))) f.ShowDialog(owner);
        }

        public static void ShowItemCard(IWin32Window owner, int item)
        {
            if (!Session.Demand(Session.Reports)) return;
            ReportParams p = new ReportParams(); p.ItemNo = item; p.From = DateTime.Today.AddYears(-1).AddDays(1); p.To = DateTime.Today;
            ReportData r;
            UI.WaitCursor(true);
            try { r = Reports.ItemCard(p); } finally { UI.WaitCursor(false); }
            using (ReportViewerForm f = new ReportViewerForm(r)) f.ShowDialog(owner);
        }
    }
}
