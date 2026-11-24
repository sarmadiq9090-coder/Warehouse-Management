using System.Diagnostics;
using System.Drawing.Imaging;
using System.Drawing;
using System.IO.Compression;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Windows.Forms;
using System;

namespace Stores
{
    /// <summary>«تبليغ عن مشكلة»: the user describes the problem; the program
    /// (1) records it in the activity log so the administrator sees it on every computer,
    /// (2) saves a zip (description + screenshot + technical details) on the Desktop to send by WhatsApp / e-mail,
    /// (3) when an update repository is set, opens a pre-filled GitHub issue page (the screenshot is put on the clipboard).</summary>
    class ProblemReportForm : BaseDialog
    {
        TextBox txt; CheckBox chkShot, chkTech; Bitmap shot;

        public static void Open(Form owner)
        {
            Bitmap b = null;
            try
            {
                if (owner != null && owner.Visible)
                {
                    b = new Bitmap(owner.Width, owner.Height);
                    owner.DrawToBitmap(b, new Rectangle(0, 0, owner.Width, owner.Height));
                }
            }
            catch { b = null; }
            using (ProblemReportForm f = new ProblemReportForm(b)) f.ShowDialog(owner);
        }

        ProblemReportForm(Bitmap screenshot)
        {
            shot = screenshot;
            Text = "تبليغ عن مشكلة";
            ClientSize = new Size(600, 470);
            Panel body = new Panel(); body.Dock = DockStyle.Fill; body.Padding = new Padding(16, 10, 16, 6); body.BackColor = Theme.Card;
            Label t = new Label(); t.Text = "تبليغ عن مشكلة أو اقتراح"; t.Font = Theme.FontTitle; t.ForeColor = Theme.Primary; t.Dock = DockStyle.Top; t.Height = 40;
            Label h = new Label(); h.Dock = DockStyle.Top; h.Height = 46; h.ForeColor = Theme.Muted;
            h.Text = "اكتب ماذا كنت تفعل، وماذا ظهر لك، وماذا كنت تتوقع.\nمثال: عند حفظ سند إخراج رقم 120 ظهرت رسالة خطأ، والمادة لم تُخصم.";
            txt = new TextBox(); txt.Multiline = true; txt.Dock = DockStyle.Fill; txt.ScrollBars = ScrollBars.Vertical; txt.Font = Theme.Font;
            chkShot = new CheckBox(); chkShot.Text = "إرفاق صورة شاشة البرنامج"; chkShot.Checked = shot != null; chkShot.Enabled = shot != null; chkShot.Dock = DockStyle.Bottom; chkShot.Height = 28;
            chkTech = new CheckBox(); chkTech.Text = "إرفاق المعلومات الفنية (الإصدار، ويندوز، نوع الاتصال، آخر الأخطاء)"; chkTech.Checked = true; chkTech.Dock = DockStyle.Bottom; chkTech.Height = 28;
            LinkLabel preview = new LinkLabel(); preview.Text = "عرض المعلومات الفنية التي سترفق"; preview.Dock = DockStyle.Bottom; preview.Height = 24; preview.LinkColor = Theme.AccentDark;
            body.Controls.Add(txt); body.Controls.Add(h); body.Controls.Add(t); body.Controls.Add(chkShot); body.Controls.Add(chkTech); body.Controls.Add(preview);

            Button send = UI.Btn("إرسال التبليغ", Theme.Accent, 140);
            Button cancel = UI.BtnLight("إلغاء", 90);
            Controls.Add(body); Controls.Add(ButtonBar(send, cancel));
            preview.LinkClicked += delegate { UI.Info(TechInfo()); };
            send.Click += delegate { Send(); };
            cancel.Click += delegate { Close(); };
        }

        /// <summary>What may go on the PUBLIC GitHub page: no paths, machine or user names, no error log.</summary>
        static string PublicInfo()
        {
            StringBuilder sb = new StringBuilder();
            sb.AppendLine("البرنامج: " + Program.AppName + " " + Program.Version);
            sb.AppendLine("ويندوز: " + Environment.OSVersion.VersionString + (Environment.Is64BitOperatingSystem ? " (64 بت)" : " (32 بت)"));
            sb.AppendLine("الاتصال: " + (Db.IsSql ? "السيرفر (SQL Server)" : "ملف Access"));
            return sb.ToString();
        }

        /// <summary>Full details, only for the zip on the Desktop (sent privately).</summary>
        static string TechInfo()
        {
            StringBuilder sb = new StringBuilder(PublicInfo());
            sb.AppendLine("الحاسوب: " + Environment.MachineName + "    المستخدم في البرنامج: " + Session.UserName);
            sb.AppendLine("الوقت: " + DateTime.Now.ToString("yyyy/MM/dd HH:mm"));
            string log = Path.Combine(Settings.AppDir, "errors.log");
            if (File.Exists(log))
            {
                try
                {
                    string all = File.ReadAllText(log, Encoding.UTF8);
                    if (all.Length > 3000) all = "…" + all.Substring(all.Length - 3000);
                    sb.AppendLine().AppendLine("آخر الأخطاء المسجلة:").AppendLine(all.Trim());
                }
                catch { }
            }
            return sb.ToString();
        }

        void Send()
        {
            string desc = txt.Text.Trim();
            if (desc.Length < 5) { UI.Warn("اكتب وصفا للمشكلة أولا."); txt.Focus(); return; }
            string tech = chkTech.Checked ? TechInfo() : "";
            string stamp = DateTime.Now.ToString("yyyy-MM-dd HHmm");

            // 1. activity log (seen by the administrator on every computer)
            try { Db.Log(null, "تبليغ عن مشكلة", desc.Length > 240 ? desc.Substring(0, 240) + "…" : desc); } catch { }

            // 2. zip on the desktop
            string zip = null;
            try
            {
                string desk = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
                zip = Path.Combine(desk, "تبليغ مشكلة - مخازن - " + stamp + ".zip");
                if (File.Exists(zip)) File.Delete(zip);
                using (ZipArchive z = ZipFile.Open(zip, ZipArchiveMode.Create))
                {
                    ZipArchiveEntry e = z.CreateEntry("التبليغ.txt");
                    using (StreamWriter w = new StreamWriter(e.Open(), new UTF8Encoding(true))) { w.WriteLine(desc); w.WriteLine(); w.WriteLine(tech); }
                    if (chkShot.Checked && shot != null)
                    {
                        ZipArchiveEntry im = z.CreateEntry("صورة الشاشة.png");
                        using (Stream s = im.Open()) shot.Save(s, ImageFormat.Png);
                    }
                }
            }
            catch (Exception ex) { zip = null; UI.Warn("تعذر حفظ ملف التبليغ على سطح المكتب: " + ex.Message); }

            // 3. GitHub issue (when an update repository is set)
            string repo = Updater.Repo;
            bool web = false;
            if (repo.Length > 0 && UI.Confirm("هل تريد فتح صفحة التبليغ على GitHub أيضا؟\n\n⚠ صفحة GitHub عامة يراها الجميع: لا تكتب فيها أسماء أو أرقاما أو بيانات خاصة بالشركة،\nولا تلصق صورة شاشة فيها بيانات. تُرفق فيها فقط نسخة البرنامج وويندوز ونوع الاتصال.\n(تحتاج حسابا على GitHub. صورة الشاشة ستُنسخ لتلصقها بـ Ctrl+V إن أردت)"))
            {
                try
                {
                    string title = desc.Split('\n')[0].Trim(); if (title.Length > 80) title = title.Substring(0, 80) + "…";
                    string body = desc + "\n\n---\n" + (chkTech.Checked ? PublicInfo().Replace("\r", "") : "");
                    if (body.Length > 5000) body = body.Substring(0, 5000) + "…";
                    string url = "https://github.com/" + repo + "/issues/new?title=" + Uri.EscapeDataString(title) + "&body=" + Uri.EscapeDataString(body);
                    if (chkShot.Checked && shot != null) Clipboard.SetImage(shot);
                    Process.Start(url);
                    web = true;
                }
                catch (Exception ex) { UI.Warn("تعذر فتح صفحة GitHub: " + ex.Message); }
            }

            UI.Info("✓ شكرا، تم تسجيل التبليغ.\n\n• سُجّل في «سجل العمليات» ليراه المدير." +
                    (zip != null ? "\n• حُفظ ملف على سطح المكتب يمكنك إرساله (واتساب / بريد):\n" + Path.GetFileName(zip) : "") +
                    (web ? "\n• فُتحت صفحة GitHub: الصق صورة الشاشة بـ Ctrl+V ثم اضغط Submit." : ""));
            Close();
        }
    }
}
