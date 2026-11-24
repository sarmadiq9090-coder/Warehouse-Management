using System.Collections.Generic;
using System.Data;
using System.Globalization;
using System.Linq;
using System;

namespace Stores
{
    /// <summary>Printouts of a physical stock count: the blind count sheet and the variance report.
    /// Both are ordinary ReportData, so screen, print and Excel export work as for every report.</summary>
    static class CountReports
    {
        static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

        static List<KeyValuePair<string, string>> Head(CountDoc c)
        {
            List<KeyValuePair<string, string>> i = new List<KeyValuePair<string, string>>();
            i.Add(new KeyValuePair<string, string>("رقم الجرد", c.No));
            i.Add(new KeyValuePair<string, string>("التاريخ", UI.Date(c.Date)));
            i.Add(new KeyValuePair<string, string>("المخزن", Cache.Store(c.Store)));
            i.Add(new KeyValuePair<string, string>("نوع المواد", c.TypeNo == 0 ? "كل الأنواع" : Cache.Type(c.TypeNo)));
            if (c.Posted) i.Add(new KeyValuePair<string, string>("الحالة", "مُرحّل بتاريخ " + UI.Date(c.PostedAt)));
            return i;
        }

        static string Subtitle(CountDoc c)
        {
            return string.Join("     |     ", Head(c).Select(kv => kv.Key + ": " + kv.Value).ToArray());
        }

        static IEnumerable<CountLine> Sorted(CountDoc c, bool byCode)
        {
            return byCode ? c.Lines.OrderBy(l => Cache.ItemCode(l.ItemNo), StringComparer.Ordinal).ThenBy(l => Cache.ItemName(l.ItemNo))
                          : c.Lines.OrderBy(l => Cache.ItemName(l.ItemNo)).ThenBy(l => Cache.ItemCode(l.ItemNo), StringComparer.Ordinal);
        }

        /// <summary>Count sheet order: by shelf location first (the team walks the shelves; lines without a location
        /// at the end), then by code (or name). Without any location this is exactly <see cref="Sorted"/>.</summary>
        static IEnumerable<CountLine> SortedForSheet(CountDoc c, bool byCode)
        {
            if (!c.Lines.Any(l => Cache.Bin(l.ItemNo, c.Store).Length > 0)) return Sorted(c, byCode);
            IOrderedEnumerable<CountLine> o = c.Lines.OrderBy(l => Cache.Bin(l.ItemNo, c.Store).Length == 0 ? 1 : 0)
                                                     .ThenBy(l => Cache.Bin(l.ItemNo, c.Store), StringComparer.OrdinalIgnoreCase);
            return byCode ? o.ThenBy(l => Cache.ItemCode(l.ItemNo), StringComparer.Ordinal).ThenBy(l => Cache.ItemName(l.ItemNo))
                          : o.ThenBy(l => Cache.ItemName(l.ItemNo)).ThenBy(l => Cache.ItemCode(l.ItemNo), StringComparer.Ordinal);
        }

        static string TypeName(int item) { DataRow ir = Cache.Item(item); return ir == null ? "" : Db.S(ir["type_name"]); }

        /// <summary>Blank count sheet WITHOUT book quantities (blind count), for the counting team.</summary>
        public static ReportData Sheet(CountDoc c, bool byCode)
        {
            ReportData r = new ReportData();
            r.Title = "ورقة الجرد الفعلي رقم " + c.No;
            r.Subtitle = Subtitle(c);
            r.Info = Head(c);
            bool bins = c.Lines.Any(l => Cache.Bin(l.ItemNo, c.Store).Length > 0);
            r.Col("ت", ColKind.Int); r.Col("الرمز", ColKind.Text); r.Col("اسم المادة", ColKind.Text); r.Col("النوع", ColKind.Text);
            if (bins) r.Col("الموقع", ColKind.Text);
            r.Col("الكمية الموجودة فعلا", ColKind.Text); r.Col("ملاحظات", ColKind.Text);
            // non-breaking spaces keep room for handwriting in the blank columns
            string qtyBox = new string('\u00A0', 24), noteBox = new string('\u00A0', 30);
            int n = 0;
            foreach (CountLine l in SortedForSheet(c, byCode))
            {
                if (bins) r.Row(null, ++n, Cache.ItemCode(l.ItemNo), Cache.ItemName(l.ItemNo), TypeName(l.ItemNo), Cache.Bin(l.ItemNo, c.Store), qtyBox, noteBox);
                else r.Row(null, ++n, Cache.ItemCode(l.ItemNo), Cache.ItemName(l.ItemNo), TypeName(l.ItemNo), qtyBox, noteBox);
            }
            r.Footer = (bins ? "الورقة مرتبة حسب موقع المادة على الرفوف ثم " + (byCode ? "الرمز" : "الاسم") + ". " : "") + "اكتب الكمية الموجودة فعلا على الرف لكل مادة. المواد غير الموجودة في القائمة تُكتب في آخر الورقة وتُضاف في البرنامج. " +
                       "عدد المواد: " + n.ToString("#,##0", Inv) + ".";
            r.Signatures = true;
            r.SigLabels = new[] { "لجنة الجرد", "أمين المخزن", "المدير" };
            return r;
        }

        /// <summary>Book vs counted vs difference per item (shortage / surplus) with totals.</summary>
        public static ReportData Variance(CountDoc c, bool onlyDiff)
        {
            ReportData r = new ReportData();
            r.Title = "فروقات الجرد الفعلي رقم " + c.No;
            r.Subtitle = Subtitle(c);
            r.Col("ت", ColKind.Int); r.Col("الرمز", ColKind.Text); r.Col("اسم المادة", ColKind.Text); r.Col("النوع", ColKind.Text);
            r.ColT("الرصيد الدفتري"); r.ColT("الكمية المعدودة"); r.ColT("الفرق"); r.ColT("النقص"); r.ColT("الزيادة"); r.Col("الحالة", ColKind.Text);
            int n = 0, counted = 0, match = 0, nShort = 0, nSur = 0, notCounted = 0;
            double qShort = 0, qSur = 0;
            foreach (CountLine l in Sorted(c, false))
            {
                double d = l.Diff;
                string state; string cls = null;
                if (!l.Counted.HasValue) { notCounted++; state = "لم يُعد"; cls = "muted"; }
                else
                {
                    counted++;
                    if (d < -0.0000001) { nShort++; qShort += -d; state = "نقص"; cls = "neg"; }
                    else if (d > 0.0000001) { nSur++; qSur += d; state = "زيادة"; cls = "in"; }
                    else { match++; state = "مطابق"; }
                }
                if (l.Added) state += " (أُضيفت أثناء الجرد)";
                if (onlyDiff && (!l.Counted.HasValue || Math.Abs(d) <= 0.0000001)) continue;
                r.Row(cls, ++n, Cache.ItemCode(l.ItemNo), Cache.ItemName(l.ItemNo), TypeName(l.ItemNo), l.Book,
                    l.Counted.HasValue ? (object)l.Counted.Value : null, l.Counted.HasValue ? (object)d : null,
                    d < -0.0000001 ? (object)(-d) : null, d > 0.0000001 ? (object)d : null, state);
                r.Link(RowLinks.Item(l.ItemNo));
            }
            r.Sum("عدد المواد", c.Lines.Count.ToString("#,##0", Inv));
            r.Sum("المعدودة", counted.ToString("#,##0", Inv));
            r.Sum("مطابقة", match.ToString("#,##0", Inv));
            r.Sum("مواد فيها نقص", nShort.ToString("#,##0", Inv) + " (" + UI.Num(qShort) + ")");
            r.Sum("مواد فيها زيادة", nSur.ToString("#,##0", Inv) + " (" + UI.Num(qSur) + ")");
            if (notCounted > 0) r.Sum("لم تُعد", notCounted.ToString("#,##0", Inv));
            if (c.Posted)
                r.Footer = "تم ترحيل التسوية بتاريخ " + UI.Date(c.PostedAt) + " بواسطة " + c.PostedBy +
                           (c.SaleNo.Length > 0 ? "   —   النقص: إخراج مخزني (تسوية جرد) رقم " + c.SaleNo : "") +
                           (c.ShopNo.Length > 0 ? "   —   الزيادة: إدخال مخزني (تسوية جرد) رقم " + c.ShopNo : "") + ".";
            else
                r.Footer = "الفرق = الكمية المعدودة − الرصيد الدفتري (الرصيد عند إنشاء الجرد). المواد التي لم تُعد لا تُسوّى عند الترحيل.";
            r.Signatures = true;
            r.SigLabels = new[] { "لجنة الجرد", "أمين المخزن", "المدير" };
            return r;
        }
    }
}
