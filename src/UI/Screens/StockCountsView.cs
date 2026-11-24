using System.Data;
using System.Drawing;
using System.Windows.Forms;
using System;

namespace Stores
{
    // ================================================================== physical stock counts (list)
    class StockCountsView : UserControl
    {
        MainForm main;
        DataGridView grid; Label lblCount;

        public StockCountsView(MainForm m)
        {
            main = m;
            BackColor = Theme.Bg;
            Panel filt = UI.Card(); filt.Dock = DockStyle.Top; filt.Height = 64;
            FlowLayoutPanel f = UI.Flow(); f.WrapContents = true;
            Button add = UI.Btn("+ جرد جديد", Theme.Accent, 130);
            Button open = UI.Btn("فتح", Theme.Primary, 100);
            Button del = UI.Btn("حذف جرد مفتوح", Theme.Danger, 140);
            Button refresh = UI.BtnLight("تحديث", 90);
            f.Controls.AddRange(new Control[] { add, open, del, refresh });
            filt.Controls.Add(f); UI.FitHeight(filt);

            Label help = new Label(); help.Dock = DockStyle.Top; help.Height = 28; help.ForeColor = Theme.Muted;
            help.Text = "الجرد الفعلي: ① جرد جديد لمخزن   ② طباعة ورقة الجرد وعدّ المواد   ③ إدخال الكميات المعدودة   ④ تقرير الفروقات   ⑤ ترحيل التسوية (للمدير).";

            Panel gc = UI.Card(); gc.Dock = DockStyle.Fill; gc.Padding = new Padding(1);
            grid = new DataGridView(); grid.Dock = DockStyle.Fill; UI.ReadOnlyGrid(grid);
            grid.Columns.Add(UI.Col("no", "رقم الجرد", "menu_no", 90, false));
            grid.Columns.Add(UI.Col("d", "التاريخ", "count_date", 110, false)); grid.Columns["d"].DefaultCellStyle.Format = "yyyy/MM/dd";
            grid.Columns.Add(UI.Col("s", "المخزن", "sname", 140, false));
            grid.Columns.Add(UI.Col("t", "نوع المواد", "tname", 120, false));
            grid.Columns.Add(UI.Col("n", "عدد المواد", "n", 100, true));
            grid.Columns.Add(UI.Col("nc", "المعدودة", "nc", 100, true));
            grid.Columns.Add(UI.Col("st", "الحالة", "status_name", 120, false));
            grid.Columns.Add(UI.Col("u", "أنشأه", "user_name", 90, false));
            grid.Columns.Add(UI.Col("note", "ملاحظات", "notes", 0, false));
            gc.Controls.Add(grid);
            lblCount = new Label(); lblCount.Dock = DockStyle.Bottom; lblCount.Height = 26; lblCount.ForeColor = Theme.Muted; lblCount.TextAlign = ContentAlignment.MiddleLeft;
            Controls.Add(gc); Controls.Add(lblCount); Controls.Add(help); Controls.Add(filt); Controls.Add(ViewHelp.Title("الجرد الفعلي وتسوية الفروقات"));

            if (!Db.HasCount)
            {
                help.ForeColor = Theme.Danger;
                help.Text = StockCount.NotReady + " " + (Db.SchemaWarning ?? "");
                add.Enabled = open.Enabled = del.Enabled = false;
            }

            add.Click += delegate
            {
                using (NewCountDialog d = new NewCountDialog())
                    if (d.ShowDialog(FindForm()) == DialogResult.OK && d.CreatedId > 0) main.Show(new StockCountView(main, d.CreatedId));
            };
            open.Click += delegate { OpenCurrent(); };
            grid.CellDoubleClick += delegate(object s, DataGridViewCellEventArgs e) { if (e.RowIndex >= 0) OpenCurrent(); };
            grid.KeyDown += delegate(object s, KeyEventArgs e) { if (e.KeyCode == Keys.Enter) { e.Handled = true; OpenCurrent(); } };
            del.Click += delegate { DeleteCurrent(); };
            refresh.Click += delegate { Reload(); };
            grid.CellFormatting += delegate(object s, DataGridViewCellFormattingEventArgs e)
            {
                if (e.RowIndex < 0) return;
                DataRowView rv = grid.Rows[e.RowIndex].DataBoundItem as DataRowView;
                if (rv != null && Db.I(rv["status"]) == 1) e.CellStyle.ForeColor = Theme.Muted;
            };
            Load += delegate { if (Db.HasCount) Reload(); };
        }

        DataRowView Current { get { return grid.CurrentRow == null ? null : grid.CurrentRow.DataBoundItem as DataRowView; } }

        void Reload()
        {
            try
            {
                DataTable t = StockCount.List();
                grid.DataSource = t;
                lblCount.Text = "عدد عمليات الجرد: " + t.Rows.Count + "    —    انقر مرتين على الجرد لفتحه";
            }
            catch (Exception ex) { UI.Error(ex); }
        }

        void OpenCurrent()
        {
            DataRowView rv = Current;
            if (rv == null) { UI.Warn("اختر جردا من القائمة أولا."); return; }
            main.Show(new StockCountView(main, Db.I(rv["count_no"])));
        }

        void DeleteCurrent()
        {
            DataRowView rv = Current;
            if (rv == null) { UI.Warn("اختر جردا من القائمة أولا."); return; }
            if (Db.I(rv["status"]) == 1) { UI.Warn("هذا الجرد مُرحّل ومقفل ولا يمكن حذفه."); return; }
            if (!StockCount.CanPost && Db.S(rv["user_name"]) != (Session.UserName ?? "")) { UI.Warn("حذف الجرد للمدير أو لمن أنشأه."); return; }
            if (!UI.Confirm("حذف الجرد رقم " + Db.S(rv["menu_no"]) + " وكل الكميات المدخلة فيه؟\nلن تتغير أرصدة المواد.")) return;
            try { StockCount.Delete(Db.I(rv["count_no"])); Reload(); }
            catch (Exception ex) { UI.Error(ex); }
        }
    }
}
