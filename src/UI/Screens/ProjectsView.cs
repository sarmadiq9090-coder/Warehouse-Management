using System.Collections.Generic;
using System.Data;
using System.Drawing;
using System.Windows.Forms;
using System;

namespace Stores
{
    // ================================================================== cost centres («مراكز الكلفة», table project)
    class ProjectsView : UserControl
    {
        TextBox txt; ComboBox cmbType; CheckBox chkClosed; DataGridView grid; DataView view; Label lblCount; Button close;

        public ProjectsView()
        {
            BackColor = Theme.Bg;
            Panel filt = UI.Card(); filt.Dock = DockStyle.Top; filt.Height = 64;
            FlowLayoutPanel f = UI.Flow(); f.WrapContents = true;
            txt = UI.Text(180);
            cmbType = UI.Combo(130);
            cmbType.Items.Add(new ComboItem(0, "كل الأنواع")); cmbType.Items.AddRange(CostCentres.TypeItems().ToArray()); cmbType.SelectedIndex = 0;
            chkClosed = new CheckBox(); chkClosed.Text = "إظهار المغلقة"; chkClosed.AutoSize = true; chkClosed.Margin = new Padding(8, 10, 8, 0);
            Button add = UI.Btn("+ مركز كلفة جديد", Theme.Accent, 150);
            Button edit = UI.Btn("تعديل", Theme.Primary, 90);
            close = UI.Btn("إغلاق", Theme.Warning, 100);
            Button stmt = UI.Btn("كشف مركز الكلفة", Theme.Teal, 140);
            Button resv = UI.Btn("حجز مواد", Theme.Purple, 110);
            bool can = Session.Can(Session.Persons);
            add.Visible = edit.Visible = close.Visible = can; stmt.Visible = Session.Can(Session.Reports); resv.Visible = Session.Can(Session.IssAdd);
            f.Controls.AddRange(new Control[] { UI.Field("بحث", txt), UI.Field("النوع", cmbType), chkClosed, add, edit, close, stmt, resv });
            filt.Controls.Add(f); UI.FitHeight(filt);

            Label help = new Label(); help.Dock = DockStyle.Top; help.Height = 28; help.ForeColor = Theme.Muted;
            help.Text = "مركز الكلفة هو الجهة التي تُحمّل عليها المواد المُخرجة: مشروع أو قسم أو صيانة أو إدارة. يُختار في سند الإخراج، ويمكن حجز مواد له.";

            Panel gc = UI.Card(); gc.Dock = DockStyle.Fill; gc.Padding = new Padding(1);
            grid = new DataGridView(); grid.Dock = DockStyle.Fill; UI.ReadOnlyGrid(grid);
            grid.Columns.Add(UI.Col("code", "الرمز", "proj_code", 90, false));
            grid.Columns.Add(UI.Col("name", "اسم مركز الكلفة", "proj_name", 0, false));
            grid.Columns.Add(UI.Col("type", "النوع", "type_name", 80, false));
            grid.Columns.Add(UI.Col("status", "الحالة", "status_name", 70, false));
            grid.Columns.Add(UI.Col("client", "الجهة المستفيدة", "client", 150, false));
            grid.Columns.Add(UI.Col("docs", "سندات الإخراج", "t_docs", 115, true));
            grid.Columns.Add(UI.Col("net", "صافي الكميات", "t_net", 115, true));
            grid.Columns.Add(UI.Col("cost", "الكلفة", "t_cost", 110, true));
            grid.Columns.Add(UI.Col("notes", "ملاحظات", "notes", 140, false));
            gc.Controls.Add(grid);
            lblCount = new Label(); lblCount.Dock = DockStyle.Bottom; lblCount.Height = 26; lblCount.ForeColor = Theme.Muted; lblCount.TextAlign = ContentAlignment.MiddleLeft;
            Controls.Add(gc); Controls.Add(lblCount); Controls.Add(help); Controls.Add(filt); Controls.Add(ViewHelp.Title("مراكز الكلفة"));

            if (!Db.HasProj)
            {
                help.ForeColor = Theme.Danger;
                help.Text = "ميزة مراكز الكلفة غير مفعّلة بعد: يجب إغلاق البرنامج القديم على كل الأجهزة ثم فتح هذا البرنامج مرة واحدة. " + (Db.SchemaWarning ?? "");
                add.Enabled = false;
            }

            txt.TextChanged += delegate { ApplyFilter(); };
            cmbType.SelectedIndexChanged += delegate { ApplyFilter(); };
            chkClosed.CheckedChanged += delegate { ApplyFilter(); };
            add.Click += delegate { if (ProjectDialog.AddNew(FindForm(), txt.Text.Trim()) > 0) Rebind(); };
            edit.Click += delegate { Edit(); };
            close.Click += delegate { ToggleClosed(); };
            grid.CellDoubleClick += delegate(object s, DataGridViewCellEventArgs e) { if (e.RowIndex >= 0 && can) Edit(); };
            grid.SelectionChanged += delegate { UpdateClose(); };
            stmt.Click += delegate { int id = CurrentId(); if (id == 0) { UI.Warn("اختر سطرا من القائمة أولا."); return; } MainForm mf = FindForm() as MainForm; if (mf != null) mf.OpenReport("project", 0, 0, id); };
            resv.Click += delegate { int id = CurrentId(); MainForm mf = FindForm() as MainForm; if (mf != null) mf.OpenDoc(DocKind.Reserve, 0, id); };
            Load += delegate { Rebind(); txt.Focus(); };
        }

        int CurrentId()
        {
            if (grid.CurrentRow == null) return 0;
            DataRowView rv = grid.CurrentRow.DataBoundItem as DataRowView;
            return rv == null ? 0 : Db.I(rv["proj_no"]);
        }

        /// <summary>Cost centres with their totals since the beginning (issue documents, net quantity, FIFO cost).</summary>
        public static DataTable WithTotals()
        {
            DataTable t = Cache.Projects.Copy();
            t.Columns.Add("t_docs", typeof(double)); t.Columns.Add("t_net", typeof(double)); t.Columns.Add("t_cost", typeof(double));
            Dictionary<int, double[]> tot = new Dictionary<int, double[]>();
            try { tot = CostCentres.Totals(new DateTime(1900, 1, 1), DateTime.Today.AddYears(10), 0); } catch { }
            foreach (DataRow r in t.Rows)
            {
                double[] v;
                if (!tot.TryGetValue(Db.I(r["proj_no"]), out v)) continue;
                r["t_docs"] = v[0]; r["t_net"] = v[1] - v[2]; r["t_cost"] = v[3];
            }
            t.AcceptChanges();
            return t;
        }

        void Rebind()
        {
            UI.WaitCursor(true);
            try { view = new DataView(WithTotals()); view.Sort = "proj_name"; grid.DataSource = view; ApplyFilter(); }
            finally { UI.WaitCursor(false); }
        }

        void ApplyFilter()
        {
            if (view == null) return;
            List<string> parts = new List<string>();
            string s = UI.BuildFilter(txt.Text, "_s"); if (s.Length > 0) parts.Add(s);
            int ty = UI.Sel(cmbType); if (ty > 0) parts.Add("cc_type = " + ty);
            if (!chkClosed.Checked) parts.Add("(status IS NULL OR status <> 2)");
            try { view.RowFilter = string.Join(" AND ", parts.ToArray()); } catch { view.RowFilter = ""; }
            lblCount.Text = "عدد مراكز الكلفة: " + view.Count;
            UpdateClose();
        }

        void UpdateClose()
        {
            if (grid.CurrentRow == null) return;
            DataRowView rv = grid.CurrentRow.DataBoundItem as DataRowView;
            close.Text = rv != null && Db.I(rv["status"]) == 2 ? "إعادة فتح" : "إغلاق";
        }

        void ToggleClosed()
        {
            if (!Session.Demand(Session.Persons)) return;
            int id = CurrentId(); if (id == 0) { UI.Warn("اختر سطرا من القائمة أولا."); return; }
            DataRow r = CostCentres.Row(id);
            bool closed = r != null && Db.I(r["status"]) == 2;
            if (!closed && !UI.Confirm("إغلاق مركز الكلفة «" + Cache.Project(id) + "»؟\nلن يظهر في قائمة سندات الإخراج الجديدة، وتبقى سنداته وكشفه كما هي.")) return;
            try { CostCentres.SetClosed(id, !closed); Rebind(); }
            catch (Exception ex) { UI.Error(ex); }
        }

        void Edit()
        {
            int id = CurrentId(); if (id == 0) { UI.Warn("اختر سطرا من القائمة أولا."); return; }
            using (ProjectDialog d = new ProjectDialog(id)) if (d.ShowDialog(FindForm()) == DialogResult.OK) Rebind();
        }
    }
}
