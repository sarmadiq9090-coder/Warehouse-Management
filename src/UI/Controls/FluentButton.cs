using System.Drawing.Drawing2D;
using System.Drawing;
using System.Windows.Forms;
using System;

namespace Stores
{
    /// <summary>Windows 11 style button (made by UI.Btn / UI.BtnLight): rounded corners, hover / pressed /
    /// keyboard-focus / disabled states. Colours come from BackColor / ForeColor, so screens that recolour a
    /// button keep working. Behaves like a normal Button (PerformClick, AcceptButton, DialogResult).</summary>
    class FluentButton : Button
    {
        public int Radius = 6;
        bool hover, pressed;

        public FluentButton()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            FlatStyle = FlatStyle.Flat;
            FlatAppearance.BorderSize = 0;
            UseVisualStyleBackColor = false;
        }

        /// <summary>Standard (secondary) button: white, or the dark theme's button grey; otherwise an accent button.</summary>
        bool Light { get { return BackColor.GetBrightness() > 0.9f || BackColor.ToArgb() == Theme.ButtonBack.ToArgb(); } }

        protected override void OnMouseEnter(EventArgs e) { hover = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { hover = false; pressed = false; Invalidate(); base.OnMouseLeave(e); }
        protected override void OnMouseDown(MouseEventArgs e) { if (e.Button == MouseButtons.Left) { pressed = true; Invalidate(); } base.OnMouseDown(e); }
        protected override void OnMouseUp(MouseEventArgs e) { pressed = false; Invalidate(); base.OnMouseUp(e); }
        protected override void OnEnabledChanged(EventArgs e) { Cursor = Enabled ? Cursors.Hand : Cursors.Default; hover = pressed = false; Invalidate(); base.OnEnabledChanged(e); }
        protected override void OnGotFocus(EventArgs e) { Invalidate(); base.OnGotFocus(e); }
        protected override void OnLostFocus(EventArgs e) { Invalidate(); base.OnLostFocus(e); }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            using (SolidBrush pb = new SolidBrush(Theme.BackOf(Parent))) g.FillRectangle(pb, ClientRectangle);
            g.SmoothingMode = SmoothingMode.AntiAlias;

            Color back = BackColor, fore = ForeColor, border = Color.Empty;
            if (!Enabled)
            {
                back = Theme.IsDark ? (Light ? Theme.Card : Color.FromArgb(58, 58, 58)) : Light ? Color.FromArgb(249, 249, 249) : Color.FromArgb(222, 222, 222);
                fore = Theme.Disabled;
                border = Light ? Theme.Border : Color.Empty;
            }
            else if (Light)
            {
                // standard button: white, hairline border slightly darker at the bottom (Fluent "elevation")
                back = pressed ? Theme.Pressed : hover ? Theme.Hover : back;
                border = hover ? Theme.BorderStrong : Theme.Border;
                if (pressed) fore = Theme.Muted;
            }
            else
            {
                // accent button: lighter on hover, a little darker when pressed
                back = pressed ? ControlPaint.Dark(back, 0.08f) : hover ? Color.FromArgb(Math.Min(255, back.R + 18), Math.Min(255, back.G + 18), Math.Min(255, back.B + 18)) : back;
            }

            RectangleF r = new RectangleF(0.5f, 0.5f, Width - 1.5f, Height - 1.5f);
            using (GraphicsPath p = Theme.Round(r, Radius))
            {
                using (SolidBrush b = new SolidBrush(back)) g.FillPath(b, p);
                if (border != Color.Empty) using (Pen pen = new Pen(border)) g.DrawPath(pen, p);
            }
            if (Enabled && Light && !pressed)
                using (Pen pen = new Pen(Theme.IsDark ? Color.FromArgb(40, 255, 255, 255) : Color.FromArgb(40, 0, 0, 0))) g.DrawLine(pen, Radius, Height - 1.5f, Width - Radius - 1, Height - 1.5f);

            // keyboard focus: the Windows 11 double ring (dark outside, white inside)
            if (Focused && ShowFocusCues && Enabled)
            {
                using (GraphicsPath p = Theme.Round(new RectangleF(1, 1, Width - 3, Height - 3), Radius))
                using (Pen pen = new Pen(Theme.Text, 2f)) g.DrawPath(pen, p);
                using (GraphicsPath p = Theme.Round(new RectangleF(2.5f, 2.5f, Width - 6, Height - 6), Radius - 2))
                using (Pen pen = new Pen(Theme.IsDark ? Color.Black : Color.White, 1f)) g.DrawPath(pen, p);
            }

            TextFormatFlags f = TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.SingleLine;
            if (RightToLeft == RightToLeft.Yes) f |= TextFormatFlags.RightToLeft;
            if (!UseMnemonic) f |= TextFormatFlags.NoPrefix;
            ContentAlignment a = TextAlign;
            bool left = a == ContentAlignment.MiddleLeft || a == ContentAlignment.TopLeft || a == ContentAlignment.BottomLeft;
            bool right = a == ContentAlignment.MiddleRight || a == ContentAlignment.TopRight || a == ContentAlignment.BottomRight;
            // with RightToLeft, «Left» alignment means the reading start (the right side), as in a normal Button
            if (left) f |= RightToLeft == RightToLeft.Yes ? TextFormatFlags.Right : TextFormatFlags.Left;
            else if (right) f |= RightToLeft == RightToLeft.Yes ? TextFormatFlags.Left : TextFormatFlags.Right;
            else f |= TextFormatFlags.HorizontalCenter;
            // the text keeps its side margins only when it fits (small «…» and «استعراض…» buttons use all their width)
            // Fluent icon (Windows' own icon font) before the text, when the button is wide enough
            string glyph = Glyph ?? AutoGlyph(Text);
            if (glyph == "") glyph = null;
            string text = glyph == "\uE710" && Text.StartsWith("+ ") ? Text.Substring(2) : Text;
            text = Lang.T(text);   // English: translated when painted (Text stays Arabic: AutoGlyph reads it)
            // the icon replaces a typed «+»
            int textW = TextRenderer.MeasureText(g, text, Font).Width;
            if (glyph != null && !left && !right && textW + 34 <= Width - Padding.Horizontal)
            {
                Font icf = Theme.IconFont(Math.Max(10f, Font.Size + 1f));
                Size gs = TextRenderer.MeasureText(g, glyph, icf, Size.Empty, TextFormatFlags.NoPadding);
                int total = gs.Width + 7 + textW, cx = Width / 2, cy = Height / 2;
                bool rtl = RightToLeft == RightToLeft.Yes;
                int iconX = rtl ? cx + total / 2 - gs.Width : cx - total / 2;
                TextRenderer.DrawText(g, glyph, icf, new Rectangle(iconX, cy - gs.Height / 2 + 1, gs.Width, gs.Height), fore, TextFormatFlags.NoPadding | TextFormatFlags.SingleLine);
                Rectangle txr = rtl ? new Rectangle(iconX - 7 - textW, 0, textW, Height) : new Rectangle(iconX + gs.Width + 7, 0, textW, Height);
                TextRenderer.DrawText(g, text, Font, txr, fore, (f & ~TextFormatFlags.HorizontalCenter) | TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine);
                return;
            }
            int inset = textW + 16 <= Width - Padding.Horizontal ? 8 : 1;
            Rectangle tr = new Rectangle(Padding.Left + inset, Padding.Top, Width - Padding.Horizontal - 2 * inset, Height - Padding.Vertical);
            TextRenderer.DrawText(g, Lang.T(Text), Font, tr, fore, f);
        }

        /// <summary>Icon shown before the text; null = chosen from the text (AutoGlyph), "" = no icon.</summary>
        public string Glyph;

        /// <summary>A Fluent icon for common button texts, so every button in the program gets one automatically.</summary>
        public static string AutoGlyph(string text)
        {
            string t = (text ?? "").Trim();
            if (t.Length == 0 || t == "…" || t == "+" || t == "−" || t.StartsWith("►") || t.EndsWith("◄")) return null;
            string[][] map = {
                new[] { "حفظ", "\uE74E" }, new[] { "طباعة", "\uE749" }, new[] { "معاينة", "\uE8FF" }, new[] { "تصدير", "\uEDE1" },
                new[] { "حذف", "\uE74D" }, new[] { "إلغاء الحجز", "\uE74D" }, new[] { "تعديل", "\uE70F" }, new[] { "سند جديد", "\uE8A5" },
                new[] { "جديد", "\uE710" }, new[] { "إضافة", "\uE710" }, new[] { "+ ", "\uE710" }, new[] { "عرض التقرير", "\uE9F9" },
                new[] { "ملء الشاشة", "\uE740" }, new[] { "إعداد الصفحة", "\uE7C3" }, new[] { "بحث", "\uE721" }, new[] { "استعراض", "\uE8DA" },
                new[] { "فتح", "\uE8E5" }, new[] { "كارت", "\uE8A5" }, new[] { "كشف", "\uE8A5" }, new[] { "تحديث البرنامج", "\uE896" },
                new[] { "تحديث", "\uE72C" }, new[] { "تبليغ", "\uE8BD" }, new[] { "نسخة", "\uE78C" }, new[] { "إقفال", "\uE72E" },
                new[] { "إعادة فتح", "\uE785" }, new[] { "اختبار", "\uE703" }, new[] { "اتصال", "\uE703" }, new[] { "دخول", "\uE8FA" },
                new[] { "إغلاق", "\uE711" }, new[] { "إلغاء", "\uE711" }, new[] { "موافق", "\uE73E" }, new[] { "ترحيل", "\uE8FB" },
                new[] { "احتساب", "\uE8EF" }, new[] { "تدقيق", "\uE9D5" }, new[] { "ضغط", "\uE7B8" }, new[] { "اختيار", "\uE8B9" },
                new[] { "إزالة", "\uE74D" }, new[] { "استيراد", "\uE8B5" }, new[] { "حجز", "\uE72E" }, new[] { "السيرفر", "\uE968" },
                new[] { "سيرفر", "\uE968" }, new[] { "إنشاء", "\uE710" }, new[] { "الإعدادات", "\uE713" }
            };
            foreach (string[] m in map) if (t.Contains(m[0])) return m[1];
            return null;
        }
    }
}
