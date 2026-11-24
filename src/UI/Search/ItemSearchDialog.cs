using System.Collections.Generic;
using System.Data;
using System.Diagnostics;
using System.Drawing;
using System.Windows.Forms;
using System;

namespace Stores
{
    /// <summary>Instant item search window: type a code or any part of the name; Enter chooses.</summary>
    class ItemSearchDialog : BaseDialog
    {
        TextBox txt; DataGridView grid; Label info;
        List<int> results = new List<int>();
        bool includeFrozen;
        public int SelectedId;
        public Func<int> AddNew;

        public ItemSearchDialog(string initial, bool frozen)
        {
            includeFrozen = frozen;
            Text = "بحث سريع عن مادة";
            Size = new Size(960, 600);
            FormBorderStyle = FormBorderStyle.Sizable;
            MinimumSize = new Size(640, 420);

            Panel top = new Panel(); top.Dock = DockStyle.Top; top.Height = 70; top.Padding = new Padding(14, 14, 14, 10); top.BackColor = Theme.Primary;
            txt = new TextBox(); txt.Dock = DockStyle.Fill; txt.Font = new Font("Segoe UI", 15f); txt.BorderStyle = BorderStyle.None;
            Panel box = new Panel(); box.Dock = DockStyle.Fill; box.BackColor = Theme.Input; box.Padding = new Padding(10, 7, 10, 4);
            box.Controls.Add(txt);
            Label icon = new Label(); icon.Text = ""; icon.Font = new Font("Segoe MDL2 Assets", 16f); icon.ForeColor = Color.White; icon.Dock = DockStyle.Right; icon.Width = 40;
            icon.TextAlign = ContentAlignment.MiddleCenter;
            top.Controls.Add(box); top.Controls.Add(icon);

            info = new Label(); info.Dock = DockStyle.Top; info.Height = 26; info.ForeColor = Theme.Muted; info.Padding = new Padding(8, 4, 8, 0);
            info.TextAlign = ContentAlignment.MiddleLeft;

            grid = new DataGridView(); grid.Dock = DockStyle.Fill;
            UI.ReadOnlyGrid(grid);
            grid.VirtualMode = true;
            grid.Columns.Add(UI.Col("code", "الرمز", null, 150, false));
            grid.Columns.Add(UI.Col("name", "اسم المادة", null, 0, false));
            grid.Columns.Add(UI.Col("type", "النوع", null, 170, false));
            grid.Columns.Add(UI.Col("unit", "الوحدة", null, 80, false));
            grid.Columns.Add(UI.Col("bal", "الرصيد الكلي", null, 120, true));
            foreach (DataGridViewColumn c in grid.Columns) c.SortMode = DataGridViewColumnSortMode.NotSortable;
            grid.RowTemplate.Height = 32;
            grid.CellValueNeeded += Grid_CellValueNeeded;
            grid.CellFormatting += delegate(object s, DataGridViewCellFormattingEventArgs e)
            {
                if (e.RowIndex < 0 || e.RowIndex >= results.Count) return;
                DataRow r = Cache.Item(results[e.RowIndex]);
                if (r == null) return;
                if (Db.B(r["freez"])) e.CellStyle.ForeColor = Theme.Muted;
                else if (grid.Columns[e.ColumnIndex].Name == "bal")
                {
                    // green = in stock, grey = zero, red only for a negative balance (needs checking)
                    double b = Db.D(r["bal"]);
                    e.CellStyle.ForeColor = b > 0.0001 ? Theme.AccentDark : b < -0.0001 ? Theme.Danger : Theme.Muted;
                }
            };

            Button ok = UI.Btn("اختيار  (Enter)", Theme.Primary, 140);
            Button cancel = UI.BtnLight("إلغاء  (Esc)", 110);
            Button add = UI.Btn("+ مادة جديدة", Theme.Accent, 130);
            Label hint = UI.Lbl("اكتب الرمز أو أي جزء من الاسم • الأسهم للتنقل");
            FlowLayoutPanel bar = ButtonBar(ok, cancel, add);
            bar.Controls.Add(hint);

            Controls.Add(grid); Controls.Add(info); Controls.Add(top); Controls.Add(bar);

            txt.TextChanged += delegate { Run(); };
            txt.KeyDown += delegate(object s, KeyEventArgs e)
            {
                if (e.KeyCode == Keys.Down || e.KeyCode == Keys.Up || e.KeyCode == Keys.PageDown || e.KeyCode == Keys.PageUp)
                {
                    if (results.Count == 0) return;
                    int i = grid.CurrentCell == null ? 0 : grid.CurrentCell.RowIndex;
                    int step = e.KeyCode == Keys.PageDown || e.KeyCode == Keys.PageUp ? 10 : 1;
                    i += (e.KeyCode == Keys.Down || e.KeyCode == Keys.PageDown) ? step : -step;
                    i = Math.Max(0, Math.Min(results.Count - 1, i));
                    grid.CurrentCell = grid.Rows[i].Cells[1];
                    e.Handled = true;
                }
                else if (e.KeyCode == Keys.Enter) { e.Handled = true; e.SuppressKeyPress = true; Choose(); }
            };
            grid.CellDoubleClick += delegate(object s, DataGridViewCellEventArgs e) { if (e.RowIndex >= 0) Choose(); };
            grid.KeyDown += delegate(object s, KeyEventArgs e)
            {
                if (e.KeyCode == Keys.Enter) { e.Handled = true; Choose(); }
                else if (!e.Control && !e.Alt && (char.IsLetterOrDigit((char)e.KeyValue) || e.KeyCode == Keys.Back)) { txt.Focus(); }
            };
            ok.Click += delegate { Choose(); };
            cancel.Click += delegate { DialogResult = DialogResult.Cancel; Close(); };
            add.Click += delegate
            {
                if (AddNew == null) return;
                int id = AddNew();
                if (id > 0) { ItemIndex.Invalidate(); SelectedId = id; DialogResult = DialogResult.OK; Close(); }
            };
            Shown += delegate
            {
                add.Visible = AddNew != null;
                txt.Text = initial ?? "";
                txt.SelectionStart = txt.Text.Length;
                txt.Focus();
                Run();
            };
        }

        void Run()
        {
            Stopwatch sw = Stopwatch.StartNew();
            int total;
            results = ItemIndex.Search(txt.Text, includeFrozen, 500, out total);
            sw.Stop();
            grid.RowCount = 0;
            grid.RowCount = results.Count;
            if (results.Count > 0) grid.CurrentCell = grid.Rows[0].Cells[1];
            grid.Invalidate();
            info.Text = (txt.Text.Trim().Length == 0 ? "كل المواد: " : "النتائج: ") + total.ToString("#,##0") +
                        (total > results.Count ? "  (يُعرض أفضل " + results.Count + ")" : "") + "   •   " + sw.ElapsedMilliseconds + " ms";
        }

        void Grid_CellValueNeeded(object sender, DataGridViewCellValueEventArgs e)
        {
            if (e.RowIndex < 0 || e.RowIndex >= results.Count) return;
            DataRow r = Cache.Item(results[e.RowIndex]);
            if (r == null) return;
            switch (grid.Columns[e.ColumnIndex].Name)
            {
                case "code": e.Value = Db.S(r["item_code"]); break;
                case "name": e.Value = Db.S(r["item_name"]) + (Db.B(r["freez"]) ? "  (مجمدة)" : ""); break;
                case "type": e.Value = Db.S(r["type_name"]); break;
                case "unit": e.Value = Db.S(r["unit"]); break;
                case "bal": e.Value = Db.D(r["bal"]); break;
            }
        }

        void Choose()
        {
            if (grid.CurrentCell == null || grid.CurrentCell.RowIndex >= results.Count) return;
            SelectedId = results[grid.CurrentCell.RowIndex];
            DialogResult = DialogResult.OK;
            Close();
        }
    }
}
