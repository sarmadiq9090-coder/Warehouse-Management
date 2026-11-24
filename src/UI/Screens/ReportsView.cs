using System.Collections.Generic;
using System.Data;
using System.Diagnostics;
using System.Drawing;
using System.Globalization;
using System.IO.Compression;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Windows.Forms;
using System;

namespace Stores
{
    // ================================================================== reports screen
    class ReportsView : UserControl
    {
        ReportDef current; ReportData last;
        Label lblTitle, lblDesc;
        FlowLayoutPanel pars;
        ComboBox cmbPreset, cmbStore, cmbType, cmbRType;
        DateTimePicker d1, d2;
        ItemPicker item; PersonPicker person; ProjectPicker proj;
        NumericUpDown top; TextBox txt; CheckBox flag; ComboBox cmbMonths;
        Label lFrom, lTo, lPreset, lStore, lItem, lPerson, lType, lRType, lTop, lText, lProj;
        WebBrowser wb;
        Dictionary<string, Button> buttons = new Dictionary<string, Button>();
        Button preq;    // «إنشاء طلب شراء» (critical balances report only)
        int lastStore;  // store of the report shown

        public ReportsView(string key)
        {
            BackColor = Theme.Bg;
            // list of reports
            Panel listPanel = new Panel(); listPanel.Dock = DockStyle.Right; listPanel.Width = 240; listPanel.BackColor = Theme.Card; listPanel.AutoScroll = true;
            FlowLayoutPanel list = new FlowLayoutPanel(); list.FlowDirection = FlowDirection.TopDown; list.WrapContents = false; list.Dock = DockStyle.Fill; list.AutoScroll = true;
            list.Padding = new Padding(6);
            string group = null;
            foreach (ReportDef d in Reports.All)
            {
                if (!Session.Advanced && Array.IndexOf(Reports.Basic, d.Key) < 0) continue;
                if (d.Group != group)
                {
                    group = d.Group;
                    Label g = new Label(); g.Text = group; g.Font = Theme.FontBold; g.ForeColor = Theme.AccentDark; g.AutoSize = false; g.Size = new Size(200, 30);
                    g.TextAlign = ContentAlignment.BottomLeft; g.Margin = new Padding(0, 6, 0, 2);
                    list.Controls.Add(g);
                }
                Button b = new Button(); b.Text = d.Title; b.FlatStyle = FlatStyle.Flat; b.FlatAppearance.BorderSize = 0; b.TextAlign = ContentAlignment.MiddleLeft;
                b.Size = new Size(200, 34); b.Margin = new Padding(0, 1, 0, 1); b.Cursor = Cursors.Hand; b.BackColor = Theme.Card; b.ForeColor = Theme.Text;
                b.FlatAppearance.MouseOverBackColor = Theme.Select;
                ReportDef dd = d;
                b.Click += delegate { Select(dd); if ((dd.P & (RP.ItemReq | RP.PersonReq | RP.ProjectReq)) == 0) Run(); };   // show at once when nothing must be chosen first
                buttons[d.Key] = b;
                list.Controls.Add(b);
            }
            listPanel.Controls.Add(list);

            // parameters
            Panel head = UI.Card(); head.Dock = DockStyle.Top; head.Height = 150; head.Padding = new Padding(12, 6, 12, 6);
            lblTitle = new Label(); lblTitle.Font = Theme.FontTitle; lblTitle.ForeColor = Theme.Primary; lblTitle.Dock = DockStyle.Top; lblTitle.Height = 34;
            lblDesc = new Label(); lblDesc.ForeColor = Theme.Muted; lblDesc.Dock = DockStyle.Top; lblDesc.Height = 22;
            pars = UI.Flow(); pars.Dock = DockStyle.Top; pars.WrapContents = true;
            cmbPreset = UI.Combo(130);
            cmbPreset.Items.AddRange(new object[] { "اليوم", "هذا الشهر", "الشهر الماضي", "هذه السنة", "السنة الماضية", "آخر 12 شهرا", "كل الفترة", "مخصص" });
            DateTime t0 = DateTime.Today;
            d1 = UI.DatePicker(new DateTime(t0.Year, 1, 1)); d2 = UI.DatePicker(t0);
            cmbStore = UI.Combo(160); UI.Fill(cmbStore, Cache.Stores, "stor_no", "stor_name", "كل المخازن");
            item = new ItemPicker(280); person = new PersonPicker(260); proj = new ProjectPicker(280);
            cmbType = UI.Combo(170); UI.Fill(cmbType, Cache.Types, "type_no", "type_name", "كل الأنواع");
            cmbRType = UI.Combo(120);
            List<ComboItem> rts = new List<ComboItem> { new ComboItem(0, "الكل"), new ComboItem(1, "استيراد"), new ComboItem(2, "محلي"), new ComboItem(3, "إعادة") };
            cmbRType.DisplayMember = "Name"; cmbRType.DataSource = rts;
            top = new NumericUpDown(); top.Minimum = 5; top.Maximum = 1000; top.Value = 50; top.Width = 70; top.Margin = new Padding(4, 6, 4, 4);
            txt = UI.Text(160);
            cmbMonths = UI.Combo(130); cmbMonths.DisplayMember = "Name";
            cmbMonths.DataSource = new List<ComboItem> { new ComboItem(6, "آخر 6 أشهر"), new ComboItem(3, "آخر 3 أشهر") };
            flag = new CheckBox(); flag.AutoSize = true; flag.Margin = new Padding(8, 9, 8, 0);
            lPreset = UI.Lbl("الفترة"); lFrom = UI.Lbl("من"); lTo = UI.Lbl("إلى"); lStore = UI.Lbl("المخزن"); lItem = UI.Lbl("المادة"); lPerson = UI.Lbl("الجهة");
            lProj = UI.Lbl("مركز الكلفة"); lType = UI.Lbl("النوع"); lRType = UI.Lbl("نوع الإدخال"); lTop = UI.Lbl("العدد"); lText = UI.Lbl("بحث");
            pars.Controls.AddRange(new Control[] { UI.Field(lPreset, cmbPreset), UI.Field(lFrom, d1), UI.Field(lTo, d2), UI.Field(lStore, cmbStore), UI.Field(lItem, item), UI.Field(lProj, proj), UI.Field(lPerson, person), UI.Field(lType, cmbType), UI.Field(lRType, cmbRType), UI.Field(lTop, top), UI.Field(lText, txt), UI.Field(UI.Lbl("متوسط الاستهلاك من"), cmbMonths), flag });

            FlowLayoutPanel bar = UI.Flow(); bar.Dock = DockStyle.Top;
            Button run = UI.Btn("عرض التقرير", Theme.Accent, 130), print = UI.Btn("طباعة", Theme.Primary, 86), prev = UI.BtnLight("معاينة", 80),
                   xl = UI.BtnLight("تصدير Excel", 110), full = UI.BtnLight("ملء الشاشة", 104), setup = UI.BtnLight("إعداد الصفحة", 110),
                   zin = UI.BtnLight("+", 38), zout = UI.BtnLight("−", 38);
            new ToolTip().SetToolTip(zin, "تكبير الخط"); new ToolTip().SetToolTip(zout, "تصغير الخط");
            preq = UI.Btn("إنشاء طلب شراء", Theme.Primary, 140); preq.Visible = false;
            new ToolTip().SetToolTip(preq, "طلب شراء (مسودة) بالكميات المقترحة للمواد المعروضة");
            bar.Controls.AddRange(new Control[] { run, print, prev, xl, full, setup, zin, zout, preq });
            head.Controls.Add(bar); head.Controls.Add(pars); head.Controls.Add(lblDesc); head.Controls.Add(lblTitle); UI.FitHeight(head);

            Panel wbCard = UI.Card(); wbCard.Dock = DockStyle.Fill; wbCard.Padding = new Padding(1);
            wb = new WebBrowser(); wb.Dock = DockStyle.Fill; wb.IsWebBrowserContextMenuEnabled = false; wb.AllowWebBrowserDrop = false; wb.ScriptErrorsSuppressed = true;
            wbCard.Controls.Add(wb);

            Panel spacer = new Panel(); spacer.Dock = DockStyle.Right; spacer.Width = 10;
            Panel main = new Panel(); main.Dock = DockStyle.Fill;
            Panel sp2 = new Panel(); sp2.Dock = DockStyle.Top; sp2.Height = 8;
            main.Controls.Add(wbCard); main.Controls.Add(sp2); main.Controls.Add(head);
            Controls.Add(main); Controls.Add(spacer); Controls.Add(listPanel);

            cmbPreset.SelectedIndexChanged += delegate { ApplyPreset(); };
            d1.ValueChanged += delegate { if (!presetting) cmbPreset.SelectedIndex = 7; };
            d2.ValueChanged += delegate { if (!presetting) cmbPreset.SelectedIndex = 7; };
            run.Click += delegate { Run(); };
            Func<bool> ready = delegate { if (last == null) UI.Warn("اعرض التقرير أولا بالضغط على «عرض التقرير»."); return last != null; };
            print.Click += delegate { if (ready()) PrintSetup.Print(wb); };
            prev.Click += delegate { if (ready()) PrintSetup.Preview(wb); };
            setup.Click += delegate { PrintSetup.PageSetup(wb); };
            xl.Click += delegate { if (ready()) Excel.Export(FindForm(), last); };
            full.Click += delegate { if (ready()) ReportViewerForm.ShowFull(FindForm(), last); };
            preq.Click += delegate { if (ready()) CreatePurchaseRequest(); };
            zin.Click += delegate { PrintSetup.ZoomBy(wb, 10); };
            zout.Click += delegate { PrintSetup.ZoomBy(wb, -10); };
            wb.DocumentCompleted += delegate { PrintSetup.ApplyZoom(wb); };
            txt.KeyDown += delegate(object s, KeyEventArgs e) { if (e.KeyCode == Keys.Enter) { e.SuppressKeyPress = true; Run(); } };

            cmbPreset.SelectedIndex = 3;
            Select(Reports.Get(key ?? "stock") ?? Reports.All[0]);
            wb.DocumentText = "<html dir='rtl'><body style=\"font-family:'Segoe UI';color:#888;text-align:center;padding-top:80px\">اختر التقرير وحدد الخيارات ثم اضغط «عرض التقرير»</body></html>";
            Load += delegate { if (key != null && (current.P & (RP.ItemReq | RP.PersonReq | RP.ProjectReq)) == 0) Run(); };
        }

        bool presetting;
        void ApplyPreset()
        {
            DateTime t = DateTime.Today, a = t, b = t;
            switch (cmbPreset.SelectedIndex)
            {
                case 0: a = t; break;
                case 1: a = new DateTime(t.Year, t.Month, 1); break;
                case 2: a = new DateTime(t.Year, t.Month, 1).AddMonths(-1); b = new DateTime(t.Year, t.Month, 1).AddDays(-1); break;
                case 3: a = new DateTime(t.Year, 1, 1); break;
                case 4: a = new DateTime(t.Year - 1, 1, 1); b = new DateTime(t.Year - 1, 12, 31); break;
                case 5: a = t.AddYears(-1).AddDays(1); break;
                case 6: a = new DateTime(2000, 1, 1); break;
                default: return;
            }
            presetting = true; d1.Value = a; d2.Value = b; presetting = false;
        }

        void Select(ReportDef d)
        {
            current = d;
            foreach (KeyValuePair<string, Button> kv in buttons)
            {
                bool on = kv.Key == d.Key;
                kv.Value.BackColor = on ? Theme.Primary : Theme.Card;
                kv.Value.ForeColor = on ? Color.White : Theme.Text;
                kv.Value.Font = on ? Theme.FontBold : Theme.Font;
            }
            lblTitle.Text = d.Title; lblDesc.Text = d.Desc;
            preq.Visible = d.Key == "critical" && Db.HasPreq && PurchaseRequests.CanEdit;
            RP p = d.P;
            bool dates = (p & RP.Dates) != 0, asOf = (p & RP.AsOf) != 0, since = (p & RP.Since) != 0;
            cmbPreset.Parent.Visible = dates;
            d1.Parent.Visible = dates || since; lFrom.Text = since ? "لم يُخرج منذ" : "من";
            d2.Parent.Visible = dates || asOf; lTo.Text = asOf ? "الرصيد بتاريخ" : "إلى";
            if (asOf) d2.Value = DateTime.Today;
            if (since && !dates) d1.Value = DateTime.Today.AddMonths(-6);
            cmbStore.Parent.Visible = (p & RP.Store) != 0;
            item.Parent.Visible = (p & RP.Item) != 0;
            person.Parent.Visible = (p & RP.Person) != 0;
            proj.Parent.Visible = (p & RP.Project) != 0;
            lProj.Text = (p & RP.ProjectReq) != 0 ? "مركز الكلفة *" : "مركز الكلفة";
            cmbType.Parent.Visible = (p & RP.Type) != 0;
            cmbRType.Parent.Visible = (p & RP.RType) != 0;
            top.Parent.Visible = (p & RP.Top) != 0;
            txt.Parent.Visible = (p & RP.Text) != 0;
            cmbMonths.Parent.Visible = (p & RP.Months) != 0;
            flag.Visible = (p & RP.Flag) != 0; flag.Text = d.FlagText ?? ""; flag.Checked = false;
            lItem.Text = (p & RP.ItemReq) != 0 ? "المادة *" : "المادة";
            lPerson.Text = (p & RP.PersonReq) != 0 ? "الجهة *" : "الجهة";
            if (dates && cmbPreset.SelectedIndex == 6 && d.Key != "card") cmbPreset.SelectedIndex = 3;
        }

        public void Preset(int itemNo, int personNo, int projNo)
        {
            if (itemNo > 0) item.Value = itemNo;
            if (personNo > 0) person.Value = personNo;
            if (projNo > 0) proj.Value = projNo;
            if ((itemNo > 0 && (current.P & RP.Item) != 0) || (personNo > 0 && (current.P & RP.Person) != 0) || (projNo > 0 && (current.P & RP.Project) != 0)) Run();
        }

        /// <summary>Critical balances → a draft purchase request with the suggested quantities of the rows shown
        /// (the report filters, and the search box inside the report when used).</summary>
        void CreatePurchaseRequest()
        {
            List<Reports.CriticalRow> rows = last.Tag as List<Reports.CriticalRow>;
            if (rows == null) { UI.Warn("اعرض تقرير الأرصدة الحرجة أولا."); return; }
            HashSet<int> shown = ShownRowNumbers();
            List<Reports.CriticalRow> pick = new List<Reports.CriticalRow>();
            for (int i = 0; i < rows.Count; i++) if ((shown == null || shown.Contains(i + 1)) && rows[i].Suggest > 0) pick.Add(rows[i]);
            if (pick.Count == 0) { UI.Warn("لا توجد مواد معروضة لها كمية مقترحة."); return; }
            int store = lastStore;
            Dictionary<int, double> onOrder = PurchaseRequests.OnOrder(store);
            int already = pick.Count(c => onOrder.ContainsKey(c.Item));
            bool skip = false;
            if (already > 0)
            {
                DialogResult a = UI.YesNoCancel(already + " من المواد المعروضة لها طلب شراء مفتوح لم يُستلم بالكامل.\n\nنعم: تخطيها   •   لا: إضافتها أيضا");
                if (a == DialogResult.Cancel) return;
                skip = a == DialogResult.Yes;
            }
            else if (!UI.Confirm("إنشاء طلب شراء (مسودة) لـ " + pick.Count + " مادة بالكميات المقترحة" + (store > 0 ? " لمخزن " + Cache.Store(store) : "") + "؟\nيمكن تعديل الكميات قبل الإرسال.")) return;
            try
            {
                int skipped;
                int id = PurchaseRequests.CreateFromCritical(pick, store, skip, out skipped);
                if (id == 0) { UI.Info("لم يُنشأ طلب: كل المواد لها طلب شراء مفتوح."); return; }
                using (PurchaseRequestDialog d = new PurchaseRequestDialog(id)) d.ShowDialog(FindForm());
            }
            catch (UserError ue) { UI.Warn(ue.Message); }
            catch (Exception ex) { UI.Error(ex); }
        }

        /// <summary>Row numbers («ت») still visible after the in-report search, null = all rows.</summary>
        HashSet<int> ShownRowNumbers()
        {
            try
            {
                HtmlElement tb = wb.Document == null ? null : wb.Document.GetElementById("tb");
                if (tb == null) return null;
                HashSet<int> s = new HashSet<int>(); bool hidden = false;
                foreach (HtmlElement tr in tb.GetElementsByTagName("tr"))
                {
                    string st = tr.Style ?? "";
                    if (st.Replace(" ", "").ToLowerInvariant().Contains("display:none")) { hidden = true; continue; }
                    HtmlElementCollection tds = tr.GetElementsByTagName("td");
                    int n; if (tds.Count > 0 && int.TryParse((tds[0].InnerText ?? "").Replace(",", "").Trim(), out n)) s.Add(n);
                }
                return hidden ? s : null;
            }
            catch { return null; }
        }

        void Run()
        {
            ReportParams p = new ReportParams();
            p.From = d1.Value.Date; p.To = d2.Value.Date;
            if ((current.P & RP.AsOf) != 0) p.From = p.To;
            p.Store = (current.P & RP.Store) != 0 ? UI.Sel(cmbStore) : 0;
            p.ItemNo = (current.P & RP.Item) != 0 ? item.Value : 0;
            p.ProjNo = (current.P & RP.Project) != 0 ? proj.Value : 0;
            p.PersonNo = (current.P & RP.Person) != 0 ? person.Value : 0;
            p.TypeNo = (current.P & RP.Type) != 0 ? UI.Sel(cmbType) : 0;
            p.RType = (current.P & RP.RType) != 0 ? UI.Sel(cmbRType) : 0;
            p.Top = (int)top.Value;
            p.Months = (current.P & RP.Months) != 0 ? UI.Sel(cmbMonths) : 6;
            p.Text = (current.P & RP.Text) != 0 ? txt.Text.Trim() : "";
            p.Flag = (current.P & RP.Flag) != 0 && flag.Checked;
            if ((current.P & RP.ItemReq) != 0 && p.ItemNo == 0) { UI.Warn("اختر المادة."); return; }
            if ((current.P & RP.PersonReq) != 0 && p.PersonNo == 0) { UI.Warn("اختر الجهة."); return; }
            if ((current.P & RP.ProjectReq) != 0 && p.ProjNo == 0) { UI.Warn("اختر مركز الكلفة."); return; }
            if ((current.P & RP.Dates) != 0 && p.From > p.To) { UI.Warn("تاريخ البداية بعد تاريخ النهاية."); return; }
            try
            {
                UI.WaitCursor(true);
                last = current.Run(p); lastStore = p.Store;
                wb.DocumentText = Html.Render(last);
            }
            catch (Exception ex) { UI.Error(ex); }
            finally { UI.WaitCursor(false); }
        }
    }
}
