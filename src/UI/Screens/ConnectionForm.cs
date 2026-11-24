using System.Collections.Generic;
using System.Data;
using System.Drawing.Drawing2D;
using System.Drawing;
using System.IO;
using System.Windows.Forms;
using System;

namespace Stores
{
    // ------------------------------------------------------------------ connection settings
    class ConnectionForm : BaseDialog
    {
        RadioButton rAccess, rSql;
        TextBox txtPath, txtServer, txtDb, txtUser, txtPass;
        CheckBox chkTrust;
        Panel pAccess, pSql;

        public ConnectionForm()
        {
            Text = "إعدادات الاتصال والسيرفر";
            ClientSize = new Size(760, 680);

            BrandBar brand = new BrandBar(); brand.Dock = DockStyle.Top;

            FlowLayoutPanel body = new FlowLayoutPanel();
            body.Dock = DockStyle.Fill; body.FlowDirection = FlowDirection.TopDown; body.WrapContents = false; body.Padding = new Padding(16, 10, 16, 0);
            body.AutoScroll = true;

            // one-time network setup (moved here from the login screen so it is not clicked by mistake)
            Panel pNet = new Panel(); pNet.Size = new Size(710, 140); pNet.BackColor = Theme.Card; pNet.Margin = new Padding(3, 3, 3, 10);
            Label netTitle = new Label(); netTitle.Text = "تجهيز الشبكة • مرة واحدة لكل حاسوب"; netTitle.Font = Theme.FontBold; netTitle.ForeColor = Theme.Primary;
            netTitle.AutoSize = false; netTitle.Bounds = new Rectangle(10, 6, 690, 26); netTitle.TextAlign = ContentAlignment.MiddleLeft;
            Button bMain = UI.Btn("السيرفر الرئيسي", Theme.PrimaryDark, 160); bMain.Bounds = new Rectangle(540, 38, 160, 40);
            Label dMain = NetNote("على الحاسوب الذي سيحفظ البيانات فقط: يُنصّب السيرفر المجاني وينقل البيانات إليه،\nوتختار فيه «كلمة سر السيرفر». يحتاج صلاحيات المدير على هذا الحاسوب.", 38);
            Button bSec = UI.Btn("سيرفر ثانوي", Theme.Accent, 160); bSec.Bounds = new Rectangle(540, 88, 160, 40);
            Label dSec = NetNote("على كل حاسوب آخر: يجد السيرفر الرئيسي في الشبكة ويتصل به\nبعد كتابة «كلمة سر السيرفر» مرة واحدة.", 88);
            pNet.Controls.AddRange(new Control[] { netTitle, bMain, dMain, bSec, dSec });
            body.Controls.Add(pNet);

            rSql = new RadioButton(); rSql.Text = "سيرفر SQL Server • للعمل من أي مكان ومن عدة حواسيب (موصى به)"; rSql.AutoSize = true; rSql.Font = Theme.FontBold;
            pSql = new Panel(); pSql.Size = new Size(590, 240);
            TableLayoutPanel ts = FormTable(); ts.Padding = new Padding(20, 0, 0, 0);
            txtServer = UI.Text(300); txtDb = UI.Text(300); txtUser = UI.Text(300); txtPass = UI.Text(300); txtPass.UseSystemPasswordChar = true;
            txtServer.RightToLeft = RightToLeft.No; txtDb.RightToLeft = RightToLeft.No; txtUser.RightToLeft = RightToLeft.No;
            chkTrust = new CheckBox(); chkTrust.Text = "الوثوق بشهادة السيرفر (لخادم خاص بالشركة)"; chkTrust.AutoSize = true;
            AddRow(ts, "عنوان السيرفر", txtServer);
            Label sh = UI.Lbl("مثال:  SERVER-PC,1433   (أو زر «سيرفر ثانوي» في الأعلى)"); sh.Font = Theme.FontSmall; sh.Margin = new Padding(4, 0, 4, 2);
            AddRow(ts, "", sh);
            AddRow(ts, "قاعدة البيانات", txtDb);
            AddRow(ts, "اسم المستخدم", txtUser);
            AddRow(ts, "كلمة المرور", txtPass);
            AddRow(ts, "", chkTrust);
            pSql.Controls.Add(ts);

            rAccess = new RadioButton(); rAccess.Text = "ملف Access (.AMR) • حاسوب واحد أو مجلد مشترك على الشبكة المحلية"; rAccess.AutoSize = true; rAccess.Font = Theme.FontBold;
            rAccess.Margin = new Padding(3, 12, 3, 3);
            pAccess = new Panel(); pAccess.Size = new Size(590, 84);
            txtPath = UI.Text(470); txtPath.Location = new Point(100, 6); txtPath.RightToLeft = RightToLeft.No;
            Button browse = UI.BtnLight("استعراض…", 90); browse.Location = new Point(4, 4); browse.Height = 30;
            Button create = UI.BtnLight("لا توجد لدي بيانات • إنشاء بيانات جديدة", 290); create.Location = new Point(280, 46); create.Height = 30;
            pAccess.Controls.Add(txtPath); pAccess.Controls.Add(browse); pAccess.Controls.Add(create);
            create.Click += delegate
            {
                using (SaveFileDialog d = new SaveFileDialog())
                {
                    d.Title = "مكان ملف البيانات الجديد";
                    d.Filter = "ملف بيانات المخازن (*.AMR)|*.AMR";
                    d.FileName = "بيانات المخازن.AMR";
                    d.InitialDirectory = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
                    if (d.ShowDialog(this) != DialogResult.OK) return;
                    string err = NewDatabase.Create(d.FileName);
                    if (err != null) { UI.Warn("تعذر إنشاء البيانات:\n" + err); return; }
                    txtPath.Text = d.FileName; rAccess.Checked = true;
                    Settings.Mode = "access"; Settings.DataPath = d.FileName; Settings.Save();
                    UI.Info("✓ تم إنشاء بيانات جديدة فارغة:\n" + d.FileName + "\n\nادخل باسم المستخدم 1 وكلمة المرور 1، ثم غيّر كلمة المرور.\nابدأ بإضافة المواد من شاشة «بطاقة مادة».");
                    DialogResult = DialogResult.OK; Close();
                }
            };

            body.Controls.Add(rSql); body.Controls.Add(pSql); body.Controls.Add(rAccess); body.Controls.Add(pAccess);

            Button test = UI.BtnLight("اختبار الاتصال", 130);
            Button ok = UI.Btn("حفظ واتصال", Theme.Primary, 130);
            Button cancel = UI.BtnLight("إلغاء", 90);
            Controls.Add(body); Controls.Add(brand); Controls.Add(ButtonBar(ok, test, cancel));
            bMain.Click += delegate
            {
                if (!UI.Confirm("«السيرفر الرئيسي» يُستعمل على حاسوب واحد فقط: الحاسوب الذي سيحفظ بيانات الشركة ويبقى شغالا.\n" +
                                "سيُنصّب عليه SQL Server Express (عادة 4–10 دقائق في المرة الأولى، بدون إنترنت).\n\n" +
                                "على الحواسيب الأخرى استعمل «سيرفر ثانوي».\n\nهل هذا الحاسوب هو السيرفر الرئيسي؟")) return;
                if (ServerSetup.RunMain(this)) { DialogResult = DialogResult.OK; Close(); }
            };
            bSec.Click += delegate { if (ServerSetup.RunSecondary(this)) { DialogResult = DialogResult.OK; Close(); } };

            txtServer.Text = Settings.SqlServer; txtDb.Text = Settings.SqlDatabase; txtUser.Text = Settings.SqlUser; txtPass.Text = Settings.SqlPassword;
            chkTrust.Checked = Settings.SqlTrust;
            txtPath.Text = Settings.DataPath ?? "";
            if (Settings.Mode == "sql") rSql.Checked = true; else rAccess.Checked = true;
            EventHandler sync = delegate { pSql.Enabled = rSql.Checked; pAccess.Enabled = rAccess.Checked; };
            rSql.CheckedChanged += sync; rAccess.CheckedChanged += sync; sync(null, null);

            browse.Click += delegate { string p = Program.AskDataFile(); if (p != null) txtPath.Text = p; };
            cancel.Click += delegate { DialogResult = DialogResult.Cancel; Close(); };
            test.Click += delegate
            {
                string err = TryConnect();
                if (err == null) UI.Info("تم الاتصال بنجاح ✓\n" + Db.Describe); else UI.Warn("فشل الاتصال:\n" + err);
            };
            ok.Click += delegate
            {
                string err = TryConnect();
                if (err != null) { UI.Warn("فشل الاتصال:\n" + err); return; }
                if (rSql.Checked)
                {
                    Settings.Mode = "sql"; Settings.SqlServer = txtServer.Text.Trim(); Settings.SqlDatabase = txtDb.Text.Trim();
                    Settings.SqlUser = txtUser.Text.Trim(); Settings.SqlPassword = txtPass.Text; Settings.SqlTrust = chkTrust.Checked;
                }
                else { Settings.Mode = "access"; Settings.DataPath = txtPath.Text.Trim(); }
                Settings.Save();
                if (Db.IsSql && !Db.TableExists("item"))
                {
                    if (UI.Confirm("قاعدة البيانات على السيرفر فارغة.\nهل تريد نقل البيانات إليها الآن من ملف Access؟"))
                    {
                        if (!Migrate.Run(this)) return;
                    }
                    else return;
                }
                DialogResult = DialogResult.OK; Close();
            };
        }

        static Label NetNote(string text, int y)
        {
            Label l = new Label(); l.Text = text; l.AutoSize = false; l.Bounds = new Rectangle(10, y, 520, 40);
            l.TextAlign = ContentAlignment.MiddleLeft; l.ForeColor = Theme.Text; l.Font = Theme.FontSmall;
            return l;
        }

        string TryConnect()
        {
            UI.WaitCursor(true);
            try
            {
                if (rSql.Checked)
                {
                    if (txtServer.Text.Trim().Length == 0) return "أدخل عنوان السيرفر.";
                    if (txtDb.Text.Trim().Length == 0) return "أدخل اسم قاعدة البيانات.";
                    return Db.InitSql(Db.BuildSqlConnStr(txtServer.Text.Trim(), txtDb.Text.Trim(), txtUser.Text.Trim(), txtPass.Text, chkTrust.Checked));
                }
                string p = txtPath.Text.Trim();
                if (!File.Exists(p)) return "الملف غير موجود.";
                return Db.InitAccess(p);
            }
            finally { UI.WaitCursor(false); }
        }
    }
}
