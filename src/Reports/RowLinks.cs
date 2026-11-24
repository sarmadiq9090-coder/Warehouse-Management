using System.Collections.Generic;
using System.Data;
using System.Globalization;
using System.Linq;
using System.Windows.Forms;
using System;

namespace Stores
{
    /// <summary>What a double click on a report row opens (owner: "double click on any line shows the document").
    /// A link is a short text kept in ReportData.RowLink: "doc/Issue/12", "item/5", "proj/3/from/to", "mreq/7"…
    /// Rows of a document open that document; rows about an item, a cost centre or a person open its statement.</summary>
    static class RowLinks
    {
        static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

        public static string Make(string what, int id) { return id > 0 ? what + "/" + id.ToString(Inv) : null; }
        public static string Doc(DocKind k, int id) { return Make("doc/" + k, id); }
        public static string Item(int id) { return Make("item", id); }
        public static string Project(int id, DateTime from, DateTime to) { return id > 0 ? Make("proj", id) + Dates(from, to) : null; }
        public static string Person(int id, DateTime from, DateTime to) { return id > 0 ? Make("person", id) + Dates(from, to) : null; }
        public static string Reservation(int proj, int item, int stor) { return "resv/" + proj.ToString(Inv) + "/" + item.ToString(Inv) + "/" + stor.ToString(Inv); }
        static string Dates(DateTime a, DateTime b) { return "/" + a.ToString("yyyyMMdd", Inv) + "/" + b.ToString("yyyyMMdd", Inv); }

        /// <summary>The document of a stock movement (kinds of Reports.MvSql).</summary>
        public static string Move(int k, int id)
        {
            switch (k)
            {
                case 1: case 8: return Doc(DocKind.Receipt, id);
                case 3: return Doc(DocKind.Return, id);
                case 6: return Doc(DocKind.PurchaseReturn, id);
                case 4: case 5: return Doc(DocKind.Transfer, id);
                default: return Doc(DocKind.Issue, id);          // issue, count shortage, write-off
            }
        }

        static DateTime Day(string s, DateTime def)
        {
            DateTime d;
            return DateTime.TryParseExact(s, "yyyyMMdd", Inv, DateTimeStyles.None, out d) ? d : def;
        }

        static MainForm Main { get { return Application.OpenForms.OfType<MainForm>().FirstOrDefault(); } }

        /// <summary>Double clicks in a report shown in this browser open their links; edited = the user chose to open the
        /// document for editing in the main window.</summary>
        public static void Attach(WebBrowser wb, Action edited)
        {
            wb.Navigating += delegate(object s, WebBrowserNavigatingEventArgs e)
            {
                if (e.Url == null || e.Url.Scheme != "app") return;
                e.Cancel = true;
                string link = Uri.UnescapeDataString(e.Url.OriginalString.Substring(4));
                wb.BeginInvoke((Action)delegate { if (Open(wb.FindForm(), link) && edited != null) edited(); });
            };
        }

        /// <summary>The report as it is shown now: the rows found / sorted inside the report window, in that order.</summary>
        public static ReportData Shown(WebBrowser wb, ReportData r)
        {
            try
            {
                string s = wb.Document == null ? null : wb.Document.InvokeScript("vis") as string;
                if (s == "-") return r.Subset(new List<int>());       // the search found nothing
                if (!string.IsNullOrEmpty(s)) return r.Subset(s.Split(',').Select(int.Parse).ToList());
            }
            catch { }
            return r;
        }

        /// <summary>Opens a link. True when the user asked to open the document for editing in the main window
        /// (the calling report window closes itself then).</summary>
        public static bool Open(IWin32Window owner, string link)
        {
            if (string.IsNullOrEmpty(link)) return false;
            string[] p = link.Split('/');
            int id; if (p.Length < 2) return false;
            try
            {
                switch (p[0])
                {
                    case "doc":
                        DocKind kind;
                        if (p.Length < 3 || !Enum.TryParse(p[1], out kind) || !int.TryParse(p[2], out id)) return false;
                        return OpenDoc(owner, kind, id);
                    case "resv":
                        int pj, it, st;
                        if (p.Length < 4 || !int.TryParse(p[1], out pj) || !int.TryParse(p[2], out it) || !int.TryParse(p[3], out st)) return false;
                        id = Db.I(Db.Scalar("SELECT MAX(h.res_no) FROM reserve AS h INNER JOIN reserve_menu AS l ON h.res_no = l.seq WHERE h.proj_no = ? AND l.item_no = ? AND h.stor = ? AND h.status = 1", pj, it, st));
                        return id > 0 && OpenDoc(owner, DocKind.Reserve, id);
                }
                if (!int.TryParse(p[1], out id)) return false;
                if (!Session.Demand(Session.Reports)) return false;
                ReportParams q = new ReportParams();
                q.From = Day(p.Length > 2 ? p[2] : "", new DateTime(2000, 1, 1)); q.To = Day(p.Length > 3 ? p[3] : "", DateTime.Today);
                ReportData r = null;
                UI.WaitCursor(true);
                try
                {
                    switch (p[0])
                    {
                        case "item": q.ItemNo = id; r = Reports.ItemCard(q); break;
                        case "proj": q.ProjNo = id; q.Flag = true; r = Reports.Get("project").Run(q); break;
                        case "person": q.PersonNo = id; r = Reports.Get("person").Run(q); break;
                        case "mreq":
                            if (!Session.Can(Session.IssView) && !Requisitions.CanApprove && !Session.Demand(Session.IssView)) return false;
                            MreqDoc m = Requisitions.Load(id); if (m != null) r = Requisitions.Print(m); break;
                        case "preq":
                            if (!Session.Demand(Session.RecView)) return false;
                            PReq pr = PurchaseRequests.Load(id); if (pr != null) r = PurchaseReports.Request(pr); break;
                        case "wo":
                            if (!WriteOffs.CanPrepare && !WriteOffs.CanApprove && !Session.Demand(Session.IssView)) return false;
                            WoDoc w = WriteOffs.Load(id); if (w != null) r = WriteOffs.Print(w); break;
                    }
                }
                finally { UI.WaitCursor(false); }
                if (r == null) { UI.Warn("هذا المستند غير موجود (ربما حُذف)."); return false; }
                if (r.Info != null) { PrintPreviewForm.Show(owner, r); return false; }
                using (ReportViewerForm f = new ReportViewerForm(r)) { f.ShowDialog(owner); return f.EditOpened; }
            }
            catch (UserError ue) { UI.Warn(ue.Message); }
            catch (Exception ex) { UI.Error(ex); }
            return false;
        }

        /// <summary>Shows a document as it is printed, with a button that opens it for editing in the main window.</summary>
        static bool OpenDoc(IWin32Window owner, DocKind kind, int id)
        {
            if (!Session.Demand(MainForm.ViewPerm(kind))) return false;
            Doc d = Posting.Load(kind, id);
            if (d == null) { UI.Warn("هذا المستند غير موجود (ربما حُذف)."); return false; }
            MainForm main = Main;
            using (PrintPreviewForm f = new PrintPreviewForm(Reports.Voucher(d), main != null))
            {
                f.ShowDialog(owner);
                if (!f.EditRequested) return false;
            }
            // after every report window above it has closed
            main.BeginInvoke((Action)delegate
            {
                main.FromTray();       // also when it sits next to the clock
                main.OpenDoc(d.Kind, d.Id);
            });
            return true;
        }
    }
}
