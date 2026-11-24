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
    /// Material requisitions «طلبات صرف المواد»: tables and the permission row added to a copy of real data, draft → send →
    /// approved automatically under «حد الاعتماد» / waiting over it → approve / reject by a permitted user, refused without the
    /// permission and for one's own request → «صرف الطلب» pre-fills a new issue → partial save = «صُرف جزئيا» → the rest =
    /// «مكتمل»; deleting an issue reopens the request; balances stay right; print + report; SQL Server (LocalDB) too.
    /// Usage: RequisitionTests.exe &lt;copy.mdb&gt; &lt;out dir&gt; [&lt;sql instance, e.g. (localdb)\EngStoresTest&gt;]
    /// Screenshots (off-screen): mreq_approval_list.png, mreq_pending.png, mreq_done.png, mreq_issue_prefilled.png.
    /// </summary>
    static class RequisitionTestsMain
    {
        static int fails = 0;
        static void Check(bool ok, string what) { Console.WriteLine((ok ? "PASS  " : "FAIL  ") + what); if (!ok) fails++; }
        static bool Throws(Action a) { try { a(); return false; } catch (UserError) { return true; } }
        static object Field(object o, string name) { return o.GetType().GetField(name, BindingFlags.NonPublic | BindingFlags.Instance).GetValue(o); }
        static object Call(object o, string name) { return o.GetType().GetMethod(name, BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Public, null, Type.EmptyTypes, null).Invoke(o, null); }

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

        static void As(int user, string name) { Session.UserNo = user; Session.UserName = name; Session.LoadPerms(); }

        static double Bal(int item, int store) { return Db.D(Db.Scalar("SELECT SUM(item_bal) FROM item_stor WHERE item_no = ? AND stor = ?", item, store)); }

        static MreqDoc NewReq(int proj, int store, string by, params object[] lines)
        {
            MreqDoc d = new MreqDoc(); d.Project = proj; d.Store = store; d.RequestedBy = by;
            for (int i = 0; i < lines.Length; i += 2) d.Lines.Add(new MreqLine { ItemNo = (int)lines[i], Qty = Convert.ToDouble(lines[i + 1]) });
            Requisitions.Save(d);
            return d;
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

            // ---- schema: tables + permission row added additively
            bool before = Db.TableExists("mreq");
            Console.WriteLine("mreq before EnsureAppTables: " + (before ? "exists" : "missing (old file)"));
            Db.EnsureAppTables();
            Check(Requisitions.Ready && Db.TableExists("mreq_menu") && Db.TableExists("mreq_issue"), "tables mreq / mreq_menu / mreq_issue created (" + (Db.SchemaWarning ?? "ok") + ")");
            DataTable pr = Db.Query("SELECT command_name, user1, user2 FROM disply_command WHERE command_no = ?", Requisitions.PermApprove);
            Check(pr.Rows.Count == 1 && Db.S(pr.Rows[0][0]) == Requisitions.PermName && Db.B(pr.Rows[0][1]) && !Db.B(pr.Rows[0][2]), "permission row 82 «اعتماد طلبات الصرف»: user1 yes, others no");
            Db.EnsureAppTables();
            Check(Db.SchemaWarning == null && Db.I(Db.Scalar("SELECT COUNT(*) FROM disply_command WHERE command_no = ?", Requisitions.PermApprove)) == 1, "running EnsureAppTables again is harmless");

            As(1, "1"); Session.Advanced = true;
            Cache.LoadAll();
            Console.WriteLine("recalc fixed (historic mismatches): " + Posting.RecalcBalances());
            Cache.LoadItems();

            // ---- test data: a cost centre, a recipient, a store with two items that have stock and a known cost
            string tag = DateTime.Now.ToString("HHmmss");
            int proj = 0;
            Db.InTx(delegate(System.Data.Common.DbTransaction tx)
            {
                proj = Db.Insert(tx, "INSERT INTO project (proj_code, proj_name, status, cc_type) VALUES (?, ?, 1, 1)", "MR-" + tag, "مشروع اختبار طلبات الصرف " + tag);
            });
            Cache.LoadProjects();
            int person = Db.I(Cache.Persons.Rows[0]["p_no"]);
            int store = 0, it1 = 0, it2 = 0;
            foreach (DataRow r in Db.Query("SELECT item_no, stor, item_bal FROM item_stor WHERE item_bal >= 30 ORDER BY item_bal DESC").Rows)
            {
                int it = Db.I(r[0]), st = Db.I(r[1]);
                DataRow ir = Cache.Item(it);
                if (ir == null || Db.B(ir["freez"]) || Requisitions.LastCost(it) <= 0 || Reservations.ForOthers(it, st, proj) > 0) continue;
                if (store == 0) { store = st; it1 = it; continue; }
                if (st == store && it != it1) { it2 = it; break; }
            }
            Check(store > 0 && it1 > 0 && it2 > 0, "found a store with two items in stock with a known cost (store " + store + ", items " + it1 + ", " + it2 + ")");
            double c1 = Requisitions.LastCost(it1), c2 = Requisitions.LastCost(it2);
            double b1 = Bal(it1, store), b2 = Bal(it2, store);

            // ---- draft
            Check(Throws(delegate { NewReq(0, store, "x", it1, 1); }), "a request without a cost centre is refused");
            Check(Throws(delegate { NewReq(proj, store, "x"); }), "a request without items is refused");
            MreqDoc a = NewReq(proj, store, "مهندس الموقع", it1, 1, it1, 1);
            MreqDoc al = Requisitions.Load(a.No);
            Check(a.No > 0 && al.Status == Requisitions.Draft && al.Lines.Count == 1 && Math.Abs(al.Lines[0].Qty - 2) < 1e-9 && al.MenuNo.Length > 0,
                  "draft saved (no " + al.MenuNo + "), the same item twice is merged into one line of 2");
            int unk;
            double va = Requisitions.Value(al, out unk);
            Check(Math.Abs(va - 2 * c1) < 1e-6 && unk == 0, "value = qty × last receipt cost (" + va + ")");

            // ---- send: auto-approved under the limit
            Requisitions.SetApprovalLimit(va * 10);
            Check(Math.Abs(Requisitions.ApprovalLimit - va * 10) < 1e-3, "setting «حد الاعتماد» stored in app_settings (" + Db.GetSetting("approval_limit_value", "") + ")");
            int sa = Requisitions.Send(a.No);
            al = Requisitions.Load(a.No);
            Check(sa == Requisitions.Approved && al.Status == Requisitions.Approved && al.ApprovedBy.StartsWith("تلقائي") && Math.Abs(al.Value - va) < 1e-6, "under the limit: approved automatically when sent");
            Check(Db.I(Db.Scalar("SELECT COUNT(*) FROM app_log WHERE act = ? AND details LIKE ?", "طلب صرف: موافقة تلقائية", "رقم " + al.MenuNo + " %")) == 1, "status change logged (موافقة تلقائية)");
            Check(Throws(delegate { Requisitions.Send(a.No); }), "an approved request cannot be sent again");

            // ---- send: waiting over the limit
            MreqDoc b = NewReq(proj, store, "مهندس الموقع", it1, 6, it2, 3);
            double vb = Requisitions.Value(Requisitions.Load(b.No), out unk);
            Requisitions.SetApprovalLimit(vb / 2);
            Check(Requisitions.Send(b.No) == Requisitions.Pending && Requisitions.Load(b.No).Status == Requisitions.Pending, "over the limit: waits for approval (value " + UI.Num(vb) + ")");
            Check(Requisitions.PendingForMe() >= 1, "admin sees «بانتظار موافقتك» (" + Requisitions.PendingForMe() + ")");
            Check(Throws(delegate { Requisitions.Save(Requisitions.Load(b.No)); }), "a request waiting for approval cannot be edited");

            // ---- approval permission
            Db.Exec("UPDATE disply_command SET user2 = ?, user3 = ? WHERE command_no = ?", false, true, Requisitions.PermApprove);
            Db.Exec("UPDATE disply_command SET user2 = ?, user3 = ? WHERE command_no = ?", true, true, Session.IssView);
            As(2, "مستخدم بلا صلاحية " + tag);
            Check(!Requisitions.CanApprove && Requisitions.PendingForMe() == 0, "user without the permission: no approvals waiting for him");
            Check(Throws(delegate { Requisitions.Approve(b.No); }) && Throws(delegate { Requisitions.Reject(b.No, "لا"); }), "approve / reject refused without «اعتماد طلبات الصرف»");
            Check(Requisitions.Load(b.No).Status == Requisitions.Pending, "status unchanged after the refusal");
            As(3, "معتمد " + tag);
            Check(Requisitions.CanApprove, "user 3 with the permission (row 82 in disply_command)");
            Requisitions.Approve(b.No);
            MreqDoc bl = Requisitions.Load(b.No);
            Check(bl.Status == Requisitions.Approved && bl.ApprovedBy == "معتمد " + tag && bl.ApprovedAt > DateTime.MinValue, "approved by the permitted user (" + bl.ApprovedBy + ")");
            Check(Db.I(Db.Scalar("SELECT COUNT(*) FROM app_log WHERE act = ? AND user_name = ?", "طلب صرف: موافقة", "معتمد " + tag)) == 1, "approval logged with the approver's name");

            // separation of duties: the approver's own request
            MreqDoc c = NewReq(proj, store, "معتمد", it2, 2);
            Requisitions.Send(c.No);
            Check(Requisitions.Load(c.No).Status == Requisitions.Pending, "own request (over the limit) waits");
            Check(Throws(delegate { Requisitions.Approve(c.No); }), "an approver cannot approve his own request");
            Check(Requisitions.List(-2, "").Select("mreq_no = " + c.No).Length == 0 && Requisitions.List(-2, "").Select("mreq_no = " + b.No).Length == 0, "«بانتظار موافقتك» does not list one's own request");
            As(1, "1");
            Check(Throws(delegate { Requisitions.Reject(c.No, "  "); }), "reject needs a reason");
            Requisitions.Reject(c.No, "الكمية أكبر من الحاجة");
            MreqDoc cl = Requisitions.Load(c.No);
            Check(cl.Status == Requisitions.Rejected && cl.RejectReason == "الكمية أكبر من الحاجة", "rejected with the reason");
            Requisitions.Save(cl);
            Check(Requisitions.Load(c.No).Status == Requisitions.Draft && Db.I(Db.Scalar("SELECT COUNT(*) FROM app_log WHERE act = ? AND details LIKE ?", "طلب صرف: إعادة إلى مسودة", "رقم " + cl.MenuNo + " %")) == 1,
                  "a rejected request edited again becomes a draft (logged)");
            Requisitions.Cancel(c.No);
            Check(Requisitions.Load(c.No).Status == Requisitions.Cancelled, "draft cancelled → «ملغى»");

            // ---- «صرف الطلب»: a new issue pre-filled with the remaining quantities, saved in two parts
            Check(Requisitions.Remaining(b.No).Count == 2, "remaining before issuing: 2 lines");
            string shots = Path.Combine(outDir, "shots"); Directory.CreateDirectory(shots);
            int iss1 = 0, iss2 = 0;
            using (Form f = Host(1280, 720))
            {
                DocView dv = new DocView(null, DocKind.Issue, 0, 0); dv.Dock = DockStyle.Fill;
                f.Controls.Add(dv); f.Show(); Application.DoEvents();
                dv.Prefill(proj, store, Requisitions.Remaining(b.No), b.No);
                Application.DoEvents();
                DataGridView g = (DataGridView)Field(dv, "grid");
                List<DataGridViewRow> rows = g.Rows.Cast<DataGridViewRow>().Where(r => !r.IsNewRow).ToList();
                Func<int, DataGridViewRow> rowOf = delegate(int it) { return rows.FirstOrDefault(r => Db.I(r.Cells["itemNo"].Value) == it); };
                Check(rows.Count == 2 && rowOf(it1) != null && Db.D(rowOf(it1).Cells["qty"].Value) == 6 && Db.D(rowOf(it2).Cells["qty"].Value) == 3,
                      "issue pre-filled: 2 lines with the requested quantities");
                Check(((ProjectPicker)Field(dv, "project")).Value == proj && UI.Sel((ComboBox)Field(dv, "cmbStore")) == store, "issue pre-filled: cost centre and store of the request");
                ((PersonPicker)Field(dv, "person")).Value = person;
                f.Refresh(); Application.DoEvents();
                Shot(f, Path.Combine(shots, "mreq_issue_prefilled.png"));
                rowOf(it1).Cells["qty"].Value = 4.0;                         // the store has only part of it now
                Check((bool)Call(dv, "Save"), "partial issue saved");
                iss1 = ((Doc)Field(dv, "doc")).Id;
                bl = Requisitions.Load(b.No);
                Check(bl.Status == Requisitions.Partial && Math.Abs(bl.Lines.First(l => l.ItemNo == it1).Issued - 4) < 1e-9 && Math.Abs(bl.Lines.First(l => l.ItemNo == it2).Issued - 3) < 1e-9,
                      "after the partial issue: «صُرف جزئيا», issued 4 + 3");
                List<KeyValuePair<int, double>> rest = Requisitions.Remaining(b.No);
                Check(rest.Count == 1 && rest[0].Key == it1 && Math.Abs(rest[0].Value - 2) < 1e-9, "remaining: 2 of the first item");
                // the same screen: «صرف الطلب» again for the rest
                dv.Prefill(proj, store, rest, b.No);
                Application.DoEvents();
                ((PersonPicker)Field(dv, "person")).Value = person;
                Check(((Doc)Field(dv, "doc")).Id == 0 && g.Rows.Cast<DataGridViewRow>().Count(r => !r.IsNewRow) == 1, "second issue: a new document with the remaining line");
                Check((bool)Call(dv, "Save"), "rest issued");
                iss2 = ((Doc)Field(dv, "doc")).Id;
                // saving the same issue again (edit) keeps one link
                Check((bool)Call(dv, "Save"), "saving the same issue again (edit)");
                f.Close();
            }
            bl = Requisitions.Load(b.No);
            Check(bl.Status == Requisitions.Done && Requisitions.Remaining(b.No).Count == 0, "after the rest: «مكتمل»");
            Check(Db.I(Db.Scalar("SELECT COUNT(*) FROM mreq_issue WHERE mreq_no = ?", b.No)) == 2 && iss1 > 0 && iss2 > 0 && iss1 != iss2, "two issues linked to the request");
            Check(Math.Abs(Bal(it1, store) - (b1 - 6)) < 1e-6 && Math.Abs(Bal(it2, store) - (b2 - 3)) < 1e-6, "store balances went down by exactly the issued quantities");
            Check(Posting.RecalcBalances() == 0, "balances consistent (RecalcBalances() == 0)");
            Check(Db.I(Db.Scalar("SELECT COUNT(*) FROM app_log WHERE act = ? AND details LIKE ?", "طلب صرف: صُرف جزئيا", "رقم " + bl.MenuNo + " %")) == 1
                  && Db.I(Db.Scalar("SELECT COUNT(*) FROM app_log WHERE act = ? AND details LIKE ?", "طلب صرف: مكتمل", "رقم " + bl.MenuNo + " %")) == 1, "partial / complete logged");

            // deleting an issue reopens the request; closing it finishes it without the rest
            Posting.Delete(Posting.Load(DocKind.Issue, iss2));
            Requisitions.RecalcOpen(); Requisitions.Recalc(b.No);
            bl = Requisitions.Load(b.No);
            Check(bl.Status == Requisitions.Partial && Math.Abs(bl.Lines.First(l => l.ItemNo == it1).Issued - 4) < 1e-9, "issue deleted → the request is «صُرف جزئيا» again");
            Requisitions.Close(b.No);
            Requisitions.Recalc(b.No);
            bl = Requisitions.Load(b.No);
            Check(bl.Status == Requisitions.Done && bl.ClosedBy == "1", "«إغلاق الطلب» → «مكتمل» and stays so after a recalculation");
            Check(Throws(delegate { Requisitions.Cancel(b.No); }), "a finished request cannot be cancelled");
            Requisitions.Cancel(a.No);
            Check(Requisitions.Load(a.No).Status == Requisitions.Cancelled, "an approved request with nothing issued can be cancelled");

            // ---- print + report
            ReportData pd = Requisitions.Print(bl);
            Check(pd.Signatures && pd.SigLabels.SequenceEqual(new[] { "طالب المواد", "المعتمد", "أمين المخزن" }) && pd.Rows.Count == 2 && pd.Info.Any(kv => kv.Key == "المعتمد"),
                  "printed request: lines, approver, signatures «طالب المواد / المعتمد / أمين المخزن»");
            File.WriteAllText(Path.Combine(outDir, "mreq_print.html"), Html.Render(pd));
            ReportDef rd = Reports.Get("mreq");
            Check(rd != null && rd.Group == "الإخراج" && rd.Title == "طلبات الصرف", "report «طلبات الصرف» in group «الإخراج»");
            ReportParams rp = new ReportParams(); rp.ProjNo = proj; rp.From = DateTime.Today.AddDays(-1);
            ReportData rr = rd.Run(rp);
            Check(rr.Rows.Count == 4 && rr.Rows.All(o => o.Length == rr.Cols.Count), "report: 4 lines for the test cost centre (" + rr.Rows.Count + ")");
            object[] r1 = rr.Rows.FirstOrDefault(o => Convert.ToString(o[1]) == bl.MenuNo && Convert.ToString(o[7]) == Cache.ItemName(it1));
            Check(r1 != null && Convert.ToString(r1[5]) == "مكتمل" && Convert.ToDouble(r1[9]) == 6 && Convert.ToDouble(r1[10]) == 4 && Convert.ToDouble(r1[11]) == 0, "report: requested 6 / issued 4 / remaining 0 (closed)");
            File.WriteAllText(Path.Combine(outDir, "mreq_report.html"), Html.Render(rr));

            // ---- users screen lists the new permission
            using (UsersView uv = new UsersView())
            {
                CheckedListBox cl2 = (CheckedListBox)Field(uv, "perms");
                Check(cl2.Items.Cast<object>().Any(o => Convert.ToString(o) == Requisitions.PermName), "Users screen lists «اعتماد طلبات الصرف»");
            }

            // ---- screenshots: approval list, a pending request for the approver, a finished one
            As(2, "مستخدم بلا صلاحية " + tag);
            MreqDoc d = NewReq(proj, store, "مشرف الصيانة", it1, 5, it2, 2);
            Requisitions.Send(d.No);
            As(1, "1");
            FlowLayoutPanel chipHost = new FlowLayoutPanel();
            Check(RequisitionsView.AddAlert(chipHost, null) && chipHost.Controls.Count == 1 && chipHost.Controls[0].Text.Contains("بانتظار موافقتك"), "home screen chip «طلبات صرف بانتظار موافقتك»");
            using (Form f = Host(1280, 640))
            {
                RequisitionsView lv = new RequisitionsView(null); lv.Dock = DockStyle.Fill;
                f.Controls.Add(lv); f.Show(); Application.DoEvents(); f.Refresh();
                DataGridView g = (DataGridView)Field(lv, "grid");
                Check(UI.Sel((ComboBox)Field(lv, "cmbStatus")) == -2 && g.Rows.Count >= 1, "approver opens on «بانتظار موافقتك» (" + g.Rows.Count + " rows)");
                Shot(f, Path.Combine(shots, "mreq_approval_list.png"));
                f.Close();
            }
            using (Form f = Host(1280, 640))
            {
                RequisitionView ev = new RequisitionView(null, d.No); ev.Dock = DockStyle.Fill;
                f.Controls.Add(ev); f.Show(); Application.DoEvents(); f.Refresh();
                Check(((Button)Field(ev, "btnApprove")).Visible && ((Button)Field(ev, "btnReject")).Visible && !((Button)Field(ev, "btnSave")).Visible, "pending request: approver sees «موافقة / رفض», no editing");
                Shot(f, Path.Combine(shots, "mreq_pending.png"));
                f.Close();
            }
            using (Form f = Host(1280, 640))
            {
                RequisitionView ev = new RequisitionView(null, b.No); ev.Dock = DockStyle.Fill;
                f.Controls.Add(ev); f.Show(); Application.DoEvents(); f.Refresh();
                Shot(f, Path.Combine(shots, "mreq_done.png"));
                f.Close();
            }
            using (Form f = Host(1280, 640))
            {
                RequisitionView ev = new RequisitionView(null, 0); ev.Dock = DockStyle.Fill;
                f.Controls.Add(ev); f.Show(); Application.DoEvents();
                ((ProjectPicker)Field(ev, "project")).Value = proj; UI.SetSel((ComboBox)Field(ev, "cmbStore"), store);
                ev.AddLine(it1, 3); ev.AddLine(it2, 1);
                ((ItemPicker)Field(ev, "item")).Value = it1; Call(ev, "ShowItemAvail");
                Application.DoEvents(); f.Refresh();
                Check(((Button)Field(ev, "btnSend")).Visible && ((DataGridView)Field(ev, "grid")).Rows.Count == 2, "new request screen: lines and «إرسال للموافقة»");
                Shot(f, Path.Combine(shots, "mreq_new.png"));
                ev.GetType().GetField("dirty", BindingFlags.NonPublic | BindingFlags.Instance).SetValue(ev, false);
                f.Close();
            }

            // ---- SQL Server: migration copies the tables, the whole flow works there
            if (sql != null)
            {
                int nReq = Db.I(Db.Scalar("SELECT COUNT(*) FROM mreq"));
                Db.Close();
                const string dbName = "EngStoresRequisitionTest";
                err = Db.InitSql(Db.BuildSqlConnStr(sql, "master", "", "", true));
                if (err != null) { Console.WriteLine("sql open failed: " + err); return 1; }
                Db.Exec("IF DB_ID('" + dbName + "') IS NOT NULL BEGIN ALTER DATABASE " + dbName + " SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE " + dbName + " END");
                Db.Exec("CREATE DATABASE " + dbName);
                err = Db.InitSql(Db.BuildSqlConnStr(sql, dbName, "", "", true));
                if (err != null) { Console.WriteLine("sql open failed: " + err); return 1; }
                List<string> rep = Migrate.Copy(mdb, delegate(string s) { });
                Check(rep.TrueForAll(l => l.Contains("✓") || l.Contains("غير موجود")), "migration: every table row count matches");
                Check(Db.I(Db.Scalar("SELECT COUNT(*) FROM mreq")) == nReq && Db.I(Db.Scalar("SELECT COUNT(*) FROM disply_command WHERE command_no = 82")) == 1, "migration copies mreq (" + nReq + ") and permission 82");
                Db.EnsureAppTables(); Cache.LoadAll(); As(1, "1");
                Check(Requisitions.Ready && Db.SchemaWarning == null, "SQL Server mode: requisitions ready");
                Requisitions.SetApprovalLimit(0);
                MreqDoc e = NewReq(proj, store, "مهندس", it2, 2);
                Check(Requisitions.Send(e.No) == Requisitions.Pending, "SQL: limit 0 → every request waits");
                Requisitions.Approve(e.No);
                Doc iss = new Doc(); iss.Kind = DocKind.Issue; iss.Project = proj; iss.Store = store; iss.Person = person;
                iss.Lines.Add(new DocLine { ItemNo = it2, Qty = 2 });
                Posting.Save(iss);
                Check(Requisitions.IssueSaved(e.No, 0, 0, iss) == iss.Id && Requisitions.Load(e.No).Status == Requisitions.Done, "SQL: issue linked → «مكتمل»");
                Posting.Delete(Posting.Load(DocKind.Issue, iss.Id));
                Requisitions.RecalcOpen(); Requisitions.Recalc(e.No);
                Check(Requisitions.Load(e.No).Status == Requisitions.Approved, "SQL: issue deleted → back to «موافق عليه»");
                Check(Reports.Get("mreq").Run(rp).Rows.Count >= 5, "SQL: report «طلبات الصرف»");
                Check(Posting.RecalcBalances() == 0, "SQL: balances consistent");
                Db.Close();
                Db.InitSql(Db.BuildSqlConnStr(sql, "master", "", "", true));
                Db.Exec("ALTER DATABASE " + dbName + " SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE " + dbName);
                Db.Close();
                err = Db.InitAccess(mdb);
                Db.EnsureAppTables(); Cache.LoadAll(); As(1, "1");
            }

            // ---- clean up (copy only)
            Posting.Delete(Posting.Load(DocKind.Issue, iss1));
            Db.InTx(delegate(System.Data.Common.DbTransaction tx)
            {
                foreach (int no in new[] { a.No, b.No, c.No, d.No })
                {
                    Db.X(tx, "DELETE FROM mreq_issue WHERE mreq_no = ?", no);
                    Db.X(tx, "DELETE FROM mreq_menu WHERE seq = ?", no);
                    Db.X(tx, "DELETE FROM mreq WHERE mreq_no = ?", no);
                }
                Db.X(tx, "DELETE FROM project WHERE proj_no = ?", proj);
            });
            Check(Math.Abs(Bal(it1, store) - b1) < 1e-6 && Math.Abs(Bal(it2, store) - b2) < 1e-6, "balances back to the start after deleting the test issues");
            Check(Posting.RecalcBalances() == 0, "balances consistent after the test");

            Console.WriteLine(fails == 0 ? "\nALL TESTS PASSED" : "\nFAILURES: " + fails);
            Db.Close();
            return fails;
        }
    }
}
