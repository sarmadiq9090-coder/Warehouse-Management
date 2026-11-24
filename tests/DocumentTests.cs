using System;
using System.Collections.Generic;
using System.Data;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Windows.Forms;

namespace Stores
{
    /// <summary>
    /// Version 1.2.1 (owner's list): receipts with a store per item, double click on report rows, print preview / PDF /
    /// Excel of documents, finding documents, new item from the receipt's entry bar, item statement from the home search.
    /// Usage: DocumentTests.exe &lt;copy.mdb&gt; &lt;out dir&gt; [&lt;sql instance, e.g. (localdb)\EngStoresTest&gt;]
    /// </summary>
    static class DocumentTestsMain
    {
        static int fails = 0;
        static void Check(bool ok, string what) { Console.WriteLine((ok ? "PASS  " : "FAIL  ") + what); if (!ok) fails++; }
        static string Fails(Action a) { try { a(); return null; } catch (UserError ue) { return ue.Message; } }
        const BindingFlags BF = BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Public;
        static object Field(object o, string name) { return o.GetType().GetField(name, BF).GetValue(o); }
        static object Call(object o, string name, params object[] a) { return o.GetType().GetMethods(BF).First(m => m.Name == name && m.GetParameters().Length == a.Length).Invoke(o, a); }
        static double Bal(int item, int store) { return Db.D(Db.Scalar("SELECT SUM(item_bal) FROM item_stor WHERE item_no = ? AND stor = ?", item, store)); }
        static void Pump(int ms) { DateTime end = DateTime.Now.AddMilliseconds(ms); while (DateTime.Now < end) { Application.DoEvents(); Thread.Sleep(15); } }

        [System.Runtime.InteropServices.DllImport("user32.dll")] static extern bool PrintWindow(IntPtr hwnd, IntPtr hdc, uint flags);
        static void Shot(Control c, string path)
        {
            using (Bitmap b = new Bitmap(c.Width, c.Height))
            {
                bool ok;
                using (Graphics g = Graphics.FromImage(b)) { IntPtr dc = g.GetHdc(); ok = PrintWindow(c.Handle, dc, 2); g.ReleaseHdc(dc); }
                if (!ok) c.DrawToBitmap(b, new Rectangle(0, 0, c.Width, c.Height));
                b.Save(path, ImageFormat.Png);
            }
            Console.WriteLine("screenshot: " + path);
        }

        static Form Host(int w, int h)
        {
            Form f = new Form();
            f.StartPosition = FormStartPosition.Manual; f.Location = new Point(-3000, -3000); f.Size = new Size(w, h); f.ShowInTaskbar = false;
            f.RightToLeft = RightToLeft.Yes; f.Font = Theme.Font; f.BackColor = Theme.Bg;
            return f;
        }

        [STAThread]
        static int Main(string[] args)
        {
            Console.OutputEncoding = Encoding.UTF8;
            Application.EnableVisualStyles();
            try { return Run(args); }
            catch (Exception ex) { Console.WriteLine("FAIL  unexpected error: " + ex); return 1; }
        }

        static string outDir;

        /// <summary>One instance per session: the second is refused and the first one's window is asked to show itself.</summary>
        static void Single()
        {
            Console.WriteLine("\n-- one instance");
            const string name = "EngStores.Test.Single";
            Check(SingleInstance.Acquire(name), "first start");
            bool second = true; bool asked = false;
            Thread t = new Thread(delegate() { second = SingleInstance.Acquire(name); }); t.SetApartmentState(ApartmentState.STA);
            using (ShowProbe f = new ShowProbe(delegate(Form p) { asked = true; SingleInstance.Shown(p, name); }))
            {
                f.StartPosition = FormStartPosition.Manual; f.Location = new Point(-3000, -3000); f.Show(); Pump(100); f.Hide(); Pump(100);   // like the tray
                t.Start();
                DateTime end = DateTime.Now.AddSeconds(8); while (t.IsAlive && DateTime.Now < end) Pump(30);
            }
            if (t.IsAlive || second || !asked) Console.WriteLine("   alive " + t.IsAlive + ", second " + second + ", asked " + asked);
            Check(!t.IsAlive && !second && asked, "second start refused, the open window (hidden next to the clock) is shown");
        }

        class ShowProbe : Form
        {
            readonly Action<Form> act;
            public ShowProbe(Action<Form> a) { act = a; }
            protected override void WndProc(ref Message m) { if (act != null && SingleInstance.IsShowMessage(ref m)) { act(this); return; } base.WndProc(ref m); }
        }

        static int Run(string[] args)
        {
            outDir = args[1]; Directory.CreateDirectory(outDir);
            // the real file has one damaged record in sale_menu_1 (Jet: "attempting to change the same data"): the program's
            // «ضغط وإصلاح ملف البيانات» drops it, so the test does the same first
            Type jt = Type.GetTypeFromProgID("JRO.JetEngine");
            string fix = Path.Combine(outDir, "compacted.mdb"); if (File.Exists(fix)) File.Delete(fix);
            jt.InvokeMember("CompactDatabase", BindingFlags.InvokeMethod, null, Activator.CreateInstance(jt), new object[] {
                "Provider=Microsoft.Jet.OLEDB.4.0;Data Source=" + args[0], "Provider=Microsoft.Jet.OLEDB.4.0;Data Source=" + fix + ";Jet OLEDB:Engine Type=5" });
            args[0] = fix;
            string err = Db.InitAccess(args[0]);
            if (err != null) { Console.WriteLine("FAIL  " + err); return 1; }
            Db.EnsureAppTables();
            CostCentres.ConvertAtStart();
            Cache.LoadAll();
            Session.UserNo = 1; Session.UserName = "اختبار"; Session.LoadPerms(); Session.SetMode(true);
            Posting.RecalcBalances();
            Single();
            SplitReceipts();
            ReceiptScreen();
            Links();
            Browser();
            Pages();
            Find();
            NewItem();
            Review();
            Statement();
            Check(Posting.RecalcBalances() == 0, "balances consistent at the end");
            if (args.Length > 2) Sql(args[0], args[2]);
            Console.WriteLine(fails == 0 ? "\nALL TESTS PASSED" : "\n" + fails + " FAILED");
            return fails == 0 ? 0 : 1;
        }

        static void Sql(string mdb, string host)
        {
            Console.WriteLine("\n-- SQL Server: " + host);
            const string db = "EngStoresDocumentTest";
            Action<string> admin = delegate(string s)
            {
                using (System.Data.SqlClient.SqlConnection c = new System.Data.SqlClient.SqlConnection(Db.BuildSqlConnStr(host, "master", "", "", true)))
                { c.Open(); using (System.Data.SqlClient.SqlCommand m = new System.Data.SqlClient.SqlCommand(s, c)) { m.CommandTimeout = 300; m.ExecuteNonQuery(); } }
            };
            string drop = "IF DB_ID('" + db + "') IS NOT NULL BEGIN ALTER DATABASE [" + db + "] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE [" + db + "] END";
            admin(drop); admin("CREATE DATABASE [" + db + "]");
            try
            {
                string err = Db.InitSql(Db.BuildSqlConnStr(host, db, "", "", true));
                if (err != null) { Check(false, "LocalDB: " + err); return; }
                Migrate.Copy(mdb, delegate { });
                Db.EnsureAppTables(); Cache.LoadAll(); Session.LoadPerms();
                Posting.RecalcBalances();
                SplitReceipts(); Links(); Find();
                Check(Posting.RecalcBalances() == 0, "SQL: balances consistent");
            }
            finally { Db.Close(); System.Data.SqlClient.SqlConnection.ClearAllPools(); admin(drop); }
        }

        // ------------------------------------------------------------ data helpers
        static int[] Two()
        {
            List<int> s = Cache.Stores.Rows.Cast<DataRow>().Select(r => Db.I(r["stor_no"])).ToList();
            if (s.Count < 2) { Db.Exec("INSERT INTO stor (stor_name) VALUES (?)", "مخزن اختبار"); Cache.LoadAll(); s = Cache.Stores.Rows.Cast<DataRow>().Select(r => Db.I(r["stor_no"])).ToList(); }
            return new[] { s[0], s[1] };
        }
        static int Supplier() { return Db.I(Cache.Persons.Rows[0]["p_no"]); }
        static int[] Items(int n) { return Cache.Items.Rows.Cast<DataRow>().Where(r => !Db.B(r["freez"])).Take(n).Select(r => Db.I(r["item_no"])).ToArray(); }

        static Doc Receipt(int store, params double[] v)      // item, qty, store …
        {
            Doc d = new Doc(); d.Kind = DocKind.Receipt; d.Date = DateTime.Today; d.Person = Supplier(); d.Store = store; d.Note = "اختبار";
            for (int i = 0; i < v.Length; i += 3) d.Lines.Add(new DocLine { ItemNo = (int)v[i], Qty = v[i + 1], Store = (int)v[i + 2], Price = 2 });
            return d;
        }

        // ------------------------------------------------------------ point 5
        static void SplitReceipts()
        {
            Console.WriteLine("\n-- receipts with a store per item");
            int[] st = Two(); int[] it = Items(3); int A = st[0], B = st[1];
            double a0 = Bal(it[0], A), b1 = Bal(it[1], B), a2 = Bal(it[2], A), b0 = Bal(it[0], B);
            Doc d = Receipt(A, it[0], 5, A, it[1], 7, B, it[2], 3, A, it[0], 4, B);
            List<Doc> docs = Posting.SaveByStore(d);
            Check(docs.Count == 2 && docs[0] == d && docs[0].Store == A && docs[1].Store == B, "two stores → two receipts, the chosen store first");
            Check(docs[0].Lines.Count == 2 && docs[1].Lines.Count == 2, "each receipt has its own lines");
            Check(docs[0].No != docs[1].No && long.Parse(docs[1].No) == long.Parse(docs[0].No) + 1, "consecutive numbers " + docs[0].No + " / " + docs[1].No);
            Check(docs[1].Note.Contains("تابع لسند الإدخال رقم " + docs[0].No), "the second names the first in its notes");
            Check(Bal(it[0], A) == a0 + 5 && Bal(it[1], B) == b1 + 7 && Bal(it[2], A) == a2 + 3 && Bal(it[0], B) == b0 + 4, "balances: every item in its own store");
            Check(Posting.Load(DocKind.Receipt, docs[1].Id).Person == d.Person, "same supplier on both");

            // editing: one line moves to the other store
            Doc e = Posting.Load(DocKind.Receipt, d.Id);
            foreach (DocLine l in e.Lines) l.Store = l.ItemNo == it[2] ? B : A;
            docs = Posting.SaveByStore(e);
            Check(docs.Count == 2 && Bal(it[2], A) == a2 && Posting.Load(DocKind.Receipt, e.Id).Lines.Count == 1, "editing: a line moved to another store becomes a receipt there");
            Doc second = docs[1];

            // a line already issued from cannot move
            Doc iss = new Doc(); iss.Kind = DocKind.Issue; iss.Date = DateTime.Today; iss.Store = A;
            iss.Project = Db.I(Db.Scalar("SELECT MIN(proj_no) FROM project WHERE p_no > 0")); if (iss.Project == 0) iss.Project = Db.I(Db.Scalar("SELECT MIN(proj_no) FROM project"));
            double lotLeft = Db.D(Db.Scalar("SELECT SUM(qty - qty_sale) FROM shop_menu WHERE item_no = ?", it[0]));
            iss.Lines.Add(new DocLine { ItemNo = it[0], Qty = lotLeft });        // uses every lot, ours too
            Posting.Save(iss);
            e = Posting.Load(DocKind.Receipt, d.Id);
            double before = Bal(it[0], A); int count = Db.I(Db.Scalar("SELECT COUNT(*) FROM shop"));
            e.Lines[0].Store = B; e.Lines.Add(new DocLine { ItemNo = it[1], Qty = 1, Store = A });
            string msg = Fails(delegate { Posting.SaveByStore(e); });
            Check(msg != null && msg.Contains("أُخرج منها"), "a line already issued from cannot move: " + msg);
            Check(Bal(it[0], A) == before && Db.I(Db.Scalar("SELECT COUNT(*) FROM shop")) == count, "refused: nothing changed");
            Posting.Delete(Posting.Load(DocKind.Issue, iss.Id));

            // all or nothing: the second part fails (quantity 0)
            count = Db.I(Db.Scalar("SELECT COUNT(*) FROM shop"));
            Doc bad = Receipt(A, it[0], 5, A, it[1], 0, B);
            msg = Fails(delegate { Posting.SaveByStore(bad); });
            Check(bad.Lines.Count == 2, "refused: the document keeps its lines");
            Check(msg != null && Db.I(Db.Scalar("SELECT COUNT(*) FROM shop")) == count, "one bad part: no document is saved");
            foreach (int id in new[] { second.Id, Db.I(Db.Scalar("SELECT MAX(shop_no) FROM shop WHERE menu_no = ?", (long.Parse(d.No) + 1).ToString())), d.Id }.Distinct())
            {
                Doc x = Posting.Load(DocKind.Receipt, id);
                if (x != null && x.Note.Contains("اختبار")) Posting.Delete(x);
            }
            Check(Bal(it[0], A) == a0 && Bal(it[1], B) == b1 && Bal(it[0], B) == b0, "balances back after deleting");
            Doc one = Receipt(A, it[0], 5, A);
            Check(Posting.SaveByStore(one).Count == 1, "one store → one receipt as before");
            Posting.Delete(Posting.Load(DocKind.Receipt, one.Id));
        }

        static void ReceiptScreen()
        {
            Console.WriteLine("\n-- receipt screen");
            int[] st = Two(); int[] it = Items(3);
            foreach (bool adv in new[] { false, true })
            {
                Session.SetMode(adv);
                using (Form f = Host(1280, 720))
                {
                    DocView v = new DocView(null, DocKind.Receipt, 0, 0); v.Dock = DockStyle.Fill; f.Controls.Add(v); f.Show(); Pump(200);
                    DataGridView g = (DataGridView)Field(v, "grid"); ComboBox cs = (ComboBox)Field(v, "cmbStore"); Label tot = (Label)Field(v, "lblTotals");
                    Check(g.Columns["store"].Visible, (adv ? "advanced" : "beginner") + ": store column shown for receipts");
                    UI.SetSel(cs, st[0]);
                    Call(v, "AddFromBar", it[0], 2.0, 0.0); Call(v, "AddFromBar", it[1], 3.0, 0.0);
                    UI.SetSel(cs, st[1]);
                    Call(v, "AddFromBar", it[2], 4.0, 0.0); Call(v, "AddFromBar", it[0], 1.0, 0.0);
                    List<DataGridViewRow> rows = g.Rows.Cast<DataGridViewRow>().Where(r => !r.IsNewRow).ToList();
                    Check(rows.Count == 4 && Db.I(rows[0].Cells["store"].Value) == st[0] && Db.I(rows[2].Cells["store"].Value) == st[1], "items go into the store chosen when they were added");
                    Check(tot.Text.Contains(Cache.Store(st[0]) + " (2)") && tot.Text.Contains(Cache.Store(st[1]) + " (2)"), "totals line: " + tot.Text);
                    Shot(f, Path.Combine(outDir, "receipt_stores_" + (adv ? "adv" : "beg") + ".png"));
                    rows[1].Cells["store"].Value = st[1];
                    Doc d = (Doc)Call(v, "Collect");
                    Check(d.Store == st[1] && d.Lines.Count(l => l.Store == st[1]) == 3, "changing one line's store");
                    Call(v, "SetAllStores");
                    d = (Doc)Call(v, "Collect");
                    Check(d.Lines.All(l => l.Store == st[1]) && d.Store == st[1], "«لكل المواد» moves every line");
                    Call(v, "SetClean", "", Color.Black);
                }
            }
            using (Form f = Host(1200, 700))
            {
                DocView v = new DocView(null, DocKind.Issue, 0, 0); v.Dock = DockStyle.Fill; f.Controls.Add(v); f.Show(); Pump(100);
                Check(!((DataGridView)Field(v, "grid")).Columns["store"].Visible, "no store column in issues");
                Check(((Button)Field(v, "btnFind")).Visible, "find by number button");
                Call(v, "SetClean", "", Color.Black);
            }
        }

        // ------------------------------------------------------------ point 2
        static ReportParams Params()
        {
            ReportParams p = new ReportParams(); p.From = new DateTime(2000, 1, 1); p.To = DateTime.Today.AddYears(1);
            p.ItemNo = Db.I(Db.Scalar("SELECT TOP 1 item_no FROM sale_menu GROUP BY item_no ORDER BY COUNT(*) DESC, item_no"));
            p.PersonNo = Db.I(Db.Scalar("SELECT TOP 1 custom_no FROM sale GROUP BY custom_no ORDER BY COUNT(*) DESC, custom_no"));
            p.ProjNo = Db.I(Db.Scalar("SELECT TOP 1 proj_no FROM sale WHERE proj_no IS NOT NULL GROUP BY proj_no ORDER BY COUNT(*) DESC, proj_no"));
            return p;
        }

        static ReportParams For(ReportDef d, bool flag)
        {
            ReportParams p = Params();
            if ((d.P & RP.ItemReq) == 0) p.ItemNo = 0;
            if ((d.P & RP.PersonReq) == 0) p.PersonNo = 0;
            if ((d.P & RP.ProjectReq) == 0) p.ProjNo = 0;
            p.Flag = flag;
            return p;
        }

        static readonly string[] NoLinks = { "monthly", "audit", "audittrail", "writeoffs" };

        static void Links()
        {
            Console.WriteLine("\n-- double click on report rows");
            foreach (ReportDef d in Reports.All)
                foreach (bool flag in (d.P & RP.Flag) != 0 ? new[] { false, true } : new[] { false })
                {
                    ReportData r = d.Run(For(d, flag));
                    int none = r.RowLink.Count(l => l == null), shown = r.Rows.Count;
                    if (d.Key == "card") shown--;       // «رصيد سابق»
                    bool ok = r.RowLink.Count == r.Rows.Count && (Array.IndexOf(NoLinks, d.Key) >= 0 || none == r.Rows.Count - shown);
                    // the linked documents exist and are of the kind they say
                    int bad = 0, tried = 0;
                    foreach (string l in r.RowLink.Where(x => x != null && x.StartsWith("doc/")).Distinct().Take(6))
                    {
                        string[] p = l.Split('/'); DocKind k = (DocKind)Enum.Parse(typeof(DocKind), p[1]); tried++;
                        Doc doc = Posting.Load(k, int.Parse(p[2]));
                        if (doc == null || doc.Kind != k) bad++;
                    }
                    Check(ok && bad == 0, d.Key + (flag ? " (detail)" : "") + ": " + r.Rows.Count + " rows, " + (r.Rows.Count - none) + " open something" + (tried > 0 ? ", " + tried + " documents checked" : ""));
                }
        }

        static void Browser()
        {
            Console.WriteLine("\n-- inside the report window");
            ReportData r = Reports.Get("issues").Run(For(Reports.Get("issues"), false));
            if (r.Rows.Count > 400) r = r.Subset(Enumerable.Range(0, 400).ToList());
            using (Form f = Host(1000, 700))
            {
                WebBrowser wb = new WebBrowser(); wb.Dock = DockStyle.Fill; wb.ScriptErrorsSuppressed = true; f.Controls.Add(wb); f.Show();
                string opened = null;
                wb.Navigating += delegate(object s, WebBrowserNavigatingEventArgs e) { if (e.Url.Scheme == "app") { opened = Uri.UnescapeDataString(e.Url.OriginalString.Substring(4)); e.Cancel = true; } };
                bool done = false; wb.DocumentCompleted += delegate { done = true; };
                wb.DocumentText = Html.Render(r);
                DateTime end = DateTime.Now.AddSeconds(20); while (!done && DateTime.Now < end) Pump(30);
                wb.Document.InvokeScript("eval", new object[] { "var ev=document.createEvent('MouseEvents');ev.initEvent('dblclick',true,true);document.getElementById('tb').rows[2].cells[1].dispatchEvent(ev);" });
                Pump(300);
                Check(opened == r.RowLink[2], "a double click asks the program for " + opened);
                Check(ReferenceEquals(RowLinks.Shown(wb, r), r), "nothing filtered: the whole report is printed");
                string no = Convert.ToString(r.Rows[5][1]);
                wb.Document.InvokeScript("eval", new object[] { "document.getElementById('q').value='" + no + "';filt();" });
                ReportData sub = RowLinks.Shown(wb, r);
                Check(sub.Rows.Count > 0 && sub.Rows.Count < r.Rows.Count && sub.Rows.All(x => string.Join(" ", x.Select(Convert.ToString).ToArray()).Contains(no)), "filtered inside the report: only " + sub.Rows.Count + " rows are printed");
                wb.Document.InvokeScript("eval", new object[] { "document.getElementById('q').value='zzqqxx';filt();" });
                Check(RowLinks.Shown(wb, r).Rows.Count == 0, "nothing found inside the report: nothing is printed");
                wb.Document.InvokeScript("eval", new object[] { "document.getElementById('q').value='';filt();srt(1);srt(1);" });
                sub = RowLinks.Shown(wb, r);
                Check(sub.Rows.Count == r.Rows.Count && !ReferenceEquals(sub, r) && sub.RowLink.Count == sub.Rows.Count, "sorted inside the report: printed in that order");
            }
        }

        // ------------------------------------------------------------ points 4 and 10
        static void Pages()
        {
            Console.WriteLine("\n-- print preview, PDF, Excel");
            int id = Db.I(Db.Scalar("SELECT TOP 1 h.shop_no FROM sale AS h WHERE " + Db.IssueOnly("h") + " ORDER BY h.shop_no DESC"));
            Doc doc = Posting.Load(DocKind.Issue, id);
            ReportData v = Reports.Voucher(doc);
            ReportPages pg = new ReportPages(v); pg.Build();
            Check(pg.Count >= 1, "voucher: " + pg.Count + " page(s)");
            using (Bitmap b = pg.Image(0, 110)) b.Save(Path.Combine(outDir, "page_voucher.png"), ImageFormat.Png);
            foreach (ReportDef d in Reports.All)
            {
                ReportData r = d.Run(For(d, false));
                Stopwatch sw = Stopwatch.StartNew();
                ReportPages p = new ReportPages(r); p.Build();
                string over = Overflow(p);
                int rows = RowBlocks(p);
                Check(over == null && rows >= r.Rows.Count, d.Key + ": " + r.Rows.Count + " rows on " + p.Count + " pages, " + sw.ElapsedMilliseconds + " ms" + (over == null ? "" : " — " + over));
                using (Bitmap b = p.Image(0, 80)) b.Save(Path.Combine(outDir, "page_" + d.Key + ".png"), ImageFormat.Png);
                if (p.Count > 1) using (Bitmap b = p.Image(p.Count - 1, 60)) b.Save(Path.Combine(outDir, "page_" + d.Key + "_last.png"), ImageFormat.Png);
            }
            pg.Landscape = true; pg.PaperW = 583; pg.PaperH = 827; pg.Build();
            Check(Overflow(pg) == null, "A5 landscape");

            string pdf = Path.Combine(outDir, "إخراج مخزني.pdf");
            Stopwatch t = Stopwatch.StartNew();
            Pdf.Save(pdf, v, null);
            byte[] head = File.ReadAllBytes(pdf);
            Check(head.Length > 2000 && Encoding.ASCII.GetString(head, 0, 5) == "%PDF-", "PDF written: " + head.Length + " bytes, " + t.ElapsedMilliseconds + " ms");
            Pdf.Save(pdf, v, null);
            Check(true, "saving over an existing PDF");
            Pdf.Save(Path.Combine(outDir, "stock.pdf"), Reports.Get("critical").Run(For(Reports.Get("critical"), false)), null);
            using (FileStream locked = new FileStream(pdf, FileMode.Open, FileAccess.Read, FileShare.None))
                Check((Fails(delegate { Pdf.Save(pdf, v, null); }) ?? "").Contains("مفتوح"), "a PDF open elsewhere: clear message");
            string xl = Path.Combine(outDir, "voucher.xlsx");
            Excel.Write(xl, v.Title, v.Subtitle, v.Cols.Select(c => c.Header).ToList(), v.Cols.Select(c => c.Kind).ToList(), v.Rows, v.Cols.Select(c => c.Total).ToList());
            Check(new FileInfo(xl).Length > 1500 && v.Subtitle.Contains("التاريخ"), "Excel of the voucher with its header line");

            Lang.English = true;
            try { ReportPages en = new ReportPages(v); en.Build(); using (Bitmap b = en.Image(0, 90)) b.Save(Path.Combine(outDir, "page_voucher_en.png"), ImageFormat.Png); Check(en.Count >= 1, "English voucher"); }
            finally { Lang.English = false; }

            using (PrintPreviewForm f = new PrintPreviewForm(v, true))
            {
                f.StartPosition = FormStartPosition.Manual; f.Location = new Point(-3000, -3000); f.Show(); Pump(400);
                PictureBox pic = (PictureBox)Field(f, "pic");
                Check(pic.Image != null && pic.Image.Width > 300, "preview shows the page (" + (pic.Image == null ? 0 : pic.Image.Width) + " px wide)");
                Call(f, "Zoom", 1.5f); Pump(50);
                Call(f, "Go", 5);
                Shot(f, Path.Combine(outDir, "preview.png"));
                f.Close();
            }
        }

        static string Overflow(ReportPages p)
        {
            System.Collections.IList pages = (System.Collections.IList)Field(p, "pages");
            foreach (System.Collections.IList pageList in pages)
                foreach (object pl in pageList)
                {
                    float y = (float)Field(pl, "Y"), h = (float)Field(Field(pl, "B"), "H");
                    if (y + h > p.H - 40 - 24 + 0.5f && pageList.Count > 1) return "a block ends at " + (y + h) + " of " + p.H;
                }
            return null;
        }

        static int RowBlocks(ReportPages p)
        {
            int n = 0;
            foreach (System.Collections.IList pageList in (System.Collections.IList)Field(p, "pages"))
                foreach (object pl in pageList) if ((bool)Field(Field(pl, "B"), "Row")) n++;
            return n;
        }

        // ------------------------------------------------------------ point 3
        static void Find()
        {
            Console.WriteLine("\n-- finding documents");
            string no = Db.S(Db.Scalar("SELECT TOP 1 menu_no FROM sale AS h WHERE " + Db.IssueOnly("h") + " ORDER BY shop_no DESC"));
            Stopwatch sw = Stopwatch.StartNew();
            DataTable t = DocFindDialog.Find(no);
            Check(t.Rows.Count > 0 && Db.S(t.Rows[0]["menu_no"]) == no, "by number " + no + ": " + t.Rows.Count + " results, the exact number first (" + sw.ElapsedMilliseconds + " ms)");
            Doc d = Posting.Load((DocKind)Db.I(t.Rows[0]["k"]), Db.I(t.Rows[0]["id"]));
            Check(d != null && d.No == no, "the result opens the right document");
            int pj = Db.I(Db.Scalar("SELECT TOP 1 proj_no FROM sale WHERE proj_no IS NOT NULL ORDER BY shop_no DESC"));
            if (pj > 0)
            {
                string name = Cache.Project(pj);
                DataTable c = DocFindDialog.Find(name);
                Check(c.Rows.Cast<DataRow>().Any(x => Db.S(x["pname"]).Contains(name)), "by cost centre «" + name + "»: " + c.Rows.Count);
            }
            Check(DocFindDialog.Find("").Rows.Count > 0, "empty text: the latest documents");
            Check(DocFindDialog.Find("لا يوجد مثل هذا ١٢٣٤٥٦٧٨٩ [%_").Rows.Count == 0, "nothing found, special characters are safe");
            if (!Db.IsSql)
                using (DocFindDialog f = new DocFindDialog(no))
                {
                    f.StartPosition = FormStartPosition.Manual; f.Location = new Point(-3000, -3000); f.Show(); Pump(300);
                    Shot(f, Path.Combine(outDir, "find.png")); f.Close();
                }
        }

        // ------------------------------------------------------------ point 7
        static void NewItem()
        {
            Console.WriteLine("\n-- new item from the receipt");
            Session.SetMode(false);
            int[] it = Items(2);
            using (Form f = Host(1200, 700))
            {
                DocView v = new DocView(null, DocKind.Receipt, 0, 0); v.Dock = DockStyle.Fill; f.Controls.Add(v); f.Show(); Pump(150);
                ItemEntryBar bar = (ItemEntryBar)Field(v, "entry"); DataGridView g = (DataGridView)Field(v, "grid");
                Check(bar.NewItem != null && Field(bar, "btnNewItem") != null, "receipts offer «مادة جديدة»");
                Call(v, "AddFromBar", it[0], 2.0, 0.0); Call(v, "AddFromBar", it[1], 3.0, 0.0);
                string asked = null;
                bar.NewItem = delegate(string s)
                {
                    asked = s;
                    Db.Exec("INSERT INTO item (item_name, item_bal1, item_bal, item_price_dl1, item_bal_ret, qty_f, sale_pr, tslsl, freez) VALUES (?, 0, 0, 0, 0, 0, 0, 0, ?)", s, false);
                    Cache.LoadItems();
                    return Cache.FindItemByName(s);
                };
                bar.Search.Text = "مادة اختبار جديدة زز"; bar.Qty.Text = "6";
                Call(bar, "AddNew", "مادة اختبار جديدة زز", false);
                Check(asked == "مادة اختبار جديدة زز" && bar.Pending > 0 && bar.Qty.Text == "6", "the new item is chosen and the typed quantity kept");
                Check(g.Rows.Cast<DataGridViewRow>().Count(r => !r.IsNewRow) == 2, "the list of the receipt is untouched");
                Call(bar, "Commit");
                Check(g.Rows.Cast<DataGridViewRow>().Count(r => !r.IsNewRow) == 3, "and it is added with «إضافة»");
                bar.NewItem = delegate { return 0; };
                bar.Search.Text = "شيء آخر غير موجود"; bar.Qty.Text = "9";
                Call(bar, "AddNew", "شيء آخر غير موجود", false);
                Check(g.Rows.Cast<DataGridViewRow>().Count(r => !r.IsNewRow) == 3 && bar.Search.Text == "شيء آخر غير موجود", "cancelled: nothing is lost");
                Shot(f, Path.Combine(outDir, "new_item.png"));
                Call(v, "SetClean", "", Color.Black);
            }
            using (Form f = Host(1200, 700))
            {
                DocView v = new DocView(null, DocKind.Issue, 0, 0); v.Dock = DockStyle.Fill; f.Controls.Add(v); f.Show(); Pump(80);
                Check(((ItemEntryBar)Field(v, "entry")).NewItem == null, "not in issues");
                Call(v, "SetClean", "", Color.Black);
            }
        }

        // ------------------------------------------------------------ findings of the code review
        [System.Runtime.InteropServices.DllImport("user32.dll")] static extern bool EnumWindows(EnumProc p, IntPtr l);
        delegate bool EnumProc(IntPtr h, IntPtr l);
        [System.Runtime.InteropServices.DllImport("user32.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode)] static extern int GetClassName(IntPtr h, StringBuilder s, int n);
        [System.Runtime.InteropServices.DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr h, out int pid);
        [System.Runtime.InteropServices.DllImport("user32.dll")] static extern bool PostMessage(IntPtr h, int m, IntPtr w, IntPtr l);

        /// <summary>Runs an action whose message boxes are answered one after the other (2 = OK / Cancel, 6 = Yes, 7 = No).
        /// Returns how many boxes appeared.</summary>
        static int Answer(Action a, params int[] answers)
        {
            int n = 0; bool stop = false; int me = Process.GetCurrentProcess().Id; HashSet<IntPtr> done = new HashSet<IntPtr>();
            Thread t = new Thread(delegate()
            {
                while (!stop)
                {
                    EnumWindows(delegate(IntPtr h, IntPtr l)
                    {
                        int pid; GetWindowThreadProcessId(h, out pid); if (pid != me || done.Contains(h)) return true;
                        StringBuilder s = new StringBuilder(64); GetClassName(h, s, 64);
                        if (s.ToString() != "#32770") return true;
                        done.Add(h); Thread.Sleep(150);
                        PostMessage(h, 0x111, (IntPtr)(n < answers.Length ? answers[n] : 2), IntPtr.Zero); n++;
                        return true;
                    }, IntPtr.Zero);
                    Thread.Sleep(60);
                }
            });
            t.IsBackground = true; t.Start();
            try { a(); } finally { stop = true; t.Join(2000); }
            return n;
        }

        static void Review()
        {
            Console.WriteLine("\n-- findings of the code review");
            int[] st = Two(); int[] it = Items(3);

            // a refused save of a new item leaves no number behind
            using (ItemDialog d = new ItemDialog(0))
            {
                d.StartPosition = FormStartPosition.Manual; d.Location = new Point(-3000, -3000); d.ShowInTaskbar = false; d.Show(); Pump(100);
                d.SetName("مادة اختبار المراجعة");
                DataGridView g = (DataGridView)Field(d, "stores");
                g.Rows[0].Cells["open"].Value = "abc";
                bool saved = true; int boxes = Answer(delegate { saved = (bool)Call(d, "SaveItem"); });
                Check(boxes == 1 && !saved && d.ItemNo == 0, "new item refused: it has no number (" + d.ItemNo + ")");
                g.Rows[0].Cells["open"].Value = 0.0;
                boxes = Answer(delegate { saved = (bool)Call(d, "SaveItem"); });
                Check(boxes == 0 && saved && Db.I(Db.Scalar("SELECT COUNT(*) FROM item WHERE item_no = ?", d.ItemNo)) == 1, "saved again: the item exists (" + d.ItemNo + ")");
                d.Close();
            }

            // the store above is changed after the lines were added
            Session.SetMode(true);
            using (Form f = Host(1280, 720))
            {
                DocView v = new DocView(null, DocKind.Receipt, 0, 0); v.Dock = DockStyle.Fill; f.Controls.Add(v); f.Show(); Pump(150);
                DataGridView g = (DataGridView)Field(v, "grid"); ComboBox cs = (ComboBox)Field(v, "cmbStore");
                UI.SetSel(cs, st[0]);
                Call(v, "AddFromBar", it[0], 2.0, 0.0); Call(v, "AddFromBar", it[1], 3.0, 0.0);
                UI.SetSel(cs, st[1]);
                int count = Db.I(Db.Scalar("SELECT COUNT(*) FROM shop"));
                bool ok = true; int boxes = Answer(delegate { ok = (bool)Call(v, "Save"); }, 2);
                Check(boxes == 1 && !ok && Db.I(g.Rows[0].Cells["store"].Value) == st[0], "store above without lines: asked, cancel changes nothing");
                boxes = Answer(delegate { ok = (bool)Call(v, "Save"); }, 6, 2);      // then «choose the supplier»
                Check(boxes == 2 && !ok && Db.I(g.Rows[0].Cells["store"].Value) == st[1] && Db.I(g.Rows[1].Cells["store"].Value) == st[1], "yes: every line goes there");
                Check(Db.I(Db.Scalar("SELECT COUNT(*) FROM shop")) == count, "nothing saved without a supplier");

                // the list of stores leaves edit mode when it closes; the wheel does not change a closed one
                g.CurrentCell = g.Rows[0].Cells["store"]; g.BeginEdit(true); Pump(50);
                ComboBox cb = g.EditingControl as ComboBox;
                HandledMouseEventArgs w = new HandledMouseEventArgs(MouseButtons.None, 0, 5, 5, -120);
                typeof(Control).GetMethod("OnMouseWheel", BF).Invoke(cb, new object[] { w });
                Check(cb != null && w.Handled && Db.I(g.Rows[0].Cells["store"].Value) == st[1], "wheel over a closed list of stores does nothing");
                cb.DroppedDown = true; Pump(80); cb.DroppedDown = false; Pump(80);
                Check(!g.IsCurrentCellInEditMode, "the list closed: the cell is not in edit mode");
                Call(v, "SetClean", "", Color.Black);
            }

            // the exact number among many newer documents that mention it
            string no = "9" + (DateTime.Now.Ticks % 100000).ToString("00000");
            Doc old = Receipt(st[0], it[0], 1, st[0]); old.Date = DateTime.Today.AddDays(-2); Posting.Save(old);
            Db.Exec("UPDATE shop SET menu_no = ? WHERE shop_no = ?", no, old.Id);
            List<int> made = new List<int>();
            for (int i = 0; i < 101; i++) { Doc x = Receipt(st[0], it[0], 1, st[0]); x.Note = "تابع لسند الإدخال رقم " + no; Posting.Save(x); made.Add(x.Id); }
            DataTable t = DocFindDialog.Find(no);
            Check(t.Rows.Count > 1 && Db.S(t.Rows[0]["menu_no"]) == no, "the exact number is found among 101 newer documents");
            made.Add(old.Id);
            foreach (int id in made) Posting.Delete(Posting.Load(DocKind.Receipt, id));

            // long reports: numbers of the last rows are not cut; a very long note does not pass the page
            ReportData r = new ReportData(); r.Title = "تقرير طويل";
            r.Col("ت", ColKind.Int); r.Col("اسم المادة", ColKind.Text); r.Col("ملاحظات", ColKind.Text); r.Col("الرصيد", ColKind.Num);
            for (int i = 1; i <= 4000; i++) r.Row(null, i, "مادة " + i, i == 20 ? string.Join(" ", Enumerable.Repeat("ملاحظة طويلة جدا", 900).ToArray()) : "قصيرة", i > 3900 ? 123456789.25 + i : (double)i);
            ReportPages p = new ReportPages(r); p.Build();
            float[] widths = (float[])Field(p, "widths");
            using (Bitmap b = new Bitmap(8, 8)) using (Graphics g = Graphics.FromImage(b)) using (Font fo = new Font("Segoe UI", (float)Field(p, "fontSize")))
            {
                b.SetResolution(300, 300); ReportPages.Prepare(g);
                Check(g.MeasureString(UI.Num(123456789.25 + 4000), fo).Width <= widths[3] + 1, "numbers after row 3000 fit their column");
            }
            float most = 0; foreach (System.Collections.IList pl in (System.Collections.IList)Field(p, "pages")) foreach (object o in pl) most = Math.Max(most, (float)Field(o, "Y") + (float)Field(Field(o, "B"), "H"));
            Check(most <= p.H - 40 - 24 + 0.5f, "a very long note stays on its page (" + most + " of " + p.H + ")");

            // PDF: progress, cancel
            r.Rows.RemoveRange(300, r.Rows.Count - 300); r.RowClass.RemoveRange(300, r.RowClass.Count - 300); r.RowLink.RemoveRange(300, r.RowLink.Count - 300);
            string pdf = Path.Combine(outDir, "long.pdf"); int calls = 0; bool cancelled = false;
            try { Pdf.Save(pdf, r, null, delegate(int a, int b) { return ++calls < 3; }); } catch (OperationCanceledException) { cancelled = true; }
            Check(cancelled && calls == 3 && !File.Exists(pdf), "PDF cancelled: no file");
            Stopwatch sw = Stopwatch.StartNew(); bool done = false; bool seen = false;
            System.Windows.Forms.Timer tm = new System.Windows.Forms.Timer(); tm.Interval = 400; int tick = 0;
            tm.Tick += delegate { WaitForm wf = Application.OpenForms.OfType<WaitForm>().FirstOrDefault(); if (wf == null) return; seen = true; if (++tick == 2) Shot(wf, Path.Combine(outDir, "pdf_wait.png")); };
            tm.Start();
            using (Form f = Host(600, 400)) { f.Show(); done = (bool)typeof(Pdf).GetMethod("SaveWithWindow", BindingFlags.NonPublic | BindingFlags.Static).Invoke(null, new object[] { f, pdf, r, null }); }
            tm.Dispose();
            Check(done && seen && File.Exists(pdf) && !Application.OpenForms.OfType<WaitForm>().Any(), "PDF of a long report behind the progress window: " + sw.ElapsedMilliseconds + " ms");

            // nothing found inside the report = an empty print, not the whole report
            ReportData none = r.Subset(new List<int>());
            ReportPages np = new ReportPages(none); np.Build();
            Check(none.Rows.Count == 0 && none.Summary.Count == 0 && none.Subtitle.Contains("المعروض: 0") && np.Count == 1, "no row found: an empty page");
        }

        // ------------------------------------------------------------ points 1, 6, 8
        static void Statement()
        {
            Console.WriteLine("\n-- item statement, login, menu");
            int item = Params().ItemNo;
            using (Form f = Host(1300, 800))
            {
                ReportsView rv = new ReportsView("card"); rv.Dock = DockStyle.Fill; f.Controls.Add(rv); f.Show(); Pump(100);
                rv.Preset(item, 0, 0); Pump(200);
                ReportData last = (ReportData)Field(rv, "last");
                Check(((ComboBox)Field(rv, "cmbPreset")).SelectedIndex == 6 && last != null && last.Title.Contains(Cache.ItemName(item)), "item statement over the whole period: " + (last == null ? 0 : last.Rows.Count) + " rows");
            }
            LoginForm lf = new LoginForm(); lf.StartPosition = FormStartPosition.Manual; lf.Location = new Point(-3000, -3000); lf.Show(); Pump(200);
            Check(All(lf).OfType<LinkLabel>().Any(l => l.Text == "تحديث البرنامج" && l.Visible), "login: «تحديث البرنامج»");
            Shot(lf, Path.Combine(outDir, "login.png")); lf.Close(); lf.Dispose();
            Session.SetMode(false);
            MainForm m = new MainForm(); m.StartPosition = FormStartPosition.Manual; m.WindowState = FormWindowState.Normal; m.Location = new Point(-3000, -3000); m.Size = new Size(1280, 740); m.Show();
            m.WindowState = FormWindowState.Normal; m.Location = new Point(-3000, -3000); m.Size = new Size(1280, 740); Pump(600);
            List<string> nav = All(m).OfType<NavButton>().Select(n => n.Text).ToList();
            Check(nav.Contains("بطاقة مادة") && !nav.Contains("المواد"), "menu: «بطاقة مادة»");
            Check(nav.Contains("سجل السندات"), "beginner menu: «سجل السندات»");
            SearchBox sb = All(m).OfType<SearchBox>().Single();
            Check(sb.Choice == 0, "home: one search box, items first");
            Call(sb, "Choose", 1, true);
            Check(sb.Choice == 1 && Db.GetSetting("home_search_user" + Session.UserNo, "") == "1", "choosing «مستند» is remembered");
            Shot(m, Path.Combine(outDir, "home_beg.png"));
            Call(sb, "Choose", 0, true);
            m.Show(new DocListView(m)); Pump(300);
            m.ExitProgram(); m.Dispose();
        }

        static IEnumerable<Control> All(Control c) { foreach (Control x in c.Controls) { yield return x; foreach (Control y in All(x)) yield return y; } }
    }
}
