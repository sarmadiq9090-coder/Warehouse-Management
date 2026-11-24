using System.Collections.Generic;
using System.Data;
using System.Windows.Forms;
using System;

namespace Stores
{
    /// <summary>Lookup lists kept in memory (items, persons, stores ...). Refreshed regularly so
    /// that additions made on other computers appear automatically.</summary>
    static class Cache
    {
        public static DataTable Items, Persons, Stores, Types, Groups, Projects;
        public static Dictionary<int, string> ProjectName = new Dictionary<int, string>();
        public static Dictionary<string, int> ProjectByName = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        public static AutoCompleteStringCollection ProjectNames = new AutoCompleteStringCollection();
        public static readonly string[] ProjectStatus = { "", "فعّال", "مغلق" };
        public static Dictionary<int, DataRow> ItemById = new Dictionary<int, DataRow>();
        public static Dictionary<string, int> ItemByCode = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        public static Dictionary<string, int> ItemByName = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        public static Dictionary<int, string> PersonName = new Dictionary<int, string>();
        public static Dictionary<string, int> PersonByName = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        public static Dictionary<int, string> StoreName = new Dictionary<int, string>();
        public static Dictionary<int, string> TypeName = new Dictionary<int, string>();
        public static Dictionary<int, string> GroupName = new Dictionary<int, string>();
        public static AutoCompleteStringCollection ItemNames = new AutoCompleteStringCollection();
        public static AutoCompleteStringCollection PersonNames = new AutoCompleteStringCollection();
        public static int DefaultStore = 0;
        public static DateTime LoadedAt;

        public static readonly string[] PersonTypes = { "", "مستلم", "مجهز", "مستلم ومجهز", "متفرقة" };
        public static readonly string[] ReceiptTypes = { "", "استيراد", "محلي", "إعادة" };

        public static void LoadAll()
        {
            LoadLookups();
            LoadItems();
            LoadPersons();
            LoadProjects();
            LoadedAt = DateTime.Now;
        }

        public static void RefreshIfOld()
        {
            if ((DateTime.Now - LoadedAt).TotalMinutes >= 3)
            {
                try { LoadAll(); } catch { }
            }
        }

        public static void LoadLookups()
        {
            Stores = Db.Query("SELECT stor_no, stor_name FROM stor ORDER BY stor_no");
            Types = Db.Query("SELECT type_no, type_name FROM [type] ORDER BY type_name");
            Groups = Db.Query("SELECT group_no, group_name FROM [group] ORDER BY group_name");
            StoreName.Clear(); foreach (DataRow r in Stores.Rows) StoreName[Db.I(r[0])] = Db.S(r[1]);
            TypeName.Clear(); foreach (DataRow r in Types.Rows) TypeName[Db.I(r[0])] = Db.S(r[1]);
            GroupName.Clear(); foreach (DataRow r in Groups.Rows) GroupName[Db.I(r[0])] = Db.S(r[1]);
            if (DefaultStore == 0)
            {
                DefaultStore = Db.I(Db.Scalar("SELECT TOP 1 stor FROM sale GROUP BY stor ORDER BY COUNT(*) DESC"));
                if (DefaultStore == 0 && Stores.Rows.Count > 0) DefaultStore = Db.I(Stores.Rows[0][0]);
            }
        }

        public static void LoadItems()
        {
            DataTable t = Db.Query(
                "SELECT i.item_no, i.item_name, i.item_code, i.[type], i.[group], i.item_bal_ret, i.freez, i.tslsl, b.bal" + (Db.HasUnit ? ", i.unit " : " ") +
                "FROM item AS i LEFT JOIN (SELECT item_no, SUM(item_bal) AS bal FROM item_stor GROUP BY item_no) AS b ON i.item_no = b.item_no " +
                "ORDER BY i.item_name");
            if (!t.Columns.Contains("unit")) t.Columns.Add("unit", typeof(string));
            t.Columns.Add("type_name", typeof(string));
            t.Columns.Add("group_name", typeof(string));
            t.Columns.Add("_s", typeof(string));
            t.Columns["bal"].ReadOnly = false;
            Dictionary<int, DataRow> byId = new Dictionary<int, DataRow>();
            Dictionary<string, int> byCode = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            Dictionary<string, int> byName = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            List<string> names = new List<string>();
            foreach (DataRow r in t.Rows)
            {
                int id = Db.I(r["item_no"]);
                string tn; TypeName.TryGetValue(Db.I(r["type"]), out tn);
                string gn; GroupName.TryGetValue(Db.I(r["group"]), out gn);
                r["type_name"] = tn ?? "";
                r["group_name"] = gn ?? "";
                if (r["bal"] is DBNull) r["bal"] = 0.0;
                r["_s"] = UI.Norm(Db.S(r["item_name"]) + " " + Db.S(r["item_code"]));
                byId[id] = r;
                string code = Db.S(r["item_code"]).Trim();
                if (code.Length > 0 && !byCode.ContainsKey(code)) byCode[code] = id;
                string name = Db.S(r["item_name"]).Trim();
                if (name.Length > 0 && !byName.ContainsKey(name)) byName[name] = id;
                if (!Db.B(r["freez"])) names.Add(name);
            }
            t.AcceptChanges();
            Items = t; ItemById = byId; ItemByCode = byCode; ItemByName = byName;
            AutoCompleteStringCollection ac = new AutoCompleteStringCollection();
            ac.AddRange(names.ToArray());
            ItemNames = ac;
            LoadBins();
        }

        /// <summary>Shelf locations «الموقع» per (item, store), key = item * 100000 + store.</summary>
        public static Dictionary<long, string> Bins = new Dictionary<long, string>();

        public static void LoadBins()
        {
            Dictionary<long, string> d = new Dictionary<long, string>();
            if (Db.HasBin)
                foreach (DataRow r in Db.Query("SELECT item_no, stor, bin FROM item_stor WHERE bin IS NOT NULL").Rows)
                {
                    string b = Db.S(r[2]).Trim();
                    if (b.Length > 0) d[(long)Db.I(r[0]) * 100000 + Db.I(r[1])] = b;
                }
            Bins = d;
        }

        /// <summary>Shelf location of an item in a store ("" when not set). store 0 = the locations in all stores, e.g. «A-03، B-11».</summary>
        public static string Bin(int item, int store)
        {
            string s;
            if (store > 0) return Bins.TryGetValue((long)item * 100000 + store, out s) ? s : "";
            List<string> l = new List<string>();
            if (Stores != null)
                foreach (DataRow r in Stores.Rows)
                    if (Bins.TryGetValue((long)item * 100000 + Db.I(r[0]), out s) && !l.Contains(s)) l.Add(s);
            return string.Join("، ", l.ToArray());
        }

        public static void LoadPersons()
        {
            DataTable t = Db.Query("SELECT p_no, p_name, p_type, p_tel, p_addrs, p_note FROM person ORDER BY p_name");
            t.Columns.Add("type_name", typeof(string));
            t.Columns.Add("_s", typeof(string));
            Dictionary<int, string> byId = new Dictionary<int, string>();
            Dictionary<string, int> byName = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            List<string> names = new List<string>();
            foreach (DataRow r in t.Rows)
            {
                int id = Db.I(r["p_no"]);
                string name = Db.S(r["p_name"]).Trim();
                int pt = Db.I(r["p_type"]);
                r["type_name"] = pt > 0 && pt < PersonTypes.Length ? PersonTypes[pt] : "";
                r["_s"] = UI.Norm(name + " " + Db.S(r["p_tel"]) + " " + Db.S(r["p_note"]));
                byId[id] = name;
                if (!byName.ContainsKey(name)) byName[name] = id;
                names.Add(name);
            }
            t.AcceptChanges();
            Persons = t; PersonName = byId; PersonByName = byName;
            AutoCompleteStringCollection ac = new AutoCompleteStringCollection();
            ac.AddRange(names.ToArray());
            PersonNames = ac;
        }

        public static void LoadProjects()
        {
            // cost centres (مراكز الكلفة); cc_type NULL (old rows) = مشروع
            DataTable t = Db.HasProj ? Db.Query("SELECT proj_no, proj_code, proj_name, client, status, start_date, notes" + (Db.HasCcType ? ", cc_type" : "") + " FROM project ORDER BY proj_name")
                                     : new DataTable();
            if (!Db.HasProj) { t.Columns.Add("proj_no", typeof(int)); t.Columns.Add("proj_code"); t.Columns.Add("proj_name"); t.Columns.Add("client"); t.Columns.Add("status", typeof(int)); t.Columns.Add("start_date", typeof(DateTime)); t.Columns.Add("notes"); }
            if (!t.Columns.Contains("cc_type")) t.Columns.Add("cc_type", typeof(int));
            t.Columns["cc_type"].ReadOnly = false;
            t.Columns.Add("type_name", typeof(string));
            t.Columns.Add("status_name", typeof(string));
            t.Columns.Add("_s", typeof(string));
            Dictionary<int, string> byId = new Dictionary<int, string>();
            Dictionary<string, int> byName = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            List<string> names = new List<string>();
            foreach (DataRow r in t.Rows)
            {
                int id = Db.I(r["proj_no"]); string name = Db.S(r["proj_name"]).Trim();
                int st = Db.I(r["status"]);
                int ct = CostCentres.NormType(Db.I(r["cc_type"]));
                r["cc_type"] = ct; r["type_name"] = CostCentres.TypeName(ct);
                r["status_name"] = st == 2 ? ProjectStatus[2] : ProjectStatus[1];
                r["_s"] = UI.Norm(name + " " + Db.S(r["proj_code"]) + " " + Db.S(r["client"]) + " " + CostCentres.TypeName(ct));
                byId[id] = name;
                if (!byName.ContainsKey(name)) byName[name] = id;
                if (st != 2) names.Add(name);
            }
            t.AcceptChanges();
            Projects = t; ProjectName = byId; ProjectByName = byName;
            AutoCompleteStringCollection ac = new AutoCompleteStringCollection();
            ac.AddRange(names.ToArray());
            ProjectNames = ac;
        }

        public static string Project(int id) { string s; return ProjectName.TryGetValue(id, out s) ? s : (id == 0 ? "" : "#" + id); }

        public static int FindProject(string name)
        {
            name = (name ?? "").Trim();
            int id;
            if (name.Length == 0 || !Db.HasProj) return 0;
            if (ProjectByName.TryGetValue(name, out id)) return id;
            object o = Db.Scalar("SELECT proj_no FROM project WHERE proj_name = ? OR proj_code = ?", name, name);
            if (o != null && !(o is DBNull)) { LoadProjects(); return Db.I(o); }
            return 0;
        }

        public static string Person(int id) { string s; return PersonName.TryGetValue(id, out s) ? s : (id == 0 ? "" : "#" + id); }
        public static string Store(int id) { string s; return StoreName.TryGetValue(id, out s) ? s : (id == 0 ? "" : "#" + id); }
        public static string Type(int id) { string s; return TypeName.TryGetValue(id, out s) ? s : ""; }

        public static DataRow Item(int id) { DataRow r; return ItemById.TryGetValue(id, out r) ? r : null; }
        public static string ItemName(int id) { DataRow r = Item(id); return r == null ? "#" + id : Db.S(r["item_name"]); }
        public static string ItemCode(int id) { DataRow r = Item(id); return r == null ? "" : Db.S(r["item_code"]); }
        /// <summary>Unit of measure of an item (متر، لفة، قطعة ...), "" when not set.</summary>
        public static string ItemUnit(int id) { DataRow r = Item(id); return r == null ? "" : Db.S(r["unit"]).Trim(); }

        public static int FindItemByCode(string code)
        {
            code = UI.NormDigits(code ?? "").Trim();
            int id;
            if (code.Length == 0) return 0;
            if (ItemByCode.TryGetValue(code, out id)) return id;
            // maybe added on another computer
            object o = Db.Scalar("SELECT item_no FROM item WHERE item_code = ?", code);
            if (o != null && !(o is DBNull)) { LoadItems(); return Db.I(o); }
            return 0;
        }

        public static int FindItemByName(string name)
        {
            name = (name ?? "").Trim();
            int id;
            if (name.Length == 0) return 0;
            if (ItemByName.TryGetValue(name, out id)) return id;
            object o = Db.Scalar("SELECT item_no FROM item WHERE item_name = ?", name);
            if (o != null && !(o is DBNull)) { LoadItems(); return Db.I(o); }
            return 0;
        }

        public static int FindPerson(string name)
        {
            name = (name ?? "").Trim();
            int id;
            if (name.Length == 0) return 0;
            if (PersonByName.TryGetValue(name, out id)) return id;
            object o = Db.Scalar("SELECT p_no FROM person WHERE p_name = ?", name);
            if (o != null && !(o is DBNull)) { LoadPersons(); return Db.I(o); }
            return 0;
        }

        /// <summary>Current stored balance of an item in a store (live from the data file).</summary>
        public static double Stock(int item, int stor)
        {
            return Db.D(Db.Scalar("SELECT SUM(item_bal) FROM item_stor WHERE item_no = ? AND stor = ?", item, stor));
        }

        public static double StockAll(int item)
        {
            return Db.D(Db.Scalar("SELECT SUM(item_bal) FROM item_stor WHERE item_no = ?", item));
        }
    }
}
