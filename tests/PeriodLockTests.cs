using System;
using System.Collections.Generic;
using System.Data;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Windows.Forms;

namespace Stores
{
    /// <summary>
    /// Monthly period lock (PeriodLock, setting locked_through, permission 84) and the audit trail with before / after
    /// values (AuditTrail, table app_log_detail), on a copy of the data:
    ///  lock a month → add / edit / move / delete a document dated in it is refused with the month named, a document
    ///  after it works; permission enforced; year closing still wins when later; unlock works and is logged;
    ///  audit: editing an issue's qty and note, a receipt's price / lines, deleting a document, item master data and a
    ///  cost centre leave detail rows with old / new values; the viewer query and the report find them.
    /// Usage: PeriodLockTests.exe &lt;copy.mdb&gt; &lt;out dir&gt; [&lt;sql instance, e.g. (localdb)\EngStoresTest&gt;]
    /// With a SQL instance the same checks run again on a LocalDB database made from the copy (dropped afterwards).
    /// </summary>
    static class PeriodLockTestsMain
    {
        static int fails;
        static void Check(bool ok, string what) { Console.WriteLine((ok ? "PASS  " : "FAIL  ") + what); if (!ok) fails++; }
        static bool Refused(Action a, out string msg)
        {
            try { a(); msg = ""; return false; }
            catch (UserError e) { msg = e.Message.Split('\n')[0]; return true; }
        }

        [STAThread]
        static int Main(string[] args)
        {
            Console.OutputEncoding = System.Text.Encoding.UTF8;
            try { return Run(args); }
            catch (Exception ex) { Console.WriteLine("FAIL  unexpected error: " + ex); return 1; }
        }

        static int Run(string[] args)
        {
            string mdb = Path.GetFullPath(args[0]), outDir = args[1], sql = args.Length > 2 ? args[2] : null;
            Directory.CreateDirectory(outDir);
            string err = Db.InitAccess(mdb); if (err != null) { Console.WriteLine(err); return 1; }
            Db.EnsureAppTables();
            Check(AuditTrail.Ready && Db.TableExists("app_log_detail"), "app_log_detail created (" + (Db.SchemaWarning ?? "ok") + ")");
            Check(Db.I(Db.Scalar("SELECT COUNT(*) FROM disply_command WHERE command_no = ?", PeriodLock.Perm)) == 1, "permission row " + PeriodLock.Perm + " added to disply_command");
            Db.EnsureAppTables();
            Check(Db.I(Db.Scalar("SELECT COUNT(*) FROM disply_command WHERE command_no = ?", PeriodLock.Perm)) == 1, "running EnsureAppTables again adds nothing");
            Console.WriteLine("== Access: " + mdb);
            RunAll();

            if (sql != null)
            {
                int details = Db.I(Db.Scalar("SELECT COUNT(*) FROM app_log_detail"));
                Db.Close();
                const string dbName = "EngStoresPeriodLockTest";
                err = Db.InitSql(Db.BuildSqlConnStr(sql, "master", "", "", true));
                if (err != null) { Console.WriteLine("sql open failed: " + err); return 1; }
                Db.Exec("IF DB_ID('" + dbName + "') IS NOT NULL BEGIN ALTER DATABASE " + dbName + " SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE " + dbName + " END");
                Db.Exec("CREATE DATABASE " + dbName);
                err = Db.InitSql(Db.BuildSqlConnStr(sql, dbName, "", "", true));
                if (err != null) { Console.WriteLine("sql open failed: " + err); return 1; }
                Console.WriteLine("== SQL Server: " + sql + " / " + dbName);
                List<string> rep = Migrate.Copy(mdb, delegate(string s) { });
                Check(rep.TrueForAll(l => l.Contains("✓") || l.Contains("غير موجود")), "migration: every table row count matches");
                Check(Db.I(Db.Scalar("SELECT COUNT(*) FROM app_log_detail")) == details, "migration copies app_log_detail (" + details + " rows)");
                Db.EnsureAppTables();
                Check(AuditTrail.Ready && Db.SchemaWarning == null, "SQL Server: EnsureAppTables ok (" + (Db.SchemaWarning ?? "ok") + ")");
                RunAll();
                Db.Close();
                Db.InitSql(Db.BuildSqlConnStr(sql, "master", "", "", true));
                Db.Exec("ALTER DATABASE " + dbName + " SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE " + dbName);
                Check(Db.I(Db.Scalar("SELECT COUNT(*) FROM sys.databases WHERE name = ?", dbName)) == 0, "test database dropped");
                Db.Close();
            }
            Console.WriteLine(fails == 0 ? "\nALL PERIOD LOCK / AUDIT TESTS PASSED" : "\nFAILURES: " + fails);
            return fails;
        }

        static void AsUser(int no, string name) { Session.UserNo = no; Session.UserName = name; Session.LoadPerms(); }

        /// <summary>The detail rows of the newest app_log row with this act (and details starting with text).</summary>
        static DataTable LastDetails(string act, string detailsStart, out int logId)
        {
            logId = Db.I(Db.Scalar("SELECT MAX(id) FROM app_log WHERE act = ? AND details LIKE ?", act, detailsStart + "%"));
            return Db.Query("SELECT tbl, rec_key, fld, old_v, new_v FROM app_log_detail WHERE log_id = ? ORDER BY id", logId);
        }

        static DataRow Fld(DataTable t, string fld) { return t.Rows.Cast<DataRow>().FirstOrDefault(r => Db.S(r["fld"]) == fld); }
        static string Show(DataTable t) { return string.Join(" | ", t.Rows.Cast<DataRow>().Select(r => Db.S(r["fld"]) + ": «" + Db.S(r["old_v"]) + "» → «" + Db.S(r["new_v"]) + "»").ToArray()); }

        static Doc Issue(DateTime date, int store, int cc, int item, double qty)
        {
            Doc d = new Doc(); d.Kind = DocKind.Issue; d.Date = date; d.Store = store; d.Project = cc;
            d.Lines.Add(new DocLine { ItemNo = item, Qty = qty, Price = 0 });
            return d;
        }

        static void RunAll()
        {
            AsUser(1, "مدير الاختبار"); Session.Advanced = true;
            Cache.LoadAll();
            Console.WriteLine("balances corrected before the test (old differences in the data): " + Posting.RecalcBalances());
            Db.SetSetting(PeriodLock.Key, ""); Db.SetSetting("closed_through", "");
            string msg;
            int cc = CostCentres.Save(0, "مركز اختبار القفل " + DateTime.Now.Ticks, "", CostCentres.Other, 1, "", null, "");
            DataTable best = Db.Query("SELECT TOP 1 item_no, stor FROM item_stor WHERE item_bal >= 5 ORDER BY item_bal DESC");
            if (best.Rows.Count == 0) { Check(false, "an item with stock exists"); return; }
            int item = Db.I(best.Rows[0]["item_no"]), store = Db.I(best.Rows[0]["stor"]);
            List<int> others = Db.Query("SELECT TOP 3 item_no FROM item WHERE item_no <> ? ORDER BY item_no", item).Rows.Cast<DataRow>().Select(r => Db.I(r[0])).ToList();
            int supplier = Db.I(Db.Scalar("SELECT MIN(custom_no) FROM shop"));

            DateTime m = DateTime.Today.AddMonths(-2);
            DateTime mStart = new DateTime(m.Year, m.Month, 1), mEnd = PeriodLock.MonthEnd(m.Year, m.Month), after = mEnd.AddDays(1);
            string mName = PeriodLock.MonthName(mEnd);
            Console.WriteLine("month to lock: " + mName + " (" + UI.Date(mStart) + " .. " + UI.Date(mEnd) + "), item " + item + " store " + store);

            // documents dated in the month, made before the lock
            Doc a = Issue(mStart.AddDays(9), store, cc, item, 1); a.Note = "";
            Posting.Save(a);
            Check(a.Id > 0, "issue " + a.No + " dated " + UI.Date(a.Date) + " saved before the lock");
            Doc rc = new Doc(); rc.Kind = DocKind.Receipt; rc.RType = 2; rc.Date = mStart.AddDays(5); rc.Store = store; rc.Person = supplier;
            rc.Lines.Add(new DocLine { ItemNo = others[0], Qty = 3, Price = 1000 });
            rc.Lines.Add(new DocLine { ItemNo = others[1], Qty = 2, Price = 500 });
            Posting.Save(rc);
            Check(rc.Id > 0, "receipt " + rc.No + " dated " + UI.Date(rc.Date) + " saved before the lock");

            // ---- permission
            AsUser(2, "مستخدم الاختبار");
            Db.Exec("UPDATE disply_command SET user2 = ? WHERE command_no = ?", false, PeriodLock.Perm); Session.LoadPerms();
            Check(!PeriodLock.CanManage, "user 2 has no permission " + PeriodLock.Perm + " by default");
            Check(Refused(delegate { PeriodLock.LockThrough(m.Year, m.Month); }, out msg) && msg.Contains(PeriodLock.PermName), "locking without the permission is refused: " + msg);
            Check(PeriodLock.LockedThrough() == DateTime.MinValue, "nothing locked after the refusal");
            Db.Exec("UPDATE disply_command SET user2 = ? WHERE command_no = ?", true, PeriodLock.Perm); Session.LoadPerms();
            Check(PeriodLock.CanManage, "permission granted to user 2 (as in «المستخدمون»)");

            // ---- lock
            Check(Refused(delegate { PeriodLock.LockThrough(DateTime.Today.AddMonths(1).Year, DateTime.Today.AddMonths(1).Month); }, out msg), "a month that has not started can not be locked: " + msg);
            DateTime got = PeriodLock.LockThrough(m.Year, m.Month);
            Check(got == mEnd && PeriodLock.LockedThrough() == mEnd && PeriodLock.Limit() == mEnd, "locked through " + UI.Date(mEnd) + " (setting " + PeriodLock.Key + ")");
            Check(Refused(delegate { PeriodLock.LockThrough(m.Year, m.Month); }, out msg), "locking the same month again is refused: " + msg);
            int lockLog = Db.I(Db.Scalar("SELECT MAX(id) FROM app_log WHERE act = ? AND user_name = ?", PeriodLock.ActLock, "مستخدم الاختبار"));
            Check(lockLog > 0 && Db.I(Db.Scalar("SELECT COUNT(*) FROM app_log_detail WHERE log_id = ? AND new_v = ?", lockLog, UI.Date(mEnd))) == 1, "the lock is logged with user and new value");
            Check(PeriodLock.IsLocked(mEnd) && PeriodLock.IsLocked(mStart.AddYears(-1)) && !PeriodLock.IsLocked(after), "IsLocked: up to " + UI.Date(mEnd) + " yes, " + UI.Date(after) + " no");
            PeriodLock.IsLocked(a.Date);
            Check(PeriodLock.ShortText(a.Date) == "شهر " + PeriodLock.MonthName(a.Date) + " مقفل", "document screen status: " + PeriodLock.ShortText(a.Date));
            AsUser(1, "مدير الاختبار");

            // ---- locked: add / edit / move / delete refused, with the month in the message
            Check(Refused(delegate { Posting.Save(Issue(mStart.AddDays(3), store, cc, item, 1)); }, out msg) && msg.Contains(mName) && msg.Contains("مقفل"), "new issue in the locked month refused: " + msg);
            Check(Refused(delegate { Posting.Save(Issue(mEnd, store, cc, item, 1)); }, out msg), "new issue on the last locked day " + UI.Date(mEnd) + " refused");
            Doc e1 = Posting.Load(DocKind.Issue, a.Id); e1.Lines[0].Qty = 2;
            Check(Refused(delegate { Posting.Save(e1); }, out msg) && msg.Contains(mName), "editing issue " + a.No + " refused: " + msg);
            Doc e2 = Posting.Load(DocKind.Issue, a.Id); e2.Date = after;
            Check(Refused(delegate { Posting.Save(e2); }, out msg), "moving it out of the locked month refused");
            Check(Refused(delegate { Posting.Delete(Posting.Load(DocKind.Issue, a.Id)); }, out msg), "deleting it refused");
            Check(Refused(delegate { Posting.Delete(Posting.Load(DocKind.Receipt, rc.Id)); }, out msg), "deleting receipt " + rc.No + " refused");
            int oldDoc = Db.I(Db.Scalar("SELECT MAX(shop_no) FROM sale WHERE menu_date < ?", mStart));
            if (oldDoc > 0)
            {
                Doc o = Posting.Load(DocKind.Issue, oldDoc) ?? Posting.Load(DocKind.PurchaseReturn, oldDoc);
                if (o != null && o.Project == 0) o.Project = cc;
                Check(o != null && Refused(delegate { Posting.Save(o); }, out msg), "an older document (" + (o != null ? o.No + " " + UI.Date(o.Date) : "-") + ") is locked too");
            }
            if (Db.HasCount)
                Check(Refused(delegate { StockCount.Create(mStart.AddDays(1), store, 0, "", false); }, out msg), "a stock count dated in the locked month refused");
            Doc b = Issue(after <= DateTime.Today ? after : DateTime.Today, store, cc, item, 1);
            Posting.Save(b);
            Check(b.Id > 0, "issue dated " + UI.Date(b.Date) + " (after the lock) saved normally, no " + b.No);
            Doc b2 = Posting.Load(DocKind.Issue, b.Id); b2.Lines[0].Qty = 2; Posting.Save(b2);
            Check(Posting.Load(DocKind.Issue, b.Id).Lines[0].Qty == 2, "and edited normally");

            // ---- year closing still works: the later of the two limits applies
            DateTime yEnd = new DateTime(m.Year - 1, 12, 31);
            Db.SetSetting("closed_through", yEnd.ToString("yyyy-MM-dd"));
            Check(PeriodLock.Limit() == mEnd, "closed year " + yEnd.Year + " + month lock: limit = " + UI.Date(mEnd));
            Check(Refused(delegate { Posting.Save(Issue(new DateTime(yEnd.Year, 6, 1), store, cc, item, 1)); }, out msg) && msg.Contains("سنة " + yEnd.Year), "a document in the closed year gets the year message: " + msg);
            AsUser(2, "مستخدم الاختبار");
            Check(Refused(delegate { PeriodLock.LockThrough(yEnd.Year, 6); }, out msg), "a month inside the closed year can not be locked: " + msg);
            AsUser(1, "مدير الاختبار");
            Db.SetSetting("closed_through", "");
            Check(YearClose.ClosedThrough() == DateTime.MinValue && PeriodLock.Limit() == mEnd, "year closing removed, month lock stays");

            // ---- unlock: permission, logged
            AsUser(3, "مستخدم آخر");
            Check(Refused(delegate { PeriodLock.Unlock(); }, out msg), "unlocking without the permission is refused: " + msg);
            AsUser(2, "مستخدم الاختبار");
            Check(PeriodLock.Unlock() == mEnd && PeriodLock.LockedThrough() == DateTime.MinValue, "unlocked by user 2 (was " + UI.Date(mEnd) + ")");
            int unl; DataTable ud = LastDetails(PeriodLock.ActUnlock, "", out unl);
            Check(unl > 0 && Db.S(Db.Scalar("SELECT user_name FROM app_log WHERE id = ?", unl)) == "مستخدم الاختبار" && ud.Rows.Count == 1 && Db.S(ud.Rows[0]["old_v"]) == UI.Date(mEnd) && Db.S(ud.Rows[0]["new_v"]) == "",
                  "the unlock is logged: " + Show(ud));
            Check(PeriodLock.Unlock() == DateTime.MinValue, "unlocking again does nothing");
            AsUser(1, "مدير الاختبار");

            // ---- audit: edit an issue's quantity and a header field
            Doc e3 = Posting.Load(DocKind.Issue, a.Id); e3.Lines[0].Qty = 2; e3.Note = "ملاحظة تدقيق";
            Posting.Save(e3);
            int lid; DataTable det = LastDetails("تعديل " + Posting.KindName(DocKind.Issue), "رقم " + a.No + " ", out lid);
            DataRow q = Fld(det, "الكمية: " + AuditTrail.ItemLabel(item)), n = Fld(det, "الملاحظات");
            Check(q != null && Db.S(q["old_v"]) == "1" && Db.S(q["new_v"]) == "2" && Db.S(q["tbl"]) == "sale_menu" && Db.S(q["rec_key"]) == a.No, "issue edit: qty 1 → 2 recorded (" + Show(det) + ")");
            Check(n != null && Db.S(n["old_v"]) == "" && Db.S(n["new_v"]) == "ملاحظة تدقيق" && Db.S(n["tbl"]) == "sale", "issue edit: note recorded");
            Check(det.Rows.Count == 2, "nothing else recorded for that edit (" + det.Rows.Count + " rows)");
            // a save without changes leaves no detail rows
            Posting.Save(Posting.Load(DocKind.Issue, a.Id));
            DataTable none = LastDetails("تعديل " + Posting.KindName(DocKind.Issue), "رقم " + a.No + " ", out lid);
            Check(none.Rows.Count == 0, "saving without changes: logged, no detail rows");

            // receipt: price changed, a line removed, a line added, the date changed
            Doc r2 = Posting.Load(DocKind.Receipt, rc.Id);
            r2.Lines[0].Price = 1250; r2.Lines.RemoveAt(1); r2.Lines.Add(new DocLine { ItemNo = others[2], Qty = 4, Price = 10 }); r2.Date = rc.Date.AddDays(1);
            Posting.Save(r2);
            DataTable rd = LastDetails("تعديل " + Posting.KindName(DocKind.Receipt), "رقم " + rc.No + " ", out lid);
            DataRow pr = Fld(rd, "السعر: " + AuditTrail.ItemLabel(others[0])), rm = Fld(rd, "مادة محذوفة: " + AuditTrail.ItemLabel(others[1])),
                    ad = Fld(rd, "مادة مضافة: " + AuditTrail.ItemLabel(others[2])), dt = Fld(rd, "التاريخ");
            Check(pr != null && Db.S(pr["old_v"]) == UI.Num(1000) && Db.S(pr["new_v"]) == UI.Num(1250), "receipt edit: price 1000 → 1250");
            Check(rm != null && Db.S(rm["old_v"]).Contains("2") && Db.S(rm["new_v"]) == "", "receipt edit: removed line with its old qty");
            Check(ad != null && Db.S(ad["old_v"]) == "" && Db.S(ad["new_v"]).Contains("4"), "receipt edit: added line with its new qty");
            Check(dt != null && Db.S(dt["old_v"]) == UI.Date(rc.Date) && Db.S(dt["new_v"]) == UI.Date(rc.Date.AddDays(1)), "receipt edit: date change (" + Show(rd) + ")");

            // delete → logged with the document as it was
            string aNo = a.No;
            Posting.Delete(Posting.Load(DocKind.Issue, a.Id));
            DataTable dd = LastDetails("حذف " + Posting.KindName(DocKind.Issue), "رقم " + aNo, out lid);
            DataRow dl = Fld(dd, "مادة محذوفة: " + AuditTrail.ItemLabel(item)), dn = Fld(dd, "الملاحظات"), dtt = Fld(dd, "التاريخ");
            Check(lid > 0 && dl != null && Db.S(dl["old_v"]).Contains("2") && dn != null && Db.S(dn["old_v"]) == "ملاحظة تدقيق" && dtt != null && Db.S(dtt["new_v"]) == "",
                  "delete logged with header and lines: " + Show(dd));

            // item master data (the item dialog, as the user saves it) and a cost centre
            DataRow it = Db.Query("SELECT item_bal_ret FROM item WHERE item_no = ?", others[0]).Rows[0];
            double oldRet = Db.D(it[0]);
            using (ItemDialog dlg = new ItemDialog(others[0]))
            {
                // shown off-screen: the type / group lists are bound only once the window exists
                dlg.StartPosition = FormStartPosition.Manual; dlg.Location = new System.Drawing.Point(-4000, -4000); dlg.ShowInTaskbar = false;
                dlg.Show(); Application.DoEvents();
                int dbType = Db.I(Db.Scalar("SELECT [type] FROM item WHERE item_no = ?", others[0]));
                Check(dbType == 0 || UI.Sel((ComboBox)Field(dlg, "cmbType")) == dbType, "item dialog shows the item's type (" + dbType + ")");
                ((TextBox)Field(dlg, "txtRet")).Text = UI.Num(oldRet + 7);
                bool ok = (bool)dlg.GetType().GetMethod("SaveItem", BindingFlags.NonPublic | BindingFlags.Instance).Invoke(dlg, null);
                Check(ok, "item dialog saved");
                dlg.Close();
            }
            DataTable idt = LastDetails("تعديل مادة", "", out lid);
            DataRow ir = Fld(idt, "حد إعادة الطلب");
            Check(ir != null && Db.S(ir["old_v"]) == UI.Num(oldRet) && Db.S(ir["new_v"]) == UI.Num(oldRet + 7) && idt.Rows.Count == 1, "item edit recorded: " + Show(idt));
            Db.Exec("UPDATE item SET item_bal_ret = ? WHERE item_no = ?", oldRet, others[0]);
            string ccOld = Cache.Project(cc), ccNew = ccOld + " (معدل)";
            CostCentres.Save(cc, ccNew, Db.S(CostCentres.Row(cc)["proj_code"]), CostCentres.Department, 1, "", null, "");
            DataTable cd = LastDetails("تعديل مركز كلفة", "", out lid);
            DataRow cn = Fld(cd, "الاسم"), ct = Fld(cd, "النوع");
            Check(cn != null && Db.S(cn["old_v"]) == ccOld && Db.S(cn["new_v"]) == ccNew && ct != null && Db.S(ct["old_v"]) == "أخرى" && Db.S(ct["new_v"]) == "قسم", "cost centre edit recorded: " + Show(cd));
            CostCentres.SetClosed(cc, true);
            DataRow cs = Fld(LastDetails("إغلاق مركز كلفة", "", out lid), "الحالة");
            Check(cs != null && Db.S(cs["old_v"]) == "فعّال" && Db.S(cs["new_v"]) == "مغلق", "closing a cost centre recorded");

            // ---- viewer query and report
            Stopwatch sw = Stopwatch.StartNew();
            DataTable all = AuditTrail.Query(DateTime.Today.AddYears(-30), DateTime.Today.AddDays(1), null, null, null, true, 3000);
            long ms = sw.ElapsedMilliseconds;
            Check(all.Rows.Count > 0 && ms < 3000, "viewer query, all dates: " + all.Rows.Count + " rows in " + ms + " ms");
            DataTable byDoc = AuditTrail.Query(DateTime.Today.AddYears(-30), DateTime.Today.AddDays(1), null, aNo, AuditTrail.ActGroups().First(g => g.Key == Posting.KindName(DocKind.Issue)).Value, true, 3000);
            Check(byDoc.Rows.Count >= 4 && byDoc.Rows.Cast<DataRow>().All(r => Db.S(r["rec_key"]) == aNo), "filter by document " + aNo + " (issues): " + byDoc.Rows.Count + " rows, all of it");
            DataTable byUser = AuditTrail.Query(DateTime.Today.AddYears(-30), DateTime.Today.AddDays(1), "مستخدم الاختبار", null, null, false, 3000);
            Check(byUser.Rows.Count >= 2 && byUser.Rows.Cast<DataRow>().All(r => Db.S(r["user_name"]) == "مستخدم الاختبار"), "filter by user: " + byUser.Rows.Count + " rows (lock + unlock)");
            DataTable future = AuditTrail.Query(DateTime.Today.AddDays(1), DateTime.Today.AddDays(2), null, null, null, false, 3000);
            Check(future.Rows.Count == 0, "filter by date: nothing tomorrow");
            ReportParams p = new ReportParams(); p.From = DateTime.Today; p.To = DateTime.Today; p.Text = aNo;
            ReportData rep = Reports.Get("audittrail").Run(p);
            Check(rep.Rows.Count >= 4 && rep.Rows.All(o => o.Length == rep.Cols.Count), "report «سجل التدقيق» finds document " + aNo + ": " + rep.Rows.Count + " rows");
            using (AuditTrailForm f = new AuditTrailForm()) { } using (PeriodLockForm f = new PeriodLockForm()) { }
            Check(true, "the two windows build");

            // ---- clean up (copy only)
            Posting.Delete(Posting.Load(DocKind.Issue, b.Id));
            try { Posting.Delete(Posting.Load(DocKind.Receipt, rc.Id)); Check(true, "test receipt deleted"); }
            catch (UserError ue) { Console.WriteLine("note: test receipt kept: " + ue.Message.Split('\n')[0]); }
            Db.Exec("UPDATE disply_command SET user2 = ? WHERE command_no = ?", false, PeriodLock.Perm);
            Db.Exec("DELETE FROM project WHERE proj_no = ?", cc);
            AsUser(1, "مدير الاختبار");
            Check(Posting.RecalcBalances() == 0, "balances consistent after the test");
        }

        static object Field(object o, string name) { return o.GetType().GetField(name, BindingFlags.NonPublic | BindingFlags.Instance).GetValue(o); }
    }
}
