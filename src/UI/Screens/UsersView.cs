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
    // ================================================================== users
    class UsersView : UserControl
    {
        static readonly int[] Cmds = { 21, 24, 22, 23, 11, 14, 12, 13, 61, 74, 75, 71, 81, Requisitions.PermApprove, WriteOffs.PermApprove, PeriodLock.Perm };
        static readonly string[] CmdNames = {
            "عرض سندات الإخراج", "إضافة سند إخراج", "تعديل سند إخراج", "حذف سند إخراج",
            "عرض سندات الإدخال والمردود", "إضافة سند إدخال / مردود", "تعديل سند إدخال / مردود", "حذف سند إدخال / مردود",
            "النقل المخزني", "إدارة الجهات والأشخاص", "إدارة المواد والأرصدة الافتتاحية", "إعداد المخازن والأنواع", "التقارير", Requisitions.PermName, WriteOffs.PermName, PeriodLock.PermName };

        ListBox users; TextBox txtName, txtPass; CheckedListBox perms; Label note;

        public UsersView()
        {
            BackColor = Theme.Bg;
            Panel left = new Panel(); left.Dock = DockStyle.Right; left.Width = 260; left.Padding = new Padding(6); left.BackColor = Theme.Card;
            users = new ListBox(); users.Dock = DockStyle.Fill; users.Font = new Font("Segoe UI", 12f); users.IntegralHeight = false; users.DisplayMember = "Name";
            Label lu = new Label(); lu.Text = "المستخدمون"; lu.Font = Theme.FontBold; lu.ForeColor = Theme.Primary; lu.Dock = DockStyle.Top; lu.Height = 28;
            left.Controls.Add(users); left.Controls.Add(lu);

            Panel right = UI.Card(); right.Dock = DockStyle.Fill; right.Padding = new Padding(16); right.Margin = new Padding(8);
            FlowLayoutPanel top = UI.Flow(); top.WrapContents = true;
            txtName = UI.Text(200); txtPass = UI.Text(200); txtPass.UseSystemPasswordChar = true;
            txtName.MaxLength = 30; txtPass.MaxLength = 20;
            top.Controls.AddRange(new Control[] { UI.Field("اسم المستخدم", txtName), UI.Field("كلمة مرور جديدة", txtPass) });
            note = new Label(); note.Dock = DockStyle.Top; note.Height = 48; note.ForeColor = Theme.Muted; note.Padding = new Padding(0, 4, 0, 4);
            note.Text = "اترك كلمة المرور فارغة لإبقائها كما هي.\nالمستخدم رقم 1 هو مدير النظام وله كل الصلاحيات.";
            Label lp = new Label(); lp.Text = "الصلاحيات"; lp.Font = Theme.FontBold; lp.ForeColor = Theme.Primary; lp.Dock = DockStyle.Top; lp.Height = 30;
            perms = new CheckedListBox(); perms.Dock = DockStyle.Fill; perms.CheckOnClick = true; perms.Font = new Font("Segoe UI", 11f); perms.BorderStyle = BorderStyle.None;
            perms.MultiColumn = false;
            foreach (string n in CmdNames) perms.Items.Add(n);
            FlowLayoutPanel bar = UI.Flow(); bar.Dock = DockStyle.Bottom;
            Button save = UI.Btn("حفظ", Theme.Accent, 110);
            bar.Controls.Add(save);
            right.Controls.Add(perms); right.Controls.Add(lp); right.Controls.Add(note); right.Controls.Add(top); right.Controls.Add(bar);

            Panel spacer = new Panel(); spacer.Dock = DockStyle.Right; spacer.Width = 10;
            Controls.Add(right); Controls.Add(spacer); Controls.Add(left); Controls.Add(ViewHelp.Title("المستخدمون والصلاحيات"));

            foreach (DataRow r in Db.Query("SELECT pass_no, user_name FROM pass ORDER BY pass_no").Rows)
                users.Items.Add(new ComboItem(Db.I(r[0]), "مستخدم " + Db.I(r[0]) + " • " + Db.S(r[1])));
            users.SelectedIndexChanged += delegate { Show(); };
            save.Click += delegate { Save(); };
            if (users.Items.Count > 0) users.SelectedIndex = 0;
        }

        int Sel { get { ComboItem i = users.SelectedItem as ComboItem; return i == null ? 0 : i.Id; } }

        new void Show()
        {
            int u = Sel; if (u == 0) { UI.Warn("اختر مستخدما من القائمة أولا."); return; }
            txtName.Text = Db.S(Db.Scalar("SELECT user_name FROM pass WHERE pass_no = ?", u));
            txtPass.Text = "";
            Dictionary<int, bool> p = new Dictionary<int, bool>();
            if (u >= 1 && u <= 5)
                foreach (DataRow r in Db.Query("SELECT command_no, user" + u + " FROM disply_command").Rows) p[Db.I(r[0])] = Db.B(r[1]);
            for (int i = 0; i < Cmds.Length; i++) { bool v; perms.SetItemChecked(i, u == 1 || (p.TryGetValue(Cmds[i], out v) && v)); }
            perms.Enabled = u != 1 && u <= 5;
        }

        void Save()
        {
            int u = Sel; if (u == 0) { UI.Warn("اختر مستخدما من القائمة أولا."); return; }
            string n = txtName.Text.Trim();
            if (n.Length == 0) { UI.Warn("أدخل اسم المستخدم."); return; }
            if (Db.I(Db.Scalar("SELECT COUNT(*) FROM pass WHERE user_name = ? AND pass_no <> ?", n, u)) > 0) { UI.Warn("الاسم مستخدم."); return; }
            Db.InTx(delegate(DbTransaction tx)
            {
                Db.X(tx, "UPDATE pass SET user_name = ? WHERE pass_no = ?", n, u);
                if (txtPass.Text.Length > 0) Db.X(tx, "UPDATE pass SET pass = ? WHERE pass_no = ?", txtPass.Text, u);
                if (u > 1 && u <= 5)
                    for (int i = 0; i < Cmds.Length; i++)
                        Db.X(tx, "UPDATE disply_command SET user" + u + " = ? WHERE command_no = ?", perms.GetItemChecked(i), Cmds[i]);
                Db.Log(tx, "تعديل مستخدم", n);
            });
            ComboItem it = (ComboItem)users.SelectedItem; it.Name = "مستخدم " + u + " • " + n;
            users.Items[users.SelectedIndex] = it;
            UI.Info("تم الحفظ.");
        }
    }
}
