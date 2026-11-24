using System.Collections.Generic;
using System.Data;
using System.Drawing.Drawing2D;
using System.Drawing;
using System.Windows.Forms;
using System;

namespace Stores
{
    /// <summary>Simple two series bar chart (issues / receipts per month), newest on the left.</summary>
    class MonthChart : Control
    {
        double[] a = new double[12], b = new double[12];
        string[] labels = new string[12];

        public MonthChart()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
            BackColor = Theme.Card;
        }

        public void Load(DataTable issues, DataTable receipts, DateTime from)
        {
            for (int i = 0; i < 12; i++) { DateTime d = from.AddMonths(i); labels[i] = d.ToString("MM/yy"); a[i] = 0; b[i] = 0; }
            foreach (DataRow r in issues.Rows) { int i = (Db.I(r["y"]) - from.Year) * 12 + Db.I(r["m"]) - from.Month; if (i >= 0 && i < 12) a[i] = Db.D(r["n"]); }
            foreach (DataRow r in receipts.Rows) { int i = (Db.I(r["y"]) - from.Year) * 12 + Db.I(r["m"]) - from.Month; if (i >= 0 && i < 12) b[i] = Db.D(r["n"]); }
            Invalidate();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;
            int left = 36, right = 10, top = 30, bottom = 26;
            int w = Width - left - right, h = Height - top - bottom;
            if (w < 50 || h < 50) return;
            double max = 1; for (int i = 0; i < 12; i++) max = Math.Max(max, Math.Max(a[i], b[i]));
            double step = Math.Pow(10, Math.Floor(Math.Log10(max))); double nice = Math.Ceiling(max / step) * step; if (nice < max) nice = max;
            using (Pen grid = new Pen(Theme.ChartGrid))
            using (SolidBrush mb = new SolidBrush(Theme.Muted))
            {
                for (int k = 0; k <= 4; k++)
                {
                    int y = top + h - (int)(h * k / 4.0);
                    g.DrawLine(grid, left, y, left + w, y);
                    g.DrawString(((int)(nice * k / 4)).ToString(), Theme.FontSmall, mb, 0, y - 8);
                }
            }
            float slot = w / 12f, bw = Math.Max(4, slot * 0.32f);
            g.SetClip(new Rectangle(0, 0, Width, top + h + 1));
            using (SolidBrush ba = new SolidBrush(Theme.Primary))
            using (SolidBrush bb = new SolidBrush(Theme.Accent))
            using (SolidBrush mb = new SolidBrush(Theme.Muted))
            using (StringFormat sf = new StringFormat { Alignment = StringAlignment.Center })
            {
                for (int i = 0; i < 12; i++)
                {
                    // right-to-left time axis: oldest month on the right
                    float x = Lang.English ? left + i * slot + slot / 2f : left + w - (i + 1) * slot + slot / 2f;   // English: oldest on the left
                    float ha = (float)(h * a[i] / nice), hb = (float)(h * b[i] / nice);
                    // bars with rounded tops (Fluent)
                    if (ha > 0.5f) using (GraphicsPath p = Theme.Round(new RectangleF(x - bw, top + h - ha, bw - 1, ha + 3), Math.Min(3f, bw / 2f))) g.FillPath(ba, p);
                    if (hb > 0.5f) using (GraphicsPath p = Theme.Round(new RectangleF(x + 1, top + h - hb, bw - 1, hb + 3), Math.Min(3f, bw / 2f))) g.FillPath(bb, p);
                    g.ResetClip();
                    g.DrawString(labels[i], Theme.FontSmall, mb, new RectangleF(x - slot / 2, top + h + 4, slot, 18), sf);
                    g.SetClip(new Rectangle(0, 0, Width, top + h + 1));
                }
                // legend
                g.ResetClip();
                if (Lang.English)
                {
                    // legend from the right edge: ● Issue  ● Receipt (measured: English words are longer)
                    string la = Lang.T("إخراج"), lb = Lang.T("إدخال");
                    int wa = TextRenderer.MeasureText(la, Theme.FontSmall).Width, wb = TextRenderer.MeasureText(lb, Theme.FontSmall).Width;
                    int xb = Width - 10 - wb, xa = xb - 24 - wa;
                    g.FillEllipse(ba, xa - 14, 7, 10, 10); g.DrawString(la, Theme.FontSmall, mb, xa, 4);
                    g.FillEllipse(bb, xb - 14, 7, 10, 10); g.DrawString(lb, Theme.FontSmall, mb, xb, 4);
                }
                else
                {
                    g.FillEllipse(ba, Width - 20, 7, 10, 10); g.DrawString("إخراج", Theme.FontSmall, mb, Width - 64, 4);
                    g.FillEllipse(bb, Width - 96, 7, 10, 10); g.DrawString("إدخال", Theme.FontSmall, mb, Width - 140, 4);
                }
            }
        }
    }
}
