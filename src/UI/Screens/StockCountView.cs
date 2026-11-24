using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using System;

namespace Stores
{
    // ================================================================== one physical stock count: entry of counted quantities
    class StockCountView : UserControl, IView
    {
        MainForm main;
        int countId;
        CountDoc doc;
        bool loading, dirty, fromFind;

        Label lblTitle, lblInfo, lblTotals, lblMsg;
        TextBox txtFind;
        ComboBox cmbSort;
        CheckBox chkBook;
        EntryGrid grid;
        Button btnSave, btnSheet, btnVar, btnAdd, btnPost, btnBack, btnSale, btnShop;

        static Color CountedBack { get { return Theme.GuideBack; } }

        bool CanSeeBook { get { return StockCount.CanPost || Session.Can(Session.Reports); } }

        public StockCountView(MainForm m, int id)
        {
            main = m; countId = id;
            BackColor = Theme.Bg;
            BuildUi();
            Load += delegate { Reload(); };
        }

        // ------------------------------------------------------------ layout
        void BuildUi()
        {
            Panel head = UI.Card(); head.Dock = DockStyle.Top; head.Padding = new Padding(12, 8, 12, 6);
            Panel band = new Panel(); band.Dock = DockStyle.Top; band.Height = 42;
            lblTitle = new Label(); lblTitle.Font = Theme.FontTitle; lblTitle.ForeColor = Theme.Teal; lblTitle.AutoSize = true; lblTitle.Dock = DockStyle.Right;
            btnBack = UI.BtnLight("◄ قائمة الجرد", 130); btnBack.Height = 30; btnBack.Dock = DockStyle.Left;
            band.Controls.Add(lblTitle); band.Controls.Add(btnBack);
            lblInfo = new Label(); lblInfo.Dock = DockStyle.Top; lblInfo.Height = 26; lblInfo.ForeColor = Theme.Muted; lblInfo.TextAlign = ContentAlignment.MiddleLeft;

            FlowLayoutPanel f1 = UI.Flow(); f1.Dock = DockStyle.Top; f1.WrapContents = true;
            txtFind = UI.Text(300);
            cmbSort = UI.Combo(130);
            List<ComboItem> sorts = new List<ComboItem>(); sorts.Add(new ComboItem(0, "اسم المادة")); sorts.Add(new ComboItem(1, "رمز المادة"));
            cmbSort.DisplayMember = "Name"; cmbSort.ValueMember = "Id"; cmbSort.DataSource = sorts;
            chkBook = new CheckBox(); chkBook.Text = "إظهار الرصيد الدفتري والفرق"; chkBook.AutoSize = true; chkBook.Margin = new Padding(12, 10, 8, 0);
            chkBook.Visible = CanSeeBook;
            f1.Controls.Add(UI.Field("بحث / إضافة مادة (رمز أو اسم ثم Enter)", txtFind));
            f1.Controls.Add(UI.Field("الترتيب", cmbSort));
            f1.Controls.Add(chkBook);
            head.Controls.Add(f1); head.Controls.Add(lblInfo); head.Controls.Add(band);
            UI.FitHeight(head);

            Panel gridCard = UI.Card(); gridCard.Dock = DockStyle.Fill; gridCard.Padding = new Padding(1);
            grid = new EntryGrid(); grid.Dock = DockStyle.Fill;
            UI.StyleGrid(grid);
            grid.SelectionMode = DataGridViewSelectionMode.CellSelect;
            grid.AllowUserToAddRows = false; grid.AllowUserToDeleteRows = false; grid.MultiSelect = false;
            grid.EditMode = DataGridViewEditMode.EditOnKeystrokeOrF2;
            AddCol("no", "ت", 55, true, false);
            AddCol("code", "رمز المادة", 120, true, false);
            AddCol("name", "اسم المادة", 0, true, false);
            AddCol("type", "النوع", 120, true, false);
            AddCol("counted", "الكمية المعدودة", 130, false, true);
            AddCol("book", "الرصيد الدفتري", 120, true, true);
            AddCol("diff", "الفرق", 100, true, true);
            grid.Columns["name"].MinimumWidth = 200;
            grid.Columns["counted"].DefaultCellStyle.Font = Theme.FontBold;
            grid.Columns["no"].DefaultCellStyle.ForeColor = Theme.Muted;
            foreach (DataGridViewColumn c in grid.Columns) c.SortMode = DataGridViewColumnSortMode.NotSortable;
            gridCard.Controls.Add(grid);

            Panel bottom = new Panel(); bottom.Dock = DockStyle.Bottom; bottom.Height = 82; bottom.BackColor = Theme.Bg; bottom.Padding = new Padding(0, 6, 0, 0);
            lblTotals = new Label(); lblTotals.Dock = DockStyle.Top; lblTotals.Height = 26; lblTotals.Font = Theme.FontBold; lblTotals.ForeColor = Theme.Primary; lblTotals.TextAlign = ContentAlignment.MiddleLeft;
            FlowLayoutPanel bar = new FlowLayoutPanel(); bar.Dock = DockStyle.Fill; bar.FlowDirection = FlowDirection.LeftToRight;
            btnSave = UI.Btn("حفظ  (Ctrl+S)", Theme.Accent, 140);
            btnAdd = UI.Btn("+ إضافة مادة  (F2)", Theme.Purple, 170);
            btnSheet = UI.Btn("طباعة ورقة الجرد", Theme.Teal, 160);
            btnVar = UI.Btn("تقرير الفروقات", Theme.Primary, 150);
            btnPost = UI.Btn("ترحيل التسوية", Theme.Danger, 150);
            btnSale = UI.BtnLight("سند النقص", 110); btnShop = UI.BtnLight("سند الزيادة", 110);
            lblMsg = new Label(); lblMsg.AutoSize = true; lblMsg.Margin = new Padding(16, 12, 4, 0); lblMsg.Font = Theme.FontBold;
            bar.Controls.AddRange(new Control[] { btnSave, btnAdd, btnSheet, btnVar, btnPost, btnSale, btnShop, lblMsg });
            btnVar.Visible = CanSeeBook;
            btnPost.Visible = StockCount.CanPost;
            Label hint = new Label(); hint.Dock = DockStyle.Bottom; hint.Height = 18; hint.ForeColor = Theme.Muted; hint.Font = Theme.FontSmall; hint.TextAlign = ContentAlignment.MiddleLeft;
            hint.Text = "اكتب الكمية ثم Enter للانتقال للمادة التالية   •   اكتب رمز المادة أو اسمها في مربع البحث ثم Enter   •   F2 بحث وإضافة مادة غير موجودة في القائمة   •   Delete مسح الكمية";
            bottom.Controls.Add(bar); bottom.Controls.Add(lblTotals); bottom.Controls.Add(hint);

            Controls.Add(gridCard); Controls.Add(head); Controls.Add(bottom);

            grid.EnterPressed += delegate { EnterNext(); };
            grid.CellEndEdit += Grid_CellEndEdit;
            grid.CellFormatting += Grid_CellFormatting;
            grid.KeyDown += Grid_KeyDown;
            grid.DataError += delegate(object s, DataGridViewDataErrorEventArgs e) { e.ThrowException = false; };
            grid.EditingControlShowing += delegate(object s, DataGridViewEditingControlShowingEventArgs e)
            {
                TextBox tb = e.Control as TextBox;
                if (tb != null) { tb.RightToLeft = RightToLeft.No; tb.AutoCompleteMode = AutoCompleteMode.None; }
            };
            txtFind.KeyDown += delegate(object s, KeyEventArgs e)
            {
                if (e.KeyCode == Keys.Enter) { e.SuppressKeyPress = true; FindOrAdd(txtFind.Text.Trim()); }
            };
            txtFind.AutoCompleteMode = AutoCompleteMode.SuggestAppend; txtFind.AutoCompleteSource = AutoCompleteSource.CustomSource; txtFind.AutoCompleteCustomSource = Cache.ItemNames;
            cmbSort.SelectedIndexChanged += delegate { if (!loading && doc != null) Fill(); };
            chkBook.CheckedChanged += delegate { ShowBookCols(); };
            btnSave.Click += delegate { Save(); };
            btnAdd.Click += delegate { PickAndAdd(""); };
            btnSheet.Click += delegate { PrintSheet(); };
            btnVar.Click += delegate { ShowVariance(); };
            btnPost.Click += delegate { PostCount(); };
            btnBack.Click += delegate { if (CanLeave()) main.Show(new StockCountsView(main)); };
            btnSale.Click += delegate { if (doc != null && doc.SaleId > 0 && CanLeave()) main.OpenDoc(DocKind.Issue, doc.SaleId); };
            btnShop.Click += delegate { if (doc != null && doc.ShopId > 0 && CanLeave()) main.OpenDoc(DocKind.Receipt, doc.ShopId); };
        }

        void AddCol(string name, string header, int width, bool ro, bool num)
        {
            DataGridViewTextBoxColumn c = UI.Col(name, header, null, width, num);
            c.ReadOnly = ro;
            if (ro && name != "no") c.DefaultCellStyle.BackColor = Theme.RowAlt;
            grid.Columns.Add(c);
        }

        // ------------------------------------------------------------ state
        void MarkDirty()
        {
            if (loading) return;
            if (!dirty) { dirty = true; lblMsg.Text = "● كميات غير محفوظة"; lblMsg.ForeColor = Theme.Warning; }
        }

        void Msg(string text, Color c) { lblMsg.Text = text; lblMsg.ForeColor = c; }

        void Reload()
        {
            try
            {
                UI.WaitCursor(true);
                doc = StockCount.Load(countId);
            }
            catch (Exception ex) { UI.Error(ex); return; }
            finally { UI.WaitCursor(false); }
            if (doc == null) { UI.Warn("هذا الجرد غير موجود (ربما حُذف من حاسوب آخر)."); main.Show(new StockCountsView(main)); return; }
            dirty = false; Msg("", Theme.Muted);
            lblTitle.Text = "الجرد الفعلي رقم " + doc.No;
            string info = "التاريخ: " + UI.Date(doc.Date) + "     |     المخزن: " + Cache.Store(doc.Store) + "     |     نوع المواد: " +
                          (doc.TypeNo == 0 ? "كل الأنواع" : Cache.Type(doc.TypeNo)) + "     |     أنشأه: " + doc.User + "     |     الحالة: " + StockCount.StatusName(doc.Status);
            if (doc.Posted)
                info += " بتاريخ " + UI.Date(doc.PostedAt) + " (" + doc.PostedBy + ")" + (doc.SaleNo.Length > 0 ? "  —  النقص: إخراج رقم " + doc.SaleNo : "") +
                        (doc.ShopNo.Length > 0 ? "  —  الزيادة: إدخال رقم " + doc.ShopNo : "");
            if (doc.Note.Length > 0) info += "     |     " + doc.Note;
            lblInfo.Text = info;
            lblInfo.ForeColor = doc.Posted ? Theme.Danger : Theme.Muted;
            bool open = !doc.Posted;
            grid.ReadOnly = !open;
            grid.Columns["counted"].ReadOnly = !open;
            btnSave.Enabled = btnAdd.Enabled = btnPost.Enabled = open;
            btnSale.Visible = doc.SaleId > 0; btnShop.Visible = doc.ShopId > 0;
            Fill();
            ShowBookCols();
            if (open) BeginInvoke((Action)delegate { GoToFirstUncounted(); });
        }

        void ShowBookCols()
        {
            bool show = chkBook.Checked && CanSeeBook;
            grid.Columns["book"].Visible = show; grid.Columns["diff"].Visible = show;
            UpdateTotals();
        }

        void Fill()
        {
            loading = true;
            grid.SuspendLayout();
            grid.Rows.Clear();
            IEnumerable<CountLine> lines = UI.Sel(cmbSort) == 1
                ? doc.Lines.OrderBy(l => Cache.ItemCode(l.ItemNo), StringComparer.Ordinal).ThenBy(l => Cache.ItemName(l.ItemNo))
                : doc.Lines.OrderBy(l => Cache.ItemName(l.ItemNo)).ThenBy(l => Cache.ItemCode(l.ItemNo), StringComparer.Ordinal);
            List<DataGridViewRow> rows = new List<DataGridViewRow>();
            int n = 0;
            foreach (CountLine l in lines)
            {
                DataGridViewRow r = new DataGridViewRow();
                r.CreateCells(grid);
                SetRow(r, l, ++n);
                rows.Add(r);
            }
            grid.Rows.AddRange(rows.ToArray());
            grid.ResumeLayout();
            loading = false;
            UpdateTotals();
        }

        void SetRow(DataGridViewRow r, CountLine l, int no)
        {
            SetCell(r, "no", no);
            SetCell(r, "code", Cache.ItemCode(l.ItemNo));
            SetCell(r, "name", Cache.ItemName(l.ItemNo) + (l.Added ? "   (أُضيفت أثناء الجرد)" : ""));
            System.Data.DataRow ir = Cache.Item(l.ItemNo);
            SetCell(r, "type", ir == null ? "" : Db.S(ir["type_name"]));
            SetCell(r, "counted", l.Counted.HasValue ? (object)l.Counted.Value : null);
            SetCell(r, "book", l.Id == 0 ? null : (object)l.Book);
            SetCell(r, "diff", l.Counted.HasValue && l.Id > 0 ? (object)l.Diff : null);
            r.Tag = l;
        }

        void SetCell(DataGridViewRow r, string col, object v) { r.Cells[grid.Columns[col].Index].Value = v; }

        static CountLine LineOf(DataGridViewRow r) { return r == null ? null : r.Tag as CountLine; }

        void UpdateTotals()
        {
            if (doc == null) return;
            int counted = doc.Lines.Count(l => l.Counted.HasValue);
            string s = "عدد المواد: " + doc.Lines.Count.ToString("#,##0") + "     المعدودة: " + counted.ToString("#,##0") + "     المتبقية: " + (doc.Lines.Count - counted).ToString("#,##0");
            if (chkBook.Checked && CanSeeBook)
            {
                List<CountLine> c = doc.Lines.Where(l => l.Counted.HasValue && l.Id > 0).ToList();
                int ns = c.Count(l => l.Diff < -0.0000001), np = c.Count(l => l.Diff > 0.0000001);
                s += "     |     نقص: " + ns + " مادة (" + UI.Num(-c.Where(l => l.Diff < 0).Sum(l => l.Diff)) + ")" +
                     "     زيادة: " + np + " مادة (" + UI.Num(c.Where(l => l.Diff > 0).Sum(l => l.Diff)) + ")";
            }
            lblTotals.Text = s;
        }

        // ------------------------------------------------------------ navigation / entry
        void GoTo(int row)
        {
            if (row < 0 || row >= grid.Rows.Count) return;
            try { grid.CurrentCell = grid.Rows[row].Cells["counted"]; grid.Focus(); } catch { }
        }

        void GoToFirstUncounted()
        {
            for (int i = 0; i < grid.Rows.Count; i++) if (!LineOf(grid.Rows[i]).Counted.HasValue) { GoTo(i); return; }
            GoTo(0);
        }

        int RowOfItem(int item)
        {
            for (int i = 0; i < grid.Rows.Count; i++) { CountLine l = LineOf(grid.Rows[i]); if (l != null && l.ItemNo == item) return i; }
            return -1;
        }

        void EnterNext()
        {
            if (grid.CurrentCell == null) return;
            int row = grid.CurrentCell.RowIndex;
            if (grid.IsCurrentCellInEditMode) grid.EndEdit();
            if (fromFind) { fromFind = false; txtFind.Focus(); txtFind.SelectAll(); return; }
            if (row + 1 < grid.Rows.Count) GoTo(row + 1);
        }

        void FindOrAdd(string text)
        {
            if (doc == null || text.Length == 0) { if (text.Length == 0) PickAndAdd(""); return; }
            int id = Cache.FindItemByCode(text);
            if (id == 0) id = Cache.FindItemByName(text);
            if (id == 0) { PickAndAdd(text); return; }
            JumpOrAdd(id);
        }

        void PickAndAdd(string text)
        {
            if (doc == null) return;
            if (grid.IsCurrentCellInEditMode) grid.EndEdit();
            int id = SearchDialog.PickItem(FindForm(), text, true);
            if (id != 0) JumpOrAdd(id);
        }

        void JumpOrAdd(int item)
        {
            int row = RowOfItem(item);
            if (row < 0)
            {
                if (doc.Posted) { UI.Warn("المادة غير موجودة في هذا الجرد."); return; }
                CountLine l = new CountLine(); l.ItemNo = item; l.Added = true;
                doc.Lines.Add(l);
                DataGridViewRow r = new DataGridViewRow(); r.CreateCells(grid);
                SetRow(r, l, grid.Rows.Count + 1);
                grid.Rows.Add(r);
                row = grid.Rows.Count - 1;
                MarkDirty();
                Msg("✓ أُضيفت للجرد: " + Cache.ItemName(item), Theme.Success);
                UpdateTotals();
            }
            else Msg(Cache.ItemName(item), Theme.Primary);
            txtFind.Text = "";
            fromFind = true;
            GoTo(row);
        }

        void Grid_CellEndEdit(object sender, DataGridViewCellEventArgs e)
        {
            if (loading || e.RowIndex < 0 || grid.Columns[e.ColumnIndex].Name != "counted") return;
            DataGridViewRow r = grid.Rows[e.RowIndex];
            CountLine l = LineOf(r);
            if (l == null) return;
            string t = Db.S(r.Cells["counted"].Value).Trim();
            double? v = null;
            if (t.Length > 0)
            {
                double x;
                if (!UI.TryNum(t, out x) || x < 0)
                {
                    Msg("كمية غير صحيحة: " + t, Theme.Danger);
                    r.Cells["counted"].Value = l.Counted.HasValue ? (object)l.Counted.Value : null;
                    return;
                }
                v = Math.Round(x, 6);
            }
            if (v != l.Counted) { l.Counted = v; l.Dirty = true; MarkDirty(); }
            loading = true;
            r.Cells["counted"].Value = v.HasValue ? (object)v.Value : null;
            r.Cells["diff"].Value = v.HasValue && l.Id > 0 ? (object)l.Diff : null;
            loading = false;
            UpdateTotals();
        }

        void Grid_CellFormatting(object sender, DataGridViewCellFormattingEventArgs e)
        {
            if (e.RowIndex < 0) return;
            CountLine l = LineOf(grid.Rows[e.RowIndex]);
            if (l == null) return;
            string col = grid.Columns[e.ColumnIndex].Name;
            if (col == "counted" && l.Counted.HasValue) e.CellStyle.BackColor = CountedBack;
            if (col == "diff" && l.Counted.HasValue)
            {
                if (l.Diff < -0.0000001) e.CellStyle.ForeColor = Theme.Danger;
                else if (l.Diff > 0.0000001) e.CellStyle.ForeColor = Theme.Success;
            }
            if (col == "name" && l.Added) e.CellStyle.ForeColor = Theme.Purple;
        }

        void Grid_KeyDown(object sender, KeyEventArgs e)
        {
            if (doc == null || doc.Posted || grid.CurrentCell == null || grid.IsCurrentCellInEditMode) return;
            if (e.KeyCode != Keys.Delete) return;
            e.Handled = true;
            DataGridViewRow r = grid.Rows[grid.CurrentCell.RowIndex];
            CountLine l = LineOf(r);
            if (l == null) return;
            if (l.Added && !l.Counted.HasValue)
            {
                if (!UI.Confirm("حذف المادة المضافة من الجرد: " + Cache.ItemName(l.ItemNo) + " ؟")) return;
                if (l.Id > 0) doc.Removed.Add(l.Id);
                doc.Lines.Remove(l);
                grid.Rows.Remove(r);
                for (int i = 0; i < grid.Rows.Count; i++) grid.Rows[i].Cells["no"].Value = i + 1;
                MarkDirty(); UpdateTotals();
                return;
            }
            if (!l.Counted.HasValue) return;
            l.Counted = null; l.Dirty = true;
            r.Cells["counted"].Value = null; r.Cells["diff"].Value = null;
            MarkDirty(); UpdateTotals();
        }

        // ------------------------------------------------------------ commands
        bool Save()
        {
            if (doc == null || doc.Posted) return true;
            if (grid.IsCurrentCellInEditMode) grid.EndEdit();
            try
            {
                UI.WaitCursor(true);
                StockCount.SaveCounts(doc);
                dirty = false;
                Msg("✓ تم الحفظ " + DateTime.Now.ToString("HH:mm"), Theme.Success);
                // added lines now have their book balance
                loading = true;
                foreach (DataGridViewRow r in grid.Rows)
                {
                    CountLine l = LineOf(r);
                    if (l == null || !l.Added) continue;
                    r.Cells["book"].Value = l.Book;
                    r.Cells["diff"].Value = l.Counted.HasValue ? (object)l.Diff : null;
                }
                loading = false;
                UpdateTotals();
                return true;
            }
            catch (Exception ex) { UI.WaitCursor(false); UI.Error(ex); return false; }
            finally { UI.WaitCursor(false); }
        }

        void PrintSheet()
        {
            if (doc == null) return;
            PrintPreviewForm.Show(FindForm(), CountReports.Sheet(doc, UI.Sel(cmbSort) == 1));
        }

        void ShowVariance()
        {
            if (doc == null) return;
            if (dirty && !Save()) return;
            using (ReportViewerForm f = new ReportViewerForm(CountReports.Variance(doc, false))) { f.WindowState = FormWindowState.Maximized; f.ShowDialog(FindForm()); }
        }

        void PostCount()
        {
            if (doc == null || doc.Posted) return;
            if (!StockCount.CanPost) { UI.Warn("ترحيل تسوية الجرد للمدير أو لمن لديه صلاحية الإعدادات فقط."); return; }
            if (dirty && !Save()) return;
            List<CountLine> c = doc.Lines.Where(l => l.Counted.HasValue).ToList();
            if (c.Count == 0) { UI.Warn("لم تُدخل أي كمية معدودة بعد."); return; }
            if (PeriodLock.IsLocked(doc.Date)) { UI.Warn(PeriodLock.Error(doc.Date).Message); return; }
            int ns = c.Count(l => l.Diff < -0.0000001), np = c.Count(l => l.Diff > 0.0000001);
            double qs = -c.Where(l => l.Diff < 0).Sum(l => l.Diff), qp = c.Where(l => l.Diff > 0).Sum(l => l.Diff);
            int notCounted = doc.Lines.Count - c.Count;
            int changed = StockCount.ChangedSinceCreated(doc);
            string msg = "ترحيل تسوية الجرد رقم " + doc.No + " — " + Cache.Store(doc.Store) + "\n\n" +
                         "• المواد المعدودة: " + c.Count + "\n" +
                         "• نقص: " + ns + " مادة (مجموع " + UI.Num(qs) + ") ← سند إخراج مخزني «تسوية جرد (نقص)»\n" +
                         "• زيادة: " + np + " مادة (مجموع " + UI.Num(qp) + ") ← سند إدخال مخزني «تسوية جرد (زيادة)»\n" +
                         (notCounted > 0 ? "• لم تُعد: " + notCounted + " مادة (تبقى أرصدتها كما هي)\n" : "") +
                         (changed > 0 ? "\n⚠ " + changed + " مادة تغيّر رصيدها بسندات بعد إنشاء الجرد؛ تُرحّل الفروقات محسوبة من الرصيد عند إنشاء الجرد.\n" : "") +
                         "\nبعد الترحيل يُقفل الجرد ولا يمكن تعديله. متابعة؟";
            if (!UI.Confirm(msg)) return;
            try
            {
                UI.WaitCursor(true);
                if (!Db.IsSql) Backup.Now("قبل ترحيل الجرد " + doc.No);
                CountDoc posted = StockCount.Post(doc.Id);
                Cache.LoadItems();
                UI.WaitCursor(false);
                Reload();
                UI.Info("تم ترحيل تسوية الجرد ✓" + (posted.SaleNo.Length > 0 ? "\nالنقص: إخراج مخزني رقم " + posted.SaleNo : "") +
                        (posted.ShopNo.Length > 0 ? "\nالزيادة: إدخال مخزني رقم " + posted.ShopNo : "") +
                        (posted.SaleNo.Length == 0 && posted.ShopNo.Length == 0 ? "\nلا توجد فروقات: كل المواد المعدودة مطابقة." : ""));
            }
            catch (Exception ex) { UI.WaitCursor(false); UI.Error(ex); }
            finally { UI.WaitCursor(false); }
        }

        public bool CanLeave()
        {
            if (grid.IsCurrentCellInEditMode) grid.EndEdit();
            if (!dirty) return true;
            DialogResult r = UI.YesNoCancel("يوجد كميات جرد غير محفوظة.\nهل تريد حفظها؟");
            if (r == DialogResult.Yes) return Save();
            return r == DialogResult.No;
        }

        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            switch (keyData)
            {
                case Keys.Control | Keys.S: Save(); return true;
                case Keys.Control | Keys.P: PrintSheet(); return true;
                case Keys.Control | Keys.F: txtFind.Focus(); txtFind.SelectAll(); return true;
                case Keys.F2:
                    if (doc != null && !doc.Posted)
                    {
                        string text = "";
                        if (grid.IsCurrentCellInEditMode) grid.EndEdit();
                        if (txtFind.Focused) text = txtFind.Text.Trim();
                        PickAndAdd(text);
                        return true;
                    }
                    break;
            }
            return base.ProcessCmdKey(ref msg, keyData);
        }
    }
}
