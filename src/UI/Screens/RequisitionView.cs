using System.Collections.Generic;
using System.Data;
using System.Drawing;
using System.Windows.Forms;
using System;

namespace Stores
{
    // ================================================================== one material requisition «طلب صرف مواد»
    /// <summary>
    /// New / edit a requisition (cost centre, store, requested by, items with the quantity and the available stock),
    /// «إرسال للموافقة», and for the approver «موافقة» / «رفض»; for the store «صرف الطلب» opens a new issue pre-filled
    /// with the remaining quantities.
    /// </summary>
    class RequisitionView : UserControl, IView
    {
        MainForm main;
        MreqDoc doc;
        bool loading, dirty;
        StockSnapshot snap;

        Label lblTitle, lblState, lblMsg, lblAvail;
        DateTimePicker dtp;
        ProjectPicker project;
        ComboBox cmbStore;
        TextBox txtBy, txtNote, txtQty;
        ItemPicker item;
        DataGridView grid;
        Panel entryCard;
        Button btnSave, btnSend, btnApprove, btnReject, btnIssue, btnClose, btnCancel, btnPrint, btnBack, btnAdd;

        public RequisitionView(MainForm m, int no)
        {
            main = m;
            BackColor = Theme.Bg;
            BuildUi();
            Load += delegate
            {
                MreqDoc d = no > 0 ? Requisitions.Load(no) : null;
                if (d != null) { try { Requisitions.Recalc(no); d = Requisitions.Load(no); } catch { } }
                if (d == null) { d = new MreqDoc(); d.Store = Cache.DefaultStore; d.RequestedBy = Session.UserName ?? ""; }
                Show(d);
                if (d.No == 0) project.FocusText();
            };
        }

        // ------------------------------------------------------------ layout
        void BuildUi()
        {
            Panel band = new Panel(); band.Dock = DockStyle.Top; band.Height = 44;
            lblTitle = new Label(); lblTitle.Font = Theme.FontTitle; lblTitle.ForeColor = Theme.Text; lblTitle.AutoSize = true; lblTitle.Dock = DockStyle.Right; lblTitle.Padding = new Padding(0, 6, 0, 0);
            lblState = new Label(); lblState.Font = Theme.FontBold; lblState.AutoSize = true; lblState.Dock = DockStyle.Right; lblState.Padding = new Padding(14, 12, 14, 0);
            btnBack = UI.BtnLight("◄ قائمة الطلبات", 140); btnBack.Dock = DockStyle.Left; btnBack.Height = 32;
            band.Controls.Add(lblState); band.Controls.Add(lblTitle); band.Controls.Add(btnBack);

            // actions at the top (as in the document screen)
            FlowLayoutPanel bar = UI.Flow(); bar.WrapContents = true; bar.Padding = new Padding(2, 2, 2, 4);
            btnSave = UI.Btn("حفظ كمسودة", Theme.Accent, 120);
            btnSend = UI.Btn("إرسال للموافقة", Theme.Primary, 140);
            btnApprove = UI.Btn("موافقة", Theme.Success, 110);
            btnReject = UI.Btn("رفض", Theme.Danger, 100);
            btnIssue = UI.Btn("صرف الطلب", Theme.Teal, 120);
            btnClose = UI.BtnLight("إغلاق الطلب", 110);
            btnCancel = UI.BtnLight("إلغاء الطلب", 110);
            btnPrint = UI.BtnLight("طباعة", 90);
            Glyphs(btnSend, "", btnReject, "", btnIssue, "");
            lblMsg = new Label(); lblMsg.AutoSize = true; lblMsg.Margin = new Padding(14, 12, 4, 0); lblMsg.Font = Theme.FontBold;
            bar.Controls.AddRange(new Control[] { btnSave, btnSend, btnApprove, btnReject, btnIssue, btnClose, btnCancel, btnPrint, lblMsg });

            Panel head = UI.Card(); head.Dock = DockStyle.Top; head.Padding = new Padding(10, 6, 10, 6);
            FlowLayoutPanel f = UI.Flow(); f.WrapContents = true;
            dtp = UI.DatePicker(DateTime.Today);
            project = new ProjectPicker(300);
            cmbStore = UI.Combo(170); UI.Fill(cmbStore, Cache.Stores, "stor_no", "stor_name", null);
            txtBy = UI.Text(190);
            txtNote = UI.Text(320);
            f.Controls.AddRange(new Control[] { UI.Field("مركز الكلفة", project), UI.Field("المخزن", cmbStore), UI.Field("التاريخ", dtp),
                                                UI.Field("طالب المواد", txtBy), UI.Field("ملاحظات", txtNote) });
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
            AddCol("qty", "الكمية المطلوبة", 125, false, true);
            AddCol("avail", "المتوفر", 100, true, true);
            AddCol("issued", "المصروف", 100, true, true);
            AddCol("rem", "المتبقي", 100, true, true);
            DataGridViewButtonColumn del = new DataGridViewButtonColumn(); del.Name = "del"; del.HeaderText = ""; del.Text = "✕"; del.UseColumnTextForButtonValue = true; del.Width = 40;
            del.FlatStyle = FlatStyle.Flat; del.DefaultCellStyle.ForeColor = Theme.Danger;
            grid.Columns.Add(del);
            DataGridViewTextBoxColumn hid = new DataGridViewTextBoxColumn(); hid.Name = "itemNo"; hid.Visible = false; grid.Columns.Add(hid);
            gc.Controls.Add(grid);
            Label hint = new Label(); hint.Dock = DockStyle.Bottom; hint.Height = 22; hint.ForeColor = Theme.Muted; hint.Font = Theme.FontSmall; hint.TextAlign = ContentAlignment.MiddleLeft;
            hint.Text = "① اختر مركز الكلفة والمخزن   ② ابحث عن المادة واكتب الكمية ثم Enter   ③ «إرسال للموافقة»   •   «المتوفر» = رصيد المخزن ناقص المحجوز لمراكز كلفة أخرى";

            Controls.Add(gc); Controls.Add(hint); Controls.Add(entryCard); Controls.Add(head); Controls.Add(bar); Controls.Add(band);

            project.Changed += delegate { MarkDirty(); RefreshAvail(); };
            cmbStore.SelectedIndexChanged += delegate { if (loading) return; snap = null; MarkDirty(); RefreshAvail(); };
            dtp.ValueChanged += delegate { MarkDirty(); };
            txtBy.TextChanged += delegate { MarkDirty(); };
            txtNote.TextChanged += delegate { MarkDirty(); };
            item.Changed += delegate { ShowItemAvail(); txtQty.Focus(); txtQty.SelectAll(); };
            txtQty.KeyDown += delegate(object s, KeyEventArgs k) { if (k.KeyCode == Keys.Enter) { k.SuppressKeyPress = true; AddEntry(); } };
            btnAdd.Click += delegate { AddEntry(); };
            grid.CellEndEdit += delegate(object s, DataGridViewCellEventArgs k) { if (k.RowIndex >= 0) { UpdateRow(grid.Rows[k.RowIndex]); MarkDirty(); } };
            grid.CellContentClick += delegate(object s, DataGridViewCellEventArgs k)
            {
                if (k.RowIndex < 0 || grid.Columns[k.ColumnIndex].Name != "del" || !Editable) return;
                grid.Rows.RemoveAt(k.RowIndex); Renumber(); MarkDirty();
            };
            grid.DataError += delegate(object s, DataGridViewDataErrorEventArgs k) { k.ThrowException = false; };
            grid.CellFormatting += delegate(object s, DataGridViewCellFormattingEventArgs k)
            {
                if (k.RowIndex < 0) return;
                string c = grid.Columns[k.ColumnIndex].Name;
                DataGridViewRow r = grid.Rows[k.RowIndex];
                if (c == "avail" && Db.D(r.Cells["avail"].Value) < Db.D(r.Cells["rem"].Value) - 1e-9) { k.CellStyle.ForeColor = Theme.Danger; k.CellStyle.Font = Theme.FontBold; }
                if (c == "rem" && Db.D(r.Cells["rem"].Value) <= 1e-9) k.CellStyle.ForeColor = Theme.Success;
            };

            btnBack.Click += delegate { if (CanLeave() && main != null) main.Show(new RequisitionsView(main)); };
            btnSave.Click += delegate { Save(); };
            btnSend.Click += delegate { Send(); };
            btnApprove.Click += delegate { Approve(); };
            btnReject.Click += delegate { Reject(); };
            btnIssue.Click += delegate { Issue(); };
            btnClose.Click += delegate { Act("إغلاق الطلب رقم " + doc.MenuNo + " دون صرف المتبقي؟", delegate { Requisitions.Close(doc.No); }, "✓ أُغلق الطلب"); };
            btnCancel.Click += delegate { Act("إلغاء الطلب رقم " + doc.MenuNo + "؟", delegate { Requisitions.Cancel(doc.No); }, "✓ أُلغي الطلب"); };
            btnPrint.Click += delegate { Print(); };
        }

        /// <summary>Fluent icons for buttons whose text has no automatic one (pairs: button, glyph).</summary>
        internal static void Glyphs(params object[] a)
        {
            for (int i = 0; i + 1 < a.Length; i += 2) { FluentButton b = a[i] as FluentButton; if (b != null) b.Glyph = (string)a[i + 1]; }
        }

        void AddCol(string name, string header, int width, bool ro, bool num)
        {
            DataGridViewTextBoxColumn c = UI.Col(name, header, null, width, num);
            c.ReadOnly = ro; c.SortMode = DataGridViewColumnSortMode.NotSortable;
            if (ro && name != "no") c.DefaultCellStyle.BackColor = Theme.RowAlt;
            grid.Columns.Add(c);
        }

        bool Editable { get { return doc != null && Requisitions.CanEdit(doc); } }

        // ------------------------------------------------------------ state
        void MarkDirty()
        {
            if (loading || !Editable) return;
            if (!dirty) { dirty = true; Msg("● تغييرات غير محفوظة", Theme.Warning); }
        }

        void Msg(string text, Color c) { lblMsg.Text = text ?? ""; lblMsg.ForeColor = c; }

        static Color StatusColor(int s)
        {
            switch (s)
            {
                case Requisitions.Pending: return Theme.Warning;
                case Requisitions.Approved: return Theme.Primary;
                case Requisitions.Rejected: return Theme.Danger;
                case Requisitions.Partial: return Theme.Teal;
                case Requisitions.Done: return Theme.Success;
                default: return Theme.Muted;
            }
        }

        void Show(MreqDoc d)
        {
            loading = true;
            doc = d;
            lblTitle.Text = d.No == 0 ? "طلب صرف مواد جديد" : "طلب صرف مواد رقم " + d.MenuNo;
            string st = d.No == 0 ? "جديد • الرقم يُثبّت عند الحفظ" : Requisitions.StatusName(d.Status);
            if (d.No > 0 && (d.Status == Requisitions.Approved || d.Status == Requisitions.Partial || d.Status == Requisitions.Done) && d.ApprovedBy.Length > 0)
                st += "  •  اعتمده: " + d.ApprovedBy + (d.ApprovedAt > DateTime.MinValue ? " " + UI.Date(d.ApprovedAt) : "");
            if (d.Status == Requisitions.Done && d.ClosedBy.Length > 0) st += "  •  أُغلق دون صرف المتبقي (" + d.ClosedBy + ")";
            if (d.Status == Requisitions.Rejected) st += "  •  رفضه: " + d.ApprovedBy + "  •  السبب: " + d.RejectReason;
            if (d.No > 0 && d.Value > 0) st += "  •  القيمة التقديرية: " + UI.Num(d.Value);
            lblState.Text = st; lblState.ForeColor = d.No == 0 ? Theme.Muted : StatusColor(d.Status);
            dtp.Value = d.Date < dtp.MinDate ? DateTime.Today : d.Date;
            project.Value = d.Project;
            UI.SetSel(cmbStore, d.Store);
            txtBy.Text = d.RequestedBy; txtNote.Text = d.Notes;
            snap = null;
            grid.Rows.Clear();
            foreach (MreqLine l in d.Lines)
            {
                int i = grid.Rows.Add();
                DataGridViewRow r = grid.Rows[i];
                r.Cells["itemNo"].Value = l.ItemNo; r.Cells["qty"].Value = l.Qty; r.Cells["issued"].Value = l.Issued;
                FillRow(r);
            }
            Renumber(); RefreshAvail();

            bool ed = Editable;
            bool mine = Session.IsAdmin || d.UserName == (Session.UserName ?? "");
            grid.Columns["qty"].ReadOnly = !ed; grid.Columns["del"].Visible = ed;
            entryCard.Visible = ed;
            dtp.Enabled = project.Enabled = cmbStore.Enabled = txtBy.Enabled = txtNote.Enabled = ed;
            btnSave.Visible = ed;
            btnSend.Visible = ed;
            btnApprove.Visible = btnReject.Visible = d.Status == Requisitions.Pending && Requisitions.CanApprove && (Session.IsAdmin || !mine);
            btnIssue.Visible = (d.Status == Requisitions.Approved || d.Status == Requisitions.Partial) && Session.Can(Session.IssAdd);
            btnClose.Visible = d.Status == Requisitions.Partial && (Requisitions.CanApprove || Session.Can(Session.IssAdd));
            btnCancel.Visible = d.No > 0 && d.Status <= Requisitions.Rejected && (mine || Requisitions.CanApprove);
            btnPrint.Visible = d.No > 0;
            bool showIssued = d.Status == Requisitions.Approved || d.Status == Requisitions.Partial || d.Status == Requisitions.Done;
            grid.Columns["issued"].Visible = grid.Columns["rem"].Visible = showIssued;
            dirty = false;
            if (d.Status == Requisitions.Pending && !btnApprove.Visible) Msg("بانتظار موافقة من لديه صلاحية «" + Requisitions.PermName + "»", Theme.Warning);
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

        void UpdateRow(DataGridViewRow r)
        {
            double q, iss = Db.D(r.Cells["issued"].Value);
            UI.TryNum(Db.S(r.Cells["qty"].Value), out q);
            r.Cells["rem"].Value = Math.Max(0, q - iss);
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

        /// <summary>Free stock in the request's store: balance minus what is reserved for other cost centres.</summary>
        double Available(int it)
        {
            if (it == 0 || UI.Sel(cmbStore) == 0) return 0;
            StockSnapshot s = Snap;
            return s.Stock(it) - (Db.HasProj ? s.ForOthers(it, project.Value) : 0);
        }

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
            lblAvail.Text = "المتوفر في " + Cache.Store(UI.Sel(cmbStore)) + ": " + UI.Num(a) + (u.Length > 0 ? " " + u : "");
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

        /// <summary>Adds an item (an item already in the request gets its quantity increased).</summary>
        public void AddLine(int it, double q)
        {
            DataGridViewRow row = null;
            foreach (DataGridViewRow r in grid.Rows) if (Db.I(r.Cells["itemNo"].Value) == it) { row = r; break; }
            if (row != null) { double o; UI.TryNum(Db.S(row.Cells["qty"].Value), out o); row.Cells["qty"].Value = o + q; UpdateRow(row); }
            else
            {
                row = grid.Rows[grid.Rows.Add()];
                row.Cells["itemNo"].Value = it; row.Cells["qty"].Value = q; row.Cells["issued"].Value = 0;
                FillRow(row);
                row.Cells["avail"].Value = Available(it);
            }
            Renumber(); MarkDirty();
            double a = Available(it);
            if (Db.D(row.Cells["qty"].Value) > a + 1e-9) Msg("⚠ " + Cache.ItemName(it) + ": المطلوب أكبر من المتوفر (" + UI.Num(a) + ")", Theme.Danger);
            else Msg("✓ أُضيفت: " + Cache.ItemName(it) + " • " + UI.Num(Db.D(row.Cells["qty"].Value)), Theme.Success);
        }

        // ------------------------------------------------------------ commands
        MreqDoc Collect()
        {
            if (grid.IsCurrentCellInEditMode) grid.EndEdit();
            MreqDoc d = new MreqDoc();
            d.No = doc.No; d.MenuNo = doc.MenuNo; d.Status = doc.Status; d.UserName = doc.UserName;
            d.Date = dtp.Value.Date; d.Project = project.Value; d.Store = UI.Sel(cmbStore);
            d.RequestedBy = txtBy.Text.Trim(); d.Notes = txtNote.Text.Trim();
            foreach (DataGridViewRow r in grid.Rows)
            {
                double q;
                int it = Db.I(r.Cells["itemNo"].Value);
                if (!UI.TryNum(Db.S(r.Cells["qty"].Value), out q) || q <= 0) throw new UserError("السطر " + r.Cells["no"].Value + ": أدخل كمية صحيحة للمادة " + Cache.ItemName(it));
                d.Lines.Add(new MreqLine { ItemNo = it, Qty = q });
            }
            return d;
        }

        public bool Save()
        {
            if (!Editable) return true;
            try
            {
                MreqDoc d = Collect();
                UI.WaitCursor(true);
                int no = Requisitions.Save(d);
                Show(Requisitions.Load(no));
                Msg("✓ حُفظ الطلب رقم " + doc.MenuNo + " كمسودة", Theme.Success);
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
                int st = Requisitions.Send(doc.No);
                Show(Requisitions.Load(doc.No));
                if (st == Requisitions.Approved) Msg("✓ اعتُمد الطلب تلقائيا (قيمته دون حد الاعتماد) • يمكن صرفه الآن", Theme.Success);
                else Msg("✓ أُرسل الطلب للموافقة", Theme.Primary);
            }
            catch (UserError ue) { UI.Warn(ue.Message); }
            catch (Exception ex) { UI.Error(ex); }
        }

        void Approve() { Act(null, delegate { Requisitions.Approve(doc.No); }, "✓ تمت الموافقة على الطلب"); }

        void Reject()
        {
            string reason = InputDialog.Ask(FindForm(), "رفض طلب الصرف", "سبب الرفض:", "");
            if (reason == null) return;
            Act(null, delegate { Requisitions.Reject(doc.No, reason); }, "✓ رُفض الطلب");
        }

        void Act(string confirm, Action a, string done)
        {
            if (doc == null || doc.No == 0) return;
            if (confirm != null && !UI.Confirm(confirm)) return;
            try { a(); Show(Requisitions.Load(doc.No)); Msg(done, Theme.Success); }
            catch (UserError ue) { UI.Warn(ue.Message); }
            catch (Exception ex) { UI.Error(ex); }
        }

        /// <summary>«صرف الطلب»: a new issue with the cost centre, the store and the remaining quantities.</summary>
        void Issue()
        {
            if (main == null || doc == null || doc.No == 0 || !Session.Demand(Session.IssAdd)) return;
            try { Requisitions.Recalc(doc.No); } catch { }
            List<KeyValuePair<int, double>> rest = Requisitions.Remaining(doc.No);
            if (rest.Count == 0) { UI.Info("لا توجد كميات متبقية للصرف في هذا الطلب."); Show(Requisitions.Load(doc.No)); return; }
            DocView v = new DocView(main, DocKind.Issue, 0, doc.Project);
            main.Show(v);
            v.Prefill(doc.Project, doc.Store, rest, doc.No);
        }

        void Print()
        {
            if (doc == null || doc.No == 0) return;
            if (dirty) { if (!UI.Confirm("يجب حفظ الطلب قبل الطباعة. حفظ الآن؟") || !Save()) return; }
            using (ReportViewerForm f = new ReportViewerForm(Requisitions.Print(doc))) f.ShowDialog(FindForm());
        }

        public bool CanLeave()
        {
            if (!dirty) return true;
            DialogResult r = UI.YesNoCancel("يوجد تغييرات غير محفوظة في طلب الصرف.\nهل تريد حفظها؟");
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
