using System.Collections.Generic;
using System.Data;
using System.Drawing;
using System.Windows.Forms;
using System;

namespace Stores
{
    /// <summary>Add / edit a cost centre («مركز الكلفة»). Stored in the table project; a project is one type among others.
    /// Client and start date only apply to the type «مشروع».</summary>
    class ProjectDialog : BaseDialog
    {
        int id;
        TextBox txtCode, txtName, txtClient, txtNotes; ComboBox cmbStatus, cmbType; DateTimePicker dtp;
        Label lblClient, lblStart;

        public ProjectDialog(int pid)
        {
            id = pid;
            Text = id == 0 ? "مركز كلفة جديد" : "تعديل مركز كلفة";
            ClientSize = new Size(580, 390);
            TableLayoutPanel t = FormTable();
            txtCode = UI.Text(160); txtCode.MaxLength = 30;
            txtName = UI.Text(380); txtName.MaxLength = 100;
            cmbType = UI.Combo(160); cmbType.Items.AddRange(CostCentres.TypeItems().ToArray()); cmbType.SelectedIndex = 0;
            txtClient = UI.Text(380); txtClient.MaxLength = 100;
            cmbStatus = UI.Combo(160);
            cmbStatus.Items.AddRange(new object[] { new ComboItem(1, Cache.ProjectStatus[1]), new ComboItem(2, Cache.ProjectStatus[2]) }); cmbStatus.SelectedIndex = 0;
            dtp = UI.DatePicker(DateTime.Today);
            txtNotes = UI.Text(380); txtNotes.MaxLength = 250;
            AddRow(t, "اسم مركز الكلفة *", txtName); AddRow(t, "النوع", cmbType); AddRow(t, "الرمز", txtCode);
            AddRow(t, "الجهة المستفيدة", txtClient); lblClient = (Label)t.GetControlFromPosition(0, t.RowCount - 1);
            AddRow(t, "تاريخ البدء", dtp); lblStart = (Label)t.GetControlFromPosition(0, t.RowCount - 1);
            AddRow(t, "الحالة", cmbStatus); AddRow(t, "ملاحظات", txtNotes);
            Label note = UI.Lbl("الجهة المستفيدة وتاريخ البدء للمشاريع فقط. الرمز الفارغ يُعطى تلقائيا."); note.ForeColor = Theme.Muted; note.AutoSize = false; note.Height = 46; note.TextAlign = ContentAlignment.MiddleLeft;
            AddRow(t, "", note);
            Button ok = UI.Btn("حفظ", Theme.Accent, 110); Button cancel = UI.BtnLight("إلغاء", 90);
            Controls.Add(t); Controls.Add(ButtonBar(ok, cancel));
            AcceptButton = ok;
            if (id > 0)
            {
                DataRow r = Db.Query("SELECT * FROM project WHERE proj_no = ?", id).Rows[0];
                txtCode.Text = Db.S(r["proj_code"]); txtName.Text = Db.S(r["proj_name"]); txtClient.Text = Db.S(r["client"]); txtNotes.Text = Db.S(r["notes"]);
                UI.SetSel(cmbStatus, Math.Max(1, Db.I(r["status"])));
                UI.SetSel(cmbType, CostCentres.NormType(r.Table.Columns.Contains("cc_type") ? Db.I(r["cc_type"]) : 1));
                if (!(r["start_date"] is DBNull)) dtp.Value = Db.Dt(r["start_date"]);
            }
            cmbType.SelectedIndexChanged += delegate { ApplyType(); };
            ApplyType();
            cancel.Click += delegate { Close(); };
            ok.Click += delegate { if (SaveCc()) { DialogResult = DialogResult.OK; Close(); } };
        }

        /// <summary>Client and start date only make sense for a project.</summary>
        void ApplyType()
        {
            bool proj = UI.Sel(cmbType) == CostCentres.Project;
            txtClient.Enabled = dtp.Enabled = proj;
            lblClient.ForeColor = lblStart.ForeColor = proj ? Theme.Text : Theme.Muted;
        }

        bool SaveCc()
        {
            if (!Session.Demand(Session.Persons)) return false;
            try
            {
                int type = UI.Sel(cmbType);
                id = CostCentres.Save(id, txtName.Text, txtCode.Text, type, UI.Sel(cmbStatus), txtClient.Text, dtp.Value.Date, txtNotes.Text);
                return true;
            }
            catch (Exception ex) { UI.Error(ex); return false; }
        }

        public static int AddNew(IWin32Window owner, string name)
        {
            if (!Session.Demand(Session.Persons)) return 0;
            if (!Db.HasProj) { UI.Warn("ميزة مراكز الكلفة غير مفعّلة بعد (راجع الإعدادات)."); return 0; }
            using (ProjectDialog d = new ProjectDialog(0))
            {
                d.txtName.Text = name ?? "";
                return d.ShowDialog(owner) == DialogResult.OK ? d.id : 0;
            }
        }
    }
}
