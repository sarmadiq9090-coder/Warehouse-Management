using System.Collections.Generic;
using System.Data;
using System.Drawing;
using System.Windows.Forms;
using System;

namespace Stores
{
    // ================================================================== write-offs list «سندات الإتلاف / شطب المواد»
    /// <summary>
    /// All write-offs with a status filter; approvers open on «بانتظار اعتمادك» and can approve (post) / reject from the list.
    /// </summary>
    class WriteOffsView : UserControl
    {
        MainForm main;
        ComboBox cmbStatus; TextBox txt; DataGridView grid; Label lblCount, help;
        Button add, open, approve, reject;

        const int FilterAll = -1, FilterMine = -2, FilterOpen = -3;

        public WriteOffsView(MainForm m) : this(m, WriteOffs.PendingForMe() > 0 ? FilterMine : FilterOpen) { }

        public WriteOffsView(MainForm m, int filter)
        {
            main = m;
            BackColor = Theme.Bg;
            Panel filt = UI.Card(); filt.Dock = DockStyle.Top; filt.Height = 64;
            FlowLayoutPanel f = UI.Flow(); f.WrapContents = true;
            cmbStatus = UI.Combo(190);
            List<ComboItem> st = new List<ComboItem>();
            if (WriteOffs.CanApprove) st.Add(new ComboItem(FilterMine, "بانتظار اعتمادك"));
            st.Add(new ComboItem(FilterOpen, "السندات غير المُرحّلة"));
            st.Add(new ComboItem(FilterAll, "كل السندات"));
            foreach (int s in WriteOffs.AllStatuses) st.Add(new ComboItem(s, WriteOffs.StatusName(s)));
            foreach (ComboItem c in st) cmbStatus.Items.Add(c);
            txt = UI.Text(150);
            Button go = UI.BtnLight("عرض", 80); go.Height = 30;
            add = UI.Btn("+ سند إتلاف جديد", Theme.Accent, 150);
            open = UI.Btn("فتح", Theme.Primary, 80);
            approve = UI.Btn("اعتماد وترحيل", Theme.Success, 130);
            reject = UI.Btn("رفض", Theme.Danger, 90);
            RequisitionView.Glyphs(reject, "");
            Button report = UI.BtnLight("تقرير الإتلاف حسب السبب", 190);
            Button xl = UI.BtnLight("تصدير Excel", 110);
            f.Controls.AddRange(new Control[] { add, open, approve, reject, UI.Field("الحالة", cmbStatus), UI.Field("رقم / ملاحظة", txt), go, report, xl });
            filt.Controls.Add(f); UI.FitHeight(filt);
            approve.Visible = reject.Visible = WriteOffs.CanApprove;
            add.Visible = WriteOffs.CanPrepare;
            report.Visible = Session.Can(Session.Reports);

            help = new Label(); help.Dock = DockStyle.Top; help.Height = 28; help.ForeColor = Theme.Muted; help.TextAlign = ContentAlignment.MiddleLeft;
            help.Text = "① أمين المخزن يكتب المواد التالفة أو المفقودة وسببها ويرسلها للاعتماد   ② المعتمد يعتمد السند فتخرج المواد بكلفتها، أو يرفضه";

            Panel gc = UI.Card(); gc.Dock = DockStyle.Fill; gc.Padding = new Padding(1);
            grid = new DataGridView(); grid.Dock = DockStyle.Fill; UI.ReadOnlyGrid(grid);
            grid.Columns.Add(UI.Col("no", "رقم السند", "menu_no", 80, false));
            grid.Columns.Add(UI.Col("d", "التاريخ", "wo_date", 100, false)); grid.Columns["d"].DefaultCellStyle.Format = "yyyy/MM/dd";
            grid.Columns.Add(UI.Col("s", "المخزن", "sname", 130, false));
            grid.Columns.Add(UI.Col("r", "السبب", "rname", 120, false));
            grid.Columns.Add(UI.Col("n", "عدد المواد", "n", 85, true));
            grid.Columns.Add(UI.Col("q", "الكميات", "q", 90, true));
            grid.Columns.Add(UI.Col("v", "الكلفة", "wo_value", 110, true));
            grid.Columns.Add(UI.Col("st", "الحالة", "status_name", 125, false));
            grid.Columns.Add(UI.Col("by", "أعدّه", "user_name", 110, false));
            grid.Columns.Add(UI.Col("ap", "المعتمد", "approved_by", 110, false));
            grid.Columns.Add(UI.Col("note", "ملاحظات", "notes", 0, false));
            int[] minW = { 75, 90, 100, 90, 75, 85, 90, 110, 80, 80, 80 };
            for (int i = 0; i < grid.Columns.Count; i++)
            {
                DataGridViewColumn c = grid.Columns[i];
                c.FillWeight = i == 10 ? 200 : c.Width;
                c.AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill; c.MinimumWidth = minW[i];
            }
            gc.Controls.Add(grid);
            lblCount = new Label(); lblCount.Dock = DockStyle.Bottom; lblCount.Height = 26; lblCount.ForeColor = Theme.Muted; lblCount.TextAlign = ContentAlignment.MiddleLeft;
            Controls.Add(gc); Controls.Add(lblCount); Controls.Add(help); Controls.Add(filt); Controls.Add(ViewHelp.Title("سندات الإتلاف / شطب المواد"));

            if (!WriteOffs.Ready)
            {
                help.ForeColor = Theme.Danger;
                help.Text = "سندات الإتلاف غير مفعّلة في ملف البيانات بعد. " + (Db.SchemaWarning ?? "");
                add.Enabled = open.Enabled = approve.Enabled = reject.Enabled = false;
            }

            UI.SetSel(cmbStatus, filter);
            cmbStatus.SelectedIndexChanged += delegate { Reload(); };
            go.Click += delegate { Reload(); };
            txt.KeyDown += delegate(object s, KeyEventArgs e) { if (e.KeyCode == Keys.Enter) { e.SuppressKeyPress = true; Reload(); } };
            add.Click += delegate { if (main != null) main.Show(new WriteOffView(main, 0)); };
            open.Click += delegate { OpenCurrent(); };
            grid.CellDoubleClick += delegate(object s, DataGridViewCellEventArgs e) { if (e.RowIndex >= 0) OpenCurrent(); };
            grid.KeyDown += delegate(object s, KeyEventArgs e) { if (e.KeyCode == Keys.Enter) { e.Handled = true; OpenCurrent(); } };
            approve.Click += delegate { Decide(true); };
            reject.Click += delegate { Decide(false); };
            report.Click += delegate { if (main != null) main.OpenReport("writeoffs", 0, 0); };
            xl.Click += delegate { Excel.ExportGrid(FindForm(), grid, "سندات الإتلاف"); };
            grid.CellFormatting += delegate(object s, DataGridViewCellFormattingEventArgs e)
            {
                if (e.RowIndex < 0 || grid.Columns[e.ColumnIndex].Name != "st") return;
                DataRowView rv = grid.Rows[e.RowIndex].DataBoundItem as DataRowView;
                if (rv == null) return;
                e.CellStyle.ForeColor = StatusColor(Db.I(rv["status"]));
                e.CellStyle.Font = Theme.FontBold;
            };
            Load += delegate { if (WriteOffs.Ready) Reload(); };
        }

        internal static Color StatusColor(int s)
        {
            switch (s)
            {
                case WriteOffs.Pending: return Theme.Warning;
                case WriteOffs.Posted: return Theme.Success;
                case WriteOffs.Rejected: return Theme.Danger;
                default: return Theme.Muted;
            }
        }

        DataRowView Current { get { return grid.CurrentRow == null ? null : grid.CurrentRow.DataBoundItem as DataRowView; } }

        public void Reload()
        {
            try
            {
                UI.WaitCursor(true);
                int k = UI.Sel(cmbStatus);
                if (cmbStatus.SelectedItem == null) k = FilterOpen;
                DataTable t = WriteOffs.List(k, txt.Text);
                grid.DataSource = t;
                int mine = WriteOffs.PendingForMe();
                lblCount.Text = "عدد السندات: " + t.Rows.Count + (mine > 0 ? "    •    بانتظار اعتمادك: " + mine : "") + "    •    انقر مرتين على السند لفتحه";
            }
            catch (Exception ex) { UI.Error(ex); }
            finally { UI.WaitCursor(false); }
        }

        void OpenCurrent()
        {
            DataRowView rv = Current;
            if (rv == null || main == null) return;
            main.Show(new WriteOffView(main, Db.I(rv["wo_no"])));
        }

        void Decide(bool ok)
        {
            DataRowView rv = Current;
            if (rv == null) { UI.Warn("اختر سندا من القائمة أولا."); return; }
            int no = Db.I(rv["wo_no"]); string n = Db.S(rv["menu_no"]);
            try
            {
                if (ok)
                {
                    if (!UI.Confirm("اعتماد سند الإتلاف رقم " + n + " (" + Db.S(rv["rname"]) + ")؟\nستخرج مواده من " + Db.S(rv["sname"]) + " بكلفتها ولا يمكن التراجع.")) return;
                    WriteOffs.Approve(no);
                }
                else
                {
                    string reason = InputDialog.Ask(FindForm(), "رفض سند الإتلاف رقم " + n, "سبب الرفض:", "");
                    if (reason == null) return;
                    WriteOffs.Reject(no, reason);
                }
                Cache.LoadItems();
                Reload();
            }
            catch (UserError ue) { UI.Warn(ue.Message); }
            catch (Exception ex) { UI.Error(ex); }
        }

        /// <summary>Home screen chip «N سندات إتلاف بانتظار اعتمادك» (only for approvers, only when there are any).
        /// Returns true when it was added.</summary>
        public static bool AddAlert(Control host, MainForm main)
        {
            int n = WriteOffs.PendingForMe();
            if (n == 0 || host == null) return false;
            AlertChip a = new AlertChip("", Theme.Danger);
            a.Text = n.ToString("#,##0") + "  سندات إتلاف بانتظار اعتمادك";
            a.Click += delegate { if (main != null) main.Show(new WriteOffsView(main, FilterMine)); };
            host.Controls.Add(a);
            return true;
        }
    }
}
