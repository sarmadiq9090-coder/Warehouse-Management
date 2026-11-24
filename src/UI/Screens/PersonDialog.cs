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
    class PersonDialog : BaseDialog
    {
        int id;
        TextBox txtName, txtTel, txtAddr, txtNote; ComboBox cmbType;

        public PersonDialog(int pid)
        {
            id = pid;
            Text = id == 0 ? "جهة / شخص جديد" : "تعديل جهة / شخص";
            ClientSize = new Size(560, 300);
            TableLayoutPanel t = FormTable();
            txtName = UI.Text(360); txtName.MaxLength = 70;
            cmbType = UI.Combo(200);
            List<ComboItem> types = new List<ComboItem>();
            for (int i = 1; i < Cache.PersonTypes.Length; i++) types.Add(new ComboItem(i, Cache.PersonTypes[i]));
            cmbType.DisplayMember = "Name"; cmbType.DataSource = types;
            txtTel = UI.Text(200); txtTel.MaxLength = 50; txtTel.RightToLeft = RightToLeft.No;
            txtAddr = UI.Text(360); txtAddr.MaxLength = 50;
            txtNote = UI.Text(360); txtNote.MaxLength = 50;
            AddRow(t, "الاسم *", txtName); AddRow(t, "النوع", cmbType); AddRow(t, "الهاتف", txtTel); AddRow(t, "العنوان", txtAddr); AddRow(t, "ملاحظات", txtNote);
            Button ok = UI.Btn("حفظ", Theme.Accent, 110); Button cancel = UI.BtnLight("إلغاء", 90);
            Controls.Add(t); Controls.Add(ButtonBar(ok, cancel));
            AcceptButton = ok;
            UI.SetSel(cmbType, 3);
            if (id > 0)
            {
                DataRow r = Db.Query("SELECT p_name, p_type, p_tel, p_addrs, p_note FROM person WHERE p_no = ?", id).Rows[0];
                txtName.Text = Db.S(r[0]); UI.SetSel(cmbType, Db.I(r[1])); txtTel.Text = Db.S(r[2]); txtAddr.Text = Db.S(r[3]); txtNote.Text = Db.S(r[4]);
            }
            cancel.Click += delegate { Close(); };
            ok.Click += delegate
            {
                if (!Session.Demand(Session.Persons)) return;
                string n = txtName.Text.Trim();
                if (n.Length == 0) { UI.Warn("أدخل الاسم."); return; }
                if (Db.I(Db.Scalar("SELECT COUNT(*) FROM person WHERE p_name = ? AND p_no <> ?", n, id)) > 0) { UI.Warn("الاسم موجود مسبقا."); return; }
                int type = UI.Sel(cmbType);
                if (id == 0)
                {
                    Db.InTx(delegate(DbTransaction tx)
                    {
                        id = Db.Insert(tx, "INSERT INTO person (p_name, p_bal_id, p_bal_dl, p_bal_id_f, p_bal_dl_f, p_addrs, p_tel, p_note, p_type, p_sjl) VALUES (?, 0, 0, 0, 0, ?, ?, ?, ?, 0)",
                            n, Db.Null(txtAddr.Text.Trim()), Db.Null(txtTel.Text.Trim()), Db.Null(txtNote.Text.Trim()), type);
                        Db.Log(tx, "إضافة جهة", n);
                    });
                }
                else
                {
                    Db.Exec("UPDATE person SET p_name = ?, p_addrs = ?, p_tel = ?, p_note = ?, p_type = ? WHERE p_no = ?",
                        n, Db.Null(txtAddr.Text.Trim()), Db.Null(txtTel.Text.Trim()), Db.Null(txtNote.Text.Trim()), type, id);
                    Db.Log(null, "تعديل جهة", n);
                }
                Cache.LoadPersons();
                DialogResult = DialogResult.OK; Close();
            };
        }

        public static int AddNew(IWin32Window owner, string name)
        {
            if (!Session.Demand(Session.Persons)) return 0;
            using (PersonDialog d = new PersonDialog(0))
            {
                d.txtName.Text = name ?? "";
                return d.ShowDialog(owner) == DialogResult.OK ? d.id : 0;
            }
        }
    }
}
