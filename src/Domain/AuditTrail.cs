using System.Collections.Generic;
using System.Data.Common;
using System.Data;
using System.Globalization;
using System.Linq;
using System.Text;
using System;

namespace Stores
{
    /// <summary>
    /// Audit trail with before / after values «سجل التدقيق». Every app_log row may have detail rows in app_log_detail
    /// (log_id, tbl, rec_key, fld, old_v, new_v). Written inside the caller's transaction with the tx overloads only:
    ///  - editing an existing document: changed header fields, and lines added / removed / changed (qty, price);
    ///    lines are matched by item (the original tables re-insert the lines of an issue on every save);
    ///  - deleting (or cancelling) a document: the header and every line as they were;
    ///  - item master data, opening balances and cost centres: the changed fields;
    ///  - the month lock (PeriodLock).
    /// Values are stored as readable text (names, not numbers) so the viewer needs no lookups.
    /// </summary>
    static class AuditTrail
    {
        public const string Table = "app_log_detail";
        static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

        /// <summary>True when app_log_detail exists (set by EnsureSchema).</summary>
        public static bool Ready;

        /// <summary>Creates app_log_detail (+ indexes on log_id and app_log.log_at) once. Called by Db.EnsureAppTables.</summary>
        public static void EnsureSchema()
        {
            if (!Db.TableExists(Table))
            {
                try
                {
                    Db.Exec(Db.IsSql
                        ? "CREATE TABLE app_log_detail (id INT IDENTITY(1,1) PRIMARY KEY, log_id INT NOT NULL, tbl NVARCHAR(30), rec_key NVARCHAR(50), fld NVARCHAR(150), old_v NVARCHAR(255), new_v NVARCHAR(255))"
                        : "CREATE TABLE app_log_detail (id COUNTER PRIMARY KEY, log_id INT, tbl TEXT(30), rec_key TEXT(50), fld TEXT(150), old_v TEXT(255), new_v TEXT(255))");
                    try { Db.Exec("CREATE INDEX ix_logd_log ON app_log_detail (log_id)"); } catch { }
                    try { Db.Exec("CREATE INDEX ix_log_at ON app_log (log_at)"); } catch { }
                }
                catch (Exception ex) { Db.SchemaWarning = Table + ": " + ex.Message; }
            }
            Ready = Db.TableExists(Table);
        }

        // ------------------------------------------------------------ writing
        /// <summary>Like Db.Log, but returns the new app_log id (0 when it could not be written).</summary>
        public static int Log(DbTransaction tx, string act, string details)
        {
            try
            {
                if (details != null && details.Length > 250) details = details.Substring(0, 250);
                return Db.Insert(tx, "INSERT INTO app_log (log_at, user_name, computer, act, details) VALUES (?,?,?,?,?)",
                                 Db.Now(), Session.UserName ?? "", Environment.MachineName, act, details ?? "");
            }
            catch { return 0; }
        }

        static string Cut(string s, int n) { s = s ?? ""; return s.Length > n ? s.Substring(0, n) : s; }

        public static void Detail(DbTransaction tx, int logId, string tbl, string key, string fld, string oldV, string newV)
        {
            if (logId <= 0 || !Ready) return;
            try
            {
                Db.X(tx, "INSERT INTO app_log_detail (log_id, tbl, rec_key, fld, old_v, new_v) VALUES (?, ?, ?, ?, ?, ?)",
                     logId, Cut(tbl, 30), Cut(key, 50), Cut(fld, 150), Cut(oldV, 250), Cut(newV, 250));
            }
            catch { }
        }

        /// <summary>Writes one detail row per field whose value differs (after = null: a deleted record, every field).</summary>
        public static void Changes(DbTransaction tx, int logId, string tbl, string key, List<KeyValuePair<string, string>> before, List<KeyValuePair<string, string>> after)
        {
            if (logId <= 0 || !Ready || before == null) return;
            Dictionary<string, string> a = new Dictionary<string, string>();
            if (after != null) foreach (KeyValuePair<string, string> kv in after) a[kv.Key] = kv.Value;
            foreach (KeyValuePair<string, string> kv in before)
            {
                string nv;
                if (after == null) { if (kv.Value.Length > 0) Detail(tx, logId, tbl, key, kv.Key, kv.Value, ""); continue; }
                if (!a.TryGetValue(kv.Key, out nv)) nv = "";
                if (nv != kv.Value) Detail(tx, logId, tbl, key, kv.Key, kv.Value, nv);
            }
        }

        /// <summary>Logs a document save / delete with its details. before = null for a new document (no details);
        /// after = null for a deleted document.</summary>
        public static int LogDoc(DbTransaction tx, string act, string details, DocSnap before, DocSnap after)
        {
            int id = Log(tx, act, details);
            if (id <= 0 || !Ready || before == null) return id;
            string key = after != null ? after.No : before.No;
            Changes(tx, id, before.HeadTable, key, before.Head, after == null ? null : after.Head);
            // lines, matched by item in their order
            Dictionary<int, List<SnapLine>> oldBy = ByItem(before.Lines), newBy = ByItem(after == null ? new List<SnapLine>() : after.Lines);
            List<int> items = before.Lines.Select(l => l.Item).Concat(after == null ? new int[0] : after.Lines.Select(l => l.Item)).Distinct().ToList();
            foreach (int it in items)
            {
                List<SnapLine> o, n;
                if (!oldBy.TryGetValue(it, out o)) o = new List<SnapLine>();
                if (!newBy.TryGetValue(it, out n)) n = new List<SnapLine>();
                string lbl = ItemLabel(it);
                for (int i = 0; i < Math.Max(o.Count, n.Count); i++)
                {
                    if (i >= n.Count) Detail(tx, id, before.LineTable, key, "مادة محذوفة: " + lbl, LineText(o[i], before.HasPrice), "");
                    else if (i >= o.Count) Detail(tx, id, before.LineTable, key, "مادة مضافة: " + lbl, "", LineText(n[i], before.HasPrice));
                    else
                    {
                        if (Math.Abs(o[i].Qty - n[i].Qty) > 1e-9) Detail(tx, id, before.LineTable, key, "الكمية: " + lbl, UI.Num(o[i].Qty), UI.Num(n[i].Qty));
                        if (before.HasPrice && Math.Abs(o[i].Price - n[i].Price) > 1e-9) Detail(tx, id, before.LineTable, key, "السعر: " + lbl, UI.Num(o[i].Price), UI.Num(n[i].Price));
                    }
                }
            }
            return id;
        }

        static Dictionary<int, List<SnapLine>> ByItem(List<SnapLine> lines)
        {
            Dictionary<int, List<SnapLine>> d = new Dictionary<int, List<SnapLine>>();
            foreach (SnapLine l in lines) { List<SnapLine> x; if (!d.TryGetValue(l.Item, out x)) { x = new List<SnapLine>(); d[l.Item] = x; } x.Add(l); }
            return d;
        }

        static string LineText(SnapLine l, bool price) { return "الكمية " + UI.Num(l.Qty) + (price && Math.Abs(l.Price) > 1e-9 ? " × السعر " + UI.Num(l.Price) : ""); }

        public static string ItemLabel(int item)
        {
            string c = Cache.ItemCode(item);
            return (c.Length > 0 ? c + " " : "") + Cache.ItemName(item);
        }

        // ------------------------------------------------------------ snapshots (inside the transaction)
        public class SnapLine { public int Item; public double Qty, Price; }

        public class DocSnap
        {
            public string No, HeadTable, LineTable;
            public bool HasPrice;
            public List<KeyValuePair<string, string>> Head = new List<KeyValuePair<string, string>>();
            public List<SnapLine> Lines = new List<SnapLine>();
            public void Add(string label, string value) { Head.Add(new KeyValuePair<string, string>(label, value ?? "")); }
        }

        static string V(object o)
        {
            if (o == null || o is DBNull) return "";
            if (o is DateTime) return UI.Date((DateTime)o);
            if (o is double || o is float || o is decimal) return UI.Num(Convert.ToDouble(o));
            return Convert.ToString(o, Inv).Trim();
        }

        static string Has(DataRow r, string col) { return r.Table.Columns.Contains(col) ? V(r[col]) : ""; }

        /// <summary>The stored document as readable fields and lines (null when it does not exist).</summary>
        public static DocSnap ReadDoc(DbTransaction tx, DocKind kind, int id)
        {
            if (id <= 0) return null;
            DocSnap s = new DocSnap();
            DataTable h, l;
            if (Posting.IsSale(kind))
            {
                s.HeadTable = "sale"; s.LineTable = "sale_menu"; s.HasPrice = true;
                h = Db.Q(tx, "SELECT menu_no, menu_date, custom_no, stor, [note]" + (Db.HasProj ? ", proj_no" : "") + " FROM sale WHERE shop_no = ?", id);
                l = Db.Q(tx, "SELECT item_no, qty, price_dl FROM sale_menu WHERE seq = ? ORDER BY auto_seq", id);
            }
            else if (kind == DocKind.Transfer)
            {
                s.HeadTable = "trans"; s.LineTable = "trans_menu";
                h = Db.Q(tx, "SELECT menu_no, menu_date, stor_out, stor_in, notes FROM trans WHERE shop_no = ?", id);
                l = Db.Q(tx, "SELECT item_no, qty FROM trans_menu WHERE seq = ? ORDER BY item_no, qty", id);
            }
            else if (kind == DocKind.Reserve)
            {
                s.HeadTable = "reserve"; s.LineTable = "reserve_menu";
                h = Db.Q(tx, "SELECT menu_no, res_date AS menu_date, proj_no, stor, notes, status FROM reserve WHERE res_no = ?", id);
                l = Db.Q(tx, "SELECT item_no, qty FROM reserve_menu WHERE seq = ? ORDER BY id", id);
            }
            else
            {
                s.HeadTable = "shop"; s.LineTable = "shop_menu"; s.HasPrice = true;
                h = Db.Q(tx, "SELECT menu_no, menu_date, custom_no, stor, [note], shop_type" + (Db.HasProj ? ", proj_no" : "") + " FROM shop WHERE shop_no = ?", id);
                l = Db.Q(tx, "SELECT item_no, qty, price_dl FROM shop_menu WHERE seq = ? ORDER BY auto_seq", id);
            }
            if (h.Rows.Count == 0) return null;
            DataRow r = h.Rows[0];
            s.No = V(r["menu_no"]);
            s.Add("التاريخ", V(r["menu_date"]));
            if (kind == DocKind.Transfer)
            {
                s.Add("من مخزن", Cache.Store(Db.I(r["stor_out"])));
                s.Add("إلى مخزن", Cache.Store(Db.I(r["stor_in"])));
                s.Add("الملاحظات", V(r["notes"]));
            }
            else
            {
                s.Add("المخزن", Cache.Store(Db.I(r["stor"])));
                if (r.Table.Columns.Contains("custom_no") && !Posting.CcIsRecipient(kind))
                    s.Add(kind == DocKind.Issue ? "المستلم" : "الجهة", Cache.Person(Db.I(r["custom_no"])));
                if (r.Table.Columns.Contains("proj_no")) s.Add("مركز الكلفة", Cache.Project(Db.I(r["proj_no"])));
                if (r.Table.Columns.Contains("shop_type"))
                {
                    int t = Db.I(r["shop_type"]);
                    s.Add("نوع الإدخال", t >= 0 && t < Cache.ReceiptTypes.Length ? Cache.ReceiptTypes[t] : t.ToString(Inv));
                }
                if (r.Table.Columns.Contains("status")) s.Add("الحالة", Db.I(r["status"]) == 0 ? "ملغى" : "قائم");
                s.Add("الملاحظات", r.Table.Columns.Contains("note") ? V(r["note"]) : Has(r, "notes"));
            }
            foreach (DataRow x in l.Rows)
            {
                SnapLine sl = new SnapLine();
                sl.Item = Db.I(x["item_no"]); sl.Qty = Db.D(x["qty"]);
                if (s.HasPrice) sl.Price = Db.D(x["price_dl"]);
                s.Lines.Add(sl);
            }
            return s;
        }

        static List<KeyValuePair<string, string>> Pairs(params string[] kv)
        {
            List<KeyValuePair<string, string>> l = new List<KeyValuePair<string, string>>();
            for (int i = 0; i + 1 < kv.Length; i += 2) l.Add(new KeyValuePair<string, string>(kv[i], kv[i + 1] ?? ""));
            return l;
        }

        /// <summary>Item master data as readable fields (null when it does not exist).</summary>
        public static List<KeyValuePair<string, string>> ItemSnap(DbTransaction tx, int id)
        {
            DataTable t = Db.Q(tx, "SELECT * FROM item WHERE item_no = ?", id);
            if (t.Rows.Count == 0) return null;
            DataRow r = t.Rows[0];
            string grp; Cache.GroupName.TryGetValue(Db.I(r["group"]), out grp);
            return Pairs("الاسم", V(r["item_name"]), "الرمز", V(r["item_code"]), "المجموعة", grp ?? "", "النوع", Cache.Type(Db.I(r["type"])),
                         "الوحدة", Has(r, "unit"), "حد إعادة الطلب", V(r["item_bal_ret"]), "الترتيب في التقارير", V(r["tslsl"]),
                         "مجمدة", Db.B(r["freez"]) ? "نعم" : "لا");
        }

        /// <summary>Cost centre fields as readable text (null when it does not exist).</summary>
        public static List<KeyValuePair<string, string>> CostCentreSnap(DbTransaction tx, int id)
        {
            DataTable t = Db.Q(tx, "SELECT * FROM project WHERE proj_no = ?", id);
            if (t.Rows.Count == 0) return null;
            DataRow r = t.Rows[0];
            int st = Db.I(r["status"]);
            return Pairs("الاسم", V(r["proj_name"]), "الرمز", V(r["proj_code"]),
                         "النوع", r.Table.Columns.Contains("cc_type") ? CostCentres.TypeName(CostCentres.NormType(Db.I(r["cc_type"]))) : "",
                         "الحالة", st == 2 ? "مغلق" : "فعّال", "الجهة المستفيدة", V(r["client"]), "تاريخ البدء", V(r["start_date"]), "الملاحظات", V(r["notes"]));
        }

        // ------------------------------------------------------------ reading (viewer and report)
        /// <summary>The kinds of operation offered in the viewer's filter, each with its app_log.act values.</summary>
        public static List<KeyValuePair<string, string[]>> ActGroups()
        {
            List<KeyValuePair<string, string[]>> g = new List<KeyValuePair<string, string[]>>();
            foreach (DocKind k in Enum.GetValues(typeof(DocKind)))
            {
                string n = Posting.KindName(k);
                if (g.Any(x => x.Key == n)) continue;
                g.Add(new KeyValuePair<string, string[]>(n, new[] { "إضافة " + n, "تعديل " + n, "حذف " + n, "إلغاء " + n }));
            }
            g.Add(new KeyValuePair<string, string[]>("المواد", new[] { "إضافة مادة", "تعديل مادة", "حذف مادة", "رصيد افتتاحي" }));
            g.Add(new KeyValuePair<string, string[]>("مراكز الكلفة", new[] { "إضافة مركز كلفة", "تعديل مركز كلفة", "إغلاق مركز كلفة", "إعادة فتح مركز كلفة" }));
            g.Add(new KeyValuePair<string, string[]>("قفل الفترات وإقفال السنة", new[] { PeriodLock.ActLock, PeriodLock.ActUnlock, "إقفال السنة", "إعادة فتح سنة" }));
            return g;
        }

        /// <summary>Audit rows, newest first: id, log_at, user_name, act, details, tbl, rec_key, fld, old_v, new_v.
        /// user / docNo empty = all; acts null = all; onlyChanges = only operations with before/after details.</summary>
        public static DataTable Query(DateTime from, DateTime toEx, string user, string docNo, string[] acts, bool onlyChanges, int top)
        {
            if (!Ready) return new DataTable();
            StringBuilder w = new StringBuilder(" WHERE l.log_at >= ? AND l.log_at < ?");
            List<object> a = new List<object> { from, toEx };
            if (!string.IsNullOrEmpty(user)) { w.Append(" AND l.user_name = ?"); a.Add(user); }
            if (!string.IsNullOrEmpty(docNo))
            {
                docNo = docNo.Trim();
                w.Append(" AND (d.rec_key = ? OR l.details = ? OR l.details LIKE ?)");
                a.Add(docNo); a.Add("رقم " + docNo); a.Add("رقم " + docNo.Replace("[", "[[]").Replace("%", "[%]").Replace("_", "[_]") + " -%");
            }
            if (acts != null && acts.Length > 0)
            {
                w.Append(" AND l.act IN (" + string.Join(", ", acts.Select(x => "?").ToArray()) + ")");
                a.AddRange(acts);
            }
            if (onlyChanges) w.Append(" AND d.id IS NOT NULL");
            string sql = "SELECT TOP " + Math.Max(1, top) + " l.id, l.log_at, l.user_name, l.act, l.details, d.tbl, d.rec_key, d.fld, d.old_v, d.new_v " +
                         "FROM app_log AS l " + (onlyChanges ? "INNER" : "LEFT") + " JOIN app_log_detail AS d ON d.log_id = l.id" + w + " ORDER BY l.id DESC, d.id";
            return Db.Query(sql, a.ToArray());
        }

        /// <summary>Report «سجل التدقيق» (Reports.All): edits and deletions in the period with before / after; the text
        /// filter matches the user, the operation, the document number or the field.</summary>
        public static ReportData Report(ReportParams p)
        {
            ReportData r = new ReportData();
            r.Title = "سجل التدقيق (القيم قبل وبعد التعديل)";
            r.Subtitle = "من " + UI.Date(p.From) + " إلى " + UI.Date(p.To) + (string.IsNullOrEmpty(p.Text) ? "" : " • بحث: " + p.Text);
            r.Col("الوقت", ColKind.Text); r.Col("المستخدم", ColKind.Text); r.Col("العملية", ColKind.Text); r.Col("السند / السجل", ColKind.Text);
            r.Col("الحقل", ColKind.Text); r.Col("القيمة السابقة", ColKind.Text); r.Col("القيمة الجديدة", ColKind.Text);
            string q = (p.Text ?? "").Trim();
            int ops = 0, last = 0;
            foreach (DataRow x in Query(p.From.Date, p.ToEx, null, null, null, true, 20000).Rows)
            {
                string key = Db.S(x["rec_key"]);
                if (q.Length > 0 && Db.S(x["user_name"]).IndexOf(q, StringComparison.OrdinalIgnoreCase) < 0 && Db.S(x["act"]).IndexOf(q, StringComparison.OrdinalIgnoreCase) < 0 &&
                    key != q && Db.S(x["fld"]).IndexOf(q, StringComparison.OrdinalIgnoreCase) < 0) continue;
                int id = Db.I(x["id"]); if (id != last) { ops++; last = id; }
                r.Row(null, Db.Dt(x["log_at"]).ToString("yyyy/MM/dd HH:mm", Inv), Db.S(x["user_name"]), Db.S(x["act"]), key, Db.S(x["fld"]), Db.S(x["old_v"]), Db.S(x["new_v"]));
            }
            r.Sum("العمليات", ops.ToString("#,##0", Inv)); r.Sum("التغييرات", r.Rows.Count.ToString("#,##0", Inv));
            return r;
        }
    }
}
