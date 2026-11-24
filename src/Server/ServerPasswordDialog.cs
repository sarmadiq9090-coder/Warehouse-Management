using System.Drawing;
using System.Windows.Forms;
using System;

namespace Stores
{
    /// <summary>Asks the server password: chosen (typed twice) on the main server, typed once on every other computer.</summary>
    class ServerPasswordDialog : BaseDialog
    {
        TextBox txt1, txt2;
        Label lblErr;
        bool isNew;

        ServerPasswordDialog(bool choose, string note, bool noteIsError)
        {
            isNew = choose;
            Text = choose ? "السيرفر الرئيسي — كلمة سر السيرفر" : "سيرفر ثانوي — كلمة سر السيرفر";
            int w = 560;
            string help = choose
                ? "اختر كلمة سر للسيرفر (" + ServerSetup.MinPasswordLength + " أحرف على الأقل).\n" +
                  "كل حاسوب آخر سيحتاجها مرة واحدة فقط عند الضغط على «سيرفر ثانوي».\n" +
                  "احفظها في مكان آمن، ولا تعطها إلا لمن يجهّز البرنامج على الحواسيب.\n" +
                  "إذا كان السيرفر مجهزا مسبقا: اكتب كلمة السر نفسها (كلمة سر جديدة تعني إعادة «سيرفر ثانوي» على كل حاسوب)."
                : "اكتب كلمة سر السيرفر التي اختيرت عند تجهيز «السيرفر الرئيسي».\n" +
                  "تُطلب مرة واحدة فقط على هذا الحاسوب، ثم تُحفظ فيه مشفرة.";
            int y = 14;
            if (!string.IsNullOrEmpty(note))
            {
                Label n = Lbl(note, new Rectangle(16, y, w - 32, 46)); n.Font = Theme.FontBold;
                n.ForeColor = noteIsError ? Theme.Danger : Theme.Primary;
                y += 50;
            }
            Label h = Lbl(help, new Rectangle(16, y, w - 32, choose ? 110 : 48)); y += h.Height + 10;
            Lbl("كلمة سر السيرفر", new Rectangle(16, y, w - 32, 24)); y += 26;
            txt1 = Box(y); y += 38;
            if (choose)
            {
                Lbl("أعد كتابة كلمة السر للتأكيد", new Rectangle(16, y, w - 32, 24)); y += 26;
                txt2 = Box(y); y += 38;
            }
            CheckBox show = new CheckBox(); show.Text = "إظهار كلمة السر"; show.AutoSize = true; show.Location = new Point(w - 160, y); Controls.Add(show);
            show.CheckedChanged += delegate { txt1.UseSystemPasswordChar = !show.Checked; if (txt2 != null) txt2.UseSystemPasswordChar = !show.Checked; };
            y += 30;
            lblErr = Lbl("", new Rectangle(16, y, w - 32, 24)); lblErr.ForeColor = Theme.Danger; y += 30;

            Button ok = UI.Btn("موافق", Theme.Primary, 100);
            Button cancel = UI.BtnLight("إلغاء", 90);
            Controls.Add(ButtonBar(ok, cancel));
            ClientSize = new Size(w, y + 56);
            AcceptButton = ok;
            ok.Click += delegate
            {
                string p = txt1.Text.Trim();
                if (p.Length == 0) { Fail("اكتب كلمة سر السيرفر."); return; }
                if (isNew)
                {
                    if (p.Length < ServerSetup.MinPasswordLength) { Fail("كلمة السر قصيرة: " + ServerSetup.MinPasswordLength + " أحرف على الأقل."); return; }
                    if (txt2.Text.Trim() != p) { Fail("كلمتا السر غير متطابقتين. اكتبهما مرة أخرى."); txt2.Text = ""; txt2.Focus(); return; }
                }
                DialogResult = DialogResult.OK; Close();
            };
            cancel.Click += delegate { DialogResult = DialogResult.Cancel; Close(); };
            Shown += delegate { txt1.Focus(); };
        }

        Label Lbl(string text, Rectangle r)
        {
            Label l = UI.Lbl(text); l.AutoSize = false; l.Bounds = r; l.ForeColor = Theme.Text; l.TextAlign = ContentAlignment.MiddleLeft;
            Controls.Add(l); return l;
        }

        TextBox Box(int y)
        {
            TextBox t = UI.Text(300); t.Location = new Point(560 - 16 - 300, y); t.UseSystemPasswordChar = true; t.RightToLeft = RightToLeft.No;
            Controls.Add(t); return t;
        }

        void Fail(string m) { lblErr.Text = m; txt1.Focus(); txt1.SelectAll(); }

        /// <summary>Main server: the owner chooses the password (twice). null = cancelled.</summary>
        public static string AskNew(IWin32Window owner)
        {
            using (ServerPasswordDialog d = new ServerPasswordDialog(true, null, false))
                return d.ShowDialog(owner) == DialogResult.OK ? d.txt1.Text.Trim() : null;
        }

        /// <summary>Other computers: the password chosen on the main server (once). null = cancelled.</summary>
        public static string AskExisting(IWin32Window owner, string note, bool noteIsError)
        {
            using (ServerPasswordDialog d = new ServerPasswordDialog(false, note, noteIsError))
                return d.ShowDialog(owner) == DialogResult.OK ? d.txt1.Text.Trim() : null;
        }
    }
}
