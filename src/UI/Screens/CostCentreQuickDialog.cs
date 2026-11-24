using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using System;

namespace Stores
{
    /// <summary>«مركز كلفة جديد» opened from the «+» next to the cost centre list in a document or report:
    /// name (required), type, code (empty = automatic). The new cost centre is selected right away.</summary>
    class CostCentreQuickDialog : BaseDialog
    {
        TextBox txtName, txtCode; ComboBox cmbType; Label lblHint;
        public int NewId;

        [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern IntPtr SendMessage(IntPtr h, int msg, IntPtr w, string l);

        public CostCentreQuickDialog(string name)
        {
            Text = "مركز كلفة جديد";
            ClientSize = new Size(500, 250);
            TableLayoutPanel t = FormTable();
            t.ColumnStyles[0].Width = 130;
            txtName = UI.Text(320); txtName.MaxLength = 100; txtName.Text = name ?? "";
            cmbType = UI.Combo(170); cmbType.Items.AddRange(CostCentres.TypeItems().ToArray()); cmbType.SelectedIndex = 0;
            txtCode = UI.Text(170); txtCode.MaxLength = 30;
            lblHint = UI.Lbl(""); lblHint.ForeColor = Theme.Muted; lblHint.AutoSize = false; lblHint.Height = 26; lblHint.TextAlign = ContentAlignment.MiddleLeft;
            AddRow(t, "الاسم *", txtName); AddRow(t, "النوع", cmbType); AddRow(t, "الرمز", txtCode); AddRow(t, "", lblHint);
            Button ok = UI.Btn("حفظ واختيار", Theme.Accent, 130); Button cancel = UI.BtnLight("إلغاء", 90);
            Controls.Add(t); Controls.Add(ButtonBar(ok, cancel));
            AcceptButton = ok;
            Action hint = delegate
            {
                string auto = CostCentres.NextCode(UI.Sel(cmbType));
                lblHint.Text = "اترك الرمز فارغا ليُعطى تلقائيا: " + auto;
                if (txtCode.IsHandleCreated) SendMessage(txtCode.Handle, 0x1501, (IntPtr)1, auto);
            };
            cmbType.SelectedIndexChanged += delegate { hint(); };
            txtCode.HandleCreated += delegate { hint(); };
            Shown += delegate { hint(); txtName.Focus(); txtName.SelectionStart = txtName.Text.Length; };
            cancel.Click += delegate { Close(); };
            ok.Click += delegate { if (SaveNew()) { DialogResult = DialogResult.OK; Close(); } };
        }

        /// <summary>Selects a type in the combo (tests and callers that already know the kind).</summary>
        public void SetType(int type) { UI.SetSel(cmbType, CostCentres.NormType(type)); }

        /// <summary>Validates and saves (the OK button). Public for the tests.</summary>
        public bool SaveNew()
        {
            if (!Session.Demand(Session.Persons)) return false;
            try
            {
                int type = UI.Sel(cmbType);
                NewId = CostCentres.Save(0, txtName.Text, txtCode.Text, type, 1, null, type == CostCentres.Project ? (DateTime?)DateTime.Today : null, null);
                return NewId > 0;
            }
            catch (Exception ex) { UI.Error(ex); txtName.Focus(); return false; }
        }

        /// <summary>Opens the dialog; returns the new cost centre number or 0.</summary>
        public static int AddNew(IWin32Window owner, string name)
        {
            if (!Session.Demand(Session.Persons)) return 0;
            if (!Db.HasProj) { UI.Warn("ميزة مراكز الكلفة غير مفعّلة بعد (راجع الإعدادات)."); return 0; }
            using (CostCentreQuickDialog d = new CostCentreQuickDialog(name))
                return d.ShowDialog(owner) == DialogResult.OK ? d.NewId : 0;
        }
    }
}
