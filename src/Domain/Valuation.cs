using System.Collections.Generic;
using System.Data.Common;
using System.Data;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System;

namespace Stores
{
    /// <summary>
    /// Stock valuation to IAS 2 with the FIFO cost formula («تقييم المخزون»).
    ///
    /// Cost layers are the receipt lines the program already keeps: every shop_menu row (إدخال مخزني, مردود إخراج
    /// مخزني, تسوية جرد زيادة) is a lot with its quantity and unit cost (shop_menu.cost). Issues, purchase returns and
    /// count shortages take quantity out of the oldest lots (Posting.Fifo, per item over all stores).
    /// Under FIFO the quantity still on hand is always made of the NEWEST lots, so the value at the end of day D is:
    /// quantity on hand at D (opening balance + all movements up to D, exactly like the stock report), covered by
    /// the lots dated up to D from the newest backwards; what the lots do not cover is the opening balance
    /// (item_stor.item_bal_f, which has no lot), valued at the oldest known lot cost.
    /// This gives the same total as a perpetual FIFO run and does not depend on how old issues were linked.
    ///
    /// FIFO is kept per item over all stores (the program's lots are not per store), so a store's value is its
    /// quantity × the item's FIFO unit cost. A negative balance is valued at the last known cost.
    ///
    /// Lots without a cost (0): issue returns and receipts saved without a price. On saving, LineCost gives them
    /// a cost (a return: the cost it went out at; a receipt: the item's last known cost), and RepairAtStart fills
    /// the old ones once. Quantities and balances are never touched.
    /// </summary>
    static class Valuation
    {
        static readonly CultureInfo Inv = CultureInfo.InvariantCulture;
        const double Eps = 1e-9;

        public class ItemValue
        {
            public int Item;
            public double Qty;                 // all stores, end of the day
            public double Value;               // FIFO value of Qty
            public double UnknownQty;          // part of Qty that lies in lots without a cost (valued 0)
            public double LastCost;            // newest known lot cost up to the day (0 = never had a cost)
            public Dictionary<int, double> Stores = new Dictionary<int, double>();

            /// <summary>Average FIFO unit cost of the quantity on hand (the last known cost when nothing is on hand).</summary>
            public double UnitCost { get { return Qty > Eps ? Value / Qty : LastCost; } }

            /// <summary>Unit cost of the on-hand quantity whose cost is known (ignores lots without a cost).</summary>
            public double KnownUnitCost { get { double k = Qty - UnknownQty; return Qty > Eps && k > Eps ? Value / k : LastCost; } }

            public double StoreQty(int store)
            {
                if (store == 0) return Qty;
                double v; Stores.TryGetValue(store, out v); return v;
            }

            public double StoreValue(int store) { return store == 0 ? Value : StoreQty(store) * UnitCost; }
        }

        class Lot { public double Qty, Cost; }

        static DataTable Q(DbTransaction tx, string sql, params object[] a) { return tx == null ? Db.Query(sql, a) : Db.Q(tx, sql, a); }

        static object[] Rep(int times, params object[] a)
        {
            List<object> l = new List<object>();
            for (int i = 0; i < times; i++) l.AddRange(a);
            return l.ToArray();
        }

        // ------------------------------------------------------------ value at a date
        /// <summary>Quantity and FIFO value of every item at the end of <paramref name="day"/>.</summary>
        public static Dictionary<int, ItemValue> At(DateTime day) { return At(day, 0, null, 0); }

        /// <summary>Same, for one item (item 0 = all).</summary>
        public static ItemValue ItemAt(DateTime day, int item)
        {
            Dictionary<int, ItemValue> m = At(day, item, null, 0);
            ItemValue v;
            if (!m.TryGetValue(item, out v)) { v = new ItemValue(); v.Item = item; }
            return v;
        }

        /// <summary>Values from quantities already computed by the caller (item → store → quantity at the end of the day).</summary>
        public static Dictionary<int, ItemValue> At(DateTime day, Dictionary<int, Dictionary<int, double>> qty)
        {
            return Build(qty, Lots(day, 0, null, 0));
        }

        /// <summary>item 0 = all items. exceptShop: a receipt document (shop_no) left out completely (its lines are
        /// neither quantity nor lots), used while that document is being saved. tx: inside a transaction (or null).</summary>
        static Dictionary<int, ItemValue> At(DateTime day, int item, DbTransaction tx, int exceptShop)
        {
            DateTime dayEx = day.Date.AddDays(1);
            Dictionary<int, Dictionary<int, double>> qty = new Dictionary<int, Dictionary<int, double>>();
            Action<int, int, double> add = delegate(int it, int st, double v)
            {
                Dictionary<int, double> m;
                if (!qty.TryGetValue(it, out m)) { m = new Dictionary<int, double>(); qty[it] = m; }
                double o; m.TryGetValue(st, out o); m[st] = o + v;
            };
            DataTable open = item > 0 ? Q(tx, "SELECT item_no, stor, item_bal_f FROM item_stor WHERE item_bal_f <> 0 AND item_no = ?", item)
                                      : Q(tx, "SELECT item_no, stor, item_bal_f FROM item_stor WHERE item_bal_f <> 0");
            foreach (DataRow r in open.Rows) add(Db.I(r[0]), Db.I(r[1]), Db.D(r[2]));
            DataTable mv = item > 0
                ? Q(tx, "SELECT it, st, SUM(q) AS b FROM (" + Reports.MvSql("{D} < ? AND {I} = ?") + ") AS m GROUP BY it, st", Rep(4, dayEx, item))
                : Q(tx, "SELECT it, st, SUM(q) AS b FROM (" + Reports.MvSql("{D} < ?") + ") AS m GROUP BY it, st", Rep(4, dayEx));
            foreach (DataRow r in mv.Rows) add(Db.I(r["it"]), Db.I(r["st"]), Db.D(r["b"]));
            if (exceptShop > 0)
            {
                // the document being saved: take its current lines back out (only when dated up to the day)
                foreach (DataRow r in Q(tx, "SELECT l.item_no, h.stor, SUM(l.qty) AS q FROM shop AS h INNER JOIN shop_menu AS l ON h.shop_no = l.seq WHERE h.shop_no = ? AND h.menu_date < ? GROUP BY l.item_no, h.stor", exceptShop, dayEx).Rows)
                    if (item == 0 || Db.I(r[0]) == item) add(Db.I(r[0]), Db.I(r[1]), -Db.D(r[2]));
            }
            return Build(qty, Lots(day, item, tx, exceptShop));
        }

        /// <summary>Lots dated up to the day, per item, NEWEST first.</summary>
        static Dictionary<int, List<Lot>> Lots(DateTime day, int item, DbTransaction tx, int exceptShop)
        {
            DateTime dayEx = day.Date.AddDays(1);
            string sql = "SELECT l.item_no, l.qty, l.cost FROM shop AS h INNER JOIN shop_menu AS l ON h.shop_no = l.seq WHERE h.menu_date < ?";
            List<object> a = new List<object> { dayEx };
            if (item > 0) { sql += " AND l.item_no = ?"; a.Add(item); }
            if (exceptShop > 0) { sql += " AND h.shop_no <> ?"; a.Add(exceptShop); }
            sql += " ORDER BY l.item_no, h.menu_date DESC, l.auto_seq DESC";
            Dictionary<int, List<Lot>> lots = new Dictionary<int, List<Lot>>();
            foreach (DataRow r in Q(tx, sql, a.ToArray()).Rows)
            {
                int it = Db.I(r[0]);
                List<Lot> l; if (!lots.TryGetValue(it, out l)) { l = new List<Lot>(); lots[it] = l; }
                Lot x = new Lot(); x.Qty = Db.D(r[1]); x.Cost = Db.D(r[2]);
                l.Add(x);
            }
            return lots;
        }

        static Dictionary<int, ItemValue> Build(Dictionary<int, Dictionary<int, double>> qty, Dictionary<int, List<Lot>> lots)
        {
            Dictionary<int, ItemValue> res = new Dictionary<int, ItemValue>();
            foreach (KeyValuePair<int, Dictionary<int, double>> kv in qty)
            {
                ItemValue v = new ItemValue(); v.Item = kv.Key;
                foreach (KeyValuePair<int, double> s in kv.Value) { v.Stores[s.Key] = s.Value; v.Qty += s.Value; }
                List<Lot> l; lots.TryGetValue(kv.Key, out l);
                Value(v, l ?? new List<Lot>());
                res[kv.Key] = v;
            }
            // items with lots but no quantity at all: only their last cost is of interest
            foreach (KeyValuePair<int, List<Lot>> kv in lots)
            {
                if (res.ContainsKey(kv.Key)) continue;
                ItemValue v = new ItemValue(); v.Item = kv.Key;
                Value(v, kv.Value);
                res[kv.Key] = v;
            }
            return res;
        }

        /// <summary>FIFO: the quantity on hand is covered by the newest lots first.</summary>
        static void Value(ItemValue v, List<Lot> newestFirst)
        {
            double oldestCost = 0;
            foreach (Lot x in newestFirst)
                if (x.Cost > 0) { if (v.LastCost == 0) v.LastCost = x.Cost; oldestCost = x.Cost; }
            v.Qty = Math.Round(v.Qty, 6);
            if (v.Qty <= Eps)
            {
                // nothing (or less than nothing) on hand: a negative balance is valued at the last known cost
                v.Value = v.Qty < -Eps ? v.Qty * v.LastCost : 0;
                if (v.Qty < -Eps && v.LastCost <= 0) v.UnknownQty = v.Qty;
                return;
            }
            double need = v.Qty;
            foreach (Lot x in newestFirst)
            {
                if (x.Qty <= Eps) continue;
                double take = Math.Min(x.Qty, need);
                v.Value += take * x.Cost;
                if (x.Cost <= 0) v.UnknownQty += take;
                need -= take;
                if (need <= Eps) break;
            }
            if (need > Eps)
            {
                // older than every lot: the opening balance (it has no lot), at the oldest known cost
                v.Value += need * oldestCost;
                if (oldestCost <= 0) v.UnknownQty += need;
            }
            v.Value = Math.Round(v.Value, 6);
        }

        // ------------------------------------------------------------ cost of a new lot
        /// <summary>The unit cost a receipt line gets (shop_menu.cost). A price entered is the cost. Without a price:
        /// an issue return comes back at the cost it went out at to that cost centre (else the item's FIFO cost on
        /// that day, else its last known cost); a receipt (e.g. beginner mode, where the price is not asked) takes the
        /// item's last known cost. 0 when the item never had a cost. Inside the document's save transaction.</summary>
        public static double LineCost(DbTransaction tx, Doc d, DocLine l)
        {
            if (l.Price > 0) return l.Price;
            return GuessCost(tx, l.ItemNo, l.Qty, d.Date, d.Kind == DocKind.Return, d.Project, d.Person, d.Id, null);
        }

        static double GuessCost(DbTransaction tx, int item, double qty, DateTime date, bool isReturn, int proj, int person, int shopId, KnownCosts kc)
        {
            if (item == 0) return 0;
            double c = 0;
            if (isReturn)
            {
                c = IssuedCost(tx, item, qty, date, proj, person);
                if (c <= 0)
                {
                    ItemValue v;
                    if (At(date, item, tx, shopId).TryGetValue(item, out v) && v.Qty - v.UnknownQty > Eps) c = v.KnownUnitCost;
                }
            }
            if (c <= 0) c = kc != null ? kc.Last(item, date, shopId) : LastKnownCost(tx, item, date, shopId);
            return Math.Round(c, 4);
        }

        /// <summary>The cost at which the item went out to this cost centre (or recipient): the FIFO cost per unit of each
        /// issue line up to the date (as charged to the cost centre), newest issues first until the returned quantity is
        /// covered. Only links to lots with a known cost count. 0 = not traced.</summary>
        static double IssuedCost(DbTransaction tx, int item, double qty, DateTime date, int proj, int person)
        {
            string who; object whoVal;
            if (Db.HasProj && proj > 0) { who = "h.proj_no = ?"; whoVal = proj; }
            else if (person > 0) { who = "h.custom_no = ?"; whoVal = person; }
            else return 0;
            DataTable t = Q(tx, "SELECT m.auto_seq, h.menu_date, SUM(x.qty) AS q, SUM(x.qty * s.cost) AS v FROM ((sale AS h INNER JOIN sale_menu AS m ON h.shop_no = m.seq) INNER JOIN sale_menu_1 AS x ON x.seq_sale = m.auto_seq) " +
                                "INNER JOIN shop_menu AS s ON s.auto_seq = x.seq_shop WHERE m.item_no = ? AND " + who + " AND " + Db.IssueOnly("h") + " AND h.menu_date < ? AND s.cost > 0 " +
                                "GROUP BY m.auto_seq, h.menu_date ORDER BY h.menu_date DESC, m.auto_seq DESC", item, whoVal, date.Date.AddDays(1));
            double need = qty > Eps ? qty : 1, q = 0, val = 0;
            foreach (DataRow r in t.Rows)
            {
                double lq = Db.D(r["q"]);
                if (lq <= Eps) continue;
                double take = Math.Min(lq, need - q);
                q += take; val += take * Db.D(r["v"]) / lq;
                if (q >= need - Eps) break;
            }
            return q > Eps ? val / q : 0;
        }

        /// <summary>Newest known cost of the item on or before the date (a priced receipt first, then any lot with a
        /// cost); when it had none yet, the first one after the date. Lines of document exceptShop are left out.</summary>
        static double LastKnownCost(DbTransaction tx, int item, DateTime date, int exceptShop)
        {
            DateTime dayEx = date.Date.AddDays(1);
            string b = "SELECT TOP 1 l.cost FROM shop AS h INNER JOIN shop_menu AS l ON h.shop_no = l.seq WHERE l.item_no = ? AND l.cost > 0 AND h.shop_no <> ?";
            string rec = " AND h.shop_type <> 3 AND " + Db.NotCountAdj("h");
            string[] conds = {
                b + rec + " AND h.menu_date < ? ORDER BY h.menu_date DESC, l.auto_seq DESC",
                b + " AND h.menu_date < ? ORDER BY h.menu_date DESC, l.auto_seq DESC",
                b + rec + " AND h.menu_date >= ? ORDER BY h.menu_date, l.auto_seq",
                b + " AND h.menu_date >= ? ORDER BY h.menu_date, l.auto_seq" };
            foreach (string sql in conds)
            {
                DataTable t = Q(tx, sql, item, exceptShop, dayEx);
                if (t.Rows.Count > 0 && Db.D(t.Rows[0][0]) > 0) return Db.D(t.Rows[0][0]);
            }
            return 0;
        }

        /// <summary>The lots that have a cost, in memory: the same answer as LastKnownCost without a query per line
        /// (the repair looks at thousands of lines).</summary>
        class KnownCosts
        {
            class K { public DateTime D; public int Seq, Shop; public double Cost; public bool Rec; }
            readonly Dictionary<int, List<K>> m = new Dictionary<int, List<K>>();

            public KnownCosts(DbTransaction tx)
            {
                foreach (DataRow r in Db.Q(tx, "SELECT l.item_no, h.menu_date, l.auto_seq, h.shop_no, l.cost, h.shop_type" + (Db.HasCount ? ", h.doc_kind" : ", 0 AS doc_kind") +
                                               " FROM shop AS h INNER JOIN shop_menu AS l ON h.shop_no = l.seq WHERE l.cost > 0").Rows)
                    Add(Db.I(r[0]), Db.Dt(r[1]), Db.I(r[2]), Db.I(r[3]), Db.D(r[4]), Db.I(r["shop_type"]) != 3 && Db.I(r["doc_kind"]) != StockCount.AdjKind);
            }

            public bool Has(int item) { return m.ContainsKey(item); }

            public void Add(int item, DateTime d, int seq, int shop, double cost, bool rec)
            {
                List<K> l; if (!m.TryGetValue(item, out l)) { l = new List<K>(); m[item] = l; }
                K k = new K(); k.D = d.Date; k.Seq = seq; k.Shop = shop; k.Cost = cost; k.Rec = rec;
                l.Add(k);
            }

            public double Last(int item, DateTime date, int exceptShop)
            {
                List<K> l; if (!m.TryGetValue(item, out l)) return 0;
                DateTime day = date.Date;
                for (int pass = 0; pass < 4; pass++)
                {
                    bool before = pass < 2, recOnly = pass % 2 == 0;
                    K best = null;
                    foreach (K k in l)
                    {
                        if (k.Shop == exceptShop || (recOnly && !k.Rec) || (before ? k.D > day : k.D <= day)) continue;
                        if (best == null) { best = k; continue; }
                        int c = k.D == best.D ? k.Seq.CompareTo(best.Seq) : k.D.CompareTo(best.D);
                        if (before ? c > 0 : c < 0) best = k;
                    }
                    if (best != null) return best.Cost;
                }
                return 0;
            }
        }

        // ------------------------------------------------------------ repair of old lots without a cost
        public class RepairResult
        {
            public int Returns, Receipts, FromPrice, Left;
            public string LogFile;
            public int Total { get { return Returns + Receipts + FromPrice; } }
            public override string ToString()
            {
                return Total + " سطر: " + Returns + " مردود إخراج، " + Receipts + " إدخال بدون سعر، " + FromPrice + " من سعر السطر" +
                       (Left > 0 ? "، وبقي " + Left + " سطر لمواد بلا أي كلفة معروفة" : "");
            }
        }

        const string FlagKey = "lot_costs_repaired";   // highest shop_menu.auto_seq already looked at

        /// <summary>Gives a cost to receipt lines (lots) whose cost is 0, the same way a save does (LineCost). Only items
        /// that have a known cost somewhere can be filled; the others stay 0. Never changes a quantity. One transaction;
        /// every changed line is written to a text file next to the backups. fromSeq: only lines after this auto_seq.</summary>
        public static RepairResult RepairZeroCosts(int fromSeq)
        {
            RepairResult res = new RepairResult();
            List<object[]> report = new List<object[]>();
            DateTime locked = PeriodLock.Limit();   // closed years / locked months keep their costs (read before the transaction)
            Db.InTx(delegate(DbTransaction tx)
            {
                KnownCosts kc = new KnownCosts(tx);
                // updates are sent in groups of the same cost (one statement per ~50 lines is much faster on Jet);
                // they are written before a return is costed, because a return reads the lots' costs. The lines were read
                // as cost 0 in this same transaction («AND cost = 0» in the UPDATE makes Jet scan the whole table)
                Dictionary<double, List<int>> pending = new Dictionary<double, List<int>>();
                Action flush = delegate
                {
                    foreach (KeyValuePair<double, List<int>> kv in pending)
                        for (int i = 0; i < kv.Value.Count; i += 50)
                        {
                            List<int> part = kv.Value.GetRange(i, Math.Min(50, kv.Value.Count - i));
                            List<object> a = new List<object> { kv.Key };
                            foreach (int id in part) a.Add(id);
                            Db.X(tx, "UPDATE shop_menu SET cost = ? WHERE auto_seq IN (" + string.Join(", ", part.Select(x => "?").ToArray()) + ")", a.ToArray());
                        }
                    pending.Clear();
                };
                DataTable t = Db.Q(tx, "SELECT l.auto_seq, l.seq, l.item_no, l.qty, l.price_dl, h.menu_date, h.menu_no, h.shop_type, h.custom_no" + (Db.HasProj ? ", h.proj_no" : ", 0 AS proj_no") +
                                       (Db.HasCount ? ", h.doc_kind" : ", 0 AS doc_kind") +
                                       " FROM shop AS h INNER JOIN shop_menu AS l ON h.shop_no = l.seq WHERE l.cost = 0 AND l.auto_seq > ? ORDER BY h.menu_date, l.auto_seq", fromSeq);
                foreach (DataRow r in t.Rows)
                {
                    int it = Db.I(r["item_no"]);
                    if (locked != DateTime.MinValue && Db.Dt(r["menu_date"]).Date <= locked) { res.Left++; continue; }
                    bool ret = Db.I(r["shop_type"]) == 3;
                    double price = Db.D(r["price_dl"]), c;
                    if (price <= 0 && !kc.Has(it)) { res.Left++; continue; }   // the item never had a cost
                    if (price > 0) { c = price; res.FromPrice++; }
                    else
                    {
                        if (ret) flush();
                        c = GuessCost(tx, it, Db.D(r["qty"]), Db.Dt(r["menu_date"]), ret, Db.I(r["proj_no"]), Db.I(r["custom_no"]), Db.I(r["seq"]), kc);
                        if (c <= 0) { res.Left++; continue; }
                        if (ret) res.Returns++; else res.Receipts++;
                    }
                    List<int> ids; if (!pending.TryGetValue(c, out ids)) { ids = new List<int>(); pending[c] = ids; }
                    ids.Add(Db.I(r["auto_seq"]));
                    kc.Add(it, Db.Dt(r["menu_date"]), Db.I(r["auto_seq"]), Db.I(r["seq"]), c, !ret && Db.I(r["doc_kind"]) != StockCount.AdjKind);
                    report.Add(new object[] { Db.I(r["auto_seq"]), ret ? "مردود إخراج" : "إدخال", Db.S(r["menu_no"]), Db.Dt(r["menu_date"]), it, Db.D(r["qty"]), c });
                }
                flush();
                if (res.Total > 0) Db.Log(tx, "تعبئة كلفة الدفعات بدون كلفة", res.ToString());
            });
            if (report.Count > 0)
            {
                try
                {
                    Directory.CreateDirectory(Settings.BackupDir);
                    res.LogFile = Path.Combine(Settings.BackupDir, "تعبئة الكلفة " + DateTime.Now.ToString("yyyy-MM-dd HHmmss", Inv) + ".txt");
                    // the cache may not be loaded yet (program start): names straight from the item table
                    Dictionary<int, string> names = new Dictionary<int, string>();
                    foreach (DataRow r in Db.Query("SELECT item_no, item_code, item_name FROM item").Rows) names[Db.I(r[0])] = Db.S(r[1]) + "\t" + Db.S(r[2]);
                    StringBuilder sb = new StringBuilder("auto_seq\tالنوع\tرقم السند\tالتاريخ\tالرمز\tالمادة\tالكمية\tالكلفة الجديدة\r\n");
                    foreach (object[] o in report)
                    {
                        string nm; if (!names.TryGetValue((int)o[4], out nm)) nm = "\t" + o[4];
                        sb.Append(o[0]).Append('\t').Append(o[1]).Append('\t').Append(o[2]).Append('\t').Append(((DateTime)o[3]).ToString("yyyy-MM-dd", Inv)).Append('\t')
                          .Append(nm).Append('\t').Append(((double)o[5]).ToString("0.######", Inv)).Append('\t').Append(((double)o[6]).ToString("0.####", Inv)).Append("\r\n");
                    }
                    File.WriteAllText(res.LogFile, sb.ToString(), new UTF8Encoding(true));
                }
                catch { res.LogFile = null; }
            }
            return res;
        }

        /// <summary>Program start: the first time, a backup (Access) and then every old lot without a cost is filled;
        /// later starts look only at lines added since (e.g. by the old program). Never stops the program.</summary>
        public static RepairResult RepairAtStart()
        {
            try
            {
                string flag = Db.GetSetting(FlagKey, "");
                int from;
                bool first = !int.TryParse(flag, NumberStyles.Integer, Inv, out from);
                if (first) from = 0;
                int max = Db.I(Db.Scalar("SELECT MAX(auto_seq) FROM shop_menu"));
                if (!first && max <= from) return null;
                if (first && !Db.IsSql)
                {
                    int zero = Db.I(Db.Scalar("SELECT COUNT(*) FROM shop_menu WHERE cost = 0"));
                    if (zero > 0) Backup.Now("قبل تعبئة كلفة الدفعات");
                }
                RepairResult r = RepairZeroCosts(from);
                Db.SetSetting(FlagKey, max.ToString(Inv));
                return r;
            }
            catch (Exception ex)
            {
                UI.Warn("تعذر تعبئة كلفة الدفعات التي بدون كلفة (سيُعاد المحاولة عند التشغيل القادم):\n" + ex.Message);
                return null;
            }
        }
    }
}
