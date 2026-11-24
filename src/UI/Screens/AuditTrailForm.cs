using System.Collections.Generic;
using System.Data;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using System;

namespace Stores
{
    /// <summary>The «سجل التدقيق» viewer (Settings screen): who edited or deleted what and when, with the values before
    /// and after. Filters: dates, user, kind of operation, document number. Newest first, at most 3000 rows.</summary>
    class AuditTrailForm : BaseDialog
    {
        const int Max = 3000;
        DateTimePicker dFrom, dTo; ComboBox cmbUser, cmbKind; TextBox txtDoc; CheckBox chkOnly; DataGridView grid; Label lblCount;
        List<KeyValuePair<string, string[]>> groups = AuditTrail.ActGroups();

        public AuditTrailForm()
        {
            Text = "سجل التدقيق";
            FormBorderStyle = FormBorderStyle.Sizable; MaximizeBox = true; MinimumSize = new Size(820, 480);
            ClientSize = new Size(1080, 640);

            Panel top = new Panel(); top.Dock = DockStyle.Top; top.BackColor = Color.White; top.Padding = new Padding(12, 8, 12, 4);
            Label title = new Label(); title.Text = "سجل التدقيق • القيم قبل وبعد التعديل"; title.Font = Theme.FontTitle; title.ForeColor = Theme.Primary; title.Dock = DockStyle.Top; title.Height = 38;
            FlowLayoutPanel f = UI.Flow(); f.WrapContents = true;
            dFrom = UI.DatePicker(DateTime.Today.AddMonths(-1)); dTo = UI.DatePicker(DateTime.Today);
            cmbUser = UI.Combo(130); cmbKind = UI.Combo(190); txtDoc = UI.Text(90);
            chkOnly = new CheckBox(); chkOnly.Text = "التعديلات والحذف فقط"; chkOnly.Checked = true; chkOnly.AutoSize = true; chkOnly.Margin = new Padding(8, 9, 4, 4);
            Button bShow = UI.Btn("عرض", Theme.Primary, 90); bShow.Margin = new Padding(8, 4, 4, 4);
            f.Controls.AddRange(new Control[] { UI.Field("من", dFrom), UI.Field("إلى", dTo), UI.Field("المستخدم", cmbUser), UI.Field("العملية", cmbKind),
                                                UI.Field("رقم السند", txtDoc), chkOnly, bShow });
            top.Controls.Add(f); top.Controls.Add(title);
            UI.FitHeight(top);

            grid = new DataGridView(); grid.Dock = DockStyle.Fill; UI.ReadOnlyGrid(grid); grid.AutoGenerateColumns = false;
            grid.Columns.Add(UI.Col("at", "الوقت", "log_at", 140, false)); grid.Columns["at"].DefaultCellStyle.Format = "yyyy/MM/dd HH:mm";
            grid.Columns.Add(UI.Col("u", "المستخدم", "user_name", 90, false));
            grid.Columns.Add(UI.Col("a", "العملية", "act", 150, false));
            grid.Columns.Add(UI.Col("k", "السند / السجل", "rec_key", 115, false));
            grid.Columns.Add(UI.Col("f", "الحقل", "fld", 0, false));
            grid.Columns.Add(UI.Col("o", "القيمة السابقة", "old_v", 160, false));
            grid.Columns.Add(UI.Col("n", "القيمة الجديدة", "new_v", 160, false));
            grid.Columns["o"].DefaultCellStyle.ForeColor = Theme.Danger;
            grid.Columns["n"].DefaultCellStyle.ForeColor = Theme.Success;
            grid.Columns["f"].MinimumWidth = 160;
            grid.CellFormatting += Shade;

            lblCount = new Label(); lblCount.Dock = DockStyle.Fill; lblCount.ForeColor = Theme.Muted; lblCount.TextAlign = ContentAlignment.MiddleLeft;
            Button bExit = UI.BtnLight("إغلاق", 80);
            FlowLayoutPanel bar = ButtonBar(bExit); bar.Controls.Add(lblCount); lblCount.AutoSize = true; lblCount.Margin = new Padding(12, 10, 4, 0);

            Controls.Add(grid); Controls.Add(top); Controls.Add(bar);

            // filters
            List<string> users = new List<string>();
            try { foreach (DataRow r in Db.Query("SELECT DISTINCT user_name FROM app_log WHERE user_name IS NOT NULL AND user_name <> ''").Rows) users.Add(Db.S(r[0])); } catch { }
            cmbUser.Items.Add("الكل"); foreach (string u in users.OrderBy(x => x)) cmbUser.Items.Add(u); cmbUser.SelectedIndex = 0;
            cmbKind.Items.Add("الكل"); foreach (KeyValuePair<string, string[]> g in groups) cmbKind.Items.Add(g.Key); cmbKind.SelectedIndex = 0;

            bShow.Click += delegate { Run(); };
            bExit.Click += delegate { Close(); };
            txtDoc.KeyDown += delegate(object s, KeyEventArgs e) { if (e.KeyCode == Keys.Enter) { e.SuppressKeyPress = true; Run(); } };
            Shown += delegate { Run(); };
        }

        /// <summary>Alternating shade per operation, so the detail rows of one save read as a group.</summary>
        void Shade(object s, DataGridViewCellFormattingEventArgs e)
        {
            if (e.RowIndex < 0) return;
            DataRowView v = grid.Rows[e.RowIndex].DataBoundItem as DataRowView;
            if (v == null) return;
            int grp = Db.I(v["grp"]);
            e.CellStyle.BackColor = grp % 2 == 0 ? Color.White : Theme.Layer;
        }

        void Run()
        {
            try
            {
                UI.WaitCursor(true);
                string[] acts = cmbKind.SelectedIndex > 0 ? groups[cmbKind.SelectedIndex - 1].Value : null;
                string user = cmbUser.SelectedIndex > 0 ? Convert.ToString(cmbUser.SelectedItem) : null;
                DataTable t = AuditTrail.Query(dFrom.Value.Date, dTo.Value.Date.AddDays(1), user, UI.NormDigits(txtDoc.Text.Trim()), acts, chkOnly.Checked, Max);
                // operations without detail rows (additions, lock …) show their details text in the field column
                t.Columns.Add("grp", typeof(int));
                int last = -1, g = 0, ops = 0;
                foreach (DataRow r in t.Rows)
                {
                    int id = Db.I(r["id"]); if (id != last) { g++; ops++; last = id; }
                    r["grp"] = g;
                    if (r["fld"] is DBNull) r["fld"] = Db.S(r["details"]);
                }
                grid.DataSource = t;
                lblCount.Text = ops.ToString("#,##0") + " عملية • " + t.Rows.Count.ToString("#,##0") + " سطر" + (t.Rows.Count >= Max ? " (يُعرض أحدث " + Max + " فقط، ضيّق البحث)" : "");
            }
            catch (Exception ex) { UI.Error(ex); }
            finally { UI.WaitCursor(false); }
        }
    }
}
