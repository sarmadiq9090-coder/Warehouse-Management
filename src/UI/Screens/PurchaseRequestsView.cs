using System.Data;
using System.Drawing;
using System.Windows.Forms;
using System;

namespace Stores
{
    // ================================================================== purchase requests (list)
    class PurchaseRequestsView : UserControl
    {
        DataGridView grid; Label lblCount; CheckBox chkAll;

        public PurchaseRequestsView()
        {
            BackColor = Theme.Bg;
            Panel filt = UI.Card(); filt.Dock = DockStyle.Top; filt.Height = 64;
            FlowLayoutPanel f = UI.Flow(); f.WrapContents = true;
            Button add = UI.Btn("+ طلب شراء جديد", Theme.Accent, 150);
            Button open = UI.Btn("فتح", Theme.Primary, 90);
            Button del = UI.Btn("حذف مسودة", Theme.Danger, 110);
            Button xl = UI.BtnLight("تصدير Excel", 110);
            Button rep = UI.BtnLight("تقرير الطلبات المفتوحة", 170);
            Button refresh = UI.BtnLight("تحديث", 80);
            chkAll = new CheckBox(); chkAll.Text = "إظهار المكتملة والملغاة"; chkAll.AutoSize = true; chkAll.Margin = new Padding(12, 11, 4, 0);
            f.Controls.AddRange(new Control[] { add, open, del, xl, rep, refresh, chkAll });
            filt.Controls.Add(f); UI.FitHeight(filt);

            Label help = new Label(); help.Dock = DockStyle.Top; help.Height = 28; help.ForeColor = Theme.Muted;
            help.Text = "لا يغيّر الأرصدة  •  أنشئه هنا أو من تقرير «الأرصدة الحرجة»  •  اطبعه وأرسله  •  اربطه عند حفظ «إدخال مخزني» فيُحدَّث المستلم";

            Panel gc = UI.Card(); gc.Dock = DockStyle.Fill; gc.Padding = new Padding(1);
            grid = new DataGridView(); grid.Dock = DockStyle.Fill; UI.ReadOnlyGrid(grid);
            grid.Columns.Add(UI.Col("no", "رقم الطلب", "menu_no", 90, false));
            grid.Columns.Add(UI.Col("d", "التاريخ", "preq_date", 100, false)); grid.Columns["d"].DefaultCellStyle.Format = "yyyy/MM/dd";
            grid.Columns.Add(UI.Col("st", "الحالة", "status_name", 105, false));
            grid.Columns.Add(UI.Col("p", "المجهز", "pname", 150, false));
            grid.Columns.Add(UI.Col("s", "المخزن", "sname", 110, false));
            grid.Columns.Add(UI.Col("n", "عدد المواد", "n", 92, true));
            grid.Columns.Add(UI.Col("q", "المطلوب", "q", 85, true));
            grid.Columns.Add(UI.Col("rq", "المستلم", "rq", 85, true));
            grid.Columns.Add(UI.Col("age", "العمر (يوم)", "age", 85, true));
            grid.Columns.Add(UI.Col("u", "أعدّه", "user_name", 80, false));
            grid.Columns.Add(UI.Col("note", "ملاحظات", "notes", 0, false)); grid.Columns["note"].MinimumWidth = 100;
            gc.Controls.Add(grid);
            lblCount = new Label(); lblCount.Dock = DockStyle.Bottom; lblCount.Height = 26; lblCount.ForeColor = Theme.Muted; lblCount.TextAlign = ContentAlignment.MiddleLeft;
            Controls.Add(gc); Controls.Add(lblCount); Controls.Add(help); Controls.Add(filt); Controls.Add(ViewHelp.Title("طلبات الشراء"));

            bool can = PurchaseRequests.CanEdit;
            add.Enabled = del.Enabled = can;
            if (!Db.HasPreq)
            {
                help.ForeColor = Theme.Danger;
                help.Text = PurchaseRequests.NotReady + " " + (Db.SchemaWarning ?? "");
                add.Enabled = open.Enabled = del.Enabled = xl.Enabled = false;
            }

            add.Click += delegate { OpenReq(0); };
            open.Click += delegate { OpenCurrent(); };
            grid.CellDoubleClick += delegate(object s, DataGridViewCellEventArgs e) { if (e.RowIndex >= 0) OpenCurrent(); };
            grid.KeyDown += delegate(object s, KeyEventArgs e) { if (e.KeyCode == Keys.Enter) { e.Handled = true; OpenCurrent(); } };
            del.Click += delegate { DeleteCurrent(); };
            xl.Click += delegate { Excel.ExportGrid(FindForm(), grid, "طلبات الشراء"); };
            rep.Click += delegate { ReportsWindow.Open(FindForm(), "openpreq", 0, 0, 0); };
            refresh.Click += delegate { Reload(true); };
            chkAll.CheckedChanged += delegate { Reload(false); };
            grid.CellFormatting += delegate(object s, DataGridViewCellFormattingEventArgs e)
            {
                if (e.RowIndex < 0) return;
                DataRowView rv = grid.Rows[e.RowIndex].DataBoundItem as DataRowView;
                if (rv == null) return;
                int st = Db.I(rv["status"]);
                if (st == PurchaseRequests.Done || st == PurchaseRequests.Cancelled) e.CellStyle.ForeColor = Theme.Muted;
                else if (grid.Columns[e.ColumnIndex].Name == "st") e.CellStyle.ForeColor = st == PurchaseRequests.Partial ? Theme.Warning : st == PurchaseRequests.Draft ? Theme.Muted : Theme.Primary;
                if (grid.Columns[e.ColumnIndex].Name == "age" && st == PurchaseRequests.Sent && Db.I(rv["age"]) > 30) e.CellStyle.ForeColor = Theme.Danger;
            };
            Load += delegate { if (Db.HasPreq) Reload(true); };
        }

        DataRowView Current { get { return grid.CurrentRow == null ? null : grid.CurrentRow.DataBoundItem as DataRowView; } }

        void Reload(bool recalc)
        {
            try
            {
                // receipts may have been edited or deleted since: received quantities are recomputed from the linked receipts
                if (recalc && PurchaseRequests.CanEdit) PurchaseRequests.RecalcAll();
                DataTable t = PurchaseRequests.List(chkAll.Checked);
                grid.DataSource = t;
                int open = 0; foreach (DataRow r in t.Rows) if (Db.I(r["status"]) <= PurchaseRequests.Partial) open++;
                lblCount.Text = "عدد الطلبات: " + t.Rows.Count + " (مفتوحة " + open + ")    —    انقر مرتين على الطلب لفتحه";
            }
            catch (Exception ex) { UI.Error(ex); }
        }

        void OpenReq(int id)
        {
            using (PurchaseRequestDialog d = new PurchaseRequestDialog(id))
            {
                d.ShowDialog(FindForm());
                if (d.Changed) Reload(false);
            }
        }

        void OpenCurrent()
        {
            DataRowView rv = Current;
            if (rv == null) { UI.Warn("اختر طلبا من القائمة أولا."); return; }
            OpenReq(Db.I(rv["preq_no"]));
        }

        void DeleteCurrent()
        {
            DataRowView rv = Current;
            if (rv == null) { UI.Warn("اختر طلبا من القائمة أولا."); return; }
            if (Db.I(rv["status"]) != PurchaseRequests.Draft) { UI.Warn("يمكن حذف المسودات فقط. افتح الطلب واستعمل «إلغاء الطلب»."); return; }
            if (!UI.Confirm("حذف مسودة طلب الشراء رقم " + Db.S(rv["menu_no"]) + " نهائيا؟")) return;
            try { PurchaseRequests.Delete(Db.I(rv["preq_no"])); Reload(false); }
            catch (UserError ue) { UI.Warn(ue.Message); }
            catch (Exception ex) { UI.Error(ex); }
        }
    }
}
