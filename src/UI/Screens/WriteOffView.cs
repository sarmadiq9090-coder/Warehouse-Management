using System.Collections.Generic;
using System.Data;
using System.Drawing;
using System.Windows.Forms;
using System;

namespace Stores
{
    // ================================================================== one write-off «سند إتلاف / شطب مواد»
    /// <summary>
    /// New / edit a write-off (store, date, reason, notes, items with the quantity, an optional reason per line, the balance and
    /// the cost), «إرسال للاعتماد», and for the approver «اعتماد وترحيل» / «رفض». Printable with the signatures
    /// «أعدّه / اعتمده / أمين المخزن». Built like the requisition screen (RequisitionView).
    /// </summary>
    class WriteOffView : UserControl, IView
    {
        MainForm main;
        WoDoc doc;
        bool loading, dirty;
        StockSnapshot snap;

        Label lblTitle, lblState, lblMsg, lblAvail, lblTotal;
        DateTimePicker dtp;
        ComboBox cmbStore, cmbReason;
        TextBox txtNote, txtQty;
        ItemPicker item;
        DataGridView grid;
        Panel entryCard;
        Button btnSave, btnSend, btnApprove, btnReject, btnCancel, btnPrint, btnBack, btnAdd;

        public WriteOffView(MainForm m, int no)
        {
            main = m;
            BackColor = Theme.Bg;
            BuildUi();
            Load += delegate
            {
                WoDoc d = no > 0 ? WriteOffs.Load(no) : null;
                if (d == null) { d = new WoDoc(); d.Store = Cache.DefaultStore; }
                Show(d);
                if (d.No == 0) item.Focus();
            };
        }

        // ------------------------------------------------------------ layout
        void BuildUi()
        {
            Panel band = new Panel(); band.Dock = DockStyle.Top; band.Height = 44;
            lblTitle = new Label(); lblTitle.Font = Theme.FontTitle; lblTitle.ForeColor = Theme.Text; lblTitle.AutoSize = true; lblTitle.Dock = DockStyle.Right; lblTitle.Padding = new Padding(0, 6, 0, 0);
            lblState = new Label(); lblState.Font = Theme.FontBold; lblState.AutoSize = true; lblState.Dock = DockStyle.Right; lblState.Padding = new Padding(14, 12, 14, 0);
            btnBack = UI.BtnLight("◄ قائمة سندات الإتلاف", 170); btnBack.Dock = DockStyle.Left; btnBack.Height = 32;
            band.Controls.Add(lblState); band.Controls.Add(lblTitle); band.Controls.Add(btnBack);

            FlowLayoutPanel bar = UI.Flow(); bar.WrapContents = true; bar.Padding = new Padding(2, 2, 2, 4);
            btnSave = UI.Btn("حفظ كمسودة", Theme.Accent, 120);
            btnSend = UI.Btn("إرسال للاعتماد", Theme.Primary, 140);
            btnApprove = UI.Btn("اعتماد وترحيل", Theme.Success, 140);
            btnReject = UI.Btn("رفض", Theme.Danger, 100);
            btnCancel = UI.BtnLight("إلغاء السند", 110);
            btnPrint = UI.BtnLight("طباعة", 90);
            RequisitionView.Glyphs(btnSend, "", btnApprove, "", btnReject, "");
            lblMsg = new Label(); lblMsg.AutoSize = true; lblMsg.Margin = new Padding(14, 12, 4, 0); lblMsg.Font = Theme.FontBold;
            bar.Controls.AddRange(new Control[] { btnSave, btnSend, btnApprove, btnReject, btnCancel, btnPrint, lblMsg });

            Panel head = UI.Card(); head.Dock = DockStyle.Top; head.Padding = new Padding(10, 6, 10, 6);
            FlowLayoutPanel f = UI.Flow(); f.WrapContents = true;
            dtp = UI.DatePicker(DateTime.Today);
            cmbStore = UI.Combo(170); UI.Fill(cmbStore, Cache.Stores, "stor_no", "stor_name", null);
            cmbReason = UI.Combo(160);
            foreach (int r in WriteOffs.AllReasons) cmbReason.Items.Add(new ComboItem(r, WriteOffs.ReasonName(r)));
            txtNote = UI.Text(360);
            f.Controls.AddRange(new Control[] { UI.Field("المخزن", cmbStore), UI.Field("التاريخ", dtp), UI.Field("سبب الإتلاف", cmbReason), UI.Field("ملاحظات / وصف الحالة", txtNote) });
            head.Controls.Add(f); UI.FitHeight(head);

            entryCard = UI.Card(); entryCard.Dock = DockStyle.Top; entryCard.Padding = new Padding(10, 4, 10, 4);
            FlowLayoutPanel e = UI.Flow(); e.WrapContents = true;
            item = new ItemPicker(380);
            txtQty = UI.Text(90); txtQty.RightToLeft = RightToLeft.No; txtQty.TextAlign = HorizontalAlignment.Center;
            btnAdd = UI.Btn("+ إضافة", Theme.Purple, 100); btnAdd.Height = 30;
            lblAvail = new Label(); lblAvail.AutoSize = true; lblAvail.Margin = new Padding(12, 11, 4, 0); lblAvail.ForeColor = Theme.Muted;
            e.Controls.AddRange(new Control[] { UI.Field("المادة", item), UI.Field("الكمية", txtQty), btnAdd, lblAvail });
            entryCard.Controls.Add(e); UI.FitHeight(entryCard);

            Panel gc = UI.Card(); gc.Dock = DockStyle.Fill; gc.Padding = new Padding(1);
            grid = new DataGridView(); grid.Dock = DockStyle.Fill; UI.StyleGrid(grid);
            grid.AllowUserToAddRows = false; grid.AllowUserToDeleteRows = false; grid.MultiSelect = false;
            grid.SelectionMode = DataGridViewSelectionMode.CellSelect;
            AddCol("no", "ت", 45, true, false);
            AddCol("code", "الرمز", 110, true, false);
            AddCol("name", "اسم المادة", 0, true, false);
            AddCol("unit", "الوحدة", 75, true, false);
            AddCol("qty", "الكمية", 100, false, true);
            DataGridViewComboBoxColumn rc = new DataGridViewComboBoxColumn(); rc.Name = "reason"; rc.HeaderText = "السبب (إن اختلف)"; rc.Width = 150;
            List<ComboItem> rs = new List<ComboItem> { new ComboItem(0, "سبب السند") };
            foreach (int r in WriteOffs.AllReasons) rs.Add(new ComboItem(r, WriteOffs.ReasonName(r)));
            rc.DataSource = rs; rc.ValueMember = "Id"; rc.DisplayMember = "Name"; rc.ValueType = typeof(int);
            rc.FlatStyle = FlatStyle.Flat; rc.DisplayStyle = DataGridViewComboBoxDisplayStyle.Nothing; rc.SortMode = DataGridViewColumnSortMode.NotSortable;
            grid.Columns.Add(rc);
            AddCol("avail", "رصيد المخزن", 125, true, true);
            AddCol("cost", "كلفة الوحدة", 105, true, true);
            AddCol("value", "الكلفة", 140, true, true);
            DataGridViewButtonColumn del = new DataGridViewButtonColumn(); del.Name = "del"; del.HeaderText = ""; del.Text = "✕"; del.UseColumnTextForButtonValue = true; del.Width = 40;
            del.FlatStyle = FlatStyle.Flat; del.DefaultCellStyle.ForeColor = Theme.Danger;
            grid.Columns.Add(del);
            DataGridViewTextBoxColumn hid = new DataGridViewTextBoxColumn(); hid.Name = "itemNo"; hid.Visible = false; grid.Columns.Add(hid);
            DataGridViewTextBoxColumn hc = new DataGridViewTextBoxColumn(); hc.Name = "posted"; hc.Visible = false; grid.Columns.Add(hc);
            gc.Controls.Add(grid);
            Panel foot = new Panel(); foot.Dock = DockStyle.Bottom; foot.Height = 24;
            lblTotal = new Label(); lblTotal.Dock = DockStyle.Left; lblTotal.AutoSize = true; lblTotal.Font = Theme.FontBold; lblTotal.ForeColor = Theme.Text; lblTotal.Padding = new Padding(6, 4, 6, 0);
            Label hint = new Label(); hint.Dock = DockStyle.Fill; hint.ForeColor = Theme.Muted; hint.Font = Theme.FontSmall; hint.TextAlign = ContentAlignment.MiddleLeft;
            hint.Text = "① اختر المخزن والسبب   ② ابحث عن المادة واكتب الكمية ثم Enter   ③ «إرسال للاعتماد»   •   لا تخرج المواد من المخزن إلا بعد الاعتماد";
            foot.Controls.Add(hint); foot.Controls.Add(lblTotal);

            Controls.Add(gc); Controls.Add(foot); Controls.Add(entryCard); Controls.Add(head); Controls.Add(bar); Controls.Add(band);

            cmbStore.SelectedIndexChanged += delegate { if (loading) return; snap = null; MarkDirty(); RefreshAvail(); };
            cmbReason.SelectedIndexChanged += delegate { MarkDirty(); };
            dtp.ValueChanged += delegate { MarkDirty(); };
            txtNote.TextChanged += delegate { MarkDirty(); };
            item.Changed += delegate { ShowItemAvail(); txtQty.Focus(); txtQty.SelectAll(); };
            txtQty.KeyDown += delegate(object s, KeyEventArgs k) { if (k.KeyCode == Keys.Enter) { k.SuppressKeyPress = true; AddEntry(); } };
            btnAdd.Click += delegate { AddEntry(); };
            grid.CellEndEdit += delegate(object s, DataGridViewCellEventArgs k) { if (k.RowIndex >= 0) { UpdateRow(grid.Rows[k.RowIndex]); UpdateTotal(); MarkDirty(); } };
            grid.CurrentCellDirtyStateChanged += delegate { if (grid.CurrentCell != null && grid.CurrentCell.OwningColumn.Name == "reason" && grid.IsCurrentCellDirty) grid.CommitEdit(DataGridViewDataErrorContexts.Commit); };
            grid.CellValueChanged += delegate(object s, DataGridViewCellEventArgs k) { if (k.RowIndex >= 0 && grid.Columns[k.ColumnIndex].Name == "reason") MarkDirty(); };
            grid.CellContentClick += delegate(object s, DataGridViewCellEventArgs k)
            {
                if (k.RowIndex < 0 || grid.Columns[k.ColumnIndex].Name != "del" || !Editable) return;
                grid.Rows.RemoveAt(k.RowIndex); Renumber(); UpdateTotal(); MarkDirty();
            };
            grid.DataError += delegate(object s, DataGridViewDataErrorEventArgs k) { k.ThrowException = false; };
            grid.CellFormatting += delegate(object s, DataGridViewCellFormattingEventArgs k)
            {
                if (k.RowIndex < 0 || grid.Columns[k.ColumnIndex].Name != "avail" || doc == null || doc.Status == WriteOffs.Posted) return;
                DataGridViewRow r = grid.Rows[k.RowIndex];
                double q; UI.TryNum(Db.S(r.Cells["qty"].Value), out q);
                if (Db.D(r.Cells["avail"].Value) < q - 1e-9) { k.CellStyle.ForeColor = Theme.Danger; k.CellStyle.Font = Theme.FontBold; }
            };

            btnBack.Click += delegate { if (CanLeave() && main != null) main.Show(new WriteOffsView(main)); };
            btnSave.Click += delegate { Save(); };
            btnSend.Click += delegate { Send(); };
            btnApprove.Click += delegate { Approve(); };
            btnReject.Click += delegate { Reject(); };
            btnCancel.Click += delegate { Act("إلغاء سند الإتلاف رقم " + doc.MenuNo + "؟", delegate { WriteOffs.Cancel(doc.No); }, "✓ أُلغي السند"); };
            btnPrint.Click += delegate { Print(); };
        }

        void AddCol(string name, string header, int width, bool ro, bool num)
        {
            DataGridViewTextBoxColumn c = UI.Col(name, header, null, width, num);
            c.ReadOnly = ro; c.SortMode = DataGridViewColumnSortMode.NotSortable;
            if (ro && name != "no") c.DefaultCellStyle.BackColor = Theme.RowAlt;
            grid.Columns.Add(c);
        }

        bool Editable { get { return doc != null && WriteOffs.CanEdit(doc); } }

        // ------------------------------------------------------------ state
        void MarkDirty()
        {
            if (loading || !Editable) return;
            if (!dirty) { dirty = true; Msg("● تغييرات غير محفوظة", Theme.Warning); }
        }

        void Msg(string text, Color c) { lblMsg.Text = text ?? ""; lblMsg.ForeColor = c; }

        void Show(WoDoc d)
        {
            loading = true;
            doc = d;
            bool posted = d.Status == WriteOffs.Posted;
            lblTitle.Text = d.No == 0 ? "سند إتلاف / شطب مواد جديد" : "سند إتلاف / شطب مواد رقم " + d.MenuNo;
            string st = d.No == 0 ? "جديد • الرقم يُثبّت عند الحفظ" : WriteOffs.StatusName(d.Status);
            if (d.No > 0 && d.UserName.Length > 0) st += "  •  أعدّه: " + d.UserName;
            if (posted) st += "  •  اعتمده: " + d.ApprovedBy + (d.ApprovedAt > DateTime.MinValue ? " " + UI.Date(d.ApprovedAt) : "") + (d.SaleNo.Length > 0 ? "  •  سند الترحيل " + d.SaleNo : "");
            if (d.Status == WriteOffs.Rejected) st += "  •  رفضه: " + d.ApprovedBy + "  •  السبب: " + d.RejectReason;
            lblState.Text = st; lblState.ForeColor = d.No == 0 ? Theme.Muted : WriteOffsView.StatusColor(d.Status);
            dtp.Value = d.Date < dtp.MinDate ? DateTime.Today : d.Date;
            UI.SetSel(cmbStore, d.Store);
            UI.SetSel(cmbReason, d.Reason);
            txtNote.Text = d.Notes;
            snap = null;
            grid.Rows.Clear();
            foreach (WoLine l in d.Lines)
            {
                int i = grid.Rows.Add();
                DataGridViewRow r = grid.Rows[i];
                r.Cells["itemNo"].Value = l.ItemNo; r.Cells["qty"].Value = l.Qty; r.Cells["reason"].Value = l.Reason;
                if (posted) r.Cells["posted"].Value = l.Cost;
                FillRow(r);
            }
            Renumber(); RefreshAvail();

            bool ed = Editable;
            bool mine = Session.IsAdmin || d.UserName == (Session.UserName ?? "");
            grid.Columns["qty"].ReadOnly = !ed; grid.Columns["reason"].ReadOnly = !ed; grid.Columns["del"].Visible = ed;
            grid.Columns["avail"].Visible = !posted;
            grid.Columns["cost"].HeaderText = posted ? "كلفة الوحدة" : "آخر كلفة";
            grid.Columns["value"].HeaderText = posted ? "الكلفة" : "القيمة التقديرية";
            entryCard.Visible = ed;
            dtp.Enabled = cmbStore.Enabled = cmbReason.Enabled = txtNote.Enabled = ed;
            btnSave.Visible = btnSend.Visible = ed;
            btnApprove.Visible = btnReject.Visible = d.Status == WriteOffs.Pending && WriteOffs.CanApprove && (Session.IsAdmin || !mine);
            btnCancel.Visible = d.No > 0 && (d.Status == WriteOffs.Draft || d.Status == WriteOffs.Pending || d.Status == WriteOffs.Rejected) && (mine || WriteOffs.CanApprove);
            btnPrint.Visible = d.No > 0;
            UpdateTotal();
            dirty = false;
            if (d.Status == WriteOffs.Pending && !btnApprove.Visible) Msg("بانتظار اعتماد من لديه صلاحية «" + WriteOffs.PermName + "»", Theme.Warning);
            else if (posted) Msg("✓ خرجت المواد من المخزن بكلفتها • السند مقفل", Theme.Success);
            else Msg("", Theme.Muted);
            loading = false;
        }

        void FillRow(DataGridViewRow r)
        {
            int it = Db.I(r.Cells["itemNo"].Value);
            r.Cells["code"].Value = Cache.ItemCode(it);
            r.Cells["name"].Value = Cache.ItemName(it);
            r.Cells["unit"].Value = Cache.ItemUnit(it);
            UpdateRow(r);
        }

        /// <summary>Cost columns: the FIFO cost once posted, else qty × last receipt cost.</summary>
        void UpdateRow(DataGridViewRow r)
        {
            int it = Db.I(r.Cells["itemNo"].Value);
            double q; UI.TryNum(Db.S(r.Cells["qty"].Value), out q);
            if (r.Cells["posted"].Value != null)
            {
                double v = Db.D(r.Cells["posted"].Value);
                r.Cells["value"].Value = Math.Round(v, 4);
                r.Cells["cost"].Value = q > 0 ? Math.Round(v / q, 4) : 0;
            }
            else
            {
                double c = Requisitions.LastCost(it);
                r.Cells["cost"].Value = c;
                r.Cells["value"].Value = Math.Round(q * c, 4);
            }
        }

        void UpdateTotal()
        {
            double q = 0, v = 0;
            foreach (DataGridViewRow r in grid.Rows) { double x; if (UI.TryNum(Db.S(r.Cells["qty"].Value), out x)) q += x; v += Db.D(r.Cells["value"].Value); }
            bool posted = doc != null && doc.Status == WriteOffs.Posted;
            lblTotal.Text = grid.Rows.Count == 0 ? "" : "المواد: " + grid.Rows.Count + "   •   الكميات: " + UI.Num(q) + "   •   " + (posted ? "الكلفة: " : "القيمة التقديرية: ") + UI.Num(v);
        }

        void Renumber() { for (int i = 0; i < grid.Rows.Count; i++) grid.Rows[i].Cells["no"].Value = i + 1; }

        StockSnapshot Snap
        {
            get
            {
                int st = UI.Sel(cmbStore);
                if (snap == null || snap.Store != st) snap = new StockSnapshot(st);
                return snap;
            }
        }

        double Available(int it) { return it == 0 || UI.Sel(cmbStore) == 0 ? 0 : Snap.Stock(it); }

        void RefreshAvail()
        {
            try { foreach (DataGridViewRow r in grid.Rows) r.Cells["avail"].Value = Available(Db.I(r.Cells["itemNo"].Value)); }
            catch { }
            grid.Invalidate();
            ShowItemAvail();
        }

        void ShowItemAvail()
        {
            int it = item.Value;
            if (it == 0) { lblAvail.Text = ""; return; }
            double a = Available(it);
            string u = Cache.ItemUnit(it);
            lblAvail.Text = "الرصيد في " + Cache.Store(UI.Sel(cmbStore)) + ": " + UI.Num(a) + (u.Length > 0 ? " " + u : "");
            lblAvail.ForeColor = a > 0 ? Theme.Success : Theme.Danger;
        }

        void AddEntry()
        {
            if (!Editable) return;
            int it = item.Value;
            if (it == 0) { UI.Warn("اختر المادة أولا."); item.Focus(); return; }
            double q;
            if (!UI.TryNum(txtQty.Text, out q) || q <= 0) { UI.Warn("اكتب كمية صحيحة."); txtQty.Focus(); return; }
            AddLine(it, q);
            item.Value = 0; txtQty.Text = ""; lblAvail.Text = "";
            item.Focus();
        }

        /// <summary>Adds an item (an item already in the document with the document's reason gets its quantity increased).</summary>
        public void AddLine(int it, double q)
        {
            DataGridViewRow row = null;
            foreach (DataGridViewRow r in grid.Rows) if (Db.I(r.Cells["itemNo"].Value) == it && Db.I(r.Cells["reason"].Value) == 0) { row = r; break; }
            if (row != null) { double o; UI.TryNum(Db.S(row.Cells["qty"].Value), out o); row.Cells["qty"].Value = o + q; UpdateRow(row); }
            else
            {
                row = grid.Rows[grid.Rows.Add()];
                row.Cells["itemNo"].Value = it; row.Cells["qty"].Value = q; row.Cells["reason"].Value = 0;
                FillRow(row);
                row.Cells["avail"].Value = Available(it);
            }
            Renumber(); UpdateTotal(); MarkDirty();
            double a = Available(it);
            if (Db.D(row.Cells["qty"].Value) > a + 1e-9) Msg("⚠ " + Cache.ItemName(it) + ": الكمية أكبر من رصيد المخزن (" + UI.Num(a) + ")", Theme.Danger);
            else Msg("✓ أُضيفت: " + Cache.ItemName(it) + " • " + UI.Num(Db.D(row.Cells["qty"].Value)), Theme.Success);
        }

        // ------------------------------------------------------------ commands
        WoDoc Collect()
        {
            if (grid.IsCurrentCellInEditMode) grid.EndEdit();
            WoDoc d = new WoDoc();
            d.No = doc.No; d.MenuNo = doc.MenuNo; d.Status = doc.Status; d.UserName = doc.UserName;
            d.Date = dtp.Value.Date; d.Store = UI.Sel(cmbStore); d.Reason = UI.Sel(cmbReason); d.Notes = txtNote.Text.Trim();
            foreach (DataGridViewRow r in grid.Rows)
            {
                double q;
                int it = Db.I(r.Cells["itemNo"].Value);
                if (!UI.TryNum(Db.S(r.Cells["qty"].Value), out q) || q <= 0) throw new UserError("السطر " + r.Cells["no"].Value + ": أدخل كمية صحيحة للمادة " + Cache.ItemName(it));
                d.Lines.Add(new WoLine { ItemNo = it, Qty = q, Reason = Db.I(r.Cells["reason"].Value) });
            }
            return d;
        }

        public bool Save()
        {
            if (!Editable) return true;
            try
            {
                WoDoc d = Collect();
                UI.WaitCursor(true);
                int no = WriteOffs.Save(d);
                Show(WriteOffs.Load(no));
                Msg("✓ حُفظ سند الإتلاف رقم " + doc.MenuNo + " كمسودة", Theme.Success);
                return true;
            }
            catch (UserError ue) { UI.Warn(ue.Message); return false; }
            catch (Exception ex) { UI.Error(ex); return false; }
            finally { UI.WaitCursor(false); }
        }

        void Send()
        {
            if (!Save()) return;
            try
            {
                WriteOffs.Send(doc.No);
                Show(WriteOffs.Load(doc.No));
                Msg("✓ أُرسل السند للاعتماد • لم تخرج المواد بعد", Theme.Primary);
            }
            catch (UserError ue) { UI.Warn(ue.Message); }
            catch (Exception ex) { UI.Error(ex); }
        }

        void Approve()
        {
            Act("اعتماد سند الإتلاف رقم " + doc.MenuNo + "؟\nستخرج مواده من " + Cache.Store(doc.Store) + " بكلفتها ولا يمكن التراجع.",
                delegate { WriteOffs.Approve(doc.No); Cache.LoadItems(); }, "✓ اعتُمد السند وخرجت المواد من المخزن");
        }

        void Reject()
        {
            string reason = InputDialog.Ask(FindForm(), "رفض سند الإتلاف", "سبب الرفض:", "");
            if (reason == null) return;
            Act(null, delegate { WriteOffs.Reject(doc.No, reason); }, "✓ رُفض السند");
        }

        void Act(string confirm, Action a, string done)
        {
            if (doc == null || doc.No == 0) return;
            if (confirm != null && !UI.Confirm(confirm)) return;
            try { a(); Show(WriteOffs.Load(doc.No)); Msg(done, Theme.Success); }
            catch (UserError ue) { UI.Warn(ue.Message); }
            catch (Exception ex) { UI.Error(ex); }
        }

        void Print()
        {
            if (doc == null || doc.No == 0) return;
            if (dirty) { if (!UI.Confirm("يجب حفظ السند قبل الطباعة. حفظ الآن؟") || !Save()) return; }
            PrintPreviewForm.Show(FindForm(), WriteOffs.Print(doc));
        }

        public bool CanLeave()
        {
            if (!dirty) return true;
            DialogResult r = UI.YesNoCancel("يوجد تغييرات غير محفوظة في سند الإتلاف.\nهل تريد حفظها؟");
            if (r == DialogResult.Yes) return Save();
            return r == DialogResult.No;
        }

        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            if (keyData == (Keys.Control | Keys.S)) { Save(); return true; }
            if (keyData == (Keys.Control | Keys.P)) { Print(); return true; }
            return base.ProcessCmdKey(ref msg, keyData);
        }
    }
}
