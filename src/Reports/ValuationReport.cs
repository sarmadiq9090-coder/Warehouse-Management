using System.Collections.Generic;
using System.Data;
using System.Globalization;
using System.Linq;
using System;

namespace Stores
{
    /// <summary>«تقييم المخزون بتاريخ»: quantity and FIFO cost value of every item at the end of a day (IAS 2), see Valuation.</summary>
    static class ValuationReport
    {
        static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

        public static ReportData Run(ReportParams p)
        {
            ReportData r = new ReportData();
            r.Title = "تقييم المخزون بتاريخ " + UI.Date(p.To);
            r.Subtitle = "الكمية والقيمة في نهاية يوم " + UI.Date(p.To) + "   —   " + (p.Store == 0 ? "كل المخازن" : "مخزن: " + Cache.Store(p.Store)) +
                         (p.TypeNo > 0 ? "   —   النوع: " + Cache.Type(p.TypeNo) : "") + "   —   طريقة الكلفة: الوارد أولا صادر أولا (FIFO)";
            Dictionary<int, Valuation.ItemValue> val = Valuation.At(p.To);
            r.Col("ت", ColKind.Int); r.Col("الرمز", ColKind.Text); r.Col("اسم المادة", ColKind.Text); r.Col("الوحدة", ColKind.Text);
            r.ColT("الكمية"); r.Col("متوسط كلفة الوحدة", ColKind.Num); r.ColT("القيمة");
            int n = 0, unknown = 0, neg = 0; double total = 0;
            foreach (DataRow ir in Cache.Items.Select("", "item_name"))
            {
                if (!Matches(ir, p)) continue;
                int it = Db.I(ir["item_no"]);
                Valuation.ItemValue v;
                if (!val.TryGetValue(it, out v)) continue;
                double q = Math.Round(v.StoreQty(p.Store), 6);
                if (Math.Abs(q) < 0.0001) continue;
                bool noCost = v.LastCost <= 0 || (q > 0 && v.UnknownQty > 0.0001);
                if (p.Flag && v.LastCost <= 0) continue;          // only items with a known cost
                double value = Math.Round(v.StoreValue(p.Store), 2);
                n++; total += value;
                if (noCost) unknown++;
                if (q < -0.0001) neg++;
                r.Row(q < -0.0001 ? "neg" : (v.LastCost <= 0 ? "muted" : null), n, Db.S(ir["item_code"]), Db.S(ir["item_name"]), Db.S(ir["unit"]),
                      q, v.LastCost <= 0 ? null : (object)Math.Round(v.UnitCost, 4), value);
                r.Link(RowLinks.Item(it));
            }
            r.Sum("عدد المواد", n.ToString("#,##0", Inv));
            r.Sum("قيمة المخزون", UI.Num(Math.Round(total, 2)));
            if (unknown > 0) r.Sum("مواد كلفتها غير معروفة كليا أو جزئيا", unknown.ToString("#,##0", Inv));
            if (neg > 0) r.Sum("أرصدة سالبة", neg.ToString("#,##0", Inv));
            r.Footer = "طريقة التقييم: حسب المعيار المحاسبي الدولي IAS 2 بطريقة الوارد أولا صادر أولا (FIFO): الكمية الموجودة تُقيَّم بكلفة أحدث الإدخالات حتى التاريخ، " +
                       "والرصيد الافتتاحي بأقدم كلفة معروفة للمادة. قيمة المادة في مخزن = كميتها فيه × متوسط كلفة الوحدة للمادة. " +
                       "المواد باللون الرمادي ليس لها أي سعر في سندات الإدخال فقيمتها صفر؛ يمكن إدخال السعر في سند الإدخال (الوضع المتقدم). " +
                       "مردود الإخراج يدخل بالكلفة التي أُخرج بها، والإدخال بدون سعر يأخذ آخر كلفة معروفة للمادة.";
            r.Signatures = true;
            return r;
        }

        static bool Matches(DataRow ir, ReportParams p)
        {
            if (p.TypeNo > 0 && Db.I(ir["type"]) != p.TypeNo) return false;
            if (p.Text.Length > 0)
            {
                string s = Db.S(ir["_s"]);
                foreach (string w in UI.Norm(p.Text).Split(new char[] { ' ' }, StringSplitOptions.RemoveEmptyEntries))
                    if (s.IndexOf(w, StringComparison.Ordinal) < 0) return false;
            }
            return true;
        }
    }
}
