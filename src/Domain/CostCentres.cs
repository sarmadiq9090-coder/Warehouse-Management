using System.Collections.Generic;
using System.Data.Common;
using System.Data;
using System.Linq;
using System;

namespace Stores
{
    /// <summary>
    /// Cost centres («مراكز الكلفة»). Stored in the table project (proj_no is the cost centre number everywhere:
    /// sale.proj_no, shop.proj_no, reserve.proj_no). project.cc_type says what kind of cost centre it is;
    /// a project is one kind among others. NULL cc_type (rows from before the column existed) = مشروع.
    /// </summary>
    static class CostCentres
    {
        public const int Project = 1, Department = 2, Maintenance = 3, Admin = 4, Other = 5;
        public static readonly string[] TypeNames = { "", "مشروع", "قسم", "صيانة", "إدارة", "أخرى" };
        static readonly string[] CodePrefix = { "", "P", "D", "M", "A", "O" };

        public static int NormType(int t) { return t >= 1 && t < TypeNames.Length ? t : Project; }
        public static string TypeName(int t) { return TypeNames[NormType(t)]; }

        public static List<ComboItem> TypeItems()
        {
            List<ComboItem> l = new List<ComboItem>();
            for (int i = 1; i < TypeNames.Length; i++) l.Add(new ComboItem(i, TypeNames[i]));
            return l;
        }

        /// <summary>Type of a cost centre from the cache (1 = مشروع when unknown).</summary>
        public static int TypeOf(int id)
        {
            DataRow r = Row(id);
            return r == null ? Project : NormType(Db.I(r["cc_type"]));
        }

        public static DataRow Row(int id)
        {
            if (Cache.Projects == null) return null;
            return Cache.Projects.Select("proj_no = " + id).FirstOrDefault();
        }

        /// <summary>Next free automatic code for a type: P-001, D-001, M-001, A-001, O-001…</summary>
        public static string NextCode(int type)
        {
            string pre = CodePrefix[NormType(type)] + "-";
            int max = 0;
            if (Db.HasProj)
                foreach (DataRow r in Db.Query("SELECT proj_code FROM project WHERE proj_code IS NOT NULL").Rows)
                {
                    string c = Db.S(r[0]).Trim();
                    int n;
                    if (c.StartsWith(pre, StringComparison.OrdinalIgnoreCase) && int.TryParse(c.Substring(pre.Length), out n) && n > max) max = n;
                }
            return pre + (max + 1).ToString("000");
        }

        /// <summary>Adds (id = 0) or changes a cost centre. Returns its number. Throws UserError on bad input.
        /// Client and start date belong to projects only: other types store NULL.</summary>
        public static int Save(int id, string name, string code, int type, int status, string client, DateTime? start, string notes)
        {
            if (!Db.HasProj) throw new UserError("ميزة مراكز الكلفة غير مفعّلة بعد (راجع الإعدادات).");
            name = (name ?? "").Trim(); code = (code ?? "").Trim();
            type = NormType(type);
            if (status != 2) status = 1;
            if (name.Length == 0) throw new UserError("أدخل اسم مركز الكلفة.");
            if (name.Length > 100) name = name.Substring(0, 100);
            if (Db.I(Db.Scalar("SELECT COUNT(*) FROM project WHERE proj_name = ? AND proj_no <> ?", name, id)) > 0)
                throw new UserError("يوجد مركز كلفة بنفس الاسم.");
            if (code.Length == 0) code = id == 0 ? NextCode(type) : "";
            if (code.Length > 30) code = code.Substring(0, 30);
            if (code.Length > 0 && Db.I(Db.Scalar("SELECT COUNT(*) FROM project WHERE proj_code = ? AND proj_no <> ?", code, id)) > 0)
                throw new UserError("الرمز «" + code + "» مستعمل لمركز كلفة آخر.");
            bool isProj = type == Project;
            object oc = Db.Null(code), cl = isProj ? Db.Null((client ?? "").Trim()) : DBNull.Value, nt = Db.Null((notes ?? "").Trim());
            object sd = isProj && start.HasValue ? (object)start.Value.Date : DBNull.Value;
            bool hasType = Db.HasCcType;
            int result = id;
            Db.InTx(delegate(DbTransaction tx)
            {
                if (id == 0)
                {
                    result = hasType
                        ? Db.Insert(tx, "INSERT INTO project (proj_code, proj_name, client, status, start_date, notes, cc_type) VALUES (?, ?, ?, ?, ?, ?, ?)", oc, name, cl, status, sd, nt, type)
                        : Db.Insert(tx, "INSERT INTO project (proj_code, proj_name, client, status, start_date, notes) VALUES (?, ?, ?, ?, ?, ?)", oc, name, cl, status, sd, nt);
                    Db.Log(tx, "إضافة مركز كلفة", TypeName(type) + ": " + name);
                }
                else
                {
                    List<KeyValuePair<string, string>> before = AuditTrail.CostCentreSnap(tx, id);
                    if (hasType) Db.X(tx, "UPDATE project SET proj_code = ?, proj_name = ?, client = ?, status = ?, start_date = ?, notes = ?, cc_type = ? WHERE proj_no = ?", oc, name, cl, status, sd, nt, type, id);
                    else Db.X(tx, "UPDATE project SET proj_code = ?, proj_name = ?, client = ?, status = ?, start_date = ?, notes = ? WHERE proj_no = ?", oc, name, cl, status, sd, nt, id);
                    // the linked person carries the same name, so the old program shows the new name too
                    int pn = Db.HasCcPerson ? Db.I(Db.S1(tx, "SELECT p_no FROM project WHERE proj_no = ?", id)) : 0;
                    if (pn > 0) Db.X(tx, "UPDATE person SET p_name = ? WHERE p_no = ?", PersonName(name), pn);
                    AuditTrail.Changes(tx, AuditTrail.Log(tx, "تعديل مركز كلفة", TypeName(type) + ": " + name), "project", name, before, AuditTrail.CostCentreSnap(tx, id));
                }
            });
            Cache.LoadProjects();
            if (id > 0 && Db.HasCcPerson) Cache.LoadPersons();
            return result;
        }

        // ---------------------------------------------------------------- cost centre = recipient
        // Owner's decision «المستلم = مركز الكلفة»: in issues and issue returns the cost centre IS the recipient.
        // The original tables only know sale.custom_no / shop.custom_no (a row in table person), so every cost
        // centre is linked to one person row (project.p_no) that carries its name; saving an issue stores that
        // person in custom_no and the old program keeps showing the recipient.

        static string PersonName(string name)
        {
            name = (name ?? "").Trim();
            return name.Length > 70 ? name.Substring(0, 70) : name;
        }

        /// <summary>The person row that stands for this cost centre (project.p_no). When it has none yet, a person
        /// with exactly the same name is linked, otherwise one is created (inside the caller's transaction).
        /// Returns 0 when the cost centre does not exist. created = a person row was added (reload Cache.Persons).</summary>
        public static int PersonFor(int projNo, DbTransaction tx, out bool created)
        {
            created = false;
            if (projNo <= 0 || !Db.HasCcPerson) return 0;
            DataTable t = Db.Q(tx, "SELECT proj_name, p_no FROM project WHERE proj_no = ?", projNo);
            if (t.Rows.Count == 0) return 0;
            int p = Db.I(t.Rows[0]["p_no"]);
            if (p > 0 && Db.I(Db.S1(tx, "SELECT COUNT(*) FROM person WHERE p_no = ?", p)) > 0) return p;
            string name = PersonName(Db.S(t.Rows[0]["proj_name"]));
            p = Db.I(Db.S1(tx, "SELECT MIN(p_no) FROM person WHERE p_name = ?", name));
            if (p == 0)
            {
                p = NewPerson(tx, name);
                created = true;
            }
            Db.X(tx, "UPDATE project SET p_no = ? WHERE proj_no = ?", p, projNo);
            return p;
        }

        public static int PersonFor(int projNo, DbTransaction tx) { bool c; return PersonFor(projNo, tx, out c); }

        /// <summary>A person row for a cost centre, filled like the old program's own rows
        /// (p_type 3 «مستلم ومجهز», the type of practically every row there; zero balances).</summary>
        static int NewPerson(DbTransaction tx, string name)
        {
            return Db.Insert(tx, "INSERT INTO person (p_name, p_bal_id, p_bal_dl, p_bal_id_f, p_bal_dl_f, p_addrs, p_tel, p_note, p_type, p_sjl) VALUES (?, 0, 0, 0, 0, ?, ?, ?, 3, 0)",
                name, DBNull.Value, DBNull.Value, "مركز كلفة");
        }

        /// <summary>What SyncFromRecipients did.</summary>
        public class SyncResult
        {
            public int Recipients, Created, Linked, Issues, Returns, Skipped;
            public long Ms;
            public override string ToString()
            {
                return "مستلمون: " + Recipients + "، مراكز كلفة جديدة: " + Created + "، رُبطت بمراكز موجودة: " + Linked +
                       "، سندات إخراج: " + Issues + "، سندات مردود: " + Returns + (Skipped > 0 ? "، بلا مستلم صالح: " + Skipped : "");
            }
        }

        /// <summary>Gives every issue (sale, doc_kind NULL/0) and issue return (shop_type 3) without a cost centre the
        /// cost centre of its recipient (custom_no): the cost centre linked to that person, else one with exactly
        /// the same name, else a new one (type «أخرى», automatic code, active) linked to the person.
        /// Purchase returns, receipts and stock count adjustments are never touched.
        /// Cheap when there is nothing to do (two small queries): run at every start to pick up documents
        /// that the old program saved (it only knows custom_no).</summary>
        public static SyncResult SyncFromRecipients()
        {
            SyncResult res = new SyncResult();
            if (!Db.HasProj || !Db.HasCcPerson) return res;
            System.Diagnostics.Stopwatch sw = System.Diagnostics.Stopwatch.StartNew();
            string noProj = "(proj_no IS NULL OR proj_no = 0)";
            string issueW = noProj + " AND " + Db.IssueOnly("");
            string returnW = noProj + " AND shop_type = 3 AND " + Db.NotCountAdj("");
            // recipient → [issues, returns]
            Dictionary<int, int[]> todo = new Dictionary<int, int[]>();
            foreach (DataRow r in Db.Query("SELECT custom_no, COUNT(*) FROM sale WHERE " + issueW + " GROUP BY custom_no").Rows)
                todo[Db.I(r[0])] = new int[] { Db.I(r[1]), 0 };
            foreach (DataRow r in Db.Query("SELECT custom_no, COUNT(*) FROM shop WHERE " + returnW + " GROUP BY custom_no").Rows)
            {
                int[] v; if (!todo.TryGetValue(Db.I(r[0]), out v)) { v = new int[2]; todo[Db.I(r[0])] = v; }
                v[1] = Db.I(r[1]);
            }
            if (todo.Count == 0) { res.Ms = sw.ElapsedMilliseconds; return res; }

            Dictionary<int, string> persons = new Dictionary<int, string>();
            foreach (DataRow r in Db.Query("SELECT p_no, p_name FROM person").Rows) persons[Db.I(r[0])] = Db.S(r[1]);
            Dictionary<int, int> byPerson = new Dictionary<int, int>();                                   // p_no → proj_no
            Dictionary<string, int> byName = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase); // name → proj_no
            HashSet<int> linked = new HashSet<int>();                                                     // cost centres with a person
            foreach (DataRow r in Db.Query("SELECT proj_no, proj_name, p_no FROM project ORDER BY proj_no").Rows)
            {
                int pj = Db.I(r[0]), pn = Db.I(r[2]);
                string nm = Db.S(r[1]).Trim();
                if (pn > 0) { linked.Add(pj); if (!byPerson.ContainsKey(pn)) byPerson[pn] = pj; }
                if (nm.Length > 0 && !byName.ContainsKey(nm)) byName[nm] = pj;
            }
            string code = NextCode(Other);                               // O-001…: counted on in memory
            int codeNo = int.Parse(code.Substring(2));
            bool hasType = Db.HasCcType;

            foreach (KeyValuePair<int, int[]> kv in todo)
            {
                int pno = kv.Key;
                string pname;
                if (pno <= 0 || !persons.TryGetValue(pno, out pname) || pname.Trim().Length == 0) { res.Skipped += kv.Value[0] + kv.Value[1]; continue; }
                pname = pname.Trim();
                string ccName = pname.Length > 100 ? pname.Substring(0, 100) : pname;
                int[] counts = kv.Value;
                Db.InTx(delegate(DbTransaction tx)
                {
                    int pj;
                    bool made = false, link = false;
                    if (!byPerson.TryGetValue(pno, out pj))
                    {
                        // another computer starting at the same moment may have made it already: look again inside the transaction
                        int again = Db.I(Db.S1(tx, "SELECT MIN(proj_no) FROM project WHERE p_no = ?", pno));
                        if (again > 0) { pj = again; byPerson[pno] = pj; }
                        else if (byName.TryGetValue(ccName, out pj)) link = !linked.Contains(pj);
                        else
                        {
                            string c = "O-" + (codeNo++).ToString("000");
                            pj = hasType
                                ? Db.Insert(tx, "INSERT INTO project (proj_code, proj_name, status, cc_type, p_no) VALUES (?, ?, 1, ?, ?)", c, ccName, Other, pno)
                                : Db.Insert(tx, "INSERT INTO project (proj_code, proj_name, status, p_no) VALUES (?, ?, 1, ?)", c, ccName, pno);
                            made = true;
                        }
                        if (link) Db.X(tx, "UPDATE project SET p_no = ? WHERE proj_no = ?", pno, pj);
                    }
                    Db.X(tx, "UPDATE sale SET proj_no = ? WHERE custom_no = ? AND " + issueW, pj, pno);
                    Db.X(tx, "UPDATE shop SET proj_no = ? WHERE custom_no = ? AND " + returnW, pj, pno);
                    // after the commit: remember for the next recipients
                    if (made || link) { byPerson[pno] = pj; linked.Add(pj); }
                    if (made) { byName[ccName] = pj; res.Created++; }
                    if (link) res.Linked++;
                });
                res.Recipients++; res.Issues += counts[0]; res.Returns += counts[1];
            }
            res.Ms = sw.ElapsedMilliseconds;
            return res;
        }

        /// <summary>Program start: links the issues to cost centres. The first time (flag recipients_converted)
        /// a backup is taken first (Access; the server keeps its own backups) and the conversion is logged.
        /// Never stops the program: a problem is shown and the conversion is tried again at the next start.</summary>
        public static SyncResult ConvertAtStart()
        {
            if (!Db.HasProj || !Db.HasCcPerson) return null;
            try
            {
                bool first = Db.GetSetting("recipients_converted", "") != "1";
                if (first && !Db.IsSql) ConversionBackup = Backup.Now("قبل تحويل المستلمين إلى مراكز كلفة");
                SyncResult r = SyncFromRecipients();
                if (first)
                {
                    Db.SetSetting("recipients_converted", "1");
                    Db.Log(null, "تحويل المستلمين إلى مراكز كلفة", r.ToString() + " (" + r.Ms + " ms)");
                }
                else if (r.Recipients > 0) Db.Log(null, "ربط سندات جديدة بمراكز الكلفة", r.ToString());
                return r;
            }
            catch (Exception ex)
            {
                UI.Warn("تعذر ربط سندات الإخراج بمراكز الكلفة (سيُعاد المحاولة عند التشغيل القادم):\n" + ex.Message);
                return null;
            }
        }

        /// <summary>The backup taken before the first conversion (null = none: SQL Server, or not yet).</summary>
        public static string ConversionBackup;

        /// <summary>Closes (status 2) or reopens (status 1) a cost centre. A closed one is not offered in new documents.</summary>
        public static void SetClosed(int id, bool closed)
        {
            Db.InTx(delegate(DbTransaction tx)
            {
                List<KeyValuePair<string, string>> before = AuditTrail.CostCentreSnap(tx, id);
                Db.X(tx, "UPDATE project SET status = ? WHERE proj_no = ?", closed ? 2 : 1, id);
                AuditTrail.Changes(tx, AuditTrail.Log(tx, closed ? "إغلاق مركز كلفة" : "إعادة فتح مركز كلفة", Cache.Project(id)), "project", Cache.Project(id), before, AuditTrail.CostCentreSnap(tx, id));
            });
            Cache.LoadProjects();
        }

        /// <summary>Per cost centre: [0] issue documents, [1] issued quantity, [2] returned quantity, [3] cost of the issues (FIFO).
        /// Only cost centres with movements in the period appear. store 0 = all stores.</summary>
        public static Dictionary<int, double[]> Totals(DateTime from, DateTime toEx, int store)
        {
            Dictionary<int, double[]> d = new Dictionary<int, double[]>();
            if (!Db.HasProj) return d;
            string sc = store > 0 ? " AND h.stor = ?" : "";
            List<object> a = new List<object> { from, toEx }; if (store > 0) a.Add(store);
            string w = "h.proj_no IS NOT NULL AND " + Db.IssueOnly("h") + " AND h.menu_date >= ? AND h.menu_date < ?" + sc;
            Func<int, double[]> get = delegate(int k) { double[] v; if (!d.TryGetValue(k, out v)) { v = new double[4]; d[k] = v; } return v; };
            foreach (DataRow x in Db.Query("SELECT h.proj_no, COUNT(*) FROM sale AS h WHERE " + w + " GROUP BY h.proj_no", a.ToArray()).Rows) get(Db.I(x[0]))[0] = Db.D(x[1]);
            foreach (DataRow x in Db.Query("SELECT h.proj_no, SUM(l.qty) FROM sale AS h INNER JOIN sale_menu AS l ON h.shop_no = l.seq WHERE " + w + " GROUP BY h.proj_no", a.ToArray()).Rows) get(Db.I(x[0]))[1] = Db.D(x[1]);
            foreach (DataRow x in Db.Query("SELECT h.proj_no, SUM(l.qty) FROM shop AS h INNER JOIN shop_menu AS l ON h.shop_no = l.seq WHERE h.proj_no IS NOT NULL AND h.shop_type = 3 AND h.menu_date >= ? AND h.menu_date < ?" + sc + " GROUP BY h.proj_no", a.ToArray()).Rows) get(Db.I(x[0]))[2] = Db.D(x[1]);
            foreach (DataRow x in Db.Query("SELECT h.proj_no, SUM(x.qty * s.cost) FROM ((sale AS h INNER JOIN sale_menu AS m ON h.shop_no = m.seq) INNER JOIN sale_menu_1 AS x ON x.seq_sale = m.auto_seq) INNER JOIN shop_menu AS s ON s.auto_seq = x.seq_shop WHERE " + w + " GROUP BY h.proj_no", a.ToArray()).Rows) get(Db.I(x[0]))[3] = Db.D(x[1]);
            return d;
        }
    }
}
