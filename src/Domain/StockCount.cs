using System.Collections.Generic;
using System.Data.Common;
using System.Data;
using System.Globalization;
using System.Linq;
using System;

namespace Stores
{
    /// <summary>
    /// Physical stock count «الجرد الفعلي وتسوية الفروقات».
    ///  1. Create: a count of one store (optionally one item type) with the book balance of each item frozen.
    ///  2. Count: print a blind sheet (no book quantities), enter the counted quantities.
    ///  3. Post «ترحيل التسوية» (administrator / setup permission): the differences go through the normal
    ///     posting code (Posting.SaveNewIn: balances, FIFO, numbering) as
    ///       shortages → one issue    (table sale, doc_kind 2) — never an issue to a project or recipient
    ///       surpluses → one receipt  (table shop, shop_type 2 «محلي», doc_kind 2)
    ///     For the old Access program they are ordinary documents with the note «تسوية جرد فعلي رقم …».
    ///     The count is then locked and linked to the two documents; the documents cannot be edited or deleted.
    /// </summary>
    static class StockCount
    {
        /// <summary>Value of sale.doc_kind / shop.doc_kind for stock count adjustments.</summary>
        public const int AdjKind = 2;
        const double Eps = 0.0000001;
        static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

        /// <summary>Posting the adjustments: administrator or a user with the setup permission.</summary>
        public static bool CanPost { get { return Session.IsAdmin || Session.Can(Session.Setup); } }

        public const string NotReady = "ميزة الجرد الفعلي غير مفعّلة بعد: يجب إغلاق البرنامج القديم على كل الأجهزة ثم فتح هذا البرنامج مرة واحدة.";

        static void RequireTables()
        {
            if (!Db.HasCount) throw new UserError(NotReady + (Db.SchemaWarning != null ? "\n" + Db.SchemaWarning : ""));
        }

        public static string StatusName(int status) { return status == 1 ? "مُرحّل (مقفل)" : "مفتوح"; }

        // ------------------------------------------------------------ list / load
        public static DataTable List()
        {
            DataTable t = Db.Query(
                "SELECT h.count_no, h.menu_no, h.count_date, h.stor, h.type_no, h.notes, h.status, h.user_name, h.posted_at, h.posted_by, h.sale_id, h.shop_id, " +
                "(SELECT COUNT(*) FROM count_menu m WHERE m.seq = h.count_no) AS n, " +
                "(SELECT COUNT(*) FROM count_menu m WHERE m.seq = h.count_no AND m.count_qty IS NOT NULL) AS nc " +
                "FROM count_head AS h ORDER BY h.count_date DESC, h.count_no DESC");
            t.Columns.Add("sname", typeof(string)); t.Columns.Add("tname", typeof(string)); t.Columns.Add("status_name", typeof(string));
            foreach (DataRow r in t.Rows)
            {
                r["sname"] = Cache.Store(Db.I(r["stor"]));
                r["tname"] = Db.I(r["type_no"]) == 0 ? "كل الأنواع" : Cache.Type(Db.I(r["type_no"]));
                r["status_name"] = StatusName(Db.I(r["status"]));
            }
            return t;
        }

        public static CountDoc Load(int id)
        {
            DataTable h = Db.Query("SELECT count_no, menu_no, count_date, stor, type_no, notes, status, user_name, created, posted_at, posted_by, sale_id, shop_id FROM count_head WHERE count_no = ?", id);
            if (h.Rows.Count == 0) return null;
            DataRow r = h.Rows[0];
            CountDoc c = new CountDoc();
            c.Id = id; c.No = Db.S(r["menu_no"]); c.Date = Db.Dt(r["count_date"]); c.Store = Db.I(r["stor"]); c.TypeNo = Db.I(r["type_no"]);
            c.Note = Db.S(r["notes"]); c.Status = Db.I(r["status"]); c.User = Db.S(r["user_name"]); c.Created = Db.Dt(r["created"]);
            c.PostedAt = Db.Dt(r["posted_at"]); c.PostedBy = Db.S(r["posted_by"]); c.SaleId = Db.I(r["sale_id"]); c.ShopId = Db.I(r["shop_id"]);
            if (c.SaleId > 0) c.SaleNo = Db.S(Db.Scalar("SELECT menu_no FROM sale WHERE shop_no = ?", c.SaleId));
            if (c.ShopId > 0) c.ShopNo = Db.S(Db.Scalar("SELECT menu_no FROM shop WHERE shop_no = ?", c.ShopId));
            foreach (DataRow l in Db.Query("SELECT id, item_no, book_qty, count_qty, added FROM count_menu WHERE seq = ? ORDER BY id", id).Rows)
            {
                CountLine x = new CountLine();
                x.Id = Db.I(l["id"]); x.ItemNo = Db.I(l["item_no"]); x.Book = Db.D(l["book_qty"]); x.Added = Db.I(l["added"]) == 1;
                if (!(l["count_qty"] is DBNull)) x.Counted = Db.D(l["count_qty"]);
                c.Lines.Add(x);
            }
            return c;
        }

        static string NextNo(DbTransaction tx)
        {
            double max = Db.D(Db.S1(tx, "SELECT MAX(" + Db.NumOf("menu_no") + ") FROM count_head"));
            return ((long)max + 1).ToString(Inv);
        }

        static bool TypeOk(int item, int type)
        {
            if (type == 0) return true;
            DataRow ir = Cache.Item(item);
            return ir != null && Db.I(ir["type"]) == type;
        }

        static void CheckOpen(DbTransaction tx, int id)
        {
            object o = Db.S1(tx, "SELECT status FROM count_head WHERE count_no = ?", id);
            if (o == null) throw new UserError("هذا الجرد غير موجود (ربما حُذف من حاسوب آخر).");
            if (Db.I(o) == 1) throw new UserError("هذا الجرد مُرحّل ومقفل. لا يمكن تعديله.");
        }

        // ------------------------------------------------------------ create
        /// <summary>Creates a count of the store with the current book balance of each item (items with a
        /// non-zero balance; with includeZero also the other active items of the type). Returns its id.</summary>
        public static int Create(DateTime date, int store, int type, string note, bool includeZero)
        {
            RequireTables();
            if (store == 0) throw new UserError("اختر المخزن.");
            if (date.Date > DateTime.Today) throw new UserError("لا يمكن أن يكون تاريخ الجرد بعد اليوم.");
            DateTime closed = PeriodLock.Limit();     // year closing / month lock, read before the transaction starts
            if (closed != DateTime.MinValue && date.Date <= closed) throw PeriodLock.Error(date);
            if (note != null && note.Length > 250) note = note.Substring(0, 250);
            int id = 0;
            Db.InTx(delegate(DbTransaction tx)
            {
                string no = NextNo(tx);
                id = Db.Insert(tx, "INSERT INTO count_head (menu_no, count_date, stor, type_no, notes, status, user_name, created) VALUES (?, ?, ?, ?, ?, 0, ?, ?)",
                    no, date.Date, store, type == 0 ? (object)DBNull.Value : type, Db.Null(note), Session.UserName ?? "", Db.Now());
                Dictionary<int, double> bal = new Dictionary<int, double>();
                foreach (DataRow r in Db.Q(tx, "SELECT item_no, SUM(item_bal) AS b FROM item_stor WHERE stor = ? GROUP BY item_no", store).Rows)
                    bal[Db.I(r["item_no"])] = Math.Round(Db.D(r["b"]), 6);
                List<int> items = new List<int>();
                foreach (DataRow ir in Cache.Items.Select("", "item_name"))
                {
                    int it = Db.I(ir["item_no"]);
                    double b; bal.TryGetValue(it, out b);
                    if (!TypeOk(it, type)) continue;
                    if (Math.Abs(b) > Eps || (includeZero && !Db.B(ir["freez"]))) items.Add(it);
                }
                foreach (int it in items)
                {
                    double b; bal.TryGetValue(it, out b);
                    Db.X(tx, "INSERT INTO count_menu (seq, item_no, book_qty, count_qty, added) VALUES (?, ?, ?, NULL, 0)", id, it, b);
                }
                Db.Log(tx, "إنشاء جرد فعلي", "رقم " + no + " - " + Cache.Store(store) + (type > 0 ? " - " + Cache.Type(type) : "") + " - " + items.Count + " مادة");
            });
            return id;
        }

        // ------------------------------------------------------------ counted quantities
        /// <summary>Saves the changed counted quantities, the added and the removed lines.
        /// A line added by hand gets the item's current balance in the store as its book balance.</summary>
        public static void SaveCounts(CountDoc c)
        {
            RequireTables();
            int[] oldIds = c.Lines.Select(l => l.Id).ToArray();
            double[] oldBook = c.Lines.Select(l => l.Book).ToArray();
            int changed = 0;
            try
            {
                Db.InTx(delegate(DbTransaction tx)
                {
                    CheckOpen(tx, c.Id);
                    foreach (int lid in c.Removed) Db.X(tx, "DELETE FROM count_menu WHERE id = ? AND seq = ? AND added = 1", lid, c.Id);
                    foreach (CountLine l in c.Lines)
                    {
                        object q = l.Counted.HasValue ? (object)l.Counted.Value : DBNull.Value;
                        if (l.Id == 0)
                        {
                            int ex = Db.I(Db.S1(tx, "SELECT MAX(id) FROM count_menu WHERE seq = ? AND item_no = ?", c.Id, l.ItemNo));
                            if (ex > 0) { Db.X(tx, "UPDATE count_menu SET count_qty = ? WHERE id = ?", q, ex); l.Id = ex; }
                            else
                            {
                                l.Book = Math.Round(Db.D(Db.S1(tx, "SELECT SUM(item_bal) FROM item_stor WHERE item_no = ? AND stor = ?", l.ItemNo, c.Store)), 6);
                                l.Id = Db.Insert(tx, "INSERT INTO count_menu (seq, item_no, book_qty, count_qty, added) VALUES (?, ?, ?, ?, 1)", c.Id, l.ItemNo, l.Book, q);
                            }
                            changed++;
                        }
                        else if (l.Dirty) { Db.X(tx, "UPDATE count_menu SET count_qty = ? WHERE id = ? AND seq = ?", q, l.Id, c.Id); changed++; }
                    }
                    Db.Log(tx, "حفظ كميات الجرد", "جرد رقم " + c.No + " - " + changed + " سطر" + (c.Removed.Count > 0 ? " - حذف " + c.Removed.Count : ""));
                });
            }
            catch
            {
                for (int i = 0; i < c.Lines.Count && i < oldIds.Length; i++) { c.Lines[i].Id = oldIds[i]; c.Lines[i].Book = oldBook[i]; }
                throw;
            }
            foreach (CountLine l in c.Lines) l.Dirty = false;
            c.Removed.Clear();
        }

        /// <summary>Deletes an open (not posted) count.</summary>
        public static void Delete(int id)
        {
            RequireTables();
            Db.InTx(delegate(DbTransaction tx)
            {
                CheckOpen(tx, id);
                string no = Db.S(Db.S1(tx, "SELECT menu_no FROM count_head WHERE count_no = ?", id));
                Db.X(tx, "DELETE FROM count_menu WHERE seq = ?", id);
                Db.X(tx, "DELETE FROM count_head WHERE count_no = ?", id);
                Db.Log(tx, "حذف جرد فعلي", "رقم " + no);
            });
        }

        /// <summary>Counted lines whose current store balance is no longer the frozen book balance
        /// (documents were saved after the count was created).</summary>
        public static int ChangedSinceCreated(CountDoc c)
        {
            Dictionary<int, double> bal = new Dictionary<int, double>();
            foreach (DataRow r in Db.Query("SELECT item_no, SUM(item_bal) AS b FROM item_stor WHERE stor = ? GROUP BY item_no", c.Store).Rows)
                bal[Db.I(r["item_no"])] = Db.D(r["b"]);
            int n = 0;
            foreach (CountLine l in c.Lines)
            {
                if (l.Id == 0 || !l.Counted.HasValue) continue;
                double b; bal.TryGetValue(l.ItemNo, out b);
                if (Math.Abs(b - l.Book) > 0.0001) n++;
            }
            return n;
        }

        // ------------------------------------------------------------ post
        static double LastCost(DbTransaction tx, int item)
        {
            return Db.D(Db.S1(tx, "SELECT TOP 1 l.cost FROM shop_menu AS l INNER JOIN shop AS h ON h.shop_no = l.seq WHERE l.item_no = ? AND l.cost > 0 ORDER BY h.menu_date DESC, l.auto_seq DESC", item));
        }

        /// <summary>«ترحيل التسوية»: posts the differences of the counted items (not counted items are left as they are),
        /// locks the count and returns it reloaded. Refused for a posted count and for a count dated in a closed year.</summary>
        public static CountDoc Post(int id)
        {
            RequireTables();
            if (!CanPost) throw new UserError("ترحيل تسوية الجرد للمدير أو لمن لديه صلاحية الإعدادات فقط.");
            DateTime closed = PeriodLock.Limit();     // year closing / month lock, read before the transaction starts
            Db.InTx(delegate(DbTransaction tx)
            {
                DataTable h = Db.Q(tx, "SELECT menu_no, count_date, stor, status FROM count_head WHERE count_no = ?", id);
                if (h.Rows.Count == 0) throw new UserError("هذا الجرد غير موجود (ربما حُذف من حاسوب آخر).");
                if (Db.I(h.Rows[0]["status"]) == 1) throw new UserError("هذا الجرد مُرحّل مسبقا. لا يمكن ترحيله مرة ثانية.");
                string no = Db.S(h.Rows[0]["menu_no"]);
                DateTime date = Db.Dt(h.Rows[0]["count_date"]).Date;
                int store = Db.I(h.Rows[0]["stor"]);
                if (closed != DateTime.MinValue && date <= closed) throw PeriodLock.Error(date);

                string note = "تسوية جرد فعلي رقم " + no;
                Doc shortage = new Doc(); shortage.Kind = DocKind.Issue; shortage.Date = date; shortage.Store = store; shortage.Note = note;
                Doc surplus = new Doc(); surplus.Kind = DocKind.Receipt; surplus.RType = 2; surplus.Date = date; surplus.Store = store; surplus.Note = note;
                double qs = 0, qp = 0;
                foreach (DataRow r in Db.Q(tx, "SELECT item_no, book_qty, count_qty FROM count_menu WHERE seq = ? AND count_qty IS NOT NULL ORDER BY id", id).Rows)
                {
                    int it = Db.I(r["item_no"]);
                    double diff = Math.Round(Db.D(r["count_qty"]) - Db.D(r["book_qty"]), 6);
                    DocLine l = new DocLine(); l.ItemNo = it;
                    if (diff < -Eps) { l.Qty = -diff; shortage.Lines.Add(l); qs += -diff; }
                    else if (diff > Eps) { l.Qty = diff; l.Price = LastCost(tx, it); surplus.Lines.Add(l); qp += diff; }
                }
                object saleId = DBNull.Value, shopId = DBNull.Value;
                if (surplus.Lines.Count > 0)
                {
                    Posting.SaveNewIn(tx, surplus);
                    Db.X(tx, "UPDATE shop SET doc_kind = ? WHERE shop_no = ?", AdjKind, surplus.Id);
                    shopId = surplus.Id;
                }
                if (shortage.Lines.Count > 0)
                {
                    Posting.SaveNewIn(tx, shortage);
                    Db.X(tx, "UPDATE sale SET doc_kind = ?, proj_no = NULL WHERE shop_no = ?", AdjKind, shortage.Id);
                    saleId = shortage.Id;
                }
                Db.X(tx, "UPDATE count_head SET status = 1, posted_at = ?, posted_by = ?, sale_id = ?, shop_id = ? WHERE count_no = ?",
                    Db.Now(), Session.UserName ?? "", saleId, shopId, id);
                Db.Log(tx, "ترحيل تسوية جرد", "جرد رقم " + no + " - " + Cache.Store(store) +
                    " - نقص " + shortage.Lines.Count + " مادة (" + UI.Num(qs) + ")" + (shortage.Id > 0 ? " إخراج رقم " + shortage.No : "") +
                    " - زيادة " + surplus.Lines.Count + " مادة (" + UI.Num(qp) + ")" + (surplus.Id > 0 ? " إدخال رقم " + surplus.No : ""));
            });
            return Load(id);
        }

        /// <summary>DocView: a stock-count adjustment or a posted write-off is shown read-only. Returns the reason, or null.</summary>
        public static string AdjustmentText(Doc d)
        {
            if (!Db.HasCount || d == null || d.Id == 0) return null;
            string t = Posting.IsSale(d.Kind) ? "sale" : Posting.IsShop(d.Kind) ? "shop" : null;
            if (t == null) return null;
            int k;
            try { k = Db.I(Db.Scalar("SELECT doc_kind FROM " + t + " WHERE shop_no = ?", d.Id)); } catch { return null; }
            if (k == AdjKind) return "تسوية جرد فعلي";
            if (k == WriteOffs.SaleKind && t == "sale") return "ترحيل سند إتلاف";
            return null;
        }

        /// <summary>Posting / DocView guard: the adjustment documents of a count cannot be changed or deleted.</summary>
        internal static void CheckNotAdjustment(DbTransaction tx, Doc d)
        {
            if (!Db.HasCount || d.Id == 0) return;
            string t = Posting.IsSale(d.Kind) ? "sale" : Posting.IsShop(d.Kind) ? "shop" : null;
            if (t == null) return;
            int k = Db.I(Db.S1(tx, "SELECT doc_kind FROM " + t + " WHERE shop_no = ?", d.Id));
            if (k == AdjKind)
                throw new UserError("هذا السند تسوية جرد فعلي (أُنشئ عند ترحيل الجرد) ولا يمكن تعديله أو حذفه.");
            if (k == WriteOffs.SaleKind && t == "sale")
                throw new UserError("هذا السند ترحيل لسند إتلاف / شطب مواد معتمد ولا يمكن تعديله أو حذفه.");
        }
    }
}
