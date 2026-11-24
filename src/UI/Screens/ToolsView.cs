using System.Collections.Generic;
using System.Data.Common;
using System.Data;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Windows.Forms;
using System;

namespace Stores
{
    // ================================================================== tools
    class ToolsView : UserControl
    {
        MainForm main;
        public ToolsView(MainForm m)
        {
            main = m;
            BackColor = Theme.Bg;
            AutoScroll = true;
            FlowLayoutPanel flow = new FlowLayoutPanel(); flow.Dock = DockStyle.Top; flow.AutoSize = true; flow.FlowDirection = FlowDirection.LeftToRight; flow.WrapContents = true;
            flow.Padding = new Padding(0, 0, 0, 10);

            bool admin = Session.IsAdmin;
            flow.Controls.Add(Tile("الاتصال بالبيانات", Db.Describe.Replace("الخادم", "السيرفر") + (Db.IsSql ? "\nالعمل المشترك عبر السيرفر مفعّل ✓" : "\nملف Access"),
                admin ? "تغيير الاتصال…" : null, delegate
                {
                    using (ConnectionForm f = new ConnectionForm())
                        if (f.ShowDialog(FindForm()) == DialogResult.OK) { UI.Info("سيتم إعادة تشغيل البرنامج."); Db.SignOff(); SingleInstance.Release(); Application.Restart(); }
                }));
            { Panel ap = Tile("المظهر", "", null, null); ap.Controls[0].Dispose(); ThemePicker tp = new ThemePicker(); tp.Dock = DockStyle.Fill; ap.Controls.Add(tp); tp.BringToFront(); flow.Controls.Add(ap); }
            if (Db.IsSql && admin)
                flow.Controls.Add(Tile("استيراد من ملف Access", "نقل كل البيانات من ملف .AMR إلى السيرفر.\nيُستبدل ما في السيرفر حاليا.", "استيراد…", delegate
                {
                    if (Migrate.Run(FindForm())) { Cache.LoadAll(); UI.Info("تم. يفضل إعادة تشغيل البرنامج."); }
                }));
            if (!Db.IsSql)
            {
                flow.Controls.Add(Tile("نسخة احتياطية", "يتم أخذ نسخة تلقائية يوميا عند الدخول.\nالمجلد: " + Settings.BackupDir, "نسخة الآن", delegate
                {
                    try { string p = Backup.Now("يدوي"); UI.Info("تم حفظ النسخة:\n" + p); } catch (Exception ex) { UI.Error(ex); }
                }, "فتح المجلد", delegate { try { Directory.CreateDirectory(Settings.BackupDir); Process.Start("explorer.exe", "\"" + Settings.BackupDir + "\""); } catch { } }));
                if (admin)
                    flow.Controls.Add(Tile("ضغط وإصلاح ملف البيانات", "يقلل حجم الملف ويصلح الأخطاء.\nيجب أن يكون الجميع خارج البرنامج.", "ضغط وإصلاح", delegate { Compact(); }));
            }
            if (admin)
                flow.Controls.Add(Tile("إعادة احتساب الأرصدة", "يحسب رصيد كل مادة في كل مخزن من جديد\n(الافتتاحي + كل الحركات) ويصحح الفروقات.", "احتساب", delegate
                {
                    if (!UI.Confirm("سيتم تصحيح أرصدة المواد حسب الحركات المسجلة.\nيفضل أن يكون الآخرون خارج البرنامج. متابعة؟")) return;
                    try
                    {
                        UI.WaitCursor(true);
                        if (!Db.IsSql) Backup.Now("قبل احتساب الأرصدة");
                        int n = Posting.RecalcBalances();
                        Cache.LoadItems();
                        UI.Info(n == 0 ? "كل الأرصدة صحيحة ✓" : "تم تصحيح " + n + " رصيد.");
                    }
                    catch (Exception ex) { UI.Error(ex); }
                    finally { UI.WaitCursor(false); }
                }));
            if (!Db.HasProj)
                flow.Controls.Add(Tile("تفعيل مراكز الكلفة والحجز", "تحتاج إضافة حقول جديدة لملف البيانات.\nأغلق البرنامج القديم على كل الأجهزة ثم اضغط تفعيل.\n" + (Db.SchemaWarning ?? ""), "تفعيل الآن", delegate
                {
                    Db.EnsureAppTables(); Cache.LoadProjects();
                    if (Db.HasProj) UI.Info("تم تفعيل مراكز الكلفة والحجز ✓\nأعد تشغيل البرنامج."); else UI.Warn("لم يتم التفعيل:\n" + Db.SchemaWarning);
                }));
            if (admin)
                flow.Controls.Add(Tile("الإعدادات العامة", "اسم الشركة: " + Branding.Company + "\nالشعار: " + (Branding.LogoBytes != null ? "موجود ✓" : "بدون (اختياري)") +
                    "\nيظهران في الشاشات والتقارير وExcel.", "تعديل…", delegate
                    {
                        using (GeneralSettingsForm f = new GeneralSettingsForm()) if (f.ShowDialog(FindForm()) == DialogResult.OK) main.Show(new ToolsView(main));
                    }));
            flow.Controls.Add(Tile("دليل الاستخدام", "شرح مصوّر خطوة بخطوة لكل شاشات البرنامج،\nيُفتح في المتصفح ويمكن طباعته.\nاختصار: F1 من أي شاشة.", "فتح الدليل", delegate { Manual.Open(FindForm()); }));
            flow.Controls.Add(Tile("التحديثات والدعم", "الإصدار الحالي: " + Program.Version + "\nالبحث عن إصدار جديد على GitHub وتنصيبه بضغطة،\nأو إرسال تبليغ عن مشكلة.", "تحديث البرنامج", delegate { Updater.CheckNow(FindForm()); },
                "تبليغ عن مشكلة", delegate { ProblemReportForm.Open(FindForm()); }));
            if (admin)
            {
                int cy = YearClose.ClosedYear();
                flow.Controls.Add(Tile("إقفال السنة", (cy == 0 ? "لا توجد سنة مقفلة." : "آخر سنة مقفلة: " + cy + " ✓") +
                    "\nيدوي: يحفظ جرد نهاية السنة ويمنع تعديل سنداتها.\nالأرصدة تستمر للسنة الجديدة تلقائيا.", "إقفال السنة…", delegate
                    {
                        using (YearCloseForm f = new YearCloseForm()) f.ShowDialog(FindForm());
                        main.Show(new ToolsView(main));
                    }));
            }
            if (PeriodLock.CanManage)
                flow.Controls.Add(Tile("قفل الفترات الشهرية", PeriodLock.StatusText() + "\nيمنع إضافة أو تعديل أو حذف السندات حتى نهاية\nالشهر المختار. الفتح يحتاج نفس الصلاحية ويُسجَّل.", "قفل الفترات…", delegate
                    {
                        using (PeriodLockForm f = new PeriodLockForm()) f.ShowDialog(FindForm());
                        main.Show(new ToolsView(main));
                    }));
            if (Session.Can(Session.Reports))
                flow.Controls.Add(Tile("سجل التدقيق", "من عدّل أو حذف ماذا ومتى: القيم قبل وبعد\nالتعديل في السندات والمواد ومراكز الكلفة.", "عرض السجل…", delegate
                    {
                        using (AuditTrailForm f = new AuditTrailForm()) f.ShowDialog(FindForm());
                    }));
            if (Session.Can(Session.Reports))
                flow.Controls.Add(Tile("تدقيق البيانات", "يكشف الأرصدة غير المطابقة والسالبة،\nوالتواريخ المستقبلية والأرقام المكررة.", "تشغيل التدقيق", delegate { main.OpenReport("audit", 0, 0); }));

            // activity log
            Panel logCard = UI.Card(); logCard.Dock = DockStyle.Top; logCard.Height = 360; logCard.Padding = new Padding(14, 10, 14, 12);
            Label lt = new Label(); lt.Text = "سجل العمليات (آخر 300 عملية)"; lt.Font = Theme.FontSubtitle; lt.ForeColor = Theme.Text; lt.Dock = DockStyle.Top; lt.Height = 32;
            DataGridView g = new DataGridView(); g.Dock = DockStyle.Fill; UI.ReadOnlyGrid(g);
            g.Columns.Add(UI.Col("at", "الوقت", "log_at", 150, false)); g.Columns["at"].DefaultCellStyle.Format = "yyyy/MM/dd HH:mm";
            g.Columns.Add(UI.Col("u", "المستخدم", "user_name", 110, false));
            g.Columns.Add(UI.Col("c", "الحاسوب", "computer", 150, false));
            g.Columns.Add(UI.Col("a", "العملية", "act", 170, false));
            g.Columns.Add(UI.Col("d", "التفاصيل", "details", 0, false));
            logCard.Controls.Add(g); logCard.Controls.Add(lt);
            try { g.DataSource = Db.Query("SELECT TOP 300 log_at, user_name, computer, act, details FROM app_log ORDER BY id DESC"); } catch { }

            Controls.Add(logCard); Controls.Add(flow); Controls.Add(ViewHelp.Title("الإعدادات"));
            // focus starts on the cards; the screen opens scrolled to the top (focusing a control scrolls it into view)
            flow.TabIndex = 0; logCard.TabIndex = 1;
            Load += delegate { BeginInvoke((Action)delegate { AutoScrollPosition = Point.Empty; }); };
        }

        Panel Tile(string title, string text, string b1, Action a1) { return Tile(title, text, b1, a1, null, null); }

        Panel Tile(string title, string text, string b1, Action a1, string b2, Action a2)
        {
            // Windows 11 settings card: icon + title, a short explanation, the buttons at the bottom
            // (control order d, bar, title is used by the projects tile above)
            Panel p = UI.Card(); p.Size = new Size(344, 176); p.Margin = new Padding(6); p.Padding = new Padding(16, 12, 16, 10);
            Panel t = new Panel(); t.Dock = DockStyle.Top; t.Height = 36; t.BackColor = Theme.Card;
            Label ic = new Label(); ic.Text = TileGlyph(title); ic.Font = Theme.IconFont(14f); ic.ForeColor = Theme.Primary; ic.Dock = DockStyle.Right; ic.Width = 32;
            ic.TextAlign = ContentAlignment.MiddleCenter;
            Label tl = new Label(); tl.Text = title; tl.Font = Theme.FontSubtitle; tl.ForeColor = Theme.Text; tl.Dock = DockStyle.Fill; tl.TextAlign = ContentAlignment.MiddleLeft;
            tl.Padding = new Padding(0, 0, 6, 0); tl.AutoEllipsis = true;
            t.Controls.Add(tl); t.Controls.Add(ic);
            Label d = new Label(); d.Text = text; d.ForeColor = Theme.Muted; d.Dock = DockStyle.Fill; d.Font = Theme.FontSmall; d.AutoEllipsis = true; d.Padding = new Padding(0, 4, 0, 0);
            FlowLayoutPanel bar = UI.Flow(); bar.Dock = DockStyle.Bottom; bar.Padding = new Padding(0, 4, 0, 0);
            if (b1 != null) { Button b = UI.Btn(b1, Theme.Primary, Math.Max(120, TextRenderer.MeasureText(b1, Theme.FontBold).Width + 32)); b.Margin = new Padding(0, 0, 8, 0); b.Click += delegate { if (a1 != null) a1(); }; bar.Controls.Add(b); }
            if (b2 != null) { Button b = UI.BtnLight(b2, Math.Max(110, TextRenderer.MeasureText(b2, Theme.Font).Width + 32)); b.Margin = new Padding(0); b.Click += delegate { a2(); }; bar.Controls.Add(b); }
            p.Controls.Add(d); p.Controls.Add(bar); p.Controls.Add(t);
            return p;
        }

        /// <summary>Fluent icon for a settings card (by its title, so the card lines above stay unchanged).</summary>
        static string TileGlyph(string title)
        {
            if (title.Contains("المظهر")) return "";
            if (title.Contains("الاتصال")) return "";
            if (title.Contains("استيراد")) return "";
            if (title.Contains("نسخة")) return "";
            if (title.Contains("ضغط")) return "";
            if (title.Contains("احتساب")) return "";
            if (title.Contains("المشاريع") || title.Contains("الكلف")) return "";
            if (title.Contains("الإعدادات العامة")) return "";
            if (title.Contains("التحديثات")) return "";
            if (title.Contains("إقفال")) return "";
            if (title.Contains("قفل الفترات")) return "";
            if (title.Contains("سجل التدقيق")) return "";
            if (title.Contains("تدقيق")) return "";
            if (title.Contains("دليل")) return "";   // Help (book with a question mark)
            return "";
        }

        void Compact()
        {
            DataTable on = Db.OnlineUsers();
            int others = 0; foreach (DataRow r in on.Rows) if (Db.S(r["computer"]) != Environment.MachineName) others++;
            if (others > 0) { UI.Warn("يوجد " + others + " مستخدم آخر متصل الآن. اطلب منهم الخروج من البرنامج أولا."); return; }
            if (!UI.Confirm("سيتم أخذ نسخة احتياطية ثم ضغط الملف. متابعة؟")) return;
            string src = Db.DataPath;
            try
            {
                UI.WaitCursor(true);
                Backup.Now("قبل الضغط");
                Db.SignOff();
                Db.Close();
                string tmp = Path.Combine(Path.GetTempPath(), "compact_" + Guid.NewGuid().ToString("N") + ".mdb");
                Type t = Type.GetTypeFromProgID("JRO.JetEngine");
                if (t == null) throw new UserError("أداة الضغط (JRO) غير متوفرة على هذا الحاسوب.");
                object je = Activator.CreateInstance(t);
                t.InvokeMember("CompactDatabase", System.Reflection.BindingFlags.InvokeMethod, null, je, new object[] {
                    "Provider=Microsoft.Jet.OLEDB.4.0;Data Source=" + src,
                    "Provider=Microsoft.Jet.OLEDB.4.0;Data Source=" + tmp + ";Jet OLEDB:Engine Type=5" });
                long before = new FileInfo(src).Length, after = new FileInfo(tmp).Length;
                File.Copy(tmp, src, true);
                File.Delete(tmp);
                UI.Info("تم الضغط ✓\nالحجم قبل: " + (before / 1024 / 1024.0).ToString("0.0") + " MB\nالحجم بعد: " + (after / 1024 / 1024.0).ToString("0.0") + " MB");
            }
            catch (Exception ex) { UI.Error(ex); }
            finally
            {
                Db.InitAccess(src);
                Db.Heartbeat();
                UI.WaitCursor(false);
            }
        }
    }
}
