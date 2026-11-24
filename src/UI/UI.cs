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
    // ------------------------------------------------------------------ UI helpers
    static class UI
    {
        public static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

        static MessageBoxOptions Rtl { get { return Lang.English ? 0 : MessageBoxOptions.RtlReading | MessageBoxOptions.RightAlign; } }

        public static void Info(string msg) { MessageBox.Show(Lang.T(msg), Program.AppName, MessageBoxButtons.OK, MessageBoxIcon.Information, MessageBoxDefaultButton.Button1, Rtl); }
        public static void Warn(string msg) { MessageBox.Show(Lang.T(msg), Program.AppName, MessageBoxButtons.OK, MessageBoxIcon.Warning, MessageBoxDefaultButton.Button1, Rtl); }
        public static bool Confirm(string msg)
        {
            return MessageBox.Show(Lang.T(msg), Program.AppName, MessageBoxButtons.YesNo, MessageBoxIcon.Question, MessageBoxDefaultButton.Button2, Rtl) == DialogResult.Yes;
        }
        public static DialogResult YesNoCancel(string msg)
        {
            return MessageBox.Show(Lang.T(msg), Program.AppName, MessageBoxButtons.YesNoCancel, MessageBoxIcon.Question, MessageBoxDefaultButton.Button1, Rtl);
        }

        public static void Error(Exception ex)
        {
            UserError ue = ex as UserError;
            if (ue != null) { Warn(ue.Message); return; }
            string msg = ex.Message;
            if (msg.Contains("could not be found") || msg.Contains("Could not find file")) msg = "ملف البيانات غير متاح. تأكد من الاتصال بالشبكة.\n\n" + msg;
            else if (msg.Contains("locked") || msg.Contains("currently in use")) msg = "السجل مستخدم حاليا من جهاز آخر، حاول مرة ثانية.\n\n" + msg;
            MessageBox.Show(Lang.T("حدث خطأ:\n" + msg), Program.AppName, MessageBoxButtons.OK, MessageBoxIcon.Error, MessageBoxDefaultButton.Button1, Rtl);
            try { File.AppendAllText(Path.Combine(Settings.AppDir, "errors.log"), DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + "  " + ex + "\r\n\r\n", Encoding.UTF8); } catch { }
        }

        public static string Num(double v)
        {
            if (Math.Abs(v) < 0.0000001) return "0";
            return v.ToString("#,##0.###", Inv);
        }

        public static string NumOrEmpty(double v) { return Math.Abs(v) < 0.0000001 ? "" : Num(v); }

        public static string Date(DateTime d) { return d.ToString("yyyy/MM/dd", Inv); }

        public static string NormDigits(string s)
        {
            if (s == null) return "";
            StringBuilder sb = new StringBuilder(s.Length);
            foreach (char c in s)
            {
                if (c >= '٠' && c <= '٩') sb.Append((char)('0' + (c - '٠')));
                else if (c >= '۰' && c <= '۹') sb.Append((char)('0' + (c - '۰')));
                else if (c == '٫' || c == '،') sb.Append('.');
                else if (c == '٬') { }
                else sb.Append(c);
            }
            return sb.ToString();
        }

        public static bool TryNum(string s, out double v)
        {
            s = NormDigits(s ?? "").Replace(",", "").Trim();
            return double.TryParse(s, NumberStyles.Float, Inv, out v);
        }

        /// <summary>Arabic normalisation for searching.</summary>
        public static string Norm(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            StringBuilder sb = new StringBuilder(s.Length);
            foreach (char ch in NormDigits(s).ToLowerInvariant())
            {
                char c = ch;
                if (c == 'أ' || c == 'إ' || c == 'آ' || c == 'ٱ') c = 'ا';
                else if (c == 'ة') c = 'ه';
                else if (c == 'ى') c = 'ي';
                else if (c == 'ؤ') c = 'و';
                else if (c == 'ئ') c = 'ي';
                else if (c >= 'ً' && c <= 'ْ') continue; // harakat
                else if (c == 'ـ') continue;
                sb.Append(c);
            }
            return sb.ToString();
        }

        public static string LikeEscape(string s)
        {
            StringBuilder sb = new StringBuilder();
            foreach (char c in s)
            {
                if (c == '[' || c == ']' || c == '*' || c == '%') sb.Append('[').Append(c).Append(']');
                else if (c == '\'') sb.Append("''");
                else sb.Append(c);
            }
            return sb.ToString();
        }

        public static string BuildFilter(string text, string column)
        {
            string t = Norm(text).Trim();
            if (t.Length == 0) return "";
            List<string> parts = new List<string>();
            foreach (string w in t.Split(new char[] { ' ' }, StringSplitOptions.RemoveEmptyEntries))
                parts.Add(column + " LIKE '%" + LikeEscape(w) + "%'");
            return string.Join(" AND ", parts.ToArray());
        }

        /// <summary>Accent / coloured button (Windows 11 style: rounded, reacts to hover, press and keyboard focus).</summary>
        public static Button Btn(string text, Color back, int width)
        {
            FluentButton b = new FluentButton();
            b.Text = text;
            b.BackColor = back;
            b.ForeColor = back.GetBrightness() > 0.9f || back.ToArgb() == Theme.ButtonBack.ToArgb() ? Theme.Text : Color.White;
            b.Font = Theme.FontBold;
            b.Height = 34;
            b.Width = width;
            b.Cursor = Cursors.Hand;
            b.Margin = new Padding(4, 4, 4, 4);
            return b;
        }

        /// <summary>Standard (secondary) button: white (dark grey in the dark theme) with a hairline border.</summary>
        public static Button BtnLight(string text, int width)
        {
            Button b = Btn(text, Theme.ButtonBack, width);
            b.ForeColor = Theme.Text;
            b.Font = Theme.Font;
            return b;
        }

        public static Label Lbl(string text)
        {
            Label l = new Label();
            l.Text = text;
            l.AutoSize = true;
            l.ForeColor = Theme.Muted;
            l.Font = Theme.Font;
            l.Margin = new Padding(6, 10, 2, 0);
            return l;
        }

        public static void StyleGrid(DataGridView g)
        {
            g.BackgroundColor = Theme.Card;
            g.BorderStyle = BorderStyle.None;
            g.RightToLeft = RightToLeft.Yes;
            g.Font = Theme.Font;
            g.EnableHeadersVisualStyles = false;
            g.ColumnHeadersBorderStyle = DataGridViewHeaderBorderStyle.Single;
            // light Fluent header: semibold dark text on a subtle layer, separated by a hairline
            g.ColumnHeadersDefaultCellStyle.BackColor = Theme.Layer;
            g.ColumnHeadersDefaultCellStyle.ForeColor = Theme.Text;
            g.ColumnHeadersDefaultCellStyle.Font = Theme.FontBold;
            g.ColumnHeadersDefaultCellStyle.SelectionBackColor = Theme.Layer;
            g.ColumnHeadersDefaultCellStyle.SelectionForeColor = Theme.Text;
            g.ColumnHeadersDefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleCenter;
            g.ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.DisableResizing;
            g.ColumnHeadersHeight = 36;
            g.RowTemplate.Height = 30;
            g.DefaultCellStyle.BackColor = Theme.Card;
            g.DefaultCellStyle.SelectionBackColor = Theme.Select;
            g.DefaultCellStyle.SelectionForeColor = Theme.Text;
            g.DefaultCellStyle.ForeColor = Theme.Text;
            g.DefaultCellStyle.Padding = new Padding(4, 0, 4, 0);
            g.AlternatingRowsDefaultCellStyle.BackColor = Theme.RowAlt;
            g.GridColor = Theme.Border;
            g.CellBorderStyle = DataGridViewCellBorderStyle.SingleHorizontal;
            g.RowHeadersVisible = false;
            g.AllowUserToResizeRows = false;
            g.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
            g.AutoGenerateColumns = false;
            typeof(DataGridView).InvokeMember("DoubleBuffered",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.SetProperty,
                null, g, new object[] { true });
        }

        public static void ReadOnlyGrid(DataGridView g)
        {
            StyleGrid(g);
            g.ReadOnly = true;
            g.AllowUserToAddRows = false;
            g.AllowUserToDeleteRows = false;
            g.MultiSelect = false;
        }

        public static DataGridViewTextBoxColumn Col(string name, string header, string prop, int width, bool num)
        {
            DataGridViewTextBoxColumn c = new DataGridViewTextBoxColumn();
            c.Name = name;
            c.HeaderText = header;
            c.DataPropertyName = prop;
            if (width > 0) c.Width = width;
            else c.AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill;
            if (num)
            {
                c.DefaultCellStyle.Format = "#,##0.###";
                c.DefaultCellStyle.FormatProvider = Inv;
                c.DefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleCenter;
            }
            c.SortMode = DataGridViewColumnSortMode.Automatic;
            return c;
        }

        public static DateTimePicker DatePicker(DateTime value)
        {
            DateTimePicker d = new DateTimePicker();
            d.Format = DateTimePickerFormat.Custom;
            d.CustomFormat = "yyyy/MM/dd";
            d.Width = 130;
            d.Value = value;
            d.Font = Theme.Font;
            d.RightToLeft = RightToLeft.No;
            d.Margin = new Padding(4, 6, 4, 4);
            return d;
        }

        public static ComboBox Combo(int width)
        {
            ComboBox c = new ComboBox();
            c.DropDownStyle = ComboBoxStyle.DropDownList;
            c.Width = width;
            c.Font = Theme.Font;
            c.Margin = new Padding(4, 6, 4, 4);
            return c;
        }

        public static TextBox Text(int width)
        {
            TextBox t = new TextBox();
            t.Width = width;
            t.Font = Theme.Font;
            t.Margin = new Padding(4, 6, 4, 4);
            return t;
        }

        public static FlowLayoutPanel Flow()
        {
            FlowLayoutPanel f = new FlowLayoutPanel();
            f.FlowDirection = FlowDirection.LeftToRight; // with RightToLeft=Yes this flows from the right
            f.WrapContents = true;
            f.AutoSize = true;
            f.AutoSizeMode = AutoSizeMode.GrowAndShrink;
            f.Dock = DockStyle.Top;
            f.Padding = new Padding(6, 4, 6, 4);
            f.BackColor = Color.Transparent;
            return f;
        }

        /// <summary>A label and its control kept together on one line when the row wraps.</summary>
        public static FlowLayoutPanel Field(string label, Control c) { return Field(Lbl(label), c); }

        public static FlowLayoutPanel Field(Label label, Control c)
        {
            FlowLayoutPanel p = new FlowLayoutPanel();
            p.AutoSize = true; p.AutoSizeMode = AutoSizeMode.GrowAndShrink;
            p.WrapContents = false; p.FlowDirection = FlowDirection.LeftToRight;
            p.Margin = new Padding(0, 0, 10, 0); p.Padding = new Padding(0);
            p.BackColor = Color.Transparent;
            p.Controls.Add(label);
            p.Controls.Add(c);
            return p;
        }

        /// <summary>Keeps a header panel exactly as tall as its (wrapping) top/bottom docked children.</summary>
        public static void FitHeight(Panel host)
        {
            host.Layout += delegate
            {
                int w = Math.Max(100, host.ClientSize.Width - host.Padding.Horizontal);
                int h = host.Padding.Vertical;
                foreach (Control c in host.Controls)
                {
                    if (!c.Visible || (c.Dock != DockStyle.Top && c.Dock != DockStyle.Bottom)) continue;
                    if (c is FlowLayoutPanel) { ((FlowLayoutPanel)c).AutoSize = false; c.Height = c.GetPreferredSize(new Size(w, 0)).Height; }
                    h += c.Height + c.Margin.Vertical * 0;
                }
                if (host.Height != h) host.Height = h;
            };
            host.Resize += delegate { host.PerformLayout(); };
        }

        /// <summary>White card with rounded corners and a hairline border (Windows 11).</summary>
        public static Panel Card()
        {
            RoundPanel p = new RoundPanel();
            p.Padding = new Padding(12, 10, 12, 10);
            return p;
        }

        public static void Fill(ComboBox c, System.Data.DataTable t, string idCol, string nameCol, string allText)
        {
            List<ComboItem> list = new List<ComboItem>();
            if (allText != null) list.Add(new ComboItem(0, allText));
            foreach (System.Data.DataRow r in t.Rows) list.Add(new ComboItem(Db.I(r[idCol]), Db.S(r[nameCol])));
            c.DataSource = null;
            c.Items.Clear();
            c.DisplayMember = "Name";
            c.ValueMember = "Id";
            c.DataSource = list;
        }

        public static int Sel(ComboBox c)
        {
            ComboItem i = c.SelectedItem as ComboItem;
            return i == null ? 0 : i.Id;
        }

        public static void SetSel(ComboBox c, int id)
        {
            for (int i = 0; i < c.Items.Count; i++)
            {
                ComboItem ci = c.Items[i] as ComboItem;
                if (ci != null && ci.Id == id) { c.SelectedIndex = i; return; }
            }
            if (c.Items.Count > 0 && c.SelectedIndex < 0) c.SelectedIndex = 0;
        }

        public static void WaitCursor(bool on) { Cursor.Current = on ? Cursors.WaitCursor : Cursors.Default; Application.UseWaitCursor = on; }
    }
}
