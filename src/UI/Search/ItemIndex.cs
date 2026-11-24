using System.Collections.Generic;
using System.Data;
using System.Diagnostics;
using System.Drawing;
using System.Windows.Forms;
using System;

namespace Stores
{
    /// <summary>
    /// In-memory index of all items for instant search (like InstaFind): every keystroke searches
    /// all items by code or any part of the name in a few milliseconds, best matches first.
    /// </summary>
    static class ItemIndex
    {
        static DataTable built;
        static int[] ids;
        static string[] nName, nCode, name;
        static bool[] frozen;

        static void Ensure()
        {
            DataTable t = Cache.Items;
            if (built == t && ids != null) return;
            int n = t.Rows.Count;
            int[] a = new int[n]; string[] nn = new string[n], nc = new string[n], nm = new string[n]; bool[] fz = new bool[n];
            for (int i = 0; i < n; i++)
            {
                DataRow r = t.Rows[i];
                a[i] = Db.I(r["item_no"]);
                nm[i] = Db.S(r["item_name"]);
                nn[i] = UI.Norm(nm[i]);
                nc[i] = UI.Norm(Db.S(r["item_code"])).Trim();
                fz[i] = Db.B(r["freez"]);
            }
            ids = a; nName = nn; nCode = nc; name = nm; frozen = fz; built = t;
        }

        public static void Invalidate() { built = null; }

        /// <summary>Ranked search. Empty text returns all items (alphabetical).</summary>
        public static List<int> Search(string text, bool includeFrozen, int max, out int total)
        {
            Ensure();
            string q = UI.Norm(text ?? "").Trim();
            string[] tokens = q.Split(new char[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            List<KeyValuePair<long, int>> hits = new List<KeyValuePair<long, int>>();
            for (int i = 0; i < ids.Length; i++)
            {
                if (frozen[i] && !includeFrozen) continue;
                string nc = nCode[i], nn = nName[i];
                int score;
                if (tokens.Length == 0) score = 5;
                else
                {
                    bool all = true;
                    foreach (string t in tokens)
                        if (nn.IndexOf(t, StringComparison.Ordinal) < 0 && nc.IndexOf(t, StringComparison.Ordinal) < 0) { all = false; break; }
                    if (!all) continue;
                    if (nc == q) score = 0;
                    else if (nc.StartsWith(q, StringComparison.Ordinal)) score = 1;
                    else if (nn.StartsWith(q, StringComparison.Ordinal)) score = 2;
                    else if (nn.IndexOf(" " + tokens[0], StringComparison.Ordinal) >= 0) score = 3;
                    else if (nc.IndexOf(tokens[0], StringComparison.Ordinal) >= 0) score = 4;
                    else score = 5;
                }
                hits.Add(new KeyValuePair<long, int>((long)score * 1000 + Math.Min(999, nn.Length), i));
            }
            total = hits.Count;
            hits.Sort(delegate(KeyValuePair<long, int> x, KeyValuePair<long, int> y)
            {
                int c = x.Key.CompareTo(y.Key);
                return c != 0 ? c : string.CompareOrdinal(name[x.Value], name[y.Value]);
            });
            List<int> res = new List<int>(Math.Min(max, hits.Count));
            for (int i = 0; i < hits.Count && i < max; i++) res.Add(ids[hits[i].Value]);
            return res;
        }
    }
}
