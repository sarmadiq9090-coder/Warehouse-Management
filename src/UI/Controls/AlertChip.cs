using System.Drawing.Drawing2D;
using System.Drawing;
using System.Windows.Forms;
using System;

namespace Stores
{
    /// <summary>Compact rounded alert of the home screen («255 أرصدة حرجة»): an icon, a number and a word,
    /// tinted in its colour. Sizes itself to the text; click opens the matching report.</summary>
    class AlertChip : Control
    {
        public string Glyph; public Color Color; bool hover, pressed;

        public AlertChip(string glyph, Color color)
        {
            Glyph = glyph; Color = color;
            Height = 34; Margin = new Padding(0, 0, 10, 0); Cursor = Cursors.Hand; TabStop = true;
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.ResizeRedraw | ControlStyles.Selectable, true);
        }

        protected override void OnTextChanged(EventArgs e)
        {
            Width = TextRenderer.MeasureText(Lang.T(Text), Theme.FontBold).Width + 62;
            Invalidate(); base.OnTextChanged(e);
        }

        protected override void OnMouseEnter(EventArgs e) { hover = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { hover = false; pressed = false; Invalidate(); base.OnMouseLeave(e); }
        protected override void OnMouseDown(MouseEventArgs e) { pressed = true; Invalidate(); base.OnMouseDown(e); }
        protected override void OnMouseUp(MouseEventArgs e) { pressed = false; Invalidate(); base.OnMouseUp(e); }
        protected override void OnGotFocus(EventArgs e) { Invalidate(); base.OnGotFocus(e); }
        protected override void OnLostFocus(EventArgs e) { Invalidate(); base.OnLostFocus(e); }
        protected override bool IsInputKey(Keys k) { return k == Keys.Enter || k == Keys.Space || base.IsInputKey(k); }
        protected override void OnKeyDown(KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Enter || e.KeyCode == Keys.Space) { e.Handled = true; OnClick(EventArgs.Empty); return; }
            base.OnKeyDown(e);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            using (SolidBrush b = new SolidBrush(Theme.BackOf(Parent))) g.FillRectangle(b, ClientRectangle);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            RectangleF r = new RectangleF(0.5f, 0.5f, Width - 1.5f, Height - 1.5f);
            using (GraphicsPath p = Theme.Round(r, (Height - 1) / 2f))
            {
                using (SolidBrush b = new SolidBrush(Theme.Tint(Color, pressed ? 0.78f : hover ? 0.84f : 0.90f))) g.FillPath(b, p);
                using (Pen pen = new Pen(Theme.Tint(Color, hover ? 0.5f : 0.7f))) g.DrawPath(pen, p);
            }
            if (Focused && ShowFocusCues)
                using (GraphicsPath p = Theme.Round(new RectangleF(1.5f, 1.5f, Width - 4, Height - 4), (Height - 4) / 2f))
                using (Pen pen = new Pen(Theme.Text, 2f)) g.DrawPath(pen, p);
            Color fg = Theme.Readable(Color);
            TextRenderer.DrawText(g, Glyph, Theme.IconFont(11f), Lang.Mirror(new Rectangle(Width - 36, 0, 26, Height), Width), fg, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
            TextRenderer.DrawText(g, Lang.T(Text), Theme.FontBold, Lang.Mirror(new Rectangle(8, 0, Width - 44, Height), Width), fg,
                Lang.Flags(TextFormatFlags.Right | TextFormatFlags.VerticalCenter | TextFormatFlags.RightToLeft | TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix));
            // a chevron at the end says «opens something»
            TextRenderer.DrawText(g, Lang.English ? "" : "", Theme.IconFont(8f), Lang.Mirror(new Rectangle(6, 0, 16, Height), Width), fg, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
        }
    }
}
