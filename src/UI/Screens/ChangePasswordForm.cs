using System.Collections.Generic;
using System.Data;
using System.Drawing.Drawing2D;
using System.Drawing;
using System.IO;
using System.Windows.Forms;
using System;

namespace Stores
{
    class ChangePasswordForm : BaseDialog
    {
        public ChangePasswordForm() : this(null) { }

        /// <summary>current: the password just typed at sign-in (forced change of the starting password), filled in for the user.</summary>
        public ChangePasswordForm(string current)
        {
            Text = "تغيير كلمة المرور";
            ClientSize = new Size(440, 230);
            TableLayoutPanel t = FormTable();
            TextBox oldP = UI.Text(220); oldP.UseSystemPasswordChar = true;
            if (current != null) { oldP.Text = current; oldP.ReadOnly = true; }
            TextBox n1 = UI.Text(220); n1.UseSystemPasswordChar = true;
            TextBox n2 = UI.Text(220); n2.UseSystemPasswordChar = true;
            AddRow(t, "كلمة المرور الحالية", oldP);
            AddRow(t, "كلمة المرور الجديدة", n1);
            AddRow(t, "تأكيد كلمة المرور", n2);
            Button ok = UI.Btn("حفظ", Theme.Primary, 100);
            Button cancel = UI.BtnLight("إلغاء", 90);
            Controls.Add(t); Controls.Add(ButtonBar(ok, cancel));
            AcceptButton = ok;
            cancel.Click += delegate { Close(); };
            ok.Click += delegate
            {
                string cur = Db.S(Db.Scalar("SELECT pass FROM pass WHERE pass_no = ?", Session.UserNo));
                if (cur != oldP.Text) { UI.Warn("كلمة المرور الحالية غير صحيحة."); return; }
                if (n1.Text.Length == 0) { UI.Warn("أدخل كلمة المرور الجديدة."); return; }
                if (n1.Text != n2.Text) { UI.Warn("كلمتا المرور غير متطابقتين."); return; }
                if (n1.Text == oldP.Text) { UI.Warn("اختر كلمة مرور مختلفة عن الحالية."); return; }
                if (n1.Text.Length > 20) { UI.Warn("كلمة المرور طويلة (الحد 20 حرفا)."); return; }
                Db.Exec("UPDATE pass SET pass = ? WHERE pass_no = ?", n1.Text, Session.UserNo);
                Db.Log(null, "تغيير كلمة المرور", Session.UserName);
                UI.Info("تم تغيير كلمة المرور.");
                DialogResult = DialogResult.OK; Close();
            };
        }
    }
}
