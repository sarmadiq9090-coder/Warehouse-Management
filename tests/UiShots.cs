using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;

namespace Stores
{
    // Off-screen screenshots of the main screens (login, home beginner/advanced, an issue document, items, reports,
    // settings) at the given window sizes, to check the look and that nothing is clipped.
    // Usage: UiShots.exe <copy of data.mdb> <out dir> [1366x740 1100x680 …] [theme=dark|light|auto[:accent]] [live] [--en]
    // theme=…: the screens in that theme (accent: blue green teal purple orange graphite); live: built in light, then switched
    // while open (checks that a theme change recolours open windows).
    // --en: the English UI.
    static class UiShotsMain
    {
        [DllImport("user32.dll")] static extern bool PrintWindow(IntPtr h, IntPtr hdc, uint flags);

        static void Pump(int ms) { DateTime end = DateTime.Now.AddMilliseconds(ms); while (DateTime.Now < end) { Application.DoEvents(); Thread.Sleep(15); } }

        static void Shot(Form f, string path)
        {
            Pump(400);
            using (Bitmap b = new Bitmap(f.Width, f.Height))
            {
                bool ok = false;
                using (Graphics g = Graphics.FromImage(b))
                {
                    IntPtr hdc = g.GetHdc();
                    try { ok = PrintWindow(f.Handle, hdc, 2 /*PW_RENDERFULLCONTENT*/); } finally { g.ReleaseHdc(hdc); }
                }
                if (!ok) f.DrawToBitmap(b, new Rectangle(0, 0, f.Width, f.Height));
                b.Save(path, ImageFormat.Png);
            }
            Console.WriteLine(path);
        }

        static void DialogShot(Form f, string path)
        {
            f.StartPosition = FormStartPosition.Manual; f.Location = new Point(-4000, -4000); f.ShowInTaskbar = false;
            f.Show(); Shot(f, path); f.Close(); f.Dispose();
        }

        /// <summary>The reports window (its own top-level window) off-screen, with the stock report shown.</summary>
        static void ReportsWindowShot(MainForm main, string path)
        {
            System.Reflection.BindingFlags bf = System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance;
            Form w = (Form)Activator.CreateInstance(typeof(ReportsWindow), bf, null, new object[] { main }, null);
            ReportsView rv = new ReportsView("stock");
            typeof(ReportsWindow).GetMethod("SetView", bf).Invoke(w, new object[] { rv });
            w.StartPosition = FormStartPosition.Manual; w.Location = new Point(-4000, -4000); w.ShowInTaskbar = false;
            w.Show(); Pump(1500); Shot(w, path); w.Close(); w.Dispose();
        }

        [STAThread]
        static int Main(string[] args)
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            string outDir = args[1]; Directory.CreateDirectory(outDir);
            List<Size> sizes = new List<Size>();
            Palette target = null; bool live = false;
            for (int i = 2; i < args.Length; i++)
            {
                if (args[i].StartsWith("theme="))
                {
                    string[] t = args[i].Substring(6).Split(':');
                    Theme.Mode = t[0]; Theme.AccentKey = t.Length > 1 ? t[1] : "blue";
                    target = Palette.Make(Theme.Resolve(Theme.Mode), Theme.AccentKey);
                    continue;
                }
                if (args[i] == "live") { live = true; continue; }
                if (args[i] == "--en") { Lang.English = true; continue; }
                string[] p = args[i].Split('x'); sizes.Add(new Size(int.Parse(p[0]), int.Parse(p[1])));
            }
            string tag = target == null ? "" : "_" + Theme.Mode + "-" + Theme.AccentKey + (live ? "-live" : "");
            Palette light = Palette.Make(false, "blue");
            Action<MainForm> sw = delegate(MainForm mf) { if (live) { Theme.Use(light); Pump(100); Theme.Use(target); Pump(200); } };
            if (sizes.Count == 0) { sizes.Add(new Size(1366, 740)); sizes.Add(new Size(1100, 680)); }
            string err = Db.InitAccess(args[0]); if (err != null) { Console.WriteLine(err); return 1; }
            Db.EnsureAppTables();
            Branding.Load();

            LoginForm login = new LoginForm();
            login.StartPosition = FormStartPosition.Manual; login.Location = new Point(-4000, -4000); login.ShowInTaskbar = false;
            login.Show(); Shot(login, Path.Combine(outDir, "login.png")); login.Close();
            DialogShot(new ConnectionForm(), Path.Combine(outDir, "connection.png"));
            bool en = Lang.English; Lang.English = false;   // the first start: no language chosen yet
            DialogShot(new LanguageForm(), Path.Combine(outDir, "first_start_language.png"));
            Lang.English = en;
            if (target != null && !live) Theme.Use(target);

            Session.UserNo = 1; Session.UserName = "خالد"; Session.LoadPerms(); Session.SetMode(false);
            Cache.LoadAll();
            int lastIssue = Db.I(Db.Scalar("SELECT MAX(shop_no) FROM sale"));

            foreach (Size sz in sizes)
            {
                string s = tag + "_" + sz.Width + "x" + sz.Height + ".png";
                foreach (bool adv in new bool[] { false, true })
                {
                    Session.SetMode(adv);
                    MainForm main = new MainForm();
                    main.StartPosition = FormStartPosition.Manual; main.WindowState = FormWindowState.Normal;
                    main.Location = new Point(-4000, -4000); main.Size = sz; main.ShowInTaskbar = false;
                    main.Show(); Pump(1200);
                    if (main.Size != sz) { main.Size = sz; Pump(300); }   // the first Size can be clamped before the window exists
                    if (main.Size != sz) Console.WriteLine("window is " + main.Size + " (screen " + Screen.PrimaryScreen.Bounds.Size + ")");
                    string m = adv ? "adv" : "beg";
                    sw(main); Shot(main, Path.Combine(outDir, "home_" + m + s));
                    main.OpenDoc(DocKind.Issue, lastIssue); sw(main); Shot(main, Path.Combine(outDir, "issue_" + m + s));
                    main.Show(new ItemsView(main)); sw(main); Shot(main, Path.Combine(outDir, "items_" + m + s));
                    main.Show(new ReportsView("stock")); Pump(1500); sw(main); Shot(main, Path.Combine(outDir, "reports_" + m + s));
                    main.Show(new ToolsView(main)); sw(main); Shot(main, Path.Combine(outDir, "settings_" + m + s));
                    if (!adv) ReportsWindowShot(main, Path.Combine(outDir, "reportswindow" + s));
                    main.Show(new RequisitionsView(main)); Pump(600); sw(main); Shot(main, Path.Combine(outDir, "requisitions_" + m + s));
                    main.Show(new PurchaseRequestsView()); Pump(600); sw(main); Shot(main, Path.Combine(outDir, "purchases_" + m + s));
                    main.Show(new WriteOffsView(main)); Pump(600); sw(main); Shot(main, Path.Combine(outDir, "writeoffs_" + m + s));
                    if (adv) { main.Show(new ReportsView("valuation")); Pump(2500); sw(main); Shot(main, Path.Combine(outDir, "valuation_" + m + s)); }
                    main.Show(new ReportsView("valuation")); Pump(1500); sw(main); Shot(main, Path.Combine(outDir, "valuation_" + m + s));
                    main.Show(new DashboardView(main)); Pump(300);
                    main.Dispose();
                }
            }
            // printed pages (look at them with tools\HtmlShot.exe) and an Excel file of the stock report + the last issue
            ReportData stock = Reports.All.Find(d => d.Key == "stock").Run(new ReportParams());
            File.WriteAllText(Path.Combine(outDir, "report_stock.html"), Html.Render(stock), new System.Text.UTF8Encoding(false));
            File.WriteAllText(Path.Combine(outDir, "report_valuation.html"), Html.Render(Reports.Get("valuation").Run(new ReportParams())), new System.Text.UTF8Encoding(false));
            File.WriteAllText(Path.Combine(outDir, "voucher.html"), Html.Render(Reports.Voucher(Posting.Load(DocKind.Issue, lastIssue))), new System.Text.UTF8Encoding(false));
            Excel.Write(Path.Combine(outDir, "report_stock.xlsx"), stock.Title, stock.Subtitle, stock.Cols.ConvertAll(c => c.Header), stock.Cols.ConvertAll(c => c.Kind), stock.Rows, stock.Cols.ConvertAll(c => c.Total));
            DialogShot(new ItemDialog(Db.I(Db.Scalar("SELECT TOP 1 item_no FROM item ORDER BY item_no"))), Path.Combine(outDir, "item_dialog" + tag + ".png"));
            DialogShot(new PeriodLockForm(), Path.Combine(outDir, "period_lock" + tag + ".png"));
            DialogShot(new AuditTrailForm(), Path.Combine(outDir, "audit_trail" + tag + ".png"));
            DialogShot(new PeriodLockForm(), Path.Combine(outDir, "period_lock.png"));
            DialogShot(new AuditTrailForm(), Path.Combine(outDir, "audit_trail.png"));
            Session.SetMode(false);
            Db.SignOff();
            Environment.Exit(0);
            return 0;
        }
    }
}
