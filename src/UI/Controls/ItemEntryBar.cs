using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Windows.Forms;
using System;

namespace Stores
{
    /// <summary>
    /// Item entry bar at the top of a document's item area (point-of-sale style, everything on the same page):
    ///   type a code / part of the name / scan → results open instantly under the box (in-memory ItemIndex, no query per key)
    ///   ↓ ↑ move • Enter picks → the quantity box (1, selected) • Enter adds the line → back to an empty search box • Esc clears.
    /// A barcode scanner (characters ≤ 35 ms apart, then Enter) with an exact code chooses the item directly (quantity 1
    /// selected): Enter adds it. Nothing is ever added before the user confirms the quantity. Empty box + ↓ (or a click into it): the last used items.
    /// The document does the adding (AddLine): an item already in the document gets its quantity increased.
    /// </summary>
    class ItemEntryBar : Panel
    {
        const int ScanMsPerKey = 35;

        readonly Control host;
        readonly ItemDropDown drop = new ItemDropDown();
        public readonly EntryTextBox Search = new EntryTextBox();
        public readonly EntryTextBox Qty = new EntryTextBox();
        public readonly EntryTextBox Price = new EntryTextBox();
        readonly Panel searchFrame, qtyFrame, priceFrame;
        readonly Label lblUnit, lblStatus, lblPrice;
        readonly Button btnAdd;

        int pending;                 // item picked, waiting for its quantity
        bool quiet, showingRecent, noRecentOnce;
        long firstKey, lastKey; int keyCount;
        readonly Stopwatch clock = Stopwatch.StartNew();
        string hint;

        public Func<int, double, double, bool> AddLine;   // item, quantity, price → true when added
        public Func<List<int>> Recent;                     // last used items of this document kind
        public string RecentTitle = "آخر المواد المستعملة";

        public ItemEntryBar(Control overlayHost)
        {
            host = overlayHost;
            Dock = DockStyle.Top;
            Height = 82;
            BackColor = Theme.Card;
            Padding = new Padding(10, 8, 10, 2);

            Panel row = new Panel(); row.Dock = DockStyle.Top; row.Height = 42;

            Search.BorderStyle = BorderStyle.None; Search.Font = new Font("Segoe UI", 12.5f); Search.Dock = DockStyle.Fill;
            Search.Cue = "ابحث برمز المادة أو اسمها أو امسح الباركود…";
            searchFrame = Frame(Search, 0); searchFrame.Dock = DockStyle.Fill;

            Label icon = new Label(); icon.Text = ""; icon.Font = new Font("Segoe MDL2 Assets", 14f); icon.ForeColor = Theme.Primary;
            icon.Dock = DockStyle.Right; icon.Width = 36; icon.TextAlign = ContentAlignment.MiddleCenter;

            Qty.BorderStyle = BorderStyle.None; Qty.Font = new Font("Segoe UI", 12.5f, FontStyle.Bold); Qty.Dock = DockStyle.Fill;
            Qty.RightToLeft = RightToLeft.No; Qty.TextAlign = HorizontalAlignment.Center; Qty.Text = "1";
            qtyFrame = Frame(Qty, 96); qtyFrame.Dock = DockStyle.Left;
            Label lq = Caption("الكمية", 62);

            lblUnit = new Label(); lblUnit.Dock = DockStyle.Left; lblUnit.Width = 74; lblUnit.ForeColor = Theme.Muted; lblUnit.Font = Theme.Font;
            lblUnit.TextAlign = ContentAlignment.MiddleCenter;

            Price.BorderStyle = BorderStyle.None; Price.Font = new Font("Segoe UI", 12.5f); Price.Dock = DockStyle.Fill;
            Price.RightToLeft = RightToLeft.No; Price.TextAlign = HorizontalAlignment.Center;
            priceFrame = Frame(Price, 100); priceFrame.Dock = DockStyle.Left;
            lblPrice = Caption("السعر", 52);

            btnAdd = UI.Btn("+ إضافة", Theme.Accent, 104); btnAdd.Dock = DockStyle.Left; btnAdd.Height = 42; btnAdd.TabStop = false;
            Panel gap = new Panel(); gap.Dock = DockStyle.Left; gap.Width = 8;

            // docking: last added is placed first → right: icon, search (fill); left: add, gap, price, unit, qty, caption
            row.Controls.Add(searchFrame);
            row.Controls.Add(lq); row.Controls.Add(qtyFrame); row.Controls.Add(lblUnit);
            row.Controls.Add(lblPrice); row.Controls.Add(priceFrame);
            row.Controls.Add(gap); row.Controls.Add(btnAdd);
            row.Controls.Add(icon);
            ShowPrice = false;

            lblStatus = new Label(); lblStatus.Dock = DockStyle.Fill; lblStatus.Font = Theme.FontSmall; lblStatus.ForeColor = Theme.Muted;
            lblStatus.TextAlign = ContentAlignment.MiddleLeft; lblStatus.Padding = new Padding(36, 0, 0, 0);
            hint = "↓ ↑ للتنقل بين النتائج   •   Enter للاختيار ثم اكتب الكمية و Enter للإضافة   •   Esc للمسح   •   F2 للعودة إلى البحث";
            lblStatus.Text = hint;

            Controls.Add(lblStatus); Controls.Add(row);

            host.Controls.Add(drop);
            drop.Picked += delegate(int id)
            {
                Pick(id);   // choosing never adds: the quantity box gets the focus, Enter / «+ إضافة» adds (owner's request)
            };

            Search.TextChanged += delegate
            {
                if (Search.TextLength == 0) keyCount = 0;
                if (quiet) return;
                pending = 0; lblUnit.Text = "";
                RunSearch();
            };
            Search.KeyPress += delegate(object s, KeyPressEventArgs e)
            {
                if (char.IsControl(e.KeyChar)) return;
                long now = clock.ElapsedMilliseconds;
                if (Search.SelectionLength == Search.TextLength) keyCount = 0;   // typing replaces everything
                if (keyCount == 0) firstKey = now;
                lastKey = now; keyCount++;
            };
            Search.KeyDown += Search_KeyDown;
            Search.Enter += delegate
            {
                searchFrame.BackColor = Theme.Primary;
                if (noRecentOnce) { noRecentOnce = false; return; }
                if (Search.TextLength == 0) ShowRecent();
            };
            Search.MouseDown += delegate { if (Search.TextLength == 0 && !drop.Visible) ShowRecent(); };
            Search.Leave += delegate { searchFrame.BackColor = Theme.Border; HideLater(); };
            Qty.Enter += delegate { qtyFrame.BackColor = Theme.Primary; Qty.SelectAll(); };
            Qty.Leave += delegate { qtyFrame.BackColor = Theme.Border; };
            Qty.KeyDown += delegate(object s, KeyEventArgs e)
            {
                if (e.KeyCode == Keys.Enter) { e.SuppressKeyPress = e.Handled = true; if (ShowPrice) { Price.Focus(); Price.SelectAll(); } else Commit(); }
                else if (e.KeyCode == Keys.Escape) { e.SuppressKeyPress = e.Handled = true; BackToSearch(); }
            };
            Price.Enter += delegate { priceFrame.BackColor = Theme.Primary; Price.SelectAll(); };
            Price.Leave += delegate { priceFrame.BackColor = Theme.Border; };
            Price.KeyDown += delegate(object s, KeyEventArgs e)
            {
                if (e.KeyCode == Keys.Enter) { e.SuppressKeyPress = e.Handled = true; Commit(); }
                else if (e.KeyCode == Keys.Escape) { e.SuppressKeyPress = e.Handled = true; BackToSearch(); }
            };
            btnAdd.Click += delegate
            {
                if (pending == 0 && drop.Visible && drop.SelectedId != 0) pending = drop.SelectedId;
                Commit();
            };
            host.Resize += delegate { if (drop.Visible) Place(drop.Count); };
            EnabledChanged += delegate { if (!Enabled) { drop.Visible = false; pending = 0; } };
        }

        static Panel Frame(Control inner, int width)
        {
            Panel f = new Panel(); f.Width = width; f.BackColor = Theme.Border; f.Padding = new Padding(1);
            Panel w = new Panel(); w.Dock = DockStyle.Fill; w.BackColor = Theme.Card; w.Padding = new Padding(8, 9, 8, 4);
            w.Controls.Add(inner); f.Controls.Add(w);
            w.Click += delegate { inner.Focus(); };
            return f;
        }

        static Label Caption(string text, int width)
        {
            Label l = new Label(); l.Text = text; l.Dock = DockStyle.Left; l.Width = width; l.Font = Theme.FontBold; l.ForeColor = Theme.Text;
            l.TextAlign = ContentAlignment.MiddleCenter;
            return l;
        }

        // ------------------------------------------------------------ public
        public bool ShowPrice
        {
            get { return priceFrame.Visible; }
            set { priceFrame.Visible = lblPrice.Visible = value; }
        }

        public bool DropDownOpen { get { return drop.Visible; } }
        public ItemDropDown DropDown { get { return drop; } }
        public int Pending { get { return pending; } }

        public Func<int, double> Avail { set { drop.Avail = value; } }
        public Func<int, double> InDoc { set { drop.InDoc = value; } }
        public bool WarnShort { set { drop.WarnShort = value; } }
        public string AvailTitle { set { drop.AvailTitle = value; } }

        /// <summary>Focus the search box; optional text starts a search (F2 from a grid cell).</summary>
        public void FocusSearch(string text)
        {
            if (!Enabled) return;
            pending = 0; lblUnit.Text = "";
            bool typed = !string.IsNullOrEmpty(text);
            if (typed) { quiet = true; Search.Text = text; quiet = false; }
            noRecentOnce = true;
            Search.Focus();
            noRecentOnce = false;
            if (typed) Search.SelectionStart = Search.TextLength;    // keep typing after it
            else Search.SelectAll();
            if (Search.TextLength > 0) RunSearch();
            else ShowRecent();
        }

        public void Status(string text, Color c) { lblStatus.Text = string.IsNullOrEmpty(text) ? hint : text; lblStatus.ForeColor = string.IsNullOrEmpty(text) ? Theme.Muted : c; }

        /// <summary>New / other document: empty search, quantity 1, list closed.</summary>
        public void Reset()
        {
            quiet = true; Search.Text = ""; quiet = false;
            Qty.Text = "1"; Price.Text = ""; pending = 0; lblUnit.Text = ""; keyCount = 0;
            drop.Visible = false;
            Status(null, Theme.Muted);
        }

        // ------------------------------------------------------------ search
        void RunSearch()
        {
            string t = Search.Text.Trim();
            if (t.Length == 0) { if (Search.Focused) ShowRecent(); else drop.Visible = false; return; }
            Stopwatch sw = Stopwatch.StartNew();
            int total;
            List<int> res = ItemIndex.Search(t, false, 60, out total);
            sw.Stop();
            showingRecent = false;
            string foot = res.Count == 0 ? "لا توجد مادة تطابق «" + t + "»   •   Esc للمسح"
                : "النتائج: " + total.ToString("#,##0") + (total > res.Count ? "  (يُعرض أفضل " + res.Count + ")" : "") +
                  "   •   " + sw.ElapsedMilliseconds + " ms   •   ↓ ↑ ثم Enter، أو انقر على المادة";
            drop.SetItems(res, foot);
            Place(res.Count);
        }

        void ShowRecent()
        {
            if (!Enabled || Recent == null) return;
            List<int> r = Recent();
            if (r == null || r.Count == 0) { drop.Visible = false; return; }
            showingRecent = true;
            drop.SetItems(r, RecentTitle + "   •   انقر على المادة أو ↓ ↑ ثم Enter، ثم اكتب الكمية");
            Place(r.Count);
        }

        void Place(int rows)
        {
            Point p = host.PointToClient(searchFrame.Parent.PointToScreen(new Point(0, searchFrame.Parent.Height + 2)));
            Point left = host.PointToClient(PointToScreen(Point.Empty));
            drop.SetBounds(left.X, p.Y, Math.Max(300, Width), drop.HeightFor(rows, host.ClientSize.Height - p.Y - 4));
            drop.Visible = true;
            drop.BringToFront();
            drop.Invalidate(true);
        }

        void HideLater()
        {
            if (!IsHandleCreated) { drop.Visible = false; return; }
            BeginInvoke((Action)delegate { if (!Search.Focused && !drop.ContainsFocus) drop.Visible = false; });
        }

        bool LooksScanned()
        {
            if (keyCount < 3 || keyCount != Search.TextLength) return false;
            return (lastKey - firstKey) <= (long)ScanMsPerKey * (keyCount - 1);
        }

        void Search_KeyDown(object sender, KeyEventArgs e)
        {
            switch (e.KeyCode)
            {
                case Keys.Down: case Keys.Up: case Keys.PageDown: case Keys.PageUp:
                    e.Handled = e.SuppressKeyPress = true;
                    if (!drop.Visible) { if (Search.TextLength == 0) ShowRecent(); else RunSearch(); return; }
                    int step = e.KeyCode == Keys.PageDown || e.KeyCode == Keys.PageUp ? 10 : 1;
                    drop.MoveSelection(e.KeyCode == Keys.Down || e.KeyCode == Keys.PageDown ? step : -step);
                    break;
                case Keys.Escape:
                    e.Handled = e.SuppressKeyPress = true;
                    if (drop.Visible && Search.TextLength == 0) drop.Visible = false;
                    else if (Search.TextLength > 0) { Search.Text = ""; Status(null, Theme.Muted); drop.Visible = false; }
                    else drop.Visible = false;
                    break;
                case Keys.Enter:
                    e.Handled = e.SuppressKeyPress = true;
                    OnSearchEnter();
                    break;
            }
        }

        void OnSearchEnter()
        {
            string t = Search.Text.Trim();
            bool scanned = LooksScanned();
            keyCount = 0;
            if (scanned)
            {
                int id = Cache.FindItemByCode(t);
                if (id == 0) id = Cache.FindItemByCode(UI.NormDigits(t));
                if (id != 0) { Pick(id); return; }   // scanned code: chosen, quantity 1 selected, Enter adds
                Status("✗ لا توجد مادة بالرمز «" + t + "» (باركود)", Theme.Danger);
                Search.SelectAll();
                return;
            }
            if (drop.Visible && drop.SelectedId != 0) { Pick(drop.SelectedId); return; }
            if (t.Length == 0) { ShowRecent(); return; }
            int exact = Cache.FindItemByCode(t);
            if (exact != 0) { Pick(exact); return; }
            RunSearch();
            if (drop.Count == 0) Status("✗ لا توجد مادة تطابق «" + t + "»", Theme.Danger);
        }

        // ------------------------------------------------------------ pick / add
        /// <summary>Item chosen: show it in the box and move to the quantity.</summary>
        public void Pick(int id)
        {
            if (id == 0 || !Enabled) return;
            pending = id;
            drop.Visible = false;
            quiet = true; Search.Text = Cache.ItemCode(id) + "  " + Cache.ItemName(id); quiet = false;
            lblUnit.Text = Cache.ItemUnit(id);
            Qty.Text = "1";
            if (drop.Avail != null)
            {
                double a = drop.Avail(id), inDoc = drop.InDoc == null ? 0 : drop.InDoc(id);
                bool shortStock = drop.WarnShort && a - inDoc <= 1e-9;
                Status(Cache.ItemName(id) + "   •   " + drop.AvailTitle + ": " + UI.Num(a) + (lblUnit.Text.Length > 0 ? " " + lblUnit.Text : "") +
                       (inDoc > 0 ? "   •   في السند: " + UI.Num(inDoc) + " (ستُضاف الكمية إلى السطر نفسه)" : "") +
                       (shortStock ? "   •   ⚠ لا يوجد رصيد كافٍ" : "") + "   •   اكتب الكمية ثم Enter",
                       shortStock ? Theme.Danger : Theme.Primary);
            }
            Qty.Focus(); Qty.SelectAll();
        }

        void Commit()
        {
            if (pending == 0)
            {
                Status(Search.TextLength == 0 ? "اكتب رمز المادة أو اسمها أولا" : "اختر المادة من القائمة أولا (↓ ثم Enter)", Theme.Warning);
                Search.Focus();
                return;
            }
            double q, p = 0;
            if (!UI.TryNum(Qty.Text, out q) || q <= 0)
            {
                Status("✗ أدخل كمية صحيحة أكبر من صفر", Theme.Danger);
                Qty.Focus(); Qty.SelectAll();
                return;
            }
            if (ShowPrice && Price.Text.Trim().Length > 0 && (!UI.TryNum(Price.Text, out p) || p < 0))
            {
                Status("✗ السعر غير صحيح", Theme.Danger);
                Price.Focus(); Price.SelectAll();
                return;
            }
            Commit(pending, q, p);
        }

        void Commit(int id, double qty, double price)
        {
            bool ok = AddLine != null && AddLine(id, qty, price);
            if (!ok) return;
            pending = 0; lblUnit.Text = "";
            quiet = true; Search.Text = ""; quiet = false;
            Qty.Text = "1"; Price.Text = "";
            drop.Visible = false;
            noRecentOnce = !Search.Focused;
            Search.Focus();
            noRecentOnce = false;
        }

        void BackToSearch()
        {
            pending = 0; lblUnit.Text = ""; Qty.Text = "1";
            Status(null, Theme.Muted);
            noRecentOnce = true;
            Search.Focus(); Search.SelectAll();
            noRecentOnce = false;
        }
    }
}
