using System.Collections.Generic;
using System.Data;
using System.Diagnostics;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Windows.Forms;
using System;

namespace Stores
{
    /// <summary>The «إقفال السنة» window (Tools screen, administrator).</summary>
    class YearCloseForm : BaseDialog
    {
        ComboBox cmbYear; CheckBox chkBackup, chkArchive; Label lblStatus, lblCount;
        Button bClose, bReopen;

        public YearCloseForm()
        {
            Text = "إقفال السنة";
            ClientSize = new Size(560, 440);
            Panel body = new Panel(); body.Dock = DockStyle.Fill; body.Padding = new Padding(18, 12, 18, 8); body.BackColor = Theme.Card;

            Label title = new Label(); title.Text = "إقفال السنة (يدوي، للمدير)"; title.Font = Theme.FontTitle; title.ForeColor = Theme.Primary; title.Dock = DockStyle.Top; title.Height = 40;
            lblStatus = new Label(); lblStatus.Dock = DockStyle.Top; lblStatus.Height = 30; lblStatus.Font = Theme.FontBold;
            Label explain = new Label(); explain.Dock = DockStyle.Top; explain.Height = 150; explain.ForeColor = Theme.Text;
            explain.Text = "عند إقفال السنة:\n" +
                           "• تؤخذ نسخة احتياطية من البيانات.\n" +
                           "• يُحفظ «جرد نهاية السنة» (رصيد كل مادة في كل مخزن بتاريخ 31/12) كملف Excel للأرشيف.\n" +
                           "• لا يمكن بعدها إضافة أو تعديل أو حذف أي سند بتاريخ في تلك السنة أو قبلها.\n" +
                           "• الأرصدة لا تتغير ولا تُنقل: رصيد نهاية السنة هو نفسه رصيد بداية السنة الجديدة تلقائيا.\n" +
                           "• يمكن للمدير إعادة فتح آخر سنة مقفلة إذا احتاج تصحيحا.";
            FlowLayoutPanel yr = UI.Flow(); yr.Dock = DockStyle.Top; yr.Height = 44;
            cmbYear = UI.Combo(120);
            lblCount = new Label(); lblCount.AutoSize = true; lblCount.ForeColor = Theme.Muted; lblCount.Margin = new Padding(10, 10, 4, 0);
            yr.Controls.Add(UI.Field("السنة المراد إقفالها", cmbYear)); yr.Controls.Add(lblCount);
            chkBackup = new CheckBox(); chkBackup.Text = "أخذ نسخة احتياطية قبل الإقفال"; chkBackup.Checked = true; chkBackup.Dock = DockStyle.Top; chkBackup.Height = 28;
            chkArchive = new CheckBox(); chkArchive.Text = "حفظ جرد نهاية السنة (Excel) في: المستندات ← مخازن الشركة الهندسية - إقفال السنوات"; chkArchive.Checked = true; chkArchive.Dock = DockStyle.Top; chkArchive.Height = 28;

            body.Controls.Add(chkArchive); body.Controls.Add(chkBackup); body.Controls.Add(yr); body.Controls.Add(explain); body.Controls.Add(lblStatus); body.Controls.Add(title);

            bClose = UI.Btn("إقفال السنة", Theme.Danger, 140);
            bReopen = UI.BtnLight("إعادة فتح آخر سنة مقفلة", 190);
            Button bArchive = UI.BtnLight("فتح مجلد الأرشيف", 140);
            Button bExit = UI.BtnLight("إغلاق", 80);
            Controls.Add(body); Controls.Add(ButtonBar(bClose, bReopen, bArchive, bExit));

            cmbYear.SelectedIndexChanged += delegate { ShowCount(); };
            bClose.Click += delegate { DoClose(); };
            bReopen.Click += delegate { DoReopen(); };
            bArchive.Click += delegate { try { Directory.CreateDirectory(YearClose.ArchiveDir); Process.Start("explorer.exe", "\"" + YearClose.ArchiveDir + "\""); } catch { } };
            bExit.Click += delegate { Close(); };
            Refresh2();
        }

        void Refresh2()
        {
            int closed = YearClose.ClosedYear();
            lblStatus.Text = closed == 0 ? "الحالة: لا توجد سنة مقفلة." : "الحالة: آخر سنة مقفلة " + closed + " (السندات حتى " + UI.Date(YearClose.ClosedThrough()) + " مقفلة).";
            lblStatus.ForeColor = closed == 0 ? Theme.Muted : Theme.Success;
            List<int> years = YearClose.ClosableYears();
            cmbYear.Items.Clear();
            foreach (int y in years) cmbYear.Items.Add(y);
            if (years.Count > 0) cmbYear.SelectedIndex = 0;
            cmbYear.Enabled = bClose.Enabled = years.Count > 0;
            if (years.Count == 0) lblCount.Text = "لا توجد سنة منتهية غير مقفلة.";
            bReopen.Enabled = closed > 0;
            bReopen.Text = closed > 0 ? "إعادة فتح سنة " + closed : "إعادة فتح آخر سنة مقفلة";
        }

        void ShowCount()
        {
            if (cmbYear.SelectedItem == null) return;
            int y = (int)cmbYear.SelectedItem;
            DateTime a = new DateTime(y, 1, 1), b = a.AddYears(1);
            int n = Db.I(Db.Scalar("SELECT COUNT(*) FROM sale WHERE menu_date >= ? AND menu_date < ?", a, b)) +
                    Db.I(Db.Scalar("SELECT COUNT(*) FROM shop WHERE menu_date >= ? AND menu_date < ?", a, b)) +
                    Db.I(Db.Scalar("SELECT COUNT(*) FROM trans WHERE menu_date >= ? AND menu_date < ?", a, b));
            int prev = YearClose.ClosedYear();
            lblCount.Text = "فيها " + n.ToString("#,##0") + " سند" + (y - 1 > prev && prev > 0 ? "، وتُقفل معها السنوات قبلها" : "");
        }

        void DoClose()
        {
            if (cmbYear.SelectedItem == null) return;
            int y = (int)cmbYear.SelectedItem;
            if (!UI.Confirm("سيتم إقفال سنة " + y + ":\nلن يمكن إضافة أو تعديل أو حذف السندات بتاريخ حتى " + UI.Date(new DateTime(y, 12, 31)) + ".\n\nهل أنت متأكد؟")) return;
            try
            {
                UI.WaitCursor(true);
                string file = YearClose.Close(y, chkBackup.Checked, chkArchive.Checked);
                UI.WaitCursor(false);
                Refresh2();
                string msg = "✓ تم إقفال سنة " + y + ".";
                if (file != null)
                {
                    msg += "\n\nجرد نهاية السنة محفوظ في:\n" + file + "\n\nهل تريد فتحه الآن؟";
                    if (UI.Confirm(msg)) { try { Process.Start(file); } catch (Exception ex) { UI.Warn("تعذر فتح الملف: " + ex.Message); } }
                }
                else UI.Info(msg);
            }
            catch (Exception ex) { UI.WaitCursor(false); UI.Error(ex); }
        }

        void DoReopen()
        {
            int y = YearClose.ClosedYear();
            if (y == 0) return;
            if (!UI.Confirm("إعادة فتح سنة " + y + " تسمح بتعديل سنداتها وتغيّر أرصدة نهاية تلك السنة المؤرشفة.\nاستعملها للتصحيح فقط، ثم أعد الإقفال.\n\nمتابعة؟")) return;
            try { YearClose.Reopen(); Refresh2(); UI.Info("تم فتح سنة " + y + "."); }
            catch (Exception ex) { UI.Error(ex); }
        }
    }
}
