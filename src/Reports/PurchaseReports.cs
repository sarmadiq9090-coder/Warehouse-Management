using System.Collections.Generic;
using System.Data;
using System.Globalization;
using System.Linq;
using System;

namespace Stores
{
    /// <summary>Printouts of purchase requests: one request (with signatures) and the open requests report.
    /// Both are ordinary ReportData: screen, print and Excel export work as for every report.</summary>
    static class PurchaseReports
    {
        static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

        /// <summary>A purchase request as a printable document.</summary>
        public static ReportData Request(PReq q)
        {
            ReportData r = new ReportData();
            r.Title = "طلب شراء رقم " + q.No;
            r.Info = new List<KeyValuePair<string, string>>();
            r.Info.Add(new KeyValuePair<string, string>("التاريخ", UI.Date(q.Date)));
            r.Info.Add(new KeyValuePair<string, string>("المخزن", q.Store > 0 ? Cache.Store(q.Store) : "غير محدد"));
            r.Info.Add(new KeyValuePair<string, string>("المجهز", q.Supplier > 0 ? Cache.Person(q.Supplier) : "غير محدد"));
            r.Info.Add(new KeyValuePair<string, string>("الحالة", PurchaseRequests.StatusName(q.Status)));
            r.Info.Add(new KeyValuePair<string, string>("أعدّه", q.User));
            r.Subtitle = string.Join("     |     ", r.Info.Select(kv => kv.Key + ": " + kv.Value).ToArray());
            bool rec = q.Lines.Any(l => l.Received > 1e-9);
            r.Col("ت", ColKind.Int); r.Col("الرمز", ColKind.Text); r.Col("اسم المادة", ColKind.Text); r.Col("الوحدة", ColKind.Text);
            r.ColT("الكمية المطلوبة");
            if (rec) { r.ColT("المستلم"); r.ColT("المتبقي"); }
            r.Col("الرصيد الحالي", ColKind.Num); r.Col("ملاحظات", ColKind.Text);
            int n = 0;
            foreach (PReqLine l in q.Lines)
            {
                double bal = q.Store > 0 ? Cache.Stock(l.ItemNo, q.Store) : Cache.StockAll(l.ItemNo);
                List<object> v = new List<object> { ++n, Cache.ItemCode(l.ItemNo), Cache.ItemName(l.ItemNo), Cache.ItemUnit(l.ItemNo), l.Qty };
                if (rec) { v.Add(l.Received); v.Add(l.Remaining); }
                v.Add(bal); v.Add(new string(' ', 20));
                r.Row(null, v.ToArray());
            }
            List<string> rc = Db.HasPreq && q.Id > 0 ? PurchaseRequests.Receipts(q.Id) : new List<string>();
            r.Footer = (string.IsNullOrEmpty(q.Note) ? "" : "ملاحظات: " + q.Note + "   ") +
                       (rc.Count > 0 ? "سندات الإدخال المرتبطة: " + string.Join("، ", rc.ToArray()) + ".   " : "") +
                       "طلب الشراء لا يغيّر أرصدة المخزن؛ المواد تُضاف إلى الرصيد عند حفظ سند الإدخال المخزني.";
            r.Signatures = true;
            r.SigLabels = new[] { "طالب الشراء", "أمين المخزن", "المدير" };
            return r;
        }

        /// <summary>Lines of sent / partly received requests (and drafts when Flag) that still wait for a quantity.</summary>
        public static ReportData Open(ReportParams p)
        {
            ReportData r = new ReportData();
            r.Title = "طلبات الشراء المفتوحة";
            r.Subtitle = (p.Store == 0 ? "كل المخازن" : "مخزن: " + Cache.Store(p.Store)) + (p.PersonNo > 0 ? "   —   المجهز: " + Cache.Person(p.PersonNo) : "") +
                         (p.ItemNo > 0 ? "   —   " + Cache.ItemName(p.ItemNo) : "") + (p.Flag ? "   —   مع المسودات" : "");
            r.Col("ت", ColKind.Int); r.Col("رقم الطلب", ColKind.Text); r.Col("التاريخ", ColKind.Date); r.Col("العمر (يوم)", ColKind.Int); r.Col("الحالة", ColKind.Text);
            r.Col("المجهز", ColKind.Text); r.Col("المخزن", ColKind.Text); r.Col("الرمز", ColKind.Text); r.Col("اسم المادة", ColKind.Text); r.Col("الوحدة", ColKind.Text);
            r.ColT("المطلوب"); r.ColT("المستلم"); r.ColT("المتبقي");
            if (!Db.HasPreq) { r.Footer = PurchaseRequests.NotReady; return r; }
            string sql = "SELECT h.preq_no, h.menu_no, h.preq_date, h.stor, h.status, h.p_no, l.item_no, l.qty, l.received_qty FROM preq AS h INNER JOIN preq_menu AS l ON l.seq = h.preq_no " +
                         "WHERE h.status IN (" + (p.Flag ? "0, " : "") + "1, 2)";
            List<object> a = new List<object>();
            if (p.Store > 0) { sql += " AND (h.stor = ? OR h.stor IS NULL)"; a.Add(p.Store); }
            if (p.PersonNo > 0) { sql += " AND h.p_no = ?"; a.Add(p.PersonNo); }
            if (p.ItemNo > 0) { sql += " AND l.item_no = ?"; a.Add(p.ItemNo); }
            sql += " ORDER BY h.preq_date, h.preq_no, l.id";
            int n = 0, old = 0; double tq = 0, tr = 0; HashSet<int> reqs = new HashSet<int>();
            foreach (DataRow x in Db.Query(sql, a.ToArray()).Rows)
            {
                double q = Db.D(x["qty"]), rc = Db.D(x["received_qty"]), rem = Math.Max(0, q - rc);
                if (rem <= 1e-9) continue;
                int it = Db.I(x["item_no"]), age = Math.Max(0, (int)(DateTime.Today - Db.Dt(x["preq_date"]).Date).TotalDays);
                bool late = age > 30 && Db.I(x["status"]) != PurchaseRequests.Draft;
                if (late) old++;
                reqs.Add(Db.I(x["preq_no"])); tq += q; tr += rc;
                r.Row(late ? "neg" : Db.I(x["status"]) == PurchaseRequests.Draft ? "muted" : null, ++n, Db.S(x["menu_no"]), Db.Dt(x["preq_date"]), age,
                    PurchaseRequests.StatusName(Db.I(x["status"])), Cache.Person(Db.I(x["p_no"])), Db.I(x["stor"]) > 0 ? Cache.Store(Db.I(x["stor"])) : "",
                    Cache.ItemCode(it), Cache.ItemName(it), Cache.ItemUnit(it), q, rc == 0 ? null : (object)rc, rem);
                r.Link(RowLinks.Make("preq", Db.I(x["preq_no"])));
            }
            r.Sum("عدد الطلبات", reqs.Count.ToString("#,##0", Inv));
            r.Sum("عدد الأسطر", n.ToString("#,##0", Inv));
            r.Sum("المتبقي", UI.Num(tq - tr));
            if (old > 0) r.Sum("أقدم من 30 يوما", old.ToString("#,##0", Inv));
            r.Footer = "المتبقي = المطلوب − المستلم بسندات الإدخال المرتبطة بالطلب. الأحمر: طلب مُرسل منذ أكثر من 30 يوما ولم يكتمل" +
                       (p.Flag ? "، والرمادي: مسودة لم تُرسل بعد." : ".");
            return r;
        }
    }
}
