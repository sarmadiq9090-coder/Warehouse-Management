using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing;
using System.IO;
using System.Windows.Forms;
using System;

namespace Stores
{
    /// <summary>«الإعدادات العامة» (administrator): company name and optional logo.</summary>
    class GeneralSettingsForm : BaseDialog
    {
        TextBox txtCompany; PictureBox pic; byte[] newLogo; bool logoChanged;

        public GeneralSettingsForm()
        {
            Text = "الإعدادات العامة";
            ClientSize = new Size(620, 430);
            TableLayoutPanel t = FormTable();
            txtCompany = UI.Text(380); txtCompany.MaxLength = 120; txtCompany.Text = Branding.Company;
            AddRow(t, "اسم الشركة", txtCompany);

            pic = new PictureBox(); pic.Size = new Size(380, 90); pic.SizeMode = PictureBoxSizeMode.Zoom; pic.BorderStyle = BorderStyle.FixedSingle; pic.BackColor = Color.White;
            ShowLogo(Branding.LogoBytes);
            AddRow(t, "الشعار (اختياري)", pic);
            FlowLayoutPanel lb = UI.Flow();
            Button choose = UI.BtnLight("اختيار صورة…", 130), remove = UI.BtnLight("إزالة الشعار", 120);
            lb.Controls.Add(choose); lb.Controls.Add(remove);
            AddRow(t, "", lb);
            Label lh = UI.Lbl("يظهر في الشاشات والتقارير المطبوعة وملفات Excel. بدون شعار يظهر اسم الشركة.");
            lh.ForeColor = Theme.Muted; lh.AutoSize = true; lh.MaximumSize = new Size(390, 0);
            AddRow(t, "", lh);

            Label rh = UI.Lbl("الإصدار " + Program.Version + " • التحديث التلقائي: " + (Updater.Repo.Length > 0 ? "من GitHub (" + Updater.Repo + ")" : "غير مفعّل في هذه النسخة"));
            rh.ForeColor = Theme.Muted; rh.AutoSize = true; rh.MaximumSize = new Size(390, 0);
            AddRow(t, "التحديثات", rh);

            Button save = UI.Btn("حفظ", Theme.Accent, 110), cancel = UI.BtnLight("إلغاء", 90);
            Controls.Add(t); Controls.Add(ButtonBar(save, cancel));

            choose.Click += delegate
            {
                using (OpenFileDialog d = new OpenFileDialog())
                {
                    d.Title = "اختر صورة الشعار";
                    d.Filter = "صور (*.png;*.jpg;*.jpeg;*.bmp;*.gif)|*.png;*.jpg;*.jpeg;*.bmp;*.gif";
                    if (d.ShowDialog(this) != DialogResult.OK) return;
                    try
                    {
                        byte[] b = File.ReadAllBytes(d.FileName);
                        using (MemoryStream ms = new MemoryStream(b)) using (Image.FromStream(ms)) { }   // is it a picture?
                        newLogo = b; logoChanged = true; ShowLogo(b);
                    }
                    catch { UI.Warn("الملف ليس صورة صالحة."); }
                }
            };
            remove.Click += delegate { newLogo = null; logoChanged = true; ShowLogo(null); };
            save.Click += delegate { Save(); };
            cancel.Click += delegate { Close(); };
        }

        void ShowLogo(byte[] b)
        {
            Image old = pic.Image; pic.Image = null; if (old != null) old.Dispose();
            if (b == null) return;
            try { using (MemoryStream ms = new MemoryStream(b)) using (Image i = Image.FromStream(ms)) pic.Image = new Bitmap(i); } catch { }
        }

        void Save()
        {
            try
            {
                Branding.SetCompany(txtCompany.Text);
                if (logoChanged) Branding.SetLogo(newLogo);
                Db.Log(null, "الإعدادات العامة", "الشركة: " + Branding.Company + " — الشعار: " + (Branding.LogoBytes != null ? "موجود" : "بدون"));
                UI.Info("تم الحفظ ✓\nيظهر الاسم والشعار الجديدان كاملين بعد إعادة فتح البرنامج.");
                DialogResult = DialogResult.OK;
                Close();
            }
            catch (Exception ex) { UI.Error(ex); }
        }
    }
}
