using System.Collections.Generic;
using System.ComponentModel;
using System.Drawing;
using System.IO;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Forms;
using System;

namespace Stores
{
    /// <summary>English UI as a runtime layer over the Arabic program: texts stay Arabic in the code and are looked up
    /// in lang-en.txt (embedded) when shown. Apply(form) translates a control tree and turns it left-to-right;
    /// owner-drawn controls call T / Flags / Mirror while painting. Unknown texts (data) pass through unchanged.
    /// The language: settings.ini «Language» (this computer, asked on the first start) and «lang_user» + user no
    /// in the data (each user's own choice).</summary>
    static class Lang
    {
        public static bool English;
        /// <summary>The language was switched on the login screen: it becomes the user's choice after signing in.</summary>
        public static bool ChosenAtLogin;

        public static string Dir { get { return English ? "ltr" : "rtl"; } }
        public static string Code { get { return English ? "en" : "ar"; } }

        // ------------------------------------------------------------ choosing the language

        /// <summary>Program start: the computer's language (settings.ini); asks once on the very first start.</summary>
        public static void Init()
        {
            string v = Settings.Get("Language", "");
            if (v != "ar" && v != "en")
            {
                using (LanguageForm f = new LanguageForm()) { f.ShowDialog(); v = f.Choice; }
                SetDefault(v == "en");
            }
            English = v == "en";
        }

        public static void SetDefault(bool en)
        {
            Settings.Set("Language", en ? "en" : "ar");
            try { Settings.Save(); } catch { }
        }

        /// <summary>After signing in: the user's own choice, else the computer's language.</summary>
        public static void LoadUser()
        {
            if (ChosenAtLogin) { ChosenAtLogin = false; SetUser(English); return; }
            string v = "";
            try { v = Db.GetSetting("lang_user" + Session.UserNo, ""); } catch { }
            if (v == "") v = Settings.Get("Language", "ar");
            English = v == "en";
        }

        public static void SetUser(bool en)
        {
            English = en;
            if (Session.UserNo > 0) Db.SetSetting("lang_user" + Session.UserNo, en ? "en" : "ar");
        }

        // ------------------------------------------------------------ dictionary

        static Dictionary<string, string> dict;
        static Dictionary<string, List<string>> byFirstWord;

        static Dictionary<string, string> Dict { get { if (dict == null) Load(null); return dict; } }

        /// <summary>Loads the dictionary (null = the embedded lang-en.txt).</summary>
        public static void Load(string text)
        {
            if (text == null)
            {
                Stream s = Assembly.GetExecutingAssembly().GetManifestResourceStream("lang-en.txt");
                if (s != null) using (StreamReader r = new StreamReader(s, Encoding.UTF8)) text = r.ReadToEnd();
            }
            Dictionary<string, string> d = new Dictionary<string, string>(StringComparer.Ordinal);
            Dictionary<string, List<string>> idx = new Dictionary<string, List<string>>(StringComparer.Ordinal);
            foreach (string raw in (text ?? "").Split('\n'))
            {
                string line = raw.TrimEnd('\r');
                int t = line.IndexOf('\t');
                if (line.Length == 0 || line[0] == '#' || t <= 0) continue;
                string k = LangKey.Unescape(line.Substring(0, t)), v = LangKey.Unescape(line.Substring(t + 1));
                if (v.Trim().Length == 0) continue;
                d[k] = v;
                if (k.Length < 2) continue;   // single letters (unit abbreviations) only as whole texts
                int e = 0; while (e < k.Length && LangKey.IsWord(k[e])) e++;
                if (e == 0) continue;
                string w = k.Substring(0, e);
                List<string> l;
                if (!idx.TryGetValue(w, out l)) { l = new List<string>(); idx[w] = l; }
                l.Add(k);
            }
            foreach (List<string> l in idx.Values) l.Sort(delegate(string a, string b) { return b.Length.CompareTo(a.Length); });
            dict = d; byFirstWord = idx;
        }

        public static int Count { get { return Dict.Count; } }

        /// <summary>The English text: the whole text, then its core (without edge spaces / punctuation), then every
        /// known phrase inside it («رقم 123» → «No. 123»). Numbers, dates and unknown words stay as they are.</summary>
        public static string T(string s)
        {
            if (!English || string.IsNullOrEmpty(s) || !LangKey.HasArabic(s)) return s;
            string en = Whole(s);
            return en ?? Phrases(s);
        }

        /// <summary>For data (grid cells, combo items): only a whole known text is translated, never parts of a name.</summary>
        public static string TCell(string s)
        {
            if (!English || string.IsNullOrEmpty(s) || !LangKey.HasArabic(s)) return s;
            return Whole(s) ?? s;
        }

        /// <summary>T for any language (tests).</summary>
        public static string ToEnglish(string s) { bool e = English; English = true; try { return T(s); } finally { English = e; } }

        static string Whole(string s)
        {
            string en;
            if (Dict.TryGetValue(s, out en)) return en;
            int a = LangKey.CoreStart(s), b = LangKey.CoreEnd(s);
            if (b > a && Dict.TryGetValue(s.Substring(a, b - a), out en)) return Edge(s.Substring(0, a)) + en + Edge(s.Substring(b));
            return null;
        }

        /// <summary>Arabic punctuation at the edges becomes its Latin form («،» «؛» «؟» «»»).</summary>
        static string Edge(string s)
        {
            if (s.Length == 0) return s;
            StringBuilder sb = new StringBuilder(s.Length);
            foreach (char c in s)
            {
                // Arabic punctuation → Latin; arrows that point «back» / «forward» turn round
                switch (c)
                {
                    case '،': sb.Append(','); break;        // ،
                    case '؛': sb.Append(';'); break;        // ؛
                    case '؟': sb.Append('?'); break;        // ؟
                    case '«': case '»': sb.Append('"'); break;
                    case '►': sb.Append('◄'); break;   // ► ◄
                    case '◄': sb.Append('►'); break;
                    case '▶': sb.Append('◀'); break;   // ▶ ◀
                    case '◀': sb.Append('▶'); break;
                    case '→': sb.Append('←'); break;   // → ←
                    case '←': sb.Append('→'); break;
                    default: sb.Append(c); break;
                }
            }
            return sb.ToString();
        }

        static string Phrases(string s)
        {
            Dictionary<string, string> d = Dict;
            StringBuilder sb = new StringBuilder(s.Length + 16);
            int i = 0, n = s.Length;
            while (i < n)
            {
                char c = s[i];
                if (!LangKey.IsWord(c)) { sb.Append(Edge(c.ToString())); i++; continue; }
                int j = i; while (j < n && LangKey.IsWord(s[j])) j++;
                string w = s.Substring(i, j - i);
                List<string> cands; bool hit = false;
                if (LangKey.HasArabic(w) && byFirstWord.TryGetValue(w, out cands))
                    foreach (string k in cands)
                    {
                        int end = i + k.Length;
                        if (end > n || string.CompareOrdinal(s, i, k, 0, k.Length) != 0) continue;
                        if (end < n && LangKey.IsWord(s[end])) continue;
                        sb.Append(d[k]); i = end; hit = true; break;
                    }
                if (!hit) { sb.Append(w); i = j; }
            }
            return sb.ToString();
        }

        // ------------------------------------------------------------ owner-drawn controls

        /// <summary>Text flags for the current language: English drops RightToLeft and swaps Left / Right alignment.</summary>
        public static TextFormatFlags Flags(TextFormatFlags f)
        {
            if (!English) return f;
            f &= ~TextFormatFlags.RightToLeft;
            if ((f & TextFormatFlags.HorizontalCenter) != 0) return f;
            return (f & TextFormatFlags.Right) != 0 ? f & ~TextFormatFlags.Right : f | TextFormatFlags.Right;
        }

        /// <summary>The rectangle mirrored inside a control of the given width (English: left ↔ right).</summary>
        public static Rectangle Mirror(Rectangle r, int width) { return English ? new Rectangle(width - r.Right, r.Y, r.Width, r.Height) : r; }
        public static RectangleF Mirror(RectangleF r, float width) { return English ? new RectangleF(width - r.Right, r.Y, r.Width, r.Height) : r; }
        /// <summary>Mirrors r inside the band «within» (a cell or row of a list).</summary>
        public static Rectangle Mirror(Rectangle r, Rectangle within)
        {
            return English ? new Rectangle(within.Left + within.Right - r.Right, r.Y, r.Width, r.Height) : r;
        }

        // ------------------------------------------------------------ control trees

        static readonly ConditionalWeakTable<Control, object> seen = new ConditionalWeakTable<Control, object>();
        static readonly object Mark = new object();

        /// <summary>English: translates the texts of a form / screen and all its children, turns them left-to-right and
        /// mirrors what right-to-left does not mirror by itself (Dock Left/Right, panel padding, absolute positions).
        /// Children added later and texts changed later are handled too. Arabic: does nothing.</summary>
        public static void Apply(Control root)
        {
            if (!English || root == null) return;
            Walk(root, true);
        }

        /// <summary>The control is left as it is (its text is not translated, e.g. the name of the other language).</summary>
        public static void Keep(Control c) { object o; if (!seen.TryGetValue(c, out o)) seen.Add(c, Mark); }

        static void Walk(Control c, bool layout)
        {
            object o;
            if (seen.TryGetValue(c, out o)) return;
            seen.Add(c, Mark);
            Form f = c as Form;
            if (f != null) f.RightToLeftLayout = false;
            if (f != null && !f.IsHandleCreated && f.WindowState != FormWindowState.Normal && f.RightToLeft == RightToLeft.Yes)
            {
                // WinForms creates the window of a maximized form when its RightToLeft changes (it then shows maximized
                // and loses the theme's title bar): change it while the form is «normal»
                FormWindowState ws = f.WindowState;
                f.WindowState = FormWindowState.Normal; f.RightToLeft = RightToLeft.No; f.WindowState = ws;
            }
            if (c.RightToLeft == RightToLeft.Yes) c.RightToLeft = RightToLeft.No;
            c.RightToLeftChanged += delegate { if (English && c.RightToLeft == RightToLeft.Yes) c.RightToLeft = RightToLeft.No; };

            if (c.Dock == DockStyle.Left) c.Dock = DockStyle.Right;
            else if (c.Dock == DockStyle.Right) c.Dock = DockStyle.Left;
            bool selfMirrors = c is FlowLayoutPanel || c is TableLayoutPanel;
            if (!selfMirrors && c.Padding.Left != c.Padding.Right)
                c.Padding = new Padding(c.Padding.Right, c.Padding.Top, c.Padding.Left, c.Padding.Bottom);
            TranslateTexts(c);   // before positioning: an auto-size label keeps its end edge
            Control p = c.Parent;
            if (layout && f == null && c.Dock == DockStyle.None && p != null && !(p is FlowLayoutPanel) && !(p is TableLayoutPanel))
            {
                AnchorStyles a = c.Anchor;
                bool l = (a & AnchorStyles.Left) != 0, r = (a & AnchorStyles.Right) != 0;
                if (l != r) a = (a & ~(AnchorStyles.Left | AnchorStyles.Right)) | (l ? AnchorStyles.Right : AnchorStyles.Left);
                c.Left = p.ClientSize.Width - c.Left - c.Width;
                c.Anchor = a;
            }

            ToolTips(c);

            c.ControlAdded += delegate(object s, ControlEventArgs e) { if (English) Walk(e.Control, false); };
            Control[] kids = new Control[c.Controls.Count];
            c.Controls.CopyTo(kids, 0);
            foreach (Control k in kids) Walk(k, layout);
        }

        static bool OwnText(Control c)
        {
            return c is Label || c is GroupBox || c is TabPage || c is Form || (c is ButtonBase && !(c is FluentButton));
        }

        static void TranslateTexts(Control c)
        {
            if (OwnText(c))
            {
                SetText(c);
                bool busy = false;
                c.TextChanged += delegate { if (busy || !English) return; busy = true; try { SetText(c); } finally { busy = false; } };
            }
            FluentButton fb = c as FluentButton;
            if (fb != null) { Widen(fb); fb.TextChanged += delegate { if (English) Widen(fb); }; }

            ComboBox cb = c as ComboBox;
            if (cb != null && cb.DropDownStyle == ComboBoxStyle.DropDownList)
            {
                cb.Format += delegate(object s, ListControlConvertEventArgs e)
                {
                    string t = e.Value as string;
                    if (t == null && e.ListItem != null && !(e.ListItem is System.Data.DataRowView)) t = e.ListItem.ToString();
                    // plain text items are option lists built by the program («10 • تشرين الأول»): phrases too;
                    // ComboItem / data rows hold names from the data: whole known texts only
                    if (t != null && English) { string en = e.ListItem is string ? T(t) : TCell(t); if (!ReferenceEquals(en, t)) e.Value = en; }
                };
                // turning formatting on refreshes the items already there (the selection is kept)
                if (!cb.FormattingEnabled) cb.FormattingEnabled = true;
                else try { typeof(ComboBox).GetMethod("RefreshItems", BindingFlags.NonPublic | BindingFlags.Instance, null, Type.EmptyTypes, null).Invoke(cb, null); } catch { }
            }

            TextBox tb = c as TextBox;
            if (tb != null)
            {
                // the grey hint of an empty box (EM_SETCUEBANNER, set by the screens when the handle is created)
                tb.HandleCreated += delegate { if (English) Cue(tb); };
                if (tb.IsHandleCreated) Cue(tb);
            }

            ToolStrip ts = c as ToolStrip;
            if (ts != null) foreach (ToolStripItem it in ts.Items) Item(it);

            DataGridView g = c as DataGridView;
            if (g != null)
            {
                foreach (DataGridViewColumn col in g.Columns) Header(col);
                g.ColumnAdded += delegate(object s, DataGridViewColumnEventArgs e) { if (English) Header(e.Column); };
                g.CellPainting += delegate(object s, DataGridViewCellPaintingEventArgs e)
                {
                    if (English && e.RowIndex == -1 && e.ColumnIndex >= 0) Header(g.Columns[e.ColumnIndex]);
                };
                g.CellFormatting += delegate(object s, DataGridViewCellFormattingEventArgs e)
                {
                    if (!English || e.RowIndex < 0 || e.ColumnIndex < 0) return;
                    string v = e.Value as string;
                    if (v == null || !(g.ReadOnly || g.Columns[e.ColumnIndex].ReadOnly)) return;
                    string en = TCell(v);
                    if (!ReferenceEquals(en, v)) { e.Value = en; e.FormattingApplied = true; }
                };
            }
        }

        [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern IntPtr SendMessage(IntPtr h, int msg, StringBuilder w, IntPtr l);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern IntPtr SendMessage(IntPtr h, int msg, IntPtr w, string l);

        static void Cue(TextBox tb)
        {
            try
            {
                StringBuilder sb = new StringBuilder(512);
                if (SendMessage(tb.Handle, 0x1502 /*EM_GETCUEBANNER*/, sb, (IntPtr)sb.Capacity) == IntPtr.Zero) return;
                string s = sb.ToString(), en = T(s);
                if (en != s) SendMessage(tb.Handle, 0x1501 /*EM_SETCUEBANNER*/, (IntPtr)1, en);
            }
            catch { }
        }

        /// <summary>A menu item and its sub-items (context menus, the tray menu).</summary>
        static void Item(ToolStripItem it)
        {
            string t = it.Text, en = T(t);
            if (en != t) it.Text = en;
            if (!string.IsNullOrEmpty(it.ToolTipText)) it.ToolTipText = T(it.ToolTipText);
            ToolStripDropDownItem dd = it as ToolStripDropDownItem;
            if (dd != null) foreach (ToolStripItem sub in dd.DropDownItems) Item(sub);
        }

        static void Header(DataGridViewColumn col)
        {
            string h = col.HeaderText, en = T(h);
            if (!ReferenceEquals(h, en) && h != en)
            {
                col.HeaderText = en;
                // a fixed-width column grows to show its (usually longer) English header on one line
                DataGridView g = col.DataGridView;
                if (g != null)
                {
                    Font f = g.ColumnHeadersDefaultCellStyle.Font ?? g.Font;
                    int need = TextRenderer.MeasureText(en, f).Width + 20;
                    if (col.InheritedAutoSizeMode == DataGridViewAutoSizeColumnMode.Fill) { if (need <= 220 && need > col.MinimumWidth) col.MinimumWidth = need; }
                    else if (need > col.Width && need < col.Width * 3) col.Width = need;
                }
            }
            if (col.ToolTipText != null && LangKey.HasArabic(col.ToolTipText)) col.ToolTipText = T(col.ToolTipText);
            DataGridViewButtonColumn bc = col as DataGridViewButtonColumn;
            if (bc != null && bc.Text != null && LangKey.HasArabic(bc.Text)) bc.Text = T(bc.Text);
        }

        static void SetText(Control c)
        {
            string t = c.Text, en = T(t);
            if (en == t) return;
            c.Text = en;
            // a fixed-width one-line caption in a row grows to fit its (usually longer) English text
            Label l = c as Label;
            if (l == null || l.AutoSize || l.AutoEllipsis || en.IndexOf('\n') >= 0) return;
            if (!(l.Parent is FlowLayoutPanel) && l.Dock != DockStyle.Left && l.Dock != DockStyle.Right) return;
            int need = TextRenderer.MeasureText(en, l.Font).Width + l.Padding.Horizontal + 4;
            if (need > l.Width && need < l.Width * 3) l.Width = need;
        }

        /// <summary>A button in a flowing bar grows to fit its (usually longer) English text.</summary>
        static void Widen(FluentButton b)
        {
            if (!(b.Parent is FlowLayoutPanel) || b.AutoSize || b.Dock != DockStyle.None) return;
            string en = T(b.Text);
            if (en == b.Text) return;
            int need = TextRenderer.MeasureText(en, b.Font).Width + b.Padding.Horizontal + (FluentButton.AutoGlyph(b.Text) != null ? 46 : 22);
            if (need <= b.Width) return;
            // a bar of fixed width docked at a side grows with its buttons
            FlowLayoutPanel bar = (FlowLayoutPanel)b.Parent;
            if (!bar.AutoSize && !bar.WrapContents && (bar.Dock == DockStyle.Left || bar.Dock == DockStyle.Right)) bar.Width += need - b.Width;
            b.Width = need;
        }

        /// <summary>Tool tips set on the control (found through the ToolTip's handlers on the control's events).</summary>
        static void ToolTips(Control c)
        {
            try
            {
                PropertyInfo pi = typeof(Component).GetProperty("Events", BindingFlags.NonPublic | BindingFlags.Instance);
                EventHandlerList list = (EventHandlerList)pi.GetValue(c, null);
                FieldInfo head = typeof(EventHandlerList).GetField("head", BindingFlags.NonPublic | BindingFlags.Instance);
                object e = head == null ? null : head.GetValue(list);
                List<ToolTip> tips = new List<ToolTip>();
                while (e != null)
                {
                    Type et = e.GetType();
                    Delegate d = et.GetField("handler", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(e) as Delegate;
                    if (d != null)
                        foreach (Delegate x in d.GetInvocationList()) { ToolTip tt = x.Target as ToolTip; if (tt != null && !tips.Contains(tt)) tips.Add(tt); }
                    e = et.GetField("next", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(e);
                }
                foreach (ToolTip tt in tips)
                {
                    string s = tt.GetToolTip(c), en = T(s);
                    if (en != s) tt.SetToolTip(c, en);
                }
            }
            catch { }
        }

        // ------------------------------------------------------------ reports

        /// <summary>A translated copy of a report for the screen, print and Excel (data cells: whole known texts only).</summary>
        public static ReportData Report(ReportData r)
        {
            if (!English || r == null) return r;
            ReportData t = new ReportData();
            t.Title = T(r.Title); t.Subtitle = T(r.Subtitle); t.Footer = T(r.Footer);
            foreach (RCol c in r.Cols) t.Cols.Add(new RCol(T(c.Header), c.Kind, c.Total));
            foreach (object[] row in r.Rows)
            {
                object[] x = (object[])row.Clone();
                for (int i = 0; i < x.Length; i++) { string s = x[i] as string; if (s != null) x[i] = TCell(s); }
                t.Rows.Add(x);
            }
            t.RowClass.AddRange(r.RowClass); t.RowLink.AddRange(r.RowLink);
            foreach (KeyValuePair<string, string> kv in r.Summary) t.Summary.Add(new KeyValuePair<string, string>(kv.Key, TCell(kv.Value)));
            if (r.Chart != null)
            {
                t.Chart = new List<KeyValuePair<string, double[]>>();
                foreach (KeyValuePair<string, double[]> kv in r.Chart) t.Chart.Add(new KeyValuePair<string, double[]>(TCell(kv.Key), kv.Value));
            }
            if (r.ChartSeries != null) t.ChartSeries = Array.ConvertAll(r.ChartSeries, T);
            t.Signatures = r.Signatures;
            if (r.SigLabels != null) t.SigLabels = Array.ConvertAll(r.SigLabels, T);
            if (r.Info != null)
            {
                t.Info = new List<KeyValuePair<string, string>>();
                foreach (KeyValuePair<string, string> kv in r.Info) t.Info.Add(new KeyValuePair<string, string>(T(kv.Key), TCell(kv.Value)));
            }
            return t;
        }
    }
}
