using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;
using System;

namespace Stores
{
    /// <summary>One purchase request «طلب شراء»: head (date, store, supplier, notes), the items with the requested quantity,
    /// what was received from the linked receipts and what remains; save, print, send, cancel.</summary>
    class PurchaseRequestDialog : BaseDialog
    {
        PReq req;
        DateTimePicker dtp; ComboBox cmbStore; PersonPicker sup; TextBox txtNote, txtQty; ItemPicker pick;
        Label lblNo, lblStatus, lblInfo;
        DataGridView grid;
        Button save, print, send, cancelReq, addBtn;
        bool dirty, loading;
        public bool Changed;

        public PurchaseRequestDialog(int id)
        {
            req = id > 0 ? PurchaseRequests.Load(id) : null;
            if (req == null) { req = new PReq(); req.Store = Cache.DefaultStore; }
            Text = "طلب شراء";
            ClientSize = new Size(980, 640);
            MinimumSize = new Size(820, 520);
            FormBorderStyle = FormBorderStyle.Sizable; MaximizeBox = true;

            // ---- head
            Panel head = UI.Card(); head.Dock = DockStyle.Top; head.Padding = new Padding(10, 6, 10, 6);
            FlowLayoutPanel f1 = UI.Flow(), f2 = UI.Flow();
            lblNo = new Label(); lblNo.AutoSize = true; lblNo.Font = Theme.FontSubtitle; lblNo.ForeColor = Theme.Primary; lblNo.Margin = new Padding(6, 8, 16, 0);
            dtp = UI.DatePicker(req.Date);
            cmbStore = UI.Combo(170); cmbStore.BindingContext = new BindingContext(); UI.Fill(cmbStore, Cache.Stores, "stor_no", "stor_name", "غير محدد");
            sup = new PersonPicker(260);
            lblStatus = new Label(); lblStatus.AutoSize = true; lblStatus.Font = Theme.FontBold; lblStatus.Margin = new Padding(16, 10, 6, 0);
            f1.Controls.AddRange(new Control[] { lblNo, UI.Field("التاريخ", dtp), UI.Field("المخزن", cmbStore), UI.Field("المجهز (اختياري)", sup), lblStatus });
            txtNote = UI.Text(560); txtNote.MaxLength = 255;
            f2.Controls.Add(UI.Field("ملاحظات", txtNote));
            head.Controls.Add(f2); head.Controls.Add(f1); UI.FitHeight(head);

            // ---- add an item
            Panel addP = UI.Card(); addP.Dock = DockStyle.Top; addP.Padding = new Padding(10, 4, 10, 4);
            FlowLayoutPanel fa = UI.Flow();
            pick = new ItemPicker(400);
            txtQty = UI.Text(90); txtQty.RightToLeft = RightToLeft.No; txtQty.TextAlign = HorizontalAlignment.Center;
            addBtn = UI.Btn("+ إضافة", Theme.Accent, 100);
            Label hint = UI.Lbl("اكتب جزءا من اسم المادة أو رمزها ثم الكمية و Enter");
            fa.Controls.AddRange(new Control[] { UI.Field("المادة", pick), UI.Field("الكمية", txtQty), addBtn, hint });
            addP.Controls.Add(fa); UI.FitHeight(addP);

            // ---- lines
            grid = new DataGridView(); grid.Dock = DockStyle.Fill; UI.StyleGrid(grid);
            grid.AllowUserToAddRows = false; grid.AllowUserToDeleteRows = false; grid.SelectionMode = DataGridViewSelectionMode.CellSelect; grid.MultiSelect = false;
            grid.Columns.Add(UI.Col("lid", "", null, 10, false)); grid.Columns["lid"].Visible = false;
            grid.Columns.Add(UI.Col("item", "", null, 10, false)); grid.Columns["item"].Visible = false;
            grid.Columns.Add(UI.Col("code", "الرمز", null, 110, false));
            grid.Columns.Add(UI.Col("name", "اسم المادة", null, 0, false));
            grid.Columns.Add(UI.Col("unit", "الوحدة", null, 80, false));
            grid.Columns.Add(UI.Col("bal", "الرصيد الحالي", null, 110, true));
            grid.Columns.Add(UI.Col("qty", "الكمية المطلوبة", null, 120, true));
            grid.Columns.Add(UI.Col("rec", "المستلم", null, 90, true));
            grid.Columns.Add(UI.Col("rem", "المتبقي", null, 90, true));
            DataGridViewButtonColumn del = new DataGridViewButtonColumn(); del.Name = "del"; del.HeaderText = ""; del.Text = "حذف"; del.UseColumnTextForButtonValue = true; del.Width = 70;
            grid.Columns.Add(del);
            foreach (DataGridViewColumn c in grid.Columns) if (c.Name != "qty") c.ReadOnly = true;
            grid.Columns["qty"].DefaultCellStyle.BackColor = Color.FromArgb(255, 253, 231);
            Panel gc = UI.Card(); gc.Dock = DockStyle.Fill; gc.Padding = new Padding(1); gc.Controls.Add(grid);
            lblInfo = new Label(); lblInfo.Dock = DockStyle.Bottom; lblInfo.Height = 28; lblInfo.ForeColor = Theme.Muted; lblInfo.Padding = new Padding(10, 6, 10, 0);

            save = UI.Btn("حفظ", Theme.Accent, 100); print = UI.Btn("طباعة", Theme.Primary, 90);
            send = UI.BtnLight("تعليم كمُرسل", 120); cancelReq = UI.BtnLight("إلغاء الطلب", 110);
            Button close = UI.BtnLight("إغلاق", 90);
            Panel body = new Panel(); body.Dock = DockStyle.Fill; body.Padding = new Padding(12, 8, 12, 4);
            Panel s1 = new Panel(); s1.Dock = DockStyle.Top; s1.Height = 6;
            Panel s2 = new Panel(); s2.Dock = DockStyle.Top; s2.Height = 6;
            body.Controls.Add(gc); body.Controls.Add(s2); body.Controls.Add(addP); body.Controls.Add(s1); body.Controls.Add(head);
            Controls.Add(body); Controls.Add(lblInfo); Controls.Add(ButtonBar(save, print, send, cancelReq, close));

            // ---- events
            addBtn.Click += delegate { AddPicked(); };
            txtQty.KeyDown += delegate(object s, KeyEventArgs e) { if (e.KeyCode == Keys.Enter) { e.SuppressKeyPress = true; AddPicked(); } };
            pick.Changed += delegate { if (pick.Value > 0) { if (txtQty.Text.Trim().Length == 0) txtQty.Text = "1"; txtQty.Focus(); txtQty.SelectAll(); } };
            grid.CellContentClick += delegate(object s, DataGridViewCellEventArgs e)
            {
                if (e.RowIndex < 0 || grid.Columns[e.ColumnIndex].Name != "del" || grid.ReadOnly) return;
                RemoveRow(grid.Rows[e.RowIndex]);
            };
            grid.KeyDown += delegate(object s, KeyEventArgs e)
            {
                if (e.KeyCode == Keys.Delete && !grid.IsCurrentCellInEditMode && grid.CurrentRow != null && !grid.ReadOnly) { e.Handled = true; RemoveRow(grid.CurrentRow); }
            };
            grid.CellValueChanged += delegate(object s, DataGridViewCellEventArgs e)
            {
                if (loading || e.RowIndex < 0) return;
                DataGridViewRow r = grid.Rows[e.RowIndex];
                double q; if (UI.TryNum(Db.S(r.Cells["qty"].Value), out q)) r.Cells["rem"].Value = Math.Max(0, q - Db.D(r.Cells["rec"].Value));
                Dirty();
            };
            dtp.ValueChanged += delegate { Dirty(); };
            cmbStore.SelectedIndexChanged += delegate { if (!loading) { RefreshBalances(); Dirty(); } };
            sup.Changed += delegate { Dirty(); };
            txtNote.TextChanged += delegate { Dirty(); };
            save.Click += delegate { Save(); };
            print.Click += delegate { Print(); };
            send.Click += delegate { ChangeStatus(PurchaseRequests.Sent, "تعليم الطلب رقم " + req.No + " كمُرسل إلى المجهز؟"); };
            cancelReq.Click += delegate
            {
                ChangeStatus(PurchaseRequests.Cancelled, "إلغاء طلب الشراء رقم " + req.No + "؟\nيبقى الطلب محفوظا كملغى ولا يُعرض في الطلبات المفتوحة" +
                             (req.Status == PurchaseRequests.Partial ? " (ما استُلم منه يبقى كما هو)." : "."));
            };
            close.Click += delegate { Close(); };
            FormClosing += delegate(object s, FormClosingEventArgs e)
            {
                if (!dirty || grid.ReadOnly) return;
                DialogResult a = UI.YesNoCancel("حفظ التغييرات على طلب الشراء؟");
                if (a == DialogResult.Cancel || (a == DialogResult.Yes && !Save())) e.Cancel = true;
            };
            ShowReq();
        }

        public PReq Request { get { return req; } }

        void Dirty() { if (loading) return; dirty = true; Info("تغييرات غير محفوظة", Theme.Warning); }

        void Info(string text, Color c) { lblInfo.Text = text; lblInfo.ForeColor = c; }

        bool ReadOnlyReq { get { return !PurchaseRequests.CanEdit || req.Status == PurchaseRequests.Done || req.Status == PurchaseRequests.Cancelled; } }

        void ShowReq()
        {
            loading = true;
            lblNo.Text = req.Id == 0 ? "طلب شراء جديد" : "طلب شراء رقم " + req.No;
            dtp.Value = req.Date == DateTime.MinValue ? DateTime.Today : req.Date;
            UI.SetSel(cmbStore, req.Store);
            sup.Value = req.Supplier;
            txtNote.Text = req.Note;
            lblStatus.Text = "الحالة: " + PurchaseRequests.StatusName(req.Status);
            lblStatus.ForeColor = req.Status == PurchaseRequests.Done ? Theme.Success : req.Status == PurchaseRequests.Cancelled ? Theme.Danger : req.Status == PurchaseRequests.Partial ? Theme.Warning : Theme.Primary;
            grid.Rows.Clear();
            foreach (PReqLine l in req.Lines) AddRow(l.LineId, l.ItemNo, l.Qty, l.Received);
            bool ro = ReadOnlyReq;
            grid.ReadOnly = ro; grid.Columns["del"].Visible = !ro;
            foreach (DataGridViewColumn c in grid.Columns) if (c.Name != "qty") c.ReadOnly = true;
            dtp.Enabled = cmbStore.Enabled = sup.Enabled = txtNote.Enabled = pick.Enabled = txtQty.Enabled = addBtn.Enabled = save.Enabled = !ro;
            send.Visible = req.Id > 0 && req.Status == PurchaseRequests.Draft && PurchaseRequests.CanEdit;
            cancelReq.Visible = req.Id > 0 && req.Open && PurchaseRequests.CanEdit;
            loading = false;
            dirty = false;
            List<string> rc = req.Id > 0 ? PurchaseRequests.Receipts(req.Id) : new List<string>();
            Info(!PurchaseRequests.CanEdit ? "للعرض فقط: إنشاء وتعديل طلبات الشراء لمن لديه صلاحية إضافة سندات الإدخال."
                 : rc.Count > 0 ? "سندات الإدخال المرتبطة: " + string.Join("، ", rc.ToArray()) + "   •   المستلم يُحدَّث تلقائيا عند حفظ سند الإدخال"
                 : "طلب الشراء لا يغيّر الأرصدة. عند حفظ «إدخال مخزني» لهذه المواد يمكن ربطه بهذا الطلب.", Theme.Muted);
        }

        void AddRow(int lineId, int item, double qty, double rec)
        {
            int st = UI.Sel(cmbStore);
            grid.Rows.Add(lineId, item, Cache.ItemCode(item), Cache.ItemName(item), Cache.ItemUnit(item),
                st > 0 ? Cache.Stock(item, st) : Cache.StockAll(item), qty, rec, Math.Max(0, qty - rec));
        }

        void RefreshBalances()
        {
            int st = UI.Sel(cmbStore);
            foreach (DataGridViewRow r in grid.Rows) { int it = Db.I(r.Cells["item"].Value); r.Cells["bal"].Value = st > 0 ? Cache.Stock(it, st) : Cache.StockAll(it); }
        }

        void AddPicked()
        {
            if (ReadOnlyReq) return;
            int it = pick.Value;
            if (it == 0) { UI.Warn("اختر المادة أولا."); pick.Focus(); return; }
            double q; if (!UI.TryNum(txtQty.Text, out q) || q <= 0) { UI.Warn("أدخل كمية صحيحة أكبر من صفر."); txtQty.Focus(); return; }
            foreach (DataGridViewRow r in grid.Rows)
                if (Db.I(r.Cells["item"].Value) == it)
                {
                    r.Cells["qty"].Value = Db.D(r.Cells["qty"].Value) + q;
                    grid.CurrentCell = r.Cells["qty"];
                    pick.Value = 0; txtQty.Text = ""; pick.Focus();
                    return;
                }
            AddRow(0, it, q, 0);
            grid.CurrentCell = grid.Rows[grid.Rows.Count - 1].Cells["qty"];
            Dirty();
            pick.Value = 0; txtQty.Text = ""; pick.Focus();
        }

        void RemoveRow(DataGridViewRow r)
        {
            if (Db.D(r.Cells["rec"].Value) > 1e-9) { UI.Warn("لا يمكن حذف مادة استُلم منها شيء."); return; }
            grid.Rows.Remove(r);
            Dirty();
        }

        bool Save()
        {
            if (!Session.Demand(Session.RecAdd)) return false;
            if (grid.IsCurrentCellInEditMode) grid.EndEdit();
            PReq r = new PReq();
            r.Id = req.Id; r.No = req.No; r.Status = req.Status; r.User = req.User;
            r.Date = dtp.Value.Date; r.Store = UI.Sel(cmbStore); r.Supplier = sup.Value; r.Note = txtNote.Text.Trim();
            foreach (DataGridViewRow g in grid.Rows)
            {
                double q;
                if (!UI.TryNum(Db.S(g.Cells["qty"].Value), out q)) { UI.Warn("كمية غير صحيحة: " + g.Cells["name"].Value); return false; }
                PReqLine l = new PReqLine(); l.LineId = Db.I(g.Cells["lid"].Value); l.ItemNo = Db.I(g.Cells["item"].Value); l.Qty = q;
                r.Lines.Add(l);
            }
            try
            {
                UI.WaitCursor(true);
                PurchaseRequests.Save(r);
                req = PurchaseRequests.Load(r.Id) ?? r;
                Changed = true;
                ShowReq();
                Info("✓ تم الحفظ • طلب الشراء رقم " + req.No, Theme.Success);
                return true;
            }
            catch (UserError ue) { UI.Warn(ue.Message); return false; }
            catch (Exception ex) { UI.Error(ex); return false; }
            finally { UI.WaitCursor(false); }
        }

        void Print()
        {
            if (req.Id == 0 || dirty) { if (ReadOnlyReq || !UI.Confirm("يجب حفظ الطلب قبل الطباعة. حفظ الآن؟") || !Save()) return; }
            PrintPreviewForm.Show(this, PurchaseReports.Request(req));
        }

        void ChangeStatus(int status, string question)
        {
            if (dirty && !Save()) return;
            if (!UI.Confirm(question)) return;
            try
            {
                PurchaseRequests.SetStatus(req.Id, status);
                req = PurchaseRequests.Load(req.Id);
                Changed = true;
                ShowReq();
                Info("✓ الحالة الآن: " + PurchaseRequests.StatusName(req.Status), Theme.Success);
            }
            catch (UserError ue) { UI.Warn(ue.Message); }
            catch (Exception ex) { UI.Error(ex); }
        }
    }
}
