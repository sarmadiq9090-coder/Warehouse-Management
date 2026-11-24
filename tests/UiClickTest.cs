using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Windows.Forms;

namespace Stores
{
    // Clicks every button / link / tile on every screen of the program (advanced and beginner mode) on a copy of
    // the data. Every dialog that opens is recorded and closed (message boxes answered «لا» / «إلغاء»), every error
    // is recorded. Usage: UiTest.exe <copy of data.mdb> <out dir>
    static class UiTestMain
    {
        delegate bool EnumProc(IntPtr h, IntPtr l);
        [DllImport("user32.dll")] static extern bool EnumThreadWindows(int tid, EnumProc f, IntPtr l);
        [DllImport("user32.dll")] static extern bool EnumChildWindows(IntPtr p, EnumProc f, IntPtr l);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern int GetClassName(IntPtr h, StringBuilder s, int n);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern int GetWindowText(IntPtr h, StringBuilder s, int n);
        [DllImport("user32.dll")] static extern bool IsWindowVisible(IntPtr h);
        [DllImport("user32.dll")] static extern int GetDlgCtrlID(IntPtr h);
        [DllImport("user32.dll")] static extern IntPtr SendMessage(IntPtr h, int msg, IntPtr w, IntPtr l);
        [DllImport("user32.dll")] static extern bool PostMessage(IntPtr h, int msg, IntPtr w, IntPtr l);
        [DllImport("kernel32.dll")] static extern int GetCurrentThreadId();

        static string Cls(IntPtr h) { StringBuilder s = new StringBuilder(256); GetClassName(h, s, 256); return s.ToString(); }
        static string Txt(IntPtr h) { StringBuilder s = new StringBuilder(2048); GetWindowText(h, s, 2048); return s.ToString(); }

        static MainForm main;
        static string outDir;
        static StreamWriter log;
        static List<string> events = new List<string>();   // what happened during the current click
        static int errors, clicks, shotNo;
        static HashSet<IntPtr> handled = new HashSet<IntPtr>();
        static Dictionary<Form, DateTime> seenForms = new Dictionary<Form, DateTime>();
        static bool inTick;

        static void Log(string s) { log.WriteLine(s); log.Flush(); Console.WriteLine(s); }

        static void Pump(int ms) { DateTime end = DateTime.Now.AddMilliseconds(ms); while (DateTime.Now < end) { Application.DoEvents(); Thread.Sleep(15); } }

        // ------------------------------------------------------------ watchdog: records and closes dialogs
        static void Tick()
        {
            if (inTick) return; inTick = true;
            try
            {
                // WinForms dialogs
                foreach (Form f in Application.OpenForms.Cast<Form>().ToList())
                {
                    if (f == main || !f.Visible || f.IsDisposed) continue;
                    DateTime first;
                    if (!seenForms.TryGetValue(f, out first))
                    {
                        seenForms[f] = DateTime.Now;
                        string texts = string.Join(" | ", AllControls(f).OfType<Label>().Select(l => l.Text).Where(t => !string.IsNullOrWhiteSpace(t)).Take(4).ToArray());
                        string name = "shot_" + (++shotNo).ToString("000") + "_" + f.GetType().Name + ".png";
                        try { using (Bitmap b = new Bitmap(f.Width, f.Height)) { f.DrawToBitmap(b, new Rectangle(0, 0, f.Width, f.Height)); b.Save(Path.Combine(outDir, name), ImageFormat.Png); } } catch { }
                        events.Add("window " + f.GetType().Name + " «" + f.Text + "» " + (texts.Length > 0 ? "[" + Short(texts, 120) + "] " : "") + "(" + name + ")");
                        continue;
                    }
                    if ((DateTime.Now - first).TotalMilliseconds > 500) { try { f.DialogResult = DialogResult.Cancel; f.Close(); } catch (Exception ex) { events.Add("close failed: " + ex.Message); } }
                }
                // native windows of this thread: message boxes, file / print dialogs, print preview
                List<IntPtr> wins = new List<IntPtr>();
                EnumThreadWindows(GetCurrentThreadId(), delegate(IntPtr h, IntPtr l) { if (IsWindowVisible(h)) wins.Add(h); return true; }, IntPtr.Zero);
                foreach (IntPtr h in wins)
                {
                    if (Control.FromHandle(h) != null || handled.Contains(h)) continue;
                    string cls = Cls(h);
                    if (cls == "#32770")
                    {
                        List<string> texts = new List<string>(); IntPtr no = IntPtr.Zero, cancel = IntPtr.Zero, ok = IntPtr.Zero;
                        EnumChildWindows(h, delegate(IntPtr c, IntPtr l)
                        {
                            string cc = Cls(c); int id = GetDlgCtrlID(c);
                            if (cc == "Static" && Txt(c).Trim().Length > 0) texts.Add(Txt(c).Trim());
                            if (cc == "Button") { if (id == 7) no = c; else if (id == 2) cancel = c; else if (id == 1) ok = c; }
                            return true;
                        }, IntPtr.Zero);
                        string msg = string.Join(" ", texts.ToArray()).Replace("\r", " ").Replace("\n", " ");
                        bool isError = msg.Contains("حدث خطأ") || msg.Contains("Exception") || msg.Contains("خطأ غير متوقع");
                        if (isError) errors++;
                        IntPtr btn = no != IntPtr.Zero ? no : cancel != IntPtr.Zero ? cancel : ok;
                        events.Add((isError ? "ERROR " : "") + "dialog «" + Txt(h) + "» " + Short(msg, 220) + " → " + (btn == no ? "لا" : btn == cancel ? "إلغاء" : "موافق"));
                        handled.Add(h);
                        if (btn != IntPtr.Zero) PostMessage(btn, 0x00F5 /*BM_CLICK*/, IntPtr.Zero, IntPtr.Zero);
                        else PostMessage(h, 0x0010 /*WM_CLOSE*/, IntPtr.Zero, IntPtr.Zero);
                    }
                    else if (cls != "IME" && cls != "MSCTFIME UI" && !cls.StartsWith("WindowsForms10.tooltips") && cls != "tooltips_class32" && !cls.StartsWith("Internet Explorer_Hidden"))
                    {
                        events.Add("native window «" + Txt(h) + "» class " + cls + " → closed");
                        handled.Add(h);
                        PostMessage(h, 0x0010, IntPtr.Zero, IntPtr.Zero);
                    }
                }
            }
            catch (Exception ex) { events.Add("watchdog: " + ex.Message); }
            finally { inTick = false; }
        }

        static string Short(string s, int n) { return s.Length > n ? s.Substring(0, n) + "…" : s; }

        // ------------------------------------------------------------ finding clickable controls
        static readonly object EvClick = typeof(Control).GetField("EventClick", BindingFlags.NonPublic | BindingFlags.Static).GetValue(null);
        static readonly object EvLink = typeof(LinkLabel).GetField("EventLinkClicked", BindingFlags.NonPublic | BindingFlags.Static).GetValue(null);
        static readonly PropertyInfo EventsProp = typeof(Component).GetProperty("Events", BindingFlags.NonPublic | BindingFlags.Instance);

        static IEnumerable<Control> AllControls(Control root)
        {
            foreach (Control c in root.Controls)
            {
                yield return c;
                if (c is DataGridView) continue;
                foreach (Control k in AllControls(c)) yield return k;
            }
        }

        static bool Shown(Control c) { for (Control p = c; p != null; p = p.Parent) if (!p.Visible) return false; return c.Enabled; }

        static bool Clickable(Control c)
        {
            EventHandlerList ev = (EventHandlerList)EventsProp.GetValue(c, null);
            if (c is LinkLabel && ev[EvLink] != null) return true;
            return ev[EvClick] != null;
        }

        static string Name(Control c)
        {
            string t = c.Text;
            if (string.IsNullOrWhiteSpace(t)) { FieldInfo f = c.GetType().GetField("Title"); if (f != null) t = Convert.ToString(f.GetValue(c)); }
            if (string.IsNullOrWhiteSpace(t)) t = "(" + c.GetType().Name + ")";
            return t.Replace("\n", " ").Replace("\r", "");
        }

        static readonly string[] Skip = { "السيرفر", "سيرفر", "تغيير الاتصال", "إعدادات الاتصال", "خروج", "فتح المجلد", "فتح مجلد" };

        static List<Control> Clickables(Control root)
        {
            return AllControls(root).Where(c => Shown(c) && Clickable(c) && !(c is NavButton) && !(c is DataGridView)
                                              && !Skip.Any(s => Name(c).Contains(s))).ToList();
        }

        static void Click(Control c)
        {
            if (c is LinkLabel && ((LinkLabel)c).Links.Count > 0)
                typeof(LinkLabel).GetMethod("OnLinkClicked", BindingFlags.NonPublic | BindingFlags.Instance).Invoke(c, new object[] { new LinkLabelLinkClickedEventArgs(((LinkLabel)c).Links[0]) });
            else if (c is Button) ((Button)c).PerformClick();
            else typeof(Control).GetMethod("OnClick", BindingFlags.NonPublic | BindingFlags.Instance).Invoke(c, new object[] { EventArgs.Empty });
        }

        static Control Content() { return (Control)typeof(MainForm).GetField("current", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(main); }

        // ------------------------------------------------------------ one screen: click everything on it
        static void TestScreen(string name, Action open)
        {
            events.Clear();
            main.Show(new Label()); open(); Pump(700);
            Control view = Content();
            string viewType = view == null ? "?" : view.GetType().Name;
            if (events.Count > 0) { Log("[" + name + "] opening: " + string.Join(" ; ", events.ToArray())); }
            List<Control> list = Clickables(view);
            Log("=== " + name + " (" + viewType + "): " + list.Count + " clickable items");
            for (int i = 0; i < list.Count; i++)
            {
                main.Show(new Label()); open(); Pump(500);
                List<Control> now = Clickables(Content());
                if (i >= now.Count) { Log("  ! item " + i + " missing after reopen"); continue; }
                Control c = now[i];
                string label = Name(c);
                events.Clear(); int errBefore = errors;
                DateTime t0 = DateTime.Now;
                try { Click(c); }
                catch (TargetInvocationException ex) { errors++; events.Add("EXCEPTION " + ex.InnerException.GetType().Name + ": " + ex.InnerException.Message); }
                catch (Exception ex) { errors++; events.Add("EXCEPTION " + ex.GetType().Name + ": " + ex.Message); }
                Pump(600);
                clicks++;
                Control after = Content();
                string where = after == null ? "" : after.GetType().Name;
                string ms = ((int)(DateTime.Now - t0).TotalMilliseconds - 600).ToString();
                Log("  " + (errors > errBefore ? "✗ " : "✓ ") + "«" + label + "» [" + c.GetType().Name + "] → " + (where != viewType ? "opened " + where + "; " : "")
                    + (events.Count == 0 ? "(no dialog)" : string.Join(" ; ", events.ToArray())) + "  " + ms + "ms");
            }
            // grids: double-click the first row
            main.Show(new Label()); open(); Pump(500);
            foreach (DataGridView g in AllControls(Content()).OfType<DataGridView>().Where(Shown))
            {
                if (g.RowCount == 0) continue;
                events.Clear(); int errBefore = errors;
                try { typeof(DataGridView).GetMethod("OnCellDoubleClick", BindingFlags.NonPublic | BindingFlags.Instance).Invoke(g, new object[] { new DataGridViewCellEventArgs(Math.Min(1, g.ColumnCount - 1), 0) }); }
                catch (TargetInvocationException ex) { errors++; events.Add("EXCEPTION " + ex.InnerException.GetType().Name + ": " + ex.InnerException.Message); }
                Pump(600); clicks++;
                Control after = Content();
                Log("  " + (errors > errBefore ? "✗ " : "✓ ") + "double-click first row of grid (" + g.RowCount + " rows) → " + (after != null && after.GetType().Name != viewType ? "opened " + after.GetType().Name + "; " : "")
                    + (events.Count == 0 ? "(no dialog)" : string.Join(" ; ", events.ToArray())));
                main.Show(new Label()); open(); Pump(400);
                break;
            }
        }

        static void SaveShot(string name)
        {
            Pump(300);
            using (Bitmap b = new Bitmap(main.Width, main.Height)) { main.DrawToBitmap(b, new Rectangle(0, 0, main.Width, main.Height)); b.Save(Path.Combine(outDir, name + ".png"), ImageFormat.Png); }
        }

        // ------------------------------------------------------------ the report screen: run every report, test search + sort
        static void TestReports()
        {
            Log("=== التقارير: run every report from the screen");
            main.Show(new Label()); ReportsView rv = new ReportsView(null); main.Show(rv); Pump(500);
            Dictionary<string, Button> buttons = (Dictionary<string, Button>)typeof(ReportsView).GetField("buttons", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(rv);
            WebBrowser wb = (WebBrowser)typeof(ReportsView).GetField("wb", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(rv);
            Button run = AllControls(rv).OfType<Button>().First(b => b.Text == "عرض التقرير");
            ItemPicker ip = (ItemPicker)typeof(ReportsView).GetField("item", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(rv);
            PersonPicker pp = (PersonPicker)typeof(ReportsView).GetField("person", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(rv);
            ProjectPicker jp = (ProjectPicker)typeof(ReportsView).GetField("proj", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(rv);
            ip.Value = Db.I(Db.Scalar("SELECT TOP 1 item_no FROM sale_menu GROUP BY item_no ORDER BY COUNT(*) DESC"));
            pp.Value = Db.I(Db.Scalar("SELECT TOP 1 custom_no FROM sale GROUP BY custom_no ORDER BY COUNT(*) DESC"));
            jp.Value = Db.I(Db.Scalar("SELECT MAX(proj_no) FROM project"));
            foreach (KeyValuePair<string, Button> kv in buttons)
            {
                events.Clear(); int errBefore = errors;
                kv.Value.PerformClick(); Pump(200);
                bool done = false; WebBrowserDocumentCompletedEventHandler h = delegate { done = true; };
                wb.DocumentCompleted += h;
                DateTime t0 = DateTime.Now;
                run.PerformClick();
                while (!done && (DateTime.Now - t0).TotalSeconds < 120) Pump(50);
                wb.DocumentCompleted -= h;
                int ms = (int)(DateTime.Now - t0).TotalMilliseconds;
                ReportData last = (ReportData)typeof(ReportsView).GetField("last", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(rv);
                string extra = "";
                HtmlElement q = wb.Document == null ? null : wb.Document.GetElementById("q");
                if (q != null && last != null && last.Rows.Count > 3)
                {
                    // search inside the report: the 2nd row's code / name, then sort by the first column twice
                    string probe = "";
                    object[] r1 = last.Rows[1];
                    foreach (object v in r1) if (v is string && ((string)v).Length >= 3) { probe = ((string)v).Split(' ')[0]; break; }
                    q.SetAttribute("value", probe);
                    DateTime t1 = DateTime.Now;
                    wb.Document.InvokeScript("filt");
                    string cnt = wb.Document.GetElementById("cnt").InnerText;
                    int fms = (int)(DateTime.Now - t1).TotalMilliseconds;
                    q.SetAttribute("value", ""); wb.Document.InvokeScript("filt");
                    t1 = DateTime.Now;
                    wb.Document.InvokeScript("srt", new object[] { 0 }); wb.Document.InvokeScript("srt", new object[] { 0 });
                    int sms = (int)(DateTime.Now - t1).TotalMilliseconds;
                    string arrow = wb.Document.GetElementById("hd").Children[0].InnerText;
                    extra = "  search «" + probe + "» → " + cnt + " (" + fms + "ms), sort x2 " + sms + "ms, header now «" + arrow.Trim() + "»";
                }
                else if (last != null && last.Rows.Count > 20000) extra = "  (search/sort not offered: " + last.Rows.Count + " rows)";
                clicks++;
                Log("  " + (errors > errBefore ? "✗ " : "✓ ") + kv.Key + ": " + (last == null ? "no report" : last.Rows.Count + " rows") + " shown in " + ms + "ms" + extra
                    + (events.Count > 0 ? "  ; " + string.Join(" ; ", events.ToArray()) : ""));
                if (kv.Key == "critical") SaveShot("ui_reports_critical");
            }
        }

        [STAThread]
        static int Main(string[] args)
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
            Application.ThreadException += delegate(object s, ThreadExceptionEventArgs e) { errors++; events.Add("UNHANDLED " + e.Exception.GetType().Name + ": " + e.Exception.Message); };
            outDir = args[1]; Directory.CreateDirectory(outDir);
            log = new StreamWriter(Path.Combine(outDir, "ui_test.log"), false, new UTF8Encoding(false));
            string err = Db.InitAccess(args[0]); if (err != null) { Log(err); return 1; }
            Db.EnsureAppTables();
            Session.UserNo = 1; Session.UserName = "1"; Session.LoadPerms(); Session.SetMode(true);
            Cache.LoadAll();

            System.Windows.Forms.Timer wd = new System.Windows.Forms.Timer(); wd.Interval = 150; wd.Tick += delegate { Tick(); }; wd.Start();

            main = new MainForm();
            main.StartPosition = FormStartPosition.Manual; main.WindowState = FormWindowState.Normal;
            main.Location = new Point(-4000, -4000); main.Size = new Size(1366, 740); main.ShowInTaskbar = false;
            main.Show(); Pump(1500);

            int lastIssue = Db.I(Db.Scalar("SELECT MAX(shop_no) FROM sale"));
            int lastRec = Db.I(Db.Scalar("SELECT MAX(shop_no) FROM shop WHERE shop_type <> 3"));

            // top bar of the main window (outside the screens)
            events.Clear();
            Log("=== top bar");
            foreach (Control c in AllControls(main).Where(c => Shown(c) && Clickable(c) && !(c is NavButton) && Name(c).Contains("كلمة المرور")))
            { events.Clear(); Click(c); Pump(600); clicks++; Log("  ✓ «" + Name(c) + "» → " + string.Join(" ; ", events.ToArray())); }

            Session.SetMode(true);
            TestScreen("الرئيسية (متقدم)", delegate { main.Show(new DashboardView(main)); });
            TestScreen("إخراج مخزني جديد", delegate { main.OpenDoc(DocKind.Issue, 0); });
            TestScreen("إخراج مخزني موجود", delegate { main.OpenDoc(DocKind.Issue, lastIssue); });
            TestScreen("إدخال مخزني جديد", delegate { main.OpenDoc(DocKind.Receipt, 0); });
            TestScreen("إدخال مخزني موجود", delegate { main.OpenDoc(DocKind.Receipt, lastRec); });
            TestScreen("مردود إخراج مخزني", delegate { main.OpenDoc(DocKind.Return, 0); });
            TestScreen("مردود مشتريات", delegate { main.OpenDoc(DocKind.PurchaseReturn, 0); });
            TestScreen("حجز مواد لمشروع", delegate { main.OpenDoc(DocKind.Reserve, 0); });
            TestScreen("نقل مخزني", delegate { main.OpenDoc(DocKind.Transfer, 0); });
            TestScreen("المشاريع", delegate { main.Show(new ProjectsView()); });
            TestScreen("المواد", delegate { main.Show(new ItemsView(main)); });
            TestScreen("سجل السندات", delegate { main.Show(new DocListView(main)); });
            TestScreen("الجهات والأشخاص", delegate { main.Show(new PersonsView()); });
            TestScreen("التقارير (أزرار)", delegate { main.Show(new ReportsView("negative")); });
            TestScreen("المخازن والأنواع", delegate { main.Show(new LookupsView()); });
            TestScreen("المستخدمون", delegate { main.Show(new UsersView()); });
            TestScreen("الأدوات والصيانة", delegate { main.Show(new ToolsView(main)); });
            TestReports();

            Session.SetMode(false);
            TestScreen("الرئيسية (مبتدئ)", delegate { main.Show(new DashboardView(main)); });
            TestScreen("إخراج مخزني (مبتدئ)", delegate { main.OpenDoc(DocKind.Issue, 0); });
            TestScreen("إدخال مخزني (مبتدئ)", delegate { main.OpenDoc(DocKind.Receipt, 0); });
            TestScreen("حجز (مبتدئ)", delegate { main.OpenDoc(DocKind.Reserve, 0); });
            TestScreen("المواد (مبتدئ)", delegate { main.Show(new ItemsView(main)); });
            TestScreen("التقارير (مبتدئ)", delegate { main.Show(new ReportsView(null)); });
            Session.SetMode(true);

            main.Show(new Label());
            Log("");
            Log("TOTAL: " + clicks + " clicks, " + errors + " errors");
            log.Close();
            Environment.Exit(errors == 0 ? 0 : 2);
            return 0;
        }
    }
}
