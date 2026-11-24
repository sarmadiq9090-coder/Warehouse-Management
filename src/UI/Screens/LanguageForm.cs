using System.Drawing;
using System.Windows.Forms;
using System;

namespace Stores
{
    /// <summary>The very first start on a computer (before the connection window and the login): «Choose the
    /// program language» in both languages with two big buttons. The choice goes to settings.ini (Language=ar|en).</summary>
    class LanguageForm : Form
    {
        /// <summary>"ar" or "en" (closing the window = Arabic).</summary>
        public string Choice = "ar";

        public LanguageForm()
        {
            Text = Program.AppName;
            Font = Theme.Font; BackColor = Theme.Bg; Icon = Theme.AppIcon;
            FormBorderStyle = FormBorderStyle.FixedDialog; MaximizeBox = false; MinimizeBox = false;
            StartPosition = FormStartPosition.CenterScreen;
            ClientSize = new Size(520, 250);

            Label ar = new Label(); ar.Text = "اختر لغة البرنامج"; ar.RightToLeft = RightToLeft.Yes;
            ar.Font = new Font(Theme.FontName, 16f, FontStyle.Bold); ar.ForeColor = Theme.Text;
            ar.TextAlign = ContentAlignment.MiddleCenter; ar.Bounds = new Rectangle(20, 22, 480, 36);
            Label en = new Label(); en.Text = "Choose the program language";
            en.Font = new Font(Theme.LatinFontName, 13f); en.ForeColor = Theme.Muted;
            en.TextAlign = ContentAlignment.MiddleCenter; en.Bounds = new Rectangle(20, 60, 480, 30);

            Button bAr = UI.Btn("العربية", Theme.Primary, 220); bAr.Bounds = new Rectangle(270, 120, 220, 70);
            bAr.Font = new Font(Theme.FontName, 16f, FontStyle.Bold); bAr.RightToLeft = RightToLeft.Yes;
            Button bEn = UI.Btn("English", Theme.Primary, 220); bEn.Bounds = new Rectangle(30, 120, 220, 70);
            bEn.Font = new Font(Theme.LatinFontName, 16f, FontStyle.Bold);
            ((FluentButton)bAr).Glyph = ""; ((FluentButton)bEn).Glyph = "";
            bAr.Click += delegate { Choice = "ar"; DialogResult = DialogResult.OK; Close(); };
            bEn.Click += delegate { Choice = "en"; DialogResult = DialogResult.OK; Close(); };

            Label note = new Label(); note.Text = "يمكن تغييرها لاحقا من الشريط العلوي  •  You can change it later in the top bar";
            note.Font = Theme.FontSmall; note.ForeColor = Theme.Muted; note.TextAlign = ContentAlignment.MiddleCenter;
            note.Bounds = new Rectangle(10, 208, 500, 24);

            Controls.AddRange(new Control[] { ar, en, bAr, bEn, note });
            AcceptButton = bAr;
        }
    }
}
