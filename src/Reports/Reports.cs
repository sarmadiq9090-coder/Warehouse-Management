using System.Collections.Generic;
using System.Data;
using System.Globalization;
using System.Linq;
using System.Text;
using System;

namespace Stores
{
    static class Reports
    {
        static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

        public static List<ReportDef> All = new List<ReportDef>
        {
            new ReportDef("stock", "المخزون", "جرد أرصدة المواد", RP.AsOf | RP.Store | RP.Type | RP.Text | RP.Flag, "إظهار المواد ذات الرصيد صفر", "رصيد كل مادة في كل مخزن بتاريخ معين", Stock),
            new ReportDef("valuation", "المخزون", "تقييم المخزون بتاريخ", RP.AsOf | RP.Store | RP.Type | RP.Text | RP.Flag, "المواد ذات الكلفة المعروفة فقط", "كمية وقيمة كل مادة بتاريخ معين بطريقة الوارد أولا صادر أولا (FIFO) حسب المعيار IAS 2", ValuationReport.Run),
            new ReportDef("critical", "المخزون", "الأرصدة الحرجة (تحتاج تعزيز)", RP.Store | RP.Type | RP.Months, null, "مواد رصيدها الحر أقل من حد الطلب أو من استهلاك شهر — مع الكمية المقترحة للتعزيز", Critical),
            new ReportDef("card", "المخزون", "كارت المادة (حركة مادة)", RP.Dates | RP.Store | RP.Item | RP.ItemReq, null, "كل حركات المادة مع الرصيد بعد كل حركة", ItemCard),
            new ReportDef("stagnant", "المخزون", "المواد الراكدة", RP.Since | RP.Store | RP.Type, null, "مواد لها رصيد ولم يُخرج منها شيء منذ تاريخ معين", Stagnant),
            new ReportDef("writeoffs", "المخزون", "الإتلاف حسب السبب", RP.Dates | RP.Store | RP.Item | RP.Flag, "تفصيل حسب المادة", "سندات الإتلاف / الشطب المعتمدة لكل سبب: الكميات والكلفة", WriteOffs.ByReason),
            new ReportDef("negative", "المخزون", "الأرصدة السالبة", RP.None, null, "مواد رصيدها أقل من صفر وتحتاج مراجعة", Negative),

            new ReportDef("project", "مراكز الكلفة", "كشف مركز كلفة", RP.Dates | RP.Project | RP.ProjectReq | RP.Flag, "تفصيل حسب السندات", "كل ما أُخرج لمركز الكلفة وما أُعيد منه والصافي والكلفة", ProjectStatement),
            new ReportDef("byprojects", "مراكز الكلفة", "الإخراج حسب مراكز الكلفة", RP.Dates | RP.Store, null, "ملخص الكميات والكلفة لكل مركز كلفة، مع مجموع كل نوع", ByProjects),
            new ReportDef("reservations", "مراكز الكلفة", "الحجوزات القائمة", RP.Project | RP.Item, null, "المواد المحجوزة لمراكز الكلفة: المحجوز والمُخرج والمتبقي", ReservationsReport),

            new ReportDef("issues", "الإخراج", "سجل سندات الإخراج المخزني", RP.Dates | RP.Store | RP.Person | RP.Project, null, "قائمة سندات الإخراج خلال فترة", Issues),
            new ReportDef("issuedetail", "الإخراج", "الإخراج التفصيلي", RP.Dates | RP.Store | RP.Person | RP.Project | RP.Item | RP.Type, null, "كل مادة أُخرجت: التاريخ ومركز الكلفة والمستلم والكمية", IssueDetail),
            new ReportDef("topitems", "الإخراج", "أكثر المواد إخراجا", RP.Dates | RP.Store | RP.Type | RP.Top, null, "ترتيب المواد حسب الكمية المُخرجة", TopItems),
            new ReportDef("bypersons", "الإخراج", "الإخراج حسب المستلمين", RP.Dates | RP.Store, null, "عدد السندات والكميات لكل مستلم", ByPersons),
            new ReportDef("mreq", "الإخراج", "طلبات الصرف", RP.Dates | RP.Store | RP.Project | RP.Item, null, "طلبات صرف المواد وحالتها: المطلوب والمصروف والمتبقي", Requisitions.Report),
            new ReportDef("person", "الإخراج", "كشف جهة / شخص", RP.Dates | RP.Person | RP.PersonReq | RP.Flag, "تجميع حسب المادة", "كل ما أُخرج للجهة وما أعادته أو جهّزته", PersonStatement),

            new ReportDef("receipts", "الإدخال والمردودات", "سجل سندات الإدخال والمردود", RP.Dates | RP.RType | RP.Person, null, "سندات الإدخال المخزني ومردود الإخراج خلال فترة", Receipts),
            new ReportDef("receiptdetail", "الإدخال والمردودات", "الإدخال التفصيلي", RP.Dates | RP.RType | RP.Person | RP.Item | RP.Type, null, "كل مادة أُدخلت: التاريخ والمجهز والكمية", ReceiptDetail),
            new ReportDef("purchreturns", "الإدخال والمردودات", "مردودات المشتريات", RP.Dates | RP.Person | RP.Item, null, "المواد المُرجعة إلى المجهزين", PurchaseReturns),
            new ReportDef("transfers", "الإدخال والمردودات", "النقل المخزني", RP.Dates | RP.Item, null, "حركات النقل بين المخازن", Transfers),
            new ReportDef("openpreq", "الإدخال والمردودات", "طلبات الشراء المفتوحة", RP.Store | RP.Item | RP.Person | RP.Flag, "إظهار المسودات أيضا", "المطلوب والمستلم والمتبقي لكل مادة وعمر الطلب بالأيام", PurchaseReports.Open),

            new ReportDef("monthly", "ملخصات", "الملخص الشهري", RP.Dates, null, "عدد السندات والكميات لكل شهر مع رسم بياني", Monthly),
            new ReportDef("itemlist", "ملخصات", "دليل المواد", RP.Type | RP.Text, null, "أسماء ورموز المواد", ItemList),
            new ReportDef("personlist", "ملخصات", "دليل الجهات", RP.Text, null, "أسماء الجهات والأشخاص", PersonList),
            new ReportDef("audit", "ملخصات", "تدقيق البيانات", RP.None, null, "فحص سلامة البيانات والأرصدة", Audit),
            new ReportDef("audittrail", "ملخصات", "سجل التدقيق (قبل / بعد)", RP.Dates | RP.Text, null, "من عدّل أو حذف السندات والمواد ومراكز الكلفة، مع القيم قبل وبعد التعديل", AuditTrail.Report),
        };

        /// <summary>Reports shown in beginner mode.</summary>
        public static readonly string[] Basic = { "stock", "critical", "card", "project", "reservations", "issues", "receipts" };

        public static ReportDef Get(string key) { return All.FirstOrDefault(r => r.Key == key); }

        // ------------------------------------------------------------ movement source
        /// <summary>All stock movements as one union: d=date, st=store, it=item, q=signed qty,
        /// k=kind (1 receipt, 2 issue, 3 return, 4 transfer in, 5 transfer out, 6 purchase return, 7 count shortage, 8 count surplus, 9 write-off), no, p=person, id=document.
        /// cond may use {D} (date), {I} (item), {S} (store); parameters are repeated for each part by the caller.</summary>
        public static string MvSql(string cond)
        {
            string[] parts = {
                "SELECT h.menu_date AS d, h.stor AS st, l.item_no AS it, l.qty AS q, " + (Db.HasCount ? "IIF(h.shop_type = 3, 3, IIF(h.doc_kind = 2, 8, 1))" : "IIF(h.shop_type = 3, 3, 1)") + " AS k, h.menu_no AS dno, h.custom_no AS p, h.shop_no AS id FROM shop AS h INNER JOIN shop_menu AS l ON h.shop_no = l.seq WHERE {C}",
                "SELECT h.menu_date, h.stor, l.item_no, -l.qty, " + (Db.HasCount ? "IIF(h.doc_kind = 1, 6, IIF(h.doc_kind = 2, 7, IIF(h.doc_kind = " + WriteOffs.SaleKind + ", 9, 2)))" : Db.HasProj ? "IIF(h.doc_kind = 1, 6, 2)" : "2") + ", h.menu_no, h.custom_no, h.shop_no FROM sale AS h INNER JOIN sale_menu AS l ON h.shop_no = l.seq WHERE {C}",
                "SELECT h.menu_date, h.stor_in, l.item_no, l.qty, 4, h.menu_no, h.stor_out, h.shop_no FROM trans AS h INNER JOIN trans_menu AS l ON h.shop_no = l.seq WHERE {C}",
                "SELECT h.menu_date, h.stor_out, l.item_no, -l.qty, 5, h.menu_no, h.stor_in, h.shop_no FROM trans AS h INNER JOIN trans_menu AS l ON h.shop_no = l.seq WHERE {C}" };
            string[] stores = { "h.stor", "h.stor", "h.stor_in", "h.stor_out" };
            StringBuilder sb = new StringBuilder();
            for (int i = 0; i < 4; i++)
            {
                if (i > 0) sb.Append(" UNION ALL ");
                string c = cond.Replace("{D}", "h.menu_date").Replace("{I}", "l.item_no").Replace("{S}", stores[i]);
                sb.Append(parts[i].Replace("{C}", c));
            }
            return sb.ToString();
        }

        static object[] Rep(int times, params object[] a)
        {
            List<object> l = new List<object>();
            for (int i = 0; i < times; i++) l.AddRange(a);
            return l.ToArray();
        }

        public static string KindText(int k)
        {
            switch (k) { case 1: return "إدخال مخزني"; case 2: return "إخراج مخزني"; case 3: return "مردود إخراج"; case 4: return "نقل وارد"; case 5: return "نقل صادر"; case 6: return "مردود مشتريات"; case 7: return "تسوية جرد (نقص)"; case 8: return "تسوية جرد (زيادة)"; case 9: return "إتلاف / شطب"; default: return ""; }
        }

        static string Period(ReportParams p) { return "من " + UI.Date(p.From) + " إلى " + UI.Date(p.To); }
        static string StoreText(ReportParams p) { return p.Store == 0 ? "كل المخازن" : "مخزن: " + Cache.Store(p.Store); }
        static string Unit(int item) { return Cache.ItemUnit(item); }

        static bool ItemMatches(DataRow ir, ReportParams p) { return ItemMatches(ir, p, null); }

        /// <summary>Type + text filter; <paramref name="extra"/> is searched too (e.g. the shelf location).</summary>
        static bool ItemMatches(DataRow ir, ReportParams p, string extra)
        {
            if (ir == null) return p.TypeNo == 0 && p.Text.Length == 0;
            if (p.TypeNo > 0 && Db.I(ir["type"]) != p.TypeNo) return false;
            if (p.Text.Length > 0)
            {
                string s = Db.S(ir["_s"]) + (string.IsNullOrEmpty(extra) ? "" : " " + UI.Norm(extra));
                foreach (string w in UI.Norm(p.Text).Split(new char[] { ' ' }, StringSplitOptions.RemoveEmptyEntries))
                    if (s.IndexOf(w, StringComparison.Ordinal) < 0) return false;
            }
            return true;
        }

        /// <summary>Balance per (item, store) at the end of a day: opening + movements.</summary>
        static Dictionary<int, Dictionary<int, double>> Balances(DateTime? before)
        {
            Dictionary<int, Dictionary<int, double>> b = new Dictionary<int, Dictionary<int, double>>();
            Action<int, int, double> add = delegate(int it, int st, double v)
            {
                Dictionary<int, double> m;
                if (!b.TryGetValue(it, out m)) { m = new Dictionary<int, double>(); b[it] = m; }
                double o; m.TryGetValue(st, out o); m[st] = o + v;
            };
            foreach (DataRow r in Db.Query("SELECT item_no, stor, item_bal_f FROM item_stor WHERE item_bal_f <> 0").Rows)
                add(Db.I(r[0]), Db.I(r[1]), Db.D(r[2]));
            DataTable mv = before.HasValue
                ? Db.Query("SELECT it, st, SUM(q) AS b FROM (" + MvSql("{D} < ?") + ") AS m GROUP BY it, st", Rep(4, before.Value))
                : Db.Query("SELECT it, st, SUM(q) AS b FROM (" + MvSql("1=1") + ") AS m GROUP BY it, st");
            foreach (DataRow r in mv.Rows) add(Db.I(r["it"]), Db.I(r["st"]), Db.D(r["b"]));
            return b;
        }

        // ------------------------------------------------------------ stock
        static ReportData Stock(ReportParams p)
        {
            ReportData r = new ReportData();
            r.Title = "جرد أرصدة المواد";
            r.Subtitle = "الرصيد في نهاية يوم " + UI.Date(p.To) + "   —   " + StoreText(p) + (p.TypeNo > 0 ? "   —   النوع: " + Cache.Type(p.TypeNo) : "");
            Dictionary<int, Dictionary<int, double>> bal = Balances(p.ToEx);
            // columns: stores that have any balance
            List<int> stores = new List<int>();
            if (p.Store == 0)
            {
                HashSet<int> used = new HashSet<int>();
                foreach (Dictionary<int, double> m in bal.Values) foreach (KeyValuePair<int, double> kv in m) if (Math.Abs(kv.Value) > 0.0001) used.Add(kv.Key);
                foreach (DataRow s in Cache.Stores.Rows) if (used.Contains(Db.I(s[0]))) stores.Add(Db.I(s[0]));
            }
            r.Col("ت", ColKind.Int); r.Col("الرمز", ColKind.Text); r.Col("اسم المادة", ColKind.Text); r.Col("النوع", ColKind.Text); r.Col("الوحدة", ColKind.Text);
            bool bins = Db.HasBin && Cache.Bins.Count > 0;   // «الموقع» (shelf location) once any item has one
            if (bins) r.Col("الموقع", ColKind.Text);
            bool multi = stores.Count > 1;
            if (multi) foreach (int s in stores) r.ColT(Cache.Store(s));
            r.ColT(multi ? "المجموع" : "الرصيد");
            // FIFO value (see Valuation): only when some item has a known cost
            Dictionary<int, Valuation.ItemValue> val = Valuation.At(p.To, bal);
            bool hasValue = val.Values.Any(v => Math.Abs(v.Value) > 0.005);
            if (hasValue) r.ColT("القيمة");
            int n = 0; double total = 0, tval = 0; int neg = 0;
            foreach (DataRow ir in Cache.Items.Select("", "item_name"))
            {
                int it = Db.I(ir["item_no"]);
                string bin = bins ? Cache.Bin(it, p.Store) : "";
                if (!ItemMatches(ir, p, bin)) continue;
                Dictionary<int, double> m; bal.TryGetValue(it, out m);
                double tot = 0;
                List<object> vals = new List<object>();
                if (m != null)
                {
                    if (p.Store > 0) { double v; m.TryGetValue(p.Store, out v); tot = v; }
                    else foreach (double v in m.Values) tot += v;
                }
                if (Math.Abs(tot) < 0.0001 && !p.Flag && (m == null || m.Values.All(v => Math.Abs(v) < 0.0001))) continue;
                if (Db.B(ir["freez"]) && Math.Abs(tot) < 0.0001) continue;
                n++;
                vals.Add(n); vals.Add(Db.S(ir["item_code"])); vals.Add(Db.S(ir["item_name"])); vals.Add(Db.S(ir["type_name"])); vals.Add(Db.S(ir["unit"]));
                if (bins) vals.Add(bin);
                if (multi) foreach (int s in stores) { double v = 0; if (m != null) m.TryGetValue(s, out v); vals.Add(v); }
                vals.Add(tot);
                if (hasValue) { Valuation.ItemValue iv; double x = val.TryGetValue(it, out iv) ? Math.Round(iv.StoreValue(p.Store), 2) : 0; vals.Add(x); tval += x; }
                total += tot; if (tot < -0.0001) neg++;
                r.Row(tot < -0.0001 ? "neg" : null, vals.ToArray()); r.Link(RowLinks.Item(it));
            }
            r.Sum("عدد المواد", n.ToString("#,##0", Inv));
            r.Sum("مجموع الكميات", UI.Num(total));
            if (hasValue) r.Sum("قيمة المخزون (FIFO)", UI.Num(Math.Round(tval, 2)));
            if (neg > 0) r.Sum("أرصدة سالبة", neg.ToString());
            r.Signatures = true;
            return r;
        }

        // ------------------------------------------------------------ item card
        public static ReportData ItemCard(ReportParams p)
        {
            ReportData r = new ReportData();
            int it = p.ItemNo;
            r.Title = "كارت المادة: " + Cache.ItemName(it);
            r.Subtitle = "الرمز: " + Cache.ItemCode(it) + (Unit(it).Length > 0 ? "   —   الوحدة: " + Unit(it) : "") + "   —   " + Period(p) + "   —   " + StoreText(p);
            string sc = p.Store > 0 ? " AND {S} = ?" : "";
            List<object> a = new List<object> { it, p.From }; if (p.Store > 0) a.Add(p.Store);
            double open = Db.D(p.Store > 0 ? Db.Scalar("SELECT SUM(item_bal_f) FROM item_stor WHERE item_no = ? AND stor = ?", it, p.Store)
                                           : Db.Scalar("SELECT SUM(item_bal_f) FROM item_stor WHERE item_no = ?", it));
            open += Db.D(Db.Scalar("SELECT SUM(q) FROM (" + MvSql("{I} = ? AND {D} < ?" + sc) + ") AS m", Rep(4, a.ToArray())));
            List<object> b = new List<object> { it, p.From, p.ToEx }; if (p.Store > 0) b.Add(p.Store);
            DataTable t = Db.Query("SELECT d, st, q, k, dno, p, id FROM (" + MvSql("{I} = ? AND {D} >= ? AND {D} < ?" + sc) + ") AS m ORDER BY d, k, id", Rep(4, b.ToArray()));
            r.Col("التاريخ", ColKind.Date); r.Col("الحركة", ColKind.Text); r.Col("رقم السند", ColKind.Text); r.Col("الجهة / المخزن", ColKind.Text);
            r.Col("المخزن", ColKind.Text); r.ColT("وارد"); r.ColT("صادر"); r.Col("الرصيد", ColKind.Num);
            double run = open, tin = 0, tout = 0;
            r.Row("open", null, "رصيد سابق", "", "", "", null, null, open);
            foreach (DataRow x in t.Rows)
            {
                int k = Db.I(x["k"]); double q = Db.D(x["q"]);
                if (p.Store == 0 && (k == 4 || k == 5)) { }
                run += q;
                if (q >= 0) tin += q; else tout += -q;
                string party = k == 4 || k == 5 ? (k == 4 ? "من " : "إلى ") + Cache.Store(Db.I(x["p"])) : k == 7 || k == 8 ? "الجرد الفعلي" : k == 9 ? "سند إتلاف" : Cache.Person(Db.I(x["p"]));
                r.Row(run < -0.0001 ? "neg" : null, Db.Dt(x["d"]), KindText(k), Db.S(x["dno"]), party, Cache.Store(Db.I(x["st"])),
                    q >= 0 ? (object)q : null, q < 0 ? (object)(-q) : null, run);
                r.Link(RowLinks.Move(k, Db.I(x["id"])));
            }
            r.Sum("الرصيد السابق", UI.Num(open)); r.Sum("مجموع الوارد", UI.Num(tin)); r.Sum("مجموع الصادر", UI.Num(tout)); r.Sum("الرصيد النهائي", UI.Num(run));
            r.Footer = "الرصيد الحالي المسجل في كل المخازن: " + UI.Num(Cache.StockAll(it));
            return r;
        }

        // ------------------------------------------------------------ reorder / stagnant / negative
        static ReportData Reorder(ReportParams p)
        {
            ReportData r = new ReportData();
            r.Title = "مواد وصلت حد إعادة الطلب"; r.Subtitle = StoreText(p);
            r.Col("ت", ColKind.Int); r.Col("الرمز", ColKind.Text); r.Col("اسم المادة", ColKind.Text); r.Col("النوع", ColKind.Text);
            r.Col("حد إعادة الطلب", ColKind.Num); r.Col("الرصيد", ColKind.Num); r.Col("النقص", ColKind.Num);
            DataTable t = p.Store > 0
                ? Db.Query("SELECT i.item_no, i.item_bal_ret, s.b FROM item AS i LEFT JOIN (SELECT item_no, SUM(item_bal) AS b FROM item_stor WHERE stor = ? GROUP BY item_no) AS s ON i.item_no = s.item_no WHERE i.item_bal_ret > 0", p.Store)
                : Db.Query("SELECT i.item_no, i.item_bal_ret, s.b FROM item AS i LEFT JOIN (SELECT item_no, SUM(item_bal) AS b FROM item_stor GROUP BY item_no) AS s ON i.item_no = s.item_no WHERE i.item_bal_ret > 0");
            int n = 0;
            foreach (DataRow x in t.Rows)
            {
                int it = Db.I(x[0]); double ret = Db.D(x[1]), b = Db.D(x[2]);
                DataRow ir = Cache.Item(it);
                if (b > ret || !ItemMatches(ir, p) || (ir != null && Db.B(ir["freez"]))) continue;
                r.Row(b <= 0 ? "neg" : null, ++n, Cache.ItemCode(it), Cache.ItemName(it), ir == null ? "" : Db.S(ir["type_name"]), ret, b, ret - b); r.Link(RowLinks.Item(it));
            }
            r.Sum("عدد المواد", n.ToString());
            if (Db.I(Db.Scalar("SELECT COUNT(*) FROM item WHERE item_bal_ret > 0")) < 5)
                r.Footer = "ملاحظة: قليل من المواد لها حد إعادة طلب. يمكن تحديده من شاشة المواد ← تعديل.";
            return r;
        }

        static ReportData Stagnant(ReportParams p)
        {
            ReportData r = new ReportData();
            r.Title = "المواد الراكدة"; r.Subtitle = "مواد لها رصيد ولم يُخرج منها شيء منذ " + UI.Date(p.From) + "   —   " + StoreText(p);
            string sc = p.Store > 0 ? " AND h.stor = ?" : "";
            List<object> a = new List<object>(); if (p.Store > 0) a.Add(p.Store);
            Dictionary<int, DateTime> lastIssue = new Dictionary<int, DateTime>(), lastRec = new Dictionary<int, DateTime>();
            foreach (DataRow x in Db.Query("SELECT l.item_no, MAX(h.menu_date) FROM sale AS h INNER JOIN sale_menu AS l ON h.shop_no = l.seq WHERE " + Db.IssueOnly("h") + sc + " GROUP BY l.item_no", a.ToArray()).Rows)
                lastIssue[Db.I(x[0])] = Db.Dt(x[1]);
            foreach (DataRow x in Db.Query("SELECT l.item_no, MAX(h.menu_date) FROM shop AS h INNER JOIN shop_menu AS l ON h.shop_no = l.seq WHERE 1=1" + sc + " GROUP BY l.item_no", a.ToArray()).Rows)
                lastRec[Db.I(x[0])] = Db.Dt(x[1]);
            DataTable bal = p.Store > 0 ? Db.Query("SELECT item_no, SUM(item_bal) FROM item_stor WHERE stor = ? GROUP BY item_no", p.Store)
                                        : Db.Query("SELECT item_no, SUM(item_bal) FROM item_stor GROUP BY item_no");
            r.Col("ت", ColKind.Int); r.Col("الرمز", ColKind.Text); r.Col("اسم المادة", ColKind.Text); r.Col("النوع", ColKind.Text); r.Col("الوحدة", ColKind.Text);
            r.ColT("الرصيد"); r.Col("آخر إخراج", ColKind.Date); r.Col("آخر إدخال", ColKind.Date); r.Col("أيام بلا إخراج", ColKind.Int);
            List<object[]> rows = new List<object[]>(); Dictionary<object[], string> links = new Dictionary<object[], string>();
            foreach (DataRow x in bal.Rows)
            {
                int it = Db.I(x[0]); double b = Db.D(x[1]);
                if (b <= 0.0001) continue;
                DataRow ir = Cache.Item(it); if (!ItemMatches(ir, p)) continue;
                DateTime li; bool has = lastIssue.TryGetValue(it, out li);
                if (has && li >= p.From) continue;
                DateTime lr; bool hasR = lastRec.TryGetValue(it, out lr);
                int days = has ? (int)(DateTime.Today - li).TotalDays : -1;
                rows.Add(new object[] { 0, Cache.ItemCode(it), Cache.ItemName(it), ir == null ? "" : Db.S(ir["type_name"]), Unit(it), b, has ? (object)li : null, hasR ? (object)lr : null, days < 0 ? null : (object)days });
                links[rows[rows.Count - 1]] = RowLinks.Item(it);
            }
            int n = 0;
            foreach (object[] o in rows.OrderBy(o => o[6] == null ? DateTime.MinValue : (DateTime)o[6])) { o[0] = ++n; r.Row(null, o); r.Link(links[o]); }
            r.Sum("عدد المواد الراكدة", n.ToString("#,##0", Inv));
            return r;
        }

        static ReportData Negative(ReportParams p)
        {
            ReportData r = new ReportData();
            r.Title = "الأرصدة السالبة"; r.Subtitle = "مواد أُخرج منها أكثر من رصيدها — يجب تدقيقها";
            r.Col("ت", ColKind.Int); r.Col("المخزن", ColKind.Text); r.Col("الرمز", ColKind.Text); r.Col("اسم المادة", ColKind.Text); r.Col("الوحدة", ColKind.Text); r.ColT("الرصيد");
            int n = 0;
            foreach (DataRow x in Db.Query("SELECT stor, item_no, item_bal FROM item_stor WHERE item_bal < -0.0001 ORDER BY stor, item_no").Rows)
            {
                r.Row("neg", ++n, Cache.Store(Db.I(x[0])), Cache.ItemCode(Db.I(x[1])), Cache.ItemName(Db.I(x[1])), Unit(Db.I(x[1])), Db.D(x[2]));
                r.Link(RowLinks.Item(Db.I(x[1])));
            }
            r.Sum("العدد", n.ToString());
            return r;
        }

        // ------------------------------------------------------------ issues
        static ReportData Issues(ReportParams p)
        {
            ReportData r = new ReportData();
            r.Title = "سجل سندات الإخراج المخزني";
            r.Subtitle = Period(p) + "   —   " + StoreText(p) + (p.PersonNo > 0 ? "   —   المستلم: " + Cache.Person(p.PersonNo) : "") + (p.ProjNo > 0 ? "   —   مركز الكلفة: " + Cache.Project(p.ProjNo) : "");
            string sql = "SELECT h.shop_no, h.menu_no, h.menu_date, h.custom_no, h.stor, h.[note], (SELECT COUNT(*) FROM sale_menu m WHERE m.seq = h.shop_no) AS n, " +
                         "(SELECT SUM(qty) FROM sale_menu m WHERE m.seq = h.shop_no) AS q" + (Db.HasProj ? ", h.proj_no" : ", 0 AS proj_no") + " FROM sale AS h WHERE " + Db.IssueOnly("h") + " AND h.menu_date >= ? AND h.menu_date < ?";
            List<object> a = new List<object> { p.From, p.ToEx };
            if (p.Store > 0) { sql += " AND h.stor = ?"; a.Add(p.Store); }
            if (p.PersonNo > 0) { sql += " AND h.custom_no = ?"; a.Add(p.PersonNo); }
            if (p.ProjNo > 0 && Db.HasProj) { sql += " AND h.proj_no = ?"; a.Add(p.ProjNo); }
            sql += " ORDER BY h.menu_date, h.shop_no";
            r.Col("ت", ColKind.Int); r.Col("رقم السند", ColKind.Text); r.Col("التاريخ", ColKind.Date); r.Col("مركز الكلفة", ColKind.Text); r.Col("المستلم", ColKind.Text);
            r.Col("المخزن", ColKind.Text); r.ColT("عدد المواد"); r.ColT("مجموع الكميات"); r.Col("ملاحظات", ColKind.Text);
            int n = 0;
            foreach (DataRow x in Db.Query(sql, a.ToArray()).Rows)
            {
                r.Row(null, ++n, Db.S(x["menu_no"]), Db.Dt(x["menu_date"]), Cache.Project(Db.I(x["proj_no"])), Cache.Person(Db.I(x["custom_no"])), Cache.Store(Db.I(x["stor"])), Db.D(x["n"]), Db.D(x["q"]), Db.S(x["note"]));
                r.Link(RowLinks.Doc(DocKind.Issue, Db.I(x["shop_no"])));
            }
            r.Sum("عدد السندات", n.ToString("#,##0", Inv));
            return r;
        }

        static ReportData IssueDetail(ReportParams p) { return Detail(p, 0); }
        static ReportData ReceiptDetail(ReportParams p) { return Detail(p, 1); }
        static ReportData PurchaseReturns(ReportParams p) { return Detail(p, 2); }

        /// <summary>mode 0 = issues, 1 = receipts / returns, 2 = purchase returns</summary>
        static ReportData Detail(ReportParams p, int mode)
        {
            bool sale = mode != 1;
            ReportData r = new ReportData();
            r.Title = mode == 0 ? "الإخراج التفصيلي" : mode == 1 ? "الإدخال التفصيلي" : "مردودات المشتريات";
            r.Subtitle = Period(p) + (mode == 0 ? "   —   " + StoreText(p) : (mode == 1 && p.RType > 0 ? "   —   " + Cache.ReceiptTypes[p.RType] : "")) +
                         (p.PersonNo > 0 ? "   —   " + Cache.Person(p.PersonNo) : "") + (p.ProjNo > 0 ? "   —   " + Cache.Project(p.ProjNo) : "") + (p.ItemNo > 0 ? "   —   " + Cache.ItemName(p.ItemNo) : "");
            string t = sale ? "sale" : "shop", l = sale ? "sale_menu" : "shop_menu";
            string proj = Db.HasProj && mode != 2 ? ", h.proj_no" : ", 0 AS proj_no";
            string sql = "SELECT h.shop_no, h.menu_no, h.menu_date, h.custom_no, h.stor, l.item_no, l.qty, l.price_dl" + (sale ? ", 0 AS shop_type" : ", h.shop_type") + proj +
                         " FROM " + t + " AS h INNER JOIN " + l + " AS l ON h.shop_no = l.seq WHERE h.menu_date >= ? AND h.menu_date < ?";
            if (mode == 0) sql += " AND " + Db.IssueOnly("h");
            if (mode == 1) sql += " AND " + Db.NotCountAdj("h");
            if (mode == 2) sql += Db.HasProj ? " AND h.doc_kind = 1" : " AND 1=0";
            List<object> a = new List<object> { p.From, p.ToEx };
            if (mode == 0 && p.Store > 0) { sql += " AND h.stor = ?"; a.Add(p.Store); }
            if (mode == 1 && p.RType > 0) { sql += " AND h.shop_type = ?"; a.Add(p.RType); }
            if (p.PersonNo > 0) { sql += " AND h.custom_no = ?"; a.Add(p.PersonNo); }
            if (mode == 0 && p.ProjNo > 0 && Db.HasProj) { sql += " AND h.proj_no = ?"; a.Add(p.ProjNo); }
            if (p.ItemNo > 0) { sql += " AND l.item_no = ?"; a.Add(p.ItemNo); }
            sql += " ORDER BY h.menu_date, h.shop_no, l.auto_seq";
            r.Col("ت", ColKind.Int); r.Col("التاريخ", ColKind.Date); r.Col("رقم السند", ColKind.Text);
            if (mode == 1) r.Col("النوع", ColKind.Text);
            if (mode != 2) r.Col("مركز الكلفة", ColKind.Text);
            r.Col(mode == 0 ? "المستلم" : "المجهز / الجهة", ColKind.Text); r.Col("الرمز", ColKind.Text); r.Col("اسم المادة", ColKind.Text); r.Col("الوحدة", ColKind.Text); r.ColT("الكمية");
            int n = 0; double tq = 0; HashSet<int> items = new HashSet<int>();
            foreach (DataRow x in Db.Query(sql, a.ToArray()).Rows)
            {
                int it = Db.I(x["item_no"]);
                if (p.TypeNo > 0) { DataRow ir = Cache.Item(it); if (ir == null || Db.I(ir["type"]) != p.TypeNo) continue; }
                double q = Db.D(x["qty"]); tq += q; items.Add(it);
                List<object> v = new List<object> { ++n, Db.Dt(x["menu_date"]), Db.S(x["menu_no"]) };
                if (mode == 1) { int st = Db.I(x["shop_type"]); v.Add(st == 3 ? "مردود إخراج" : Cache.ReceiptTypes[Math.Max(0, Math.Min(3, st))]); }
                if (mode != 2) v.Add(Cache.Project(Db.I(x["proj_no"])));
                v.Add(Cache.Person(Db.I(x["custom_no"]))); v.Add(Cache.ItemCode(it)); v.Add(Cache.ItemName(it)); v.Add(Unit(it)); v.Add(q);
                r.Row(mode == 1 && Db.I(x["shop_type"]) == 3 ? "in" : null, v.ToArray());
                r.Link(RowLinks.Doc(mode == 0 ? DocKind.Issue : mode == 2 ? DocKind.PurchaseReturn : Db.I(x["shop_type"]) == 3 ? DocKind.Return : DocKind.Receipt, Db.I(x["shop_no"])));
            }
            r.Sum("عدد الأسطر", n.ToString("#,##0", Inv)); r.Sum("عدد المواد المختلفة", items.Count.ToString("#,##0", Inv)); r.Sum("مجموع الكميات", UI.Num(tq));
            return r;
        }

        // ------------------------------------------------------------ cost centres (table project)
        /// <summary>Cost of issued quantities from the FIFO links to receipt prices (0 when receipts have no price).</summary>
        static Dictionary<int, double> LineCosts(string saleWhere, object[] args)
        {
            Dictionary<int, double> c = new Dictionary<int, double>();
            DataTable t = Db.Query("SELECT m.auto_seq, SUM(x.qty * s.cost) AS v FROM ((sale AS h INNER JOIN sale_menu AS m ON h.shop_no = m.seq) " +
                                   "INNER JOIN sale_menu_1 AS x ON x.seq_sale = m.auto_seq) INNER JOIN shop_menu AS s ON s.auto_seq = x.seq_shop WHERE " + saleWhere +
                                   " GROUP BY m.auto_seq", args);
            foreach (DataRow r in t.Rows) c[Db.I(r[0])] = Db.D(r[1]);
            return c;
        }

        static ReportData ProjectStatement(ReportParams p)
        {
            ReportData r = new ReportData();
            r.Title = "كشف مركز كلفة: " + Cache.Project(p.ProjNo);
            r.Subtitle = Period(p);
            if (!Db.HasProj) { r.Footer = "ميزة مراكز الكلفة غير مفعّلة بعد."; r.Col("—", ColKind.Text); return r; }
            DataRow pr = Cache.Projects.Select("proj_no = " + p.ProjNo).FirstOrDefault();
            if (pr != null)
            {
                List<string> info = new List<string>();
                if (Db.S(pr["proj_code"]).Length > 0) info.Add("الرمز: " + Db.S(pr["proj_code"]));
                info.Add("النوع: " + Db.S(pr["type_name"]));
                if (Db.S(pr["client"]).Length > 0) info.Add("الجهة المستفيدة: " + Db.S(pr["client"]));
                info.Add("الحالة: " + Db.S(pr["status_name"]));
                r.Subtitle += "   —   " + string.Join("   —   ", info.ToArray());
            }
            string w = "h.proj_no = ? AND " + Db.IssueOnly("h") + " AND h.menu_date >= ? AND h.menu_date < ?";
            Dictionary<int, double> cost = LineCosts(w, new object[] { p.ProjNo, p.From, p.ToEx });
            DataTable iss = Db.Query("SELECT h.menu_date AS d, h.menu_no AS dno, h.custom_no AS pe, l.auto_seq AS ln, l.item_no AS it, l.qty AS q, h.shop_no AS id FROM sale AS h INNER JOIN sale_menu AS l ON h.shop_no = l.seq WHERE " + w + " ORDER BY h.menu_date, h.shop_no",
                p.ProjNo, p.From, p.ToEx);
            DataTable ret = Db.Query("SELECT h.menu_date AS d, h.menu_no AS dno, h.custom_no AS pe, l.item_no AS it, l.qty AS q, l.cost AS pr, h.shop_no AS id FROM shop AS h INNER JOIN shop_menu AS l ON h.shop_no = l.seq " +
                "WHERE h.proj_no = ? AND h.shop_type = 3 AND h.menu_date >= ? AND h.menu_date < ? ORDER BY h.menu_date, h.shop_no", p.ProjNo, p.From, p.ToEx);
            double tOut = 0, tIn = 0, tCost = 0;
            if (!p.Flag)
            {
                r.Col("ت", ColKind.Int); r.Col("الرمز", ColKind.Text); r.Col("اسم المادة", ColKind.Text); r.Col("النوع", ColKind.Text); r.Col("الوحدة", ColKind.Text);
                r.ColT("المُخرج لمركز الكلفة"); r.ColT("المُعاد منه"); r.ColT("صافي المستهلك"); r.ColT("الكلفة");
                Dictionary<int, double[]> g = new Dictionary<int, double[]>();
                foreach (DataRow x in iss.Rows)
                {
                    int it = Db.I(x["it"]); double[] v; if (!g.TryGetValue(it, out v)) { v = new double[3]; g[it] = v; }
                    v[0] += Db.D(x["q"]); double c; cost.TryGetValue(Db.I(x["ln"]), out c); v[2] += c;
                }
                foreach (DataRow x in ret.Rows)
                {
                    int it = Db.I(x["it"]); double[] v; if (!g.TryGetValue(it, out v)) { v = new double[3]; g[it] = v; }
                    v[1] += Db.D(x["q"]); v[2] -= Db.D(x["q"]) * Db.D(x["pr"]);
                }
                int n = 0;
                foreach (KeyValuePair<int, double[]> kv in g.OrderBy(k => Cache.ItemName(k.Key)))
                {
                    DataRow ir = Cache.Item(kv.Key);
                    r.Row(null, ++n, Cache.ItemCode(kv.Key), Cache.ItemName(kv.Key), ir == null ? "" : Db.S(ir["type_name"]), Unit(kv.Key), kv.Value[0], kv.Value[1], kv.Value[0] - kv.Value[1], kv.Value[2]);
                    r.Link(RowLinks.Item(kv.Key));
                    tOut += kv.Value[0]; tIn += kv.Value[1]; tCost += kv.Value[2];
                }
                r.Sum("عدد المواد", n.ToString("#,##0", Inv));
            }
            else
            {
                r.Col("ت", ColKind.Int); r.Col("التاريخ", ColKind.Date); r.Col("الحركة", ColKind.Text); r.Col("رقم السند", ColKind.Text); r.Col("المستلم / المُعيد", ColKind.Text);
                r.Col("الرمز", ColKind.Text); r.Col("اسم المادة", ColKind.Text); r.Col("الوحدة", ColKind.Text); r.ColT("مُخرج"); r.ColT("مُعاد"); r.ColT("الكلفة");
                List<object[]> rows = new List<object[]>(); Dictionary<object[], string> links = new Dictionary<object[], string>();
                foreach (DataRow x in iss.Rows)
                {
                    double c; cost.TryGetValue(Db.I(x["ln"]), out c); int it = Db.I(x["it"]);
                    rows.Add(new object[] { 0, Db.Dt(x["d"]), "إخراج مخزني", Db.S(x["dno"]), Cache.Person(Db.I(x["pe"])), Cache.ItemCode(it), Cache.ItemName(it), Unit(it), Db.D(x["q"]), null, c });
                    links[rows[rows.Count - 1]] = RowLinks.Doc(DocKind.Issue, Db.I(x["id"]));
                    tOut += Db.D(x["q"]); tCost += c;
                }
                foreach (DataRow x in ret.Rows)
                {
                    int it = Db.I(x["it"]); double c = -Db.D(x["q"]) * Db.D(x["pr"]);
                    rows.Add(new object[] { 0, Db.Dt(x["d"]), "مردود إخراج", Db.S(x["dno"]), Cache.Person(Db.I(x["pe"])), Cache.ItemCode(it), Cache.ItemName(it), Unit(it), null, Db.D(x["q"]), c });
                    links[rows[rows.Count - 1]] = RowLinks.Doc(DocKind.Return, Db.I(x["id"]));
                    tIn += Db.D(x["q"]); tCost += c;
                }
                int n = 0;
                foreach (object[] o in rows.OrderBy(o => (DateTime)o[1])) { o[0] = ++n; r.Row(o[2].ToString() == "مردود إخراج" ? "in" : null, o); r.Link(links[o]); }
            }
            r.Sum("مجموع المُخرج", UI.Num(tOut)); r.Sum("مجموع المُعاد", UI.Num(tIn)); r.Sum("صافي المستهلك", UI.Num(tOut - tIn));
            if (Math.Abs(tCost) > 0.0001) r.Sum("الكلفة", UI.Num(tCost));
            double resv = 0; foreach (ResRow rr in Reservations.Get(0, p.ProjNo)) resv += rr.Outstanding;
            if (resv > 0) r.Footer = "يوجد لمركز الكلفة مواد محجوزة لم تُخرج بعد بمجموع كميات " + UI.Num(resv) + " (راجع تقرير الحجوزات القائمة).";
            r.Signatures = true;
            return r;
        }

        static ReportData ByProjects(ReportParams p)
        {
            ReportData r = new ReportData();
            r.Title = "الإخراج حسب مراكز الكلفة"; r.Subtitle = Period(p) + "   —   " + StoreText(p);
            r.Col("ت", ColKind.Int); r.Col("مركز الكلفة", ColKind.Text); r.Col("النوع", ColKind.Text); r.Col("الحالة", ColKind.Text); r.ColT("سندات الإخراج"); r.ColT("الكميات المُخرجة"); r.ColT("المُعاد"); r.ColT("الصافي"); r.ColT("الكلفة");
            if (!Db.HasProj) return r;
            string sc = p.Store > 0 ? " AND h.stor = ?" : "";
            List<object> a = new List<object> { p.From, p.ToEx }; if (p.Store > 0) a.Add(p.Store);
            Dictionary<int, double[]> d = CostCentres.Totals(p.From, p.ToEx, p.Store);
            // rows grouped by type (مشروع، قسم، صيانة…), biggest first inside each type; one summary chip per type
            int n = 0;
            SortedDictionary<int, double[]> byType = new SortedDictionary<int, double[]>();
            foreach (KeyValuePair<int, double[]> kv in d.OrderBy(k => CostCentres.TypeOf(k.Key)).ThenByDescending(k => k.Value[1]))
            {
                DataRow pr = CostCentres.Row(kv.Key);
                int ty = CostCentres.TypeOf(kv.Key);
                r.Row(null, ++n, Cache.Project(kv.Key), CostCentres.TypeName(ty), pr == null ? "" : Db.S(pr["status_name"]), kv.Value[0], kv.Value[1], kv.Value[2], kv.Value[1] - kv.Value[2], kv.Value[3]);
                r.Link(RowLinks.Project(kv.Key, p.From, p.To));
                double[] s; if (!byType.TryGetValue(ty, out s)) { s = new double[3]; byType[ty] = s; }
                s[0] += 1; s[1] += kv.Value[1] - kv.Value[2]; s[2] += kv.Value[3];
            }
            r.Chart = new List<KeyValuePair<string, double[]>>(); r.ChartSeries = new string[] { "الصافي" };
            foreach (KeyValuePair<int, double[]> kv in d.OrderByDescending(k => k.Value[1]).Take(15))
                r.Chart.Add(new KeyValuePair<string, double[]>(Cache.Project(kv.Key), new double[] { kv.Value[1] - kv.Value[2] }));
            double none = Db.D(Db.Scalar("SELECT COUNT(*) FROM sale AS h WHERE h.proj_no IS NULL AND " + Db.IssueOnly("h") + " AND h.menu_date >= ? AND h.menu_date < ?" + sc, a.ToArray()));
            r.Sum("عدد مراكز الكلفة", n.ToString());
            foreach (KeyValuePair<int, double[]> kv in byType)
                r.Sum(CostCentres.TypeName(kv.Key) + " (" + UI.Num(kv.Value[0]) + ")", "الصافي " + UI.Num(kv.Value[1]) + (Math.Abs(kv.Value[2]) > 0.0001 ? " • الكلفة " + UI.Num(kv.Value[2]) : ""));
            if (none > 0) r.Footer = "سندات إخراج بدون مركز كلفة في هذه الفترة: " + UI.Num(none) + " (غالبا سندات قديمة قبل تفعيل مراكز الكلفة).";
            return r;
        }

        static ReportData ReservationsReport(ReportParams p)
        {
            ReportData r = new ReportData();
            r.Title = "الحجوزات القائمة"; r.Subtitle = (p.ProjNo > 0 ? "مركز الكلفة: " + Cache.Project(p.ProjNo) : "كل مراكز الكلفة") + (p.ItemNo > 0 ? "   —   " + Cache.ItemName(p.ItemNo) : "");
            r.Col("ت", ColKind.Int); r.Col("مركز الكلفة", ColKind.Text); r.Col("المخزن", ColKind.Text); r.Col("الرمز", ColKind.Text); r.Col("اسم المادة", ColKind.Text); r.Col("الوحدة", ColKind.Text);
            r.Col("محجوز منذ", ColKind.Date); r.ColT("الكمية المحجوزة"); r.ColT("المُخرج لمركز الكلفة"); r.ColT("المتبقي محجوزا"); r.Col("الرصيد في المخزن", ColKind.Num);
            int n = 0; double tot = 0;
            foreach (ResRow x in Reservations.Get(p.ItemNo, p.ProjNo).OrderBy(x => Cache.Project(x.Proj)).ThenBy(x => Cache.ItemName(x.Item)))
            {
                double stock = Cache.Stock(x.Item, x.Stor);
                r.Row(x.Outstanding <= 0 ? "muted" : (stock < x.Outstanding ? "neg" : null), ++n, Cache.Project(x.Proj), Cache.Store(x.Stor), Cache.ItemCode(x.Item), Cache.ItemName(x.Item), Unit(x.Item),
                      x.Since, x.Reserved, Math.Max(0, x.Used), x.Outstanding, stock);
                r.Link(RowLinks.Reservation(x.Proj, x.Item, x.Stor));
                tot += x.Outstanding;
            }
            r.Sum("عدد الأسطر", n.ToString()); r.Sum("مجموع المتبقي محجوزا", UI.Num(tot));
            r.Footer = "اللون الأحمر: الرصيد في المخزن أقل من الكمية المحجوزة. الرمادي: الحجز اكتمل إخراجه.";
            return r;
        }

        // ------------------------------------------------------------ critical balances
        public class CriticalRow
        {
            public int Item; public double Stock, Reserved, Free, Min, Used, Monthly, Days, Suggest; public string Reason;
            /// <summary>Minimum (safety stock) and maximum of the store(s), lead time in days, reorder point; 0 = not set.</summary>
            public double MinQty, MaxQty, Rop; public int Lead;
            /// <summary>Rule used for the suggested quantity «أساس الاقتراح».</summary>
            public string Basis;
        }

        public const string BasisMax = "الحد الأعلى − الرصيد الحر";
        public const string BasisTwoMonths = "استهلاك شهرين − الرصيد الحر";

        /// <summary>Per item: min_qty / max_qty of one store (or summed over all stores) and item.lead_days.</summary>
        static void PlanOf(int store, out Dictionary<int, double> mins, out Dictionary<int, double> maxs, out Dictionary<int, int> leads)
        {
            mins = new Dictionary<int, double>(); maxs = new Dictionary<int, double>(); leads = new Dictionary<int, int>();
            if (!Db.HasMinMax) return;
            DataTable t = store > 0
                ? Db.Query("SELECT item_no, SUM(min_qty), SUM(max_qty) FROM item_stor WHERE stor = ? AND (min_qty > 0 OR max_qty > 0) GROUP BY item_no", store)
                : Db.Query("SELECT item_no, SUM(min_qty), SUM(max_qty) FROM item_stor WHERE min_qty > 0 OR max_qty > 0 GROUP BY item_no");
            foreach (DataRow x in t.Rows) { if (Db.D(x[1]) > 0) mins[Db.I(x[0])] = Db.D(x[1]); if (Db.D(x[2]) > 0) maxs[Db.I(x[0])] = Db.D(x[2]); }
            foreach (DataRow x in Db.Query("SELECT item_no, lead_days FROM item WHERE lead_days > 0").Rows) leads[Db.I(x[0])] = Db.I(x[1]);
        }

        /// <summary>
        /// Items that need replenishment. Free balance = stock − reservations.
        /// When the item has a minimum (safety stock, per store) or a lead time:
        ///   reorder point ROP = average daily issue × lead_days + min_qty; the item shows when free ≤ ROP.
        /// Otherwise (old rule): free ≤ the item's reorder level (item_bal_ret), or — when no level is set — free below one
        /// month of its average monthly issue over the last <paramref name="months"/> months (3 or 6, items issued at least twice).
        /// Reservations exceeding the stock always show.
        /// Suggested quantity = max_qty − free when a maximum is set, otherwise 2 × average monthly issue − free
        /// (at least up to the reorder point / level).
        /// </summary>
        public static List<CriticalRow> CriticalItems(int store, int type, int months)
        {
            if (months != 3) months = 6;
            DateTime since = DateTime.Today.AddMonths(-months);
            string sc = store > 0 ? " AND h.stor = ?" : "";
            List<object> a = new List<object> { since }; if (store > 0) a.Add(store);
            Dictionary<int, double> use = new Dictionary<int, double>();
            Dictionary<int, int> times = new Dictionary<int, int>();
            foreach (DataRow x in Db.Query("SELECT l.item_no, SUM(l.qty), COUNT(*) FROM sale AS h INNER JOIN sale_menu AS l ON h.shop_no = l.seq WHERE " + Db.IssueOnly("h") + " AND h.menu_date >= ?" + sc + " GROUP BY l.item_no", a.ToArray()).Rows)
            { use[Db.I(x[0])] = Db.D(x[1]); times[Db.I(x[0])] = Db.I(x[2]); }
            Dictionary<int, double> stock = new Dictionary<int, double>();
            foreach (DataRow x in (store > 0 ? Db.Query("SELECT item_no, SUM(item_bal) FROM item_stor WHERE stor = ? GROUP BY item_no", store) : Db.Query("SELECT item_no, SUM(item_bal) FROM item_stor GROUP BY item_no")).Rows)
                stock[Db.I(x[0])] = Db.D(x[1]);
            Dictionary<int, double> resv = new Dictionary<int, double>();
            foreach (ResRow rr in Reservations.Get(0, 0)) if (store == 0 || rr.Stor == store) { double v; resv.TryGetValue(rr.Item, out v); resv[rr.Item] = v + rr.Outstanding; }
            Dictionary<int, double> mins, maxs; Dictionary<int, int> leads;
            PlanOf(store, out mins, out maxs, out leads);
            List<CriticalRow> list = new List<CriticalRow>();
            foreach (DataRow ir in Cache.Items.Rows)
            {
                if (Db.B(ir["freez"])) continue;
                if (type > 0 && Db.I(ir["type"]) != type) continue;
                int it = Db.I(ir["item_no"]);
                double u, st, rv, mn, mx; int cnt, lead;
                use.TryGetValue(it, out u); times.TryGetValue(it, out cnt); stock.TryGetValue(it, out st); resv.TryGetValue(it, out rv);
                mins.TryGetValue(it, out mn); maxs.TryGetValue(it, out mx); leads.TryGetValue(it, out lead);
                CriticalRow c = Evaluate(st, rv, u, cnt, months, Db.D(ir["item_bal_ret"]), mn, mx, lead);
                if (c == null) continue;
                c.Item = it;
                list.Add(c);
            }
            return list.OrderBy(c => c.Free <= 0 ? 0 : 1).ThenBy(c => c.Days).ToList();
        }

        /// <summary>The replenishment rule for one item (null = no need); public for the tests.
        /// used = issued in the last <paramref name="months"/> months (<paramref name="times"/> issues); level = item_bal_ret;
        /// minQty / maxQty / lead = 0 when not set.</summary>
        public static CriticalRow Evaluate(double stock, double reserved, double used, int times, int months, double level, double minQty, double maxQty, int lead)
        {
            double monthly = used / months, daily = monthly / 30.0;
            double free = stock - reserved;
            bool plan = minQty > 0 || lead > 0;              // min / lead time set: reorder point rule
            double rop = plan ? daily * lead + minQty : level;
            string reason = null;
            if (reserved > 0.0001 && free < -0.0001) reason = "المحجوز لمراكز الكلفة أكبر من الرصيد";
            else if (plan && free <= rop + 1e-9 && (rop > 0 || free < 0)) reason = free <= 0 ? "نفد الرصيد (نقطة إعادة الطلب)" : "وصل نقطة إعادة الطلب";
            else if (!plan && level > 0 && free <= level) reason = "وصل حد إعادة الطلب";
            else if (!plan && level <= 0 && times >= 2 && monthly > 0 && free < monthly) reason = free <= 0 ? "نفد الرصيد ويوجد استهلاك" : "يكفي أقل من شهر";
            if (reason == null) return null;
            CriticalRow c = new CriticalRow();
            c.Stock = stock; c.Reserved = reserved; c.Free = free; c.Min = rop; c.Used = used; c.Monthly = monthly; c.Reason = reason;
            c.MinQty = minQty; c.MaxQty = maxQty; c.Lead = lead; c.Rop = rop;
            c.Days = monthly > 0 ? Math.Max(0, free) / daily : 0;
            if (maxQty > 0)
            {
                c.Suggest = Math.Max(0, Math.Ceiling(maxQty - free - 1e-9));
                c.Basis = BasisMax;
            }
            else
            {
                double target = Math.Max(rop, monthly * 2);    // two months of average issue (or the reorder point / level)
                c.Suggest = Math.Max(0, Math.Ceiling(target - free - 1e-9));
                c.Basis = BasisTwoMonths + (rop > monthly * 2 ? " (حتى نقطة الطلب)" : "");
            }
            return c;
        }

        static ReportData Critical(ReportParams p)
        {
            int months = p.Months == 3 ? 3 : 6;
            DateTime since = DateTime.Today.AddMonths(-months);
            string per = "آخر " + months + " أشهر";
            ReportData r = new ReportData();
            r.Title = "الأرصدة الحرجة — مواد تحتاج تعزيز";
            r.Subtitle = StoreText(p) + (p.TypeNo > 0 ? "   —   النوع: " + Cache.Type(p.TypeNo) : "") +
                         "   —   متوسط الاستهلاك محسوب من " + per + " (من " + UI.Date(since) + " إلى " + UI.Date(DateTime.Today) + ")";
            r.Col("ت", ColKind.Int); r.Col("الرمز", ColKind.Text); r.Col("اسم المادة", ColKind.Text); r.Col("النوع", ColKind.Text); r.Col("الوحدة", ColKind.Text);
            r.Col("الرصيد", ColKind.Num); r.Col("محجوز", ColKind.Num); r.Col("الرصيد الحر", ColKind.Num); r.Col("نقطة / حد الطلب", ColKind.Num);
            r.Col("المُخرج خلال " + per, ColKind.Num); r.Col("متوسط الإخراج الشهري (" + per + ")", ColKind.Num); r.Col("يكفي (يوم)", ColKind.Int);
            r.Col("مدة التوريد (يوم)", ColKind.Int); r.Col("الحد الأعلى", ColKind.Num);
            r.ColT("الكمية المقترحة"); r.Col("أساس الاقتراح", ColKind.Text); r.Col("السبب", ColKind.Text);
            int n = 0, out0 = 0, nMax = 0, nRop = 0;
            List<CriticalRow> rows = CriticalItems(p.Store, p.TypeNo, months);
            foreach (CriticalRow c in rows)
            {
                DataRow ir = Cache.Item(c.Item);
                if (c.Free <= 0) out0++;
                if (c.MaxQty > 0) nMax++;
                if (c.MinQty > 0 || c.Lead > 0) nRop++;
                r.Row(c.Free <= 0 ? "neg" : null, ++n, Cache.ItemCode(c.Item), Cache.ItemName(c.Item), ir == null ? "" : Db.S(ir["type_name"]), Unit(c.Item),
                    c.Stock, c.Reserved == 0 ? null : (object)c.Reserved, c.Free, c.Min == 0 ? null : (object)Math.Round(c.Min, 2),
                    c.Used == 0 ? null : (object)c.Used, Math.Round(c.Monthly, 2),
                    c.Monthly > 0 ? (object)Math.Round(c.Days) : null, c.Lead > 0 ? (object)c.Lead : null, c.MaxQty > 0 ? (object)c.MaxQty : null,
                    c.Suggest, c.Basis, c.Reason);
                r.Link(RowLinks.Item(c.Item));
            }
            r.Tag = rows;
            r.Sum("فترة المتوسط", per);
            r.Sum("مواد تحتاج تعزيز", n.ToString("#,##0", Inv));
            r.Sum("نفد رصيدها الحر", out0.ToString("#,##0", Inv));
            if (nRop > 0) r.Sum("بنقطة إعادة الطلب", nRop.ToString("#,##0", Inv));
            if (nMax > 0) r.Sum("اقتراحها بالحد الأعلى", nMax.ToString("#,##0", Inv));
            r.Footer = "طريقة الحساب: متوسط الإخراج الشهري = مجموع ما أُخرج خلال " + per + " ÷ " + months + "، والمتوسط اليومي = الشهري ÷ 30.   " +
                       "الرصيد الحر = الرصيد − المحجوز لمراكز الكلفة.   " +
                       "إذا حُدد للمادة حد أدنى في المخزن أو مدة توريد: نقطة إعادة الطلب = المتوسط اليومي × مدة التوريد + الحد الأدنى (مخزون الأمان)، وتظهر المادة إذا كان رصيدها الحر لا يزيد عن هذه النقطة.   " +
                       "وإلا تظهر إذا وصل رصيدها الحر حد إعادة الطلب، أو كان أقل من استهلاك شهر واحد (للمواد التي أُخرجت مرتين على الأقل في الفترة).   " +
                       "أساس الاقتراح: «" + BasisMax + "» إذا حُدد للمادة حد أعلى، وإلا «" + BasisTwoMonths + "» أي ما يكفي شهرين، وتُكمَّل حتى نقطة الطلب إذا كانت أكبر.   " +
                       "الحد الأدنى والأعلى لكل مخزن ومدة التوريد تُحدد من شاشة المواد ← تعديل.";
            return r;
        }

        static ReportData TopItems(ReportParams p)
        {
            ReportData r = new ReportData();
            r.Title = "أكثر المواد إخراجا"; r.Subtitle = Period(p) + "   —   " + StoreText(p) + "   —   أعلى " + p.Top + " مادة";
            string sql = "SELECT l.item_no, SUM(l.qty) AS q, COUNT(*) AS c FROM sale AS h INNER JOIN sale_menu AS l ON h.shop_no = l.seq WHERE " + Db.IssueOnly("h") + " AND h.menu_date >= ? AND h.menu_date < ?";
            List<object> a = new List<object> { p.From, p.ToEx };
            if (p.Store > 0) { sql += " AND h.stor = ?"; a.Add(p.Store); }
            sql += " GROUP BY l.item_no ORDER BY SUM(l.qty) DESC";
            r.Col("الترتيب", ColKind.Int); r.Col("الرمز", ColKind.Text); r.Col("اسم المادة", ColKind.Text); r.Col("النوع", ColKind.Text); r.Col("الوحدة", ColKind.Text);
            r.ColT("مرات الإخراج"); r.ColT("الكمية المُخرجة"); r.Col("الرصيد الحالي", ColKind.Num);
            int n = 0;
            r.Chart = new List<KeyValuePair<string, double[]>>(); r.ChartSeries = new string[] { "الكمية" };
            foreach (DataRow x in Db.Query(sql, a.ToArray()).Rows)
            {
                int it = Db.I(x["item_no"]); DataRow ir = Cache.Item(it);
                if (p.TypeNo > 0 && (ir == null || Db.I(ir["type"]) != p.TypeNo)) continue;
                n++;
                r.Row(null, n, Cache.ItemCode(it), Cache.ItemName(it), ir == null ? "" : Db.S(ir["type_name"]), Unit(it), Db.D(x["c"]), Db.D(x["q"]), ir == null ? 0 : Db.D(ir["bal"]));
                r.Link(RowLinks.Item(it));
                if (n <= 15) r.Chart.Add(new KeyValuePair<string, double[]>(Cache.ItemName(it), new double[] { Db.D(x["q"]) }));
                if (n >= p.Top) break;
            }
            return r;
        }

        static ReportData ByPersons(ReportParams p)
        {
            ReportData r = new ReportData();
            r.Title = "الإخراج حسب المستلمين"; r.Subtitle = Period(p) + "   —   " + StoreText(p);
            string sc = p.Store > 0 ? " AND h.stor = ?" : "";
            List<object> a = new List<object> { p.From, p.ToEx }; if (p.Store > 0) a.Add(p.Store);
            Dictionary<int, double[]> d = new Dictionary<int, double[]>();
            foreach (DataRow x in Db.Query("SELECT h.custom_no, COUNT(*) FROM sale AS h WHERE " + Db.IssueOnly("h") + " AND h.menu_date >= ? AND h.menu_date < ?" + sc + " GROUP BY h.custom_no", a.ToArray()).Rows)
                d[Db.I(x[0])] = new double[] { Db.D(x[1]), 0, 0 };
            foreach (DataRow x in Db.Query("SELECT h.custom_no, COUNT(*), SUM(l.qty) FROM sale AS h INNER JOIN sale_menu AS l ON h.shop_no = l.seq WHERE " + Db.IssueOnly("h") + " AND h.menu_date >= ? AND h.menu_date < ?" + sc + " GROUP BY h.custom_no", a.ToArray()).Rows)
            {
                double[] v; if (!d.TryGetValue(Db.I(x[0]), out v)) { v = new double[3]; d[Db.I(x[0])] = v; }
                v[1] = Db.D(x[1]); v[2] = Db.D(x[2]);
            }
            r.Col("ت", ColKind.Int); r.Col("الجهة", ColKind.Text); r.ColT("عدد السندات"); r.ColT("عدد البنود"); r.ColT("مجموع الكميات");
            int n = 0;
            foreach (KeyValuePair<int, double[]> kv in d.OrderByDescending(k => k.Value[0]))
            {
                r.Row(null, ++n, Cache.Person(kv.Key), kv.Value[0], kv.Value[1], kv.Value[2]);
                r.Link(RowLinks.Person(kv.Key, p.From, p.To));
            }
            r.Sum("عدد الجهات", n.ToString("#,##0", Inv));
            return r;
        }

        static ReportData PersonStatement(ReportParams p)
        {
            ReportData r = new ReportData();
            r.Title = "كشف جهة: " + Cache.Person(p.PersonNo); r.Subtitle = Period(p);
            DataTable t = Db.Query(
                "SELECT h.menu_date AS d, h.menu_no AS dno, " + (Db.HasProj ? "IIF(h.doc_kind = 1, 6, 2)" : "2") + " AS k, l.item_no AS it, l.qty AS q, h.shop_no AS id FROM sale AS h INNER JOIN sale_menu AS l ON h.shop_no = l.seq WHERE h.custom_no = ? AND h.menu_date >= ? AND h.menu_date < ? " +
                "UNION ALL SELECT h.menu_date, h.menu_no, IIF(h.shop_type = 3, 3, 1), l.item_no, l.qty, h.shop_no FROM shop AS h INNER JOIN shop_menu AS l ON h.shop_no = l.seq WHERE h.custom_no = ? AND h.menu_date >= ? AND h.menu_date < ? " +
                "ORDER BY d, id", p.PersonNo, p.From, p.ToEx, p.PersonNo, p.From, p.ToEx);
            double ti = 0, tr = 0, ts = 0;
            if (p.Flag)
            {
                r.Col("ت", ColKind.Int); r.Col("الرمز", ColKind.Text); r.Col("اسم المادة", ColKind.Text); r.Col("الوحدة", ColKind.Text); r.ColT("المصروف له"); r.ColT("المُعاد منه"); r.ColT("المُجهّز منه"); r.ColT("صافي المصروف");
                Dictionary<int, double[]> g = new Dictionary<int, double[]>();
                foreach (DataRow x in t.Rows)
                {
                    int it = Db.I(x["it"]); double[] v; if (!g.TryGetValue(it, out v)) { v = new double[3]; g[it] = v; }
                    int k = Db.I(x["k"]); v[k == 2 ? 0 : k == 3 ? 1 : 2] += Db.D(x["q"]);
                }
                int n = 0;
                foreach (KeyValuePair<int, double[]> kv in g.OrderBy(k => Cache.ItemName(k.Key)))
                {
                    r.Row(null, ++n, Cache.ItemCode(kv.Key), Cache.ItemName(kv.Key), Unit(kv.Key), kv.Value[0], kv.Value[1], kv.Value[2], kv.Value[0] - kv.Value[1]);
                    r.Link(RowLinks.Item(kv.Key));
                    ti += kv.Value[0]; tr += kv.Value[1]; ts += kv.Value[2];
                }
            }
            else
            {
                r.Col("ت", ColKind.Int); r.Col("التاريخ", ColKind.Date); r.Col("نوع السند", ColKind.Text); r.Col("الرقم", ColKind.Text);
                r.Col("الرمز", ColKind.Text); r.Col("اسم المادة", ColKind.Text); r.Col("الوحدة", ColKind.Text); r.ColT("مصروف له"); r.ColT("مُعاد / مُجهّز منه");
                int n = 0;
                foreach (DataRow x in t.Rows)
                {
                    int k = Db.I(x["k"]); double q = Db.D(x["q"]); int it = Db.I(x["it"]);
                    if (k == 2) ti += q; else if (k == 3) tr += q; else ts += q;
                    r.Row(k == 2 ? null : "in", ++n, Db.Dt(x["d"]), KindText(k), Db.S(x["dno"]), Cache.ItemCode(it), Cache.ItemName(it), Unit(it), k == 2 ? (object)q : null, k != 2 ? (object)q : null);
                    r.Link(RowLinks.Move(k, Db.I(x["id"])));
                }
            }
            r.Sum("مجموع المصروف", UI.Num(ti)); r.Sum("مجموع المُعاد", UI.Num(tr)); if (ts > 0) r.Sum("مجموع المُجهّز", UI.Num(ts)); r.Sum("صافي المصروف", UI.Num(ti - tr));
            r.Signatures = true;
            return r;
        }

        // ------------------------------------------------------------ receipts / transfers
        static ReportData Receipts(ReportParams p)
        {
            ReportData r = new ReportData();
            r.Title = "سجل سندات الإدخال والمردود"; r.Subtitle = Period(p) + (p.RType > 0 ? "   —   " + Cache.ReceiptTypes[p.RType] : "") + (p.PersonNo > 0 ? "   —   " + Cache.Person(p.PersonNo) : "");
            string sql = "SELECT h.shop_no, h.menu_no, h.menu_date, h.custom_no, h.stor, h.shop_type, h.[note], (SELECT COUNT(*) FROM shop_menu m WHERE m.seq = h.shop_no) AS n, " +
                         "(SELECT SUM(qty) FROM shop_menu m WHERE m.seq = h.shop_no) AS q FROM shop AS h WHERE " + Db.NotCountAdj("h") + " AND " + Db.MainShop("h") + " AND h.menu_date >= ? AND h.menu_date < ?";
            Dictionary<int, Posting.GroupInfo> groups = Posting.Groups();      // receipts into several stores: one row
            List<object> a = new List<object> { p.From, p.ToEx };
            if (p.RType > 0) { sql += " AND h.shop_type = ?"; a.Add(p.RType); }
            if (p.PersonNo > 0) { sql += " AND h.custom_no = ?"; a.Add(p.PersonNo); }
            sql += " ORDER BY h.menu_date, h.shop_no";
            r.Col("ت", ColKind.Int); r.Col("رقم السند", ColKind.Text); r.Col("التاريخ", ColKind.Date); r.Col("النوع", ColKind.Text); r.Col("المجهز / الجهة", ColKind.Text);
            r.Col("المخزن", ColKind.Text); r.ColT("عدد المواد"); r.ColT("مجموع الكميات"); r.Col("ملاحظات", ColKind.Text);
            int n = 0;
            foreach (DataRow x in Db.Query(sql, a.ToArray()).Rows)
            {
                Posting.GroupInfo gi; groups.TryGetValue(Db.I(x["shop_no"]), out gi);
                r.Row(Db.I(x["shop_type"]) == 3 ? "in" : null, ++n, Db.S(x["menu_no"]), Db.Dt(x["menu_date"]), Cache.ReceiptTypes[Math.Max(0, Math.Min(3, Db.I(x["shop_type"])))],
                    Cache.Person(Db.I(x["custom_no"])), gi != null ? gi.Stores : Cache.Store(Db.I(x["stor"])), gi != null ? gi.Lines : Db.D(x["n"]), gi != null ? gi.Qty : Db.D(x["q"]), Db.S(x["note"]));
                r.Link(RowLinks.Doc(Db.I(x["shop_type"]) == 3 ? DocKind.Return : DocKind.Receipt, Db.I(x["shop_no"])));
            }
            r.Sum("عدد السندات", n.ToString("#,##0", Inv));
            return r;
        }

        static ReportData Transfers(ReportParams p)
        {
            ReportData r = new ReportData();
            r.Title = "النقل المخزني"; r.Subtitle = Period(p);
            string sql = "SELECT h.shop_no, h.menu_no, h.menu_date, h.stor_out, h.stor_in, l.item_no, l.qty FROM trans AS h INNER JOIN trans_menu AS l ON h.shop_no = l.seq WHERE h.menu_date >= ? AND h.menu_date < ?";
            List<object> a = new List<object> { p.From, p.ToEx };
            if (p.ItemNo > 0) { sql += " AND l.item_no = ?"; a.Add(p.ItemNo); }
            sql += " ORDER BY h.menu_date, h.shop_no";
            r.Col("ت", ColKind.Int); r.Col("التاريخ", ColKind.Date); r.Col("الرقم", ColKind.Text); r.Col("من مخزن", ColKind.Text); r.Col("إلى مخزن", ColKind.Text);
            r.Col("الرمز", ColKind.Text); r.Col("اسم المادة", ColKind.Text); r.Col("الوحدة", ColKind.Text); r.ColT("الكمية");
            int n = 0;
            foreach (DataRow x in Db.Query(sql, a.ToArray()).Rows)
            {
                r.Row(null, ++n, Db.Dt(x["menu_date"]), Db.S(x["menu_no"]), Cache.Store(Db.I(x["stor_out"])), Cache.Store(Db.I(x["stor_in"])),
                    Cache.ItemCode(Db.I(x["item_no"])), Cache.ItemName(Db.I(x["item_no"])), Unit(Db.I(x["item_no"])), Db.D(x["qty"]));
                r.Link(RowLinks.Doc(DocKind.Transfer, Db.I(x["shop_no"])));
            }
            r.Sum("عدد الأسطر", n.ToString());
            return r;
        }

        // ------------------------------------------------------------ summaries
        static ReportData Monthly(ReportParams p)
        {
            ReportData r = new ReportData();
            r.Title = "الملخص الشهري"; r.Subtitle = Period(p);
            SortedDictionary<int, double[]> m = new SortedDictionary<int, double[]>();
            Action<DataTable, int> add = delegate(DataTable t, int off)
            {
                foreach (DataRow x in t.Rows)
                {
                    int key = Db.I(x[0]) * 100 + Db.I(x[1]);
                    double[] v; if (!m.TryGetValue(key, out v)) { v = new double[6]; m[key] = v; }
                    v[off] += Db.D(x[2]);
                }
            };
            string g = " GROUP BY YEAR(h.menu_date), MONTH(h.menu_date)";
            string w = " h.menu_date >= ? AND h.menu_date < ?";
            add(Db.Query("SELECT YEAR(h.menu_date), MONTH(h.menu_date), COUNT(*) FROM shop AS h WHERE h.shop_type <> 3 AND " + Db.NotCountAdj("h") + " AND " + Db.MainShop("h") + " AND" + w + g, p.From, p.ToEx), 0);
            add(Db.Query("SELECT YEAR(h.menu_date), MONTH(h.menu_date), SUM(l.qty) FROM shop AS h INNER JOIN shop_menu AS l ON h.shop_no = l.seq WHERE h.shop_type <> 3 AND " + Db.NotCountAdj("h") + " AND" + w + g, p.From, p.ToEx), 1);
            add(Db.Query("SELECT YEAR(h.menu_date), MONTH(h.menu_date), COUNT(*) FROM sale AS h WHERE " + Db.IssueOnly("h") + " AND" + w + g, p.From, p.ToEx), 2);
            add(Db.Query("SELECT YEAR(h.menu_date), MONTH(h.menu_date), SUM(l.qty) FROM sale AS h INNER JOIN sale_menu AS l ON h.shop_no = l.seq WHERE " + Db.IssueOnly("h") + " AND" + w + g, p.From, p.ToEx), 3);
            add(Db.Query("SELECT YEAR(h.menu_date), MONTH(h.menu_date), COUNT(*) FROM shop AS h WHERE h.shop_type = 3 AND" + w + g, p.From, p.ToEx), 4);
            add(Db.Query("SELECT YEAR(h.menu_date), MONTH(h.menu_date), SUM(l.qty) FROM shop AS h INNER JOIN shop_menu AS l ON h.shop_no = l.seq WHERE h.shop_type = 3 AND" + w + g, p.From, p.ToEx), 5);
            r.Col("الشهر", ColKind.Text); r.ColT("سندات الإدخال"); r.ColT("كمية الإدخال"); r.ColT("سندات الإخراج"); r.ColT("كمية الإخراج"); r.ColT("سندات المردود"); r.ColT("كمية المردود");
            r.Chart = new List<KeyValuePair<string, double[]>>(); r.ChartSeries = new string[] { "سندات الإدخال", "سندات الإخراج", "سندات المردود" };
            foreach (KeyValuePair<int, double[]> kv in m)
            {
                string label = (kv.Key / 100) + "/" + (kv.Key % 100).ToString("00");
                double[] v = kv.Value;
                r.Row(null, label, v[0], v[1], v[2], v[3], v[4], v[5]);
                r.Chart.Add(new KeyValuePair<string, double[]>(label, new double[] { v[0], v[2], v[4] }));
            }
            return r;
        }

        static ReportData ItemList(ReportParams p)
        {
            ReportData r = new ReportData();
            r.Title = "دليل المواد"; r.Subtitle = p.TypeNo > 0 ? "النوع: " + Cache.Type(p.TypeNo) : "كل الأنواع";
            r.Col("ت", ColKind.Int); r.Col("الرمز", ColKind.Text); r.Col("اسم المادة", ColKind.Text); r.Col("النوع", ColKind.Text); r.Col("المجموعة", ColKind.Text); r.Col("الوحدة", ColKind.Text);
            r.Col("حد إعادة الطلب", ColKind.Num); r.Col("الحالة", ColKind.Text);
            int n = 0;
            foreach (DataRow ir in Cache.Items.Select("", "item_name"))
            {
                if (!ItemMatches(ir, p)) continue;
                r.Row(Db.B(ir["freez"]) ? "muted" : null, ++n, Db.S(ir["item_code"]), Db.S(ir["item_name"]), Db.S(ir["type_name"]), Db.S(ir["group_name"]), Db.S(ir["unit"]),
                    Db.D(ir["item_bal_ret"]) == 0 ? null : (object)Db.D(ir["item_bal_ret"]), Db.B(ir["freez"]) ? "مجمدة" : "");
                r.Link(RowLinks.Item(Db.I(ir["item_no"])));
            }
            r.Sum("عدد المواد", n.ToString("#,##0", Inv));
            return r;
        }

        static ReportData PersonList(ReportParams p)
        {
            ReportData r = new ReportData();
            r.Title = "دليل الجهات والأشخاص"; r.Subtitle = "";
            Dictionary<int, DateTime> last = new Dictionary<int, DateTime>();
            foreach (DataRow x in Db.Query("SELECT custom_no, MAX(menu_date) FROM sale GROUP BY custom_no").Rows) last[Db.I(x[0])] = Db.Dt(x[1]);
            r.Col("ت", ColKind.Int); r.Col("الاسم", ColKind.Text); r.Col("النوع", ColKind.Text); r.Col("الهاتف", ColKind.Text); r.Col("العنوان", ColKind.Text); r.Col("آخر إخراج", ColKind.Date);
            int n = 0;
            string norm = UI.Norm(p.Text);
            foreach (DataRow pr in Cache.Persons.Select("", "p_name"))
            {
                if (norm.Length > 0 && Db.S(pr["_s"]).IndexOf(norm, StringComparison.Ordinal) < 0) continue;
                DateTime d; bool has = last.TryGetValue(Db.I(pr["p_no"]), out d);
                r.Row(null, ++n, Db.S(pr["p_name"]), Db.S(pr["type_name"]), Db.S(pr["p_tel"]), Db.S(pr["p_addrs"]), has ? (object)d : null);
                r.Link(RowLinks.Person(Db.I(pr["p_no"]), new DateTime(2000, 1, 1), DateTime.Today));
            }
            r.Sum("العدد", n.ToString("#,##0", Inv));
            return r;
        }

        static ReportData Audit(ReportParams p)
        {
            ReportData r = new ReportData();
            r.Title = "تدقيق البيانات"; r.Subtitle = "فحص بتاريخ " + UI.Date(DateTime.Today);
            r.Col("ت", ColKind.Int); r.Col("نوع الملاحظة", ColKind.Text); r.Col("التفاصيل", ColKind.Text); r.Col("المسجل", ColKind.Num); r.Col("الصحيح", ColKind.Num);
            int n = 0;
            // 1. stored balance vs movements
            Dictionary<int, Dictionary<int, double>> bal = Balances(null);
            int mism = 0;
            foreach (DataRow x in Db.Query("SELECT item_no, stor, item_bal FROM item_stor").Rows)
            {
                int it = Db.I(x[0]), st = Db.I(x[1]);
                Dictionary<int, double> m; double c = 0; if (bal.TryGetValue(it, out m)) m.TryGetValue(st, out c);
                if (Math.Abs(c - Db.D(x[2])) > 0.001)
                {
                    mism++;
                    r.Row("neg", ++n, "رصيد غير مطابق للحركات", Cache.ItemName(it) + " — " + Cache.Store(st), Db.D(x[2]), c);
                    r.Link(RowLinks.Item(it));
                }
            }
            // 2. negative balances
            int neg = 0;
            foreach (DataRow x in Db.Query("SELECT item_no, stor, item_bal FROM item_stor WHERE item_bal < -0.0001").Rows)
            { neg++; r.Row(null, ++n, "رصيد سالب", Cache.ItemName(Db.I(x[0])) + " — " + Cache.Store(Db.I(x[1])), Db.D(x[2]), null); r.Link(RowLinks.Item(Db.I(x[0]))); }
            // 3. future dates
            DateTime tomorrow = DateTime.Today.AddDays(1);
            foreach (DataRow x in Db.Query("SELECT menu_no, menu_date FROM sale WHERE menu_date >= ?", tomorrow).Rows)
                r.Row(null, ++n, "تاريخ مستقبلي", "سند إخراج رقم " + Db.S(x[0]) + " بتاريخ " + UI.Date(Db.Dt(x[1])), null, null);
            foreach (DataRow x in Db.Query("SELECT menu_no, menu_date, shop_type FROM shop WHERE " + Db.MainShop("") + " AND menu_date >= ?", tomorrow).Rows)
                r.Row(null, ++n, "تاريخ مستقبلي", "سند " + (Db.I(x[2]) == 3 ? "إعادة" : "استلام") + " رقم " + Db.S(x[0]) + " بتاريخ " + UI.Date(Db.Dt(x[1])), null, null);
            // 4. duplicated numbers
            foreach (DataRow x in Db.Query("SELECT menu_no, COUNT(*) FROM sale GROUP BY menu_no HAVING COUNT(*) > 1").Rows)
                r.Row(null, ++n, "رقم مكرر", "سند إخراج رقم " + Db.S(x[0]) + " مكرر " + Db.I(x[1]) + " مرات", null, null);
            foreach (DataRow x in Db.Query("SELECT menu_no, COUNT(*) FROM shop WHERE " + Db.MainShop("") + " GROUP BY menu_no HAVING COUNT(*) > 1").Rows)
                r.Row(null, ++n, "رقم مكرر", "سند استلام رقم " + Db.S(x[0]) + " مكرر " + Db.I(x[1]) + " مرات", null, null);
            // 5. empty documents
            foreach (DataRow x in Db.Query("SELECT menu_no, menu_date FROM sale AS h WHERE NOT EXISTS (SELECT 1 FROM sale_menu m WHERE m.seq = h.shop_no)").Rows)
                r.Row(null, ++n, "سند بدون مواد", "سند إخراج رقم " + Db.S(x[0]) + " — " + UI.Date(Db.Dt(x[1])), null, null);
            foreach (DataRow x in Db.Query("SELECT menu_no, menu_date FROM shop AS h WHERE NOT EXISTS (SELECT 1 FROM shop_menu m WHERE m.seq = h.shop_no)").Rows)
                r.Row(null, ++n, "سند بدون مواد", "سند استلام رقم " + Db.S(x[0]) + " — " + UI.Date(Db.Dt(x[1])), null, null);
            // 6. lines with unknown item
            int unk = Db.I(Db.Scalar("SELECT COUNT(*) FROM sale_menu AS l WHERE NOT EXISTS (SELECT 1 FROM item i WHERE i.item_no = l.item_no)")) +
                      Db.I(Db.Scalar("SELECT COUNT(*) FROM shop_menu AS l WHERE NOT EXISTS (SELECT 1 FROM item i WHERE i.item_no = l.item_no)"));
            if (unk > 0) r.Row(null, ++n, "مواد غير معرفة", unk + " سطر في السندات يشير إلى مادة محذوفة", null, null);
            r.Sum("عدد الملاحظات", n.ToString());
            r.Sum("أرصدة غير مطابقة", mism.ToString());
            r.Sum("أرصدة سالبة", neg.ToString());
            if (mism > 0) r.Footer = "لتصحيح الأرصدة غير المطابقة: الإعدادات ← إعادة احتساب الأرصدة (للمدير).";
            if (n == 0) r.Footer = "لا توجد ملاحظات ✓ البيانات سليمة.";
            return r;
        }

        // ------------------------------------------------------------ voucher
        public static ReportData Voucher(Doc d)
        {
            ReportData r = new ReportData();
            string adj = StockCount.AdjustmentText(d);            // count adjustment / posted write-off: no recipient
            string kind = adj ?? Posting.KindName(d.Kind);
            r.Title = kind + " رقم " + d.No;
            List<int> stores = d.Kind == DocKind.Receipt ? d.Lines.Select(l => l.Store > 0 ? l.Store : d.Store).Distinct().OrderBy(x => x).ToList() : new List<int>();
            bool many = stores.Count > 1;         // items in several stores: a column with the store of each
            List<string> sub = new List<string>();
            sub.Add("التاريخ: " + UI.Date(d.Date));
            if (d.Kind == DocKind.Transfer) { sub.Add("من مخزن: " + Cache.Store(d.Store)); sub.Add("إلى مخزن: " + Cache.Store(d.Store2)); }
            else
            {
                // issues / issue returns: the cost centre is the recipient («المستلم = مركز الكلفة»), one line
                bool ccIsRecipient = Posting.CcIsRecipient(d.Kind) && d.Project > 0;
                if (ccIsRecipient) sub.Add((d.Kind == DocKind.Return ? "مركز الكلفة (المُعاد منه): " : "مركز الكلفة (المستلم): ") + Cache.Project(d.Project));
                else if (d.Project > 0) sub.Add("مركز الكلفة: " + Cache.Project(d.Project));
                if (d.Kind != DocKind.Reserve && !ccIsRecipient && adj == null)
                    sub.Add((d.Kind == DocKind.Issue ? "المستلم: " : d.Kind == DocKind.Return ? "الجهة المُعيدة: " : "المجهز: ") + Cache.Person(d.Person));
                sub.Add(many ? "إلى المخازن: " + string.Join("، ", stores.Select(x => Cache.Store(x)).ToArray()) : (Posting.IsOut(d.Kind) ? "من مخزن: " : "إلى مخزن: ") + Cache.Store(d.Store));
                if (d.Kind == DocKind.Receipt) sub.Add("النوع: " + Cache.ReceiptTypes[d.RType == 1 ? 1 : 2]);
                if (d.Kind == DocKind.Reserve && d.Status == 0) sub.Add("الحالة: ملغى");
            }
            r.Subtitle = string.Join("     |     ", sub.ToArray());
            r.Info = new List<KeyValuePair<string, string>>();
            foreach (string s in sub) { int i = s.IndexOf(": "); r.Info.Add(new KeyValuePair<string, string>(s.Substring(0, i), s.Substring(i + 2))); }
            switch (d.Kind)
            {
                case DocKind.Receipt: r.SigLabels = new[] { "المُسلِّم (المجهز)", "أمين المخزن", "المدير" }; break;
                case DocKind.Return: r.SigLabels = new[] { "المُعيد", "أمين المخزن", "المدير" }; break;
                case DocKind.PurchaseReturn: r.SigLabels = new[] { "مستلم المردود (المجهز)", "أمين المخزن", "المدير" }; break;
                case DocKind.Transfer: r.SigLabels = new[] { "أمين المخزن المُرسِل", "أمين المخزن المستلم", "المدير" }; break;
                case DocKind.Reserve: r.SigLabels = new[] { "مسؤول مركز الكلفة", "أمين المخزن", "المدير" }; break;
            }
            bool price = d.Kind == DocKind.Receipt && d.Lines.Any(l => l.Price != 0);
            // out documents: the shelf location «الموقع» helps the storekeeper pick (shown when any line has one)
            bool bin = Posting.IsOut(d.Kind) && d.Lines.Any(l => Cache.Bin(l.ItemNo, d.Store).Length > 0);
            r.Col("ت", ColKind.Int); r.Col("الرمز", ColKind.Text); r.Col("اسم المادة", ColKind.Text); r.Col("الوحدة", ColKind.Text);
            if (bin) r.Col("الموقع", ColKind.Text);
            r.ColT("الكمية");
            if (many) r.Col("المخزن", ColKind.Text);
            if (price) { r.Col("سعر الوحدة", ColKind.Num); r.ColT("المبلغ"); }
            int n = 0;
            foreach (DocLine l in d.Lines)
            {
                List<object> v = new List<object> { ++n, Cache.ItemCode(l.ItemNo), Cache.ItemName(l.ItemNo), Unit(l.ItemNo) };
                if (bin) v.Add(Cache.Bin(l.ItemNo, d.Store));
                v.Add(l.Qty);
                if (many) v.Add(Cache.Store(l.Store > 0 ? l.Store : d.Store));
                if (price) { v.Add(l.Price); v.Add(l.Qty * l.Price); }
                r.Row(null, v.ToArray());
            }
            if (!string.IsNullOrEmpty(d.Note)) r.Footer = "ملاحظات: " + d.Note;
            r.Signatures = true;
            return r;
        }
    }
}
