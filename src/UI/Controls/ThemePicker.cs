using System.Drawing.Drawing2D;
using System.Drawing;
using System.Windows.Forms;
using System;

namespace Stores
{
    /// <summary>The «المظهر» card content in «الإعدادات»: light / dark / automatic (as Windows) and the accent colour.
    /// A click applies the choice at once to every open window and remembers it for this user.
    /// Keyboard: ←/→ the mode, ↑/↓ the colour.</summary>
    class ThemePicker : Control
    {
        static readonly string[] Modes = { Theme.ModeLight, Theme.ModeDark, Theme.ModeAuto };
        static readonly string[] ModeNames = { "فاتح", "داكن", "تلقائي (حسب ويندوز)" };
        Rectangle[] modeRects = new Rectangle[3], swatchRects = new Rectangle[Palette.AccentKeys.Length];
        int hoverMode = -1, hoverSwatch = -1;
        ToolTip tips = new ToolTip();
        static readonly Font BoldSmall = new Font(Theme.FontName, 9f, FontStyle.Bold);

        public ThemePicker()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.ResizeRedraw | ControlStyles.Selectable, true);
            TabStop = true; Height = 112; Cursor = Cursors.Hand;
        }

        protected override void Dispose(bool disposing) { if (disposing) tips.Dispose(); base.Dispose(disposing); }

        void Layout2()
        {
            int x = Width - 2, y = 6;
            int lw = 52;
            x -= lw;
            using (Graphics g = CreateGraphics())
                for (int i = 0; i < 3; i++)
                {
                    int w = TextRenderer.MeasureText(g, Lang.T(ModeNames[i]), Theme.FontSmall).Width + 18;
                    modeRects[i] = new Rectangle(x - w, y, w, 30);
                    x -= w + 6;
                }
            x = Width - 2 - lw; y = 48;
            for (int i = 0; i < swatchRects.Length; i++) { swatchRects[i] = new Rectangle(x - 28, y, 28, 28); x -= 36; }
            for (int i = 0; i < 3; i++) modeRects[i] = Lang.Mirror(modeRects[i], Width);   // English: from the left
            for (int i = 0; i < swatchRects.Length; i++) swatchRects[i] = Lang.Mirror(swatchRects[i], Width);
        }

        protected override void OnResize(EventArgs e) { Layout2(); base.OnResize(e); }

        protected override void OnPaint(PaintEventArgs e)
        {
            Layout2();
            Graphics g = e.Graphics;
            using (SolidBrush b = new SolidBrush(Theme.BackOf(Parent))) g.FillRectangle(b, ClientRectangle);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            TextFormatFlags rtl = Lang.Flags(TextFormatFlags.Right | TextFormatFlags.VerticalCenter | TextFormatFlags.RightToLeft | TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix);
            TextRenderer.DrawText(g, Lang.T("الوضع"), Theme.FontSmall, Lang.Mirror(new Rectangle(Width - 52, 6, 50, 30), Width), Theme.Muted, rtl);
            TextRenderer.DrawText(g, Lang.T("اللون"), Theme.FontSmall, Lang.Mirror(new Rectangle(Width - 52, 48, 50, 28), Width), Theme.Muted, rtl);

            int mi = Array.IndexOf(Modes, Theme.Mode); if (mi < 0) mi = 0;
            for (int i = 0; i < 3; i++)
            {
                bool on = i == mi;
                Color back = on ? Theme.Primary : i == hoverMode ? Theme.Hover : Theme.ButtonBack;
                using (GraphicsPath p = Theme.Round(new RectangleF(modeRects[i].X + 0.5f, modeRects[i].Y + 0.5f, modeRects[i].Width - 1, modeRects[i].Height - 1), 15))
                {
                    using (SolidBrush b = new SolidBrush(back)) g.FillPath(b, p);
                    if (!on) using (Pen pen = new Pen(Theme.BorderStrong)) g.DrawPath(pen, p);
                }
                TextRenderer.DrawText(g, Lang.T(ModeNames[i]), on ? BoldSmall : Theme.FontSmall, modeRects[i], on ? Color.White : Theme.Text,
                    Lang.Flags(TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.RightToLeft | TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix));
            }

            int ai = Palette.AccentIndex(Theme.AccentKey);
            for (int i = 0; i < swatchRects.Length; i++)
            {
                Rectangle r = swatchRects[i];
                Color c = Palette.AccentColors[i];
                if (i == ai || i == hoverSwatch)
                    using (Pen pen = new Pen(i == ai ? Theme.Text : Theme.BorderStrong, 2f)) g.DrawEllipse(pen, r.X + 1, r.Y + 1, r.Width - 2, r.Height - 2);
                using (SolidBrush b = new SolidBrush(c)) g.FillEllipse(b, r.X + 5, r.Y + 5, r.Width - 10, r.Height - 10);
                if (i == ai) TextRenderer.DrawText(g, "", Theme.IconFont(8f), new Rectangle(r.X + 5, r.Y + 5, r.Width - 10, r.Height - 10), Color.White,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
            }

            string note = "اللون: " + Palette.AccentNames[ai] + " • يُطبّق فورا ويُحفظ لهذا المستخدم";
            TextRenderer.DrawText(g, Lang.T(note), Theme.FontSmall, new Rectangle(0, 84, Width - 2, 24), Theme.Muted, rtl | TextFormatFlags.EndEllipsis);
            if (Focused && ShowFocusCues)
                using (Pen pen = new Pen(Theme.Text) { DashStyle = DashStyle.Dot }) g.DrawRectangle(pen, 0, 0, Width - 1, Height - 1);
        }

        int Hit(Rectangle[] rs, Point p) { for (int i = 0; i < rs.Length; i++) if (rs[i].Contains(p)) return i; return -1; }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            int m = Hit(modeRects, e.Location), s = Hit(swatchRects, e.Location);
            if (m != hoverMode || s != hoverSwatch)
            {
                hoverMode = m; hoverSwatch = s; Invalidate();
                tips.SetToolTip(this, s >= 0 ? Lang.T(Palette.AccentNames[s]) : "");
            }
            Cursor = m >= 0 || s >= 0 ? Cursors.Hand : Cursors.Default;
            base.OnMouseMove(e);
        }

        protected override void OnMouseLeave(EventArgs e) { hoverMode = hoverSwatch = -1; Invalidate(); base.OnMouseLeave(e); }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            Focus();
            int m = Hit(modeRects, e.Location), s = Hit(swatchRects, e.Location);
            if (m >= 0) Choose(Modes[m], Theme.AccentKey);
            else if (s >= 0) Choose(Theme.Mode, Palette.AccentKeys[s]);
            base.OnMouseDown(e);
        }

        protected override bool IsInputKey(Keys k) { return k == Keys.Left || k == Keys.Right || k == Keys.Up || k == Keys.Down || base.IsInputKey(k); }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            int mi = Math.Max(0, Array.IndexOf(Modes, Theme.Mode)), ai = Palette.AccentIndex(Theme.AccentKey), n = Palette.AccentKeys.Length;
            if (e.KeyCode == Keys.Left) Choose(Modes[(mi + 1) % 3], Theme.AccentKey);          // RTL: left = next
            else if (e.KeyCode == Keys.Right) Choose(Modes[(mi + 2) % 3], Theme.AccentKey);
            else if (e.KeyCode == Keys.Down) Choose(Theme.Mode, Palette.AccentKeys[(ai + 1) % n]);
            else if (e.KeyCode == Keys.Up) Choose(Theme.Mode, Palette.AccentKeys[(ai + n - 1) % n]);
            else { base.OnKeyDown(e); return; }
            e.Handled = true;
        }

        protected override void OnGotFocus(EventArgs e) { Invalidate(); base.OnGotFocus(e); }
        protected override void OnLostFocus(EventArgs e) { Invalidate(); base.OnLostFocus(e); }

        void Choose(string mode, string accent)
        {
            if (mode == Theme.Mode && accent == Theme.AccentKey) return;
            UI.WaitCursor(true);
            try { Theme.SetUser(mode, accent); }
            finally { UI.WaitCursor(false); }
            Invalidate();
        }
    }
}
