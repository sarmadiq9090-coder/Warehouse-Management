using System.Collections.Generic;
using System.Data;
using System.Drawing.Drawing2D;
using System.Drawing;
using System.IO;
using System.Windows.Forms;
using System;

namespace Stores
{
    // ------------------------------------------------------------------ brand header
    /// <summary>Top bar brand (Windows 11 title area): the company logo or program icon, the English program name,
    /// and under it the Arabic name with the (optional) company name in small muted text.</summary>
    class BrandBar : Panel
    {
        public string Title = Program.AppName;
        public string Sub = Program.AppNameAr;

        public BrandBar()
        {
            DoubleBuffered = true;
            BackColor = Theme.Bg;
            Height = 56;
            ResizeRedraw = true;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.InterpolationMode = InterpolationMode.HighQualityBicubic;
            int right = Width - 18;
            int logoH = Math.Min(34, Height - 16);
            Image logo = Theme.Logo;
            if (logo != null)
            {
                int w = Math.Min(140, logo.Width * logoH / Math.Max(1, logo.Height));
                g.DrawImage(logo, Lang.Mirror(new Rectangle(right - w, (Height - logoH) / 2, w, logoH), Width));
                right -= w + 12;
            }
            else
            {
                Image img = Theme.ProgramImage;
                if (img != null) g.DrawImage(img, Lang.Mirror(new Rectangle(right - logoH, (Height - logoH) / 2, logoH, logoH), Width));
                right -= logoH + 12;
            }
            // English: the company (or the translated name) under the program name
            string sub = Sub, company = Program.Company;
            if (Lang.English) { sub = string.IsNullOrEmpty(company) ? Lang.T(Sub) : company; company = null; }
            using (Font f1 = new Font(Theme.LatinFontName, 13f, FontStyle.Bold))
            {
                Size s1 = TextRenderer.MeasureText(g, Title, f1);
                Size s2 = TextRenderer.MeasureText(g, sub, Theme.FontSmall);
                int y = (Height - s1.Height - s2.Height + 4) / 2;
                TextFormatFlags rtl = Lang.Flags(TextFormatFlags.Right | TextFormatFlags.RightToLeft | TextFormatFlags.NoPadding | TextFormatFlags.EndEllipsis | TextFormatFlags.SingleLine);
                TextRenderer.DrawText(g, Title, f1, Lang.Mirror(new Rectangle(0, y, right, s1.Height), Width), Theme.Text, Lang.Flags(TextFormatFlags.Right | TextFormatFlags.NoPadding | TextFormatFlags.SingleLine));
                // Arabic name, then the company (small, muted) after a dot
                TextRenderer.DrawText(g, sub, Theme.FontSmall, Lang.Mirror(new Rectangle(0, y + s1.Height - 2, right, s2.Height), Width), Theme.Primary, rtl);
                if (!string.IsNullOrEmpty(company) && company != Sub)
                {
                    int x = right - s2.Width - 2;
                    TextRenderer.DrawText(g, "•  " + company, Theme.FontSmall, new Rectangle(0, y + s1.Height - 2, Math.Max(0, x), s2.Height), Theme.Muted, rtl);
                }
            }
        }
    }
}
