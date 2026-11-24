using System;
using System.IO;
using System.Linq;

namespace Stores
{
    // Year-end closing test on a copy of the data: close year Y, try to add / edit / delete in Y (must be refused),
    // work in Y+1 (must be allowed), check the archive file, reopen.
    // Y is chosen from the data (works on the demo data and on a copy of real data):
    //   two years ago when it has stock issues (then Y+1 is closed and reopened as well), otherwise last year.
    static class YcTestMain
    {
        static int fails;
        static void Check(bool ok, string what) { Console.WriteLine((ok ? "PASS  " : "FAIL  ") + what); if (!ok) fails++; }
        static bool Refused(Action a, out string msg)
        {
            try { a(); msg = ""; return false; }
            catch (UserError e) { msg = e.Message.Split('\n')[0]; return true; }
        }

        static int IssuesIn(int year)
        {
            return Db.I(Db.Scalar("SELECT COUNT(*) FROM sale WHERE menu_date >= ? AND menu_date < ?", new DateTime(year, 1, 1), new DateTime(year + 1, 1, 1)));
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
            string err = Db.InitAccess(Path.GetFullPath(args[0])); if (err != null) { Console.WriteLine(err); return 1; }
            Db.EnsureAppTables();
            Session.UserNo = 1; Session.UserName = "1"; Session.LoadPerms(); Session.Advanced = true;
            Cache.LoadAll();
            // «المستلم = مركز الكلفة»: issues need a cost centre (refusals below must be about the closed year)
            int cc = CostCentres.Save(0, "مركز اختبار الإقفال " + DateTime.Now.Ticks, "", CostCentres.Other, 1, "", null, "");
            string msg;

            int thisYear = DateTime.Today.Year;
            int Y = IssuesIn(thisYear - 2) > 0 ? thisYear - 2 : thisYear - 1;
            bool twoYears = Y + 1 < thisYear;
            if (IssuesIn(Y) == 0) { Check(false, "the data has stock issues in " + Y + " or " + (thisYear - 2)); return 1; }
            DateTime yEnd = new DateTime(Y, 12, 31);
            bool olderData = Db.I(Db.Scalar("SELECT COUNT(*) FROM sale WHERE menu_date < ?", new DateTime(Y, 1, 1))) +
                             Db.I(Db.Scalar("SELECT COUNT(*) FROM shop WHERE menu_date < ?", new DateTime(Y, 1, 1))) > 0;
            Console.WriteLine("year to close: " + Y + (twoYears ? " (then " + (Y + 1) + ")" : "") + (olderData ? ", data before it exists" : ", no data before it"));

            Check(YearClose.ClosedYear() == 0, "no year closed at the start");
            Check(YearClose.ClosableYears().Contains(Y) && YearClose.ClosableYears().Contains(thisYear - 1) && !YearClose.ClosableYears().Contains(thisYear),
                  "closable years: " + string.Join(", ", YearClose.ClosableYears()) + " (current year not offered)");
            Check(Refused(delegate { YearClose.Close(thisYear, false, false); }, out msg), "current year cannot be closed: " + msg);

            int oldDoc = Db.I(Db.Scalar("SELECT MAX(shop_no) FROM sale WHERE menu_date >= ? AND menu_date < ?", new DateTime(Y, 1, 1), new DateTime(Y + 1, 1, 1)));
            System.Data.DataTable best = Db.Query("SELECT TOP 1 item_no, stor FROM item_stor WHERE item_bal >= 2 ORDER BY item_bal DESC");
            if (best.Rows.Count == 0) { Check(false, "an item with stock exists"); return 1; }
            int item = Db.I(best.Rows[0]["item_no"]), store = Db.I(best.Rows[0]["stor"]);
            int person = Db.I(Db.Scalar("SELECT MIN(custom_no) FROM sale"));
            Console.WriteLine("item " + item + " store " + store + ", document in " + Y + ": " + oldDoc);
            YearClose.ArchiveOverride = Path.Combine(args[1], "archive");

            string file = YearClose.Close(Y, true, true);
            Check(YearClose.ClosedYear() == Y && YearClose.ClosedThrough() == yEnd, Y + " closed (through " + UI.Date(yEnd) + ")");
            Check(file != null && File.Exists(file), "year-end stock count archived: " + file);
            Check(Directory.GetFiles(Settings.BackupDir, "قبل إقفال سنة " + Y + "*").Length > 0, "backup taken before closing (" + Settings.BackupDir + ")");

            // new document dated in the closed year
            DateTime inClosed = new DateTime(Y, 6, 1);
            Doc d = new Doc(); d.Kind = DocKind.Issue; d.Date = inClosed; d.Store = store; d.Person = person; d.Project = cc;
            d.Lines.Add(new DocLine { ItemNo = item, Qty = 1 });
            Check(Refused(delegate { Posting.Save(d); }, out msg), "new issue dated " + UI.Date(inClosed) + " refused: " + msg);

            // edit / delete an existing document of the closed year
            Doc o = Posting.Load(DocKind.Issue, oldDoc); if (o.Project == 0) o.Project = cc;
            Check(YearClose.IsClosed(o.Date), "existing document " + o.No + " (" + UI.Date(o.Date) + ") is in the closed year");
            Check(Refused(delegate { Posting.Save(o); }, out msg), "editing it is refused");
            Doc o2 = Posting.Load(DocKind.Issue, oldDoc); if (o2.Project == 0) o2.Project = cc; o2.Date = new DateTime(Y + 1, 2, 1);
            Check(Refused(delegate { Posting.Save(o2); }, out msg), "moving it out of the closed year (changing its date to " + (Y + 1) + ") is refused");
            Check(Refused(delegate { Posting.Delete(Posting.Load(DocKind.Issue, oldDoc)); }, out msg), "deleting it is refused");

            // work in the open year
            DateTime inOpen = new DateTime(Y + 1, 3, 1); if (inOpen > DateTime.Today) inOpen = DateTime.Today;
            Doc n = new Doc(); n.Kind = DocKind.Issue; n.Date = inOpen; n.Store = store; n.Person = person; n.Project = cc;
            n.Lines.Add(new DocLine { ItemNo = item, Qty = 1 });
            Posting.Save(n);
            Check(n.Id > 0, "issue dated " + UI.Date(inOpen) + " saved normally (no " + n.No + ")");
            Doc n2 = new Doc(); n2.Kind = DocKind.Issue; n2.Date = yEnd; n2.Store = store; n2.Person = person; n2.Project = cc; n2.Lines.Add(new DocLine { ItemNo = item, Qty = 1 });
            Check(Refused(delegate { Posting.Save(n2); }, out msg), "last day of the closed year (" + UI.Date(yEnd) + ") refused");
            Posting.Delete(Posting.Load(DocKind.Issue, n.Id));
            Check(Posting.Load(DocKind.Issue, n.Id) == null, (Y + 1) + " document deleted normally");

            // the archive = stock as of 31/12/Y
            ReportParams p = new ReportParams(); p.From = p.To = yEnd;
            ReportData r = Reports.Get("stock").Run(p);
            Check(r.Rows.Count > 0, "year-end stock count has " + r.Rows.Count + " items");

            // closing a later year with an earlier one already closed; reopening
            int before = olderData ? Y - 1 : 0;
            if (twoYears)
            {
                YearClose.Close(Y + 1, false, false);
                Check(YearClose.ClosedYear() == Y + 1, (Y + 1) + " closed after " + Y);
                Check(YearClose.Reopen() == Y + 1 && YearClose.ClosedYear() == Y, "reopen " + (Y + 1) + " → " + Y + " stays closed");
            }
            Check(YearClose.Reopen() == Y && YearClose.ClosedYear() == before,
                  "reopen " + Y + " → " + Y + " open" + (before > 0 ? ", earlier years (closed with it) stay closed" : ", no year closed any more"));
            Doc o3 = Posting.Load(DocKind.Issue, oldDoc); if (o3.Project == 0) o3.Project = cc;
            Posting.Save(o3);
            Check(true, "after reopening, " + Y + " documents can be edited again");
            Check(Db.Query("SELECT act FROM app_log WHERE act LIKE '%سنة%'").Rows.Count >= (twoYears ? 4 : 2), "closing and reopening are in the activity log");

            Console.WriteLine(fails == 0 ? "ALL YEAR-CLOSING TESTS PASSED" : "FAILURES: " + fails);
            Db.Close();
            return fails;
        }
    }
}
