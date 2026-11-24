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
    /// Saving and deleting documents. Everything happens inside one transaction and keeps the
    /// original program's tables consistent: item_stor balances, FIFO links (sale_menu_1 /
    /// shop_menu.qty_sale) and document numbering.
    /// Before changing an existing document we check that nobody changed it on another computer
    /// since it was opened (optimistic concurrency).
    /// </summary>
    static class Posting
    {
        static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

        public static string KindName(DocKind k)
        {
            switch (k)
            {
                case DocKind.Issue: return "إخراج مخزني";
                case DocKind.Receipt: return "إدخال مخزني";
                case DocKind.Return: return "مردود إخراج مخزني";
                case DocKind.PurchaseReturn: return "مردود مشتريات";
                case DocKind.Reserve: return "حجز مواد لمركز كلفة";
                default: return "نقل مخزني";
            }
        }

        /// <summary>Owner's decision «المستلم = مركز الكلفة»: in issues and issue returns the cost centre is the recipient;
        /// custom_no is set on saving to the cost centre's linked person (CostCentres.PersonFor).</summary>
        public static bool CcIsRecipient(DocKind k) { return Db.HasProj && Db.HasCcPerson && (k == DocKind.Issue || k == DocKind.Return); }

        public static bool IsSale(DocKind k) { return k == DocKind.Issue || k == DocKind.PurchaseReturn; }
        public static bool IsShop(DocKind k) { return k == DocKind.Receipt || k == DocKind.Return; }
        /// <summary>Documents that take quantity out of a store.</summary>
        public static bool IsOut(DocKind k) { return k == DocKind.Issue || k == DocKind.PurchaseReturn || k == DocKind.Transfer || k == DocKind.Reserve; }

        static string Head(DocKind k) { return IsSale(k) ? "sale" : k == DocKind.Transfer ? "trans" : k == DocKind.Reserve ? "reserve" : "shop"; }
        static string IdCol(DocKind k) { return k == DocKind.Reserve ? "res_no" : "shop_no"; }
        static string Lock { get { return Db.IsSql ? " WITH (UPDLOCK, HOLDLOCK)" : ""; } }

        static string TypeCond(DocKind k)
        {
            if (k == DocKind.Receipt) return " AND shop_type <> 3 AND " + Db.NotCountAdj("");
            if (k == DocKind.Return) return " AND shop_type = 3";
            if (k == DocKind.Issue) return Db.HasProj ? " AND " + Db.IssueOnly("") : "";
            if (k == DocKind.PurchaseReturn) return " AND doc_kind = 1";
            return "";
        }

        static string ProjCols { get { return Db.HasProj ? ", proj_no" : ""; } }

        // ------------------------------------------------------------ load
        public static Doc Load(DocKind kind, int id)
        {
            Doc d = new Doc();
            d.Kind = kind; d.Id = id;
            if (IsSale(kind))
            {
                DataTable h = Db.Query("SELECT shop_no, menu_no, menu_date, custom_no, stor, [note]" + (Db.HasProj ? ", proj_no, doc_kind" : "") + " FROM sale WHERE shop_no = ?", id);
                if (h.Rows.Count == 0) return null;
                DataRow r = h.Rows[0];
                d.No = Db.S(r["menu_no"]); d.Date = Db.Dt(r["menu_date"]); d.Person = Db.I(r["custom_no"]); d.Store = Db.I(r["stor"]); d.Note = Db.S(r["note"]);
                if (Db.HasProj) { d.Project = Db.I(r["proj_no"]); d.Kind = Db.I(r["doc_kind"]) == 1 ? DocKind.PurchaseReturn : DocKind.Issue; }
                foreach (DataRow l in Db.Query("SELECT auto_seq, item_no, qty, price_dl FROM sale_menu WHERE seq = ? ORDER BY auto_seq", id).Rows)
                {
                    DocLine x = new DocLine();
                    x.LineId = Db.I(l[0]); x.ItemNo = Db.I(l[1]); x.Qty = Db.D(l[2]); x.Price = Db.D(l[3]);
                    d.Lines.Add(x);
                }
            }
            else if (kind == DocKind.Transfer)
            {
                DataTable h = Db.Query("SELECT shop_no, menu_no, menu_date, stor_out, stor_in, notes FROM trans WHERE shop_no = ?", id);
                if (h.Rows.Count == 0) return null;
                DataRow r = h.Rows[0];
                d.No = Db.S(r["menu_no"]); d.Date = Db.Dt(r["menu_date"]); d.Store = Db.I(r["stor_out"]); d.Store2 = Db.I(r["stor_in"]); d.Note = Db.S(r["notes"]);
                foreach (DataRow l in Db.Query("SELECT item_no, qty FROM trans_menu WHERE seq = ? ORDER BY item_no, qty", id).Rows)
                {
                    DocLine x = new DocLine();
                    x.ItemNo = Db.I(l[0]); x.Qty = Db.D(l[1]);
                    d.Lines.Add(x);
                }
            }
            else if (kind == DocKind.Reserve)
            {
                DataTable h = Db.Query("SELECT res_no, menu_no, res_date, proj_no, stor, notes, status FROM reserve WHERE res_no = ?", id);
                if (h.Rows.Count == 0) return null;
                DataRow r = h.Rows[0];
                d.No = Db.S(r["menu_no"]); d.Date = Db.Dt(r["res_date"]); d.Project = Db.I(r["proj_no"]); d.Store = Db.I(r["stor"]); d.Note = Db.S(r["notes"]);
                d.Status = Db.I(r["status"]);
                foreach (DataRow l in Db.Query("SELECT id, item_no, qty FROM reserve_menu WHERE seq = ? ORDER BY id", id).Rows)
                {
                    DocLine x = new DocLine();
                    x.LineId = Db.I(l[0]); x.ItemNo = Db.I(l[1]); x.Qty = Db.D(l[2]);
                    d.Lines.Add(x);
                }
            }
            else
            {
                DataTable h = Db.Query("SELECT shop_no, menu_no, menu_date, custom_no, stor, [note], shop_type" + ProjCols + " FROM shop WHERE shop_no = ?", id);
                if (h.Rows.Count == 0) return null;
                DataRow r = h.Rows[0];
                d.No = Db.S(r["menu_no"]); d.Date = Db.Dt(r["menu_date"]); d.Person = Db.I(r["custom_no"]); d.Store = Db.I(r["stor"]); d.Note = Db.S(r["note"]);
                d.RType = Db.I(r["shop_type"]);
                if (Db.HasProj) d.Project = Db.I(r["proj_no"]);
                d.Kind = d.RType == 3 ? DocKind.Return : DocKind.Receipt;
                foreach (DataRow l in Db.Query("SELECT auto_seq, item_no, qty, price_dl, qty_sale FROM shop_menu WHERE seq = ? ORDER BY auto_seq", id).Rows)
                {
                    DocLine x = new DocLine();
                    x.LineId = Db.I(l[0]); x.ItemNo = Db.I(l[1]); x.Qty = Db.D(l[2]); x.Price = Db.D(l[3]); x.Used = Db.D(l[4]);
                    d.Lines.Add(x);
                }
            }
            d.Fingerprint = Fingerprint(null, d.Kind, id);
            return d;
        }

        /// <summary>A short text that changes whenever the document is changed by anyone.</summary>
        static string Fingerprint(DbTransaction tx, DocKind kind, int id)
        {
            StringBuilder sb = new StringBuilder();
            DataTable h, l;
            if (IsSale(kind))
            {
                h = Q(tx, "SELECT menu_no, menu_date, custom_no, stor, [note]" + ProjCols + " FROM sale WHERE shop_no = ?", id);
                l = Q(tx, "SELECT auto_seq, item_no, qty FROM sale_menu WHERE seq = ? ORDER BY auto_seq", id);
            }
            else if (kind == DocKind.Transfer)
            {
                h = Q(tx, "SELECT menu_no, menu_date, stor_out, stor_in, notes FROM trans WHERE shop_no = ?", id);
                l = Q(tx, "SELECT item_no, qty FROM trans_menu WHERE seq = ? ORDER BY item_no, qty", id);
            }
            else if (kind == DocKind.Reserve)
            {
                h = Q(tx, "SELECT menu_no, res_date, proj_no, stor, notes, status FROM reserve WHERE res_no = ?", id);
                l = Q(tx, "SELECT id, item_no, qty FROM reserve_menu WHERE seq = ? ORDER BY id", id);
            }
            else
            {
                h = Q(tx, "SELECT menu_no, menu_date, custom_no, stor, [note], shop_type" + ProjCols + " FROM shop WHERE shop_no = ?", id);
                l = Q(tx, "SELECT auto_seq, item_no, qty FROM shop_menu WHERE seq = ? ORDER BY auto_seq", id);
            }
            if (h.Rows.Count == 0) return "";
            foreach (object o in h.Rows[0].ItemArray) sb.Append(Str(o)).Append('|');
            foreach (DataRow r in l.Rows) { foreach (object o in r.ItemArray) sb.Append(Str(o)).Append(','); sb.Append(';'); }
            return sb.ToString();
        }

        static string Str(object o)
        {
            if (o == null || o is DBNull) return "";
            if (o is DateTime) return ((DateTime)o).ToString("yyyyMMdd", Inv);
            if (o is double || o is float || o is decimal) return Convert.ToDouble(o).ToString("0.######", Inv);
            return Convert.ToString(o, Inv);
        }

        static DataTable Q(DbTransaction tx, string sql, params object[] a) { return tx == null ? Db.Query(sql, a) : Db.Q(tx, sql, a); }

        static void CheckNotChanged(DbTransaction tx, Doc d)
        {
            string now = Fingerprint(tx, d.Kind, d.Id);
            if (now == "") throw new UserError("هذا السند حُذف من حاسوب آخر. لا يمكن حفظ التعديل.\nأعد فتح السند أو أنشئ سندا جديدا.");
            if (now != d.Fingerprint) throw new UserError("هذا السند تم تعديله من حاسوب آخر بعد أن فتحته.\nلحماية البيانات لم يتم حفظ تعديلك. أعد فتح السند لرؤية آخر نسخة ثم عدّله.");
        }

        /// <summary>Year-end closing: no document may be added, changed or deleted in a closed year
        /// (neither its new date nor, for an existing document, its stored date).</summary>
        static void CheckOpenPeriod(DbTransaction tx, Doc d, DateTime lim)
        {
            if (lim == DateTime.MinValue) return;
            if (d.Date.Date <= lim) throw PeriodLock.Error(d.Date);
            if (d.Id != 0)
            {
                object o = Db.S1(tx, "SELECT " + (d.Kind == DocKind.Reserve ? "res_date" : "menu_date") + " FROM " + Head(d.Kind) + " WHERE " + IdCol(d.Kind) + " = ?", d.Id);
                if (o is DateTime && ((DateTime)o).Date <= lim) throw PeriodLock.Error((DateTime)o);
            }
        }

        // ------------------------------------------------------------ numbering
        public static string NextNo(DocKind k, DbTransaction tx)
        {
            string sql = "SELECT MAX(" + Db.NumOf("menu_no") + ") FROM " + Head(k) + Lock;
            double max = tx == null ? Db.D(Db.Scalar(sql)) : Db.D(Db.S1(tx, sql));
            return ((long)max + 1).ToString(Inv);
        }

        static string NextShopMony(DbTransaction tx)
        {
            double max = Db.D(Db.S1(tx, "SELECT MAX(" + Db.NumOf("mony_no") + ") FROM shop" + Lock));
            return ((long)max + 1).ToString(Inv) + "C";
        }

        /// <summary>If two computers saved at the same moment and got the same number, renumber ours.</summary>
        static void EnsureUniqueNo(DbTransaction tx, Doc d)
        {
            string t = Head(d.Kind), idc = IdCol(d.Kind);
            for (int i = 0; i < 5; i++)
            {
                int n = Db.I(Db.S1(tx, "SELECT COUNT(*) FROM " + t + " WHERE menu_no = ?", d.No));
                if (n <= 1) return;
                d.No = NextNo(d.Kind, tx);
                if (IsSale(d.Kind)) Db.X(tx, "UPDATE sale SET menu_no = ?, mony_no = ? WHERE shop_no = ?", d.No, d.No + "C", d.Id);
                else Db.X(tx, "UPDATE " + t + " SET menu_no = ? WHERE " + idc + " = ?", d.No, d.Id);
            }
        }

        // ------------------------------------------------------------ stock
        static void Adjust(DbTransaction tx, int item, int stor, double delta)
        {
            if (Math.Abs(delta) < 1e-12) return;
            int n = Db.X(tx, "UPDATE item_stor SET item_bal = item_bal + ? WHERE item_no = ? AND stor = ?", delta, item, stor);
            if (n == 0)
                Db.X(tx, "INSERT INTO item_stor (item_no, stor, item_bal_f, item_bal, qty_f) VALUES (?, ?, 0, ?, 0)", item, stor, delta);
        }

        /// <summary>Links an issued quantity to the oldest receipts that still have quantity (FIFO),
        /// exactly like the original program does (tables sale_menu_1 and shop_menu.qty_sale).</summary>
        static void Fifo(DbTransaction tx, int saleLine, int saleId, int item, double qty)
        {
            if (qty <= 0) return;
            DataTable lots = Db.Q(tx,
                "SELECT l.auto_seq, l.qty - l.qty_sale AS av FROM shop_menu AS l INNER JOIN shop AS h ON h.shop_no = l.seq " +
                "WHERE l.item_no = ? AND l.qty - l.qty_sale > 0.0000001 ORDER BY h.menu_date, l.auto_seq", item);
            double rem = qty;
            foreach (DataRow r in lots.Rows)
            {
                double take = Math.Min(Db.D(r["av"]), rem);
                if (take <= 0) continue;
                int lot = Db.I(r["auto_seq"]);
                Db.X(tx, "INSERT INTO sale_menu_1 (seq_sale, seq_shop, qty, seq_sale1) VALUES (?, ?, ?, ?)", saleLine, lot, take, saleId);
                Db.X(tx, "UPDATE shop_menu SET qty_sale = qty_sale + ? WHERE auto_seq = ?", take, lot);
                rem -= take;
                if (rem <= 1e-9) break;
            }
        }

        static void ReverseIssue(DbTransaction tx, int saleId, int stor)
        {
            DataTable al = Db.Q(tx, "SELECT x.seq_shop, x.qty FROM sale_menu_1 AS x INNER JOIN sale_menu AS m ON x.seq_sale = m.auto_seq WHERE m.seq = ?", saleId);
            foreach (DataRow r in al.Rows)
            {
                int lot = Db.I(r["seq_shop"]);
                if (lot > 0) Db.X(tx, "UPDATE shop_menu SET qty_sale = qty_sale - ? WHERE auto_seq = ?", Db.D(r["qty"]), lot);
            }
            Db.X(tx, "DELETE FROM sale_menu_1 WHERE seq_sale IN (SELECT auto_seq FROM sale_menu WHERE seq = ?)", saleId);
            foreach (DataRow r in Db.Q(tx, "SELECT item_no, SUM(qty) AS q FROM sale_menu WHERE seq = ? GROUP BY item_no", saleId).Rows)
                Adjust(tx, Db.I(r["item_no"]), stor, Db.D(r["q"]));
            Db.X(tx, "DELETE FROM sale_menu WHERE seq = ?", saleId);
        }

        static void ReverseTransfer(DbTransaction tx, int id, int sOut, int sIn)
        {
            foreach (DataRow r in Db.Q(tx, "SELECT item_no, SUM(qty) AS q FROM trans_menu WHERE seq = ? GROUP BY item_no", id).Rows)
            {
                Adjust(tx, Db.I(r["item_no"]), sOut, Db.D(r["q"]));
                Adjust(tx, Db.I(r["item_no"]), sIn, -Db.D(r["q"]));
            }
            Db.X(tx, "DELETE FROM trans_menu WHERE seq = ?", id);
        }

        // ------------------------------------------------------------ save
        public static void Save(Doc d)
        {
            Validate(d);
            DateTime closed = PeriodLock.Limit();   // year closing / month lock, read before the transaction starts
            // if the save fails the transaction is rolled back; put back the ids it assigned so the
            // document on screen is exactly as before and can be saved again
            int oldId = d.Id, oldPerson = d.Person; string oldNo = d.No;
            int[] oldLines = d.Lines.Select(l => l.LineId).ToArray();
            bool newPerson = false;
            try
            {
                Db.InTx(delegate(DbTransaction tx)
                {
                    bool isNew = d.Id == 0;
                    if (!isNew) CheckNotChanged(tx, d);
                    if (!isNew) StockCount.CheckNotAdjustment(tx, d);
                    CheckOpenPeriod(tx, d, closed);
                    AuditTrail.DocSnap before = isNew ? null : AuditTrail.ReadDoc(tx, d.Kind, d.Id);
                    if (CcIsRecipient(d.Kind))
                    {
                        d.Person = CostCentres.PersonFor(d.Project, tx, out newPerson);
                        if (d.Person == 0) throw new UserError("مركز الكلفة المختار غير موجود (ربما حُذف من حاسوب آخر). اختر مركز الكلفة من جديد.");
                    }
                    if (IsSale(d.Kind)) SaveIssue(tx, d);
                    else if (d.Kind == DocKind.Transfer) SaveTransfer(tx, d);
                    else if (d.Kind == DocKind.Reserve) SaveReserve(tx, d);
                    else SaveReceipt(tx, d);
                    if (isNew) EnsureUniqueNo(tx, d);
                    AuditTrail.LogDoc(tx, (isNew ? "إضافة " : "تعديل ") + KindName(d.Kind), "رقم " + d.No + " - " + d.Lines.Count + " مادة" + (d.Project > 0 ? " - مركز الكلفة: " + Cache.Project(d.Project) : ""),
                                      before, isNew ? null : AuditTrail.ReadDoc(tx, d.Kind, d.Id));
                });
            }
            catch
            {
                d.Id = oldId; d.No = oldNo; d.Person = oldPerson;
                for (int i = 0; i < d.Lines.Count && i < oldLines.Length; i++) d.Lines[i].LineId = oldLines[i];
                throw;
            }
            if (newPerson) Cache.LoadPersons();
            Doc fresh = Load(d.Kind, d.Id);
            if (fresh != null) { d.Lines = fresh.Lines; d.Fingerprint = fresh.Fingerprint; }
        }

        /// <summary>Adds a new issue or receipt inside the caller's transaction, through the same code as
        /// Save (balances, FIFO, numbering). No validation or period check: the caller does them
        /// (used by the stock count adjustments, which have no recipient or project).</summary>
        internal static void SaveNewIn(DbTransaction tx, Doc d)
        {
            if (d.Id != 0 || d.Lines.Count == 0 || !(IsSale(d.Kind) || IsShop(d.Kind))) throw new InvalidOperationException("SaveNewIn: new issue or receipt with lines only");
            if (IsSale(d.Kind)) SaveIssue(tx, d); else SaveReceipt(tx, d);
            EnsureUniqueNo(tx, d);
        }

        static void Validate(Doc d)
        {
            if (d.Lines.Count == 0) throw new UserError("لا توجد مواد في السند.");
            if (d.Store == 0) throw new UserError("اختر المخزن.");
            switch (d.Kind)
            {
                case DocKind.Transfer:
                    if (d.Store2 == 0) throw new UserError("اختر المخزن المنقول إليه.");
                    if (d.Store2 == d.Store) throw new UserError("لا يمكن النقل إلى نفس المخزن.");
                    break;
                case DocKind.Reserve:
                    if (!Db.HasProj) throw new UserError("ميزة الحجز غير مفعّلة بعد (راجع الإعدادات).");
                    if (d.Project == 0) throw new UserError("اختر مركز الكلفة الذي تُحجز له المواد.");
                    break;
                case DocKind.Issue:
                    if (CcIsRecipient(d.Kind)) { if (d.Project == 0) throw new UserError("اختر مركز الكلفة (المستلم) الذي تُخرج له المواد."); }
                    else if (d.Person == 0) throw new UserError("اختر المستلم.");
                    break;
                case DocKind.Return:
                    if (CcIsRecipient(d.Kind)) { if (d.Project == 0) throw new UserError("اختر مركز الكلفة الذي أعاد المواد."); }
                    else if (d.Person == 0) throw new UserError("اختر الجهة التي أعادت المواد.");
                    break;
                case DocKind.PurchaseReturn:
                    if (d.Person == 0) throw new UserError("اختر المجهز الذي تُرجع إليه المواد.");
                    break;
                default:
                    if (d.Person == 0) throw new UserError("اختر المجهز / الجهة.");
                    break;
            }
            foreach (DocLine l in d.Lines)
            {
                if (l.ItemNo == 0) throw new UserError("يوجد سطر بدون مادة.");
                if (l.Qty <= 0) throw new UserError("الكمية يجب أن تكون أكبر من صفر: " + Cache.ItemName(l.ItemNo));
            }
        }

        static object ProjVal(Doc d) { return d.Project == 0 ? (object)DBNull.Value : d.Project; }

        static void SaveIssue(DbTransaction tx, Doc d)
        {
            int docKind = d.Kind == DocKind.PurchaseReturn ? 1 : 0;
            object proj = d.Kind == DocKind.PurchaseReturn ? DBNull.Value : ProjVal(d);
            if (d.Id > 0)
            {
                int oldStor = Db.I(Db.S1(tx, "SELECT stor FROM sale WHERE shop_no = ?", d.Id));
                ReverseIssue(tx, d.Id, oldStor);
                if (Db.HasProj)
                    Db.X(tx, "UPDATE sale SET menu_date = ?, custom_no = ?, stor = ?, [note] = ?, proj_no = ?, doc_kind = ? WHERE shop_no = ?",
                        d.Date.Date, d.Person, d.Store, Db.Null(d.Note), proj, docKind, d.Id);
                else
                    Db.X(tx, "UPDATE sale SET menu_date = ?, custom_no = ?, stor = ?, [note] = ? WHERE shop_no = ?",
                        d.Date.Date, d.Person, d.Store, Db.Null(d.Note), d.Id);
            }
            else
            {
                d.No = NextNo(d.Kind, tx);
                if (Db.HasProj)
                    d.Id = Db.Insert(tx,
                        "INSERT INTO sale (menu_no, menu_date, custom_no, pay_type, ksm_dl, wasl_dl, old_bal, ksm_per, stor, [note], mony_no, proj_no, doc_kind) " +
                        "VALUES (?, ?, ?, 1, 0, 0, 0, 0, ?, ?, ?, ?, ?)",
                        d.No, d.Date.Date, d.Person, d.Store, Db.Null(d.Note), d.No + "C", proj, docKind);
                else
                    d.Id = Db.Insert(tx,
                        "INSERT INTO sale (menu_no, menu_date, custom_no, pay_type, ksm_dl, wasl_dl, old_bal, ksm_per, stor, [note], mony_no) " +
                        "VALUES (?, ?, ?, 1, 0, 0, 0, 0, ?, ?, ?)",
                        d.No, d.Date.Date, d.Person, d.Store, Db.Null(d.Note), d.No + "C");
            }
            foreach (DocLine l in d.Lines)
            {
                l.LineId = Db.Insert(tx, "INSERT INTO sale_menu (seq, item_no, qty, price_dl) VALUES (?, ?, ?, ?)", d.Id, l.ItemNo, l.Qty, l.Price);
                Adjust(tx, l.ItemNo, d.Store, -l.Qty);
                Fifo(tx, l.LineId, d.Id, l.ItemNo, l.Qty);
            }
        }

        static void SaveReceipt(DbTransaction tx, Doc d)
        {
            int shopType = d.Kind == DocKind.Return ? 3 : (d.RType == 1 ? 1 : 2);
            object proj = d.Kind == DocKind.Return ? ProjVal(d) : DBNull.Value;
            if (d.Id > 0)
            {
                int oldStor = Db.I(Db.S1(tx, "SELECT stor FROM shop WHERE shop_no = ?", d.Id));
                DataTable old = Db.Q(tx, "SELECT auto_seq, item_no, qty, qty_sale FROM shop_menu WHERE seq = ?", d.Id);
                Dictionary<int, DocLine> keep = new Dictionary<int, DocLine>();
                foreach (DocLine l in d.Lines) if (l.LineId > 0) keep[l.LineId] = l;
                foreach (DataRow r in old.Rows)
                {
                    int lid = Db.I(r["auto_seq"]); int item = Db.I(r["item_no"]); double used = Db.D(r["qty_sale"]);
                    DocLine nl;
                    bool kept = keep.TryGetValue(lid, out nl);
                    if (used > 1e-9)
                    {
                        if (!kept) throw new UserError("لا يمكن حذف المادة \"" + Cache.ItemName(item) + "\" من السند لأنه أُخرج منها " + UI.Num(used) + ".");
                        if (nl.ItemNo != item) throw new UserError("لا يمكن تغيير المادة \"" + Cache.ItemName(item) + "\" لأنه أُخرج منها " + UI.Num(used) + ".");
                        if (nl.Qty < used - 1e-9) throw new UserError("كمية \"" + Cache.ItemName(item) + "\" لا يمكن أن تقل عن " + UI.Num(used) + " لأنها أُخرجت.");
                    }
                    Adjust(tx, item, oldStor, -Db.D(r["qty"]));
                    if (!kept) Db.X(tx, "DELETE FROM shop_menu WHERE auto_seq = ?", lid);
                }
                foreach (DocLine l in d.Lines)
                    if (l.LineId > 0)
                        Db.X(tx, "UPDATE shop_menu SET item_no = ?, qty = ?, price_dl = ?, cost = ? WHERE auto_seq = ?", l.ItemNo, l.Qty, l.Price, Valuation.LineCost(tx, d, l), l.LineId);
                if (Db.HasProj)
                    Db.X(tx, "UPDATE shop SET menu_date = ?, custom_no = ?, shop_type = ?, stor = ?, [note] = ?, proj_no = ? WHERE shop_no = ?",
                        d.Date.Date, d.Person, shopType, d.Store, Db.Null(d.Note), proj, d.Id);
                else
                    Db.X(tx, "UPDATE shop SET menu_date = ?, custom_no = ?, shop_type = ?, stor = ?, [note] = ? WHERE shop_no = ?",
                        d.Date.Date, d.Person, shopType, d.Store, Db.Null(d.Note), d.Id);
            }
            else
            {
                d.No = NextNo(d.Kind, tx);
                string mony = NextShopMony(tx);
                if (Db.HasProj)
                    d.Id = Db.Insert(tx,
                        "INSERT INTO shop (menu_no, menu_date, custom_no, pay_type, [note], mony_no, shop_type, stor, proj_no) VALUES (?, ?, ?, 1, ?, ?, ?, ?, ?)",
                        d.No, d.Date.Date, d.Person, Db.Null(d.Note), mony, shopType, d.Store, proj);
                else
                    d.Id = Db.Insert(tx,
                        "INSERT INTO shop (menu_no, menu_date, custom_no, pay_type, [note], mony_no, shop_type, stor) VALUES (?, ?, ?, 1, ?, ?, ?, ?)",
                        d.No, d.Date.Date, d.Person, Db.Null(d.Note), mony, shopType, d.Store);
            }
            foreach (DocLine l in d.Lines)
            {
                if (l.LineId == 0)
                    l.LineId = Db.Insert(tx, "INSERT INTO shop_menu (seq, item_no, qty, price_dl, cost, qty_sale, sale_pr) VALUES (?, ?, ?, ?, ?, 0, 0)",
                        d.Id, l.ItemNo, l.Qty, l.Price, Valuation.LineCost(tx, d, l));   // no price: a cost from the item's history (Valuation)
                Adjust(tx, l.ItemNo, d.Store, l.Qty);
            }
        }

        static void SaveTransfer(DbTransaction tx, Doc d)
        {
            if (d.Id > 0)
            {
                DataTable h = Db.Q(tx, "SELECT stor_out, stor_in FROM trans WHERE shop_no = ?", d.Id);
                ReverseTransfer(tx, d.Id, Db.I(h.Rows[0][0]), Db.I(h.Rows[0][1]));
                Db.X(tx, "UPDATE trans SET menu_date = ?, stor_out = ?, stor_in = ?, notes = ? WHERE shop_no = ?",
                    d.Date.Date, d.Store, d.Store2, Db.Null(d.Note), d.Id);
            }
            else
            {
                d.No = NextNo(DocKind.Transfer, tx);
                d.Id = Db.Insert(tx, "INSERT INTO trans (menu_no, menu_date, stor_out, stor_in, notes) VALUES (?, ?, ?, ?, ?)",
                    d.No, d.Date.Date, d.Store, d.Store2, Db.Null(d.Note));
            }
            foreach (DocLine l in d.Lines)
            {
                Db.X(tx, "INSERT INTO trans_menu (seq, item_no, qty) VALUES (?, ?, ?)", d.Id, l.ItemNo, l.Qty);
                Adjust(tx, l.ItemNo, d.Store, -l.Qty);
                Adjust(tx, l.ItemNo, d.Store2, l.Qty);
            }
        }

        static void SaveReserve(DbTransaction tx, Doc d)
        {
            string note = d.Note != null && d.Note.Length > 250 ? d.Note.Substring(0, 250) : d.Note;
            if (d.Id > 0)
            {
                Db.X(tx, "UPDATE reserve SET res_date = ?, proj_no = ?, stor = ?, notes = ? WHERE res_no = ?", d.Date.Date, d.Project, d.Store, Db.Null(note), d.Id);
                Db.X(tx, "DELETE FROM reserve_menu WHERE seq = ?", d.Id);
            }
            else
            {
                d.No = NextNo(DocKind.Reserve, tx);
                d.Id = Db.Insert(tx, "INSERT INTO reserve (menu_no, res_date, proj_no, stor, notes, status, user_name) VALUES (?, ?, ?, ?, ?, 1, ?)",
                    d.No, d.Date.Date, d.Project, d.Store, Db.Null(note), Session.UserName ?? "");
            }
            foreach (DocLine l in d.Lines)
                l.LineId = Db.Insert(tx, "INSERT INTO reserve_menu (seq, item_no, qty) VALUES (?, ?, ?)", d.Id, l.ItemNo, l.Qty);
        }

        // ------------------------------------------------------------ delete / cancel
        public static void Delete(Doc d)
        {
            DateTime closed = PeriodLock.Limit();   // year closing / month lock, read before the transaction starts
            Db.InTx(delegate(DbTransaction tx)
            {
                CheckNotChanged(tx, d);
                StockCount.CheckNotAdjustment(tx, d);
                CheckOpenPeriod(tx, d, closed);
                AuditTrail.DocSnap before = AuditTrail.ReadDoc(tx, d.Kind, d.Id);
                if (IsSale(d.Kind))
                {
                    int stor = Db.I(Db.S1(tx, "SELECT stor FROM sale WHERE shop_no = ?", d.Id));
                    ReverseIssue(tx, d.Id, stor);
                    Db.X(tx, "DELETE FROM sale WHERE shop_no = ?", d.Id);
                }
                else if (d.Kind == DocKind.Transfer)
                {
                    DataTable h = Db.Q(tx, "SELECT stor_out, stor_in FROM trans WHERE shop_no = ?", d.Id);
                    ReverseTransfer(tx, d.Id, Db.I(h.Rows[0][0]), Db.I(h.Rows[0][1]));
                    Db.X(tx, "DELETE FROM trans WHERE shop_no = ?", d.Id);
                }
                else if (d.Kind == DocKind.Reserve)
                {
                    // a reservation is never erased: it is cancelled and kept for the record
                    Db.X(tx, "UPDATE reserve SET status = 0 WHERE res_no = ?", d.Id);
                }
                else
                {
                    int stor = Db.I(Db.S1(tx, "SELECT stor FROM shop WHERE shop_no = ?", d.Id));
                    DataTable lines = Db.Q(tx, "SELECT item_no, qty, qty_sale FROM shop_menu WHERE seq = ?", d.Id);
                    List<string> used = new List<string>();
                    foreach (DataRow r in lines.Rows)
                        if (Db.D(r["qty_sale"]) > 1e-9) used.Add(Cache.ItemName(Db.I(r["item_no"])) + " (أُخرج منها " + UI.Num(Db.D(r["qty_sale"])) + ")");
                    if (used.Count > 0)
                        throw new UserError("لا يمكن حذف السند لأنه أُخرج من مواده:\n" + string.Join("\n", used.ToArray()));
                    foreach (DataRow r in lines.Rows) Adjust(tx, Db.I(r["item_no"]), stor, -Db.D(r["qty"]));
                    Db.X(tx, "DELETE FROM shop_menu WHERE seq = ?", d.Id);
                    Db.X(tx, "DELETE FROM shop WHERE shop_no = ?", d.Id);
                }
                AuditTrail.LogDoc(tx, (d.Kind == DocKind.Reserve ? "إلغاء " : "حذف ") + KindName(d.Kind), "رقم " + d.No, before,
                                  d.Kind == DocKind.Reserve ? AuditTrail.ReadDoc(tx, d.Kind, d.Id) : null);
            });
        }

        // ------------------------------------------------------------ navigation
        public static int Neighbor(DocKind k, int id, int dir)
        {
            string t = Head(k), c = IdCol(k);
            string sql;
            if (id == 0) sql = "SELECT " + (dir < 0 ? "MAX" : "MIN") + "(" + c + ") FROM " + t + " WHERE 1=1" + TypeCond(k);
            else sql = "SELECT " + (dir < 0 ? "MAX" : "MIN") + "(" + c + ") FROM " + t + " WHERE " + c + " " + (dir < 0 ? "<" : ">") + " ?" + TypeCond(k);
            return id == 0 ? Db.I(Db.Scalar(sql)) : Db.I(Db.Scalar(sql, id));
        }

        public static int FindByNo(DocKind k, string no)
        {
            return Db.I(Db.Scalar("SELECT MAX(" + IdCol(k) + ") FROM " + Head(k) + " WHERE menu_no = ?" + TypeCond(k), no.Trim()));
        }

        // ------------------------------------------------------------ maintenance
        /// <summary>Recomputes item_stor.item_bal = opening + all movements. Returns rows changed.</summary>
        public static int RecalcBalances()
        {
            int changed = 0;
            Db.InTx(delegate(DbTransaction tx)
            {
                Dictionary<string, double> mv = new Dictionary<string, double>();
                foreach (DataRow r in Db.Q(tx, "SELECT st, it, SUM(q) AS b FROM (" + Reports.MvSql("1=1") + ") AS m GROUP BY st, it").Rows)
                    mv[Db.I(r["st"]) + ":" + Db.I(r["it"])] = Db.D(r["b"]);
                HashSet<string> seen = new HashSet<string>();
                foreach (DataRow r in Db.Q(tx, "SELECT seq_no, item_no, stor, item_bal_f, item_bal FROM item_stor").Rows)
                {
                    string key = Db.I(r["stor"]) + ":" + Db.I(r["item_no"]);
                    seen.Add(key);
                    double m; mv.TryGetValue(key, out m);
                    double correct = Math.Round(Db.D(r["item_bal_f"]) + m, 6);
                    if (Math.Abs(correct - Db.D(r["item_bal"])) > 0.0001)
                    {
                        Db.X(tx, "UPDATE item_stor SET item_bal = ? WHERE seq_no = ?", correct, Db.I(r["seq_no"]));
                        changed++;
                    }
                }
                foreach (KeyValuePair<string, double> kv in mv)
                {
                    if (seen.Contains(kv.Key)) continue;
                    string[] p = kv.Key.Split(':');
                    Db.X(tx, "INSERT INTO item_stor (item_no, stor, item_bal_f, item_bal, qty_f) VALUES (?, ?, 0, ?, 0)", int.Parse(p[1]), int.Parse(p[0]), kv.Value);
                    changed++;
                }
                Db.Log(tx, "إعادة احتساب الأرصدة", changed + " سجل");
            });
            return changed;
        }
    }
}
