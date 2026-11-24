using System.Collections.Generic;
using System.Data.Common;
using System.Data;
using System.Globalization;
using System.Linq;
using System.Text;
using System;

namespace Stores
{
    // ================================================================== reservations
    class ResRow
    {
        public int Proj, Stor, Item;
        public double Reserved, Used;
        public DateTime Since;
        public double Outstanding { get { return Math.Max(0, Reserved - Math.Max(0, Used)); } }
    }

    /// <summary>
    /// Outstanding reservation per (project, store, item) =
    ///   active reserved quantity − (net quantity issued to that project from that store since the reservation).
    /// Computed live, so editing or deleting issues automatically corrects the reservations.
    /// </summary>
    static class Reservations
    {
        public static List<ResRow> Get(int item, int proj)
        {
            List<ResRow> list = new List<ResRow>();
            if (!Db.HasProj) return list;
            List<object> a = new List<object>();
            string f = "";
            if (item > 0) { f += " AND l.item_no = ?"; a.Add(item); }
            if (proj > 0) { f += " AND h.proj_no = ?"; a.Add(proj); }
            DataTable r = Db.Query("SELECT h.proj_no, h.stor, l.item_no, SUM(l.qty) AS q, MIN(h.res_date) AS d FROM reserve AS h INNER JOIN reserve_menu AS l ON l.seq = h.res_no " +
                                   "WHERE h.status = 1" + f + " GROUP BY h.proj_no, h.stor, l.item_no", a.ToArray());
            if (r.Rows.Count == 0) return list;
            Dictionary<string, ResRow> map = new Dictionary<string, ResRow>();
            DateTime min = DateTime.MaxValue;
            foreach (DataRow x in r.Rows)
            {
                ResRow rr = new ResRow();
                rr.Proj = Db.I(x["proj_no"]); rr.Stor = Db.I(x["stor"]); rr.Item = Db.I(x["item_no"]); rr.Reserved = Db.D(x["q"]); rr.Since = Db.Dt(x["d"]);
                if (rr.Since < min) min = rr.Since;
                map[rr.Proj + ":" + rr.Stor + ":" + rr.Item] = rr;
                list.Add(rr);
            }
            List<object> b = new List<object> { min }; b.AddRange(a);
            List<object> b2 = new List<object>(b); b2.AddRange(new List<object> { min }); b2.AddRange(a);
            DataTable u = Db.Query(
                "SELECT h.proj_no, h.stor, l.item_no, h.menu_date, l.qty FROM sale AS h INNER JOIN sale_menu AS l ON h.shop_no = l.seq " +
                "WHERE h.proj_no IS NOT NULL AND " + Db.IssueOnly("h") + " AND h.menu_date >= ?" + f +
                " UNION ALL SELECT h.proj_no, h.stor, l.item_no, h.menu_date, -l.qty FROM shop AS h INNER JOIN shop_menu AS l ON h.shop_no = l.seq " +
                "WHERE h.proj_no IS NOT NULL AND h.shop_type = 3 AND h.menu_date >= ?" + f, b2.ToArray());
            foreach (DataRow x in u.Rows)
            {
                ResRow rr;
                if (!map.TryGetValue(Db.I(x[0]) + ":" + Db.I(x[1]) + ":" + Db.I(x[2]), out rr)) continue;
                if (Db.Dt(x[3]) >= rr.Since) rr.Used += Db.D(x[4]);
            }
            return list;
        }

        /// <summary>Quantity of an item in a store held for projects other than <paramref name="proj"/>.</summary>
        public static double ForOthers(int item, int stor, int proj)
        {
            double s = 0;
            foreach (ResRow r in Get(item, 0)) if (r.Stor == stor && r.Proj != proj) s += r.Outstanding;
            return s;
        }

        public static double ForProject(int item, int stor, int proj)
        {
            if (proj == 0) return 0;
            double s = 0;
            foreach (ResRow r in Get(item, proj)) if (r.Stor == stor) s += r.Outstanding;
            return s;
        }
    }
}
