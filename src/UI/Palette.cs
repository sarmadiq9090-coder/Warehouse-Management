using System.Drawing;
using System;

namespace Stores
{
    /// <summary>One set of theme colours (light or dark, with an accent colour). Theme exposes the current one;
    /// ThemePainter uses two of them to recolour open windows when the user switches themes.</summary>
    class Palette
    {
        public bool Dark;
        public Color Primary, PrimaryDark, Accent, AccentDark;
        public Color Bg, Layer, Card, ButtonBack, Input, Border, BorderStrong, Sidebar, SidebarHover, SidebarActive, Hover, Pressed;
        public Color Text, Muted, Disabled, Success, Danger, Warning, Purple, Teal, RowAlt, RowWarn, Select;
        public Color GridLine, GridHeader, GridHeaderText, GuideBack, Flash, HeadTint, FootTint, Highlight, ChartGrid;

        /// <summary>Accent colours offered in «المظهر» (the first is the brand blue). Names are shown to the user.</summary>
        public static readonly string[] AccentKeys = { "blue", "green", "teal", "purple", "orange", "graphite" };
        public static readonly string[] AccentNames = { "أزرق", "أخضر", "فيروزي", "بنفسجي", "برتقالي", "رمادي" };
        public static readonly Color[] AccentColors = {
            Color.FromArgb(47, 86, 163),    // #2F56A3 brand blue
            Color.FromArgb(16, 124, 65),    // #107C41
            Color.FromArgb(3, 131, 135),    // #038387
            Color.FromArgb(116, 77, 169),   // #744DA9
            Color.FromArgb(202, 80, 16),    // #CA5010
            Color.FromArgb(81, 92, 107)     // #515C6B
        };

        public static int AccentIndex(string key)
        {
            for (int i = 0; i < AccentKeys.Length; i++) if (AccentKeys[i] == key) return i;
            return 0;
        }

        public static Palette Make(bool dark, string accentKey)
        {
            int ai = AccentIndex(accentKey);
            Color a = AccentColors[ai];
            Palette p = new Palette();
            p.Dark = dark;
            if (!dark)
            {
                // Windows 11 (Fluent) light: Mica-like window background, white cards, hairline borders
                p.Primary = a;
                p.PrimaryDark = ai == 0 ? Color.FromArgb(39, 74, 147) : Mix(a, Color.Black, 0.17f);   // #274A93 for the brand blue
                p.Accent = Color.FromArgb(1, 165, 82);        // brand green #01A552 (receipts, «حفظ»)
                p.AccentDark = Color.FromArgb(1, 140, 70);
                p.Bg = Color.FromArgb(243, 243, 243);
                p.Layer = Color.FromArgb(250, 250, 250);
                p.Card = Color.White;
                p.ButtonBack = Color.White;
                p.Input = Color.White;
                p.Border = Color.FromArgb(229, 229, 229);
                p.BorderStrong = Color.FromArgb(209, 209, 209);
                p.Sidebar = p.Bg;
                p.SidebarHover = Color.FromArgb(234, 234, 234);
                p.SidebarActive = ai == 0 ? Color.FromArgb(228, 231, 237) : Mix(a, p.Bg, 0.92f);
                p.Hover = Color.FromArgb(245, 245, 245);
                p.Pressed = Color.FromArgb(237, 237, 237);
                p.Text = Color.FromArgb(27, 27, 27);
                p.Muted = Color.FromArgb(96, 96, 96);
                p.Disabled = Color.FromArgb(160, 160, 160);
                p.Success = Color.FromArgb(15, 123, 15);
                p.Danger = Color.FromArgb(196, 43, 28);
                p.Warning = Color.FromArgb(202, 120, 0);
                p.Purple = Color.FromArgb(118, 70, 175);
                p.Teal = Color.FromArgb(0, 120, 112);
                p.RowAlt = Color.FromArgb(250, 250, 250);
                p.RowWarn = Color.FromArgb(253, 235, 233);
                p.Select = ai == 0 ? Color.FromArgb(222, 231, 246) : Mix(a, Color.White, 0.85f);
                p.GridLine = Color.FromArgb(150, 158, 170);   // print-like lines of the document grid
                p.GridHeader = Color.White;
                p.GridHeaderText = Color.Black;
                p.GuideBack = Color.FromArgb(234, 246, 239);
                p.Flash = Color.FromArgb(255, 241, 186);
                p.HeadTint = Color.FromArgb(236, 241, 250);
                p.FootTint = Color.FromArgb(247, 249, 252);
                p.Highlight = Color.FromArgb(232, 240, 254);
                p.ChartGrid = Color.FromArgb(238, 238, 238);
            }
            else
            {
                // Windows 11 dark: #202020 window, #2B2B2B cards, light text; accents a little lighter so they read on dark
                p.Primary = Mix(a, Color.White, ai == 5 ? 0.22f : 0.18f);
                p.PrimaryDark = Mix(a, Color.White, 0.42f);
                p.Accent = Color.FromArgb(22, 150, 80);
                p.AccentDark = Color.FromArgb(96, 205, 140);
                p.Bg = Color.FromArgb(32, 32, 32);
                p.Layer = Color.FromArgb(50, 50, 50);
                p.Card = Color.FromArgb(43, 43, 43);
                p.ButtonBack = Color.FromArgb(55, 55, 55);
                p.Input = Color.FromArgb(28, 28, 28);
                p.Border = Color.FromArgb(64, 64, 64);
                p.BorderStrong = Color.FromArgb(96, 96, 96);
                p.Sidebar = p.Bg;
                p.SidebarHover = Color.FromArgb(45, 45, 45);
                p.SidebarActive = Mix(a, Color.FromArgb(45, 45, 45), 0.82f);
                p.Hover = Color.FromArgb(66, 66, 66);
                p.Pressed = Color.FromArgb(40, 40, 40);
                p.Text = Color.FromArgb(240, 240, 240);
                p.Muted = Color.FromArgb(176, 176, 176);
                p.Disabled = Color.FromArgb(112, 112, 112);
                p.Success = Color.FromArgb(92, 190, 92);
                p.Danger = Color.FromArgb(226, 76, 64);
                p.Warning = Color.FromArgb(226, 150, 40);
                p.Purple = Color.FromArgb(165, 125, 220);
                p.Teal = Color.FromArgb(30, 165, 155);
                p.RowAlt = Color.FromArgb(49, 49, 49);
                p.RowWarn = Color.FromArgb(84, 42, 38);
                p.Select = Mix(p.Primary, p.Card, 0.62f);
                p.GridLine = Color.FromArgb(88, 88, 88);
                p.GridHeader = Color.FromArgb(52, 52, 52);
                p.GridHeaderText = p.Text;
                p.GuideBack = Color.FromArgb(28, 58, 40);
                p.Flash = Color.FromArgb(92, 78, 24);
                p.HeadTint = Color.FromArgb(44, 50, 62);
                p.FootTint = Color.FromArgb(38, 38, 38);
                p.Highlight = Mix(p.Primary, p.Card, 0.7f);
                p.ChartGrid = Color.FromArgb(62, 62, 62);
            }
            return p;
        }

        /// <summary>a mixed with b (amount 0..1 of b).</summary>
        public static Color Mix(Color a, Color b, float amount)
        {
            amount = Math.Max(0f, Math.Min(1f, amount));
            return Color.FromArgb((int)Math.Round(a.R + (b.R - a.R) * amount), (int)Math.Round(a.G + (b.G - a.G) * amount), (int)Math.Round(a.B + (b.B - a.B) * amount));
        }

        /// <summary>Background colours, in priority order (the first match wins when two are equal).</summary>
        public Color[] Backs()
        {
            return new Color[] { Card, Bg, Layer, Border, BorderStrong, SidebarHover, SidebarActive, Hover, Pressed, RowAlt, RowWarn, Select,
                GridHeader, GuideBack, Flash, HeadTint, FootTint, Highlight, ChartGrid,
                Primary, PrimaryDark, Accent, AccentDark, Success, Danger, Warning, Purple, Teal, Text, Muted };
        }

        /// <summary>Text colours, in priority order.</summary>
        public Color[] Fores()
        {
            return new Color[] { Text, Muted, Disabled, Primary, PrimaryDark, Accent, AccentDark, Success, Danger, Warning, Purple, Teal,
                GridHeaderText, GridLine, Border, BorderStrong };
        }
    }
}
