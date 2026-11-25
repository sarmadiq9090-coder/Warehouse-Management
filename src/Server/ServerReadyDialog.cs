using System.Drawing;
using System.Windows.Forms;
using System;

namespace Stores
{
    /// <summary>The end of the one-file setup: the server password (made by the program) to copy and keep.</summary>
    class ServerReadyDialog : BaseDialog
    {
        public ServerReadyDialog(string password, bool newData, bool reboot)
        {
            Text = "تم التنصيب";
            int w = 520, y = 18;
            Label ok = Line("✓ هذا الحاسوب أصبح السيرفر الرئيسي.", y, w, 34, Theme.Success); ok.Font = new Font(Theme.FontName, 14f, FontStyle.Bold); y += 44;
            Line("كلمة سر السيرفر (للحواسيب الأخرى):", y, w, 26, Theme.Text); y += 30;
            TextBox t = UI.Text(300); t.Text = password; t.ReadOnly = true; t.RightToLeft = RightToLeft.No; t.TextAlign = HorizontalAlignment.Center;
            t.Font = new Font("Consolas", 16f, FontStyle.Bold); t.Bounds = new Rectangle(w - 316, y, 300, 36); Controls.Add(t);
            Button copy = UI.Btn("نسخ", Theme.Primary, 110); copy.Location = new Point(w - 316 - 120, y); Controls.Add(copy);
            copy.Click += delegate { try { Clipboard.SetText(password); copy.Text = "✓ " + Lang.T("نسخ"); } catch { } };
            y += 48;
            Line("صوّرها أو اكتبها الآن: لا تظهر مرة أخرى.", y, w, 26, Theme.Warning); y += 34;
            if (newData) { Line("الدخول أول مرة: المستخدم 1 وكلمة المرور 1", y, w, 26, Theme.Text); y += 30; }
            if (reboot) { Line("⚠ أعد تشغيل هذا الحاسوب.", y, w, 26, Theme.Danger); y += 30; }
            Button done = UI.Btn("دخول البرنامج", Theme.Accent, 150);
            Controls.Add(ButtonBar(done));
            done.Click += delegate { DialogResult = DialogResult.OK; Close(); };
            ClientSize = new Size(w, y + 62);
            StartPosition = FormStartPosition.CenterScreen; ShowInTaskbar = true; TopMost = true;
        }

        Label Line(string text, int y, int w, int h, Color c)
        {
            Label l = UI.Lbl(text); l.AutoSize = false; l.Bounds = new Rectangle(16, y, w - 32, h); l.ForeColor = c; l.TextAlign = ContentAlignment.MiddleLeft;
            Controls.Add(l); return l;
        }
    }
}
