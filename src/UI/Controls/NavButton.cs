using System.Collections.Generic;
using System.Data;
using System.Drawing.Drawing2D;
using System.Drawing;
using System.Windows.Forms;
using System;

namespace Stores
{
    // ================================================================== side navigation button
    /// <summary>Windows 11 navigation item: icon + text on the light pane, a rounded highlight on hover,
    /// and for the active item a rounded fill with a short accent bar at the reading start (right).</summary>
    class NavButton : Control
    {
        public string Glyph; public bool Active; bool hover, pressed;

        public NavButton(string text, string glyph)
        {
            Text = text; Glyph = glyph;
            Height = 40; Dock = DockStyle.Top; Cursor = Cursors.Hand;
            TabStop = true;
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
            if (e.KeyCode == Keys.Down || e.KeyCode == Keys.Up) { e.Handled = true; Parent.SelectNextControl(this, e.KeyCode == Keys.Up, true, false, true); return; }
            base.OnKeyDown(e);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            using (SolidBrush b = new SolidBrush(Theme.BackOf(Parent))) g.FillRectangle(b, ClientRectangle);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            RectangleF pill = new RectangleF(6, 2, Width - 12, Height - 4);
            Color fill = pressed ? Theme.Pressed : Active ? (hover ? Theme.SidebarHover : Theme.SidebarActive) : hover ? Theme.SidebarHover : Color.Empty;
            if (fill != Color.Empty)
                using (GraphicsPath p = Theme.Round(pill, 5)) using (SolidBrush b = new SolidBrush(fill)) g.FillPath(b, p);
            if (Active)
            {
                // the accent indicator: a short rounded bar at the right edge of the pill
                float h = Math.Min(18, Height - 16);
                using (GraphicsPath p = Theme.Round(Lang.Mirror(new RectangleF(pill.Right - 3, (Height - h) / 2f, 3, h), Width), 1.5f))
                using (SolidBrush b = new SolidBrush(Theme.Primary)) g.FillPath(b, p);
            }
            if (Focused && ShowFocusCues)
                using (GraphicsPath p = Theme.Round(new RectangleF(pill.X + 1, pill.Y + 1, pill.Width - 2, pill.Height - 2), 5))
                using (Pen pen = new Pen(Theme.Text, 2f)) g.DrawPath(pen, p);

            Color fg = Active ? Theme.Text : Palette.Mix(Theme.Text, Theme.Sidebar, 0.1f);   // #303030 on the light pane
            if (Width < 90)
            {
                // collapsed menu: the icon only (the name shows as a tooltip)
                TextRenderer.DrawText(g, Glyph, Theme.IconFont(13f), ClientRectangle, Active ? Theme.Primary : fg,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
                return;
            }
            Rectangle ir = Lang.Mirror(new Rectangle(Width - 6 - 44, 0, 40, Height), Width);
            TextRenderer.DrawText(g, Glyph, Theme.IconFont(12f), ir, Active ? Theme.Primary : fg,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
            Rectangle tr = Lang.Mirror(new Rectangle(8, 0, Width - 6 - 44 - 8, Height), Width);
            TextRenderer.DrawText(g, Lang.T(Text), Active ? Theme.FontBold : Theme.Font, tr, fg,
                Lang.Flags(TextFormatFlags.Right | TextFormatFlags.VerticalCenter | TextFormatFlags.RightToLeft | TextFormatFlags.EndEllipsis | TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix));
        }
    }
}
