using System.Drawing;
using System.Windows.Forms;
using System;

namespace Stores
{
    /// <summary>Starts a new physical stock count: store, optional item type, date, notes.</summary>
    class NewCountDialog : BaseDialog
    {
        DateTimePicker dtp; ComboBox cmbStore, cmbType; TextBox txtNote; CheckBox chkZero;
        public int CreatedId;

        public NewCountDialog()
        {
            Text = "جرد فعلي جديد";
            ClientSize = new Size(560, 330);
            TableLayoutPanel t = FormTable();
            dtp = UI.DatePicker(DateTime.Today);
            cmbStore = UI.Combo(260); UI.Fill(cmbStore, Cache.Stores, "stor_no", "stor_name", null); UI.SetSel(cmbStore, Cache.DefaultStore);
            cmbType = UI.Combo(260); UI.Fill(cmbType, Cache.Types, "type_no", "type_name", "كل الأنواع");
            txtNote = UI.Text(340); txtNote.MaxLength = 250;
            chkZero = new CheckBox(); chkZero.Text = "إضافة المواد التي رصيدها صفر أيضا"; chkZero.AutoSize = true;
            AddRow(t, "تاريخ الجرد", dtp);
            AddRow(t, "المخزن", cmbStore);
            AddRow(t, "نوع المواد", cmbType);
            AddRow(t, "ملاحظات / لجنة الجرد", txtNote);
            AddRow(t, "", chkZero);
            Label help = new Label(); help.AutoSize = false; help.Height = 64; help.ForeColor = Theme.Muted;
            help.Text = "يُحفظ الرصيد الدفتري لكل مادة لحظة إنشاء الجرد. اطبع ورقة الجرد (بدون الأرصدة)، ثم أدخل الكميات المعدودة، ثم راجع تقرير الفروقات ورحّل التسوية.";
            AddRow(t, "", help);
            Button ok = UI.Btn("إنشاء الجرد", Theme.Accent, 130), cancel = UI.BtnLight("إلغاء", 90);
            Controls.Add(t); Controls.Add(ButtonBar(ok, cancel));
            AcceptButton = ok;
            cancel.Click += delegate { DialogResult = DialogResult.Cancel; Close(); };
            ok.Click += delegate
            {
                try
                {
                    UI.WaitCursor(true);
                    CreatedId = StockCount.Create(dtp.Value.Date, UI.Sel(cmbStore), UI.Sel(cmbType), txtNote.Text.Trim(), chkZero.Checked);
                    DialogResult = DialogResult.OK; Close();
                }
                catch (Exception ex) { UI.WaitCursor(false); UI.Error(ex); }
                finally { UI.WaitCursor(false); }
            };
        }
    }
}
