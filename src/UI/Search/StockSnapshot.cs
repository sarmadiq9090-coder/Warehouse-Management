using System.Collections.Generic;
using System.Data;
using System.Diagnostics;
using System;

namespace Stores
{
    /// <summary>
    /// Balances and outstanding reservations of ONE store, read in two queries and kept in memory, so the
    /// item entry bar can show the available quantity of every search result on every keystroke without touching the data file.
    /// A document reloads it when its store changes, after saving and when a document is opened.
    /// </summary>
    class StockSnapshot
    {
        public readonly int Store;
        public readonly long LoadMs;
        readonly Dictionary<int, double> bal = new Dictionary<int, double>();
        readonly Dictionary<int, List<ResRow>> res = new Dictionary<int, List<ResRow>>();

        public StockSnapshot(int store)
        {
            Store = store;
            Stopwatch sw = Stopwatch.StartNew();
            if (store > 0)
            {
                foreach (DataRow r in Db.Query("SELECT item_no, SUM(item_bal) AS b FROM item_stor WHERE stor = ? GROUP BY item_no", store).Rows)
                    bal[Db.I(r[0])] = Db.D(r[1]);
                if (Db.HasProj)
                    foreach (ResRow r in Reservations.Get(0, 0))
                    {
                        if (r.Stor != store || r.Outstanding <= 0) continue;
                        List<ResRow> l;
                        if (!res.TryGetValue(r.Item, out l)) { l = new List<ResRow>(); res[r.Item] = l; }
                        l.Add(r);
                    }
            }
            LoadMs = sw.ElapsedMilliseconds;
        }

        /// <summary>Stored balance of the item in this store.</summary>
        public double Stock(int item) { double b; return bal.TryGetValue(item, out b) ? b : 0; }

        /// <summary>Same as Reservations.ForOthers for this store (proj = -1: every reservation counts).</summary>
        public double ForOthers(int item, int proj)
        {
            List<ResRow> l;
            if (!res.TryGetValue(item, out l)) return 0;
            double s = 0;
            foreach (ResRow r in l) if (r.Proj != proj) s += r.Outstanding;
            return s;
        }
    }
}
