using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Windows.Forms;

namespace Stores.Installer
{
    /// <summary>The window of the setup: main page (install / update / same / newer), «the program is open», progress, result;
    /// and the same for the uninstaller. Every page is laid out in left-to-right coordinates and mirrored for Arabic (Put).</summary>
    class SetupForm : Form
    {
        const int W = 640, HeadH = 92, BodyH = 340, FootH = 60, M = 30;   // M = side margin
        static readonly Color Blue = Color.FromArgb(47, 86, 163), Green = Color.FromArgb(15, 138, 75), Muted = Color.FromArgb(96, 103, 115),
                              Line = Color.FromArgb(224, 227, 233), Amber = Color.FromArgb(154, 91, 0), Red = Color.FromArgb(190, 30, 40);

        readonly SetupEngine en;
        readonly bool uninstall;
        /// <summary>Exit code of the process.</summary>
        public int Code = SetupCode.Cancelled;

        Panel head, body, foot;
        Action page;                        // rebuilds the current page (language switch)
        System.Windows.Forms.Timer poll;
        bool busy, desktop = true;
        volatile bool skipComponents, closing;

        internal List<InstallCopy> Found = new List<InstallCopy>();
        internal InstallCopy Sel;           // the copy that will be replaced, null = a new folder
        internal string Dir;                // target folder
        string oldDir;                      // another folder: its settings and shortcuts are taken over
        bool mixedElevated, auto;
        string warn = "";
        ProgressBar bar; Label barText; Button skip;

        public SetupForm(SetupEngine engine, string dir) : this(engine, dir, false) { }

        public SetupForm(SetupEngine engine, string dir, bool uninstall)
        {
            en = engine; this.uninstall = uninstall;
            FormBorderStyle = FormBorderStyle.FixedSingle; MaximizeBox = false;
            StartPosition = FormStartPosition.CenterScreen;
            ClientSize = new Size(W, HeadH + BodyH + FootH);
            BackColor = Color.White;
            try { using (Stream ico = OpenIcon()) Icon = new Icon(ico); } catch { }
            AutoScaleMode = AutoScaleMode.None;

            head = new Panel(); head.Bounds = new Rectangle(0, 0, W, HeadH); head.BackColor = Blue;
            body = new Panel(); body.Bounds = new Rectangle(0, HeadH, W, BodyH); body.BackColor = Color.White;
            foot = new Panel(); foot.Bounds = new Rectangle(0, HeadH + BodyH, W, FootH); foot.BackColor = Color.FromArgb(246, 247, 250);
            foot.Paint += delegate(object s, PaintEventArgs e) { using (Pen p = new Pen(Line)) e.Graphics.DrawLine(p, 0, 0, W, 0); };
            Controls.AddRange(new Control[] { body, foot, head });

            poll = new System.Windows.Forms.Timer(); poll.Interval = 700;
            if (uninstall) { Dir = dir; Show(UninstallPage); }
            else
            {
                Found = InstallFind.All(en.Env);
                InstallCopy at = dir == null ? null : InstallFind.At(en.Env, dir);
                if (at != null) Select(at);
                else if (dir != null) { Dir = dir; oldDir = Found.Count > 0 ? Found[0].Dir : null; }
                else if (Found.Count > 0) Select(Found[0]);
                else Dir = en.Env.DefaultDir;
                Show(MainPage);
            }
            // the one-file setup: run it and everything is done (files, then the program sets up the server by itself)
            long ps, pn;
            auto = !uninstall && PackageInside.Find(en.SetupExe, out ps, out pn);
            if (auto && (Sel == null || InstallFind.Compare(Sel.Version, en.Version) <= 0)) Show(AgreePage);
            FormClosing += delegate(object s, FormClosingEventArgs e)
            {
                if (busy) { e.Cancel = true; return; }
                closing = true; poll.Stop();
            };
            Disposed += delegate { closing = true; poll.Dispose(); };
        }

        static Stream OpenIcon() { return typeof(SetupForm).Assembly.GetManifestResourceStream("app.ico"); }

        void Select(InstallCopy c) { Sel = c; Dir = c.Dir; oldDir = null; }

        // ------------------------------------------------------------ building blocks
        Font F(float size, bool bold) { return new Font("Segoe UI", size, bold ? FontStyle.Bold : FontStyle.Regular); }

        /// <summary>Adds a control at left-to-right coordinates (mirrored for Arabic).</summary>
        T Put<T>(Control parent, T c, int x, int y, int w, int h) where T : Control
        {
            c.Bounds = new Rectangle(Tx.En ? x : W - x - w, y, w, h);
            parent.Controls.Add(c);
            return c;
        }

        Label Lbl(string text, int y, int h, float size, bool bold, Color color)
        {
            Label l = new Label(); l.AutoSize = false; l.Text = text; l.Font = F(size, bold); l.ForeColor = color; l.BackColor = Color.Transparent;
            l.RightToLeft = Tx.En ? RightToLeft.No : RightToLeft.Yes; l.TextAlign = ContentAlignment.TopLeft; l.UseMnemonic = false;
            return Put(body, l, M, y, W - 2 * M, h);
        }

        /// <summary>A label as high as its text needs; returns the y below it.</summary>
        int Para(string text, int y, float size, bool bold, Color color)
        {
            Label l = Lbl(text, y, 20, size, bold, color);
            TextFormatFlags f = TextFormatFlags.WordBreak | TextFormatFlags.NoPrefix | (Tx.En ? 0 : TextFormatFlags.RightToLeft | TextFormatFlags.Right);
            l.Height = TextRenderer.MeasureText(text, l.Font, new Size(l.Width - 8, 2000), f).Height + 4;
            return y + l.Height;
        }

        /// <summary>A folder path: always left-to-right, can be selected and copied.</summary>
        TextBox PathBox(string path, int y, bool edit, int w)
        {
            if (ShowPath != null) path = ShowPath(path);
            TextBox t = new TextBox(); t.Font = F(10f, false); t.RightToLeft = RightToLeft.No; t.Text = path; t.ReadOnly = !edit;
            t.BackColor = edit ? Color.White : Color.FromArgb(246, 247, 250); t.BorderStyle = BorderStyle.FixedSingle; t.TabStop = edit;
            t.SelectionStart = path.Length;
            return Put(body, t, M, y, w, 26);
        }

        Button Btn(string text, bool primary)
        {
            Button b = new Button(); b.Text = text; b.FlatStyle = FlatStyle.Flat; b.Font = F(10.5f, primary); b.Cursor = Cursors.Hand; b.UseMnemonic = false;
            b.BackColor = primary ? Blue : Color.White; b.ForeColor = primary ? Color.White : Color.FromArgb(31, 41, 55);
            b.FlatAppearance.BorderColor = primary ? Blue : Color.FromArgb(200, 204, 212);
            b.RightToLeft = Tx.En ? RightToLeft.No : RightToLeft.Yes;
            b.EnabledChanged += delegate { b.BackColor = b.Enabled ? (primary ? Blue : Color.White) : Color.FromArgb(205, 210, 220); };
            return b;
        }

        Button Big(string text, int y, Action click)
        {
            Button b = Btn(text, true); b.Font = F(13f, true);
            b.Click += delegate { click(); };
            return Put(body, b, M, y, Math.Max(270, TextRenderer.MeasureText(text, b.Font).Width + 40), 48);
        }

        Button FootBtn(string text, int fromEnd, bool primary, Action click)
        {
            int w = Math.Max(130, TextRenderer.MeasureText(text, F(10.5f, primary)).Width + 30);
            Button b = Btn(text, primary);
            b.Click += delegate { click(); };
            return Put(foot, b, W - M - fromEnd - w, 12, w, 36);
        }

        LinkLabel Link(string text, int y, Action click)
        {
            LinkLabel l = new LinkLabel(); l.Text = text; l.Font = F(10f, false); l.LinkBehavior = LinkBehavior.HoverUnderline; l.LinkColor = Blue; l.ActiveLinkColor = Blue;
            l.AutoSize = false; l.RightToLeft = Tx.En ? RightToLeft.No : RightToLeft.Yes; l.TextAlign = ContentAlignment.TopLeft;
            l.LinkClicked += delegate { click(); };
            return Put(body, l, M, y, W - 2 * M, 24);
        }

        CheckBox Check(string text, int y, bool value)
        {
            CheckBox c = new CheckBox(); c.Text = text; c.Font = F(10f, false); c.Checked = value; c.AutoSize = false; c.UseMnemonic = false;
            c.RightToLeft = Tx.En ? RightToLeft.No : RightToLeft.Yes;
            return Put(body, c, M, y, W - 2 * M, 26);
        }

        /// <summary>Shows a page (and remembers it for the language switch).</summary>
        internal void Show(Action build)
        {
            page = build;
            poll.Stop(); poll.Dispose(); poll = new System.Windows.Forms.Timer(); poll.Interval = 700;
            foreach (Panel p in new[] { head, body, foot })
            {
                Control[] old = new Control[p.Controls.Count]; p.Controls.CopyTo(old, 0);
                p.Controls.Clear();
                foreach (Control c in old) c.Dispose();
            }
            bar = null; barText = null; skip = null; AcceptButton = null;
            Text = uninstall ? Tx.S("إزالة نظام إدارة المخازن", "Uninstall Inventory Management") : Tx.Title;
            SuspendLayout();
            Header(); build();
            ResumeLayout();
        }

        void Header()
        {
            Panel pic = new Panel(); pic.BackColor = Color.White;
            Icon big = null;
            try { using (Stream ico = OpenIcon()) big = new Icon(ico, 48, 48); } catch { }   // (Icon.ToBitmap cannot read its PNG frames)
            pic.Paint += delegate(object o, PaintEventArgs e) { if (big != null) e.Graphics.DrawIcon(big, new Rectangle(4, 4, 48, 48)); };
            pic.Disposed += delegate { if (big != null) big.Dispose(); };
            Put(head, pic, M, 18, 56, 56);
            Label t = new Label(); t.Text = Tx.S("نظام إدارة المخازن", "Inventory Management Software"); t.Font = F(16f, true); t.ForeColor = Color.White;
            t.RightToLeft = Tx.En ? RightToLeft.No : RightToLeft.Yes; t.TextAlign = ContentAlignment.MiddleLeft;
            Put(head, t, M + 70, 16, 400, 34);
            Label v = new Label(); v.Font = F(10f, false); v.ForeColor = Color.FromArgb(214, 224, 244);
            v.Text = uninstall ? Tx.S("إزالة البرنامج", "Uninstall") : Tx.S("برنامج التنصيب • الإصدار ", "Setup • version ") + en.Version;
            v.RightToLeft = Tx.En ? RightToLeft.No : RightToLeft.Yes; v.TextAlign = ContentAlignment.MiddleLeft;
            Put(head, v, M + 70, 50, 400, 24);
            LinkLabel lang = new LinkLabel(); lang.Text = Tx.En ? "عربي" : "English"; lang.Font = F(10.5f, false); lang.LinkColor = Color.White; lang.ActiveLinkColor = Color.White;
            lang.LinkBehavior = LinkBehavior.HoverUnderline; lang.TextAlign = ContentAlignment.MiddleCenter; lang.Enabled = !busy; lang.DisabledLinkColor = Color.FromArgb(150, 170, 210);
            lang.LinkClicked += delegate { if (busy) return; Tx.En = !Tx.En; Show(page); };
            Put(head, lang, W - M - 80, 14, 80, 26);
        }

        // ------------------------------------------------------------ one-file setup: Microsoft's terms (one click, then everything runs)
        internal void AgreePage()
        {
            int y = Para(Tx.S("تنصيب البرنامج والسيرفر", "Install the program and the server"), 22, 15f, true, Color.FromArgb(17, 24, 39)) + 8;
            y = Para(Tx.S("يحتوي هذا الملف Microsoft SQL Server 2022 Express. بالضغط على «أوافق» تقبل شروط ترخيص مايكروسوفت له.",
                          "This file contains Microsoft SQL Server 2022 Express. By pressing \"I agree\" you accept Microsoft's license terms for it."), y, 11f, false, Color.FromArgb(31, 41, 55)) + 4;
            Link(Tx.S("عرض شروط الترخيص", "Show the license terms"), y, delegate
            {
                try
                {
                    string f = Path.Combine(Path.GetTempPath(), "SQL Server 2022 Express license terms.pdf");
                    using (Stream s = typeof(SetupForm).Assembly.GetManifestResourceStream("sql-terms.pdf")) using (FileStream o = File.Create(f)) s.CopyTo(o);
                    Process.Start(f);
                }
                catch { try { Process.Start("https://www.microsoft.com/useterms"); } catch { } }
            });
            y += 42;
            AcceptButton = Big(Tx.S("أوافق، ابدأ التنصيب", "I agree, install"), y, Go);
            Lbl("© 2026 sarmadiq9090-coder", BodyH - 62, 20, 8.5f, false, Muted).RightToLeft = RightToLeft.No;
            Lbl(Tx.S("علامتا Microsoft وSQL Server لشركة مايكروسوفت، وهي لا ترعى هذا البرنامج.", "Microsoft and SQL Server are trademarks of Microsoft, which does not endorse this program."), BodyH - 40, 22, 8.5f, false, Muted);
            FootBtn(Tx.S("إلغاء", "Cancel"), 0, false, Close);
        }

        // ------------------------------------------------------------ main page
        internal void MainPage()
        {
            int y = 22;
            if (Found.Count > 1 && Sel != null) y = CopiesList(y);
            if (Sel == null) { NewFolderPage(y); return; }

            string v = Sel.Version.Length == 0 ? "" : Sel.Version + " ";
            int cmp = InstallFind.Compare(Sel.Version, en.Version);
            string head1 = cmp < 0
                ? Tx.S("وُجدت نسخة قديمة " + v + "في هذا المجلد، وستُستبدل تلقائيا بالنسخة " + en.Version + ":", "An older version " + v + "was found in this folder. It will be replaced automatically by version " + en.Version + ":")
                : cmp == 0
                ? Tx.S("هذه النسخة " + en.Version + " منصّبة بالفعل في:", "This version " + en.Version + " is already installed in:")
                : Tx.S("النسخة المنصّبة " + v + "أحدث من هذه النسخة " + en.Version + ":", "The installed version " + v + "is newer than this one (" + en.Version + "):");
            y = Para(head1, y, 12.5f, true, cmp > 0 ? Amber : Color.FromArgb(17, 24, 39)) + 6;
            PathBox(Dir, y, false, W - 2 * M); y += 36;
            y = Para(cmp > 0
                ? Tx.S("لا حاجة لفعل شيء. لا يُنصح بالرجوع إلى نسخة أقدم.", "Nothing needs to be done. Going back to an older version is not recommended.")
                : Tx.S("بياناتك وإعداداتك والشعار والنسخ الاحتياطية لا تتغير. تُحفظ نسخة من الملفات القديمة وتُسترجع تلقائيا إذا حدث خطأ.",
                       "Your data, settings, logo and backups stay as they are. The old files are kept and put back automatically if anything goes wrong."), y, 10f, false, Muted) + 4;
            if (cmp <= 0 && InstallFind.RunningIn(en.Env, Dir).Count > 0)
                y = Para(Tx.S("● البرنامج مفتوح الآن: سيُطلب إغلاقه قبل التحديث.", "● The program is open now: it will be closed before the update."), y, 10f, true, Green) + 2;
            y = ComponentsNote(y) + 8;

            if (cmp < 0) AcceptButton = Big(Tx.S("تحديث الآن", "Update now"), y, Go);
            else if (cmp == 0) Big(Tx.S("إعادة التنصيب", "Reinstall"), y, Go);
            else Link(Tx.S("تنصيب النسخة الأقدم " + en.Version + " رغم ذلك…", "Install the older version " + en.Version + " anyway…"), y, delegate
            {
                if (Tx.Confirm(Tx.S("سترجع إلى نسخة أقدم من المنصّبة (" + Sel.Version + " ← " + en.Version + ").\nقد لا تفهم النسخة الأقدم ما أضافته الأحدث إلى البيانات.\n\nهل أنت متأكد؟",
                    "You will go back to an older version (" + Sel.Version + " → " + en.Version + ").\nIt may not understand what the newer one added to the data.\n\nAre you sure?"))) Go();
            });
            FootBtn(Tx.S("إلغاء", "Cancel"), 0, false, Close);
            LinkFoot(Tx.S("التنصيب في مجلد آخر…", "Install into another folder…"), OtherFolder);
        }

        /// <summary>Not over the selected copy: into a new folder (its settings and shortcuts are taken over).</summary>
        internal void OtherFolder()
        {
            oldDir = Sel.Dir; Sel = null; Dir = en.Env.DefaultDir;
            if (InstallEnv.SamePath(Dir, oldDir)) Dir += " 2";
            Show(MainPage);
        }

        void LinkFoot(string text, Action click)
        {
            LinkLabel l = new LinkLabel(); l.Text = text; l.Font = F(10f, false); l.LinkBehavior = LinkBehavior.HoverUnderline; l.LinkColor = Blue; l.ActiveLinkColor = Blue;
            l.RightToLeft = Tx.En ? RightToLeft.No : RightToLeft.Yes; l.TextAlign = ContentAlignment.MiddleLeft;
            l.LinkClicked += delegate { click(); };
            Put(foot, l, M, 18, 280, 24);
        }

        int CopiesList(int y)
        {
            y = Para(Tx.S("وُجدت أكثر من نسخة على هذا الحاسوب، اختر التي تُستبدل (الأخرى لا تتغير):", "Several copies were found. Choose the one to replace (the others stay as they are):"), y, 10f, false, Muted);
            ComboBox cb = new ComboBox(); cb.DropDownStyle = ComboBoxStyle.DropDownList; cb.Font = F(10f, false); cb.RightToLeft = RightToLeft.No;
            foreach (InstallCopy c in Found) cb.Items.Add((c.Version.Length > 0 ? c.Version : "?") + "   •   " + (ShowPath == null ? c.Dir : ShowPath(c.Dir)));
            cb.SelectedIndex = Math.Max(0, Found.IndexOf(Sel));
            Put(body, cb, M, y, W - 2 * M, 28);
            cb.SelectionChangeCommitted += delegate { int i = cb.SelectedIndex; BeginInvoke(new Action(delegate { Select(Found[i]); Show(MainPage); })); };
            return y + 38;
        }

        int ComponentsNote(int y)
        {
            long inside = PackageInside.Missing(en.SetupExe, Dir) / (1024 * 1024);
            if (inside > 0) return Para(Tx.S("● يحتوي هذا الملف حزمة السيرفر أيضا (" + inside + " ميغابايت): تنصيب السيرفر بدون إنترنت.", "● This file also holds the server package (" + inside + " MB): the server installs without internet."), y, 10f, false, Muted);
            List<string> c = InstallReplace.MissingComponents(en.SetupDir, Dir);
            if (c.Count == 0) return y;
            long mb = InstallReplace.ComponentsSize(en.SetupDir, c) / (1024 * 1024);
            return Para(Tx.S("● ستُنسخ أيضا ملفات السيرفر من مجلد «مكونات» (" + mb + " ميغابايت).", "● The server files of the folder «مكونات» will be copied too (" + mb + " MB)."), y, 10f, false, Muted);
        }

        void NewFolderPage(int y)
        {
            y = Para(Tx.S("تنصيب البرنامج على هذا الحاسوب", "Install the program on this computer"), y, 14f, true, Color.FromArgb(17, 24, 39)) + 4;
            y = Para(Tx.S("لا يحتاج صلاحيات المسؤول. المجلد:", "No administrator rights are needed. Folder:"), y, 10f, false, Muted) + 2;
            TextBox t = PathBox(Dir, y, true, W - 2 * M - 120);
            Button br = Btn(Tx.S("استعراض…", "Browse…"), false); br.Font = F(10f, false);
            Put(body, br, W - M - 110, y - 1, 110, 28);
            br.Click += delegate
            {
                using (FolderBrowserDialog d = new FolderBrowserDialog())
                {
                    d.Description = Tx.S("اختر مجلد البرنامج", "Choose the program folder");
                    if (Directory.Exists(t.Text)) d.SelectedPath = t.Text;
                    if (d.ShowDialog(this) != DialogResult.OK) return;
                    Chosen(ProgramFolder(en.Env, d.SelectedPath));
                }
            };
            t.TextChanged += delegate { Dir = t.Text; };
            y += 38;
            if (oldDir != null)
                y = Para(Tx.S("النسخة القديمة تبقى في مجلدها كما هي. تُنسخ منها إعدادات الاتصال والشعار، وتُحوَّل اختصاراتها إلى المجلد الجديد.",
                              "The old copy stays in its folder. Its connection settings and logo are copied, and its shortcuts will point to the new folder."), y, 10f, false, Muted) + 2;
            CheckBox c = Check(Tx.S("أيقونة على سطح المكتب", "Desktop icon"), y, desktop);
            c.CheckedChanged += delegate { desktop = c.Checked; };
            y = ComponentsNote(y + 30) + 10;
            AcceptButton = Big(Tx.S("تنصيب", "Install"), y, delegate
            {
                string d = InstallEnv.Norm(Dir);
                if (d == null || !Path.IsPathRooted(Dir.Trim())) { Tx.Warn(Tx.S("اكتب اسم المجلد كاملا، مثلا D:\\Inventory Management", "Type the full folder name, for example D:\\Inventory Management")); return; }
                Dir = d;
                if (Chosen(d)) return;      // there is a copy in it: the page shows what will happen first
                Go();
            });
            FootBtn(Tx.S("إلغاء", "Cancel"), 0, false, Close);
            if (Found.Count > 0) LinkFoot(Tx.S("الرجوع إلى النسخة الموجودة", "Back to the installed copy"), delegate { Select(Found[0]); Show(MainPage); });
        }

        /// <summary>Takes the folder; true (and the page is rebuilt) when a copy of the program is in it.</summary>
        bool Chosen(string dir)
        {
            InstallCopy c = InstallFind.At(en.Env, dir);
            if (c == null) { bool same = InstallEnv.SamePath(Dir, dir); Dir = dir; if (!same) Show(MainPage); return false; }
            foreach (InstallCopy f in Found) if (InstallEnv.SamePath(f.Dir, c.Dir)) c = f;
            if (!Found.Contains(c)) Found.Add(c);
            Select(c); Show(MainPage);
            return true;
        }

        /// <summary>A folder that holds other files gets a sub-folder for the program.</summary>
        internal static string ProgramFolder(InstallEnv env, string dir)
        {
            try
            {
                if (File.Exists(env.ExePath(dir)) || !Directory.Exists(dir) || Directory.GetFileSystemEntries(dir).Length == 0) return dir;
                return Path.Combine(dir, Path.GetFileName(env.DefaultDir));
            }
            catch { return dir; }
        }

        // ------------------------------------------------------------ the steps
        void Go()
        {
            if (InstallFind.RunningIn(en.Env, Dir).Count > 0) { Show(OpenPage); return; }
            mixedElevated = false;
            if (!InstallReplace.CanWrite(Dir)) { Show(NotWritablePage); return; }
            StartCopy();
        }

        internal void NotWritablePage()
        {
            int y = Para(Tx.S("لا توجد صلاحية كتابة في هذا المجلد:", "You may not write into this folder:"), 22, 13f, true, Amber) + 6;
            PathBox(Dir, y, false, W - 2 * M); y += 38;
            if (Sel != null)
            {
                y = Para(Tx.S("الأفضل التنصيب في مجلد آخر (مثل المجلد المقترح): تُنسخ إليه إعداداتك وتُحوَّل الاختصارات، ولا تحتاج صلاحيات بعدها.\nأو تابع في نفس المجلد بصلاحية المسؤول: ستظهر نافذة ويندوز المعتادة.",
                              "It is better to install into another folder (the suggested one): your settings are copied, the shortcuts are changed, and no rights are needed later.\nOr continue in this folder as administrator: the usual Windows prompt will appear."), y, 10.5f, false, Muted) + 14;
                Button other = Big(Tx.S("مجلد آخر (موصى به)", "Another folder (recommended)"), y, OtherFolder);
                Button b = Btn(Tx.S("متابعة كمسؤول", "Continue as administrator"), false);
                b.Click += delegate { mixedElevated = true; StartCopy(); };
                Put(body, b, M + other.Width + 16, y, W - 2 * M - other.Width - 16, 48);
            }
            else
            {
                y = Para(Tx.S("يحفظ البرنامج إعداداته في مجلده، لذلك اختر مجلدا يمكنك الكتابة فيه، مثل المجلد المقترح.", "The program keeps its settings in its folder, so choose a folder you can write into, for example the suggested one."), y, 10.5f, false, Muted) + 14;
                Big(Tx.S("استعمال المجلد المقترح", "Use the suggested folder"), y, delegate { Dir = en.Env.DefaultDir; Show(MainPage); });
            }
            FootBtn(Tx.S("رجوع", "Back"), 0, false, delegate { Show(MainPage); });
        }

        // ------------------------------------------------------------ «the program is open»
        internal bool OldProgram = false;                   // tests: which variant is shown
        internal static Func<string, string> ShowPath;      // tests: neutral folder names in the pictures
        Label openState;

        internal void OpenPage()
        {
            List<int> run = InstallFind.RunningIn(en.Env, Dir);
            bool old = OldProgram;
            foreach (int pid in run) if (!ProgramExit.CanAsk(pid)) old = true;
            int y = Para(Tx.S("البرنامج مفتوح الآن", "The program is open"), 22, 14f, true, Color.FromArgb(17, 24, 39)) + 4;
            if (!old)
            {
                y = Para(Tx.S("طُلب من البرنامج أن يُغلق. إذا ظهر سؤال عن حفظ سند فأجبه.\nتتابع العملية تلقائيا بعد إغلاقه.", "The program was asked to exit. If it asks about saving a document, answer it.\nThe setup continues by itself when the program has closed."), y, 11f, false, Muted) + 10;
                openState = Lbl(Tx.S("بانتظار إغلاق البرنامج…", "Waiting for the program to close…"), y, 90, 11f, true, Green);
                FootBtn(Tx.S("اطلب الإغلاق مرة أخرى", "Ask again"), 145, true, delegate { Show(OpenPage); });
                AskAll(run);
            }
            else
            {
                y = Para(Tx.S("قد يكون مخفيا بجانب الساعة. هذه النسخة القديمة لا تُغلق تلقائيا، لذلك:\n1. افتح البرنامج واحفظ عملك.\n2. اضغط «خروج» في قائمته، أو زر الفأرة الأيمن على أيقونته بجانب الساعة ← خروج.",
                              "It may be hidden next to the clock. This old version cannot be closed automatically:\n1. Open the program and save your work.\n2. Press \"Exit\" in its menu, or right-click its icon next to the clock → Exit."), y, 11f, false, Muted) + 8;
                Big(Tx.S("أُغلق البرنامج، تابع", "It is closed, continue"), y, delegate
                {
                    if (InstallFind.RunningIn(en.Env, Dir).Count == 0) { Resume2(); return; }
                    openState.Text = Tx.S("ما زال البرنامج مفتوحا.", "The program is still open.");
                });
                openState = new Label(); openState.Font = F(10.5f, true); openState.ForeColor = Red; openState.RightToLeft = Tx.En ? RightToLeft.No : RightToLeft.Yes;
                openState.TextAlign = ContentAlignment.MiddleLeft;
                Put(body, openState, M + 285, y, W - 2 * M - 285, 48);
                y += 64;
                CheckBox c = Check(Tx.S("لا أستطيع إغلاقه • حفظت عملي وأريد إنهاءه الآن (ما لم يُحفظ يضيع)", "I cannot close it • my work is saved, end it now (unsaved work is lost)"), y, false);
                Button kill = Btn(Tx.S("إنهاء البرنامج والمتابعة", "End the program and continue"), false); kill.Enabled = false;
                Put(body, kill, M, y + 32, 270, 36);
                c.CheckedChanged += delegate { kill.Enabled = c.Checked; };
                kill.Click += delegate
                {
                    if (!Tx.Confirm(Tx.S("سيُنهى البرنامج القديم فورا. ما لم يُحفظ فيه يضيع.\n\nهل حفظت عملك؟", "The old program will be ended at once. Unsaved work in it is lost.\n\nIs your work saved?"))) return;
                    foreach (int pid in InstallFind.RunningIn(en.Env, Dir)) ProgramExit.Kill(pid);
                    if (InstallFind.RunningIn(en.Env, Dir).Count == 0) Resume2();
                    else openState.Text = Tx.S("تعذر إنهاؤه (قد يعمل كمسؤول). أغلقه بنفسك.", "It could not be ended (administrator?). Please close it.");
                };
            }
            FootBtn(Tx.S("إلغاء", "Cancel"), 0, false, Close);
            poll.Tick += delegate { if (!closing && InstallFind.RunningIn(en.Env, Dir).Count == 0) Resume2(); };
            poll.Start();   // closed by the user: go on
        }

        void AskAll(List<int> pids)
        {
            Label state = openState;
            Thread t = new Thread(delegate()
            {
                foreach (int pid in pids)
                {
                    ExitAnswer a = ProgramExit.Ask(pid, 10 * 60000, delegate { return !closing && openState == state; });
                    if (a != ExitAnswer.Refused) continue;
                    try
                    {
                        BeginInvoke(new Action(delegate
                        {
                            if (openState != state || state.IsDisposed) return;
                            state.ForeColor = Amber;
                            state.Text = Tx.S("لم يُغلق البرنامج: أكمل عملك فيه واحفظه، أو أغلق النافذة المفتوحة فيه، ثم اضغط «اطلب الإغلاق مرة أخرى». لم يتغير شيء حتى الآن.",
                                              "The program did not close: finish and save your work, or close the window that is open in it, then press \"Ask again\". Nothing has been changed yet.");
                        }));
                    }
                    catch { }
                }
            });
            t.IsBackground = true; t.Start();
        }

        /// <summary>The program has closed: go on with what was started.</summary>
        void Resume2() { openState = null; poll.Stop(); if (uninstall) DoUninstall(); else Go(); }

        // ------------------------------------------------------------ progress
        internal void ProgressPage()
        {
            int y = Para(uninstall ? Tx.S("جارٍ إزالة البرنامج…", "Removing the program…") : Sel == null ? Tx.S("جارٍ التنصيب…", "Installing…") : Tx.S("جارٍ التحديث…", "Updating…"), 40, 14f, true, Color.FromArgb(17, 24, 39)) + 12;
            bar = new ProgressBar(); bar.RightToLeft = Tx.En ? RightToLeft.No : RightToLeft.Yes; bar.RightToLeftLayout = !Tx.En; bar.Maximum = 1000;
            if (mixedElevated || uninstall) { bar.Style = ProgressBarStyle.Marquee; bar.MarqueeAnimationSpeed = 30; }
            Put(body, bar, M, y, W - 2 * M, 22);
            barText = Lbl(Tx.S("نسخ ملفات البرنامج…", "Copying the program files…"), y + 34, 50, 10.5f, false, Muted);
            skip = Btn(Tx.S("تخطي ملفات السيرفر", "Skip the server files"), false); skip.Visible = false;
            skip.Click += delegate { skipComponents = true; skip.Enabled = false; };
            Put(body, skip, M, y + 92, 220, 36);
        }

        void StartCopy()
        {
            busy = true; warn = ""; skipComponents = false;
            Show(ProgressPage);
            int last = -1;
            en.Progress = delegate(long d, long t, string what)
            {
                int p = t <= 0 ? 0 : (int)(d * 1000 / t);
                if (p == last) return;
                last = p;
                try
                {
                    BeginInvoke(new Action(delegate
                    {
                        if (bar == null) return;
                        bar.Value = Math.Max(0, Math.Min(1000, p));
                        bool comp = what == "components";
                        skip.Visible = comp;
                        barText.Text = comp ? Tx.S("نسخ ملفات السيرفر («مكونات»): " + d / 1048576 + " من " + t / 1048576 + " ميغابايت", "Copying the server files («مكونات»): " + d / 1048576 + " of " + t / 1048576 + " MB")
                                            : Tx.S("نسخ ملفات البرنامج: " + d + " من " + t, "Copying the program files: " + d + " of " + t);
                    }));
                }
                catch { }
            };
            en.SkipComponents = delegate { return skipComponents; };
            string target = Dir, from = oldDir; bool elevated = mixedElevated, icon = desktop, english = Tx.En;
            int code = SetupCode.Failed;
            BackgroundWorker w = new BackgroundWorker();
            w.DoWork += delegate
            {
                code = elevated ? RunElevated(target) : en.CopyFiles(target);
                int comp = SetupCode.Ok;
                if (code == SetupCode.Ok && !elevated) comp = en.CopyComponents(target);
                if (code == SetupCode.ComponentsFailed) { code = SetupCode.Ok; comp = SetupCode.ComponentsFailed; }   // elevated helper
                if (comp == SetupCode.ComponentsFailed)
                    warn = Tx.S("تعذر نسخ ملفات السيرفر من «مكونات»: ", "The server files («مكونات») could not be copied: ") + en.Error;
                if (code == SetupCode.Ok) en.Finish(target, from, icon, english);
            };
            w.RunWorkerCompleted += delegate(object s, RunWorkerCompletedEventArgs e)
            {
                busy = false;
                if (e.Error != null) { code = SetupCode.Failed; en.Error = e.Error.Message; }
                Setup.L("install " + en.Version + " → " + target + ", code " + code + (en.Error.Length > 0 ? ", " + en.Error : ""));
                Code = code;
                if (code == SetupCode.Ok && auto && warn.Length == 0 && !skipComponents)
                {
                    try
                    {
                        Process.Start(new ProcessStartInfo(en.Env.ExePath(Dir), "/server /lang=" + (Tx.En ? "en" : "ar")) { WorkingDirectory = Dir, UseShellExecute = false });
                        Close(); return;
                    }
                    catch (Exception ex) { warn = ex.Message; }
                }
                if (code == SetupCode.Ok) Show(DonePage);
                else if (code == SetupCode.ProgramOpen && InstallFind.RunningIn(en.Env, target).Count > 0) Show(OpenPage);
                else if (code == SetupCode.NotWritable && !elevated) Show(NotWritablePage);
                else Show(FailedPage);
            };
            w.RunWorkerAsync();
        }

        /// <summary>The files are copied by a second setup process after the Windows prompt; everything else stays with this user.</summary>
        int RunElevated(string target)
        {
            try
            {
                // (a folder that ends with «\» would swallow the closing quote)
                ProcessStartInfo si = new ProcessStartInfo(en.SetupExe, "/files \"/D=" + (target.EndsWith("\\") ? target + "." : target) + "\"");
                si.UseShellExecute = true; si.Verb = "runas";
                using (Process p = Process.Start(si)) { p.WaitForExit(); en.Error = ""; return p.ExitCode; }
            }
            catch (Win32Exception ex)
            {
                if (ex.NativeErrorCode == 1223) { en.Error = Tx.S("لم تُعطَ صلاحية المسؤول.", "Administrator rights were not given."); return SetupCode.NotWritable; }
                en.Error = ex.Message; return SetupCode.Failed;
            }
        }

        // ------------------------------------------------------------ result
        internal void DonePage()
        {
            int y = Para("✓  " + (Sel != null ? Tx.S("تم التحديث إلى النسخة " + en.Version, "Updated to version " + en.Version) : Tx.S("تم تنصيب النسخة " + en.Version, "Version " + en.Version + " is installed")), 30, 16f, true, Green) + 8;
            PathBox(Dir, y, false, W - 2 * M); y += 40;
            y = Para(Sel != null
                ? Tx.S("بياناتك وإعداداتك كما كانت. الملفات القديمة محفوظة في المجلد ‎.update-backup‎", "Your data and settings are as before. The old files are kept in the folder .update-backup")
                : Tx.S("تجد البرنامج في قائمة ابدأ، وعلى سطح المكتب إذا اخترت الأيقونة.", "You find the program in the Start menu, and on the Desktop if you chose the icon."), y, 10.5f, false, Muted) + 6;
            if (warn.Length > 0) y = Para("⚠ " + warn, y, 10f, true, Amber) + 4;
            CheckBox run = Check(Tx.S("تشغيل البرنامج الآن", "Start the program now"), y + 6, true); run.Font = F(11.5f, true);
            AcceptButton = FootBtn(Tx.S("إنهاء", "Finish"), 0, true, delegate
            {
                if (run.Checked)
                    try { Process.Start(new ProcessStartInfo(en.Env.ExePath(Dir)) { WorkingDirectory = Dir, UseShellExecute = false }); }
                    catch (Exception ex) { Tx.Warn(Tx.S("تعذر تشغيل البرنامج:\n", "The program could not be started:\n") + ex.Message); }
                Close();
            });
        }

        internal int FailCode = -1;   // tests

        internal void FailedPage()
        {
            int code = FailCode >= 0 ? FailCode : Code;
            string h, d;
            if (code == SetupCode.ProgramOpen)
            {
                h = Tx.S("ملف البرنامج قيد الاستعمال", "The program file is in use");
                d = Tx.S("قد يكون البرنامج مفتوحا في حساب ويندوز آخر على هذا الحاسوب، أو من حاسوب آخر (مجلد مشترك). أغلقه هناك ثم أعد المحاولة. لم يتغير شيء.",
                         "The program may be open in another Windows account on this computer, or on another computer (shared folder). Close it there and try again. Nothing was changed.");
            }
            else if (code == SetupCode.NotWritable) { h = Tx.S("لا توجد صلاحية كتابة في المجلد", "You may not write into the folder"); d = Tx.S("لم يتغير شيء.", "Nothing was changed."); }
            else if (code == SetupCode.RollbackIncomplete)
            {
                h = Tx.S("لم يكتمل التنصيب، ولم تُسترجع بعض الملفات", "The installation failed and some files could not be put back");
                d = Tx.S("نسخة من الملفات القديمة في:\n", "A copy of the old files is in:\n") + en.BackupDir + Tx.S("\nأعد تشغيل الحاسوب ثم شغّل برنامج التنصيب مرة أخرى.", "\nRestart the computer and run the setup again.");
            }
            else { h = Tx.S("لم يكتمل التنصيب", "The installation failed"); d = Tx.S("أُعيدت النسخة السابقة كما كانت، ولم تُمسّ بياناتك.", "The previous version was put back, your data was not touched."); }
            int y = Para("✕  " + h, 30, 14f, true, Red) + 6;
            y = Para(d, y, 11f, false, Color.FromArgb(31, 41, 55)) + 8;
            if (en.Error.Length > 0) y = Para(en.Error, y, 9.5f, false, Muted);
            FootBtn(Tx.S("إغلاق", "Close"), 0, false, Close);
            FootBtn(Tx.S("إعادة المحاولة", "Try again"), 145, true, delegate { if (uninstall) Show(UninstallPage); else Show(MainPage); });
        }

        // ------------------------------------------------------------ uninstall
        internal void UninstallPage()
        {
            int y = Para(Tx.S("إزالة البرنامج من هذا الحاسوب", "Remove the program from this computer"), 22, 14f, true, Color.FromArgb(17, 24, 39)) + 4;
            PathBox(Dir ?? "", y, false, W - 2 * M); y += 38;
            y = Para(Tx.S("يُحذف: ملف البرنامج، دليل الاستخدام، الاختصارات.", "Removed: the program file, the manual, the shortcuts."), y, 11f, false, Muted) + 2;
            y = Para(Tx.S("يبقى: ملفات البيانات، الإعدادات (settings.ini)، الشعار، النسخ الاحتياطية، ومجلد «مكونات».", "Kept: data files, settings (settings.ini), logo, backups, and the folder «مكونات»."), y, 11f, true, Green) + 14;
            Big(Tx.S("إزالة البرنامج", "Uninstall"), y, delegate { if (InstallFind.RunningIn(en.Env, Dir).Count > 0) Show(OpenPage); else DoUninstall(); });
            FootBtn(Tx.S("إلغاء", "Cancel"), 0, false, Close);
        }

        void DoUninstall()
        {
            Code = en.Uninstall(Dir);
            Setup.L("uninstall " + Dir + ", code " + Code);
            if (Code == SetupCode.Ok) Show(UninstalledPage);
            else if (Code == SetupCode.ProgramOpen && InstallFind.RunningIn(en.Env, Dir).Count > 0) Show(OpenPage);
            else Show(FailedUninstallPage);
        }

        internal void FailedUninstallPage()
        {
            string d = Code == SetupCode.NotWritable ? Tx.S("لا توجد صلاحية كتابة في المجلد. شغّل Uninstall.exe كمسؤول.", "You may not write into the folder. Run Uninstall.exe as administrator.")
                     : Code == SetupCode.NotInstalled ? Tx.S("البرنامج غير موجود في هذا المجلد.", "The program is not in this folder.")
                     : Tx.S("ملف البرنامج قيد الاستعمال (حساب آخر أو حاسوب آخر). أغلقه ثم أعد المحاولة.", "The program file is in use (another account or computer). Close it and try again.");
            int y = Para("✕  " + Tx.S("لم يُزل البرنامج", "The program was not removed"), 30, 14f, true, Red) + 6;
            Para(d, y, 11f, false, Color.FromArgb(31, 41, 55));
            FootBtn(Tx.S("إغلاق", "Close"), 0, false, Close);
            FootBtn(Tx.S("إعادة المحاولة", "Try again"), 145, true, delegate { Show(UninstallPage); });
        }

        internal void UninstalledPage()
        {
            int y = Para("✓  " + Tx.S("أُزيل البرنامج", "The program was removed"), 30, 16f, true, Green) + 8;
            if (Directory.Exists(Dir))
            {
                y = Para(Tx.S("بقيت بياناتك وإعداداتك ونسخك الاحتياطية في:", "Your data, settings and backups are still in:"), y, 11f, false, Muted) + 4;
                PathBox(Dir, y, false, W - 2 * M);
            }
            FootBtn(Tx.S("إغلاق", "Close"), 0, true, Close);
        }
    }
}
