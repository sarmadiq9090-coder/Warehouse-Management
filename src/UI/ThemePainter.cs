using System.Collections.Generic;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using System;

namespace Stores
{
    /// <summary>Applies the theme to standard WinForms controls. Screens are built with Theme.* colours; this class
    /// (1) in the dark theme fixes what screens leave at the Windows defaults or set to fixed light colours (text boxes,
    /// lists, grids, «Color.White» panels), also for controls added later, and (2) when the user switches themes,
    /// recolours every open window from the old palette to the new one, so nothing has to be rebuilt.
    /// Custom-painted controls (FluentButton, NavButton, cards, tiles) read Theme.* when they paint.
    /// Not themable in WinForms: message boxes and tooltips (Windows draws them), the date picker's box and calendar,
    /// and the report pages (HTML, kept light for printing).</summary>
    static class ThemePainter
    {
        [DllImport("dwmapi.dll")] static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);
        [DllImport("uxtheme.dll", CharSet = CharSet.Unicode)] static extern int SetWindowTheme(IntPtr hwnd, string app, string idList);

        static readonly HashSet<Control> hooked = new HashSet<Control>();

        class Maps
        {
            public Palette From, To;
            public Dictionary<int, Color> Back = new Dictionary<int, Color>(), Fore = new Dictionary<int, Color>();
        }

        static Maps Build(Palette from, Palette to)
        {
            Maps m = new Maps(); m.From = from; m.To = to;
            Color[] fb = from.Backs(), tb = to.Backs();
            for (int i = 0; i < fb.Length; i++) if (!m.Back.ContainsKey(fb[i].ToArgb())) m.Back[fb[i].ToArgb()] = tb[i];
            Color[] ff = from.Fores(), tf = to.Fores();
            for (int i = 0; i < ff.Length; i++) if (!m.Fore.ContainsKey(ff[i].ToArgb())) m.Fore[ff[i].ToArgb()] = tf[i];
            if (!from.Dark && to.Dark)
            {
                // Windows defaults that screens did not set
                Add(m.Back, SystemColors.Control, to.Bg);
                Add(m.Back, SystemColors.AppWorkspace, to.Card);
                Add(m.Back, SystemColors.Window, to.Card);
                Add(m.Fore, SystemColors.ControlText, to.Text);
                Add(m.Fore, SystemColors.WindowText, to.Text);
                Add(m.Fore, SystemColors.GrayText, to.Muted);
                Add(m.Fore, Color.Blue, to.Primary);          // LinkLabel default
            }
            return m;
        }

        static void Add(Dictionary<int, Color> d, Color k, Color v) { if (!d.ContainsKey(k.ToArgb())) d[k.ToArgb()] = v; }

        static bool Map(Dictionary<int, Color> d, Color c, out Color r)
        {
            r = c;
            if (c.IsEmpty || c.A != 255) return false;
            return d.TryGetValue(c.ToArgb(), out r);
        }

        static Color MapBack(Maps m, Color c) { Color r; return Map(m.Back, c, out r) ? r : c; }
        static Color MapFore(Maps m, Color c) { Color r; return Map(m.Fore, c, out r) ? r : c; }
        static bool Is(Color c, Color k) { return !c.IsEmpty && c.A == 255 && c.ToArgb() == k.ToArgb(); }

        /// <summary>Prepares a window in the current theme (call at the end of its constructor or in OnLoad):
        /// dark title bar, inputs and grids, and the controls added to it later.</summary>
        public static void Attach(Form f)
        {
            if (!Theme.IsDark) return;
            Walk(f, Build(Palette.Make(false, Theme.AccentKey), Theme.Current), true, false);
            if (f.IsHandleCreated) TitleBar(f, true);
            else f.HandleCreated += delegate { TitleBar(f, Theme.IsDark); };
        }

        /// <summary>Recolours every open window from the old palette to the new one (Theme.Use).</summary>
        public static void Repaint(Palette from, Palette to)
        {
            Maps m = Build(from, to);
            List<Form> forms = new List<Form>();
            foreach (Form f in Application.OpenForms) forms.Add(f);
            foreach (Form f in forms)
            {
                f.SuspendLayout();
                Walk(f, m, to.Dark, true);
                f.ResumeLayout();
                if (f.IsHandleCreated) TitleBar(f, to.Dark);
                f.Invalidate(true);
            }
        }

        static void OnAdded(object sender, ControlEventArgs e)
        {
            if (!Theme.IsDark || e.Control == null) return;
            Walk(e.Control, Build(Palette.Make(false, Theme.AccentKey), Theme.Current), true, false);
        }

        static void Walk(Control c, Maps m, bool hook, bool deep)
        {
            if (c is WebBrowser) return;   // report pages stay light (printing)
            Paint(c, m, deep);
            if (hook && hooked.Add(c))
            {
                c.ControlAdded += OnAdded;
                c.Disposed += delegate(object s, EventArgs e) { hooked.Remove((Control)s); };
            }
            foreach (Control ch in c.Controls) Walk(ch, m, hook, deep);
        }

        static void SetBack(Control c, Color v) { if (v.ToArgb() != c.BackColor.ToArgb() || c.BackColor.A != 255) c.BackColor = v; }

        static void Paint(Control c, Maps m, bool deep)
        {
            Palette from = m.From, to = m.To;
            Color back = c.BackColor, r;
            bool input = c is TextBoxBase || c is ListBox || c is ComboBox || c is UpDownBase || c is DateTimePicker;
            TextBoxBase tbb = c as TextBoxBase;
            if (tbb != null && tbb.BorderStyle == BorderStyle.None && c.Parent != null && (Is(back, from.Input) || Is(back, from.Card) || !from.Dark && Is(back, SystemColors.Window)))
                SetBack(c, Theme.BackOf(c.Parent));   // a borderless box inside a framed panel takes the panel's colour
            else if (input && (Is(back, from.Input) || Is(back, from.Card) || (!from.Dark && (Is(back, SystemColors.Window) || Is(back, SystemColors.Control)))))
                SetBack(c, to.Input);
            else if (c is FluentButton && Is(back, from.ButtonBack)) SetBack(c, to.ButtonBack);
            else if (Map(m.Back, back, out r)) SetBack(c, r);
            if (Map(m.Fore, c.ForeColor, out r) && r.ToArgb() != c.ForeColor.ToArgb()) c.ForeColor = r;

            LinkLabel ll = c as LinkLabel;
            if (ll != null)
            {
                ll.LinkColor = MapFore(m, ll.LinkColor);
                ll.ActiveLinkColor = MapFore(m, ll.ActiveLinkColor);
                ll.VisitedLinkColor = MapFore(m, ll.VisitedLinkColor);
            }
            ActionTile at = c as ActionTile; if (at != null) at.Color = MapBack(m, at.Color);
            AlertChip ac = c as AlertChip; if (ac != null) ac.Color = MapBack(m, ac.Color);
            RoundPanel rp = c as RoundPanel; if (rp != null) { Color bc = rp.BorderColor; if (!bc.IsEmpty) rp.BorderColor = MapBack(m, bc); }
            ButtonBase bb = c as ButtonBase;
            if (bb != null && !(c is FluentButton) && bb.FlatStyle == FlatStyle.Flat)
            {
                if (!bb.FlatAppearance.MouseOverBackColor.IsEmpty) bb.FlatAppearance.MouseOverBackColor = MapBack(m, bb.FlatAppearance.MouseOverBackColor);
                if (!bb.FlatAppearance.MouseDownBackColor.IsEmpty) bb.FlatAppearance.MouseDownBackColor = MapBack(m, bb.FlatAppearance.MouseDownBackColor);
            }
            DataGridView g = c as DataGridView;
            if (g != null) Grid(g, m, deep);

            if (from.Dark == to.Dark && !to.Dark) return;   // light to light (another accent): nothing else changes
            // dark scroll bars and borders where Windows has a dark theme for the control
            if (c is ScrollBar || c is DataGridView || c is TextBoxBase || c is ListBox || c is ComboBox || c is DateTimePicker || (c is ScrollableControl && ((ScrollableControl)c).AutoScroll))
                WindowTheme(c, to.Dark, c is ComboBox || c is TextBoxBase || c is DateTimePicker ? "DarkMode_CFD" : "DarkMode_Explorer");
        }

        static void Grid(DataGridView g, Maps m, bool deep)
        {
            Palette from = m.From, to = m.To;
            g.BackgroundColor = Is(g.BackgroundColor, from.Card) || Is(g.BackgroundColor, SystemColors.AppWorkspace) && !from.Dark ? to.Card : MapBack(m, g.BackgroundColor);
            Color gc = g.GridColor, r;
            if (Is(gc, from.GridLine)) g.GridColor = to.GridLine;
            else if (Map(m.Back, gc, out r)) g.GridColor = r;
            else if (!from.Dark && to.Dark && gc.GetBrightness() > 0.5f) g.GridColor = to.Border;   // Windows default grey lines
            Header(g.ColumnHeadersDefaultCellStyle, m);
            Style(g.DefaultCellStyle, m);
            Style(g.AlternatingRowsDefaultCellStyle, m);
            Style(g.RowsDefaultCellStyle, m);
            foreach (DataGridViewColumn col in g.Columns) if (col.HasDefaultCellStyle) Style(col.DefaultCellStyle, m);
            if (deep)
                for (int i = 0; i < g.Rows.Count; i++)
                {
                    DataGridViewRow row = g.Rows.SharedRow(i);   // SharedRow: does not unshare (large lists stay light)
                    if (row.HasDefaultCellStyle) Style(row.DefaultCellStyle, m);
                }
        }

        static void Header(DataGridViewCellStyle s, Maps m)
        {
            Palette from = m.From, to = m.To;
            if (Is(s.BackColor, from.GridHeader)) s.BackColor = to.GridHeader; else s.BackColor = MapBack(m, s.BackColor);
            if (Is(s.SelectionBackColor, from.GridHeader)) s.SelectionBackColor = to.GridHeader; else s.SelectionBackColor = MapBack(m, s.SelectionBackColor);
            s.ForeColor = MapFore(m, s.ForeColor);
            s.SelectionForeColor = MapFore(m, s.SelectionForeColor);
        }

        static void Style(DataGridViewCellStyle s, Maps m)
        {
            if (!s.BackColor.IsEmpty) s.BackColor = MapBack(m, s.BackColor);
            if (!s.ForeColor.IsEmpty) s.ForeColor = MapFore(m, s.ForeColor);
            if (!s.SelectionBackColor.IsEmpty) s.SelectionBackColor = MapBack(m, s.SelectionBackColor);
            if (!s.SelectionForeColor.IsEmpty) s.SelectionForeColor = MapFore(m, s.SelectionForeColor);
        }

        static void WindowTheme(Control c, bool dark, string name)
        {
            if (c.IsHandleCreated) { Set(c, dark, name); return; }
            if (!dark) return;
            EventHandler h = null;
            h = delegate { c.HandleCreated -= h; Set(c, Theme.IsDark, name); };
            c.HandleCreated += h;
        }

        static void Set(Control c, bool dark, string name)
        {
            try { SetWindowTheme(c.Handle, dark ? name : null, null); } catch { }
        }

        /// <summary>Dark title bar on Windows 10 (2004+) and 11 (DWMWA_USE_IMMERSIVE_DARK_MODE; 19 on older builds).</summary>
        public static void TitleBar(Form f, bool dark)
        {
            int v = dark ? 1 : 0;
            try
            {
                if (DwmSetWindowAttribute(f.Handle, 20, ref v, 4) != 0) DwmSetWindowAttribute(f.Handle, 19, ref v, 4);
            }
            catch { }
        }
    }
}
