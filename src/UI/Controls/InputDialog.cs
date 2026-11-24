using System.Collections.Generic;
using System.Data;
using System.Drawing.Drawing2D;
using System.Drawing;
using System.IO;
using System.Windows.Forms;
using System;

namespace Stores
{
    // ------------------------------------------------------------------ simple dialogs
    class InputDialog : BaseDialog
    {
        TextBox txt;
        public string Value { get { return txt.Text.Trim(); } }

        public InputDialog(string title, string prompt, string value)
        {
            Text = title;
            ClientSize = new Size(420, 150);
            Label l = UI.Lbl(prompt); l.Location = new Point(16, 16); l.ForeColor = Theme.Text;
            txt = UI.Text(388); txt.Location = new Point(16, 44); txt.Text = value ?? "";
            Button ok = UI.Btn("موافق", Theme.Primary, 100);
            Button cancel = UI.BtnLight("إلغاء", 90);
            Controls.Add(l); Controls.Add(txt); Controls.Add(ButtonBar(ok, cancel));
            AcceptButton = ok;
            ok.Click += delegate { DialogResult = DialogResult.OK; Close(); };
            cancel.Click += delegate { DialogResult = DialogResult.Cancel; Close(); };
        }

        public static string Ask(IWin32Window owner, string title, string prompt, string value)
        {
            using (InputDialog d = new InputDialog(title, prompt, value))
                return d.ShowDialog(owner) == DialogResult.OK ? d.Value : null;
        }

        public static string AskPassword(IWin32Window owner, string title, string prompt)
        {
            using (InputDialog d = new InputDialog(title, prompt, ""))
            {
                d.txt.UseSystemPasswordChar = true;
                d.txt.RightToLeft = RightToLeft.No;
                return d.ShowDialog(owner) == DialogResult.OK ? d.txt.Text : null;
            }
        }
    }
}
