using System.Drawing;
using System.IO;
using System.Windows.Forms;
using System;

namespace Stores
{
    /// <summary>Shown when the SQL Server package is not on this computer (or the one found is not usable): pick the file from
    /// a flash drive / the network, download it, or cancel. Nothing is downloaded unless the user chooses it here.</summary>
    class SqlPackageDialog : BaseDialog
    {
        public enum Choice { Cancel, File, Download }

        public Choice Result = Choice.Cancel;
        /// <summary>The picked file (Choice.File).</summary>
        public string Picked;

        /// <summary>note: why the last file / download was not accepted (null = first time); partBytes: size of an unfinished download.</summary>
        public SqlPackageDialog(string note, long partBytes, long size)
        {
            Text = "السيرفر الرئيسي • حزمة SQL Server";
            const int w = 580, x = 18;
            int y = 14;
            if (!string.IsNullOrEmpty(note))
            {
                Label n = Lbl(note, x, ref y, w, Theme.FontBold); n.ForeColor = Theme.Danger;
                y += 8;
            }
            Lbl("لتجهيز السيرفر الرئيسي يحتاج البرنامج ملف SQL Server المجاني من مايكروسوفت،\nولم يجده على هذا الحاسوب: الملف " + SqlPackage.FileName + " في مجلد «مكونات».", x, ref y, w, Theme.Font);
            y += 6;
            Lbl("بدون إنترنت (الأسرع): صل الفلاش الذي فيه «النسخة الكاملة بدون إنترنت» واختر الملف منه،\nأو اختره من حاسوب آخر في الشبكة.", x, ref y, w, Theme.Font);
            y += 4;
            Button file = UI.Btn("اختيار الملف من فلاش أو الشبكة", Theme.Primary, w - 2 * x);
            ((FluentButton)file).Glyph = "\uE8E5";
            file.Height = 42; file.Location = new Point(x, y); Controls.Add(file); y += 42 + 14;

            Button down = UI.BtnLight("تنزيل من الإنترنت (" + SqlPackage.Mb(size) + " ميغابايت)", w - 2 * x);
            ((FluentButton)down).Glyph = "";
            down.Height = 42; down.Location = new Point(x, y); Controls.Add(down); y += 42 + 4;
            Label dl = Lbl((partBytes > 0 ? "يُكمل التنزيل السابق: نزل منه " + SqlPackage.Mb(partBytes) + " ميغابايت.\n" : "") +
                "من موقع مايكروسوفت مباشرة. المدة حسب سرعة الإنترنت، وإذا انقطع الاتصال يُكمل من حيث توقف.", x, ref y, w, Theme.FontSmall);
            dl.ForeColor = Theme.Muted;
            y += 8;

            Button cancel = UI.BtnLight("إلغاء", 100);
            Controls.Add(ButtonBar(cancel));
            ClientSize = new Size(w, y + 54);
            AcceptButton = file; CancelButton = cancel;

            file.Click += delegate { Pick(); };
            down.Click += delegate { Result = Choice.Download; DialogResult = DialogResult.OK; Close(); };
            cancel.Click += delegate { DialogResult = DialogResult.Cancel; Close(); };
            Shown += delegate { file.Focus(); };
        }

        Label Lbl(string text, int x, ref int y, int w, Font f)
        {
            Label l = UI.Lbl(text); l.AutoSize = false; l.Font = f; l.ForeColor = Theme.Text; l.TextAlign = ContentAlignment.TopLeft; l.UseMnemonic = false;
            // tall enough in both languages (the English text is put in when the dialog opens)
            TextFormatFlags flags = TextFormatFlags.WordBreak;
            int h = Math.Max(TextRenderer.MeasureText(text, f, new Size(w - 2 * x - 8, 0), flags).Height,
                             Lang.English ? TextRenderer.MeasureText(Lang.T(text), f, new Size(w - 2 * x - 8, 0), flags).Height : 0);
            l.Bounds = new Rectangle(x, y, w - 2 * x, h + 4);
            y += h + 4;
            Controls.Add(l); return l;
        }

        void Pick()
        {
            using (OpenFileDialog d = new OpenFileDialog())
            {
                d.Title = Lang.T("اختر ملف حزمة SQL Server:") + " " + SqlPackage.FileName;
                d.Filter = SqlPackage.FileName + "|SQLEXPR*.exe|" + Lang.T("برامج") + " (*.exe)|*.exe";
                try { string dir = Flash(); if (dir != null) d.InitialDirectory = dir; } catch { }
                if (d.ShowDialog(this) != DialogResult.OK) return;
                Picked = d.FileName;
            }
            Result = Choice.File; DialogResult = DialogResult.OK; Close();
        }

        /// <summary>The first ready flash drive, or null.</summary>
        static string Flash()
        {
            foreach (DriveInfo d in DriveInfo.GetDrives()) if (d.DriveType == DriveType.Removable && d.IsReady) return d.Name;
            return null;
        }
    }
}
