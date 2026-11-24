using System.Collections.Generic;
using System.Drawing.Drawing2D;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Text;
using System.Windows.Forms;
using System;

namespace Stores
{
    // ------------------------------------------------------------------ theme
    static class Theme
    {
        // Colours come from the current palette (light / dark × accent, chosen per user in «الإعدادات» → «المظهر»).
        // Screens read them when they are built; ThemePainter recolours open windows when the theme changes.
        static Palette pal = Palette.Make(false, "blue");
        public static Palette Current { get { return pal; } }
        public static bool IsDark { get { return pal.Dark; } }

        // Brand colours of Al-Handasya Electrical Industries (logo blue + green); blue is the default Fluent accent
        public static Color Primary { get { return pal.Primary; } }          // #2F56A3 (or the chosen accent)
        public static Color PrimaryDark { get { return pal.PrimaryDark; } }
        public static Color Accent { get { return pal.Accent; } }            // #01A552 brand green
        public static Color AccentDark { get { return pal.AccentDark; } }

        // Windows 11 (Fluent) neutrals
        public static Color Bg { get { return pal.Bg; } }                    // window / navigation pane
        public static Color Layer { get { return pal.Layer; } }              // subtle layer (status bar, grid headers)
        public static Color Card { get { return pal.Card; } }                // cards, lists, grids
        public static Color ButtonBack { get { return pal.ButtonBack; } }    // standard (secondary) buttons
        public static Color Input { get { return pal.Input; } }              // text boxes, combo boxes, lists
        public static Color Border { get { return pal.Border; } }
        public static Color BorderStrong { get { return pal.BorderStrong; } }
        public static Color Sidebar { get { return pal.Sidebar; } }
        public static Color SidebarHover { get { return pal.SidebarHover; } }
        public static Color SidebarActive { get { return pal.SidebarActive; } }
        public static Color Hover { get { return pal.Hover; } }
        public static Color Pressed { get { return pal.Pressed; } }
        public static Color Text { get { return pal.Text; } }
        public static Color Muted { get { return pal.Muted; } }
        public static Color Disabled { get { return pal.Disabled; } }
        public static Color Success { get { return pal.Success; } }
        public static Color Danger { get { return pal.Danger; } }
        public static Color Warning { get { return pal.Warning; } }
        public static Color Purple { get { return pal.Purple; } }
        public static Color Teal { get { return pal.Teal; } }
        public static Color RowAlt { get { return pal.RowAlt; } }
        public static Color RowWarn { get { return pal.RowWarn; } }
        public static Color Select { get { return pal.Select; } }            // accent tint (selection, hover in lists)
        /// <summary>Print-like grid lines and header of the document grid (DocView).</summary>
        public static Color GridLine { get { return pal.GridLine; } }
        public static Color GridHeader { get { return pal.GridHeader; } }
        public static Color GridHeaderText { get { return pal.GridHeaderText; } }
        public static Color GuideBack { get { return pal.GuideBack; } }      // green guide strip («اختر المادة…»), counted cells
        public static Color Flash { get { return pal.Flash; } }              // a line just added from the entry bar
        public static Color HeadTint { get { return pal.HeadTint; } }        // item drop-down header
        public static Color FootTint { get { return pal.FootTint; } }
        public static Color Highlight { get { return pal.Highlight; } }      // small accent tags
        public static Color ChartGrid { get { return pal.ChartGrid; } }

        // ---- theme choice (per user, like the mode)
        public const string ModeLight = "light", ModeDark = "dark", ModeAuto = "auto";
        public static string Mode = ModeLight;
        public static string AccentKey = "blue";

        /// <summary>Reads the user's theme («theme_user» + number: "mode|accent") and makes it current. Call after login.</summary>
        public static void LoadUser()
        {
            string v = "";
            try { v = Db.GetSetting("theme_user" + Session.UserNo, ""); } catch { }
            string[] p = (v ?? "").Split('|');
            Mode = p[0] == ModeDark || p[0] == ModeAuto ? p[0] : ModeLight;
            AccentKey = p.Length > 1 ? Palette.AccentKeys[Palette.AccentIndex(p[1])] : "blue";
            pal = Palette.Make(Resolve(Mode), AccentKey);
            if (!watching)
            {
                // «تلقائي»: follow Windows when the user switches its app mode while the program is open
                watching = true;
                try
                {
                    Microsoft.Win32.SystemEvents.UserPreferenceChanged += delegate(object s, Microsoft.Win32.UserPreferenceChangedEventArgs e)
                    {
                        if (Mode != ModeAuto || e.Category != Microsoft.Win32.UserPreferenceCategory.General) return;
                        bool dark = Resolve(ModeAuto);
                        if (dark != pal.Dark) Use(Palette.Make(dark, AccentKey));
                    };
                }
                catch { }
            }
        }
        static bool watching;

        /// <summary>Saves the user's choice and applies it at once to every open window.</summary>
        public static void SetUser(string mode, string accent)
        {
            Mode = mode; AccentKey = accent;
            try { Db.SetSetting("theme_user" + Session.UserNo, mode + "|" + accent); } catch { }
            Use(Palette.Make(Resolve(mode), accent));
        }

        /// <summary>Makes a palette current and recolours the open windows.</summary>
        public static void Use(Palette p)
        {
            Palette old = pal;
            pal = p;
            ThemePainter.Repaint(old, p);
        }

        /// <summary>Dark or light for a mode; «تلقائي» follows Windows (Settings → Personalisation → Colours → app mode).</summary>
        public static bool Resolve(string mode)
        {
            if (mode == ModeDark) return true;
            if (mode != ModeAuto) return false;
            try
            {
                using (Microsoft.Win32.RegistryKey k = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize"))
                {
                    object o = k == null ? null : k.GetValue("AppsUseLightTheme");
                    return o is int && (int)o == 0;
                }
            }
            catch { return false; }
        }

        /// <summary>A status colour as text on the current background (a little darker on light, lighter on dark).</summary>
        public static Color Readable(Color c)
        {
            return pal.Dark ? Palette.Mix(c, Color.White, 0.35f) : ControlPaint.Dark(c, 0.1f);
        }

        /// <summary>UI font. Arabic text stays in «Segoe UI»: «Segoe UI Variable» has no Arabic letters
        /// (Windows falls back to a narrower font), so it is used only for Latin-only text (Program.AppName).</summary>
        public const string FontName = "Segoe UI";
        public static readonly string LatinFontName = Installed("Segoe UI Variable Display") ? "Segoe UI Variable Display" : "Segoe UI";
        /// <summary>Icon font that ships with Windows: «Segoe Fluent Icons» (Windows 11) or «Segoe MDL2 Assets» (Windows 10).</summary>
        public static readonly string IconFontName = Installed("Segoe Fluent Icons") ? "Segoe Fluent Icons" : "Segoe MDL2 Assets";

        public static readonly Font Font = new Font(FontName, 10f);
        public static readonly Font FontBold = new Font(FontName, 10f, FontStyle.Bold);
        public static readonly Font FontSmall = new Font(FontName, 9f);
        public static readonly Font FontTitle = new Font(FontName, 15f, FontStyle.Bold);
        public static readonly Font FontBig = new Font(FontName, 20f, FontStyle.Bold);
        public static readonly Font FontMenu = new Font(FontName, 10.5f);
        public static readonly Font FontSubtitle = new Font(FontName, 11.5f, FontStyle.Bold);

        static Dictionary<float, Font> icons = new Dictionary<float, Font>();
        /// <summary>The icon font at a size (cached; glyphs are code points such as "").</summary>
        public static Font IconFont(float size)
        {
            Font f;
            if (!icons.TryGetValue(size, out f)) { f = new Font(IconFontName, size); icons[size] = f; }
            return f;
        }

        static bool Installed(string family)
        {
            try
            {
                using (System.Drawing.Text.InstalledFontCollection c = new System.Drawing.Text.InstalledFontCollection())
                    foreach (FontFamily f in c.Families) if (string.Equals(f.Name, family, StringComparison.OrdinalIgnoreCase)) return true;
            }
            catch { }
            return false;
        }

        /// <summary>Rounded rectangle path (Fluent corners).</summary>
        public static GraphicsPath Round(RectangleF r, float radius)
        {
            GraphicsPath p = new GraphicsPath();
            float d = Math.Min(radius * 2, Math.Min(r.Width, r.Height));
            if (d <= 0.5f) { p.AddRectangle(r); return p; }
            p.AddArc(r.X, r.Y, d, d, 180, 90);
            p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
            p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
            p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
            p.CloseFigure();
            return p;
        }

        /// <summary>Colour mixed with the card colour (amount 0..1: white in the light theme, dark grey in the dark one)
        /// for tinted backgrounds.</summary>
        public static Color Tint(Color c, float white)
        {
            Color b = pal.Card;
            return Color.FromArgb((int)(c.R + (b.R - c.R) * white), (int)(c.G + (b.G - c.G) * white), (int)(c.B + (b.B - c.B) * white));
        }

        /// <summary>The colour actually seen behind a control (first parent with a solid back colour).</summary>
        public static Color BackOf(Control c)
        {
            for (Control p = c; p != null; p = p.Parent) if (p.BackColor.A == 255) return p.BackColor;
            return Bg;
        }

        static Icon icon;
        static Image programImage;
        /// <summary>The program icon as a large picture (256 px, from the embedded app.ico) — shown when no company logo is set.</summary>
        public static Image ProgramImage
        {
            get
            {
                if (programImage == null)
                {
                    try
                    {
                        // app.ico stores PNG frames (System.Drawing.Icon cannot read those): take the largest frame's PNG
                        using (Stream s = typeof(Theme).Assembly.GetManifestResourceStream("app.ico"))
                        using (BinaryReader r = new BinaryReader(s))
                        {
                            r.ReadInt16(); r.ReadInt16(); int n = r.ReadInt16();
                            int best = -1, bestSize = 0, bestLen = 0, bestOff = 0;
                            for (int i = 0; i < n; i++)
                            {
                                int w = r.ReadByte(); r.ReadBytes(7); int len = r.ReadInt32(), off = r.ReadInt32();
                                if (w == 0) w = 256;
                                if (w > bestSize) { best = i; bestSize = w; bestLen = len; bestOff = off; }
                            }
                            if (best >= 0)
                            {
                                s.Position = bestOff;
                                byte[] png = r.ReadBytes(bestLen);
                                using (MemoryStream ms = new MemoryStream(png)) programImage = new Bitmap(Image.FromStream(ms));
                            }
                        }
                    }
                    catch { programImage = null; }
                }
                return programImage;
            }
        }

        public static Icon AppIcon
        {
            get
            {
                // the program icon embedded in the .exe (src\app.ico, made by tools\MakeIcon.cs)
                if (icon == null)
                {
                    try { icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); } catch { icon = null; }
                }
                if (icon == null)
                {
                    // neutral icon: a stack of boxes (warehouse)
                    Bitmap b = new Bitmap(32, 32);
                    using (Graphics g = Graphics.FromImage(b))
                    {
                        g.SmoothingMode = SmoothingMode.AntiAlias;
                        g.Clear(Color.Transparent);
                        using (SolidBrush br = new SolidBrush(Primary)) g.FillRectangle(br, 1, 1, 30, 30);
                        using (SolidBrush br = new SolidBrush(Accent)) g.FillRectangle(br, 1, 26, 30, 5);
                        using (Pen p = new Pen(Color.White, 2f))
                        {
                            g.DrawRectangle(p, 6, 14, 9, 9); g.DrawRectangle(p, 17, 14, 9, 9); g.DrawRectangle(p, 11, 5, 10, 9);
                        }
                    }
                    icon = Icon.FromHandle(b.GetHicon());
                }
                return icon;
            }
        }

        static Image logo; static bool logoTried;
        /// <summary>Company logo (optional, set in «الإعدادات العامة», stored in the database). null = no logo.</summary>
        public static Image Logo
        {
            get
            {
                if (!logoTried)
                {
                    logoTried = true;
                    try
                    {
                        byte[] b = Branding.LogoBytes;
                        if (b != null) using (MemoryStream ms = new MemoryStream(b)) using (Image tmp = Image.FromStream(ms)) logo = new Bitmap(tmp);
                    }
                    catch { logo = null; }
                }
                return logo;
            }
        }

        /// <summary>Called when the logo changes: screens and reports pick up the new one.</summary>
        public static void ResetLogo() { logo = null; logoTried = false; icon = null; }

        public static string LogoDataUri
        {
            get
            {
                byte[] b = Branding.LogoBytes;
                return b == null ? null : "data:image/png;base64," + Convert.ToBase64String(b);
            }
        }
    }
}
