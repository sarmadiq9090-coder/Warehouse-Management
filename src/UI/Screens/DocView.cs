using System.Collections.Generic;
using System.Data;
using System.Drawing;
using System.Windows.Forms;
using System;

namespace Stores
{
    // ================================================================== document editor
    class DocView : UserControl, IView
    {
        MainForm main;
        DocKind kind;
        Doc doc;
        bool loading, dirty;
        Dictionary<int, double> origQty = new Dictionary<int, double>();
        int origStore, presetProject;

        Label lblTitle, lblState, lblTotals, lblMsg, lblGuide;
        TextBox txtNo, txtNote;
        DateTimePicker dtp;
        PersonPicker person;
        ProjectPicker project;
        ComboBox cmbStore, cmbStore2, cmbRType;
        EntryGrid grid;
        ItemEntryBar entry;
        StockSnapshot snap;                 // balances of the document's store for the entry bar (no query per keystroke)
        List<int> recent, recentAdded = new List<int>();
        int flashItem;
        Timer flashTimer;
        Button btnSave, btnNew, btnDelete, btnPrint, btnPrev, btnNext, btnFind;

        bool Adv { get { return Session.Advanced; } }
        int PermAdd { get { return kind == DocKind.Issue || kind == DocKind.Reserve ? Session.IssAdd : kind == DocKind.Transfer ? Session.Transfer : Session.RecAdd; } }
        int PermEdit { get { return kind == DocKind.Issue || kind == DocKind.Reserve ? Session.IssEdit : kind == DocKind.Transfer ? Session.Transfer : Session.RecEdit; } }
        int PermDelete { get { return kind == DocKind.Issue || kind == DocKind.Reserve ? Session.IssDelete : kind == DocKind.Transfer ? Session.Transfer : Session.RecDelete; } }
        bool HasPrice { get { return kind == DocKind.Receipt && Adv; } }
        bool OutDoc { get { return Posting.IsOut(kind); } }
        bool UsesProject { get { return Db.HasProj && (kind == DocKind.Issue || kind == DocKind.Return || kind == DocKind.Reserve); } }
        /// <summary>Issues and issue returns: the cost centre is the recipient (one field, no person picker).</summary>
        bool CcRecipient { get { return Posting.CcIsRecipient(kind); } }
        bool UsesPerson { get { return kind != DocKind.Transfer && kind != DocKind.Reserve && !CcRecipient; } }

        Color KindColor
        {
            get
            {
                switch (kind)
                {
                    case DocKind.Issue: return Theme.Primary;
                    case DocKind.Receipt: return Theme.Accent;
                    case DocKind.Return: return Theme.Teal;
                    case DocKind.PurchaseReturn: return Theme.Warning;
                    case DocKind.Reserve: return Theme.Purple;
                    default: return Theme.PrimaryDark;
                }
            }
        }

        string Guide
        {
            get
            {
                switch (kind)
                {
                    case DocKind.Issue: return CcRecipient
                        ? "إخراج مواد من المخزن إلى مركز كلفة:   ① اختر مركز الكلفة (المستلم)   ② ابحث عن المادة واكتب الكمية ثم Enter   ③ اضغط «حفظ»"
                        : "إخراج مواد من المخزن إلى مركز كلفة:   ① اختر مركز الكلفة   ② اختر المستلم   ③ ابحث عن المادة واكتب الكمية ثم Enter   ④ اضغط «حفظ»";
                    case DocKind.Receipt: return "إدخال مواد جديدة إلى المخزن:   ① اختر المجهز   ② اختر المخزن   ③ ابحث عن المادة واكتب الكمية ثم Enter   ④ اضغط «حفظ»";
                    case DocKind.Return: return CcRecipient
                        ? "مواد رجعت من مركز كلفة إلى المخزن:   ① اختر مركز الكلفة الذي أعادها   ② ابحث عن المواد واكتب الكميات   ③ اضغط «حفظ»"
                        : "مواد رجعت من مركز كلفة إلى المخزن:   ① اختر مركز الكلفة   ② اختر الجهة التي أعادتها   ③ ابحث عن المواد واكتب الكميات   ④ اضغط «حفظ»";
                    case DocKind.PurchaseReturn: return "إرجاع مواد إلى المجهز (تنقص من المخزن):   ① اختر المجهز   ② ابحث عن المواد واكتب الكميات   ③ اضغط «حفظ»";
                    case DocKind.Reserve: return "حجز مواد لمركز كلفة (لا تخرج من المخزن لكنها لا تُخرج لغيره):   ① اختر مركز الكلفة   ② ابحث عن المواد واكتب الكميات   ③ اضغط «حفظ»";
                    default: return "نقل مواد بين مخزنين:   ① اختر المخزن المنقول منه وإليه   ② ابحث عن المواد واكتب الكميات   ③ اضغط «حفظ»";
                }
            }
        }

        public DocView(MainForm m, DocKind k, int id, int proj)
        {
            main = m; kind = k; presetProject = proj;
            BackColor = Theme.Bg;
            // the stored document decides its kind (a purchase return opened as an issue, a return as a receipt…)
            // before the fields are built: issues and returns have no person field, the others do
            Doc loaded = id > 0 ? Posting.Load(kind, id) : null;
            if (loaded != null) kind = loaded.Kind;
            BuildUi();
            Load += delegate
            {
                if (loaded != null) { Show(loaded); return; }
                NewDoc();
            };
        }

        // ------------------------------------------------------------ layout
        void BuildUi()
        {
            Panel head = UI.Card(); head.Dock = DockStyle.Top; head.Height = 150; head.Padding = new Padding(12, 8, 12, 6);
            Panel band = new Panel(); band.Dock = DockStyle.Top; band.Height = 42;
            lblTitle = new Label(); lblTitle.Font = Theme.FontTitle; lblTitle.ForeColor = KindColor; lblTitle.AutoSize = true; lblTitle.Dock = DockStyle.Right;
            lblState = new Label(); lblState.Font = Theme.Font; lblState.ForeColor = Theme.Muted; lblState.AutoSize = true; lblState.Dock = DockStyle.Right; lblState.Padding = new Padding(12, 10, 12, 0);
            FlowLayoutPanel nav = new FlowLayoutPanel(); nav.Dock = DockStyle.Left; nav.Width = 380; nav.FlowDirection = FlowDirection.RightToLeft; nav.WrapContents = false;
            btnPrev = UI.BtnLight("► السابق", 95); btnNext = UI.BtnLight("التالي ◄", 95); btnFind = UI.BtnLight("فتح سند برقم…", 140);
            btnPrev.Height = btnNext.Height = btnFind.Height = 30;
            nav.Controls.Add(btnNext); nav.Controls.Add(btnPrev); nav.Controls.Add(btnFind);
            band.Controls.Add(lblState); band.Controls.Add(lblTitle);

            lblGuide = new Label(); lblGuide.Dock = DockStyle.Top; lblGuide.Height = 30; lblGuide.Text = "   " + Guide;
            lblGuide.BackColor = Theme.GuideBack; lblGuide.ForeColor = Theme.AccentDark; lblGuide.Font = Theme.FontBold;
            lblGuide.TextAlign = ContentAlignment.MiddleLeft; lblGuide.Visible = !Adv;

            FlowLayoutPanel f1 = UI.Flow(); f1.Dock = DockStyle.Top; f1.WrapContents = true;
            txtNo = UI.Text(110); txtNo.ReadOnly = true; txtNo.BackColor = Theme.RowAlt; txtNo.TabStop = false;
            dtp = UI.DatePicker(DateTime.Today);
            person = new PersonPicker(300);
            project = new ProjectPicker(320);
            cmbStore = UI.Combo(170); cmbStore2 = UI.Combo(170); cmbRType = UI.Combo(110);
            UI.Fill(cmbStore, Cache.Stores, "stor_no", "stor_name", null);
            UI.Fill(cmbStore2, Cache.Stores, "stor_no", "stor_name", null);
            List<ComboItem> rt = new List<ComboItem>(); rt.Add(new ComboItem(2, "محلي")); rt.Add(new ComboItem(1, "استيراد"));
            cmbRType.DisplayMember = "Name"; cmbRType.ValueMember = "Id"; cmbRType.DataSource = rt;

            f1.Controls.Add(UI.Field("رقم السند", txtNo));
            f1.Controls.Add(UI.Field("التاريخ", dtp));
            if (UsesProject) f1.Controls.Add(UI.Field(kind == DocKind.Return ? "مركز الكلفة (المُعاد منه)" : kind == DocKind.Issue && CcRecipient ? "مركز الكلفة (المستلم)" : "مركز الكلفة", project));
            if (UsesPerson)
                f1.Controls.Add(UI.Field(kind == DocKind.Issue ? "المستلم" : kind == DocKind.Return ? "الجهة المُعيدة" : kind == DocKind.PurchaseReturn ? "المجهز (يُرجع إليه)" : "المجهز / الجهة", person));
            f1.Controls.Add(UI.Field(OutDoc ? "من مخزن" : "إلى مخزن", cmbStore));
            if (kind == DocKind.Transfer) f1.Controls.Add(UI.Field("إلى مخزن", cmbStore2));
            if (kind == DocKind.Receipt) f1.Controls.Add(UI.Field("النوع", cmbRType));

            Panel f2 = new Panel(); f2.Dock = DockStyle.Top; f2.Height = 38; f2.Padding = new Padding(6, 4, 6, 4);
            txtNote = UI.Text(400); txtNote.MaxLength = kind == DocKind.Reserve ? 250 : 4000; txtNote.Dock = DockStyle.Fill;
            Label ln = UI.Lbl("ملاحظات"); ln.Dock = DockStyle.Right; ln.AutoSize = false; ln.Width = 80; ln.Margin = new Padding(0);
            f2.Controls.Add(txtNote); f2.Controls.Add(ln);

            head.Controls.Add(f2); head.Controls.Add(f1); head.Controls.Add(lblGuide); head.Controls.Add(band);
            UI.FitHeight(head);

            // grid
            Panel gridCard = UI.Card(); gridCard.Dock = DockStyle.Fill; gridCard.Padding = new Padding(1);
            grid = new EntryGrid(); grid.Dock = DockStyle.Fill;
            UI.StyleGrid(grid);
            grid.SelectionMode = DataGridViewSelectionMode.CellSelect;
            grid.AllowUserToAddRows = Adv; grid.AllowUserToDeleteRows = false;
            grid.EditMode = DataGridViewEditMode.EditOnKeystrokeOrF2;
            grid.RowTemplate.Height = Adv ? 32 : 36;
            AddCol("no", "ت", 50, true, false);
            AddCol("code", "رمز المادة", 130, false, false);
            AddCol("name", "اسم المادة", 0, false, false);
            AddCol("unit", "الوحدة", 80, true, false);
            AddCol("qty", kind == DocKind.Reserve ? "الكمية المحجوزة" : "الكمية", 120, false, true);
            AddCol("avail", OutDoc ? "المتوفر (غير محجوز)" : "الرصيد الحالي", 150, true, true);
            AddCol("resv", "محجوز له", 150, true, true);
            AddCol("price", "سعر الوحدة", 120, false, true);
            AddCol("used", "المصروف منها", 120, true, true);
            AddCol("lineId", "", 10, true, false); AddCol("itemNo", "", 10, true, false);
            // «✕ حذف» on every line: remove an item from the list with one click (owner's request)
            DataGridViewButtonColumn del = new DataGridViewButtonColumn();
            del.Name = "del"; del.HeaderText = ""; del.Text = "✕ حذف"; del.UseColumnTextForButtonValue = true; del.Width = 80;
            del.FlatStyle = FlatStyle.Flat; del.DefaultCellStyle.ForeColor = Theme.Danger; del.DefaultCellStyle.SelectionForeColor = Theme.Danger;
            del.SortMode = DataGridViewColumnSortMode.NotSortable;
            grid.Columns.Add(del);
            grid.CellPainting += delegate(object s, DataGridViewCellPaintingEventArgs e)
            {
                // the empty «new line» row has nothing to delete: no button there
                if (e.RowIndex < 0 || e.ColumnIndex < 0 || grid.Columns[e.ColumnIndex].Name != "del" || !grid.Rows[e.RowIndex].IsNewRow) return;
                e.PaintBackground(e.ClipBounds, false); e.Handled = true;
            };
            grid.Columns["lineId"].Visible = false; grid.Columns["itemNo"].Visible = false;
            grid.Columns["price"].Visible = HasPrice;
            grid.Columns["used"].Visible = false;
            grid.Columns["resv"].Visible = kind == DocKind.Issue && Db.HasProj;
            grid.Columns["no"].DefaultCellStyle.ForeColor = Theme.Muted;
            grid.Columns["avail"].DefaultCellStyle.ForeColor = Theme.Muted;
            grid.Columns["resv"].DefaultCellStyle.ForeColor = Theme.Purple;
            grid.Columns["qty"].DefaultCellStyle.Font = Theme.FontBold;
            if (!Adv) { grid.Columns["code"].ReadOnly = true; grid.Columns["name"].ReadOnly = true; }
            foreach (DataGridViewColumn c in grid.Columns) c.SortMode = DataGridViewColumnSortMode.NotSortable;
            // clear lines around every cell, like the printed document
            grid.CellBorderStyle = DataGridViewCellBorderStyle.Single;
            grid.GridColor = Theme.GridLine;
            grid.ColumnHeadersBorderStyle = DataGridViewHeaderBorderStyle.Single;
            grid.EnableHeadersVisualStyles = false;
            grid.ColumnHeadersDefaultCellStyle.BackColor = Theme.GridHeader;
            grid.ColumnHeadersDefaultCellStyle.ForeColor = Theme.GridHeaderText;
            grid.ColumnHeadersDefaultCellStyle.SelectionBackColor = Theme.GridHeader;
            grid.ColumnHeadersDefaultCellStyle.Font = Theme.FontBold;
            grid.CellContentClick += delegate(object s, DataGridViewCellEventArgs e)
            {
                if (e.RowIndex < 0 || grid.Columns[e.ColumnIndex].Name != "del" || grid.ReadOnly) return;
                if (grid.Rows[e.RowIndex].IsNewRow) return;
                grid.CurrentCell = grid.Rows[e.RowIndex].Cells["name"];
                DeleteCurrentRow();
            };
            gridCard.Controls.Add(grid);

            // item entry bar (search + quantity on the same page), above the lines
            entry = new ItemEntryBar(this);
            entry.ShowPrice = HasPrice;
            entry.AvailTitle = OutDoc ? "المتوفر" : "الرصيد الحالي";
            entry.WarnShort = OutDoc && kind != DocKind.Reserve;
            entry.Avail = FastAvailable;
            entry.InDoc = QtyInDoc;
            entry.Recent = RecentItems;
            entry.RecentTitle = "آخر المواد المستعملة في " + Posting.KindName(kind);
            entry.AddLine = AddFromBar;
            Panel sep = new Panel(); sep.Dock = DockStyle.Top; sep.Height = 1; sep.BackColor = Theme.Border;
            gridCard.Controls.Add(sep); gridCard.Controls.Add(entry);
            flashTimer = new Timer(); flashTimer.Interval = 1400;
            flashTimer.Tick += delegate { flashTimer.Stop(); flashItem = 0; grid.Invalidate(); };

            // action bar at the TOP of the page (owner's request): save, new, print, delete • previous / next
            Panel actions = new Panel(); actions.Dock = DockStyle.Top; actions.Height = 48; actions.BackColor = Theme.Bg; actions.Padding = new Padding(0, 2, 0, 6);
            // totals + hint at the bottom
            Panel bottom = new Panel(); bottom.Dock = DockStyle.Bottom; bottom.Height = 48; bottom.BackColor = Theme.Bg; bottom.Padding = new Padding(0, 4, 0, 0);
            lblTotals = new Label(); lblTotals.Dock = DockStyle.Top; lblTotals.Height = 26; lblTotals.Font = Theme.FontBold; lblTotals.ForeColor = Theme.Primary;
            lblTotals.TextAlign = ContentAlignment.MiddleLeft;
            FlowLayoutPanel bar = new FlowLayoutPanel(); bar.Dock = DockStyle.Fill; bar.FlowDirection = FlowDirection.LeftToRight; bar.WrapContents = false;
            btnSave = UI.Btn(Adv ? "حفظ  (Ctrl+S)" : "حفظ", Theme.Accent, 140);
            btnNew = UI.Btn(Adv ? "جديد  (Ctrl+N)" : "سند جديد", Theme.Primary, 140);
            btnPrint = UI.Btn(Adv ? "طباعة  (Ctrl+P)" : "طباعة", Theme.Teal, 140);
            btnDelete = UI.Btn(kind == DocKind.Reserve ? "إلغاء الحجز" : "حذف السند", Theme.Danger, 120);
            lblMsg = new Label(); lblMsg.AutoSize = true; lblMsg.Margin = new Padding(16, 12, 4, 0); lblMsg.Font = Theme.FontBold;
            bar.Controls.AddRange(new Control[] { btnSave, btnNew, btnPrint, btnDelete, lblMsg });
            Label hint = new Label(); hint.Dock = DockStyle.Bottom; hint.Height = 18; hint.ForeColor = Theme.Muted; hint.Font = Theme.FontSmall;
            hint.Text = Adv ? "F2 مربع البحث عن مادة   •   Enter للانتقال   •   Delete حذف السطر   •   PageUp/PageDown السند السابق/التالي   •   اكتب الرمز مباشرة في العمود"
                            : "لإضافة مادة اكتب جزءا من اسمها أو رمزها في مربع البحث ثم Enter واكتب الكمية ثم Enter. لتغيير الكمية انقر عليها في الجدول. لحذف مادة اضغط «✕ حذف» في سطرها.";
            hint.TextAlign = ContentAlignment.MiddleLeft;
            actions.Controls.Add(bar); actions.Controls.Add(nav);
            bottom.Controls.Add(lblTotals); bottom.Controls.Add(hint);
            btnFind.Visible = Adv;

            Controls.Add(gridCard); Controls.Add(head); Controls.Add(actions); Controls.Add(bottom);

            // events
            grid.EnterPressed += delegate { OnEnter(); };
            grid.CellEndEdit += Grid_CellEndEdit;
            grid.CellValueChanged += delegate(object s, DataGridViewCellEventArgs e) { if (!loading && e.RowIndex >= 0) { MarkDirty(); if (grid.Columns[e.ColumnIndex].Name == "qty") UpdateTotals(); } };
            grid.RowsAdded += delegate { Renumber(); };
            grid.RowsRemoved += delegate { Renumber(); UpdateTotals(); };
            grid.CellFormatting += Grid_CellFormatting;
            grid.EditingControlShowing += Grid_EditingControlShowing;
            grid.KeyDown += Grid_KeyDown;
            grid.KeyPress += delegate(object s, KeyPressEventArgs e)
            {
                // typing on a read-only code / name cell (beginner mode) goes to the search box
                if (grid.ReadOnly || grid.IsCurrentCellInEditMode || char.IsControl(e.KeyChar) || grid.CurrentCell == null) return;
                if (grid.CurrentCell.ReadOnly) { e.Handled = true; entry.FocusSearch(e.KeyChar.ToString()); }
            };
            grid.DataError += delegate(object s, DataGridViewDataErrorEventArgs e) { e.ThrowException = false; };

            dtp.ValueChanged += delegate { MarkDirty(); };
            person.Changed += delegate { MarkDirty(); };
            project.Changed += delegate { MarkDirty(); RefreshAvail(); };
            txtNote.TextChanged += delegate { MarkDirty(); };
            cmbRType.SelectedIndexChanged += delegate { MarkDirty(); };
            cmbStore.SelectedIndexChanged += delegate { MarkDirty(); snap = null; RefreshAvail(); };
            cmbStore2.SelectedIndexChanged += delegate { MarkDirty(); };

            btnSave.Click += delegate { Save(); };
            btnNew.Click += delegate { if (CanLeave()) NewDoc(); };
            btnDelete.Click += delegate { DeleteDoc(); };
            btnPrint.Click += delegate { Print(); };
            btnPrev.Click += delegate { Navigate(-1); };
            btnNext.Click += delegate { Navigate(1); };
            btnFind.Click += delegate { FindByNo(); };
        }

        void AddCol(string name, string header, int width, bool ro, bool num)
        {
            DataGridViewTextBoxColumn c = UI.Col(name, header, null, width, num);
            c.ReadOnly = ro;
            if (ro) c.DefaultCellStyle.BackColor = Theme.RowAlt;
            grid.Columns.Add(c);
        }

        // ------------------------------------------------------------ state
        void MarkDirty()
        {
            if (loading) return;
            if (!dirty) { dirty = true; lblMsg.Text = "● تغييرات غير محفوظة"; lblMsg.ForeColor = Theme.Warning; }
        }

        void SetClean(string msg, Color color) { dirty = false; lblMsg.Text = msg ?? ""; lblMsg.ForeColor = color; }

        void NewDoc()
        {
            Doc d = new Doc();
            d.Kind = kind;
            d.Store = Cache.DefaultStore;
            d.Project = presetProject;
            Show(d);
            if (kind == DocKind.Transfer) cmbStore.Focus();
            else if (UsesProject && d.Project == 0) project.FocusText();
            else if (UsesPerson) person.FocusText();
            else entry.FocusSearch(null);
        }

        void Show(Doc d)
        {
            loading = true;
            // the requisition link belongs only to the issue made from it: a new or another document drops it
            if (d.Id == 0 || d.Id != mreqIssue) { mreqNo = 0; mreqIssue = 0; }
            doc = d;
            origQty.Clear();
            origStore = d.Store;
            foreach (DocLine l in d.Lines)
            {
                double q; origQty.TryGetValue(l.ItemNo, out q);
                origQty[l.ItemNo] = q + l.Qty;
            }
            lblTitle.Text = Posting.KindName(d.Kind);
            lblTitle.ForeColor = KindColor;
            if (d.Id == 0)
            {
                txtNo.Text = "جديد";
                try { txtNo.Text = Posting.NextNo(kind, null); } catch { }
                lblState.Text = "سند جديد • الرقم يُثبّت عند الحفظ";
            }
            else
            {
                txtNo.Text = d.No;
                lblState.Text = "رقم " + d.No + "  •  " + UI.Date(d.Date) + (kind == DocKind.Reserve && d.Status == 0 ? "  •  ملغى" : "");
            }
            dtp.Value = d.Date < dtp.MinDate ? DateTime.Today : d.Date;
            person.Value = d.Person;
            project.Value = d.Project;
            UI.SetSel(cmbStore, d.Store);
            if (kind == DocKind.Transfer) { if (d.Store2 > 0) UI.SetSel(cmbStore2, d.Store2); else cmbStore2.SelectedIndex = -1; }
            UI.SetSel(cmbRType, d.RType == 1 ? 1 : 2);
            txtNote.Text = d.Note;

            grid.Rows.Clear();
            bool anyUsed = false;
            foreach (DocLine l in d.Lines)
            {
                int i = grid.Rows.Add();
                DataGridViewRow r = grid.Rows[i];
                r.Cells["itemNo"].Value = l.ItemNo;
                r.Cells["lineId"].Value = l.LineId;
                r.Cells["code"].Value = Cache.ItemCode(l.ItemNo);
                r.Cells["name"].Value = Cache.ItemName(l.ItemNo);
                r.Cells["qty"].Value = l.Qty;
                r.Cells["price"].Value = l.Price == 0 ? null : (object)l.Price;
                r.Cells["used"].Value = l.Used == 0 ? null : (object)l.Used;
                if (l.Used > 0) anyUsed = true;
            }
            grid.Columns["used"].Visible = anyUsed && Posting.IsShop(kind);
            Renumber();
            RefreshAvail();
            UpdateTotals();

            bool cancelled = kind == DocKind.Reserve && d.Status == 0;
            bool closedYear = d.Id > 0 && PeriodLock.IsLocked(d.Date);   // closed year or locked month
            string adj = StockCount.AdjustmentText(d);                    // count adjustment / posted write-off: never edited here
            bool canEdit = !cancelled && !closedYear && adj == null && (d.Id == 0 ? Session.Can(PermAdd) : Session.Can(PermEdit));
            grid.ReadOnly = !canEdit;
            grid.Columns["del"].Visible = canEdit;
            grid.AllowUserToAddRows = canEdit && Adv;
            if (canEdit)
            {
                grid.Columns["no"].ReadOnly = grid.Columns["avail"].ReadOnly = grid.Columns["used"].ReadOnly = grid.Columns["resv"].ReadOnly = grid.Columns["unit"].ReadOnly = true;
                if (!Adv) { grid.Columns["code"].ReadOnly = true; grid.Columns["name"].ReadOnly = true; }
            }
            btnSave.Enabled = entry.Enabled = canEdit;
            entry.Reset();
            snap = null;                                  // balances may have changed (saved, other users): read again once
            if (canEdit && IsHandleCreated) BeginInvoke((Action)delegate { try { EnsureSnap(); } catch { } });
            btnDelete.Enabled = d.Id > 0 && !cancelled && !closedYear && adj == null && Session.Can(PermDelete);
            btnPrint.Enabled = d.Id > 0;
            dtp.Enabled = person.Enabled = project.Enabled = cmbStore.Enabled = cmbStore2.Enabled = cmbRType.Enabled = txtNote.Enabled = canEdit;
            loading = false;
            SetClean(cancelled ? "هذا الحجز ملغى" : closedYear ? PeriodLock.ShortText(d.Date) + " • هذا السند للعرض والطباعة فقط" :
                     adj != null ? adj + " • هذا السند للعرض والطباعة فقط" : d.Id == 0 ? "" : (canEdit ? "" : "عرض فقط • لا توجد صلاحية تعديل"),
                     cancelled || closedYear || adj != null ? Theme.Danger : Theme.Muted);
        }

        // ------------------------------------------------------------ grid helpers
        void Renumber()
        {
            int n = 1;
            foreach (DataGridViewRow r in grid.Rows) { if (r.IsNewRow) continue; r.Cells["no"].Value = n++; }
        }

        int ItemOf(DataGridViewRow r) { return r == null ? 0 : Db.I(r.Cells["itemNo"].Value); }

        /// <summary>Quantity that can be used for this document: stock in the store minus what is reserved for other projects.</summary>
        double Available(int item)
        {
            int st = UI.Sel(cmbStore);
            if (item == 0 || st == 0) return 0;
            double s = Cache.Stock(item, st);
            if (doc != null && doc.Id > 0 && st == origStore && kind != DocKind.Reserve)
            {
                double q; origQty.TryGetValue(item, out q);
                if (OutDoc) s += q;     // our own saved quantity is already deducted
            }
            if (OutDoc && Db.HasProj) s -= Reservations.ForOthers(item, st, kind == DocKind.Issue || kind == DocKind.Reserve ? project.Value : -1);
            return s;
        }

        void RefreshAvail()
        {
            if (grid == null) return;
            bool was = loading; loading = true;
            Dictionary<int, double> cache = new Dictionary<int, double>();
            bool showResv = grid.Columns["resv"].Visible;
            int st = UI.Sel(cmbStore);
            foreach (DataGridViewRow r in grid.Rows)
            {
                int it = ItemOf(r);
                if (it == 0) { if (!r.IsNewRow) { r.Cells["avail"].Value = null; r.Cells["resv"].Value = null; } continue; }
                double a;
                if (!cache.TryGetValue(it, out a)) { a = Available(it); cache[it] = a; }
                r.Cells["avail"].Value = a;
                if (showResv) { double rv = Reservations.ForProject(it, st, project.Value); r.Cells["resv"].Value = rv == 0 ? null : (object)rv; }
            }
            loading = was;
            grid.Invalidate();
        }

        void UpdateTotals()
        {
            int n = 0; double q = 0;
            foreach (DataGridViewRow r in grid.Rows)
            {
                if (r.IsNewRow || ItemOf(r) == 0) continue;
                n++;
                double v; if (UI.TryNum(Db.S(r.Cells["qty"].Value), out v)) q += v;
            }
            lblTotals.Text = "عدد المواد: " + n + "     مجموع الكميات: " + UI.Num(q);
        }

        int SetItem(int rowIndex, int item)
        {
            if (rowIndex < 0 || rowIndex >= grid.Rows.Count || grid.Rows[rowIndex].IsNewRow) rowIndex = grid.Rows.Add();
            DataGridViewRow r = grid.Rows[rowIndex];
            int old = ItemOf(r);
            if (old != item && Db.D(r.Cells["used"].Value) > 0)
            {
                UI.Warn("لا يمكن تغيير هذه المادة لأنه أُخرج منها.");
                r.Cells["code"].Value = Cache.ItemCode(old); r.Cells["name"].Value = Cache.ItemName(old);
                return rowIndex;
            }
            r.Cells["itemNo"].Value = item;
            r.Cells["code"].Value = Cache.ItemCode(item);
            r.Cells["name"].Value = Cache.ItemName(item);
            r.Cells["avail"].Value = Available(item);
            if (grid.Columns["resv"].Visible) { double rv = Reservations.ForProject(item, UI.Sel(cmbStore), project.Value); r.Cells["resv"].Value = rv == 0 ? null : (object)rv; }
            DataRow ir = Cache.Item(item);
            if (ir != null && Db.B(ir["freez"])) Msg("تنبيه: هذه المادة مجمدة", Theme.Warning);
            foreach (DataGridViewRow o in grid.Rows)
            {
                if (o.Index != rowIndex && !o.IsNewRow && ItemOf(o) == item)
                {
                    Msg("تنبيه: المادة مكررة في السطر " + o.Cells["no"].Value, Theme.Warning);
                    break;
                }
            }
            Renumber();
            MarkDirty();
            UpdateTotals();
            return rowIndex;
        }

        void Msg(string text, Color c) { lblMsg.Text = text; lblMsg.ForeColor = c; }

        void GoTo(int row, string col)
        {
            if (row < 0) return;
            if (row >= grid.Rows.Count) row = grid.Rows.Count - 1;
            try { grid.CurrentCell = grid.Rows[row].Cells[col]; } catch { }
        }

        // ------------------------------------------------------------ item entry bar
        StockSnapshot EnsureSnap()
        {
            int st = UI.Sel(cmbStore);
            if (snap == null || snap.Store != st) snap = new StockSnapshot(st);
            return snap;
        }

        /// <summary>Same rule as Available() but from the in-memory snapshot (used on every keystroke).</summary>
        double FastAvailable(int item)
        {
            int st = UI.Sel(cmbStore);
            if (item == 0 || st == 0) return 0;
            double s = EnsureSnap().Stock(item);
            if (doc != null && doc.Id > 0 && st == origStore && kind != DocKind.Reserve)
            {
                double q; origQty.TryGetValue(item, out q);
                if (OutDoc) s += q;
            }
            if (OutDoc && Db.HasProj) s -= snap.ForOthers(item, kind == DocKind.Issue || kind == DocKind.Reserve ? project.Value : -1);
            return s;
        }

        double QtyInDoc(int item)
        {
            double s = 0;
            foreach (DataGridViewRow r in grid.Rows)
            {
                if (r.IsNewRow || ItemOf(r) != item) continue;
                double v; if (UI.TryNum(Db.S(r.Cells["qty"].Value), out v)) s += v;
            }
            return s;
        }

        int RowOf(int item)
        {
            foreach (DataGridViewRow r in grid.Rows) if (!r.IsNewRow && ItemOf(r) == item) return r.Index;
            return -1;
        }

        /// <summary>Last used items of this document kind (one query per screen), items added here first.</summary>
        List<int> RecentItems()
        {
            if (recent == null)
            {
                recent = new List<int>();
                string sql;
                switch (kind)
                {
                    case DocKind.Issue: sql = "SELECT TOP 400 l.item_no FROM sale_menu AS l INNER JOIN sale AS h ON h.shop_no = l.seq WHERE " + Db.IssueOnly("h") + " ORDER BY l.auto_seq DESC"; break;
                    case DocKind.PurchaseReturn: sql = Db.HasProj ? "SELECT TOP 400 l.item_no FROM sale_menu AS l INNER JOIN sale AS h ON h.shop_no = l.seq WHERE h.doc_kind = 1 ORDER BY l.auto_seq DESC" : null; break;
                    case DocKind.Receipt: sql = "SELECT TOP 400 l.item_no FROM shop_menu AS l INNER JOIN shop AS h ON h.shop_no = l.seq WHERE h.shop_type <> 3 AND " + Db.NotCountAdj("h") + " ORDER BY l.auto_seq DESC"; break;
                    case DocKind.Return: sql = "SELECT TOP 400 l.item_no FROM shop_menu AS l INNER JOIN shop AS h ON h.shop_no = l.seq WHERE h.shop_type = 3 ORDER BY l.auto_seq DESC"; break;
                    case DocKind.Transfer: sql = "SELECT TOP 400 l.item_no FROM trans_menu AS l INNER JOIN trans AS h ON h.shop_no = l.seq ORDER BY h.shop_no DESC"; break;
                    default: sql = Db.HasProj ? "SELECT TOP 400 l.item_no FROM reserve_menu AS l INNER JOIN reserve AS h ON h.res_no = l.seq ORDER BY l.id DESC" : null; break;
                }
                try { if (sql != null) foreach (DataRow r in Db.Query(sql).Rows) { int id = Db.I(r[0]); if (!recent.Contains(id)) recent.Add(id); } }
                catch { }
            }
            List<int> res = new List<int>();
            foreach (int id in recentAdded) if (!res.Contains(id)) res.Add(id);
            foreach (int id in recent) if (!res.Contains(id)) res.Add(id);
            List<int> ok = new List<int>();
            foreach (int id in res)
            {
                DataRow ir = Cache.Item(id);
                if (ir == null || Db.B(ir["freez"])) continue;
                ok.Add(id);
                if (ok.Count >= 10) break;
            }
            return ok;
        }

        /// <summary>Adds the item from the entry bar; an item already in the document gets its quantity increased.</summary>
        bool AddFromBar(int item, double qty, double price)
        {
            if (grid.ReadOnly || item == 0 || qty <= 0) return false;
            if (grid.IsCurrentCellInEditMode) grid.EndEdit();
            int row = RowOf(item);
            bool merged = row >= 0;
            if (merged)
            {
                DataGridViewRow r = grid.Rows[row];
                double old; UI.TryNum(Db.S(r.Cells["qty"].Value), out old);
                r.Cells["qty"].Value = old + qty;
                if (price > 0) r.Cells["price"].Value = price;
            }
            else
            {
                // reuse an empty current row, otherwise add a new line
                int cur = grid.CurrentCell == null ? -1 : grid.CurrentCell.RowIndex;
                if (cur >= 0 && cur < grid.Rows.Count && !grid.Rows[cur].IsNewRow && ItemOf(grid.Rows[cur]) == 0 && Db.S(grid.Rows[cur].Cells["qty"].Value) == "") row = cur;
                row = SetItem(row, item);
                grid.Rows[row].Cells["qty"].Value = qty;
                if (price > 0) grid.Rows[row].Cells["price"].Value = price;
            }
            MarkDirty();
            UpdateTotals();
            recentAdded.Remove(item); recentAdded.Insert(0, item);

            // show the line: scroll to it and flash it
            try
            {
                if (!grid.Rows[row].Displayed) grid.FirstDisplayedScrollingRowIndex = row;
                grid.CurrentCell = grid.Rows[row].Cells["qty"];
            }
            catch { }
            flashItem = item; flashTimer.Stop(); flashTimer.Start(); grid.Invalidate();

            double total = QtyInDoc(item);
            string unit = Cache.ItemUnit(item); unit = unit.Length > 0 ? " " + unit : "";
            string text = merged ? "✓ زيدت الكمية: " + Cache.ItemName(item) + " • أصبحت " + UI.Num(total) + unit + " (+" + UI.Num(qty) + ")"
                                 : "✓ أُضيفت: " + Cache.ItemName(item) + " • " + UI.Num(qty) + unit;
            Color c = Theme.Success;
            if (OutDoc)
            {
                double a = Db.D(grid.Rows[row].Cells["avail"].Value);
                if (total > a + 1e-9)
                {
                    text += "   •   ⚠ أكبر من المتوفر (" + UI.Num(a) + unit + ")" + (kind == DocKind.Reserve ? "" : "، قد يصبح الرصيد سالبا");
                    c = Theme.Danger;
                }
            }
            entry.Status(text, c);
            if (c == Theme.Danger) Msg("⚠ " + Cache.ItemName(item) + ": الكمية أكبر من المتوفر", Theme.Danger);
            return true;
        }

        // ------------------------------------------------------------ grid events
        void Grid_EditingControlShowing(object sender, DataGridViewEditingControlShowingEventArgs e)
        {
            TextBox tb = e.Control as TextBox;
            if (tb == null) return;
            string col = grid.CurrentCell.OwningColumn.Name;
            if (col == "name")
            {
                tb.AutoCompleteMode = AutoCompleteMode.SuggestAppend;
                tb.AutoCompleteSource = AutoCompleteSource.CustomSource;
                tb.AutoCompleteCustomSource = Cache.ItemNames;
            }
            else tb.AutoCompleteMode = AutoCompleteMode.None;
            tb.RightToLeft = (col == "code" || col == "qty" || col == "price") ? RightToLeft.No : RightToLeft.Yes;
        }

        void Grid_CellEndEdit(object sender, DataGridViewCellEventArgs e)
        {
            if (loading || e.RowIndex < 0) return;
            string col = grid.Columns[e.ColumnIndex].Name;
            DataGridViewRow r = grid.Rows[e.RowIndex];
            if (col == "code" || col == "name")
            {
                string text = Db.S(r.Cells[col].Value).Trim();
                int cur = ItemOf(r);
                if (text.Length == 0)
                {
                    if (cur != 0) { r.Cells["code"].Value = Cache.ItemCode(cur); r.Cells["name"].Value = Cache.ItemName(cur); }
                    return;
                }
                if (cur != 0 && (col == "code" ? Cache.ItemCode(cur) : Cache.ItemName(cur)) == text) return;
                int id = col == "code" ? Cache.FindItemByCode(text) : Cache.FindItemByName(text);
                if (id == 0 && col == "code") id = Cache.FindItemByName(text);
                if (id != 0) { SetItem(e.RowIndex, id); return; }
                // not an exact code / name: search on the same page with the entry bar (the cell goes back to its item)
                int row = e.RowIndex;
                BeginInvoke((Action)delegate
                {
                    if (row < grid.Rows.Count)
                    {
                        DataGridViewRow rr = grid.Rows[row];
                        int c2 = ItemOf(rr);
                        rr.Cells["code"].Value = c2 == 0 ? null : Cache.ItemCode(c2);
                        rr.Cells["name"].Value = c2 == 0 ? null : Cache.ItemName(c2);
                    }
                    entry.FocusSearch(text);
                });
            }
            else if (col == "qty" || col == "price")
            {
                string t = Db.S(r.Cells[col].Value).Trim();
                if (t.Length == 0) return;
                double v;
                if (UI.TryNum(t, out v)) r.Cells[col].Value = v;
                else { Msg("قيمة غير صحيحة: " + t, Theme.Danger); r.Cells[col].Value = null; }
                UpdateTotals();
            }
        }

        void Grid_CellFormatting(object sender, DataGridViewCellFormattingEventArgs e)
        {
            if (e.RowIndex >= 0 && grid.Columns[e.ColumnIndex].Name == "unit")
            {
                e.Value = Cache.ItemUnit(ItemOf(grid.Rows[e.RowIndex])); e.FormattingApplied = true;   // shown from the item, never stored
                return;
            }
            if (e.RowIndex < 0) return;
            DataGridViewRow r = grid.Rows[e.RowIndex];
            if (r.IsNewRow || ItemOf(r) == 0) return;
            if (OutDoc)
            {
                double q, a = Db.D(r.Cells["avail"].Value);
                if (UI.TryNum(Db.S(r.Cells["qty"].Value), out q) && q > a + 1e-9)
                {
                    e.CellStyle.BackColor = Theme.RowWarn;
                    if (grid.Columns[e.ColumnIndex].Name == "avail") e.CellStyle.ForeColor = Theme.Danger;
                }
            }
            if (flashItem != 0 && ItemOf(r) == flashItem) e.CellStyle.BackColor = Theme.Flash;   // line just added / increased from the entry bar
        }

        void OnEnter()
        {
            if (grid.CurrentCell == null) return;
            int row = grid.CurrentCell.RowIndex;
            string col = grid.CurrentCell.OwningColumn.Name;
            if (grid.IsCurrentCellInEditMode) grid.EndEdit();
            if (row >= grid.Rows.Count) return;
            DataGridViewRow r = grid.Rows[row];
            if (col == "code") { GoTo(row, ItemOf(r) != 0 ? "qty" : "name"); return; }
            if (col == "name") { if (ItemOf(r) != 0) GoTo(row, "qty"); return; }
            if (col == "qty" && HasPrice) { GoTo(row, "price"); return; }
            if (col == "qty" || col == "price")
            {
                if (ItemOf(r) == 0) return;
                if (!Adv) { entry.FocusSearch(null); return; }
                int next = row + 1;
                if (next >= grid.Rows.Count) return;
                GoTo(next, "code");
                return;
            }
            GoTo(row + 1 < grid.Rows.Count ? row + 1 : row, "code");
        }

        void Grid_KeyDown(object sender, KeyEventArgs e)
        {
            if (grid.ReadOnly) return;
            if (e.KeyCode == Keys.Delete && !grid.IsCurrentCellInEditMode) { DeleteCurrentRow(); e.Handled = true; }
        }

        void DeleteCurrentRow()
        {
            if (grid.CurrentCell == null) return;
            DataGridViewRow r = grid.Rows[grid.CurrentCell.RowIndex];
            if (r.IsNewRow) return;
            if (Db.D(r.Cells["used"].Value) > 0) { UI.Warn("لا يمكن حذف هذه المادة لأنه أُخرج منها."); return; }
            if (ItemOf(r) != 0 && !UI.Confirm("حذف السطر: " + Db.S(r.Cells["name"].Value) + " ؟")) return;
            grid.Rows.Remove(r);
            MarkDirty();
        }

        // ------------------------------------------------------------ commands
        Doc Collect()
        {
            Doc d = new Doc();
            d.Kind = kind; d.Id = doc.Id; d.No = doc.No; d.Fingerprint = doc.Fingerprint; d.Status = doc.Status;
            d.Date = dtp.Value.Date;
            d.Person = UsesPerson ? person.Value : 0;
            d.Project = UsesProject ? project.Value : 0;
            d.Store = UI.Sel(cmbStore);
            d.Store2 = kind == DocKind.Transfer ? UI.Sel(cmbStore2) : 0;
            d.RType = UI.Sel(cmbRType) == 1 ? 1 : 2;
            d.Note = txtNote.Text.Trim();
            foreach (DataGridViewRow r in grid.Rows)
            {
                if (r.IsNewRow) continue;
                int it = ItemOf(r);
                string q = Db.S(r.Cells["qty"].Value);
                if (it == 0)
                {
                    if (Db.S(r.Cells["code"].Value) + Db.S(r.Cells["name"].Value) + q == "") continue;
                    GoTo(r.Index, "name");
                    throw new UserError("السطر " + r.Cells["no"].Value + ": لم يتم تحديد المادة.");
                }
                double qty;
                if (!UI.TryNum(q, out qty) || qty <= 0)
                {
                    GoTo(r.Index, "qty");
                    throw new UserError("السطر " + r.Cells["no"].Value + ": أدخل كمية صحيحة للمادة " + Cache.ItemName(it));
                }
                DocLine l = new DocLine();
                l.ItemNo = it; l.Qty = qty; l.LineId = Db.I(r.Cells["lineId"].Value);
                double p; l.Price = UI.TryNum(Db.S(r.Cells["price"].Value), out p) ? p : 0;
                l.Used = Db.D(r.Cells["used"].Value);
                d.Lines.Add(l);
            }
            return d;
        }

        bool Save()
        {
            if (!Session.Demand(doc.Id == 0 ? PermAdd : PermEdit)) return false;
            if (grid.IsCurrentCellInEditMode) grid.EndEdit();
            Validate();
            Doc d;
            try { d = Collect(); }
            catch (UserError ue) { UI.Warn(ue.Message); return false; }

            if (OutDoc)
            {
                List<string> shortList = new List<string>();
                Dictionary<int, double> need = new Dictionary<int, double>();
                foreach (DocLine l in d.Lines) { double q; need.TryGetValue(l.ItemNo, out q); need[l.ItemNo] = q + l.Qty; }
                foreach (KeyValuePair<int, double> kv in need)
                {
                    double a = Available(kv.Key);
                    if (kv.Value > a + 1e-9)
                    {
                        double held = Db.HasProj ? Reservations.ForOthers(kv.Key, d.Store, kind == DocKind.Issue || kind == DocKind.Reserve ? d.Project : -1) : 0;
                        shortList.Add("• " + Cache.ItemName(kv.Key) + " : المطلوب " + UI.Num(kv.Value) + " والمتوفر " + UI.Num(a) + (held > 0 ? " (محجوز لمشاريع أخرى " + UI.Num(held) + ")" : ""));
                    }
                }
                string what = kind == DocKind.Reserve ? "الحجز" : "الحفظ";
                if (shortList.Count > 0 && !UI.Confirm("الكميات التالية أكبر من المتوفر في المخزن:\n\n" +
                    string.Join("\n", shortList.ToArray()) + "\n\nهل تريد " + what + " رغم ذلك؟" + (kind == DocKind.Reserve ? "" : " (قد يصبح الرصيد سالبا)"))) return false;
            }
            if (d.Date > DateTime.Today && !UI.Confirm("تاريخ السند (" + UI.Date(d.Date) + ") في المستقبل. هل هو صحيح؟")) return false;
            if (!Adv)
            {
                double tq = 0; foreach (DocLine l in d.Lines) tq += l.Qty;
                List<string> s = new List<string>();
                s.Add("نوع السند: " + Posting.KindName(kind));
                if (d.Project > 0) s.Add((CcRecipient ? (kind == DocKind.Return ? "مركز الكلفة (المُعاد منه): " : "مركز الكلفة (المستلم): ") : "مركز الكلفة: ") + Cache.Project(d.Project));
                if (d.Person > 0) s.Add((kind == DocKind.Issue ? "المستلم: " : "الجهة: ") + Cache.Person(d.Person));
                s.Add((OutDoc ? "من مخزن: " : "إلى مخزن: ") + Cache.Store(d.Store) + (d.Store2 > 0 ? "  ←  إلى مخزن: " + Cache.Store(d.Store2) : ""));
                s.Add("عدد المواد: " + d.Lines.Count + "   •   مجموع الكميات: " + UI.Num(tq));
                if (!UI.Confirm("تأكيد قبل الحفظ:\n\n" + string.Join("\n", s.ToArray()) + "\n\nهل المعلومات صحيحة؟")) return false;
            }

            try
            {
                UI.WaitCursor(true);
                Posting.Save(d);
                if (mreqNo > 0) mreqIssue = Requisitions.IssueSaved(mreqNo, mreqIssue, doc.Id, d);   // «صرف الطلب»: update the request
                foreach (DocLine l in d.Lines)
                {
                    DataRow ir = Cache.Item(l.ItemNo);
                    if (ir != null) ir["bal"] = Cache.StockAll(l.ItemNo);
                }
                Show(d);
                SetClean("✓ تم الحفظ • رقم السند " + d.No, Theme.Success);
                if (kind == DocKind.Receipt) PurchaseRequests.AfterReceiptSave(FindForm(), d);   // optional link to a purchase request
                return true;
            }
            catch (UserError ue) { UI.Warn(ue.Message); return false; }
            catch (Exception ex) { UI.Error(ex); return false; }
            finally { UI.WaitCursor(false); }
        }

        void DeleteDoc()
        {
            if (doc.Id == 0 || !Session.Demand(PermDelete)) return;
            string q = kind == DocKind.Reserve
                ? "هل تريد إلغاء الحجز رقم " + doc.No + "؟\nستصبح المواد متاحة لكل مراكز الكلفة (يبقى السند محفوظا كملغى).\nلإلغاء جزء من الحجز: عدّل الكميات أو احذف السطور ثم احفظ."
                : "هل تريد حذف " + Posting.KindName(kind) + " رقم " + doc.No + " نهائيا؟\nسيتم تعديل أرصدة المواد تلقائيا.";
            if (!UI.Confirm(q)) return;
            try
            {
                UI.WaitCursor(true);
                Posting.Delete(doc);
                foreach (DocLine l in doc.Lines) { DataRow ir = Cache.Item(l.ItemNo); if (ir != null) ir["bal"] = Cache.StockAll(l.ItemNo); }
                string no = doc.No;
                if (kind == DocKind.Reserve) { Doc d = Posting.Load(kind, doc.Id); if (d != null) Show(d); SetClean("✓ تم إلغاء الحجز رقم " + no, Theme.Danger); }
                else { NewDoc(); SetClean("✓ تم حذف السند رقم " + no, Theme.Danger); }
            }
            catch (UserError ue) { UI.Warn(ue.Message); }
            catch (Exception ex) { UI.Error(ex); }
            finally { UI.WaitCursor(false); }
        }

        void Print()
        {
            if (doc.Id == 0) return;
            if (dirty) { if (!UI.Confirm("يجب حفظ السند قبل الطباعة. حفظ الآن؟") || !Save()) return; }
            ReportViewerForm.ShowVoucher(FindForm(), doc);
        }

        void Navigate(int dir)
        {
            if (!CanLeave()) return;
            int id = Posting.Neighbor(kind, doc.Id, dir);
            if (id == 0) { Msg(dir < 0 ? "هذا أول سند" : "هذا آخر سند", Theme.Muted); return; }
            Doc d = Posting.Load(kind, id);
            if (d != null) { kind = d.Kind; Show(d); }
        }

        void FindByNo()
        {
            if (!CanLeave()) return;
            string no = InputDialog.Ask(FindForm(), "فتح سند", "رقم " + Posting.KindName(kind) + ":", "");
            if (string.IsNullOrEmpty(no)) return;
            int id = Posting.FindByNo(kind, UI.NormDigits(no));
            if (id == 0) { UI.Warn("لا يوجد سند بهذا الرقم."); return; }
            Doc d = Posting.Load(kind, id);
            if (d != null) { kind = d.Kind; Show(d); }
        }

        // ------------------------------------------------------------ material requisition («صرف الطلب»)
        int mreqNo, mreqIssue;

        /// <summary>A new issue pre-filled from an approved material requisition: cost centre, store and the remaining
        /// quantities (added through the entry bar code). Saving it updates the request (Requisitions.IssueSaved).</summary>
        public void Prefill(int proj, int store, List<KeyValuePair<int, double>> lines, int mreq)
        {
            Action fill = delegate
            {
                presetProject = proj;
                NewDoc();
                mreqNo = mreq; mreqIssue = 0;   // after NewDoc, whose Show(new) clears the link
                UI.SetSel(cmbStore, store); project.Value = proj;
                foreach (KeyValuePair<int, double> l in lines) AddFromBar(l.Key, l.Value, 0);
                MreqDoc m = Requisitions.Load(mreq);
                if (m != null && txtNote.Text.Length == 0) txtNote.Text = "طلب صرف رقم " + m.MenuNo;
                MarkDirty();
                Msg("من طلب الصرف رقم " + (m == null ? "" : m.MenuNo), Theme.Primary);
                if (UsesPerson) person.FocusText();
            };
            if (Created) fill(); else Load += delegate { fill(); };
        }

        public bool CanLeave()
        {
            if (!dirty) return true;
            DialogResult r = UI.YesNoCancel("يوجد تغييرات غير محفوظة في السند.\nهل تريد حفظها؟");
            if (r == DialogResult.Yes) return Save();
            return r == DialogResult.No;
        }

        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            switch (keyData)
            {
                case Keys.Control | Keys.S: Save(); return true;
                case Keys.Control | Keys.N: if (CanLeave()) NewDoc(); return true;
                case Keys.Control | Keys.P: Print(); return true;
                case Keys.PageUp: if (Adv && !grid.IsCurrentCellInEditMode && !entry.ContainsFocus) { Navigate(-1); return true; } break;
                case Keys.PageDown: if (Adv && !grid.IsCurrentCellInEditMode && !entry.ContainsFocus) { Navigate(1); return true; } break;
                case Keys.F2:
                    if (!grid.ReadOnly)
                    {
                        // F2 = the search box of the entry bar (text being typed in a grid cell moves there)
                        string text = "";
                        if (grid.IsCurrentCellInEditMode && grid.EditingControl != null) { text = grid.EditingControl.Text; grid.CancelEdit(); grid.EndEdit(); }
                        entry.FocusSearch(text);
                        return true;
                    }
                    break;
            }
            return base.ProcessCmdKey(ref msg, keyData);
        }
    }
}
