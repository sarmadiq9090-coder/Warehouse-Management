using System.Data;
using System.Drawing;
using System.Windows.Forms;
using System;

namespace Stores
{
    /// <summary>The «قفل الفترات الشهرية» window (Settings screen, permission PeriodLock.Perm): the current lock,
    /// lock up to the end of a month, unlock, and the last lock operations.</summary>
    class PeriodLockForm : BaseDialog
    {
        ComboBox cmbMonth; NumericUpDown numYear; Label lblStatus, lblYear, lblNote; Button bLock, bUnlock; DataGridView log;

        public PeriodLockForm()
        {
            Text = "قفل الفترات الشهرية";
            ClientSize = new Size(620, 520);
            Panel body = new Panel(); body.Dock = DockStyle.Fill; body.Padding = new Padding(18, 12, 18, 8); body.BackColor = Color.White;

            Label title = new Label(); title.Text = "قفل الفترات الشهرية"; title.Font = Theme.FontTitle; title.ForeColor = Theme.Primary; title.Dock = DockStyle.Top; title.Height = 40;
            lblStatus = new Label(); lblStatus.Dock = DockStyle.Top; lblStatus.Height = 28; lblStatus.Font = Theme.FontBold;
            lblYear = new Label(); lblYear.Dock = DockStyle.Top; lblYear.Height = 24; lblYear.ForeColor = Theme.Muted;
            Label explain = new Label(); explain.Dock = DockStyle.Top; explain.Height = 92; explain.ForeColor = Theme.Text;
            explain.Text = "عند قفل الفترة حتى نهاية شهر:\n" +
                           "• لا يمكن إضافة أو تعديل أو حذف أي سند بتاريخ في ذلك الشهر أو قبله (مثل السنة المقفلة).\n" +
                           "• الأرصدة والتقارير لا تتغير.\n" +
                           "• فتح القفل يحتاج نفس الصلاحية، وكل قفل أو فتح يُسجَّل في سجل التدقيق.";
            FlowLayoutPanel row = UI.Flow(); row.Dock = DockStyle.Top; row.Height = 44;
            cmbMonth = UI.Combo(130);
            for (int m = 1; m <= 12; m++) cmbMonth.Items.Add(m + " • " + PeriodLock.MonthName(m));
            numYear = new NumericUpDown(); numYear.Minimum = 2000; numYear.Maximum = DateTime.Today.Year; numYear.Width = 80; numYear.Font = Theme.Font;
            numYear.Margin = new Padding(4, 6, 4, 4); numYear.RightToLeft = RightToLeft.No;
            DateTime prev = DateTime.Today.AddMonths(-1);
            cmbMonth.SelectedIndex = prev.Month - 1; numYear.Value = prev.Year;
            row.Controls.Add(UI.Field("قفل السندات حتى نهاية شهر", cmbMonth)); row.Controls.Add(UI.Field("سنة", numYear));
            lblNote = new Label(); lblNote.Dock = DockStyle.Top; lblNote.Height = 26; lblNote.ForeColor = Theme.Muted;

            Label lt = new Label(); lt.Text = "آخر عمليات القفل والفتح"; lt.Font = Theme.FontBold; lt.ForeColor = Theme.Primary; lt.Dock = DockStyle.Top; lt.Height = 28;
            log = new DataGridView(); log.Dock = DockStyle.Fill; UI.ReadOnlyGrid(log);
            log.Columns.Add(UI.Col("at", "الوقت", "log_at", 130, false)); log.Columns["at"].DefaultCellStyle.Format = "yyyy/MM/dd HH:mm";
            log.Columns.Add(UI.Col("u", "المستخدم", "user_name", 115, false));
            log.Columns.Add(UI.Col("a", "العملية", "act", 120, false));
            log.Columns.Add(UI.Col("d", "التفاصيل", "details", 0, false));

            body.Controls.Add(log); body.Controls.Add(lt); body.Controls.Add(lblNote); body.Controls.Add(row); body.Controls.Add(explain);
            body.Controls.Add(lblYear); body.Controls.Add(lblStatus); body.Controls.Add(title);

            bLock = UI.Btn("قفل حتى نهاية الشهر", Theme.Danger, 170);
            bUnlock = UI.BtnLight("فتح القفل", 120);
            Button bExit = UI.BtnLight("إغلاق", 80);
            Controls.Add(body); Controls.Add(ButtonBar(bLock, bUnlock, bExit));

            bLock.Click += delegate { DoLock(); };
            bUnlock.Click += delegate { DoUnlock(); };
            bExit.Click += delegate { Close(); };
            cmbMonth.SelectedIndexChanged += delegate { ShowCount(); };
            numYear.ValueChanged += delegate { ShowCount(); };
            Refresh2();
        }

        void Refresh2()
        {
            DateTime l = PeriodLock.LockedThrough();
            lblStatus.Text = "الحالة: " + PeriodLock.StatusText();
            lblStatus.ForeColor = l == DateTime.MinValue ? Theme.Muted : Theme.Success;
            int cy = YearClose.ClosedYear();
            lblYear.Text = cy == 0 ? "إقفال السنة: لا توجد سنة مقفلة." : "إقفال السنة: السندات حتى " + UI.Date(YearClose.ClosedThrough()) + " مقفلة (سنة " + cy + ").";
            bool can = PeriodLock.CanManage;
            bLock.Enabled = cmbMonth.Enabled = numYear.Enabled = can;
            bUnlock.Enabled = can && l != DateTime.MinValue;
            bUnlock.Text = l == DateTime.MinValue ? "فتح القفل" : "فتح قفل " + PeriodLock.MonthName(l);
            bUnlock.Width = Math.Max(120, TextRenderer.MeasureText(bUnlock.Text, Theme.Font).Width + 32);
            if (!can) lblNote.Text = "ليس لديك صلاحية «" + PeriodLock.PermName + "».";
            else ShowCount();
            try
            {
                log.DataSource = Db.Query("SELECT TOP 30 log_at, user_name, act, details FROM app_log WHERE act IN (?, ?, ?, ?) ORDER BY id DESC",
                                          PeriodLock.ActLock, PeriodLock.ActUnlock, "إقفال السنة", "إعادة فتح سنة");
            }
            catch { }
        }

        void ShowCount()
        {
            if (cmbMonth.SelectedIndex < 0 || !PeriodLock.CanManage) return;
            DateTime end = PeriodLock.MonthEnd((int)numYear.Value, cmbMonth.SelectedIndex + 1);
            DateTime a = new DateTime(end.Year, end.Month, 1), b = end.AddDays(1);
            try
            {
                int n = Db.I(Db.Scalar("SELECT COUNT(*) FROM sale WHERE menu_date >= ? AND menu_date < ?", a, b)) +
                        Db.I(Db.Scalar("SELECT COUNT(*) FROM shop WHERE menu_date >= ? AND menu_date < ?", a, b)) +
                        Db.I(Db.Scalar("SELECT COUNT(*) FROM trans WHERE menu_date >= ? AND menu_date < ?", a, b));
                lblNote.Text = "في شهر " + PeriodLock.MonthName(end) + ": " + n.ToString("#,##0") + " سند • يُقفل معه كل ما قبله.";
            }
            catch { lblNote.Text = ""; }
        }

        void DoLock()
        {
            if (cmbMonth.SelectedIndex < 0) return;
            int y = (int)numYear.Value, m = cmbMonth.SelectedIndex + 1;
            DateTime end = PeriodLock.MonthEnd(y, m), old = PeriodLock.LockedThrough();
            string warn = old != DateTime.MinValue && end < old ? "\n\nتنبيه: القفل الحالي حتى " + UI.Date(old) + "، فهذا يفتح الأشهر بعد " + PeriodLock.MonthName(end) + "." : "";
            if (!UI.Confirm("سيتم قفل كل السندات حتى نهاية شهر " + PeriodLock.MonthName(end) + " (" + UI.Date(end) + "):\nلن يمكن إضافة أو تعديل أو حذف سند بتاريخ حتى هذا اليوم." + warn + "\n\nمتابعة؟")) return;
            try { PeriodLock.LockThrough(y, m); Refresh2(); UI.Info("✓ تم قفل السندات حتى نهاية شهر " + PeriodLock.MonthName(end) + "."); }
            catch (Exception ex) { UI.Error(ex); }
        }

        void DoUnlock()
        {
            DateTime old = PeriodLock.LockedThrough();
            if (old == DateTime.MinValue) return;
            if (!UI.Confirm("فتح القفل يسمح بتعديل السندات حتى " + UI.Date(old) + " (إلا ما كان في سنة مقفلة).\nاستعمله للتصحيح فقط، ثم أعد القفل.\n\nمتابعة؟")) return;
            try { PeriodLock.Unlock(); Refresh2(); UI.Info("تم فتح القفل الشهري."); }
            catch (Exception ex) { UI.Error(ex); }
        }
    }
}
