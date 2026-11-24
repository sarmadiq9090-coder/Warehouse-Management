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
    // ================================================================== items
    class ItemsView : UserControl
    {
        MainForm main;
        TextBox txt; ComboBox cmbType; CheckBox chkBal, chkFrozen; DataGridView grid; Label lblCount;
        List<int> rows = new List<int>();

        public ItemsView(MainForm m)
        {
            main = m;
            BackColor = Theme.Bg;
            Panel filt = UI.Card(); filt.Dock = DockStyle.Top; filt.Height = 64;
            FlowLayoutPanel f = UI.Flow(); f.WrapContents = true;
            txt = UI.Text(320); txt.Font = new Font("Segoe UI", 12f);
            cmbType = UI.Combo(180); UI.Fill(cmbType, Cache.Types, "type_no", "type_name", "كل الأنواع");
            chkBal = new CheckBox(); chkBal.Text = "لها رصيد فقط"; chkBal.AutoSize = true; chkBal.Margin = new Padding(8, 10, 8, 0);
            chkFrozen = new CheckBox(); chkFrozen.Text = "إظهار المجمدة"; chkFrozen.AutoSize = true; chkFrozen.Margin = new Padding(8, 10, 8, 0);
            f.Controls.AddRange(new Control[] { UI.Field("بحث فوري بالرمز أو الاسم", txt), UI.Field("النوع", cmbType), chkBal, chkFrozen });
            filt.Controls.Add(f); UI.FitHeight(filt);

            FlowLayoutPanel bar = UI.Flow();
            Button add = UI.Btn("+ مادة جديدة", Theme.Accent, 140);
            Button edit = UI.Btn("تعديل", Theme.Primary, 100);
            Button card = UI.Btn("كارت المادة", Theme.Teal, 120);
            Button del = UI.Btn("حذف", Theme.Danger, 90);
            Button refresh = UI.BtnLight("تحديث", 90);
            Button xl = UI.BtnLight("تصدير Excel", 110);
            bool canEdit = Session.Can(Session.Items);
            add.Visible = edit.Visible = del.Visible = canEdit;
            del.Visible = canEdit && Session.Advanced;
            card.Visible = Session.Can(Session.Reports);
            bar.Controls.AddRange(new Control[] { add, edit, card, del, refresh, xl });

            Panel gc = UI.Card(); gc.Dock = DockStyle.Fill; gc.Padding = new Padding(1);
            grid = new DataGridView(); grid.Dock = DockStyle.Fill; UI.ReadOnlyGrid(grid);
            grid.VirtualMode = true;
            grid.Columns.Add(UI.Col("code", "الرمز", null, 120, false));
            grid.Columns.Add(UI.Col("name", "اسم المادة", null, 0, false));
            grid.Columns.Add(UI.Col("type", "النوع", null, 0, false));
            grid.Columns.Add(UI.Col("group", "المجموعة", null, 90, false));
            grid.Columns.Add(UI.Col("unit", "الوحدة", null, 70, false));
            grid.Columns.Add(UI.Col("bal", "الرصيد الكلي", null, 105, true));
            grid.Columns.Add(UI.Col("ret", "حد إعادة الطلب", null, 120, true));
            // the item name gets most of the free width (type shares a little of it)
            grid.Columns["name"].FillWeight = 300; grid.Columns["name"].MinimumWidth = 220;
            grid.Columns["type"].FillWeight = 100; grid.Columns["type"].MinimumWidth = 140;
            foreach (DataGridViewColumn c in grid.Columns) c.SortMode = DataGridViewColumnSortMode.NotSortable;
            gc.Controls.Add(grid);
            lblCount = new Label(); lblCount.Dock = DockStyle.Bottom; lblCount.Height = 26; lblCount.ForeColor = Theme.Muted; lblCount.TextAlign = ContentAlignment.MiddleLeft;

            Controls.Add(gc); Controls.Add(lblCount); Controls.Add(bar); Controls.Add(filt); Controls.Add(ViewHelp.Title("بطاقة مادة"));

            txt.TextChanged += delegate { ApplyFilter(); };
            txt.KeyDown += delegate(object s, KeyEventArgs e)
            {
                if ((e.KeyCode == Keys.Down || e.KeyCode == Keys.Up) && rows.Count > 0)
                {
                    int i = grid.CurrentCell == null ? 0 : grid.CurrentCell.RowIndex + (e.KeyCode == Keys.Down ? 1 : -1);
                    grid.CurrentCell = grid.Rows[Math.Max(0, Math.Min(rows.Count - 1, i))].Cells[1];
                    e.Handled = true;
                }
                else if (e.KeyCode == Keys.Enter) { e.SuppressKeyPress = true; if (canEdit) Edit(); else Card(); }
            };
            cmbType.SelectedIndexChanged += delegate { ApplyFilter(); };
            chkBal.CheckedChanged += delegate { ApplyFilter(); };
            chkFrozen.CheckedChanged += delegate { ApplyFilter(); };
            grid.CellValueNeeded += delegate(object s, DataGridViewCellValueEventArgs e)
            {
                if (e.RowIndex < 0 || e.RowIndex >= rows.Count) return;
                DataRow r = Cache.Item(rows[e.RowIndex]); if (r == null) return;
                switch (grid.Columns[e.ColumnIndex].Name)
                {
                    case "code": e.Value = Db.S(r["item_code"]); break;
                    case "name": e.Value = Db.S(r["item_name"]); break;
                    case "type": e.Value = Db.S(r["type_name"]); break;
                    case "group": e.Value = Db.S(r["group_name"]); break;
                    case "unit": e.Value = Db.S(r["unit"]); break;
                    case "bal": e.Value = Db.D(r["bal"]); break;
                    case "ret": e.Value = Db.D(r["item_bal_ret"]); break;
                }
            };
            grid.CellFormatting += delegate(object s, DataGridViewCellFormattingEventArgs e)
            {
                if (e.RowIndex < 0 || e.RowIndex >= rows.Count) return;
                DataRow r = Cache.Item(rows[e.RowIndex]); if (r == null) return;
                if (Db.B(r["freez"])) e.CellStyle.ForeColor = Theme.Muted;
                else if (Db.D(r["bal"]) < -0.0001) e.CellStyle.ForeColor = Theme.Danger;
            };
            add.Click += delegate { int id = ItemDialog.AddNew(FindForm(), txt.Text.Trim()); if (id > 0) Rebind(id); };
            edit.Click += delegate { Edit(); };
            grid.CellDoubleClick += delegate(object s, DataGridViewCellEventArgs e) { if (e.RowIndex >= 0) { if (canEdit) Edit(); else Card(); } };
            card.Click += delegate { Card(); };
            del.Click += delegate { Delete(); };
            refresh.Click += delegate { UI.WaitCursor(true); try { Cache.LoadAll(); ItemIndex.Invalidate(); Rebind(0); } finally { UI.WaitCursor(false); } };
            xl.Click += delegate { Export(); };
            Load += delegate { Rebind(0); txt.Focus(); };
        }

        int CurrentId()
        {
            if (grid.CurrentCell != null && grid.CurrentCell.RowIndex < rows.Count) return rows[grid.CurrentCell.RowIndex];
            if (grid.SelectedRows.Count > 0 && grid.SelectedRows[0].Index < rows.Count) return rows[grid.SelectedRows[0].Index];
            return 0;
        }

        void Rebind(int selectId)
        {
            ItemIndex.Invalidate();
            ApplyFilter();
            int i = rows.IndexOf(selectId);
            if (i >= 0) grid.CurrentCell = grid.Rows[i].Cells[1];
        }

        void ApplyFilter()
        {
            int total;
            List<int> found = ItemIndex.Search(txt.Text, chkFrozen.Checked, int.MaxValue, out total);
            int t = UI.Sel(cmbType);
            List<int> list = new List<int>(found.Count);
            foreach (int id in found)
            {
                DataRow r = Cache.Item(id); if (r == null) continue;
                if (t > 0 && Db.I(r["type"]) != t) continue;
                if (chkBal.Checked && Db.D(r["bal"]) <= 0.0001) continue;
                list.Add(id);
            }
            rows = list;
            grid.RowCount = 0;
            grid.RowCount = rows.Count;
            grid.Invalidate();
            lblCount.Text = "عدد المواد: " + rows.Count.ToString("#,##0");
        }

        void Export()
        {
            List<object[]> data = new List<object[]>();
            foreach (int id in rows)
            {
                DataRow r = Cache.Item(id); if (r == null) continue;
                data.Add(new object[] { Db.S(r["item_code"]), Db.S(r["item_name"]), Db.S(r["type_name"]), Db.S(r["group_name"]), Db.S(r["unit"]), Db.D(r["bal"]), Db.D(r["item_bal_ret"]) });
            }
            ReportData rd = new ReportData(); rd.Title = "المواد"; rd.Subtitle = "";
            rd.Col("الرمز", ColKind.Text); rd.Col("اسم المادة", ColKind.Text); rd.Col("النوع", ColKind.Text); rd.Col("المجموعة", ColKind.Text); rd.Col("الوحدة", ColKind.Text); rd.Col("الرصيد الكلي", ColKind.Num); rd.Col("حد إعادة الطلب", ColKind.Num);
            foreach (object[] o in data) rd.Row(null, o);
            Excel.Export(FindForm(), rd);
        }

        void Edit()
        {
            int id = CurrentId(); if (id == 0) { UI.Warn("اختر سطرا من القائمة أولا."); return; }
            using (ItemDialog d = new ItemDialog(id)) if (d.ShowDialog(FindForm()) == DialogResult.OK) Rebind(id);
        }

        void Card()
        {
            int id = CurrentId(); if (id == 0) { UI.Warn("اختر سطرا من القائمة أولا."); return; }
            ReportViewerForm.ShowItemCard(FindForm(), id);
        }

        void Delete()
        {
            int id = CurrentId(); if (id == 0 || !Session.Demand(Session.Items)) return;
            int used = Db.I(Db.Scalar("SELECT COUNT(*) FROM sale_menu WHERE item_no = ?", id)) + Db.I(Db.Scalar("SELECT COUNT(*) FROM shop_menu WHERE item_no = ?", id))
                     + Db.I(Db.Scalar("SELECT COUNT(*) FROM trans_menu WHERE item_no = ?", id));
            foreach (string t in new[] { "reserve_menu", "mreq_menu", "preq_menu", "count_menu", "wo_menu" }) if (Db.TableExists(t)) used += Db.I(Db.Scalar("SELECT COUNT(*) FROM " + t + " WHERE item_no = ?", id));
            if (used > 0) { UI.Warn("لا يمكن حذف هذه المادة لأن لها " + used + " حركة في السندات.\nيمكنك تجميدها بدلا من الحذف (من شاشة التعديل)."); return; }
            if (Math.Abs(Cache.StockAll(id)) > 0.0001) { UI.Warn("لا يمكن حذف مادة لها رصيد."); return; }
            if (!UI.Confirm("حذف المادة: " + Cache.ItemName(id) + " ؟")) return;
            Db.InTx(delegate(DbTransaction tx)
            {
                List<KeyValuePair<string, string>> before = AuditTrail.ItemSnap(tx, id);
                Db.X(tx, "DELETE FROM item_stor WHERE item_no = ?", id);
                Db.X(tx, "DELETE FROM item WHERE item_no = ?", id);
                AuditTrail.Changes(tx, AuditTrail.Log(tx, "حذف مادة", Cache.ItemName(id)), "item", Cache.ItemCode(id).Length > 0 ? Cache.ItemCode(id) : Cache.ItemName(id), before, null);
            });
            Cache.LoadItems(); Rebind(0);
        }
    }
}
