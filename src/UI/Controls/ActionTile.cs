using System.Collections.Generic;
using System.Data;
using System.Drawing.Drawing2D;
using System.Drawing;
using System.Windows.Forms;
using System;

namespace Stores
{
    // ================================================================== dashboard
    /// <summary>Large action tile of the home screen (Windows 11 card): a tinted icon badge, a title and a one line
    /// explanation. Compact = icon and title only (advanced mode). Hover tints the card, Enter / Space click it.</summary>
    class ActionTile : Control
    {
        public string Title, Desc, Glyph; public Color Color; public bool Compact;
        bool hover, pressed;

        public ActionTile(string glyph, string title, string desc, Color color)
        {
            Glyph = glyph; Title = title; Desc = desc; Color = color;
            Size = new Size(240, 132); Margin = new Padding(6); Cursor = Cursors.Hand;
            TabStop = true; Text = title;
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.ResizeRedraw | ControlStyles.Selectable, true);
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
            Color back = pressed ? Theme.Tint(Color, 0.88f) : hover ? Theme.Tint(Color, 0.95f) : Theme.Card;
            Color border = hover || pressed ? Theme.Tint(Color, 0.55f) : Theme.Border;
            using (GraphicsPath p = Theme.Round(r, 8))
            {
                using (SolidBrush b = new SolidBrush(back)) g.FillPath(b, p);
                using (Pen pen = new Pen(border)) g.DrawPath(pen, p);
            }
            if (Focused && ShowFocusCues)
                using (GraphicsPath p = Theme.Round(new RectangleF(1.5f, 1.5f, Width - 4, Height - 4), 7))
                using (Pen pen = new Pen(Theme.Text, 2f)) g.DrawPath(pen, p);

            int d = Compact ? 36 : 44;
            Rectangle badge = Compact ? new Rectangle((Width - d) / 2, Math.Max(8, (Height - d - 44) / 2), d, d) : Lang.Mirror(new Rectangle(Width - d - 18, 18, d, d), Width);
            using (GraphicsPath p = Theme.Round(badge, 8)) using (SolidBrush b = new SolidBrush(Theme.Tint(Color, hover ? 0.80f : 0.86f))) g.FillPath(b, p);
            TextRenderer.DrawText(g, Glyph, Theme.IconFont(Compact ? 15f : 17f), badge, Color, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);

            TextFormatFlags one = TextFormatFlags.RightToLeft | TextFormatFlags.EndEllipsis | TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix;
            if (Compact)
            {
                // narrow tiles: a long title («مردود إخراج مخزني») wraps to two lines instead of being cut
                TextRenderer.DrawText(g, Lang.T(Title), Theme.FontBold, new Rectangle(6, badge.Bottom + 6, Width - 12, Height - badge.Bottom - 8), Theme.Text,
                    (Lang.English ? 0 : TextFormatFlags.RightToLeft) | TextFormatFlags.WordBreak | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix | TextFormatFlags.HorizontalCenter);
                return;
            }
            using (Font f1 = new Font(Theme.FontName, 12f, FontStyle.Bold))
            {
                int y = badge.Bottom + 10;
                TextRenderer.DrawText(g, Lang.T(Title), f1, Lang.Mirror(new Rectangle(14, y, Width - 32, 26), Width), Theme.Text, Lang.Flags(one | TextFormatFlags.Right));
                TextRenderer.DrawText(g, Lang.T(Desc), Theme.FontSmall, Lang.Mirror(new Rectangle(14, y + 27, Width - 32, Height - y - 30), Width), Theme.Muted,
                    Lang.Flags(TextFormatFlags.RightToLeft | TextFormatFlags.Right | TextFormatFlags.WordBreak | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix));
            }
        }
    }
}
