using System.Collections.Generic;
using System.Data;
using System.Drawing.Drawing2D;
using System.Drawing;
using System.Windows.Forms;
using System;

namespace Stores
{
    // ================================================================== main window
    class MainForm : Form
    {
        Panel content, side;
        Control current;
        Label lblStatus, lblOnline, lblWarn;
        List<NavButton> navs = new List<NavButton>();
        Timer timer;
        /// <summary>Set when the window closes to reopen in the other language (Program.Main runs it again).</summary>
        public static bool Reopen;

        public MainForm()
        {
            Text = Program.AppName + (Lang.English ? "" : " • " + Program.AppNameAr);
            RightToLeft = RightToLeft.Yes;
            Font = Theme.Font;
            BackColor = Theme.Bg;
            WindowState = FormWindowState.Maximized;
            MinimumSize = new Size(1100, 680);
            Icon = Theme.AppIcon;
            KeyPreview = true;

            // ---- top bar (Windows 11 title area: brand on the right, the user and the mode switch on the left)
            BrandBar brand = new BrandBar(); brand.Dock = DockStyle.Fill;
            Panel top = new Panel(); top.Dock = DockStyle.Top; top.Height = 56; top.BackColor = Theme.Bg;
            FlowLayoutPanel userBox = new FlowLayoutPanel(); userBox.Dock = DockStyle.Left; userBox.AutoSize = true; userBox.AutoSizeMode = AutoSizeMode.GrowAndShrink;
            userBox.WrapContents = false; userBox.FlowDirection = FlowDirection.RightToLeft; userBox.Padding = new Padding(14, 17, 8, 0); userBox.BackColor = Theme.Bg;
            Label ui = new Label(); ui.AutoSize = true; ui.Font = Theme.IconFont(11f); ui.ForeColor = Theme.Muted; ui.Text = ""; ui.Margin = new Padding(0, 4, 14, 0);
            Label lu = new Label(); lu.AutoSize = true; lu.Font = Theme.FontBold; lu.ForeColor = Theme.Text; lu.Text = Session.UserName;
            lu.Margin = new Padding(0, 1, 4, 0);
            Label sep = new Label(); sep.AutoSize = false; sep.Size = new Size(1, 18); sep.BackColor = Theme.BorderStrong; sep.Margin = new Padding(10, 3, 10, 0);
            // «تحديث البرنامج» and «تبليغ عن مشكلة» live in «الإعدادات» (owner's request)
            userBox.Controls.Add(lu); userBox.Controls.Add(ui); userBox.Controls.Add(sep);
            top.Controls.Add(brand); top.Controls.Add(userBox);
            // the user name opens a small menu: change password • log out (owner's request)
            lu.Text = Session.UserName + "  ▾"; lu.Cursor = ui.Cursor = Cursors.Hand;
            ContextMenuStrip userMenu = new ContextMenuStrip(); userMenu.RightToLeft = RightToLeft.Yes; userMenu.Font = Theme.Font;
            userMenu.Items.Add("تغيير كلمة المرور", null, delegate { using (ChangePasswordForm f = new ChangePasswordForm()) f.ShowDialog(this); });
            userMenu.Items.Add("تسجيل الخروج", null, delegate { Program.LogoutRequested = true; ExitProgram(); });
            Lang.Apply(userMenu);
            EventHandler showUserMenu = delegate { userMenu.Show(lu, new Point(lu.Width, lu.Height + 2), ToolStripDropDownDirection.BelowLeft); };
            lu.Click += showUserMenu; ui.Click += showUserMenu;

            // ---- side bar (content depends on beginner / advanced mode)
            side = new Panel(); side.Dock = DockStyle.Right; side.Width = 236; side.BackColor = Theme.Sidebar;
            side.Padding = new Padding(0, 4, 0, 4); side.AutoScroll = true;   // small screens: the menu scrolls instead of being cut off
            BuildNav();

            // mode switch in the top bar
            LinkLabel mode = TopLink(""); mode.Font = Theme.FontBold; mode.LinkColor = Theme.Primary;
            Action showMode = delegate { mode.Text = Session.Advanced ? "الوضع: متقدم  ⇄" : "الوضع: مبتدئ  ⇄"; };
            showMode();
            userBox.Controls.Add(mode);
            userBox.Controls.Add(LangLink());
            mode.LinkClicked += delegate
            {
                IView v = current as IView;
                if (v != null && !v.CanLeave()) return;
                Session.SetMode(!Session.Advanced);
                showMode();
                BuildNav();
                Show(new DashboardView(this));
                UI.Info(Session.Advanced
                    ? "تم التحويل إلى الوضع المتقدم:\nتظهر كل الشاشات (النقل المخزني، الجرد الفعلي، طلبات الشراء، المخازن والأنواع، المستخدمون) والإدخال السريع بلوحة المفاتيح."
                    : "تم التحويل إلى وضع المبتدئ:\nشاشات مبسطة مع شرح لكل خطوة وتأكيد قبل الحفظ.");
            };

            // ---- status bar
            Panel status = new Panel(); status.Dock = DockStyle.Bottom; status.Height = 26; status.BackColor = Theme.Bg; status.Padding = new Padding(10, 0, 10, 0);
            lblStatus = new Label(); lblStatus.Dock = DockStyle.Right; lblStatus.AutoSize = false; lblStatus.Width = 520; lblStatus.TextAlign = ContentAlignment.MiddleLeft;
            lblStatus.ForeColor = Theme.Muted; lblStatus.Font = Theme.FontSmall; lblStatus.Text = "  " + Db.Describe.Replace("الخادم", "السيرفر"); lblStatus.AutoEllipsis = true;
            lblOnline = new Label(); lblOnline.Dock = DockStyle.Left; lblOnline.AutoSize = false; lblOnline.Width = 360; lblOnline.TextAlign = ContentAlignment.MiddleLeft;
            lblOnline.ForeColor = Theme.Success; lblOnline.Font = Theme.FontSmall; lblOnline.Cursor = Cursors.Hand;
            lblWarn = new Label(); lblWarn.Dock = DockStyle.Fill; lblWarn.TextAlign = ContentAlignment.MiddleCenter; lblWarn.ForeColor = Theme.Danger; lblWarn.Font = Theme.FontSmall;
            status.Controls.Add(lblWarn); status.Controls.Add(lblStatus); status.Controls.Add(lblOnline);
            lblOnline.Click += delegate { ShowOnline(); };

            content = new Panel(); content.Dock = DockStyle.Fill; content.Padding = new Padding(16, 6, 10, 12); content.BackColor = Theme.Bg;

            Controls.Add(content); Controls.Add(side); Controls.Add(status); Controls.Add(top);
            Lang.Apply(this);            // English first: it may recreate handles the theme then paints
            ThemePainter.Attach(this);   // dark theme: title bar, inputs and grids of the screens shown later

            timer = new Timer(); timer.Interval = 60000;
            timer.Tick += delegate { Db.Heartbeat(); Backup.DailyAuto();   /* the program stays open next to the clock for days */ UpdateOnline(); if (!(current is DocView)) Cache.RefreshIfOld(); };
            timer.Start();

            string warn = Db.SyncWarning();
            if (warn != null) { lblWarn.Text = "⚠ ملف البيانات على قرص تتم مزامنته • العمل المشترك يحتاج السيرفر"; lblWarn.Cursor = Cursors.Hand; lblWarn.Click += delegate { UI.Warn(warn); }; }

            Shown += delegate
            {
                UpdateOnline(); Show(new DashboardView(this));
                // once a day: is there a newer version on GitHub? (silent when offline)
                Updater.CheckInBackground(this, delegate(Updater.Release r)
                {
                    lblWarn.Text = "⬆ يتوفر إصدار جديد " + r.Ver + " • اضغط هنا للتحديث";
                    lblWarn.ForeColor = Theme.AccentDark; lblWarn.Cursor = Cursors.Hand;
                    lblWarn.Click += delegate { IView cv = current as IView; if (cv == null || cv.CanLeave()) Updater.Offer(this, r); };   // an unsaved document is asked about first
                });
            };
            FormClosing += delegate(object s, FormClosingEventArgs e)
            {
                // ✕ keeps the program running next to the clock (owner's request); «خروج» really exits
                if (!exiting && e.CloseReason == CloseReason.UserClosing) { e.Cancel = true; ToTray(); return; }
                IView v = current as IView;
                if (v != null && !v.CanLeave()) { e.Cancel = true; exiting = false; Program.LogoutRequested = false; return; }
                if (tray != null) { tray.Visible = false; tray.Dispose(); tray = null; }
            };
        }

        /// <summary>Language switch in the top bar: the window reopens in the other language (per user).</summary>
        LinkLabel LangLink()
        {
            LinkLabel l = TopLink(Lang.English ? "عربي" : "English");
            l.Font = Lang.English ? Theme.Font : new Font(Theme.LatinFontName, Theme.Font.Size);
            Lang.Keep(l);   // the name of the other language is never translated
            l.LinkClicked += delegate
            {
                IView v = current as IView;
                if (v != null && !v.CanLeave()) return;
                Lang.SetUser(!Lang.English);
                current = null;   // CanLeave was asked already
                timer.Stop();
                exiting = true;   // a real close, not the tray
                Reopen = true;
                Close();
            };
            return l;
        }

        // ------------------------------------------------------------ tray (next to the clock)
        NotifyIcon tray; bool exiting, trayTipShown;

        void ToTray()
        {
            if (tray == null)
            {
                tray = new NotifyIcon(); tray.Icon = Theme.AppIcon; tray.Text = Program.AppName;
                ContextMenuStrip m = new ContextMenuStrip(); m.RightToLeft = RightToLeft.Yes; m.Font = Theme.Font;
                m.Items.Add("فتح البرنامج", null, delegate { FromTray(); });
                m.Items.Add("خروج", null, delegate { FromTray(); ExitProgram(); });
                Lang.Apply(m);
                tray.ContextMenuStrip = m;
                tray.DoubleClick += delegate { FromTray(); };
            }
            tray.Visible = true;
            Hide();
            if (!trayTipShown)
            {
                trayTipShown = true;
                tray.ShowBalloonTip(4000, Program.AppName, Lang.T("البرنامج يعمل بجانب الساعة. انقر مرتين لفتحه، وللخروج: زر الفأرة الأيمن ← خروج."), ToolTipIcon.Info);
            }
        }

        public void FromTray()
        {
            Show();
            if (WindowState == FormWindowState.Minimized) WindowState = FormWindowState.Normal;
            Activate();
            if (tray != null) tray.Visible = false;
        }

        protected override void WndProc(ref Message m)
        {
            // a second start of the program (or its shortcut) shows this window, also from next to the clock
            if (SingleInstance.IsShowMessage(ref m)) { if (!Visible) FromTray(); SingleInstance.Shown(this); return; }
            base.WndProc(ref m);
        }

        /// <summary>Really closes the program (side menu «خروج», tray «خروج», «تسجيل الخروج»).</summary>
        public void ExitProgram() { exiting = true; Close(); }

        /// <summary>A quiet top bar link (underlined on hover).</summary>
        static LinkLabel TopLink(string text)
        {
            LinkLabel l = new LinkLabel(); l.Text = text; l.AutoSize = true; l.Font = Theme.Font; l.Margin = new Padding(6, 2, 6, 0);
            l.LinkBehavior = LinkBehavior.HoverUnderline; l.LinkColor = Theme.Text; l.ActiveLinkColor = Theme.PrimaryDark;
            return l;
        }

        void BuildNav()
        {
            side.SuspendLayout();
            foreach (Control c in side.Controls) c.Dispose();
            side.Controls.Clear();
            navs.Clear();
            bool adv = Session.Advanced;
            List<NavButton> items = new List<NavButton>();
            items.Add(Nav("الرئيسية", "", delegate { Show(new DashboardView(this)); }, true));
            items.Add(Nav("إخراج مخزني", "", delegate { OpenDoc(DocKind.Issue, 0); }, Session.Can(Session.IssView)));
            items.Add(Nav("إدخال مخزني", "", delegate { OpenDoc(DocKind.Receipt, 0); }, Session.Can(Session.RecView)));
            items.Add(Nav("مردود إخراج مخزني", "", delegate { OpenDoc(DocKind.Return, 0); }, Session.Can(Session.RecView)));
            items.Add(Nav("مردود مشتريات", "", delegate { OpenDoc(DocKind.PurchaseReturn, 0); }, Session.Can(Session.RecView)));
            items.Add(Nav("حجز مواد لمركز كلفة", "", delegate { OpenDoc(DocKind.Reserve, 0); }, Session.Can(Session.IssView) && Db.HasProj));
            items.Add(Nav("طلبات الصرف", "", delegate { Show(new RequisitionsView(this)); }, Requisitions.Ready && (Session.Can(Session.IssView) || Requisitions.CanApprove)));
            items.Add(Nav("نقل مخزني", "", delegate { OpenDoc(DocKind.Transfer, 0); }, adv && Session.Can(Session.Transfer)));
            items.Add(Nav("مراكز الكلفة", "", delegate { Show(new ProjectsView()); }, true));
            items.Add(Nav("بطاقة مادة", "", delegate { Show(new ItemsView(this)); }, true));
            items.Add(Nav("سجل السندات", "", delegate { Show(new DocListView(this)); }, true));
            items.Add(Nav("طلبات الشراء", "\uE7BF", delegate { Show(new PurchaseRequestsView()); }, adv && Db.HasPreq && Session.Can(Session.RecView)));
            items.Add(Nav("سندات الإتلاف", "", delegate { Show(new WriteOffsView(this)); }, WriteOffs.Ready && (adv || WriteOffs.CanApprove) && (WriteOffs.CanPrepare || Session.Can(Session.IssView))));
            items.Add(Nav("الجرد الفعلي", "", delegate { Show(new StockCountsView(this)); }, adv && (Session.Can(Session.RecView) || Session.Can(Session.IssView))));
            items.Add(Nav("الجهات والأشخاص", "", delegate { Show(new PersonsView()); }, adv));
            items.Add(Nav("التقارير", "", delegate { ReportsWindow.Open(this, null, 0, 0, 0); }, Session.Can(Session.Reports)));
            items.Add(Nav("المخازن والأنواع", "", delegate { Show(new LookupsView()); }, adv && Session.Can(Session.Setup)));
            items.Add(Nav("المستخدمون", "", delegate { Show(new UsersView()); }, adv && Session.IsAdmin));
            items.Add(Nav("الإعدادات", "", delegate { Show(new ToolsView(this)); }, true));
            items.Add(Nav("خروج", "", delegate { ExitProgram(); }, true));
            foreach (NavButton b in items) b.Height = adv ? 28 : 40;   // 28: the whole advanced menu fits at the minimum window height (680)
            // «الإعدادات» and «خروج» sit at the bottom of the pane (as in Windows 11)
            int nb = 2;
            for (int i = items.Count - nb; i < items.Count; i++) { items[i].Dock = DockStyle.Bottom; side.Controls.Add(items[i]); }
            for (int i = items.Count - nb - 1; i >= 0; i--) if (items[i].Visible) side.Controls.Add(items[i]);
            // ☰ collapse / expand the menu (icons only when collapsed; remembered per user)
            bool collapsed = Db.GetSetting("nav_collapsed_user" + Session.UserNo, "0") == "1";
            NavButton toggle = new NavButton(collapsed ? "توسيع القائمة" : "طي القائمة", "\uE700");
            toggle.Dock = DockStyle.Top; toggle.Height = 36;
            toggle.Click += delegate
            {
                Db.SetSetting("nav_collapsed_user" + Session.UserNo, side.Width < 90 ? "0" : "1");
                BuildNav();
            };
            side.Controls.Add(toggle);
            toggle.TabIndex = 0; for (int i = 0; i < items.Count; i++) items[i].TabIndex = i + 1;   // Tab from top to bottom
            side.Width = collapsed ? 58 : 236;
            if (navTips == null) navTips = new ToolTip();
            navTips.RemoveAll();
            foreach (Control c in side.Controls) { NavButton nb2 = c as NavButton; if (nb2 != null) navTips.SetToolTip(nb2, Lang.T(nb2.Text)); }
            side.ResumeLayout();
        }
        ToolTip navTips;

        NavButton Nav(string text, string glyph, Action act, bool visible)
        {
            NavButton b = new NavButton(text, glyph);
            b.Visible = visible;
            b.Click += delegate
            {
                if (text == "خروج" || text == "التقارير") { act(); return; }   // reports open in their own window: the current screen stays
                IView v = current as IView;
                if (v != null && !v.CanLeave()) return;
                foreach (NavButton n in navs) { n.Active = n == b; n.Invalidate(); }
                act();
            };
            navs.Add(b);
            return b;
        }

        void Activate(string text)
        {
            foreach (NavButton n in navs) { n.Active = n.Text == text; n.Invalidate(); }
        }

        public void Show(Control v)
        {
            content.SuspendLayout();
            Control old = current;
            v.Dock = DockStyle.Fill;
            content.Controls.Add(v);
            current = v;
            if (old != null) { content.Controls.Remove(old); old.Dispose(); }
            content.ResumeLayout();
            Lang.Apply(v);
            if (v is DashboardView) Activate("الرئيسية");
            else if (v is DocListView) Activate("سجل السندات");
            else if (v is ItemsView) Activate("بطاقة مادة");
            else if (v is ReportsView) Activate("التقارير");
            else if (v is ProjectsView) Activate("مراكز الكلفة");
            else if (v is PersonsView) Activate("الجهات والأشخاص");
            else if (v is LookupsView) Activate("المخازن والأنواع");
            else if (v is UsersView) Activate("المستخدمون");
            else if (v is ToolsView) Activate("الإعدادات");
            else if (v is RequisitionsView || v is RequisitionView) Activate("طلبات الصرف");
            else if (v is PurchaseRequestsView) Activate("طلبات الشراء");
            else if (v is WriteOffsView || v is WriteOffView) Activate("سندات الإتلاف");
            else if (v is StockCountsView || v is StockCountView) Activate("الجرد الفعلي");
            v.Focus();
        }

        public static string NavName(DocKind k)
        {
            switch (k)
            {
                case DocKind.Issue: return "إخراج مخزني";
                case DocKind.Receipt: return "إدخال مخزني";
                case DocKind.Return: return "مردود إخراج مخزني";
                case DocKind.PurchaseReturn: return "مردود مشتريات";
                case DocKind.Reserve: return "حجز مواد لمركز كلفة";
                default: return "نقل مخزني";
            }
        }

        public static int ViewPerm(DocKind k)
        {
            if (k == DocKind.Issue || k == DocKind.Reserve) return Session.IssView;
            if (k == DocKind.Transfer) return Session.Transfer;
            return Session.RecView;
        }

        public void OpenDoc(DocKind kind, int id) { OpenDoc(kind, id, 0); }

        public void OpenDoc(DocKind kind, int id, int project)
        {
            IView cur = current as IView;
            if (cur != null && current.Parent != null && !cur.CanLeave()) return;
            if (!Session.Demand(ViewPerm(kind))) return;
            Show(new DocView(this, kind, id, project));
            Activate(NavName(kind));
        }

        public void OpenReport(string key, int itemNo, int personNo) { OpenReport(key, itemNo, personNo, 0); }

        public void OpenReport(string key, int itemNo, int personNo, int projNo)
        {
            ReportsWindow.Open(this, key, itemNo, personNo, projNo);   // separate window, the current screen stays open
        }

        /// <summary>Document lookup from the home screen: number, cost centre / supplier or notes.</summary>
        public void FindDoc(string text)
        {
            using (DocFindDialog d = new DocFindDialog(text))
                if (d.ShowDialog(this) == DialogResult.OK && d.Id > 0) OpenDoc(d.Kind, d.Id);
        }

        /// <summary>Home search: a cost centre's statement.</summary>
        public void FindProject(string text)
        {
            if (!Db.HasProj || !Session.Demand(Session.Reports)) return;
            using (SearchDialog d = new SearchDialog("بحث عن مركز كلفة", Cache.Projects, "proj_no", new[] { "proj_name", "proj_code", "type_name" },
                                                     new[] { "الاسم", "الرمز", "النوع" }, new[] { 0, 120, 140 }, text ?? ""))
                if (d.ShowDialog(this) == DialogResult.OK && d.SelectedId > 0) ReportsWindow.Open(this, "project", 0, 0, d.SelectedId);
        }

        /// <summary>Home search: the statement of a supplier / person.</summary>
        public void FindPerson(string text)
        {
            if (!Session.Demand(Session.Reports)) return;
            int id = SearchDialog.PickPerson(this, text ?? "");
            if (id > 0) ReportsWindow.Open(this, "person", 0, id, 0);
        }

        /// <summary>Instant item lookup from anywhere (Ctrl+F): the chosen item's account statement (item card) opens
        /// (owner's request); users without the reports permission get the item itself.</summary>
        public void QuickFind(string text)
        {
            int id = SearchDialog.PickItem(this, text ?? "", true);
            if (id == 0) return;
            if (Session.Can(Session.Reports)) ReportsWindow.Open(this, "card", id, 0, 0);
            else if (Session.Can(Session.Items))
            {
                using (ItemDialog d = new ItemDialog(id)) if (d.ShowDialog(this) == DialogResult.OK) Cache.LoadItems();
            }
            else Session.Demand(Session.Reports);
        }

        void UpdateOnline()
        {
            DataTable t = Db.OnlineUsers();
            lblOnline.Text = "  ● المتصلون الآن: " + t.Rows.Count + "  (اضغط للتفاصيل)";
        }

        void ShowOnline()
        {
            DataTable t = Db.OnlineUsers();
            List<string> lines = new List<string>();
            foreach (DataRow r in t.Rows)
                lines.Add("• " + Db.S(r["user_name"]) + "، " + Db.S(r["computer"]) + "  (منذ " + Db.Dt(r["started"]).ToString("HH:mm") + ")");
            UI.Info(lines.Count == 0 ? "لا يوجد" : "المستخدمون المتصلون الآن:\n\n" + string.Join("\n", lines.ToArray()));
        }

        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            if (keyData == Keys.F5 && current is DashboardView) { Show(new DashboardView(this)); return true; }
            if (keyData == (Keys.Control | Keys.F) || keyData == Keys.F3) { QuickFind(""); return true; }
            if (keyData == Keys.F1) { Manual.Open(this); return true; }   // «دليل الاستخدام»
            return base.ProcessCmdKey(ref msg, keyData);
        }
    }
}
