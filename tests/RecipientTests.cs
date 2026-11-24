using System;
using System.Collections.Generic;
using System.Data;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Forms;

namespace Stores
{
    /// <summary>
    /// Owner's decision «المستلم = مركز الكلفة»: the recipient of an issue is its cost centre.
    /// Conversion of the old issues / issue returns (CostCentres.SyncFromRecipients): every one gets a cost centre,
    /// one new cost centre per recipient without one, purchase returns / receipts / stock count adjustments
    /// untouched, balances unchanged, a second run changes nothing and is fast; documents the old program saves
    /// later are linked at the next start. Saving with only a cost centre sets custom_no to its person (created
    /// when missing); the cost-centre statement = the person statement; renaming a cost centre renames its person.
    /// Screenshot of a new issue document. Usage: RecipientTests.exe &lt;copy.mdb&gt; &lt;out dir&gt;
    /// </summary>
    static class RecipientTestsMain
    {
        [DllImport("user32.dll")] static extern bool PrintWindow(IntPtr h, IntPtr hdc, uint flags);

        static int fails;
        static void Check(bool ok, string what) { Console.WriteLine((ok ? "PASS  " : "FAIL  ") + what); if (!ok) fails++; }
        static object Field(object o, string name) { return o.GetType().GetField(name, BindingFlags.NonPublic | BindingFlags.Instance).GetValue(o); }
        static void Pump(int ms) { DateTime end = DateTime.Now.AddMilliseconds(ms); while (DateTime.Now < end) { Application.DoEvents(); System.Threading.Thread.Sleep(10); } }
        static int ColOf(ReportData r, string header) { for (int i = 0; i < r.Cols.Count; i++) if (r.Cols[i].Header == header) return i; return -1; }
        static int N(string sql, params object[] a) { return Db.I(Db.Scalar(sql, a)); }

        /// <summary>All rows of a query as one text (to prove that nothing changed).</summary>
        static string Snap(string sql)
        {
            StringBuilder sb = new StringBuilder();
            foreach (DataRow r in Db.Query(sql).Rows) { foreach (object o in r.ItemArray) sb.Append(Db.S(o)).Append(','); sb.Append(';'); }
            return sb.ToString();
        }

        [STAThread]
        static int Main(string[] args)
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            try { int r = Run(args); Console.WriteLine(fails == 0 ? "ALL PASSED" : fails + " FAILED"); return r; }
            catch (Exception ex) { Console.WriteLine("FAIL  unexpected error: " + ex); return 1; }
        }

        static int Run(string[] args)
        {
            string outDir = args[1]; Directory.CreateDirectory(outDir);
            string err = Db.InitAccess(Path.GetFullPath(args[0])); if (err != null) { Console.WriteLine(err); return 1; }
            Db.EnsureAppTables();
            Check(Db.HasProj && Db.HasCcPerson, "project.p_no added (" + (Db.SchemaWarning ?? "ok") + ")");
            if (!Db.HasCcPerson) return 1;
            Session.UserNo = 1; Session.UserName = "1"; Session.LoadPerms(); Session.Advanced = true;
            Cache.LoadAll();
            Console.WriteLine("recalc fixed (historic mismatches): " + Posting.RecalcBalances());

            // ---------------------------------------------------------------- before
            string issueW = "(proj_no IS NULL OR proj_no = 0) AND " + Db.IssueOnly("");
            string returnW = "(proj_no IS NULL OR proj_no = 0) AND shop_type = 3 AND " + Db.NotCountAdj("");
            int issues0 = N("SELECT COUNT(*) FROM sale WHERE " + issueW), returns0 = N("SELECT COUNT(*) FROM shop WHERE " + returnW);
            int issuesAll = N("SELECT COUNT(*) FROM sale WHERE " + Db.IssueOnly(""));
            int projects0 = N("SELECT COUNT(*) FROM project"), persons0 = N("SELECT COUNT(*) FROM person");
            // expected new cost centres: distinct recipient names with no linked / same-name cost centre
            HashSet<int> recips = new HashSet<int>();
            foreach (DataRow r in Db.Query("SELECT DISTINCT custom_no FROM sale WHERE " + issueW).Rows) recips.Add(Db.I(r[0]));
            foreach (DataRow r in Db.Query("SELECT DISTINCT custom_no FROM shop WHERE " + returnW).Rows) recips.Add(Db.I(r[0]));
            HashSet<int> linkedPersons = new HashSet<int>(); HashSet<string> ccNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (DataRow r in Db.Query("SELECT p_no, proj_name FROM project").Rows) { if (Db.I(r[0]) > 0) linkedPersons.Add(Db.I(r[0])); ccNames.Add(Db.S(r[1]).Trim()); }
            HashSet<string> newNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            int bad = 0;
            foreach (int p in recips)
            {
                string nm = Cache.PersonName.ContainsKey(p) ? Cache.PersonName[p].Trim() : "";
                if (nm.Length == 0) { bad++; continue; }
                if (linkedPersons.Contains(p) || ccNames.Contains(nm)) continue;
                newNames.Add(nm);
            }
            Console.WriteLine("issues without a cost centre: " + issues0 + " of " + issuesAll + " • issue returns: " + returns0 + " • recipients: " + recips.Count + " (without a valid person: " + bad + ") • cost centres now: " + projects0);
            string purchRet = Snap("SELECT shop_no, custom_no, proj_no, doc_kind FROM sale WHERE doc_kind = 1 ORDER BY shop_no");
            string countSale = Snap("SELECT shop_no, custom_no, proj_no, doc_kind FROM sale WHERE doc_kind = 2 ORDER BY shop_no");
            string receipts = Snap("SELECT shop_no, custom_no, proj_no, shop_type, doc_kind FROM shop WHERE shop_type <> 3 OR doc_kind = 2 ORDER BY shop_no");
            string withProj = Snap("SELECT shop_no, custom_no, proj_no FROM sale WHERE proj_no > 0 ORDER BY shop_no");
            string persons = Snap("SELECT p_no, p_name, p_type FROM person ORDER BY p_no");
            double bal0 = Db.D(Db.Scalar("SELECT SUM(item_bal) FROM item_stor"));

            // ---------------------------------------------------------------- first conversion (as at program start)
            Db.Exec("DELETE FROM app_settings WHERE k = 'recipients_converted'");
            CostCentres.ConversionBackup = null;
            Stopwatch sw = Stopwatch.StartNew();
            CostCentres.SyncResult res = CostCentres.ConvertAtStart();
            long ms = sw.ElapsedMilliseconds;
            Check(res != null, "conversion ran");
            if (res == null) return 1;
            Console.WriteLine("FIRST CONVERSION: " + ms + " ms (sync " + res.Ms + " ms) • " + res);
            Check(CostCentres.ConversionBackup != null && File.Exists(CostCentres.ConversionBackup) && Path.GetFileName(CostCentres.ConversionBackup).StartsWith("قبل تحويل المستلمين إلى مراكز كلفة"),
                  "backup taken first: " + CostCentres.ConversionBackup);
            Check(Db.GetSetting("recipients_converted", "") == "1", "flag recipients_converted set");
            Check(N("SELECT COUNT(*) FROM app_log WHERE act = ?", "تحويل المستلمين إلى مراكز كلفة") == 1, "conversion logged");
            Check(N("SELECT COUNT(*) FROM sale WHERE " + issueW + " AND custom_no IN (SELECT p_no FROM person)") == 0, "every issue now has a cost centre (left without: " + N("SELECT COUNT(*) FROM sale WHERE " + issueW) + ", recipient missing)");
            Check(N("SELECT COUNT(*) FROM shop WHERE " + returnW + " AND custom_no IN (SELECT p_no FROM person)") == 0, "every issue return now has a cost centre");
            Check(res.Issues + res.Returns + res.Skipped == issues0 + returns0 && res.Recipients == recips.Count - bad, "counts: " + res.Issues + " issues, " + res.Returns + " returns, " + res.Recipients + " recipients");
            Check(res.Created == newNames.Count, "cost centres created = distinct recipients without one: " + res.Created + " = " + newNames.Count);
            Check(N("SELECT COUNT(*) FROM project") == projects0 + res.Created, "project rows +" + res.Created);
            Check(N("SELECT COUNT(*) FROM person") == persons0 && Snap("SELECT p_no, p_name, p_type FROM person ORDER BY p_no") == persons, "no person added or changed");
            int newBad = N("SELECT COUNT(*) FROM project WHERE p_no > 0 AND proj_code LIKE 'O-%' AND (cc_type <> 5 OR status <> 1)");
            Check(newBad == 0, "new cost centres: type «أخرى», active, automatic code O-…");
            Check(N("SELECT COUNT(*) FROM (SELECT proj_code FROM project WHERE proj_code IS NOT NULL GROUP BY proj_code HAVING COUNT(*) > 1)") == 0, "cost centre codes unique");
            Check(N("SELECT COUNT(*) FROM sale AS s INNER JOIN project AS p ON p.proj_no = s.proj_no WHERE p.p_no = s.custom_no AND " + Db.IssueOnly("s")) >= res.Issues,
                  "converted issues point to the cost centre linked to their recipient");
            Check(Snap("SELECT shop_no, custom_no, proj_no, doc_kind FROM sale WHERE doc_kind = 1 ORDER BY shop_no") == purchRet, "purchase returns untouched");
            Check(Snap("SELECT shop_no, custom_no, proj_no, doc_kind FROM sale WHERE doc_kind = 2 ORDER BY shop_no") == countSale, "stock count shortages untouched");
            Check(Snap("SELECT shop_no, custom_no, proj_no, shop_type, doc_kind FROM shop WHERE shop_type <> 3 OR doc_kind = 2 ORDER BY shop_no") == receipts, "receipts and count surpluses untouched");
            bool keptOld = true;
            foreach (string part in withProj.Split(new char[] { ';' }, StringSplitOptions.RemoveEmptyEntries))
            {
                string[] v = part.Split(',');
                if (N("SELECT proj_no FROM sale WHERE shop_no = ?", int.Parse(v[0])) != int.Parse(v[2])) keptOld = false;
            }
            Check(keptOld, "issues that already had a cost centre keep it");
            Check(Math.Abs(Db.D(Db.Scalar("SELECT SUM(item_bal) FROM item_stor")) - bal0) < 1e-6 && Posting.RecalcBalances() == 0, "balances unchanged (RecalcBalances = 0)");

            // ---------------------------------------------------------------- second start: nothing to do, fast
            CostCentres.ConversionBackup = null;
            sw.Restart();
            CostCentres.SyncResult res2 = CostCentres.ConvertAtStart();
            long ms2 = sw.ElapsedMilliseconds;
            Check(res2 != null && res2.Recipients == 0 && res2.Created == 0 && res2.Issues == 0 && CostCentres.ConversionBackup == null, "second start changes nothing, no new backup (" + (res2 == null ? "?" : res2.ToString()) + ")");
            Check(ms2 < 500, "second start check: " + ms2 + " ms");
            Check(N("SELECT COUNT(*) FROM project") == projects0 + res.Created, "no cost centre added by the second run");

            // ---------------------------------------------------------------- documents saved later by the old program
            Cache.LoadAll();
            string tag = DateTime.Now.ToString("HHmmss");
            int st = Cache.DefaultStore;
            int busy = Db.I(Db.Scalar("SELECT TOP 1 custom_no FROM sale WHERE " + Db.IssueOnly("") + " GROUP BY custom_no ORDER BY COUNT(*) DESC"));
            int busyCc = N("SELECT proj_no FROM project WHERE p_no = ?", busy);
            int newP = 0, oldA = 0, oldB = 0;
            Db.InTx(delegate(System.Data.Common.DbTransaction tx)
            {
                newP = Db.Insert(tx, "INSERT INTO person (p_name, p_bal_id, p_bal_dl, p_bal_id_f, p_bal_dl_f, p_type, p_sjl) VALUES (?, 0, 0, 0, 0, 3, 0)", "99999 موقع جديد " + tag);
                // like the old program: custom_no only, no proj_no / doc_kind
                oldA = Db.Insert(tx, "INSERT INTO sale (menu_no, menu_date, custom_no, pay_type, ksm_dl, wasl_dl, old_bal, ksm_per, stor, mony_no) VALUES (?, ?, ?, 1, 0, 0, 0, 0, ?, ?)", "Z" + tag.Substring(2) + "A", DateTime.Today, newP, st, "Z" + tag.Substring(2) + "AC");
                oldB = Db.Insert(tx, "INSERT INTO sale (menu_no, menu_date, custom_no, pay_type, ksm_dl, wasl_dl, old_bal, ksm_per, stor, mony_no) VALUES (?, ?, ?, 1, 0, 0, 0, 0, ?, ?)", "Z" + tag.Substring(2) + "B", DateTime.Today, busy, st, "Z" + tag.Substring(2) + "BC");
            });
            sw.Restart();
            CostCentres.SyncResult res3 = CostCentres.ConvertAtStart();
            Check(res3 != null && res3.Recipients == 2 && res3.Created == 1 && res3.Issues == 2, "next start links the 2 new documents of the old program, 1 new cost centre (" + (res3 == null ? "?" : res3.ToString()) + ", " + sw.ElapsedMilliseconds + " ms)");
            int newCc = N("SELECT proj_no FROM project WHERE p_no = ?", newP);
            Check(newCc > 0 && N("SELECT proj_no FROM sale WHERE shop_no = ?", oldA) == newCc && Db.S(Db.Scalar("SELECT proj_name FROM project WHERE proj_no = ?", newCc)) == "99999 موقع جديد " + tag,
                  "new recipient → new cost centre with its name");
            Check(busyCc > 0 && N("SELECT proj_no FROM sale WHERE shop_no = ?", oldB) == busyCc, "known recipient → its existing cost centre");
            Db.Exec("DELETE FROM sale WHERE shop_no IN (?, ?)", oldA, oldB);
            Cache.LoadAll();

            // ---------------------------------------------------------------- saving with only a cost centre
            int item = Db.I(Db.Scalar("SELECT TOP 1 s.item_no FROM item_stor AS s INNER JOIN item AS i ON i.item_no = s.item_no WHERE s.stor = ? AND s.item_bal >= 20 ORDER BY s.item_no DESC", st));
            double b0 = Cache.Stock(item, st);
            Doc none = new Doc(); none.Kind = DocKind.Issue; none.Store = st; none.Person = busy; none.Lines.Add(new DocLine { ItemNo = item, Qty = 1 });
            string msg = "";
            try { Posting.Save(none); } catch (UserError ue) { msg = ue.Message; }
            Check(msg.Contains("مركز الكلفة"), "issue without a cost centre refused (even with a person): «" + msg + "»");
            Doc iss = new Doc(); iss.Kind = DocKind.Issue; iss.Store = st; iss.Project = busyCc; iss.Lines.Add(new DocLine { ItemNo = item, Qty = 2 });
            Posting.Save(iss);
            Check(iss.Id > 0 && N("SELECT custom_no FROM sale WHERE shop_no = ?", iss.Id) == busy && iss.Person == busy, "new issue with only a cost centre: custom_no = its linked person (" + Cache.Person(busy) + ")");
            Doc ld = Posting.Load(DocKind.Issue, iss.Id);
            Check(ld.Project == busyCc && ld.Person == busy, "loading it shows its cost centre");
            Doc ret = new Doc(); ret.Kind = DocKind.Return; ret.Store = st; ret.Project = busyCc; ret.Lines.Add(new DocLine { ItemNo = item, Qty = 1, Price = 1 });
            Posting.Save(ret);
            Check(ret.Id > 0 && N("SELECT custom_no FROM shop WHERE shop_no = ?", ret.Id) == busy, "issue return with only a cost centre: custom_no = its linked person");

            int persons1 = N("SELECT COUNT(*) FROM person");
            int fresh = CostCentres.Save(0, "مركز بلا شخص " + tag, "", CostCentres.Department, 1, "", null, "");
            Check(N("SELECT COUNT(*) FROM project WHERE proj_no = ? AND p_no IS NULL", fresh) == 1, "a cost centre made in the program has no person yet");
            Doc iss2 = new Doc(); iss2.Kind = DocKind.Issue; iss2.Store = st; iss2.Project = fresh; iss2.Lines.Add(new DocLine { ItemNo = item, Qty = 1 });
            Posting.Save(iss2);
            int fp = N("SELECT p_no FROM project WHERE proj_no = ?", fresh);
            Check(fp > 0 && N("SELECT COUNT(*) FROM person") == persons1 + 1 && N("SELECT custom_no FROM sale WHERE shop_no = ?", iss2.Id) == fp, "saving creates its person in the same transaction and links it");
            DataRow pr = Db.Query("SELECT p_name, p_type, p_addrs, p_tel, p_note FROM person WHERE p_no = ?", fp).Rows[0];
            Check(Db.S(pr["p_name"]) == "مركز بلا شخص " + tag && Db.I(pr["p_type"]) == 3 && Cache.Person(fp) == Db.S(pr["p_name"]), "the person has the cost centre's name (p_type 3, in the cache)");
            Doc iss3 = new Doc(); iss3.Kind = DocKind.Issue; iss3.Store = st; iss3.Project = fresh; iss3.Lines.Add(new DocLine { ItemNo = item, Qty = 1 });
            Posting.Save(iss3);
            Check(N("SELECT COUNT(*) FROM person") == persons1 + 1 && N("SELECT custom_no FROM sale WHERE shop_no = ?", iss3.Id) == fp, "the next issue reuses the same person");
            Doc prt = new Doc(); prt.Kind = DocKind.PurchaseReturn; prt.Store = st; prt.Person = busy; prt.Lines.Add(new DocLine { ItemNo = item, Qty = 1 });
            Posting.Save(prt);
            Check(N("SELECT COUNT(*) FROM sale WHERE shop_no = ? AND custom_no = ? AND proj_no IS NULL AND doc_kind = 1", prt.Id, busy) == 1, "purchase return keeps its supplier and no cost centre");
            Check(Math.Abs(Cache.Stock(item, st) - (b0 - 2 + 1 - 1 - 1 - 1)) < 1e-9 && Posting.RecalcBalances() == 0, "balances posted and consistent");

            // ---------------------------------------------------------------- statements: cost centre = person
            // a recipient whose documents are only issues and issue returns (no receipts / purchase returns)
            int cmp = Db.I(Db.Scalar("SELECT TOP 1 h.custom_no FROM sale AS h WHERE " + Db.IssueOnly("h") +
                " AND h.custom_no NOT IN (SELECT custom_no FROM shop WHERE shop_type <> 3) AND h.custom_no NOT IN (SELECT custom_no FROM sale WHERE doc_kind > 0)" +
                " AND h.custom_no IN (SELECT custom_no FROM shop WHERE shop_type = 3) GROUP BY h.custom_no ORDER BY COUNT(*) DESC"));
            if (cmp == 0) cmp = Db.I(Db.Scalar("SELECT TOP 1 h.custom_no FROM sale AS h WHERE " + Db.IssueOnly("h") +
                " AND h.custom_no NOT IN (SELECT custom_no FROM shop WHERE shop_type <> 3) AND h.custom_no NOT IN (SELECT custom_no FROM sale WHERE doc_kind > 0) GROUP BY h.custom_no ORDER BY COUNT(*) DESC"));
            int cmpCc = N("SELECT proj_no FROM project WHERE p_no = ?", cmp);
            ReportParams pp = new ReportParams(); pp.ProjNo = cmpCc; pp.From = new DateTime(2000, 1, 1); pp.To = DateTime.Today; pp.Flag = true;
            ReportData ps = Reports.Get("project").Run(pp);
            ReportParams qp = new ReportParams(); qp.PersonNo = cmp; qp.From = pp.From; qp.To = pp.To; qp.Flag = false;
            ReportData qs = Reports.Get("person").Run(qp);
            List<string> a = new List<string>(), b = new List<string>();
            int pd = ColOf(ps, "التاريخ"), pn = ColOf(ps, "رقم السند"), pc = ColOf(ps, "الرمز"), po = ColOf(ps, "مُخرج"), pi = ColOf(ps, "مُعاد");
            int qd = ColOf(qs, "التاريخ"), qn = ColOf(qs, "الرقم"), qc = ColOf(qs, "الرمز"), qo = ColOf(qs, "مصروف له"), qi = ColOf(qs, "مُعاد / مُجهّز منه");
            foreach (object[] r in ps.Rows) a.Add(UI.Date((DateTime)r[pd]) + "|" + r[pn] + "|" + r[pc] + "|" + UI.Num(Db.D(r[po])) + "|" + UI.Num(Db.D(r[pi])));
            foreach (object[] r in qs.Rows) b.Add(UI.Date((DateTime)r[qd]) + "|" + r[qn] + "|" + r[qc] + "|" + UI.Num(Db.D(r[qo])) + "|" + UI.Num(Db.D(r[qi])));
            a.Sort(StringComparer.Ordinal); b.Sort(StringComparer.Ordinal);
            Check(cmpCc > 0 && a.Count > 0 && a.SequenceEqual(b), "«كشف مركز كلفة» of «" + Cache.Project(cmpCc) + "» lists the same " + a.Count + " lines as «كشف جهة / شخص» (" + b.Count + ")");
            File.WriteAllText(Path.Combine(outDir, "cc_statement.html"), Html.Render(ps));
            ReportParams bp = new ReportParams(); bp.From = pp.From; bp.To = pp.To;
            ReportData byp = Reports.Get("bypersons").Run(bp);
            Check(byp.Rows.Count > 0 && Reports.Get("bypersons").Title == "الإخراج حسب المستلمين", "«الإخراج حسب المستلمين» still works (" + byp.Rows.Count + " rows; name kept: «الإخراج حسب مراكز الكلفة» exists)");

            // ---------------------------------------------------------------- renaming a cost centre renames its person
            DataRow cr = Cache.Projects.Select("proj_no = " + busyCc)[0];
            string newName = Db.S(cr["proj_name"]) + " (معدل " + tag + ")";
            CostCentres.Save(busyCc, newName, Db.S(cr["proj_code"]), CostCentres.TypeOf(busyCc), Db.I(cr["status"]), Db.S(cr["client"]), null, Db.S(cr["notes"]));
            Check(Db.S(Db.Scalar("SELECT p_name FROM person WHERE p_no = ?", busy)) == newName && Cache.Person(busy) == newName && Cache.Project(busyCc) == newName,
                  "renaming the cost centre renames its person (the old program shows «" + Db.S(Db.Scalar("SELECT p_name FROM person WHERE p_no = ?", busy)) + "», cache «" + Cache.Person(busy) + "», cost centre «" + Cache.Project(busyCc) + "»)");

            // ---------------------------------------------------------------- the document screen
            Session.UserName = "خالد"; Session.Advanced = false;
            MainForm main = new MainForm();
            main.StartPosition = FormStartPosition.Manual; main.WindowState = FormWindowState.Normal;
            main.Location = new Point(-4000, -4000); main.Size = new Size(1366, 740); main.ShowInTaskbar = false;
            main.Show(); Pump(800);
            main.OpenDoc(DocKind.Issue, 0, 0); Pump(400);
            DocView dv = (DocView)typeof(MainForm).GetField("current", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(main);
            PersonPicker person = (PersonPicker)Field(dv, "person");
            ProjectPicker project = (ProjectPicker)Field(dv, "project");
            Label guide = (Label)Field(dv, "lblGuide");
            Check(person.Parent == null || !person.Visible, "new issue: no recipient (person) field");
            Check(project.Parent != null && project.Parent.Controls.OfType<Label>().Any(l => l.Text == "مركز الكلفة (المستلم)"), "one field «مركز الكلفة (المستلم)»");
            Check(guide.Text.Contains("① اختر مركز الكلفة (المستلم)   ② ابحث عن المادة"), "beginner guide: " + guide.Text.Trim());
            project.Value = busyCc;
            ((Func<int, double, double, bool>)Delegate.CreateDelegate(typeof(Func<int, double, double, bool>), dv, typeof(DocView).GetMethod("AddFromBar", BindingFlags.NonPublic | BindingFlags.Instance)))(item, 3, 0);
            Pump(300);
            string shot = Path.Combine(outDir, "new_issue.png");
            using (Bitmap bm = new Bitmap(main.Width, main.Height))
            {
                bool ok;
                using (Graphics g = Graphics.FromImage(bm)) { IntPtr hdc = g.GetHdc(); try { ok = PrintWindow(main.Handle, hdc, 2); } finally { g.ReleaseHdc(hdc); } }
                if (!ok) main.DrawToBitmap(bm, new Rectangle(0, 0, main.Width, main.Height));
                bm.Save(shot, ImageFormat.Png);
            }
            Console.WriteLine("screenshot: " + shot);
            typeof(DocView).GetField("dirty", BindingFlags.NonPublic | BindingFlags.Instance).SetValue(dv, false);   // no «unsaved changes?» question
            main.OpenDoc(DocKind.Return, 0, 0); Pump(300);
            DocView rv = (DocView)typeof(MainForm).GetField("current", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(main);
            Check(((PersonPicker)Field(rv, "person")).Parent == null, "new issue return: no person field either");
            main.OpenDoc(DocKind.Receipt, 0, 0); Pump(300);
            DocView cv = (DocView)typeof(MainForm).GetField("current", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(main);
            Check(((PersonPicker)Field(cv, "person")).Parent != null, "receipt keeps its supplier field");
            main.Dispose();
            Db.SignOff();
            return fails == 0 ? 0 : 1;
        }
    }
}
