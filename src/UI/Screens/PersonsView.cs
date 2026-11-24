using System.Collections.Generic;
using System.Data.Common;
using System.Data;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Windows.Forms;
using System;

namespace Stores
{
    // ================================================================== persons
    class PersonsView : UserControl
    {
        TextBox txt; DataGridView grid; DataView view; Label lblCount;

        public PersonsView()
        {
            BackColor = Theme.Bg;
            Panel filt = UI.Card(); filt.Dock = DockStyle.Top; filt.Height = 64;
            FlowLayoutPanel f = UI.Flow(); f.WrapContents = true;
            txt = UI.Text(300);
            Button add = UI.Btn("+ جهة جديدة", Theme.Accent, 130);
            Button edit = UI.Btn("تعديل", Theme.Primary, 100);
            Button stmt = UI.Btn("كشف الجهة", Theme.Teal, 120);
            Button del = UI.Btn("حذف", Theme.Danger, 90);
            Button xl = UI.BtnLight("تصدير Excel", 110);
            bool can = Session.Can(Session.Persons);
            add.Visible = edit.Visible = del.Visible = can; stmt.Visible = Session.Can(Session.Reports);
            f.Controls.AddRange(new Control[] { UI.Lbl("بحث"), txt, add, edit, stmt, del, xl });
            filt.Controls.Add(f); UI.FitHeight(filt);

            Panel gc = UI.Card(); gc.Dock = DockStyle.Fill; gc.Padding = new Padding(1);
            grid = new DataGridView(); grid.Dock = DockStyle.Fill; UI.ReadOnlyGrid(grid);
            grid.Columns.Add(UI.Col("name", "الاسم", "p_name", 0, false));
            grid.Columns.Add(UI.Col("type", "النوع", "type_name", 120, false));
            grid.Columns.Add(UI.Col("tel", "الهاتف", "p_tel", 130, false));
            grid.Columns.Add(UI.Col("addr", "العنوان", "p_addrs", 0, false));
            grid.Columns.Add(UI.Col("note", "ملاحظات", "p_note", 0, false));
            // the name takes most of the width; address and notes share the rest
            grid.Columns["name"].FillWeight = 300; grid.Columns["name"].MinimumWidth = 220;
            grid.Columns["addr"].FillWeight = 110; grid.Columns["addr"].MinimumWidth = 100;
            grid.Columns["note"].FillWeight = 100; grid.Columns["note"].MinimumWidth = 80;
            gc.Controls.Add(grid);
            lblCount = new Label(); lblCount.Dock = DockStyle.Bottom; lblCount.Height = 26; lblCount.ForeColor = Theme.Muted; lblCount.TextAlign = ContentAlignment.MiddleLeft;
            Controls.Add(gc); Controls.Add(lblCount); Controls.Add(filt); Controls.Add(ViewHelp.Title("الجهات والأشخاص"));

            Timer debounce = new Timer(); debounce.Interval = 250;
            debounce.Tick += delegate { debounce.Stop(); ApplyFilter(); };
            txt.TextChanged += delegate { debounce.Stop(); debounce.Start(); };
            add.Click += delegate { if (PersonDialog.AddNew(FindForm(), txt.Text.Trim()) > 0) Rebind(); };
            edit.Click += delegate { Edit(); };
            grid.CellDoubleClick += delegate(object s, DataGridViewCellEventArgs e) { if (e.RowIndex >= 0 && can) Edit(); };
            stmt.Click += delegate
            {
                int id = CurrentId(); if (id == 0) { UI.Warn("اختر سطرا من القائمة أولا."); return; }
                MainForm mf = FindForm() as MainForm; if (mf != null) mf.OpenReport("person", 0, id);
            };
            del.Click += delegate { Delete(); };
            xl.Click += delegate { Excel.ExportGrid(FindForm(), grid, "الجهات"); };
            Load += delegate { Rebind(); txt.Focus(); };
        }

        int CurrentId()
        {
            if (grid.CurrentRow == null) return 0;
            DataRowView rv = grid.CurrentRow.DataBoundItem as DataRowView;
            return rv == null ? 0 : Db.I(rv["p_no"]);
        }

        void Rebind() { view = new DataView(Cache.Persons); view.Sort = "p_name"; grid.DataSource = view; ApplyFilter(); }

        void ApplyFilter()
        {
            if (view == null) return;
            try { view.RowFilter = UI.BuildFilter(txt.Text, "_s"); } catch { view.RowFilter = ""; }
            lblCount.Text = "العدد: " + view.Count.ToString("#,##0");
        }

        void Edit()
        {
            int id = CurrentId(); if (id == 0) { UI.Warn("اختر سطرا من القائمة أولا."); return; }
            using (PersonDialog d = new PersonDialog(id)) if (d.ShowDialog(FindForm()) == DialogResult.OK) Rebind();
        }

        void Delete()
        {
            int id = CurrentId(); if (id == 0 || !Session.Demand(Session.Persons)) return;
            int used = Db.I(Db.Scalar("SELECT COUNT(*) FROM sale WHERE custom_no = ?", id)) + Db.I(Db.Scalar("SELECT COUNT(*) FROM shop WHERE custom_no = ?", id));
            if (used > 0) { UI.Warn("لا يمكن الحذف: لهذه الجهة " + used + " سند."); return; }
            if (!UI.Confirm("حذف: " + Cache.Person(id) + " ؟")) return;
            Db.Exec("DELETE FROM person WHERE p_no = ?", id);
            Db.Log(null, "حذف جهة", Cache.Person(id));
            Cache.LoadPersons(); Rebind();
        }
    }
}
