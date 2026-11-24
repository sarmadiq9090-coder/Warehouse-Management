using System.Collections.Generic;
using System.Data;
using System.Drawing.Drawing2D;
using System.Drawing;
using System.IO;
using System.Windows.Forms;
using System;

namespace Stores
{
    // ------------------------------------------------------------------ search dialog
    /// <summary>Find a row by typing any part of the name or code (Arabic letters normalised).</summary>
    class SearchDialog : BaseDialog
    {
        TextBox txt;
        DataGridView grid;
        DataView view;
        string idCol;
        Label count;
        public int SelectedId;
        public Func<int> AddNew;

        public SearchDialog(string title, DataTable data, string idColumn, string[] cols, string[] headers, int[] widths, string initial)
        {
            Text = title;
            idCol = idColumn;
            Size = new Size(860, 560);
            FormBorderStyle = FormBorderStyle.Sizable;
            MinimumSize = new Size(600, 400);

            Panel top = new Panel(); top.Dock = DockStyle.Top; top.Height = 56; top.Padding = new Padding(12, 12, 12, 8); top.BackColor = Theme.Card;
            txt = UI.Text(400); txt.Dock = DockStyle.Fill; txt.Font = new Font("Segoe UI", 12f);
            Label l = UI.Lbl("ابحث:"); l.Dock = DockStyle.Right; l.Margin = new Padding(0);
            l.Padding = new Padding(6, 6, 6, 0);
            top.Controls.Add(txt); top.Controls.Add(l);

            grid = new DataGridView();
            grid.Dock = DockStyle.Fill;
            UI.ReadOnlyGrid(grid);
            for (int i = 0; i < cols.Length; i++)
            {
                bool num = data.Columns[cols[i]].DataType == typeof(double) || data.Columns[cols[i]].DataType == typeof(decimal) || data.Columns[cols[i]].DataType == typeof(float);
                grid.Columns.Add(UI.Col(cols[i], headers[i], cols[i], widths[i], num));
            }
            view = new DataView(data);
            grid.DataSource = view;

            count = UI.Lbl(""); count.Dock = DockStyle.Fill;
            Button ok = UI.Btn("اختيار", Theme.Primary, 110);
            Button cancel = UI.BtnLight("إلغاء", 90);
            Button add = UI.Btn("+ إضافة جديد", Theme.Accent, 130);
            FlowLayoutPanel bar = ButtonBar(ok, cancel, add);
            bar.Controls.Add(count);
            add.Visible = false;

            Controls.Add(grid); Controls.Add(top); Controls.Add(bar);

            txt.TextChanged += delegate { ApplyFilter(); };
            txt.KeyDown += delegate(object s, KeyEventArgs e)
            {
                if (e.KeyCode == Keys.Down || e.KeyCode == Keys.Up)
                {
                    if (grid.Rows.Count == 0) return;
                    int i = grid.CurrentRow == null ? 0 : grid.CurrentRow.Index + (e.KeyCode == Keys.Down ? 1 : -1);
                    i = Math.Max(0, Math.Min(grid.Rows.Count - 1, i));
                    grid.CurrentCell = grid.Rows[i].Cells[0];
                    e.Handled = true;
                }
                else if (e.KeyCode == Keys.Enter) { e.Handled = true; e.SuppressKeyPress = true; Choose(); }
            };
            grid.CellDoubleClick += delegate(object s, DataGridViewCellEventArgs e) { if (e.RowIndex >= 0) Choose(); };
            grid.KeyDown += delegate(object s, KeyEventArgs e) { if (e.KeyCode == Keys.Enter) { e.Handled = true; Choose(); } };
            ok.Click += delegate { Choose(); };
            cancel.Click += delegate { DialogResult = DialogResult.Cancel; Close(); };
            add.Click += delegate
            {
                if (AddNew == null) return;
                int id = AddNew();
                if (id > 0) { SelectedId = id; DialogResult = DialogResult.OK; Close(); }
            };
            Shown += delegate { add.Visible = AddNew != null; txt.Text = initial ?? ""; txt.SelectionStart = txt.Text.Length; txt.Focus(); ApplyFilter(); };
        }

        void ApplyFilter()
        {
            try
            {
                view.RowFilter = view.Table.Columns.Contains("_s") ? UI.BuildFilter(txt.Text, "_s") : "";
            }
            catch { view.RowFilter = ""; }
            count.Text = "العدد: " + view.Count;
        }

        void Choose()
        {
            if (grid.CurrentRow == null) return;
            DataRowView rv = grid.CurrentRow.DataBoundItem as DataRowView;
            if (rv == null) return;
            SelectedId = Db.I(rv[idCol]);
            DialogResult = DialogResult.OK;
            Close();
        }

        // --- ready made searches
        public static int PickItem(IWin32Window owner, string initial, bool includeFrozen)
        {
            using (ItemSearchDialog d = new ItemSearchDialog(initial, includeFrozen))
            {
                if (Session.Can(Session.Items))
                    d.AddNew = delegate { return ItemDialog.AddNew(d, initial); };
                return d.ShowDialog(owner) == DialogResult.OK ? d.SelectedId : 0;
            }
        }

        public static int PickPerson(IWin32Window owner, string initial)
        {
            using (SearchDialog d = new SearchDialog("بحث عن جهة / شخص", Cache.Persons, "p_no",
                new string[] { "p_name", "type_name", "p_tel" },
                new string[] { "الاسم", "النوع", "الهاتف" },
                new int[] { 0, 130, 140 }, initial))
            {
                if (Session.Can(Session.Persons))
                    d.AddNew = delegate { return PersonDialog.AddNew(d, initial); };
                return d.ShowDialog(owner) == DialogResult.OK ? d.SelectedId : 0;
            }
        }
    }
}
