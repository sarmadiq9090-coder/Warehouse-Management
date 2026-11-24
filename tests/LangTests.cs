using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Text;
using System.Windows.Forms;

namespace Stores
{
    // English UI layer: dictionary coverage of every Arabic literal in src, Lang.T patterns, Lang.Apply on a form,
    // translated reports. No data file needed.
    // Usage: LangTests.exe [<repo dir>]   (default: two folders above the exe)
    static class LangTestsMain
    {
        static int fails = 0;
        static void Check(bool ok, string what) { Console.WriteLine((ok ? "PASS  " : "FAIL  ") + what); if (!ok) fails++; }

        [STAThread]
        static int Main(string[] args)
        {
            Console.OutputEncoding = Encoding.UTF8;
            string repo = args.Length > 0 ? args[0] : Path.GetFullPath(Path.Combine(Path.GetDirectoryName(Application.ExecutablePath), "..", ".."));
            string src = Path.Combine(repo, "src"), file = Path.Combine(src, "App", "lang-en.txt");

            // ---- coverage: every Arabic literal of src has an English text
            SortedDictionary<string, string> lits = StringExtractor.Extract(src);
            Lang.Load(null);   // the embedded copy, as the program uses it
            Lang.English = true;
            Dictionary<string, string> fileDict = StringExtractor.ReadDict(file);
            int have = 0; List<string> missing = new List<string>();
            foreach (KeyValuePair<string, string> kv in lits)
            {
                string en;
                if (fileDict.TryGetValue(kv.Key, out en) && en.Trim().Length > 0) have++;
                else missing.Add(kv.Value + ": " + kv.Key);
            }
            double pct = lits.Count == 0 ? 0 : 100.0 * have / lits.Count;
            Console.WriteLine("Arabic literals in src: " + lits.Count + ", translated: " + have + " (" + pct.ToString("0.0") + "%), dictionary: " + Lang.Count);
            foreach (string m in missing) Console.WriteLine("   missing  " + m.Replace("\n", "\\n"));
            Check(missing.Count == 0, "every static Arabic literal has a translation (run tools\\ExtractStrings.exe to list new ones)");

            // ---- the file: no empty or duplicate translations, the embedded copy is the file
            int empty = 0, dup = 0, arabicLeft = 0; HashSet<string> keys = new HashSet<string>();
            foreach (string line in File.ReadAllLines(file, Encoding.UTF8))
            {
                if (line.Length == 0 || line[0] == '#') continue;
                int t = line.IndexOf('\t');
                if (t <= 0 || line.Substring(t + 1).Trim().Length == 0) { empty++; Console.WriteLine("   empty  " + line); continue; }
                if (!keys.Add(line.Substring(0, t))) { dup++; Console.WriteLine("   duplicate  " + line); }
                if (LangKey.HasArabic(line.Substring(t + 1))) arabicLeft++;
            }
            Check(empty == 0, "no empty translations");
            Check(dup == 0, "no duplicate keys");
            Check(keys.Count == Lang.Count, "the embedded dictionary is the current lang-en.txt (" + keys.Count + " lines)");
            Console.WriteLine("   (" + arabicLeft + " translations keep an Arabic name on purpose, e.g. a folder name)");

            // ---- Lang.T
            Lang.English = false;
            Check(Lang.T("حفظ") == "حفظ", "Arabic: texts unchanged");
            Lang.English = true;
            Check(Lang.T("حفظ") == "Save", "exact: حفظ → Save");
            Check(Lang.T("  حفظ  ") == "  Save  ", "trimmed: edges kept");
            Check(Lang.T("رقم 123") == "No. 123", "prefix with a number: رقم 123 → " + Lang.T("رقم 123"));
            Check(Lang.T("  ● المتصلون الآن: 3  (اضغط للتفاصيل)").StartsWith("  ● Online now: 3  ("), "composed status text → " + Lang.T("  ● المتصلون الآن: 3  (اضغط للتفاصيل)"));
            Check(Lang.T("حدث خطأ:\nfile not found") == "An error occurred:\nfile not found", "message + exception text");
            Check(Lang.T("2026/11/23  12,345.5") == "2026/11/23  12,345.5", "dates and numbers untouched");
            Check(Lang.T("خالد") == "خالد", "unknown text (a name) passes through");
            Check(Lang.TCell("حديد رقم 5") == "حديد رقم 5", "data cells: only whole texts (no part of a name translated)");
            Check(Lang.T("أنبوب م") == "أنبوب م", "single letters are never replaced inside a text");
            string s1 = Lang.T("سند جديد • الرقم يُثبّت عند الحفظ");
            Check(!LangKey.HasArabic(s1) && Lang.T(s1) == s1, "translation is stable (T(T(x)) = T(x)): " + s1);
            Check(Lang.Flags(TextFormatFlags.Right | TextFormatFlags.RightToLeft) == TextFormatFlags.Left, "Flags: right-to-left → left");
            Check(Lang.Mirror(new Rectangle(90, 0, 10, 5), 100).X == 0, "Mirror");

            // ---- Apply on a sample right-to-left form
            Form f = new Form(); f.RightToLeft = RightToLeft.Yes; f.ClientSize = new Size(400, 300); f.Text = "التقارير";
            Panel side = new Panel(); side.Dock = DockStyle.Right; side.Width = 100; side.Padding = new Padding(2, 0, 9, 0);
            Label lbl = new Label(); lbl.Text = "حفظ"; lbl.AutoSize = false; lbl.Bounds = new Rectangle(300, 10, 80, 20); lbl.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            Button btn = UI.Btn("حفظ", Theme.Primary, 40);
            FlowLayoutPanel bar = UI.Flow(); bar.Controls.Add(btn);
            DataGridView g = new DataGridView(); g.Dock = DockStyle.Bottom; UI.StyleGrid(g); g.Columns.Add(UI.Col("c", "الرمز", "c", 80, false));
            ComboBox cb = UI.Combo(120); cb.Items.Add("حفظ"); cb.SelectedIndex = 0;
            f.Controls.Add(lbl); f.Controls.Add(side); f.Controls.Add(bar); f.Controls.Add(g); bar.Controls.Add(cb);
            Lang.Apply(f);
            Check(f.RightToLeft == RightToLeft.No && g.RightToLeft == RightToLeft.No, "Apply: right-to-left turned off (form and grid)");
            Check(side.Dock == DockStyle.Left && side.Padding.Left == 9, "Apply: Dock Right → Left, padding mirrored");
            Check(lbl.Text == "Save" && lbl.Left == 20 && (lbl.Anchor & AnchorStyles.Left) != 0, "Apply: label translated, position and anchor mirrored (x=" + lbl.Left + ")");
            Check(f.Text == "Reports", "Apply: window title → " + f.Text);
            Check(g.Columns[0].HeaderText == "Code", "Apply: grid header → " + g.Columns[0].HeaderText);
            Check(btn.Text == "حفظ" && btn.Width > 40, "Apply: Fluent button keeps its Arabic text (painted in English) and grows to fit (" + btn.Width + ")");
            Check(cb.GetItemText(cb.Items[0]) == "Save" && (string)cb.SelectedItem == "حفظ", "Apply: combo shows English, its item stays Arabic");
            cb.Items.Add("10 • رقم 7");
            Check(cb.GetItemText(cb.Items[1]) == "10 • No. 7", "Apply: a composed option text in a combo → " + cb.GetItemText(cb.Items[1]));
            Label later = new Label(); later.Text = "الرمز"; bar.Controls.Add(later);
            Check(later.Text == "Code", "Apply: a control added later is translated");
            lbl.Text = "رقم 7";
            Check(lbl.Text == "No. 7", "Apply: a text changed later is translated");
            g.Columns.Add(UI.Col("d", "اسم المادة", "d", 80, false));
            Check(g.Columns[1].HeaderText == "Item name", "Apply: a column added later is translated");
            f.Dispose();
            Form max = new Form(); max.RightToLeft = RightToLeft.Yes; max.WindowState = FormWindowState.Maximized;
            Lang.Apply(max);
            Check(!max.IsHandleCreated && max.WindowState == FormWindowState.Maximized && max.RightToLeft == RightToLeft.No,
                "Apply in a constructor: a maximized form is not created early (it would ignore its size and the theme's title bar)");
            max.Dispose();

            // ---- reports
            ReportData r = new ReportData(); r.Title = "كارت المادة"; r.Col("الرمز", ColKind.Text); r.ColT("الكمية"); r.Row(null, "خالد", 5.0); r.Row(null, "حفظ", 2.0);
            string html = Html.Render(r);
            Check(html.Contains("dir='ltr'") && html.Contains("<th") && html.Contains(">Code") && !html.Contains(">الرمز"), "Html: left-to-right, translated headers");
            Check(html.Contains(">خالد<") && html.Contains(">Save<"), "Html: data cells only translated when the whole text is known");
            Check(r.Cols[0].Header == "الرمز", "Html: the report itself is not changed");
            Lang.English = false;
            Check(Html.Render(r).Contains("dir='rtl'"), "Html: Arabic stays right-to-left");

            Console.WriteLine(fails == 0 ? "ALL PASSED" : fails + " FAILED");
            return fails == 0 ? 0 : 1;
        }
    }
}
