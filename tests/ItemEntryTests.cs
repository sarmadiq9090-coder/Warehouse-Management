using System;
using System.Collections.Generic;
using System.Data;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Forms;

namespace Stores
{
    /// <summary>
    /// Item entry bar of the documents (search + quantity on the same page), driven like a user on an off-screen
    /// main window: type a code → Enter → quantity → Enter adds a line; the same item again increases the quantity;
    /// a barcode scan (fast keys + Enter) adds at once; a name search with ↓; Esc; recent items; a too large issue
    /// quantity warns (bar + save); saving posts the balances; a closed year disables the bar. Screenshots of an
    /// issue and a receipt with the list open at 1366×740 and 1100×680.
    /// Usage: ItemEntryTests.exe &lt;copy.mdb&gt; &lt;out dir&gt;
    /// </summary>
    static class ItemEntryTestsMain
    {
        delegate bool EnumProc(IntPtr h, IntPtr l);
        [DllImport("user32.dll")] static extern bool EnumThreadWindows(int tid, EnumProc f, IntPtr l);
        [DllImport("user32.dll")] static extern bool EnumChildWindows(IntPtr p, EnumProc f, IntPtr l);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern int GetClassName(IntPtr h, StringBuilder s, int n);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern int GetWindowText(IntPtr h, StringBuilder s, int n);
        [DllImport("user32.dll")] static extern bool IsWindowVisible(IntPtr h);
        [DllImport("user32.dll")] static extern IntPtr GetDlgItem(IntPtr h, int id);
        [DllImport("user32.dll")] static extern bool PostMessage(IntPtr h, int msg, IntPtr w, IntPtr l);
        [DllImport("kernel32.dll")] static extern int GetCurrentThreadId();

        static int fails;
        static string outDir;
        static MainForm main;
        static List<string> dialogs = new List<string>();
        static List<IntPtr> answered = new List<IntPtr>();
        static int shortAnswer = 7;   // IDNO

        static void Check(bool ok, string what) { Console.WriteLine((ok ? "PASS  " : "FAIL  ") + what); if (!ok) fails++; }
        static object Field(object o, string name) { return o.GetType().GetField(name, BindingFlags.NonPublic | BindingFlags.Instance).GetValue(o); }
        static object Call(object o, string name, params object[] a) { return o.GetType().GetMethod(name, BindingFlags.NonPublic | BindingFlags.Instance).Invoke(o, a); }
        static void Pump(int ms) { DateTime end = DateTime.Now.AddMilliseconds(ms); while (DateTime.Now < end) { Application.DoEvents(); System.Threading.Thread.Sleep(10); } }
        static string Cls(IntPtr h) { StringBuilder s = new StringBuilder(256); GetClassName(h, s, 256); return s.ToString(); }
        static string Txt(IntPtr h) { StringBuilder s = new StringBuilder(4096); GetWindowText(h, s, 4096); return s.ToString(); }

        /// <summary>Answers message boxes of this thread: shortage question → shortAnswer, other questions → Yes, notices → OK.</summary>
        static void AnswerDialogs()
        {
            List<IntPtr> wins = new List<IntPtr>();
            EnumThreadWindows(GetCurrentThreadId(), delegate(IntPtr h, IntPtr l) { if (IsWindowVisible(h) && Cls(h) == "#32770") wins.Add(h); return true; }, IntPtr.Zero);
            foreach (IntPtr h in wins)
            {
                if (answered.Contains(h)) continue;
                StringBuilder all = new StringBuilder();
                EnumChildWindows(h, delegate(IntPtr c, IntPtr l) { if (Cls(c) == "Static") all.Append(Txt(c)); return true; }, IntPtr.Zero);
                string text = all.ToString();
                dialogs.Add(text);
                Console.WriteLine("      [message] " + text.Replace("\n", " ").Replace("\r", ""));
                int id = GetDlgItem(h, 6) != IntPtr.Zero ? (text.Contains("أكبر من المتوفر") ? shortAnswer : 6) : 1;
                answered.Add(h);
                PostMessage(h, 0x0111, (IntPtr)id, IntPtr.Zero);   // WM_COMMAND
            }
        }

        static DocView Current() { return (DocView)typeof(MainForm).GetField("current", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(main); }

        static void Shot(string name, int w, int h)
        {
            main.Size = new Size(w, h); Pump(300);
            ItemEntryBar bar = (ItemEntryBar)Field(Current(), "entry");
            if (bar.DropDownOpen) { bar.Search.PressKey(Keys.Down); bar.Search.PressKey(Keys.Up); }   // re-place the list after the resize
            Pump(200);
            string path = Path.Combine(outDir, name + ".png");
            using (Bitmap b = new Bitmap(main.Width, main.Height))
            {
                main.DrawToBitmap(b, new Rectangle(0, 0, main.Width, main.Height));
                // DrawToBitmap paints children in collection order, so the overlay list (on top on screen) is drawn again over the page
                if (bar.DropDownOpen)
                {
                    Point p = bar.DropDown.PointToScreen(Point.Empty);
                    bar.DropDown.DrawToBitmap(b, new Rectangle(p.X - main.Left, p.Y - main.Top, bar.DropDown.Width, bar.DropDown.Height));
                }
                b.Save(path, ImageFormat.Png);
            }
            Console.WriteLine("      screenshot " + path + (bar.DropDownOpen ? " (list open, " + bar.DropDown.Count + " rows)" : " (list closed)"));
        }

        static DataGridView Grid(DocView dv) { return (DataGridView)Field(dv, "grid"); }
        static int Lines(DocView dv) { int n = 0; foreach (DataGridViewRow r in Grid(dv).Rows) if (!r.IsNewRow && Db.I(r.Cells["itemNo"].Value) != 0) n++; return n; }
        static double QtyOf(DocView dv, int item)
        {
            double s = 0;
            foreach (DataGridViewRow r in Grid(dv).Rows) if (!r.IsNewRow && Db.I(r.Cells["itemNo"].Value) == item) s += Db.D(r.Cells["qty"].Value);
            return s;
        }
        static int RowsOf(DocView dv, int item) { int n = 0; foreach (DataGridViewRow r in Grid(dv).Rows) if (!r.IsNewRow && Db.I(r.Cells["itemNo"].Value) == item) n++; return n; }
        static string Status(ItemEntryBar bar) { return ((Label)Field(bar, "lblStatus")).Text; }
        static Color StatusColor(ItemEntryBar bar) { return ((Label)Field(bar, "lblStatus")).ForeColor; }

        /// <summary>Types the code like a person (slow keys), Enter, quantity, Enter.</summary>
        static void AddByCode(ItemEntryBar bar, string code, double qty)
        {
            bar.Search.TypeText(code, 70);
            bar.Search.PressKey(Keys.Enter);
            bar.Qty.Text = UI.Num(qty);
            bar.Qty.PressKey(Keys.Enter);
            if (bar.Price.Visible && bar.Pending != 0) bar.Price.PressKey(Keys.Enter);
            Pump(50);
        }

        static bool Arabic(string s) { foreach (char c in s ?? "") if (c >= 'ء' && c <= 'ي') return true; return false; }

        /// <summary>Candidates with an Arabic name first (more like the owner's daily items).</summary>
        static DataRow[] SortArabicFirst(DataTable t)
        {
            List<DataRow> a = new List<DataRow>(), b = new List<DataRow>();
            foreach (DataRow r in t.Rows) (Arabic(Cache.ItemName(Db.I(r[0]))) ? a : b).Add(r);
            a.AddRange(b);
            return a.ToArray();
        }

        /// <summary>The most common first word of the Arabic item names (a realistic search for the screenshots).</summary>
        static string CommonWord()
        {
            Dictionary<string, int> n = new Dictionary<string, int>();
            foreach (DataRow r in Cache.Items.Rows)
            {
                string[] w = Db.S(r["item_name"]).Split(new char[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
                if (w.Length == 0 || w[0].Length < 3 || !Arabic(w[0])) continue;
                int c; n.TryGetValue(w[0], out c); n[w[0]] = c + 1;
            }
            string best = "ا"; int max = 0;
            foreach (KeyValuePair<string, int> kv in n) if (kv.Value > max) { max = kv.Value; best = kv.Key; }
            return best;
        }

        [STAThread]
        static int Main(string[] args)
        {
            Console.OutputEncoding = Encoding.UTF8;
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            outDir = args[1]; Directory.CreateDirectory(outDir);
            string err = Db.InitAccess(args[0]);
            if (err != null) { Console.WriteLine("open failed: " + err); return 1; }
            Db.EnsureAppTables();
            Session.UserNo = 1; Session.UserName = "1"; Session.LoadPerms(); Session.Advanced = false;
            Cache.LoadAll();
            Console.WriteLine("recalc fixed (historic mismatches): " + Posting.RecalcBalances());
            Cache.LoadAll();

            int st = Cache.DefaultStore;
            // two items with a unique code, stock in the default store and nothing reserved
            List<int> pick = new List<int>();
            DataTable cand = Db.Query("SELECT item_no, item_bal FROM item_stor WHERE stor = ? AND item_bal > 20 AND item_bal < 100000 ORDER BY item_no DESC", st);
            foreach (DataRow r in SortArabicFirst(cand))
            {
                int id = Db.I(r[0]); DataRow ir = Cache.Item(id);
                string code = Cache.ItemCode(id).Trim();
                if (ir == null || Db.B(ir["freez"]) || code.Length < 3 || code.Contains(" ") || Cache.FindItemByCode(code) != id || pick.Contains(id)) continue;
                int tot; List<int> s = ItemIndex.Search(code, false, 5, out tot);
                if (s.Count == 0 || s[0] != id || UI.Norm(Cache.ItemCode(s.Count > 1 ? s[1] : 0)).Trim() == UI.Norm(code)) continue;
                if (Db.HasProj && Reservations.ForOthers(id, st, -1) > 0) continue;
                if (Cache.ItemName(id).Trim().Length < 4) continue;
                pick.Add(id);
                if (pick.Count == 2) break;
            }
            if (pick.Count < 2) { Console.WriteLine("no suitable items in the data"); return 1; }
            int A = pick[0], B = pick[1];
            string codeA = Cache.ItemCode(A).Trim(), codeB = Cache.ItemCode(B).Trim();
            double stockA = Cache.Stock(A, st), stockB = Cache.Stock(B, st);
            Console.WriteLine("store " + st + " • A = " + codeA + " " + Cache.ItemName(A) + " (" + stockA + ") • B = " + codeB + " " + Cache.ItemName(B) + " (" + stockB + ")");
            int person = Db.I(Db.Scalar("SELECT TOP 1 custom_no FROM sale WHERE custom_no > 0 ORDER BY shop_no DESC"));
            int proj = Db.HasProj ? Db.I(Db.Scalar("SELECT TOP 1 proj_no FROM project ORDER BY proj_no DESC")) : 0;
            // «المستلم = مركز الكلفة»: an issue needs a cost centre
            if (proj == 0 && Db.HasProj) proj = CostCentres.Save(0, "مركز اختبار الإدخال " + DateTime.Now.Ticks, "", CostCentres.Other, 1, "", null, "");

            System.Windows.Forms.Timer wd = new System.Windows.Forms.Timer(); wd.Interval = 120; wd.Tick += delegate { AnswerDialogs(); }; wd.Start();
            main = new MainForm();
            main.StartPosition = FormStartPosition.Manual; main.WindowState = FormWindowState.Normal;
            main.Location = new Point(-4000, -4000); main.Size = new Size(1366, 740); main.ShowInTaskbar = false;
            main.Show(); Pump(800);

            // ================================================================ issue, beginner mode
            Console.WriteLine("=== issue (beginner mode)");
            main.OpenDoc(DocKind.Issue, 0, proj); Pump(400);
            DocView dv = Current();
            ItemEntryBar bar = (ItemEntryBar)Field(dv, "entry");
            ((PersonPicker)Field(dv, "person")).Value = person;
            if (proj > 0) ((ProjectPicker)Field(dv, "project")).Value = proj;
            Check(bar.Enabled && bar.Visible, "entry bar shown and enabled on a new issue");
            Check(bar.Search.Cue.Contains("امسح الباركود"), "search box placeholder: «" + bar.Search.Cue + "»");

            // type a code: results open on the same page, exact code first
            bar.Search.TypeText(codeA, 70);
            Check(bar.DropDownOpen && bar.DropDown.Count > 0 && bar.DropDown.Ids[0] == A, "typing the code opens the inline list, exact code first (" + bar.DropDown.Count + " results)");
            Check(bar.DropDown.Parent == dv && !(bar.DropDown.FindForm() is ItemSearchDialog), "the list is a child of the document page (not a dialog)");
            Check(Application.OpenForms.Count == 1, "no popup window opened (open forms: " + Application.OpenForms.Count + ")");
            bar.Search.PressKey(Keys.Enter);
            Check(bar.Pending == A && bar.Qty.Text == "1" && !bar.DropDownOpen, "Enter picks the item → quantity box = 1, list closed");
            bar.Qty.Text = "3"; bar.Qty.PressKey(Keys.Enter); Pump(50);
            Check(Lines(dv) == 1 && QtyOf(dv, A) == 3, "Enter in the quantity adds the line (qty 3)");
            Check(bar.Search.Text == "" && bar.Pending == 0 && bar.Qty.Text == "1", "search box empty again, quantity back to 1");
            Check(Status(bar).Contains("أُضيفت"), "bar status: " + Status(bar));
            Check(Convert.ToString(Grid(dv).Rows[0].Cells["unit"].FormattedValue) == Cache.ItemUnit(A), "unit column shows the item's unit («" + Cache.ItemUnit(A) + "»)");

            // the same item again → quantity increased, no duplicate line, row flashed
            AddByCode(bar, codeA, 2);
            Check(Lines(dv) == 1 && RowsOf(dv, A) == 1 && QtyOf(dv, A) == 5, "same item again: quantity 3 + 2 = 5 on the same line");
            Check((int)Field(dv, "flashItem") == A, "the line is highlighted (flash)");
            Check(Status(bar).Contains("زيدت"), "bar status: " + Status(bar));

            // barcode scanner: fast keys + Enter, exact code → chosen only (owner: nothing is added before the quantity)
            bar.Search.TypeText(codeB, 0);
            bar.Search.PressKey(Keys.Enter); Pump(50);
            Check(Lines(dv) == 1 && bar.Pending == B && bar.Qty.Text == "1", "barcode scan chooses B, adds nothing yet, quantity 1 ready");
            bar.Qty.PressKey(Keys.Enter); Pump(50);
            Check(Lines(dv) == 2 && QtyOf(dv, B) == 1 && bar.Pending == 0 && bar.Search.Text == "", "Enter in the quantity adds B, search box ready for the next scan");
            bar.Search.TypeText(codeB, 0); bar.Search.PressKey(Keys.Enter); Pump(50); bar.Qty.PressKey(Keys.Enter); Pump(50);
            Check(Lines(dv) == 2 && QtyOf(dv, B) == 2, "scanning B again + Enter: quantity 2 on the same line");
            bar.Search.TypeText("ZZQ" + DateTime.Now.Ticks, 0); bar.Search.PressKey(Keys.Enter);
            Check(Lines(dv) == 2 && Status(bar).Contains("لا توجد مادة"), "unknown barcode adds nothing: " + Status(bar));
            bar.Search.PressKey(Keys.Escape);
            Check(bar.Search.Text == "" && !bar.DropDownOpen, "Esc clears the search box and closes the list");

            // name search with the arrows
            string[] words = Cache.ItemName(B).Split(new char[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            string part = words.Length > 1 ? words[0] + " " + words[1] : words[0];
            bar.Search.TypeText(part, 60);
            int idx = bar.DropDown.Ids.IndexOf(B);
            Check(idx >= 0, "name search «" + part + "» finds B (position " + idx + " of " + bar.DropDown.Count + ")");
            for (int i = 0; i < idx; i++) bar.Search.PressKey(Keys.Down);
            Check(bar.DropDown.SelectedId == B, "↓ moves the selection to B");
            bar.Search.PressKey(Keys.Enter);
            bar.Qty.Text = "1"; bar.Qty.PressKey(Keys.Enter); Pump(50);
            Check(Lines(dv) == 2 && QtyOf(dv, B) == 3, "picked by name → B quantity 3 (same line)");
            // the dropdown values come from the in-memory snapshot (one load, none per key)
            StockSnapshot snap = (StockSnapshot)Field(dv, "snap");
            Check(snap != null && snap.Store == st && Math.Abs(snap.Stock(A) - stockA) < 1e-6, "available quantities from the store snapshot (loaded once in " + (snap == null ? -1 : snap.LoadMs) + " ms)");
            System.Diagnostics.Stopwatch sw = System.Diagnostics.Stopwatch.StartNew();
            for (int i = 0; i < 20; i++) { bar.Search.Text = i % 2 == 0 ? part : codeA; Application.DoEvents(); }
            sw.Stop();
            Check(sw.ElapsedMilliseconds / 20.0 < 40, "search + list redraw per keystroke: " + (sw.ElapsedMilliseconds / 20.0).ToString("0.0") + " ms");
            bar.Search.PressKey(Keys.Escape); bar.Search.PressKey(Keys.Escape);

            // recent items when the search box is empty
            bar.FocusSearch(null); Pump(50);
            List<int> rec = (List<int>)Call(dv, "RecentItems");
            Check(rec.Count > 0 && rec[0] == B && bar.DropDownOpen && bar.DropDown.Ids.Contains(A), "empty search box shows the last used items (" + rec.Count + ", items added here first)");
            int linesBefore = Lines(dv); double qtyA = QtyOf(dv, A);
            bar.Pick(A); Pump(50);
            Check(Lines(dv) == linesBefore && QtyOf(dv, A) == qtyA && bar.Pending == A, "choosing a recent item adds nothing until the quantity is confirmed");
            bar.Search.PressKey(Keys.Escape); bar.Search.PressKey(Keys.Escape);

            // too large quantity for an issue: red warning in the bar, red in the list
            double availA = bar.DropDown.Avail(A);
            bar.Search.TypeText(codeA, 70);
            bar.Search.PressKey(Keys.Enter);
            bar.Qty.Text = UI.Num(availA + 50 - 5); bar.Qty.PressKey(Keys.Enter); Pump(50);
            Check(QtyOf(dv, A) == availA + 50, "quantity of A now " + QtyOf(dv, A) + " (available " + availA + ")");
            Check(Status(bar).Contains("أكبر من المتوفر") && StatusColor(bar) == Theme.Danger, "insufficient stock warns in red: " + Status(bar));
            bar.Search.TypeText(codeA, 70);
            Check(bar.DropDown.WarnShort && bar.DropDown.Avail(A) - bar.DropDown.InDoc(A) <= 0, "the list marks A red (nothing left after this document)");
            string word = CommonWord();
            bar.Search.Text = ""; bar.Search.TypeText(word, 0); Pump(50);
            Shot("issue_dropdown_1366x740", 1366, 740);
            bar.Search.Text = ""; bar.Search.TypeText(codeA.Substring(0, Math.Min(4, codeA.Length)), 0); Pump(50);
            Shot("issue_dropdown_1100x680", 1100, 680);
            bar.Search.PressKey(Keys.Escape); bar.Search.PressKey(Keys.Escape);
            main.Size = new Size(1366, 740); Pump(200);

            // saving with the shortage asks (answer No → not saved), then a valid quantity is saved
            dialogs.Clear(); shortAnswer = 7;
            bool saved = (bool)Call(dv, "Save"); Pump(200);
            Check(!saved && dialogs.Exists(t => t.Contains("أكبر من المتوفر")), "save warns about the shortage (answered No → not saved)");
            DataGridView g = Grid(dv);
            foreach (DataGridViewRow r in g.Rows) if (!r.IsNewRow && Db.I(r.Cells["itemNo"].Value) == A) r.Cells["qty"].Value = 5.0;
            dialogs.Clear();
            saved = (bool)Call(dv, "Save"); Pump(300);
            Doc d = (Doc)Field(dv, "doc");
            Check(saved && d.Id > 0 && dialogs.Exists(t => t.Contains("تأكيد قبل الحفظ")), "issue saved (" + d.No + ") after the beginner confirmation");
            Check(Math.Abs(Cache.Stock(A, st) - (stockA - 5)) < 1e-6 && Math.Abs(Cache.Stock(B, st) - (stockB - 3)) < 1e-6,
                  "balances posted: A " + stockA + " → " + Cache.Stock(A, st) + ", B " + stockB + " → " + Cache.Stock(B, st));
            Doc ld = Posting.Load(DocKind.Issue, d.Id);
            int recipient = Posting.CcIsRecipient(DocKind.Issue) ? Db.I(Db.Scalar("SELECT p_no FROM project WHERE proj_no = ?", proj)) : person;   // «المستلم = مركز الكلفة»
            Check(ld.Lines.Count == 2 && ld.Project == proj && recipient > 0 && ld.Person == recipient, "saved document has 2 lines, the cost centre and its recipient person");
            Check(Posting.RecalcBalances() == 0, "balances consistent after the issue (RecalcBalances = 0)");
            Check(bar.Enabled && bar.Search.Text == "", "entry bar still enabled and empty after save");

            // closed year → read-only document, entry bar disabled
            const string key = "closed_through";
            Db.SetSetting(key, DateTime.Today.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture));
            main.OpenDoc(DocKind.Issue, d.Id); Pump(300);
            DocView ro = Current(); ItemEntryBar rbar = (ItemEntryBar)Field(ro, "entry");
            Check(Grid(ro).ReadOnly && !rbar.Enabled, "closed year: document read-only and the entry bar disabled");
            rbar.Search.TypeText(codeA, 0); rbar.Search.PressKey(Keys.Enter); rbar.Pick(A); rbar.Qty.PressKey(Keys.Enter);
            Check(Lines(ro) == 2 && QtyOf(ro, A) == 5, "nothing can be added to the read-only document");
            Db.Exec("DELETE FROM app_settings WHERE k = ?", key);

            // ================================================================ receipt, advanced mode (price box)
            Console.WriteLine("=== receipt (advanced mode)");
            Session.Advanced = true;
            main.OpenDoc(DocKind.Receipt, 0); Pump(400);
            dv = Current(); bar = (ItemEntryBar)Field(dv, "entry");
            ((PersonPicker)Field(dv, "person")).Value = person;
            Check(bar.Price.Visible, "advanced receipt: price box in the entry bar");
            bar.Search.TypeText(codeA, 70); bar.Search.PressKey(Keys.Enter);
            bar.Qty.Text = "4"; bar.Qty.PressKey(Keys.Enter);
            Check(bar.Pending == A, "Enter in the quantity moves to the price");
            bar.Price.Text = "2500"; bar.Price.PressKey(Keys.Enter); Pump(50);
            g = Grid(dv);
            Check(Lines(dv) == 1 && QtyOf(dv, A) == 4 && Db.D(g.Rows[0].Cells["price"].Value) == 2500, "receipt line added with quantity 4 and price 2500");
            bar.Search.TypeText(codeB, 0); bar.Search.PressKey(Keys.Enter); Pump(50);
            Check(Lines(dv) == 1 && bar.Pending == B, "barcode on a receipt chooses B (nothing added yet)");
            bar.Qty.PressKey(Keys.Enter); bar.Price.PressKey(Keys.Enter); Pump(50);
            Check(Lines(dv) == 2 && QtyOf(dv, B) == 1, "quantity + price confirmed → B added with quantity 1");
            bar.Search.TypeText(word, 0); Pump(50);
            Shot("receipt_dropdown_1366x740", 1366, 740);
            Shot("receipt_dropdown_1100x680", 1100, 680);
            bar.Search.PressKey(Keys.Escape); bar.FocusSearch(null); Pump(50);
            Shot("receipt_recent_1100x680", 1100, 680);
            bar.Search.PressKey(Keys.Escape); bar.Search.PressKey(Keys.Escape);
            main.Size = new Size(1366, 740); Pump(200);
            dialogs.Clear();
            saved = (bool)Call(dv, "Save"); Pump(300);
            d = (Doc)Field(dv, "doc");
            Check(saved && d.Id > 0, "receipt saved (" + d.No + ")" + (dialogs.Count > 0 ? " — messages: " + string.Join(" | ", dialogs.ToArray()) : ""));
            Check(Math.Abs(Cache.Stock(A, st) - (stockA - 5 + 4)) < 1e-6 && Math.Abs(Cache.Stock(B, st) - (stockB - 3 + 1)) < 1e-6,
                  "balances posted: A → " + Cache.Stock(A, st) + ", B → " + Cache.Stock(B, st));
            Check(Posting.RecalcBalances() == 0, "balances consistent after the receipt (RecalcBalances = 0)");

            // ================================================================ every other document kind has the bar
            foreach (DocKind k in new DocKind[] { DocKind.Return, DocKind.PurchaseReturn, DocKind.Transfer, DocKind.Reserve })
            {
                if (k == DocKind.Reserve && !Db.HasProj) continue;
                main.OpenDoc(k, 0); Pump(300);
                DocView kv = Current(); ItemEntryBar kb = (ItemEntryBar)Field(kv, "entry");
                kb.Search.TypeText(codeA, 0); kb.Search.PressKey(Keys.Enter); Pump(50); kb.Qty.PressKey(Keys.Enter); if (kb.Price.Visible) kb.Price.PressKey(Keys.Enter); Pump(50);
                Check(kb.Enabled && Lines(kv) == 1 && QtyOf(kv, A) == 1, Posting.KindName(k) + ": entry bar adds a line");
                Call(kv, "SetClean", "", Theme.Muted);
            }

            wd.Stop();
            main.Close();
            Console.WriteLine(fails == 0 ? "\nALL TESTS PASSED" : "\nFAILURES: " + fails);
            Db.Close();
            return fails;
        }
    }
}
