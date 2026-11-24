using System;
using System.Collections.Generic;
using System.Data;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Forms;

namespace Stores
{
    /// <summary>
    /// Screenshots for the user manual (docs/manual/img/*.png) with red numbered callouts drawn on them.
    /// Every screen is built off-screen on a COPY of a DemoData file (fake data only: the repository is public),
    /// filled with sample documents and captured; paths, the computer name and the company are replaced by neutral ones.
    ///   tests\bin\DemoData.exe tests\demo\empty-template.mdb %TEMP%\demo.mdb
    ///   tests\bin\ManualShots.exe %TEMP%\demo.mdb docs\manual\img [only=home,login,…]
    /// The data file is changed (sample requests, counts, bins…): give it a fresh demo file every time.
    /// The numbers drawn on an image are explained in docs/manual/index.html: keep both in step when a screen changes.
    /// </summary>
    static class ManualShotsMain
    {
        [DllImport("user32.dll")] static extern bool PrintWindow(IntPtr h, IntPtr hdc, uint flags);
        delegate bool EnumProc(IntPtr h, IntPtr l);
        [DllImport("user32.dll")] static extern bool EnumThreadWindows(int tid, EnumProc f, IntPtr l);
        [DllImport("user32.dll")] static extern bool EnumChildWindows(IntPtr p, EnumProc f, IntPtr l);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern int GetClassName(IntPtr h, StringBuilder s, int n);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern int GetWindowText(IntPtr h, StringBuilder s, int n);
        [DllImport("user32.dll")] static extern bool IsWindowVisible(IntPtr h);
        [DllImport("user32.dll")] static extern IntPtr GetDlgItem(IntPtr h, int id);
        [DllImport("user32.dll")] static extern bool PostMessage(IntPtr h, int msg, IntPtr w, IntPtr l);
        [DllImport("kernel32.dll")] static extern int GetCurrentThreadId();

        const string Company = "شركة المثال للمقاولات";
        const string FakeData = @"D:\المخازن\بيانات المخازن.AMR";
        const string FakeDir = @"D:\المخازن";
        const string FakePc = "PC-STORE-1";
        const string Admin = "المدير";
        static readonly Size Win = new Size(1280, 720);
        static readonly Color Red = Color.FromArgb(220, 30, 40);

        static string outDir, dataPath;
        static List<string> only;
        static MainForm main;
        static long total; static int count;

        // ================================================================== helpers
        /// <summary>A message box would stop the run: it is answered «Cancel» (or OK) and reported, so the shot can be fixed.</summary>
        static void CloseMessageBoxes()
        {
            List<IntPtr> wins = new List<IntPtr>();
            EnumThreadWindows(GetCurrentThreadId(), delegate(IntPtr h, IntPtr l)
            {
                StringBuilder c = new StringBuilder(64); GetClassName(h, c, 64);
                if (IsWindowVisible(h) && c.ToString() == "#32770") wins.Add(h);
                return true;
            }, IntPtr.Zero);
            foreach (IntPtr h in wins)
            {
                StringBuilder all = new StringBuilder();
                EnumChildWindows(h, delegate(IntPtr c, IntPtr l)
                {
                    StringBuilder n = new StringBuilder(64); GetClassName(c, n, 64);
                    if (n.ToString() == "Static") { StringBuilder t = new StringBuilder(2048); GetWindowText(c, t, 2048); all.Append(t); }
                    return true;
                }, IntPtr.Zero);
                Console.WriteLine("   [message box closed] " + all.ToString().Replace('\n', ' ').Replace('\r', ' '));
                PostMessage(h, 0x0111, (IntPtr)(GetDlgItem(h, 2) != IntPtr.Zero ? 2 : 1), IntPtr.Zero);
            }
        }

        static void Pump(int ms) { DateTime end = DateTime.Now.AddMilliseconds(ms); while (DateTime.Now < end) { Application.DoEvents(); System.Threading.Thread.Sleep(10); } }

        static object Field(object o, string name)
        {
            for (Type t = o.GetType(); t != null; t = t.BaseType)
            {
                FieldInfo f = t.GetField(name, BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Instance);
                if (f != null) return f.GetValue(o);
            }
            throw new Exception("field " + name + " not found in " + o.GetType().Name);
        }

        static object Call(object o, string name, params object[] a)
        {
            for (Type t = o.GetType(); t != null; t = t.BaseType)
            {
                MethodInfo m = t.GetMethod(name, BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Instance);
                if (m != null) return m.Invoke(o, a);
            }
            throw new Exception("method " + name + " not found in " + o.GetType().Name);
        }

        static List<T> All<T>(Control root, Func<T, bool> ok) where T : Control
        {
            List<T> l = new List<T>();
            foreach (Control c in root.Controls)
            {
                T t = c as T;
                if (t != null && t.Visible && (ok == null || ok(t))) l.Add(t);
                l.AddRange(All<T>(c, ok));
            }
            return l;
        }

        static T Find<T>(Control root, Func<T, bool> ok) where T : Control
        {
            List<T> l = All<T>(root, ok);
            if (l.Count == 0) throw new Exception(typeof(T).Name + " not found in " + root.GetType().Name);
            return l[0];
        }

        static Control Btn(Control root, string text) { return Find<Control>(root, delegate(Control c) { return (c is Button || c is NavButton || c is LinkLabel) && c.Text.Trim() == text; }); }
        static Control Starts(Control root, string text) { return Find<Control>(root, delegate(Control c) { return (c is Button || c is NavButton || c is LinkLabel || c is Label || c is CheckBox || c is RadioButton) && c.Text.Trim().StartsWith(text); }); }

        /// <summary>The rectangle of a control inside the captured window image.</summary>
        static Rectangle R(Form f, Control c)
        {
            Point p = c.PointToScreen(Point.Empty);
            return new Rectangle(p.X - f.Left, p.Y - f.Top, c.Width, c.Height);
        }

        static Rectangle U(Rectangle a, Rectangle b) { return Rectangle.Union(a, b); }

        /// <summary>Replaces anything that could identify this computer (paths, user and computer names) by neutral text.</summary>
        static void Scrub(Control root)
        {
            foreach (Control c in root.Controls) Scrub(c);
            string t = root.Text;
            if (!string.IsNullOrEmpty(t))
            {
                string n = Clean(t);
                if (n.Contains("تتم مزامنته") || n.Contains("يتوفر إصدار جديد") || n.Contains("synchroni") || n.Contains("new version")) n = "";
                if (n != t) root.Text = n;
            }
            DataGridView g = root as DataGridView;
            if (g != null)
                foreach (DataGridViewRow r in g.Rows)
                    foreach (DataGridViewCell cell in r.Cells)
                    {
                        string s = cell.Value as string;
                        if (s != null && Clean(s) != s) cell.Value = Clean(s);
                    }
        }

        static string Clean(string t)
        {
            string dir = Path.GetDirectoryName(dataPath);
            t = t.Replace(dataPath, FakeData).Replace(dir, FakeDir).Replace(Settings.AppDir, @"C:\Program Files\Inventory Management");
            if (Environment.MachineName.Length > 2) t = t.Replace(Environment.MachineName, FakePc);
            if (Environment.UserName.Length > 2) t = t.Replace(@"\" + Environment.UserName + @"\", @"\user\");
            if (Branding.DefaultCompany.Length > 0) t = t.Replace(Branding.DefaultCompany, Company);
            t = t.Replace("الشركة الهندسية", "الشركة");
            return t;
        }

        static bool Want(string name) { return only == null || only.Contains(name); }

        static void Show(Form f, Size size)
        {
            f.StartPosition = FormStartPosition.Manual; f.Location = new Point(-4000, -4000); f.ShowInTaskbar = false;
            f.Show();
            if (!size.IsEmpty) { f.Size = size; Pump(200); if (f.Size != size) f.Size = size; }
            Pump(500);
        }

        static Bitmap Grab(Form f)
        {
            Scrub(f);
            Pump(350);
            Bitmap b = new Bitmap(f.Width, f.Height, PixelFormat.Format24bppRgb);
            bool ok;
            using (Graphics g = Graphics.FromImage(b))
            {
                g.Clear(Color.White);
                IntPtr hdc = g.GetHdc();
                try { ok = PrintWindow(f.Handle, hdc, 2 /*PW_RENDERFULLCONTENT*/); } finally { g.ReleaseHdc(hdc); }
            }
            if (!ok) f.DrawToBitmap(b, new Rectangle(0, 0, f.Width, f.Height));
            // the report pages (WebBrowser) are not painted by PrintWindow off-screen: drawn from the document itself
            foreach (WebBrowser wb in All<WebBrowser>(f, null)) DrawBrowser(f, wb, b);
            visible = Visible(b);
            return b;
        }

        /// <summary>Part of the last captured window without the invisible resize border of Windows 10/11
        /// (PrintWindow paints it black): every saved image is cut to it.</summary>
        static Rectangle visible;

        static Rectangle Visible(Bitmap b)
        {
            Func<int, bool> blackCol = delegate(int x) { for (int k = 1; k < 8; k++) { Color c = b.GetPixel(x, b.Height * k / 8); if (c.R + c.G + c.B > 12) return false; } return true; };
            Func<int, bool> blackRow = delegate(int y) { for (int k = 1; k < 8; k++) { Color c = b.GetPixel(b.Width * k / 8, y); if (c.R + c.G + c.B > 12) return false; } return true; };
            int l = 0, r = 0, t = 0, d = 0;
            while (l < 16 && blackCol(l)) l++;
            while (r < 16 && blackCol(b.Width - 1 - r)) r++;
            while (t < 16 && blackRow(t)) t++;
            while (d < 16 && blackRow(b.Height - 1 - d)) d++;
            return Rectangle.FromLTRB(l, t, b.Width - r, b.Height - d);
        }

        [StructLayout(LayoutKind.Sequential)] struct VRect { public int Left, Top, Right, Bottom; }

        [ComImport, Guid("0000010d-0000-0000-C000-000000000046"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        interface IViewObject
        {
            [PreserveSig] int Draw(uint aspect, int index, IntPtr pvAspect, IntPtr ptd, IntPtr hdcTarget, IntPtr hdcDraw, ref VRect bounds, IntPtr wBounds, IntPtr cont, uint dwCont);
        }

        static int debugN;

        static bool Blank(Bitmap b)
        {
            for (int y = 10; y < b.Height; y += 7)
                for (int x = 10; x < b.Width; x += 7)
                    if (b.GetPixel(x, y).R < 200) return false;
            return true;
        }

        static void DrawBrowser(Form f, WebBrowser wb, Bitmap target)
        {
            DateTime end = DateTime.Now.AddSeconds(15);
            while ((wb.ReadyState != WebBrowserReadyState.Complete || wb.Document == null) && DateTime.Now < end) { Application.DoEvents(); System.Threading.Thread.Sleep(20); }
            Pump(300);
            if (wb.Document == null) return;
            IViewObject vo = wb.Document.DomDocument as IViewObject;
            if (vo == null) return;
            Rectangle r = R(f, wb);
            using (Bitmap page = new Bitmap(wb.Width, wb.Height, PixelFormat.Format24bppRgb))
            {
                using (Graphics g = Graphics.FromImage(page))
                {
                    g.Clear(Color.White);
                    IntPtr dc = g.GetHdc();
                    VRect rc = new VRect(); rc.Right = wb.Width; rc.Bottom = wb.Height;
                    try { vo.Draw(1, -1, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, dc, ref rc, IntPtr.Zero, IntPtr.Zero, 0); } finally { g.ReleaseHdc(dc); }
                }
                if (Blank(page)) { try { wb.DrawToBitmap(page, new Rectangle(0, 0, wb.Width, wb.Height)); } catch { } }
                Console.WriteLine("   (report page " + wb.Width + "x" + wb.Height + (Blank(page) ? ", still blank" : "") + ")");
                if (Environment.GetEnvironmentVariable("MANUAL_DEBUG") == "1")
                {
                    HtmlElement body = wb.Document.Body;
                    Console.WriteLine("   url=" + wb.Url + " state=" + wb.ReadyState + " text=" + (body == null ? -1 : (body.InnerText ?? "").Length) +
                        (body == null ? "" : " scroll=" + body.ScrollLeft + "," + body.ScrollTop + " size=" + body.ScrollRectangle));
                }
                if (Environment.GetEnvironmentVariable("MANUAL_DEBUG") == "1") page.Save(Path.Combine(outDir, "debug-page-" + (++debugN) + ".png"), ImageFormat.Png);
                using (Graphics g = Graphics.FromImage(target)) g.DrawImageUnscaled(page, r.Location);
            }
        }

        // ------------------------------------------------------------ callouts
        /// <summary>A numbered callout: a red frame around the area and the number in a red circle at one of its corners
        /// (0 top-right, 1 top-left, 2 bottom-left, 3 bottom-right, 4 middle-left, 5 middle-right; frame = false: number only).</summary>
        class Mark
        {
            public Rectangle R; public int N, Pos; public bool Frame = true;
            public Mark(Rectangle r, int n, int pos) { R = r; N = n; Pos = pos; }
        }

        static Mark M(Form f, Control c, int n) { return new Mark(R(f, c), n, 1); }
        static Mark M(Form f, Control c, int n, int pos) { return new Mark(R(f, c), n, pos); }
        static Mark M(Rectangle r, int n, int pos) { return new Mark(r, n, pos); }
        static Mark Num(Rectangle r, int n, int pos) { Mark m = new Mark(r, n, pos); m.Frame = false; return m; }

        static void Draw(Bitmap b, Mark[] marks)
        {
            using (Graphics g = Graphics.FromImage(b))
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;
                foreach (Mark m in marks)
                {
                    if (!m.Frame) continue;
                    Rectangle r = Rectangle.Inflate(m.R, 3, 3);
                    using (GraphicsPath p = Theme.Round(r, 6))
                    using (Pen pen = new Pen(Red, 3f)) g.DrawPath(pen, p);
                }
                foreach (Mark m in marks)
                {
                    Rectangle r = Rectangle.Inflate(m.R, 3, 3);
                    Point c;
                    switch (m.Pos)
                    {
                        case 0: c = new Point(r.Right, r.Top); break;
                        case 2: c = new Point(r.Left, r.Bottom); break;
                        case 3: c = new Point(r.Right, r.Bottom); break;
                        case 4: c = new Point(r.Left - 16, r.Top + r.Height / 2); break;
                        case 5: c = new Point(r.Right + 16, r.Top + r.Height / 2); break;
                        default: c = new Point(r.Left, r.Top); break;
                    }
                    Badge(g, visible.IsEmpty || visible.Width > b.Width ? new Rectangle(Point.Empty, b.Size) : visible, c, m.N);
                }
            }
        }

        static void Badge(Graphics g, Rectangle img, Point c, int n)
        {
            const int d = 30;
            c.X = Math.Max(img.Left + d / 2 + 1, Math.Min(img.Right - d / 2 - 2, c.X));
            c.Y = Math.Max(img.Top + d / 2 + 1, Math.Min(img.Bottom - d / 2 - 2, c.Y));
            Rectangle e = new Rectangle(c.X - d / 2, c.Y - d / 2, d, d);
            using (SolidBrush sh = new SolidBrush(Color.FromArgb(70, 0, 0, 0))) g.FillEllipse(sh, new Rectangle(e.X + 1, e.Y + 2, d, d));
            using (SolidBrush br = new SolidBrush(Red)) g.FillEllipse(br, e);
            using (Pen w = new Pen(Color.White, 2.2f)) g.DrawEllipse(w, e);
            using (Font f = new Font("Segoe UI", 12.5f, FontStyle.Bold, GraphicsUnit.Pixel))
            using (Font f2 = new Font("Segoe UI", 15f, FontStyle.Bold, GraphicsUnit.Pixel))
            using (StringFormat sf = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center })
                g.DrawString(n.ToString(), n > 9 ? f : f2, Brushes.White, new RectangleF(e.X, e.Y + 1, e.Width, e.Height), sf);
        }

        static void Arrow(Bitmap b, Point from, Point to)
        {
            using (Graphics g = Graphics.FromImage(b))
            using (Pen p = new Pen(Red, 4f))
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                p.CustomEndCap = new AdjustableArrowCap(4.5f, 4.5f, true);
                g.DrawLine(p, from, to);
            }
        }

        /// <summary>Crops, draws the callouts and saves img/&lt;name&gt;.png.</summary>
        static void Save(Bitmap full, string name, Rectangle crop, params Mark[] marks)
        {
            Draw(full, marks);
            if (crop.IsEmpty) crop = new Rectangle(Point.Empty, full.Size);
            crop.Intersect(new Rectangle(Point.Empty, full.Size));
            if (!visible.IsEmpty && visible.Width <= full.Width && visible.Height <= full.Height) crop.Intersect(visible);
            string path = Path.Combine(outDir, name + ".png");
            using (Bitmap c = full.Clone(crop, PixelFormat.Format24bppRgb)) c.Save(path, ImageFormat.Png);
            long len = new FileInfo(path).Length; total += len; count++;
            Console.WriteLine(name + ".png  " + crop.Width + "x" + crop.Height + "  " + (len / 1024) + " KB");
        }

        /// <summary>The main window without the window frame (title bar kept: users recognise the program by it).</summary>
        static Rectangle Body(Form f) { return new Rectangle(0, 0, f.Width, f.Height); }

        /// <summary>Area of the main window from the top of the content to the bottom (no side menu).</summary>
        static Rectangle ContentArea()
        {
            Control content = (Control)Field(main, "content");
            Rectangle r = R(main, content);
            return new Rectangle(0, r.Top - 8, r.Right + 6, r.Height + 8);
        }

        static Control Current() { return (Control)Field(main, "current"); }

        static MainForm NewMain(bool advanced)
        {
            Session.SetMode(advanced);
            MainForm m = new MainForm();
            m.WindowState = FormWindowState.Normal;
            Show(m, Win);
            Pump(800);
            return m;
        }

        static void Close(Form f) { if (f == null) return; f.Hide(); f.Dispose(); Pump(100); }

        // ================================================================== sample data
        static int store1, store2, store3, typeBreakers, typeCables, projA, projB, projC, projD;
        static int ItemByCode(string code) { return Db.I(Db.Scalar("SELECT item_no FROM item WHERE item_code = ?", code)); }

        static void Prepare()
        {
            Branding.SetCompany(Company);
            Db.Exec("UPDATE pass SET user_name = ? WHERE pass_no = 1", Admin);
            Db.Exec("UPDATE item SET unit = ?", "قطعة");
            Db.Exec("UPDATE item SET unit = ? WHERE item_code LIKE 'CU-%' OR item_code LIKE 'CTRL-%'", "متر");
            Db.Exec("UPDATE item SET unit = ? WHERE item_code = 'TIE-30'", "كيس");
            Db.Exec("UPDATE item SET unit = ? WHERE item_code = 'TAPE-PVC'", "لفة");

            store1 = Db.I(Db.Scalar("SELECT MIN(stor_no) FROM stor"));
            store2 = Db.I(Db.Scalar("SELECT stor_no FROM stor WHERE stor_name = ?", "مخزن الموقع"));
            store3 = Db.I(Db.Scalar("SELECT stor_no FROM stor WHERE stor_name = ?", "مخزن الكابلات"));
            typeBreakers = Db.I(Db.Scalar("SELECT type_no FROM [type] WHERE type_name = ?", "قواطع"));
            typeCables = Db.I(Db.Scalar("SELECT type_no FROM [type] WHERE type_name = ?", "كابلات"));
            DataTable pj = Db.Query("SELECT proj_no FROM project ORDER BY proj_no");
            projA = Db.I(pj.Rows[0][0]); projB = Db.I(pj.Rows[1][0]); projC = Db.I(pj.Rows[2][0]); projD = Db.I(pj.Rows[3][0]);
            // the 2nd demo centre is a department (cost centre types)
            Db.Exec("UPDATE project SET cc_type = 2 WHERE proj_no = ?", projC);

            // shelf locations, minimum / maximum and lead time in the main store
            DataTable rows = Db.Query("SELECT s.item_no, i.item_code FROM item_stor AS s INNER JOIN item AS i ON i.item_no = s.item_no WHERE s.stor = ? ORDER BY i.item_code", store1);
            int k = 0;
            foreach (DataRow r in rows.Rows)
            {
                k++;
                string code = Db.S(r[1]);
                bool cable = code.StartsWith("CU-") || code.StartsWith("CTRL-");
                string bin = (cable ? "C" : code.StartsWith("MCB") || code.StartsWith("MCCB") || code.StartsWith("RCCB") ? "A" : "B") + "-" + (1 + k % 6).ToString("00") + "-" + (1 + k % 4);
                double min = cable ? 200 : 10 + (k % 4) * 5, max = cable ? 1000 : min * 4;
                Db.Exec("UPDATE item_stor SET bin = ?, min_qty = ?, max_qty = ? WHERE item_no = ? AND stor = ?", bin, min, max, Db.I(r[0]), store1);
            }
            // a few items below their minimum: the critical balances alert and report have something to show
            foreach (string code in new string[] { "LED-F200", "DB-24", "SK-IND32", "RCCB-4P63", "CT-65", "LED-EM", "CU-4X35" })
                Db.Exec("UPDATE item_stor SET min_qty = item_bal + 15, max_qty = item_bal + 60 WHERE stor = ? AND item_no = ?", store1, ItemByCode(code));
            Db.Exec("UPDATE item SET lead_days = 14");
            Db.Exec("UPDATE item SET lead_days = 30 WHERE item_code LIKE 'MCCB%' OR item_code LIKE 'CU-4X%'");
            Cache.LoadAll();

            // material requisitions: one pending, one approved, one draft, one partly issued
            if (Requisitions.Ready)
            {
                Requisitions.SetApprovalLimit(0);
                string me = Session.UserName;
                Session.UserName = "مشرف المشاريع";
                int r1 = NewMreq(projA, "م. أحمد علي حسن", "للطابق الثاني", new object[] { "MCB-1P16", 20.0, "SW-1G", 30.0, "CU-1X2.5", 300.0 });
                Requisitions.Send(r1);
                int r2 = NewMreq(projD, "م. عمر خالد سعيد", "لوحة المضخة رقم 2", new object[] { "CT-25", 4.0, "OL-0913", 4.0, "CU-4X16", 60.0 });
                Requisitions.Send(r2);
                int r3 = NewMreq(projB, "م. علي حسين عباس", "", new object[] { "LED-P40", 24.0, "LED-EM", 6.0 });
                Requisitions.Send(r3);
                NewMreq(projC, "قسم الصيانة", "مسودة", new object[] { "TAPE-PVC", 10.0, "TIE-30", 2.0 });
                Session.UserName = me;
                Requisitions.Approve(r2);
            }

            // purchase requests: one from the critical balances, one sent to a supplier
            if (Db.HasPreq)
            {
                int skipped;
                List<Reports.CriticalRow> crit = Reports.CriticalItems(store1, 0, 6);
                if (crit.Count > 3) crit.RemoveRange(3, crit.Count - 3);   // the report still shows the others
                PurchaseRequests.CreateFromCritical(crit, store1, false, out skipped);
                PReq p = new PReq();
                p.Store = store1; p.Date = DateTime.Today.AddDays(-9);
                p.Supplier = Db.I(Db.Scalar("SELECT MIN(p_no) FROM person WHERE p_type = 2"));
                p.Note = "عرض سعر رقم 77";
                foreach (object[] l in new object[][] { new object[] { "MCCB-250", 2.0 }, new object[] { "DB-24", 3.0 } })
                    p.Lines.Add(new PReqLine { ItemNo = ItemByCode((string)l[0]), Qty = (double)l[1] });
                int id = PurchaseRequests.Save(p);
                PurchaseRequests.SetStatus(id, PurchaseRequests.Sent);
            }

            // a physical count of the breakers in the main store, partly counted
            int cid = StockCount.Create(DateTime.Today, store1, typeBreakers, "لجنة الجرد: أمين المخزن + المحاسب", false);
            CountDoc cd = StockCount.Load(cid);
            int n = 0;
            foreach (CountLine l in cd.Lines)
            {
                if (n++ >= cd.Lines.Count - 2) break;
                l.Counted = n == 3 ? l.Book - 2 : n == 5 ? l.Book + 1 : l.Book;
                l.Dirty = true;
            }
            StockCount.SaveCounts(cd);
            Cache.LoadAll();

            // the audit trail: two issues corrected after saving (quantity and note), values before / after
            DataTable last = Db.Query("SELECT TOP 2 shop_no FROM sale WHERE " + Db.IssueOnly("") + " ORDER BY shop_no DESC");
            foreach (DataRow r in last.Rows)
            {
                Doc d = Posting.Load(DocKind.Issue, Db.I(r[0]));
                d.Lines[0].Qty = Math.Max(1, d.Lines[0].Qty - 1);
                d.Note = "تصحيح الكمية بعد مراجعة المستلم";
                Posting.Save(d);
            }

            // write-offs: prepared by the storekeeper; two approved (posted), one waiting, one rejected, one draft
            if (WriteOffs.Ready)
            {
                string me = Session.UserName;
                Session.UserName = "أمين المخزن";
                int w1 = NewWriteOff(WriteOffs.Damaged, -12, "كسر أثناء التفريغ", new object[] { "LED-P40", 2.0, "SW-1G", 5.0 });
                int w2 = NewWriteOff(WriteOffs.Obsolete, -8, "موديل قديم لا يُستعمل", new object[] { "FL-36", 0.0, "SK-13A", 3.0 });
                int w3 = NewWriteOff(WriteOffs.Expired, -2, "شريط فقد لاصقيته", new object[] { "TAPE-PVC", 12.0, "TIE-30", 1.0 });
                int w4 = NewWriteOff(WriteOffs.Lost, -1, "لم يُعثر عليها في الجرد", new object[] { "MCB-1P10", 3.0 });
                NewWriteOff(WriteOffs.Damaged, 0, "رطوبة في المخزن", new object[] { "PVC-25", 6.0 });
                foreach (int w in new int[] { w1, w2, w3, w4 }) if (w > 0) WriteOffs.Send(w);
                Session.UserName = me;
                if (w1 > 0) WriteOffs.Approve(w1);
                if (w2 > 0) WriteOffs.Approve(w2);
                if (w4 > 0) WriteOffs.Reject(w4, "اذكر رقم الجرد الذي ظهر فيه النقص");
            }
            Cache.LoadAll();

            // the monthly period lock: closed up to the end of the month before last
            DateTime lk = DateTime.Today.AddMonths(-2);
            PeriodLock.LockThrough(lk.Year, lk.Month);
        }

        static int NewWriteOff(int reason, int days, string note, object[] lines)
        {
            WoDoc d = new WoDoc(); d.Store = store1; d.Reason = reason; d.Notes = note; d.Date = DateTime.Today.AddDays(days);
            for (int i = 0; i < lines.Length; i += 2)
            {
                int it = ItemByCode((string)lines[i]); double q = (double)lines[i + 1];
                if (it == 0 || q <= 0 || Cache.Stock(it, store1) < q) continue;   // only what the demo store has
                d.Lines.Add(new WoLine { ItemNo = it, Qty = q });
            }
            return d.Lines.Count == 0 ? 0 : WriteOffs.Save(d);
        }

        static int NewMreq(int proj, string by, string note, object[] lines)
        {
            MreqDoc d = new MreqDoc(); d.Project = proj; d.Store = store1; d.RequestedBy = by; d.Notes = note; d.Date = DateTime.Today.AddDays(-1);
            for (int i = 0; i < lines.Length; i += 2) d.Lines.Add(new MreqLine { ItemNo = ItemByCode((string)lines[i]), Qty = (double)lines[i + 1] });
            return Requisitions.Save(d);
        }

        static void NeutralLog()
        {
            Db.Exec("UPDATE app_log SET computer = ?", FakePc);
            Db.Exec("UPDATE app_log SET user_name = ? WHERE user_name = '1'", Admin);
        }

        // ================================================================== main
        [STAThread]
        static int Main(string[] args)
        {
            Console.OutputEncoding = Encoding.UTF8;
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            if (args.Length < 2) { Console.WriteLine("usage: ManualShots <copy of demo.mdb> <out dir> [only=a,b]"); return 2; }
            dataPath = Path.GetFullPath(args[0]); outDir = Path.GetFullPath(args[1]);
            for (int i = 2; i < args.Length; i++) if (args[i].StartsWith("only=")) only = new List<string>(args[i].Substring(5).Split(','));
            Directory.CreateDirectory(outDir);
            if (File.Exists(Path.Combine(Settings.AppDir, "logo.png"))) { Console.WriteLine("a logo.png next to the tool would be shown: remove it"); return 1; }
            string err = Db.InitAccess(dataPath); if (err != null) { Console.WriteLine(err); return 1; }
            Db.EnsureAppTables();
            // fake data only: the file must come from tests\DemoData (its persons say «بيانات تجريبية»)
            if (Db.I(Db.Scalar("SELECT COUNT(*) FROM person WHERE p_note = ?", "بيانات تجريبية")) < 5 || Db.I(Db.Scalar("SELECT COUNT(*) FROM item WHERE item_code = 'MCB-1P6'")) == 0)
            { Console.WriteLine("this is not a DemoData file: the manual must never show real data"); return 1; }
            Settings.Set("DataPath", FakeData); Settings.Mode = "access";   // in memory only (never saved): the connection window shows it
            Session.UserNo = 1; Session.UserName = Admin; Session.LoadPerms();
            Theme.Use(Palette.Make(false, "blue"));
            Timer guard = new Timer(); guard.Interval = 700; guard.Tick += delegate { CloseMessageBoxes(); }; guard.Start();
            Prepare();
            Branding.Load();
            NeutralLog();
            try
            {
                Start();
                Home();
                Items();
                CostCentres();
                Documents();
                RequisitionShots();
                PurchaseShots();
                CountShots();
                ReportShots();
                SettingsShots();
                LanguageShots();
                ValuationShots();
                WriteOffShots();
                LockShots();
                DarkShots();
            }
            catch (Exception ex) { Console.WriteLine("FAILED: " + ex); return 1; }
            Console.WriteLine(count + " images, " + (total / 1024) + " KB");
            Session.SetMode(false);
            Db.SignOff();
            Environment.Exit(0);
            return 0;
        }

        // ================================================================== 1. first start
        static void Start()
        {
            if (Want("connection"))
            {
                ConnectionForm f = new ConnectionForm(); Show(f, Size.Empty);
                Bitmap b = Grab(f);
                Control access = Starts(f, "ملف Access"), create = Starts(f, "لا توجد لدي بيانات"), browse = Btn(f, "استعراض…");
                Control sql = Starts(f, "سيرفر SQL Server"), main1 = Btn(f, "السيرفر الرئيسي"), sec = Btn(f, "سيرفر ثانوي"), ok = Btn(f, "حفظ واتصال");
                TextBox path = Find<TextBox>(f, delegate(TextBox t) { return t.Text == FakeData; });
                Save(b, "connection", Rectangle.Empty,
                    M(f, access, 1, 0), M(U(R(f, path), R(f, browse)), 2, 1), M(f, create, 3, 1),
                    M(f, sql, 4, 0), M(U(R(f, main1), R(f, sec)), 5, 0), M(f, ok, 6, 1));
                Close(f);
            }
            if (Want("login"))
            {
                LoginForm f = new LoginForm(); Show(f, Size.Empty);
                ComboBox user = Find<ComboBox>(f, null); user.Text = Admin;
                TextBox pass = Find<TextBox>(f, null); pass.Text = "1234";
                Bitmap b = Grab(f);
                Save(b, "login", Rectangle.Empty, M(f, user, 1, 0), M(f, pass, 2, 0), M(f, Btn(f, "دخول"), 3, 0), M(f, Btn(f, "إعدادات الاتصال والسيرفر"), 4, 0));
                Close(f);
            }
        }

        // ================================================================== 2. home screen
        static void Home()
        {
            main = NewMain(false);
            Control dash = Current();
            Control side = (Control)Field(main, "side");
            if (Want("home"))
            {
                Bitmap b = Grab(main);
                List<ActionTile> tiles = All<ActionTile>(dash, null);
                Rectangle tr = R(main, tiles[0]); foreach (ActionTile t in tiles) tr = U(tr, R(main, t));
                List<AlertChip> chips = All<AlertChip>(dash, null);
                Rectangle ar = R(main, chips[0]); foreach (AlertChip c in chips) ar = U(ar, R(main, c));
                DataGridView grid = Find<DataGridView>(dash, null);
                Control user = Starts(main, Admin), mode = Starts(main, "الوضع:"), toggle = Btn(main, "طي القائمة");
                Control status = (Control)Field(main, "lblOnline");
                Rectangle sr = R(main, side); sr = new Rectangle(sr.X, R(main, toggle).Bottom + 2, sr.Width, sr.Height - (R(main, toggle).Bottom + 2 - sr.Y));
                Save(b, "home", Rectangle.Empty,
                    M(sr, 1, 4), M(main, Find<SearchBox>(dash, null), 2, 0), M(tr, 3, 0), M(ar, 4, 0), M(R(main, grid.Parent), 5, 0),
                    M(main, user, 6, 3), M(main, mode, 7, 3), M(main, toggle, 8, 1), M(main, status, 9, 0));
            }
            if (Want("user-menu"))
            {
                // the user menu is a pop-up (it cannot be captured off-screen): drawn here the way it looks
                Bitmap b = Grab(main);
                Control user = Starts(main, Admin);
                Rectangle u = R(main, user);
                using (Graphics g = Graphics.FromImage(b))
                {
                    g.SmoothingMode = SmoothingMode.AntiAlias;
                    Rectangle m = new Rectangle(Math.Max(12, u.Right - 190), u.Bottom + 4, 190, 68);   // a window at the screen's left edge: the menu opens to the right
                    using (SolidBrush sh = new SolidBrush(Color.FromArgb(40, 0, 0, 0))) g.FillRectangle(sh, new Rectangle(m.X + 3, m.Y + 3, m.Width, m.Height));
                    g.FillRectangle(Brushes.White, m); g.DrawRectangle(Pens.Silver, m);
                    TextFormatFlags tf = TextFormatFlags.Right | TextFormatFlags.VerticalCenter | TextFormatFlags.RightToLeft;
                    TextRenderer.DrawText(g, "تغيير كلمة المرور", Theme.Font, new Rectangle(m.X + 8, m.Y + 4, m.Width - 22, 30), Color.Black, tf);
                    TextRenderer.DrawText(g, "تسجيل الخروج", Theme.Font, new Rectangle(m.X + 8, m.Y + 34, m.Width - 22, 30), Color.Black, tf);
                    Draw(b, new Mark[] { M(new Rectangle(m.X + 4, m.Y + 4, m.Width - 8, 28), 1, 5), M(new Rectangle(m.X + 4, m.Y + 36, m.Width - 8, 28), 2, 5) });
                }
                Save(b, "user-menu", new Rectangle(0, 30, 420, 150));
            }
            if (Want("tiles-drag"))
            {
                Bitmap b = Grab(main);
                List<ActionTile> tiles = All<ActionTile>(dash, null);
                Rectangle a = R(main, tiles[0]), z = R(main, tiles[2]);
                Rectangle all = a; foreach (ActionTile t in tiles) all = U(all, R(main, t));
                Draw(b, new Mark[] { M(a, 1, 0), Num(z, 2, 1) });
                Arrow(b, new Point(a.Left + 40, a.Bottom - 9), new Point(z.Left + z.Width / 2, z.Bottom - 9));
                Save(b, "tiles-drag", Rectangle.Inflate(all, 14, 40));
            }
            if (Want("quick-search"))
            {
                ItemSearchDialog d = new ItemSearchDialog("كيبل", false); Show(d, Size.Empty);
                Pump(400);
                Bitmap b = Grab(d);
                Save(b, "quick-search", Rectangle.Empty);
                Close(d);
            }
            if (Want("nav-collapsed"))
            {
                Db.SetSetting("nav_collapsed_user1", "1");
                Close(main); main = NewMain(false);
                Bitmap b = Grab(main);
                Control s2 = (Control)Field(main, "side");
                Rectangle sr = R(main, s2);
                Control toggle = Btn(main, "توسيع القائمة");
                Save(b, "nav-collapsed", new Rectangle(sr.Left - 330, 30, sr.Width + 330, sr.Bottom - 30), M(main, toggle, 1, 4));
                Db.SetSetting("nav_collapsed_user1", "0");
            }
            Close(main);
            if (Want("home-advanced"))
            {
                main = NewMain(true);
                Control d2 = Current();
                Bitmap b = Grab(main);
                Control mode = Starts(main, "الوضع:");
                Control chart = Find<MonthChart>(d2, null);
                Control tr = Btn(main, "نقل مخزني"), us = Btn(main, "المستخدمون");
                Save(b, "home-advanced", Rectangle.Empty, M(main, mode, 1, 3), M(U(R(main, tr), R(main, us)), 2, 4), M(R(main, chart.Parent), 3, 0));
                Close(main);
            }
        }

        // ================================================================== 3. items
        static void Items()
        {
            main = NewMain(false);
            if (Want("items"))
            {
                main.Show(new ItemsView(main)); Pump(700);
                Control v = Current();
                DataGridView g = Find<DataGridView>(v, null);
                g.ClearSelection(); g.Rows[3].Selected = true; g.CurrentCell = g.Rows[3].Cells[1];
                Bitmap b = Grab(main);
                TextBox txt = Find<TextBox>(v, null);
                Rectangle row = g.GetRowDisplayRectangle(3, false); Rectangle gr = R(main, g); row.Offset(gr.Location);
                Save(b, "items", ContentArea(),
                    M(main, txt, 1, 0), M(main, Find<ComboBox>(v, null), 2, 0), M(main, Btn(v, "+ مادة جديدة"), 3, 1), M(main, Btn(v, "تعديل"), 4, 1),
                    M(main, Btn(v, "كارت المادة"), 5, 1), M(row, 6, 0), M(main, Btn(v, "تصدير Excel"), 7, 1));
            }
            if (Want("item-dialog"))
            {
                ItemDialog d = new ItemDialog(ItemByCode("MCB-1P16")); Show(d, Size.Empty);
                Pump(300);
                DataGridView g = Find<DataGridView>(d, null);
                Rectangle gr = R(d, g);
                Func<string, Rectangle> col = delegate(string cn)
                {
                    Rectangle c = g.GetColumnDisplayRectangle(g.Columns[cn].Index, false);
                    c.Offset(gr.Location); c.Height = g.ColumnHeadersHeight + g.Rows[0].Height * 3; return c;
                };
                Rectangle bins = U(col("bin"), U(col("min"), col("max")));
                TableLayoutPanel t = Find<TableLayoutPanel>(d, null);
                Control name = t.GetControlFromPosition(1, 0), code = t.GetControlFromPosition(1, 1), unit = t.GetControlFromPosition(1, 4),
                        ret = t.GetControlFromPosition(1, 5), lead = t.GetControlFromPosition(1, 6);
                Bitmap b = Grab(d);
                Save(b, "item-dialog", Rectangle.Empty,
                    M(d, name, 1, 1), M(d, code, 2, 1), M(d, unit, 3, 1), M(R(d, ret), 4, 1), M(d, lead, 5, 1),
                    M(col("open"), 6, 0), M(bins, 7, 1), M(d, Btn(d, "حفظ"), 8, 1));
                Close(d);
            }
            Close(main);
        }

        // ================================================================== 4. cost centres
        static void CostCentres()
        {
            main = NewMain(false);
            if (Want("cost-centres"))
            {
                main.Show(new ProjectsView()); Pump(700);
                Control v = Current();
                Bitmap b = Grab(main);
                Save(b, "cost-centres", ContentArea(),
                    M(main, Btn(v, "+ مركز كلفة جديد"), 1, 1), M(main, Btn(v, "تعديل"), 2, 1), M(main, Btn(v, "إغلاق"), 3, 1),
                    M(main, Btn(v, "كشف مركز الكلفة"), 4, 1), M(main, Btn(v, "حجز مواد"), 5, 1));
            }
            if (Want("cost-centre-dialog"))
            {
                ProjectDialog d = new ProjectDialog(0); Show(d, Size.Empty);
                TableLayoutPanel t = Find<TableLayoutPanel>(d, null);
                Control name = t.GetControlFromPosition(1, 0), type = t.GetControlFromPosition(1, 1);
                name.Text = "صيانة المولدات";
                ComboBox cb = type as ComboBox; if (cb != null) cb.SelectedIndex = 2;
                Pump(200);
                Bitmap b = Grab(d);
                Save(b, "cost-centre-dialog", Rectangle.Empty, M(d, name, 1, 1), M(d, type, 2, 1), M(d, Btn(d, "حفظ"), 3, 1));
                Close(d);
            }
            Close(main);
        }

        // ================================================================== 5. documents
        static DocView OpenNew(DocKind k)
        {
            main.Show(new UserControl());   // the previous (unsaved) sample document is dropped without the «save changes?» question
            main.OpenDoc(k, 0); Pump(700);
            return (DocView)Current();
        }

        static void Add(DocView v, string code, double qty)
        {
            ItemEntryBar bar = (ItemEntryBar)Field(v, "entry");
            bar.AddLine(ItemByCode(code), qty, 0);
            Pump(100);
        }

        static void Documents()
        {
            main = NewMain(false);
            // ---- receipt
            if (Want("receipt"))
            {
                DocView v = OpenNew(DocKind.Receipt);
                PersonPicker person = (PersonPicker)Field(v, "person");
                person.Value = Db.I(Db.Scalar("SELECT MIN(p_no) FROM person WHERE p_type = 2"));
                ItemEntryBar bar = (ItemEntryBar)Field(v, "entry");
                bar.AddLine(ItemByCode("MCB-1P16"), 50, 3800); bar.AddLine(ItemByCode("CU-1X2.5"), 500, 900); bar.AddLine(ItemByCode("SK-13A"), 40, 3000);
                Pump(200);
                Bitmap b = Grab(main);
                Save(b, "receipt", ContentArea(),
                    M(main, person, 1, 0), M(main, (Control)Field(v, "cmbStore"), 2, 0), M(main, (Control)Field(v, "cmbRType"), 3, 0),
                    M(main, bar.Search.Parent, 4, 0), M(main, Btn(v, "حفظ"), 5, 1));
            }
            // ---- issue, step by step
            DocView iv = null;
            if (Want("issue-1"))
            {
                iv = OpenNew(DocKind.Issue);
                ProjectPicker pp = (ProjectPicker)Field(iv, "project");
                pp.OpenList(true); Pump(300);
                Bitmap b = Grab(main);
                Save(b, "issue-1", ContentArea(), M(main, pp, 1, 0), M(main, (Control)Field(iv, "cmbStore"), 2, 0));
            }
            if (Want("issue-2") || Want("issue-3") || Want("issue-4"))
            {
                iv = OpenNew(DocKind.Issue);
                ProjectPicker pp = (ProjectPicker)Field(iv, "project"); pp.Value = projA;
                ItemEntryBar bar = (ItemEntryBar)Field(iv, "entry");
                bar.Search.Text = "قاطع"; Pump(400);
                Bitmap b = Grab(main);
                Save(b, "issue-2", ContentArea(), M(main, bar.Search.Parent, 1, 0), M(main, bar.DropDown, 2, 1));
                bar.Pick(ItemByCode("MCB-1P16")); bar.Qty.Text = "12"; ((Control)Field(bar, "qtyFrame")).BackColor = Theme.Primary; Pump(200);
                b = Grab(main);
                Save(b, "issue-3", ContentArea(), M(main, bar.Search.Parent, 1, 0), M(main, bar.Qty.Parent, 2, 1), M(main, Btn(iv, "+ إضافة"), 3, 1),
                    M(main, (Control)Field(bar, "lblStatus"), 4, 1));
                bar.AddLine(ItemByCode("MCB-1P16"), 12, 0); bar.Reset();
                Add(iv, "SW-1G", 25); Add(iv, "CU-1X2.5", 150); Add(iv, "TAPE-PVC", 6);
                Pump(300);
                DataGridView g = (DataGridView)Field(iv, "grid");
                Rectangle gr = R(main, g);
                Rectangle del = g.GetCellDisplayRectangle(g.Columns["del"].Index, 1, false); del.Offset(gr.Location);
                Rectangle qty = g.GetCellDisplayRectangle(g.Columns["qty"].Index, 2, false); qty.Offset(gr.Location);
                Rectangle av = g.GetCellDisplayRectangle(g.Columns["avail"].Index, 0, false); av.Offset(gr.Location); av.Height *= 4;
                b = Grab(main);
                Save(b, "issue-4", ContentArea(), M(qty, 1, 0), M(av, 2, 0), M(del, 3, 1), M(main, (Control)Field(iv, "lblTotals"), 4, 0), M(main, Btn(iv, "حفظ"), 5, 1),
                    M(main, Btn(iv, "طباعة"), 6, 1));
            }
            Close(main);
            // ---- action bar in advanced mode (shortcuts on the buttons)
            main = NewMain(true);
            int lastIssue = Db.I(Db.Scalar("SELECT MAX(shop_no) FROM sale WHERE " + Db.IssueOnly("")));
            if (Want("issue-actions"))
            {
                main.OpenDoc(DocKind.Issue, lastIssue); Pump(700);
                DocView v = (DocView)Current();
                Bitmap b = Grab(main);
                Control save = Starts(v, "حفظ"), nw = Starts(v, "جديد"), pr = Starts(v, "طباعة"), del = Btn(v, "حذف السند");
                Control prev = Starts(v, "► السابق"), next = Starts(v, "التالي"), find = Btn(v, "فتح سند برقم…");
                Rectangle bar = U(R(main, save), R(main, next)); bar = U(bar, R(main, Find<DataGridView>(v, null)));
                Save(b, "issue-actions", new Rectangle(0, 30, main.Width, R(main, (Control)Field(v, "lblTitle")).Bottom + 20 - 30),
                    M(main, save, 1, 0), M(main, nw, 2, 0), M(main, pr, 3, 0), M(main, del, 4, 0), M(main, find, 5, 1), M(U(R(main, prev), R(main, next)), 6, 1));
            }
            if (Want("voucher"))
            {
                ReportViewerForm f = new ReportViewerForm(Reports.Voucher(Posting.Load(DocKind.Issue, lastIssue)));
                Show(f, new Size(1000, 720)); Pump(1500);
                Bitmap b = Grab(f);
                Save(b, "voucher", Rectangle.Empty, M(f, Starts(f, "طباعة"), 1, 0), M(f, Btn(f, "معاينة"), 2, 0), M(f, Btn(f, "إعداد الصفحة"), 3, 0), M(U(R(f, Starts(f, "تكبير")), R(f, Starts(f, "تصغير"))), 4, 0));
                Close(f);
            }
            if (Want("return"))
            {
                DocView v = OpenNew(DocKind.Return);
                ProjectPicker pp = (ProjectPicker)Field(v, "project"); pp.Value = projA;
                Add(v, "CU-1X2.5", 40); Add(v, "SW-1G", 5);
                ((TextBox)Field(v, "txtNote")).Text = "فائض من الموقع";
                Bitmap b = Grab(main);
                Save(b, "return", ContentArea(), M(main, pp, 1, 0), M(main, (Control)Field(v, "cmbStore"), 2, 0), M(main, Starts(v, "حفظ"), 3, 1));
            }
            if (Want("transfer"))
            {
                DocView v = OpenNew(DocKind.Transfer);
                ComboBox s2 = (ComboBox)Field(v, "cmbStore2"); s2.SelectedValue = store2;
                Add(v, "CU-1X2.5", 100); Add(v, "MCB-1P16", 10);
                ((TextBox)Field(v, "txtNote")).Text = "تجهيز مخزن الموقع";
                Bitmap b = Grab(main);
                Save(b, "transfer", ContentArea(), M(main, (Control)Field(v, "cmbStore"), 1, 0), M(main, s2, 2, 0));
            }
            if (Want("reserve"))
            {
                DocView v = OpenNew(DocKind.Reserve);
                ProjectPicker pp = (ProjectPicker)Field(v, "project"); pp.Value = projB;
                Add(v, "LED-P40", 30); Add(v, "DB-12", 2);
                Bitmap b = Grab(main);
                Save(b, "reserve", ContentArea(), M(main, pp, 1, 0), M(main, Btn(v, "إلغاء الحجز"), 2, 1));
            }
            if (Want("doc-list"))
            {
                main.Show(new DocListView(main)); Pump(1000);
                Control v = Current();
                Bitmap b = Grab(main);
                Save(b, "doc-list", ContentArea(), M(main, Find<ComboBox>(v, null), 1, 0), M(main, Btn(v, "عرض"), 2, 1));
            }
            Close(main);
        }

        // ================================================================== 6. requisitions
        static void RequisitionShots()
        {
            if (!Requisitions.Ready) { Console.WriteLine("requisitions not ready"); return; }
            main = NewMain(false);
            if (Want("mreq-list"))
            {
                main.Show(new RequisitionsView(main, 2 /*all*/)); Pump(800);
                Control v = Current();
                ComboBox st = Find<ComboBox>(v, null);
                for (int i = 0; i < st.Items.Count; i++) if (st.Items[i].ToString() == "كل الطلبات") st.SelectedIndex = i;
                Call(v, "Reload"); Pump(400);
                Bitmap b = Grab(main);
                Save(b, "mreq-list", ContentArea(), M(main, Btn(v, "+ طلب جديد"), 1, 1), M(main, Btn(v, "موافقة"), 2, 1), M(main, Btn(v, "رفض"), 3, 1), M(main, st, 4, 1));
            }
            DataTable t = Db.Query("SELECT mreq_no, status FROM mreq ORDER BY mreq_no");
            int pending = 0, approved = 0;
            foreach (DataRow r in t.Rows) { if (Db.I(r[1]) == Requisitions.Pending && pending == 0) pending = Db.I(r[0]); if (Db.I(r[1]) == Requisitions.Approved) approved = Db.I(r[0]); }
            if (Want("mreq-new"))
            {
                RequisitionView v = new RequisitionView(main, 0); main.Show(v); Pump(600);
                ((ProjectPicker)Field(v, "project")).Value = projA;
                v.AddLine(ItemByCode("PVC-20"), 40); v.AddLine(ItemByCode("JB-1010"), 25);
                ((TextBox)Field(v, "txtBy")).Text = "م. أحمد علي حسن";
                Pump(200);
                Bitmap b = Grab(main);
                Save(b, "mreq-new", ContentArea(), M(main, (Control)Field(v, "project"), 1, 0), M(main, (Control)Field(v, "item"), 2, 0),
                    M(main, (Control)Field(v, "btnSave"), 3, 1), M(main, (Control)Field(v, "btnSend"), 4, 1));
            }
            if (Want("mreq-approve") && pending > 0)
            {
                RequisitionView v = new RequisitionView(main, pending); main.Show(v); Pump(700);
                Bitmap b = Grab(main);
                Save(b, "mreq-approve", ContentArea(), M(main, (Control)Field(v, "btnApprove"), 1, 1), M(main, (Control)Field(v, "btnReject"), 2, 1), M(main, (Control)Field(v, "lblState"), 3, 0));
            }
            if (Want("mreq-issue") && approved > 0)
            {
                RequisitionView v = new RequisitionView(main, approved); main.Show(v); Pump(700);
                Bitmap b = Grab(main);
                Save(b, "mreq-issue", ContentArea(), M(main, (Control)Field(v, "btnIssue"), 1, 1));
            }
            Close(main);
        }

        // ================================================================== 7. purchase requests
        static ReportsWindow NewReports(string key, Size size)
        {
            ConstructorInfo ci = typeof(ReportsWindow).GetConstructor(BindingFlags.NonPublic | BindingFlags.Instance, null, new Type[] { typeof(Form) }, null);
            ReportsWindow w = (ReportsWindow)ci.Invoke(new object[] { main });
            ReportsView rv = new ReportsView(key);
            Call(w, "SetView", rv);
            Show(w, size);
            Pump(1000);
            // off-screen the first page (set while the window is created) can stay empty: the report is shown again
            Call(rv, "Run");
            Pump(2000);
            return w;
        }

        static void PurchaseShots()
        {
            main = NewMain(true);
            if (Want("preq-critical"))
            {
                ReportsWindow w = NewReports("critical", Win);
                Control rv = (Control)Field(w, "view");
                Bitmap b = Grab(w);
                Save(b, "preq-critical", Rectangle.Empty, M(w, Btn(rv, "الأرصدة الحرجة (تحتاج تعزيز)"), 1, 4), M(w, Btn(rv, "إنشاء طلب شراء"), 2, 1));
                Close(w);
            }
            if (Want("preq-list"))
            {
                main.Show(new PurchaseRequestsView()); Pump(800);
                Control v = Current();
                Bitmap b = Grab(main);
                Save(b, "preq-list", ContentArea(), M(main, Btn(v, "+ طلب شراء جديد"), 1, 1), M(main, Btn(v, "فتح"), 2, 1), M(main, Btn(v, "تقرير الطلبات المفتوحة"), 3, 1));
            }
            if (Want("preq-dialog"))
            {
                int id = Db.I(Db.Scalar("SELECT MIN(preq_no) FROM preq"));
                PurchaseRequestDialog d = new PurchaseRequestDialog(id); Show(d, Size.Empty); Pump(300);
                Bitmap b = Grab(d);
                Save(b, "preq-dialog", Rectangle.Empty, M(d, Btn(d, "+ إضافة"), 1, 1), M(d, Btn(d, "طباعة"), 2, 1), M(d, Btn(d, "تعليم كمُرسل"), 3, 1));
                Close(d);
            }
            Close(main);
        }

        // ================================================================== 8. physical count
        static void CountShots()
        {
            main = NewMain(true);
            if (Want("count-list"))
            {
                main.Show(new StockCountsView(main)); Pump(700);
                Control v = Current();
                Bitmap b = Grab(main);
                Save(b, "count-list", ContentArea(), M(main, Btn(v, "+ جرد جديد"), 1, 1));
            }
            if (Want("count-new"))
            {
                NewCountDialog d = new NewCountDialog(); Show(d, Size.Empty);
                Bitmap b = Grab(d);
                Save(b, "count-new", Rectangle.Empty, M(d, Btn(d, "إنشاء الجرد"), 1, 1));
                Close(d);
            }
            if (Want("count-sheet"))
            {
                int id = Db.I(Db.Scalar("SELECT MAX(count_no) FROM count_head"));
                StockCountView v = new StockCountView(main, id); main.Show(v); Pump(800);
                CheckBox book = (CheckBox)Field(v, "chkBook"); book.Checked = true; Pump(300);
                DataGridView g = (DataGridView)Field(v, "grid");
                Rectangle gr = R(main, g);
                Rectangle cnt = g.GetColumnDisplayRectangle(g.Columns["counted"].Index, false); cnt.Offset(gr.Location); cnt.Height = Math.Min(gr.Height, g.ColumnHeadersHeight + g.Rows[0].Height * Math.Min(8, g.Rows.Count));
                Bitmap b = Grab(main);
                Save(b, "count-sheet", ContentArea(),
                    M(main, (Control)Field(v, "btnSheet"), 1, 1), M(cnt, 2, 0), M(main, book, 3, 1), M(main, (Control)Field(v, "btnVar"), 4, 1), M(main, (Control)Field(v, "btnPost"), 5, 1));
            }
            Close(main);
        }

        // ================================================================== 9. reports
        static void ReportShots()
        {
            main = NewMain(true);
            if (Want("reports") || Want("report-search"))
            {
                ReportsWindow w = NewReports("stock", Win);
                Control rv = (Control)Field(w, "view");
                Bitmap b = Grab(w);
                Control list = Btn(rv, "جرد أرصدة المواد").Parent;
                if (Want("reports"))
                    Save(b, "reports", Rectangle.Empty,
                        M(w, list, 1, 4), M(w, (Control)Field(rv, "pars"), 2, 0), M(w, Btn(rv, "عرض التقرير"), 3, 1), M(w, Btn(rv, "طباعة"), 4, 1),
                        M(w, Btn(rv, "معاينة"), 5, 1), M(w, Btn(rv, "تصدير Excel"), 6, 1), M(w, Btn(rv, "ملء الشاشة"), 7, 1), M(w, Btn(rv, "إعداد الصفحة"), 8, 1),
                        M(U(R(w, Btn(rv, "+")), R(w, Btn(rv, "−"))), 9, 1));
                if (Want("report-search"))
                {
                    WebBrowser wb = (WebBrowser)Field(rv, "wb");
                    HtmlElement q = wb.Document.GetElementById("q");
                    if (q != null) { q.SetAttribute("value", "كيبل"); wb.Document.InvokeScript("filt"); }
                    Pump(600);
                    b = Grab(w);
                    Rectangle wr = R(w, wb);
                    List<Mark> marks = new List<Mark>();
                    if (q != null) marks.Add(M(Dom(q, wr), 1, 0));
                    HtmlElementCollection th = wb.Document.GetElementsByTagName("th");
                    if (th.Count > 0 && th[0].Parent != null) marks.Add(M(Dom(th[0].Parent, wr), 2, 1));
                    Save(b, "report-search", new Rectangle(wr.Left, wr.Top, wr.Width, wr.Height), marks.ToArray());
                }
                Close(w);
            }
            Close(main);
        }

        /// <summary>Rectangle of an element of a report page inside the captured window (the page is not scrolled).</summary>
        static Rectangle Dom(HtmlElement e, Rectangle browser)
        {
            Rectangle r = e.OffsetRectangle;
            for (HtmlElement p = e.OffsetParent; p != null; p = p.OffsetParent) r.Offset(p.OffsetRectangle.Location);
            r.Offset(browser.Location);
            return r;
        }

        // ================================================================== 10. settings, users, year closing, updates
        static void SettingsShots()
        {
            NeutralLog();
            main = NewMain(true);
            if (Want("settings") || Want("theme-picker"))
            {
                main.Size = new Size(1400, 900); Pump(300);   // three cards per row (the window cannot be taller than the screen)
                main.Show(new ToolsView(main)); Pump(800);
                ((ScrollableControl)Current()).AutoScrollPosition = new Point(0, 46); Pump(300);   // past the title: all card rows fit
                Control v = Current();
                Func<string, Control> tile = delegate(string title) { Control l = Btn2(v, title); return l.Parent.Parent; };
                Bitmap b = Grab(main);
                if (Want("theme-picker"))
                {
                    Bitmap c = (Bitmap)b.Clone();
                    Save(c, "theme-picker", Rectangle.Inflate(R(main, tile("المظهر")), 8, 8));
                }
                if (Want("settings"))
                    Save(b, "settings", ContentArea(),
                        M(main, tile("الاتصال بالبيانات"), 1, 0), M(main, tile("المظهر"), 2, 0), M(main, tile("نسخة احتياطية"), 3, 0),
                        M(main, tile("الإعدادات العامة"), 4, 0), M(main, tile("التحديثات والدعم"), 5, 0), M(main, tile("إقفال السنة"), 6, 0), M(main, tile("دليل الاستخدام"), 7, 0),
                        M(main, tile("قفل الفترات الشهرية"), 8, 0), M(main, tile("سجل التدقيق"), 9, 0));
                main.Size = Win; Pump(200);
            }
            if (Want("general-settings"))
            {
                GeneralSettingsForm f = new GeneralSettingsForm(); Show(f, Size.Empty);
                Bitmap b = Grab(f);
                TableLayoutPanel t = Find<TableLayoutPanel>(f, null);
                Save(b, "general-settings", Rectangle.Empty, M(f, t.GetControlFromPosition(1, 0), 1, 1), M(f, Btn(f, "اختيار صورة…"), 2, 1));
                Close(f);
            }
            if (Want("users"))
            {
                main.Show(new UsersView()); Pump(600);
                Control v = Current();
                ListBox users = Find<ListBox>(v, null); if (users.Items.Count > 1) users.SelectedIndex = 1; Pump(300);
                Bitmap b = Grab(main);
                Save(b, "users", ContentArea(), M(main, users, 1, 0), M(main, Find<CheckedListBox>(v, null), 2, 0), M(main, Btn(v, "حفظ"), 3, 1));
            }
            if (Want("year-close"))
            {
                YearCloseForm f = new YearCloseForm(); Show(f, Size.Empty);
                Bitmap b = Grab(f);
                Save(b, "year-close", Rectangle.Empty, M(f, Find<ComboBox>(f, null), 1, 1), M(f, Btn(f, "إقفال السنة"), 2, 1));
                Close(f);
            }
            if (Want("change-password"))
            {
                ChangePasswordForm f = new ChangePasswordForm(); Show(f, Size.Empty);
                Bitmap b = Grab(f);
                Save(b, "change-password", Rectangle.Empty);
                Close(f);
            }
            if (Want("problem-report"))
            {
                ConstructorInfo ci = typeof(ProblemReportForm).GetConstructor(BindingFlags.NonPublic | BindingFlags.Instance, null, new Type[] { typeof(Bitmap) }, null);
                Form f = (Form)ci.Invoke(new object[] { new Bitmap(10, 10) });
                Show(f, Size.Empty);
                TextBox t = Find<TextBox>(f, null); t.Text = "عند حفظ سند إخراج رقم 120 ظهرت رسالة خطأ، والمادة لم تُخصم من الرصيد.";
                Bitmap b = Grab(f);
                Save(b, "problem-report", Rectangle.Empty, M(f, t, 1, 0), M(f, Btn(f, "إرسال التبليغ"), 2, 1));
                Close(f);
            }
            Close(main);
        }

        /// <summary>A settings card title (a label inside the card's title row).</summary>
        static Control Btn2(Control root, string title)
        {
            return Find<Label>(root, delegate(Label l) { return l.Text == title && l.Parent != null && l.Parent.Parent != null && l.Parent.Parent.Width == 344; });
        }

        // ================================================================== 11. dark theme
        // ================================================================== 12. language, forced password change
        static void LanguageShots()
        {
            if (Want("language"))
            {
                bool en = Lang.English; Lang.English = false;   // the very first start: nothing chosen yet
                LanguageForm f = new LanguageForm(); Show(f, Size.Empty);
                Bitmap b = Grab(f);
                Save(b, "language", Rectangle.Empty, M(f, Btn(f, "العربية"), 1, 0), M(f, Btn(f, "English"), 2, 1));
                Close(f); Lang.English = en;
            }
            if (Want("first-password"))
            {
                // new data: user 1 / password 1 must choose a new password at the first sign-in (the current one is filled in)
                ChangePasswordForm f = new ChangePasswordForm("1"); Show(f, Size.Empty);
                TableLayoutPanel t = Find<TableLayoutPanel>(f, null);
                Control cur = t.GetControlFromPosition(1, 0), n1 = t.GetControlFromPosition(1, 1), n2 = t.GetControlFromPosition(1, 2);
                n1.Text = "demo-2026"; n2.Text = "demo-2026";
                Bitmap b = Grab(f);
                Save(b, "first-password", Rectangle.Empty, M(f, cur, 1, 1), M(f, n1, 2, 1), M(f, n2, 3, 1), M(f, Btn(f, "حفظ"), 4, 1));
                Close(f);
            }
            if (Want("lang-link"))
            {
                main = NewMain(false);
                Bitmap b = Grab(main);
                Save(b, "lang-link", new Rectangle(0, 30, 560, 64), M(main, Btn(main, "English"), 1, 3));
                Close(main);
            }
            if (Want("home-english"))
            {
                Lang.English = true;
                try
                {
                    main = NewMain(false);
                    Save(Grab(main), "home-english", Rectangle.Empty, M(main, Btn(main, "عربي"), 1, 4));
                    Close(main);
                }
                finally { Lang.English = false; }
            }
        }

        // ================================================================== 13. stock valuation
        static void ValuationShots()
        {
            if (!Want("valuation") && !Want("stock-value") && !Want("wo-report")) return;
            main = NewMain(true);
            if (Want("valuation"))
            {
                ReportsWindow w = NewReports("valuation", Win);
                Control rv = (Control)Field(w, "view");
                Bitmap b = Grab(w);
                Save(b, "valuation", Rectangle.Empty, M(w, Btn(rv, "تقييم المخزون بتاريخ"), 1, 4), M(w, (Control)Field(rv, "pars"), 2, 0), M(w, Btn(rv, "عرض التقرير"), 3, 1));
                Close(w);
            }
            if (Want("stock-value"))
            {
                ReportsWindow w = NewReports("stock", Win);
                Control rv = (Control)Field(w, "view");
                WebBrowser wb = (WebBrowser)Field(rv, "wb");
                Bitmap b = Grab(w);
                Rectangle wr = R(w, wb);
                List<Mark> marks = new List<Mark>();
                foreach (HtmlElement e in wb.Document.GetElementsByTagName("span"))
                    if (e.GetAttribute("className") == "chip" && (e.InnerText ?? "").Contains("قيمة المخزون")) { marks.Add(M(Dom(e, wr), 1, 0)); break; }
                foreach (HtmlElement e in wb.Document.GetElementsByTagName("th"))
                    if ((e.InnerText ?? "").Trim().StartsWith("القيمة")) { marks.Add(M(Dom(e, wr), 2, 1)); break; }
                Save(b, "stock-value", new Rectangle(wr.Left, wr.Top, wr.Width, wr.Height), marks.ToArray());
                Close(w);
            }
            if (Want("wo-report"))
            {
                ReportsWindow w = NewReports("writeoffs", Win);
                Control rv = (Control)Field(w, "view");
                Bitmap b = Grab(w);
                Save(b, "wo-report", Rectangle.Empty, M(w, Btn(rv, "الإتلاف حسب السبب"), 1, 4), M(w, (Control)Field(rv, "flag"), 2, 0));
                Close(w);
            }
            Close(main);
        }

        // ================================================================== 14. write-offs
        static void WriteOffShots()
        {
            if (!WriteOffs.Ready) { Console.WriteLine("write-offs not ready"); return; }
            if (!Want("wo-list") && !Want("wo-new") && !Want("wo-approve")) return;
            main = NewMain(false);
            if (Want("wo-list"))
            {
                WriteOffsView v = new WriteOffsView(main, -1 /*all*/); main.Show(v); Pump(800);
                v.Reload(); Pump(300);
                Bitmap b = Grab(main);
                Save(b, "wo-list", ContentArea(), M(main, Btn(v, "+ سند إتلاف جديد"), 1, 1), M(main, Btn(v, "اعتماد وترحيل"), 2, 1), M(main, Btn(v, "رفض"), 3, 1),
                    M(main, (Control)Field(v, "cmbStatus"), 4, 1), M(main, Btn(v, "تقرير الإتلاف حسب السبب"), 5, 1));
            }
            if (Want("wo-new"))
            {
                WriteOffView v = new WriteOffView(main, 0); main.Show(v); Pump(600);
                ComboBox reason = (ComboBox)Field(v, "cmbReason");
                UI.SetSel(reason, WriteOffs.Damaged);
                ((TextBox)Field(v, "txtNote")).Text = "تسرّب ماء من السقف على الرف B-03";
                v.AddLine(ItemByCode("LED-R18"), 4); v.AddLine(ItemByCode("SK-13A"), 2);
                Pump(300);
                DataGridView g = (DataGridView)Field(v, "grid");
                Rectangle gr = R(main, g);
                Rectangle rc = g.GetColumnDisplayRectangle(g.Columns["reason"].Index, false); rc.Offset(gr.Location); rc.Height = g.ColumnHeadersHeight + g.Rows[0].Height * 2;
                Bitmap b = Grab(main);
                Save(b, "wo-new", ContentArea(), M(main, (Control)Field(v, "cmbStore"), 1, 0), M(main, reason, 2, 0), M(main, (Control)Field(v, "item"), 3, 0),
                    M(rc, 4, 0), M(main, (Control)Field(v, "btnSave"), 5, 1), M(main, (Control)Field(v, "btnSend"), 6, 1));
            }
            if (Want("wo-approve"))
            {
                int pending = Db.I(Db.Scalar("SELECT MIN(wo_no) FROM wo_head WHERE status = ?", WriteOffs.Pending));
                if (pending > 0)
                {
                    WriteOffView v = new WriteOffView(main, pending); main.Show(v); Pump(700);
                    Bitmap b = Grab(main);
                    Save(b, "wo-approve", ContentArea(), M(main, (Control)Field(v, "btnApprove"), 1, 1), M(main, (Control)Field(v, "btnReject"), 2, 1), M(main, (Control)Field(v, "lblState"), 3, 0));
                }
            }
            Close(main);
        }

        // ================================================================== 15. monthly period lock, audit trail
        static void LockShots()
        {
            if (Want("period-lock"))
            {
                PeriodLockForm f = new PeriodLockForm(); Show(f, Size.Empty);
                Bitmap b = Grab(f);
                Save(b, "period-lock", Rectangle.Empty, M(f, (Control)Field(f, "lblStatus"), 1, 0), M(U(R(f, (Control)Field(f, "cmbMonth")), R(f, (Control)Field(f, "numYear"))), 2, 0),
                    M(f, (Control)Field(f, "bLock"), 3, 1), M(f, (Control)Field(f, "bUnlock"), 4, 1));
                Close(f);
            }
            if (Want("audit-trail"))
            {
                NeutralLog();
                AuditTrailForm f = new AuditTrailForm(); Show(f, Size.Empty);
                Pump(600);
                DataGridView g = Find<DataGridView>(f, null);
                Bitmap b = Grab(f);
                Rectangle gr = R(f, g);
                Rectangle ov = U(g.GetColumnDisplayRectangle(g.Columns["o"].Index, false), g.GetColumnDisplayRectangle(g.Columns["n"].Index, false));
                ov.Offset(gr.Location); ov.Height = Math.Min(gr.Height, g.ColumnHeadersHeight + g.RowTemplate.Height * Math.Min(6, Math.Max(1, g.Rows.Count)));
                Save(b, "audit-trail", Rectangle.Empty, M(f, ((Control)Field(f, "chkOnly")).Parent, 1, 0), M(ov, 2, 1));
                Close(f);
            }
        }

        static void DarkShots()
        {
            if (!Want("dark-home") && !Want("dark-issue")) return;
            Theme.Use(Palette.Make(true, "blue"));
            main = NewMain(false);
            if (Want("dark-home")) Save(Grab(main), "dark-home", Rectangle.Empty);
            if (Want("dark-issue"))
            {
                DocView v = OpenNew(DocKind.Issue);
                ((ProjectPicker)Field(v, "project")).Value = projD;
                Add(v, "CT-25", 2); Add(v, "CU-4X16", 40); Add(v, "OL-0913", 2);
                Save(Grab(main), "dark-issue", ContentArea());
            }
            Close(main);
            Theme.Use(Palette.Make(false, "blue"));
        }
    }
}
