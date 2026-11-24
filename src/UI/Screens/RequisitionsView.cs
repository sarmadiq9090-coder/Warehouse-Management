using System.Collections.Generic;
using System.Data;
using System.Drawing;
using System.Globalization;
using System.Windows.Forms;
using System;

namespace Stores
{
    // ================================================================== material requisitions list «طلبات الصرف»
    /// <summary>
    /// All requisitions with a status filter; approvers open on «بانتظار موافقتك» and can approve / reject from the list.
    /// The administrator sets «حد الاعتماد» here (requests below it are approved automatically when sent).
    /// </summary>
    class RequisitionsView : UserControl
    {
        MainForm main;
        ComboBox cmbStatus; TextBox txt; DataGridView grid; Label lblCount, help;
        Button add, open, approve, reject, limit;

        const int FilterAll = -1, FilterMine = -2, FilterOpen = -3;

        public RequisitionsView(MainForm m) : this(m, Requisitions.PendingForMe() > 0 ? FilterMine : FilterOpen) { }

        public RequisitionsView(MainForm m, int filter)
        {
            main = m;
            BackColor = Theme.Bg;
            Panel filt = UI.Card(); filt.Dock = DockStyle.Top; filt.Height = 64;
            FlowLayoutPanel f = UI.Flow(); f.WrapContents = true;
            cmbStatus = UI.Combo(190);
            List<ComboItem> st = new List<ComboItem>();
            if (Requisitions.CanApprove) st.Add(new ComboItem(FilterMine, "بانتظار موافقتك"));
            st.Add(new ComboItem(FilterOpen, "الطلبات المفتوحة"));
            st.Add(new ComboItem(FilterAll, "كل الطلبات"));
            foreach (int s in Requisitions.AllStatuses) st.Add(new ComboItem(s, Requisitions.StatusName(s)));
            foreach (ComboItem c in st) cmbStatus.Items.Add(c);
            txt = UI.Text(150);
            Button go = UI.BtnLight("عرض", 80); go.Height = 30;
            add = UI.Btn("+ طلب جديد", Theme.Accent, 130);
            open = UI.Btn("فتح", Theme.Primary, 80);
            approve = UI.Btn("موافقة", Theme.Success, 105);
            reject = UI.Btn("رفض", Theme.Danger, 90);
            RequisitionView.Glyphs(reject, "");
            limit = UI.BtnLight("حد الاعتماد…", 120);
            Button xl = UI.BtnLight("تصدير Excel", 110);
            f.Controls.AddRange(new Control[] { add, open, approve, reject, UI.Field("الحالة", cmbStatus), UI.Field("رقم / اسم", txt), go, limit, xl });
            filt.Controls.Add(f); UI.FitHeight(filt);
            approve.Visible = reject.Visible = Requisitions.CanApprove;
            limit.Visible = Session.IsAdmin;
            add.Visible = Session.Can(Session.IssView);

            help = new Label(); help.Dock = DockStyle.Top; help.Height = 28; help.ForeColor = Theme.Muted; help.TextAlign = ContentAlignment.MiddleLeft;
            help.Text = "طلب الصرف: ① طالب المواد يكتب الطلب ويرسله للموافقة   ② المعتمد يوافق أو يرفض   ③ أمين المخزن يصرفه بـ«صرف الطلب» (إخراج مخزني)" + LimitText();

            Panel gc = UI.Card(); gc.Dock = DockStyle.Fill; gc.Padding = new Padding(1);
            grid = new DataGridView(); grid.Dock = DockStyle.Fill; UI.ReadOnlyGrid(grid);
            grid.Columns.Add(UI.Col("no", "رقم الطلب", "menu_no", 80, false));
            grid.Columns.Add(UI.Col("d", "التاريخ", "mreq_date", 100, false)); grid.Columns["d"].DefaultCellStyle.Format = "yyyy/MM/dd";
            grid.Columns.Add(UI.Col("p", "مركز الكلفة", "pname", 220, false));
            grid.Columns.Add(UI.Col("s", "المخزن", "sname", 120, false));
            grid.Columns.Add(UI.Col("by", "طالب المواد", "requested_by", 140, false));
            grid.Columns.Add(UI.Col("n", "عدد المواد", "n", 85, true));
            grid.Columns.Add(UI.Col("q", "المطلوب", "q", 90, true));
            grid.Columns.Add(UI.Col("iq", "المصروف", "iq", 90, true));
            grid.Columns.Add(UI.Col("v", "القيمة التقديرية", "est_value", 120, true));
            grid.Columns.Add(UI.Col("st", "الحالة", "status_name", 125, false));
            grid.Columns.Add(UI.Col("ap", "المعتمد", "approved_by", 120, false));
            grid.Columns.Add(UI.Col("note", "ملاحظات", "notes", 0, false));
            int[] minW = { 75, 90, 140, 80, 100, 80, 80, 80, 110, 110, 80, 70 };
            for (int i = 0; i < grid.Columns.Count; i++)
            {
                DataGridViewColumn c = grid.Columns[i];
                c.FillWeight = i == 2 ? 200 : i == 11 ? 120 : c.Width;
                c.AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill; c.MinimumWidth = minW[i];
            }
            gc.Controls.Add(grid);
            lblCount = new Label(); lblCount.Dock = DockStyle.Bottom; lblCount.Height = 26; lblCount.ForeColor = Theme.Muted; lblCount.TextAlign = ContentAlignment.MiddleLeft;
            Controls.Add(gc); Controls.Add(lblCount); Controls.Add(help); Controls.Add(filt); Controls.Add(ViewHelp.Title("طلبات صرف المواد"));

            if (!Requisitions.Ready)
            {
                help.ForeColor = Theme.Danger;
                help.Text = "طلبات الصرف غير مفعّلة في ملف البيانات بعد. " + (Db.SchemaWarning ?? "");
                add.Enabled = open.Enabled = approve.Enabled = reject.Enabled = false;
            }

            UI.SetSel(cmbStatus, filter);
            cmbStatus.SelectedIndexChanged += delegate { Reload(); };
            go.Click += delegate { Reload(); };
            txt.KeyDown += delegate(object s, KeyEventArgs e) { if (e.KeyCode == Keys.Enter) { e.SuppressKeyPress = true; Reload(); } };
            add.Click += delegate { if (main != null) main.Show(new RequisitionView(main, 0)); };
            open.Click += delegate { OpenCurrent(); };
            grid.CellDoubleClick += delegate(object s, DataGridViewCellEventArgs e) { if (e.RowIndex >= 0) OpenCurrent(); };
            grid.KeyDown += delegate(object s, KeyEventArgs e) { if (e.KeyCode == Keys.Enter) { e.Handled = true; OpenCurrent(); } };
            approve.Click += delegate { Decide(true); };
            reject.Click += delegate { Decide(false); };
            limit.Click += delegate { EditLimit(); };
            xl.Click += delegate { Excel.ExportGrid(FindForm(), grid, "طلبات الصرف"); };
            grid.CellFormatting += delegate(object s, DataGridViewCellFormattingEventArgs e)
            {
                if (e.RowIndex < 0 || grid.Columns[e.ColumnIndex].Name != "st") return;
                DataRowView rv = grid.Rows[e.RowIndex].DataBoundItem as DataRowView;
                if (rv == null) return;
                int stt = Db.I(rv["status"]);
                e.CellStyle.ForeColor = StatusColor(stt);
                e.CellStyle.Font = Theme.FontBold;
            };
            Load += delegate { if (Requisitions.Ready) Reload(); };
        }

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

        static string LimitText()
        {
            if (!Requisitions.Ready) return "";
            double l = Requisitions.ApprovalLimit;
            return l > 0 ? "   •   حد الاعتماد: " + UI.Num(l) + " (الأقل منه يُعتمد تلقائيا)" : "   •   كل الطلبات تحتاج موافقة";
        }

        DataRowView Current { get { return grid.CurrentRow == null ? null : grid.CurrentRow.DataBoundItem as DataRowView; } }

        public void Reload()
        {
            try
            {
                UI.WaitCursor(true);
                try { Requisitions.RecalcOpen(); } catch { }
                int k = UI.Sel(cmbStatus);
                if (cmbStatus.SelectedItem == null) k = FilterOpen;
                DataTable t = Requisitions.List(k, txt.Text);
                grid.DataSource = t;
                int mine = Requisitions.PendingForMe();
                lblCount.Text = "عدد الطلبات: " + t.Rows.Count + (mine > 0 ? "    •    بانتظار موافقتك: " + mine : "") + "    •    انقر مرتين على الطلب لفتحه";
            }
            catch (Exception ex) { UI.Error(ex); }
            finally { UI.WaitCursor(false); }
        }

        void OpenCurrent()
        {
            DataRowView rv = Current;
            if (rv == null || main == null) return;
            main.Show(new RequisitionView(main, Db.I(rv["mreq_no"])));
        }

        void Decide(bool ok)
        {
            DataRowView rv = Current;
            if (rv == null) { UI.Warn("اختر طلبا من القائمة أولا."); return; }
            int no = Db.I(rv["mreq_no"]); string n = Db.S(rv["menu_no"]);
            try
            {
                if (ok)
                {
                    if (!UI.Confirm("الموافقة على طلب الصرف رقم " + n + " (" + Db.S(rv["pname"]) + ")؟")) return;
                    Requisitions.Approve(no);
                }
                else
                {
                    string reason = InputDialog.Ask(FindForm(), "رفض طلب الصرف رقم " + n, "سبب الرفض:", "");
                    if (reason == null) return;
                    Requisitions.Reject(no, reason);
                }
                Reload();
            }
            catch (UserError ue) { UI.Warn(ue.Message); }
            catch (Exception ex) { UI.Error(ex); }
        }

        void EditLimit()
        {
            string cur = Requisitions.ApprovalLimit.ToString("0.####", CultureInfo.InvariantCulture);
            string v = InputDialog.Ask(FindForm(), "حد الاعتماد",
                "الطلبات التي قيمتها (الكمية × آخر كلفة إدخال) أقل من هذا الحد تُعتمد تلقائيا عند إرسالها.\nاكتب 0 ليحتاج كل طلب إلى موافقة:", cur);
            if (v == null) return;
            double d;
            if (!UI.TryNum(v, out d) || d < 0) { UI.Warn("اكتب رقما صحيحا (0 أو أكثر)."); return; }
            try { Requisitions.SetApprovalLimit(d); }
            catch (UserError ue) { UI.Warn(ue.Message); return; }
            help.Text = help.Text.Substring(0, help.Text.IndexOf("(إخراج مخزني)") + "(إخراج مخزني)".Length) + LimitText();
        }

        /// <summary>Home screen chip «N طلبات صرف بانتظار موافقتك» (only for approvers, only when there are any).
        /// Returns true when it was added.</summary>
        public static bool AddAlert(Control host, MainForm main)
        {
            int n = Requisitions.PendingForMe();
            if (n == 0 || host == null) return false;
            AlertChip a = new AlertChip("", Theme.Primary);
            a.Text = n.ToString("#,##0") + "  طلبات صرف بانتظار موافقتك";
            a.Click += delegate { if (main != null) main.Show(new RequisitionsView(main, FilterMine)); };
            host.Controls.Add(a);
            return true;
        }
    }
}
