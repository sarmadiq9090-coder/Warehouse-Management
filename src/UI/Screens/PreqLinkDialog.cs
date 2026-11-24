using System.Data;
using System.Drawing;
using System.Windows.Forms;
using System;

namespace Stores
{
    /// <summary>Small question after a receipt is saved: «هل هذا الاستلام لطلب شراء؟» with the open requests that wait for
    /// items of the receipt (the supplier's own request first). «لا» leaves the receipt unlinked.</summary>
    class PreqLinkDialog : BaseDialog
    {
        DataGridView grid;
        public int Chosen;

        public PreqLinkDialog(DataTable open, Doc d)
        {
            Text = "ربط الاستلام بطلب شراء";
            ClientSize = new Size(640, 330);
            Label q = new Label(); q.Dock = DockStyle.Top; q.Height = 58; q.Padding = new Padding(14, 10, 14, 0); q.Font = Theme.FontSubtitle; q.ForeColor = Theme.Text;
            q.Text = "هل هذا الاستلام (إدخال مخزني رقم " + d.No + ") لطلب شراء؟";
            Label h = new Label(); h.Dock = DockStyle.Top; h.Height = 26; h.Padding = new Padding(14, 0, 14, 0); h.ForeColor = Theme.Muted;
            h.Text = "طلبات مفتوحة تنتظر مواد من هذا السند. اختر الطلب ليُحدَّث المستلم والمتبقي تلقائيا، أو «لا».";
            grid = new DataGridView(); grid.Dock = DockStyle.Fill; UI.ReadOnlyGrid(grid);
            grid.Columns.Add(UI.Col("no", "رقم الطلب", "menu_no", 90, false));
            grid.Columns.Add(UI.Col("d", "التاريخ", "preq_date", 100, false)); grid.Columns["d"].DefaultCellStyle.Format = "yyyy/MM/dd";
            grid.Columns.Add(UI.Col("p", "المجهز", "pname", 0, false));
            grid.Columns.Add(UI.Col("s", "الحالة", "status_name", 100, false));
            grid.Columns.Add(UI.Col("n", "مواد مشتركة", "n", 110, true));
            grid.Columns.Add(UI.Col("r", "المتبقي منها", "rem", 110, true));
            // the supplier's own request first
            DataView v = open.DefaultView;
            open.Columns.Add("_o", typeof(int));
            foreach (DataRow r in open.Rows) r["_o"] = d.Person > 0 && Db.I(r["p_no"]) == d.Person ? 0 : 1;
            v.Sort = "_o, preq_date DESC";
            grid.DataSource = v;
            Panel gp = new Panel(); gp.Dock = DockStyle.Fill; gp.Padding = new Padding(14, 4, 14, 8); gp.Controls.Add(grid);
            Button yes = UI.Btn("نعم، اربطه بالطلب المحدد", Theme.Accent, 210), no = UI.BtnLight("لا", 90);
            Controls.Add(gp); Controls.Add(h); Controls.Add(q); Controls.Add(ButtonBar(yes, no));
            AcceptButton = yes;
            yes.Click += delegate
            {
                DataRowView rv = grid.CurrentRow == null ? null : grid.CurrentRow.DataBoundItem as DataRowView;
                if (rv == null) { UI.Warn("اختر طلب الشراء من القائمة."); return; }
                Chosen = Db.I(rv["preq_no"]); DialogResult = DialogResult.OK; Close();
            };
            grid.CellDoubleClick += delegate(object s, DataGridViewCellEventArgs e) { if (e.RowIndex >= 0) yes.PerformClick(); };
            no.Click += delegate { DialogResult = DialogResult.Cancel; Close(); };
        }
    }
}
