using System;
using System.Collections.Generic;
using System.Data;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Windows.Forms;

namespace Stores
{
    /// <summary>
    /// Cost centres («مراكز الكلفة», table project + project.cc_type): column added to an old file, dialogs save the type,
    /// the document picker lists / filters / adds, issue and return charged to a department, statement and the
    /// «الإخراج حسب مراكز الكلفة» report with the type column and per-type chips, closing, the setting, SQL Server mode.
    /// Usage: CostCentreTests.exe &lt;copy.mdb&gt; &lt;out dir&gt; [&lt;sql instance, e.g. (localdb)\EngStoresTest&gt;]
    /// Screenshots (off-screen): cc_issue_list.png (issue document with the list open), cc_new_dialog.png.
    /// </summary>
    static class CostCentreTestsMain
    {
        static int fails = 0;
        static void Check(bool ok, string what) { Console.WriteLine((ok ? "PASS  " : "FAIL  ") + what); if (!ok) fails++; }

        static object Field(object o, string name) { return o.GetType().GetField(name, BindingFlags.NonPublic | BindingFlags.Instance).GetValue(o); }
        static object Call(object o, string name) { return o.GetType().GetMethod(name, BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Public).Invoke(o, null); }
        static int ColOf(ReportData r, string header) { for (int i = 0; i < r.Cols.Count; i++) if (r.Cols[i].Header == header) return i; return -1; }
        static bool RowsMatchCols(ReportData r) { foreach (object[] o in r.Rows) if (o.Length != r.Cols.Count) return false; return true; }
        static bool Throws(Action a) { try { a(); return false; } catch (UserError) { return true; } }

        /// <summary>The «+» quick dialog, filled and saved like the user would (no window shown).</summary>
        static int QuickAdd(string name, int type, string code)
        {
            using (CostCentreQuickDialog d = new CostCentreQuickDialog(name))
            {
                d.SetType(type);
                ((TextBox)Field(d, "txtCode")).Text = code ?? "";
                return d.SaveNew() ? d.NewId : 0;
            }
        }

        /// <summary>The full dialog (screen «مراكز الكلفة» → إضافة / تعديل).</summary>
        static int FullSave(int id, string name, int type, string client)
        {
            using (ProjectDialog d = new ProjectDialog(id))
            {
                if (name != null) ((TextBox)Field(d, "txtName")).Text = name;
                UI.SetSel((ComboBox)Field(d, "cmbType"), type);
                ((TextBox)Field(d, "txtClient")).Text = client ?? "";
                bool clientEnabled = ((TextBox)Field(d, "txtClient")).Enabled;
                if ((type == CostCentres.Project) != clientEnabled) { Check(false, "client field enabled only for type مشروع (type " + type + ")"); }
                bool ok = (bool)Call(d, "SaveCc");
                return ok ? (int)Field(d, "id") : 0;
            }
        }

        [System.Runtime.InteropServices.DllImport("user32.dll")] static extern bool PrintWindow(IntPtr hwnd, IntPtr hdc, uint flags);

        /// <summary>Window picture as composed on screen (PW_RENDERFULLCONTENT keeps the z-order of the list drawn over the
        /// document; DrawToBitmap paints children in reverse order). Works for an off-screen window.</summary>
        static void Shot(Control c, string path)
        {
            using (Bitmap b = new Bitmap(c.Width, c.Height))
            {
                bool ok;
                using (Graphics g = Graphics.FromImage(b)) { IntPtr dc = g.GetHdc(); ok = PrintWindow(c.Handle, dc, 2); g.ReleaseHdc(dc); }
                if (!ok) c.DrawToBitmap(b, new Rectangle(0, 0, c.Width, c.Height));
                b.Save(path, ImageFormat.Png);
            }
        }

        [STAThread]
        static int Main(string[] args)
        {
            Console.OutputEncoding = System.Text.Encoding.UTF8;
            Application.EnableVisualStyles();
            try { return Run(args); }
            catch (Exception ex) { Console.WriteLine("FAIL  unexpected error: " + ex); return 1; }
        }

        static int Run(string[] args)
        {
            string mdb = args[0], outDir = args[1], sql = args.Length > 2 ? args[2] : null;
            Directory.CreateDirectory(outDir);
            string err = Db.InitAccess(mdb);
            if (err != null) { Console.WriteLine("open failed: " + err); return 1; }

            // ---- old file: project.cc_type is added additively, old rows count as «مشروع»
            bool before = Db.TableExists("project") && Db.ColumnExists("project", "cc_type");
            Console.WriteLine("project.cc_type before EnsureAppTables: " + (before ? "exists" : "missing (old file)"));
            Db.EnsureAppTables();
            Check(Db.HasProj && Db.HasCcType && Db.ColumnExists("project", "cc_type"), "project.cc_type created (" + (Db.SchemaWarning ?? "ok") + ")");
            Check(before || Db.I(Db.Scalar("SELECT COUNT(*) FROM project WHERE cc_type IS NOT NULL")) == 0, "existing rows are not rewritten (cc_type stays NULL)");
            Db.EnsureAppTables();
            Check(Db.HasCcType && Db.SchemaWarning == null, "running EnsureAppTables again is harmless");
            Session.UserNo = 1; Session.UserName = "1"; Session.LoadPerms(); Session.Advanced = true;
            Cache.LoadAll();
            int oldRows = Cache.Projects.Rows.Count;
            Check(Cache.Projects.Columns.Contains("cc_type") && Cache.Projects.Columns.Contains("type_name"), "cache has cc_type + type_name");
            Check(Cache.Projects.Rows.Cast<DataRow>().All(r => Db.I(r["cc_type"]) >= 1 && Db.I(r["cc_type"]) <= 5), "every cached cost centre has a type 1..5 (" + oldRows + " existing)");
            Check(before || Cache.Projects.Rows.Cast<DataRow>().All(r => Db.S(r["type_name"]) == "مشروع"), "old rows show as «مشروع»");
            Console.WriteLine("recalc fixed (historic mismatches): " + Posting.RecalcBalances());
            Cache.LoadItems();

            // ---- create cost centres of different types
            string tag = DateTime.Now.ToString("HHmmss");
            int dept = QuickAdd("قسم الصيانة الكهربائية " + tag, CostCentres.Department, "");
            DataRow dr = CostCentres.Row(dept);
            Check(dept > 0 && dr != null && Db.I(dr["cc_type"]) == CostCentres.Department && Db.S(dr["type_name"]) == "قسم", "quick dialog «مركز كلفة جديد»: department saved with type قسم (" + dept + ")");
            Check(dr != null && Db.S(dr["proj_code"]).StartsWith("D-"), "automatic code for a department: " + (dr == null ? "" : Db.S(dr["proj_code"])));
            Check(Db.I(Db.Scalar("SELECT cc_type FROM project WHERE proj_no = ?", dept)) == 2, "cc_type = 2 stored in the data file");
            int maint = QuickAdd("صيانة المولدات " + tag, CostCentres.Maintenance, "MNT-" + tag);
            Check(maint > 0 && CostCentres.TypeOf(maint) == CostCentres.Maintenance && Db.S(CostCentres.Row(maint)["proj_code"]) == "MNT-" + tag, "quick dialog: maintenance with its own code");
            int proj = FullSave(0, "مشروع اختبار مراكز الكلفة " + tag, CostCentres.Project, "جهة مستفيدة");
            Check(proj > 0 && CostCentres.TypeOf(proj) == CostCentres.Project && Db.S(Db.Scalar("SELECT client FROM project WHERE proj_no = ?", proj)) == "جهة مستفيدة"
                  && !(Db.Scalar("SELECT start_date FROM project WHERE proj_no = ?", proj) is DBNull), "full dialog: project with client and start date");
            int adm = FullSave(0, "الإدارة العامة " + tag, CostCentres.Admin, "يجب ألا تُحفظ");
            Check(adm > 0 && CostCentres.TypeOf(adm) == CostCentres.Admin && Db.Scalar("SELECT client FROM project WHERE proj_no = ?", adm) is DBNull
                  && Db.Scalar("SELECT start_date FROM project WHERE proj_no = ?", adm) is DBNull, "full dialog: administration type stores no client / start date");
            Check(FullSave(adm, null, CostCentres.Other, "") == adm && CostCentres.TypeOf(adm) == CostCentres.Other, "edit: type changed to «أخرى»");
            Check(Throws(delegate { CostCentres.Save(0, "قسم الصيانة الكهربائية " + tag, "", 2, 1, null, null, null); }), "duplicate name rejected");
            Check(Throws(delegate { CostCentres.Save(0, "  ", "", 2, 1, null, null, null); }), "empty name rejected");
            Check(Throws(delegate { CostCentres.Save(0, "اسم آخر " + tag, "MNT-" + tag, 2, 1, null, null, null); }), "duplicate code rejected");
            Check(CostCentres.NextCode(CostCentres.Department) != Db.S(CostCentres.Row(dept)["proj_code"]), "next automatic code is a new one (" + CostCentres.NextCode(2) + ")");

            // ---- the picker used in documents, reports and reservations
            using (Form f = new Form())
            {
                f.StartPosition = FormStartPosition.Manual; f.Location = new Point(-3000, -3000); f.Size = new Size(900, 600); f.ShowInTaskbar = false;
                f.RightToLeft = RightToLeft.Yes;
                ProjectPicker pk = new ProjectPicker(320); pk.Location = new Point(400, 20);
                f.Controls.Add(pk); f.Show(); Application.DoEvents();
                TextBox t = (TextBox)Field(pk, "txt");
                pk.OpenList(true);
                List<int> all = pk.ListedIds;
                Check(pk.ListOpen && all.Contains(dept) && all.Contains(maint) && all.Contains(proj) && all.Contains(adm), "▼ lists the active cost centres (" + all.Count + ")");
                t.Text = "الكهربائية " + tag; pk.OpenList(false);
                List<int> fl = pk.ListedIds;
                Check(fl.Count(x => x != 0) == 1 && fl[0] == dept, "typing filters the list to the department (+ the «new» line)");
                t.Text = "صيانه المولدات " + tag; pk.OpenList(false);
                Check(pk.ListedIds.Contains(maint), "filter ignores ة/ه differences");
                t.Text = "مركز غير موجود " + tag; pk.OpenList(false);
                Check(pk.ListedIds.Count == 1 && pk.ListedIds[0] == 0, "unknown text offers «+ مركز كلفة جديد باسم …»");
                t.Text = "MNT-" + tag; Call(pk, "Resolve");
                Check(pk.Value == maint, "typing the code then leaving selects it");
                pk.Value = dept;
                Check(t.Text == Cache.Project(dept), "Value shows the name");
                CostCentres.SetClosed(maint, true);
                pk.OpenList(true);
                Check(!pk.ListedIds.Contains(maint), "a closed cost centre is not offered");
                Check(Cache.Project(maint).Length > 0 && Cache.FindProject("MNT-" + tag) == maint, "a closed cost centre keeps its name for old documents");
                CostCentres.SetClosed(maint, false);
                pk.OpenList(true);
                Check(pk.ListedIds.Contains(maint), "reopened cost centre is offered again");
                f.Close();
            }

            // ---- issue / return charged to the department
            int st = Cache.DefaultStore;
            int item = Db.I(Db.Scalar("SELECT TOP 1 s.item_no FROM item_stor AS s INNER JOIN item AS i ON i.item_no = s.item_no WHERE s.stor = ? AND s.item_bal >= 20 ORDER BY s.item_no DESC", st));
            int person = Db.I(Db.Scalar("SELECT TOP 1 custom_no FROM sale ORDER BY shop_no DESC"));
            Console.WriteLine("store=" + st + " item=" + item + " (" + Cache.ItemName(item) + ") stock=" + Cache.Stock(item, st));
            Doc bad = new Doc(); bad.Kind = DocKind.Issue; bad.Person = person; bad.Store = st; bad.Lines.Add(new DocLine { ItemNo = item, Qty = 1 });
            string msg = "";
            try { Posting.Save(bad); } catch (UserError ue) { msg = ue.Message; }
            Check(msg.Contains("مركز الكلفة"), "issue without a cost centre rejected: «" + msg + "»");
            Doc iss = new Doc(); iss.Kind = DocKind.Issue; iss.Person = person; iss.Project = dept; iss.Store = st; iss.Lines.Add(new DocLine { ItemNo = item, Qty = 6 });
            Posting.Save(iss);
            Check(iss.Id > 0 && Db.I(Db.Scalar("SELECT proj_no FROM sale WHERE shop_no = ?", iss.Id)) == dept, "issue charged to the department");
            Doc iss2 = new Doc(); iss2.Kind = DocKind.Issue; iss2.Person = person; iss2.Project = proj; iss2.Store = st; iss2.Lines.Add(new DocLine { ItemNo = item, Qty = 3 });
            Posting.Save(iss2);
            Doc ret = new Doc(); ret.Kind = DocKind.Return; ret.Person = person; ret.Project = dept; ret.Store = st; ret.Lines.Add(new DocLine { ItemNo = item, Qty = 2 });
            Posting.Save(ret);
            Check(ret.Id > 0, "return from the department saved");
            ReportData v = Reports.Voucher(Posting.Load(DocKind.Issue, iss.Id));
            Check(v.Info.Any(kv => kv.Key == "مركز الكلفة (المستلم)" && kv.Value == Cache.Project(dept)) && !v.Info.Any(kv => kv.Key == "المستلم"),
                  "issue voucher shows «مركز الكلفة (المستلم)» (one line, the cost centre is the recipient)");

            // ---- statement and report by cost centre and type
            ReportParams pp = new ReportParams(); pp.ProjNo = dept; pp.From = DateTime.Today.AddDays(-1); pp.To = DateTime.Today;
            ReportData ps = Reports.Get("project").Run(pp);
            int qc = ColOf(ps, "المُخرج لمركز الكلفة");
            bool found = false;
            foreach (object[] row in ps.Rows) if (qc >= 0 && Convert.ToString(row[2]) == Cache.ItemName(item) && Math.Abs(Convert.ToDouble(row[qc]) - 6) < 1e-9 && Math.Abs(Convert.ToDouble(row[qc + 1]) - 2) < 1e-9 && Math.Abs(Convert.ToDouble(row[qc + 2]) - 4) < 1e-9) found = true;
            Check(found, "«كشف مركز كلفة» for the department: issued 6, returned 2, net 4");
            Check(ps.Title.StartsWith("كشف مركز كلفة: ") && ps.Subtitle.Contains("النوع: قسم"), "statement title and type: " + ps.Title);
            Check(Reports.Get("project").Title == "كشف مركز كلفة" && Reports.Get("byprojects").Title == "الإخراج حسب مراكز الكلفة", "report names renamed");
            File.WriteAllText(Path.Combine(outDir, "cc_statement.html"), Html.Render(ps));

            ReportParams bp = new ReportParams(); bp.From = DateTime.Today.AddDays(-1); bp.To = DateTime.Today;
            ReportData by = Reports.Get("byprojects").Run(bp);
            int cn = ColOf(by, "مركز الكلفة"), ct = ColOf(by, "النوع"), cnet = ColOf(by, "الصافي");
            Check(cn >= 0 && ct == cn + 1 && RowsMatchCols(by), "by cost centres: «النوع» column next to the name, rows match columns");
            object[] dRow = by.Rows.FirstOrDefault(o => Convert.ToString(o[cn]) == Cache.Project(dept));
            object[] pRow = by.Rows.FirstOrDefault(o => Convert.ToString(o[cn]) == Cache.Project(proj));
            Check(dRow != null && Convert.ToString(dRow[ct]) == "قسم" && Math.Abs(Convert.ToDouble(dRow[cnet]) - 4) < 1e-9, "department row: type قسم, net 4");
            Check(pRow != null && Convert.ToString(pRow[ct]) == "مشروع" && Math.Abs(Convert.ToDouble(pRow[cnet]) - 3) < 1e-9, "project row: type مشروع, net 3");
            List<string> types = by.Rows.Select(o => Convert.ToString(o[ct])).ToList();
            List<int> order = types.Select(s => Array.IndexOf(CostCentres.TypeNames, s)).ToList();
            Check(order.SequenceEqual(order.OrderBy(x => x)), "rows grouped by type");
            KeyValuePair<string, string> chip = by.Summary.FirstOrDefault(kv => kv.Key.StartsWith("قسم ("));
            Check(chip.Key != null && chip.Value.Contains("الصافي"), "per-type subtotal chip: " + chip.Key + " = " + chip.Value);
            File.WriteAllText(Path.Combine(outDir, "cc_byprojects.html"), Html.Render(by));
            DataTable wt = ProjectsView.WithTotals();
            DataRow wr = wt.Select("proj_no = " + dept).FirstOrDefault();
            Check(wr != null && Math.Abs(Db.D(wr["t_net"]) - 4) < 1e-9 && Db.D(wr["t_docs"]) >= 1, "screen «مراكز الكلفة»: totals for the department (net " + (wr == null ? "" : Convert.ToString(wr["t_net"])) + ")");

            // ---- reservation for the maintenance centre + the reservations report
            Doc rs = new Doc(); rs.Kind = DocKind.Reserve; rs.Project = maint; rs.Store = st; rs.Lines.Add(new DocLine { ItemNo = item, Qty = 1 });
            Posting.Save(rs);
            ReportParams rp = new ReportParams(); rp.ProjNo = maint;
            ReportData rr = Reports.Get("reservations").Run(rp);
            Check(rr.Rows.Count == 1 && ColOf(rr, "مركز الكلفة") == 1 && rr.Subtitle.StartsWith("مركز الكلفة: "), "reservations report for a cost centre");
            Check(Posting.KindName(DocKind.Reserve) == "حجز مواد لمركز كلفة", "document kind name «حجز مواد لمركز كلفة»");

            // ---- screenshots: issue document with the list open, and the new cost centre dialog
            string shots = Path.Combine(outDir, "final"); Directory.CreateDirectory(shots);
            using (Form f = new Form())
            {
                f.StartPosition = FormStartPosition.Manual; f.Location = new Point(-3000, -3000); f.Size = new Size(1280, 720); f.ShowInTaskbar = false;
                f.RightToLeft = RightToLeft.Yes; f.Font = Theme.Font; f.BackColor = Theme.Bg;
                DocView dv = new DocView(null, DocKind.Issue, 0, 0); dv.Dock = DockStyle.Fill;
                f.Controls.Add(dv); f.Show(); Application.DoEvents();
                ProjectPicker pk = (ProjectPicker)Field(dv, "project");
                pk.FocusText(); Application.DoEvents();
                ((TextBox)Field(pk, "txt")).Text = "";
                pk.OpenList(true); Application.DoEvents();
                Check(pk.ListOpen, "issue document: the cost centre list opens");
                f.Refresh(); Application.DoEvents();
                string p1 = Path.Combine(shots, "cc_issue_list.png"); Shot(f, p1);
                Console.WriteLine("screenshot: " + p1);
                ((TextBox)Field(pk, "txt")).Text = "صيانة"; pk.OpenList(false); Application.DoEvents(); f.Refresh();
                string p1b = Path.Combine(shots, "cc_issue_list_filtered.png"); Shot(f, p1b);
                Console.WriteLine("screenshot: " + p1b);
                f.Close();
            }
            using (CostCentreQuickDialog d = new CostCentreQuickDialog("ورشة النجارة"))
            {
                d.StartPosition = FormStartPosition.Manual; d.Location = new Point(-3000, -3000);
                d.SetType(CostCentres.Department);
                d.Show(); Application.DoEvents(); d.Refresh();
                string p2 = Path.Combine(shots, "cc_new_dialog.png"); Shot(d, p2);
                Console.WriteLine("screenshot: " + p2);
                d.Close();
            }
            using (ProjectDialog d = new ProjectDialog(dept))
            {
                d.StartPosition = FormStartPosition.Manual; d.Location = new Point(-3000, -3000);
                d.Show(); Application.DoEvents(); d.Refresh();
                string p3 = Path.Combine(shots, "cc_edit_dialog.png"); Shot(d, p3);
                Console.WriteLine("screenshot: " + p3);
                d.Close();
            }
            using (Form f = new Form())
            {
                f.StartPosition = FormStartPosition.Manual; f.Location = new Point(-3000, -3000); f.Size = new Size(1280, 600); f.ShowInTaskbar = false;
                f.RightToLeft = RightToLeft.Yes; f.Font = Theme.Font; f.BackColor = Theme.Bg;
                ProjectsView pv = new ProjectsView(); pv.Dock = DockStyle.Fill;
                f.Controls.Add(pv); f.Show(); Application.DoEvents(); f.Refresh();
                DataGridView g = (DataGridView)Field(pv, "grid");
                Check(g.Columns["type"].HeaderText == "النوع" && g.Rows.Count >= 4, "screen «مراكز الكلفة»: " + g.Rows.Count + " rows with a type column");
                string p4 = Path.Combine(shots, "cc_view.png"); Shot(f, p4);
                Console.WriteLine("screenshot: " + p4);
                f.Close();
            }

            // ---- SQL Server: migration keeps cc_type, saving and reports work there too
            if (sql != null)
            {
                int withType = Db.I(Db.Scalar("SELECT COUNT(*) FROM project WHERE cc_type IS NOT NULL"));
                Db.Close();
                const string dbName = "EngStoresCostCentreTest";
                err = Db.InitSql(Db.BuildSqlConnStr(sql, "master", "", "", true));
                if (err != null) { Console.WriteLine("sql open failed: " + err); return 1; }
                Db.Exec("IF DB_ID('" + dbName + "') IS NOT NULL BEGIN ALTER DATABASE " + dbName + " SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE " + dbName + " END");
                Db.Exec("CREATE DATABASE " + dbName);
                err = Db.InitSql(Db.BuildSqlConnStr(sql, dbName, "", "", true));
                if (err != null) { Console.WriteLine("sql open failed: " + err); return 1; }
                List<string> rep = Migrate.Copy(mdb, delegate(string s) { });
                Check(rep.TrueForAll(l => l.Contains("✓") || l.Contains("غير موجود")), "migration: every table row count matches");
                Check(Db.I(Db.Scalar("SELECT cc_type FROM project WHERE proj_no = ?", dept)) == 2 && Db.I(Db.Scalar("SELECT COUNT(*) FROM project WHERE cc_type IS NOT NULL")) == withType,
                      "migration keeps cc_type (" + withType + " rows with a type)");
                Db.EnsureAppTables(); Cache.LoadAll();
                Check(Db.HasCcType && CostCentres.TypeOf(dept) == CostCentres.Department, "SQL Server mode: cache reads the type");
                int sq = QuickAdd("قسم المشتريات " + tag, CostCentres.Department, "");
                Check(sq > 0 && CostCentres.TypeOf(sq) == CostCentres.Department && Db.S(CostCentres.Row(sq)["proj_code"]).StartsWith("D-"), "SQL Server mode: quick dialog saves a department with an automatic code");
                ReportData by2 = Reports.Get("byprojects").Run(bp);
                object[] d2 = by2.Rows.FirstOrDefault(o => Convert.ToString(o[cn]) == Cache.Project(dept));
                Check(d2 != null && Convert.ToString(d2[ct]) == "قسم" && Math.Abs(Convert.ToDouble(d2[cnet]) - 4) < 1e-9, "SQL Server mode: report by cost centre and type");
                // a server database made before this version: the column is added by EnsureAppTables
                Db.Exec("ALTER TABLE project DROP COLUMN cc_type");
                Db.EnsureAppTables();
                Check(Db.HasCcType && Db.S(Db.Scalar("SELECT DATA_TYPE FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME = 'project' AND COLUMN_NAME = 'cc_type'")) == "int",
                      "old server database gets project.cc_type INT (" + (Db.SchemaWarning ?? "ok") + ")");
                Cache.LoadProjects();
                Check(CostCentres.TypeOf(dept) == CostCentres.Project, "after re-adding the column old rows count as «مشروع»");
                Db.Close();
                Db.InitSql(Db.BuildSqlConnStr(sql, "master", "", "", true));
                Db.Exec("ALTER DATABASE " + dbName + " SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE " + dbName);
                Db.Close();
                err = Db.InitAccess(mdb);
                Db.EnsureAppTables(); Cache.LoadAll();
            }

            // ---- clean up (copy only)
            Posting.Delete(Posting.Load(DocKind.Reserve, rs.Id));
            Posting.Delete(Posting.Load(DocKind.Return, ret.Id));
            Posting.Delete(Posting.Load(DocKind.Issue, iss2.Id));
            Posting.Delete(Posting.Load(DocKind.Issue, iss.Id));
            Db.InTx(delegate(System.Data.Common.DbTransaction tx)
            {
                Db.X(tx, "DELETE FROM reserve_menu WHERE seq = ?", rs.Id);
                Db.X(tx, "DELETE FROM reserve WHERE res_no = ?", rs.Id);
                foreach (int pid in new[] { dept, maint, proj, adm }) Db.X(tx, "DELETE FROM project WHERE proj_no = ?", pid);
            });
            Check(Posting.RecalcBalances() == 0, "balances consistent after the test");

            Console.WriteLine(fails == 0 ? "\nALL TESTS PASSED" : "\nFAILURES: " + fails);
            Db.Close();
            return fails;
        }
    }
}
