using System.Collections.Generic;
using System.Data.Common;
using System.Data.OleDb;
using System.Data.SqlClient;
using System.Data;
using System.Diagnostics;
using System.IO;
using System.Text;
using System;

namespace Stores
{
    enum DbKind { Access, SqlServer }

    /// <summary>
    /// Data access. Two back ends are supported with the same SQL:
    ///  - SqlServer : a central server (cloud or office) - many users from anywhere over the internet.
    ///  - Access    : the original .AMR file (shared folder on a local network, or a single computer).
    /// SQL is written with '?' placeholders; for SQL Server they are rewritten to @p0, @p1 ...
    /// </summary>
    static class Db
    {
        public static DbKind Kind = DbKind.Access;
        public static string DataPath;          // Access file
        public static string SqlConnStr;        // SQL Server
        public static string Provider;
        static DbConnection conn;

        public static bool IsSql { get { return Kind == DbKind.SqlServer; } }

        public static string Describe
        {
            get
            {
                if (IsSql)
                {
                    try { SqlConnectionStringBuilder b = new SqlConnectionStringBuilder(SqlConnStr); return "السيرفر: " + b.DataSource + " / " + b.InitialCatalog; }
                    catch { return "SQL Server"; }
                }
                return "ملف: " + DataPath;
            }
        }

        // ---------------------------------------------------------------- connect
        static string AccessCs(string provider, string path)
        {
            // Share Deny None + record level locking => several computers on a shared folder
            return "Provider=" + provider + ";Data Source=" + path + ";Mode=Share Deny None;" +
                   "Jet OLEDB:Database Locking Mode=1;";
        }

        public static string InitAccess(string path)
        {
            Close();
            string[] provs = { "Microsoft.Jet.OLEDB.4.0", "Microsoft.ACE.OLEDB.12.0", "Microsoft.ACE.OLEDB.16.0" };
            string last = "لا يوجد مزود بيانات Access مناسب على هذا الحاسوب";
            foreach (string p in provs)
            {
                try
                {
                    OleDbConnection c = new OleDbConnection(AccessCs(p, path));
                    c.Open();
                    conn = c; Provider = p; DataPath = path; Kind = DbKind.Access;
                    return null;
                }
                catch (Exception ex) { last = ex.Message; }
            }
            return last;
        }

        public static string InitSql(string connStr)
        {
            Close();
            try
            {
                SqlConnection c = null;
                for (int attempt = 1; ; attempt++)
                {
                    try
                    {
                        c = new SqlConnection(connStr);
                        c.Open();
                        break;
                    }
                    catch (SqlException ex)
                    {
                        // Azure SQL serverless/free databases pause when idle; the first connection wakes them (≈ 1 minute)
                        bool waking = ex.Number == 40613 || ex.Number == 40197 || ex.Number == 40501 || ex.Number == 49918 || ex.Number == 49919 || ex.Number == 49920;
                        if (!waking || attempt >= 6) throw;
                        System.Threading.Thread.Sleep(10000);
                    }
                }
                conn = c; SqlConnStr = connStr; Kind = DbKind.SqlServer; Provider = "SQL Server";
                return null;
            }
            catch (Exception ex) { return SqlErrorText(ex); }
        }

        /// <summary>Plain Arabic explanation of the common connection problems with Azure SQL / SQL Server.</summary>
        public static string SqlErrorText(Exception ex)
        {
            string m = ex.Message;
            SqlException se = ex as SqlException;
            int n = se != null ? se.Number : 0;
            if (n == 40615 || m.IndexOf("is not allowed to access the server", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                string ip = "";
                int i = m.IndexOf("IP address '", StringComparison.OrdinalIgnoreCase);
                if (i >= 0) { int j = m.IndexOf('\'', i + 12); if (j > i) ip = m.Substring(i + 12, j - i - 12); }
                return "جدار الحماية في السيرفر لا يسمح لهذا الحاسوب بالاتصال.\n" +
                       (ip.Length > 0 ? "عنوان هذا الحاسوب على الإنترنت: " + ip + "\n" : "") +
                       "الحل: في بوابة Azure ← خادم SQL ← Networking ← أضف هذا العنوان إلى Firewall rules ثم Save، وانتظر دقيقة وأعد المحاولة.";
            }
            if (n == 18456 || m.IndexOf("Login failed", StringComparison.OrdinalIgnoreCase) >= 0)
                return "اسم المستخدم أو كلمة المرور الخاصة بالسيرفر غير صحيحة.";
            if (n == 4060 || m.IndexOf("Cannot open database", StringComparison.OrdinalIgnoreCase) >= 0)
                return "اسم قاعدة البيانات غير صحيح أو غير موجود على السيرفر.";
            if (n == 40613 || n == 40197 || n == 40501)
                return "قاعدة البيانات في وضع الاستيقاظ أو مشغولة حاليا. انتظر دقيقة وأعد المحاولة.";
            if (n == 53 || n == 11001 || n == 10060 || m.IndexOf("network-related", StringComparison.OrdinalIgnoreCase) >= 0)
                return "تعذر الوصول إلى السيرفر. تأكد أن حاسوب السيرفر شغال ومتصل بنفس الشبكة، ومن اسمه أو عنوانه.";
            return m;
        }

        public static string BuildSqlConnStr(string server, string database, string user, string password, bool trustCert)
        {
            SqlConnectionStringBuilder b = new SqlConnectionStringBuilder();
            b.DataSource = server;
            b.InitialCatalog = database;
            if (string.IsNullOrEmpty(user)) b.IntegratedSecurity = true;
            else { b.UserID = user; b.Password = password; }
            // always encrypted over the network; LocalDB (same computer, named pipes) does not support it
            bool local = server.Trim().StartsWith("(localdb)", StringComparison.OrdinalIgnoreCase);
            b.Encrypt = !local;
            b.TrustServerCertificate = trustCert;
            b.ConnectTimeout = 60;
            b.ApplicationName = Program.AppName;
            b.MultipleActiveResultSets = false;
            return b.ConnectionString;
        }

        public static void Close()
        {
            if (conn != null)
            {
                try { conn.Close(); conn.Dispose(); } catch { }
                conn = null;
            }
            try { OleDbConnection.ReleaseObjectPool(); } catch { }
        }

        public static DbConnection Conn
        {
            get
            {
                if (conn == null || conn.State != ConnectionState.Open)
                {
                    if (conn != null) { try { conn.Dispose(); } catch { } }
                    if (IsSql) conn = new SqlConnection(SqlConnStr);
                    else conn = new OleDbConnection(AccessCs(Provider ?? "Microsoft.Jet.OLEDB.4.0", DataPath));
                    conn.Open();
                }
                return conn;
            }
        }

        // ---------------------------------------------------------------- commands
        static string Translate(string sql)
        {
            if (!IsSql) return sql;
            StringBuilder sb = new StringBuilder(sql.Length + 16);
            int n = 0; bool inStr = false;
            foreach (char c in sql)
            {
                if (c == '\'') inStr = !inStr;
                if (c == '?' && !inStr) { sb.Append("@p").Append(n++); continue; }
                sb.Append(c);
            }
            return sb.ToString();
        }

        static void AddParams(DbCommand cmd, object[] args)
        {
            if (args == null) return;
            for (int i = 0; i < args.Length; i++)
            {
                object a = args[i];
                if (IsSql)
                {
                    SqlParameter p = new SqlParameter("@p" + i, SqlDbType.NVarChar);
                    if (a == null || a is DBNull) { p.Value = DBNull.Value; }
                    else if (a is DateTime) { p.SqlDbType = SqlDbType.DateTime; p.Value = a; }
                    else if (a is string) { p.SqlDbType = SqlDbType.NVarChar; p.Size = Math.Max(1, ((string)a).Length); p.Value = a; }
                    else if (a is int) { p.SqlDbType = SqlDbType.Int; p.Value = a; }
                    else if (a is double || a is float) { p.SqlDbType = SqlDbType.Float; p.Value = Convert.ToDouble(a); }
                    else if (a is decimal) { p.SqlDbType = SqlDbType.Money; p.Value = a; }
                    else if (a is bool) { p.SqlDbType = SqlDbType.Bit; p.Value = a; }
                    else p.Value = a;
                    cmd.Parameters.Add(p);
                }
                else
                {
                    OleDbParameter p = new OleDbParameter();
                    if (a == null || a is DBNull) { p.OleDbType = OleDbType.VarWChar; p.Value = DBNull.Value; }
                    else if (a is DateTime) { p.OleDbType = OleDbType.Date; p.Value = a; }
                    else if (a is string) { p.OleDbType = OleDbType.VarWChar; p.Value = a; }
                    else if (a is int) { p.OleDbType = OleDbType.Integer; p.Value = a; }
                    else if (a is double || a is float) { p.OleDbType = OleDbType.Double; p.Value = Convert.ToDouble(a); }
                    else if (a is decimal) { p.OleDbType = OleDbType.Currency; p.Value = a; }
                    else if (a is bool) { p.OleDbType = OleDbType.Boolean; p.Value = a; }
                    else p.Value = a;
                    cmd.Parameters.Add(p);
                }
            }
        }

        static DbCommand Cmd(string sql, DbTransaction tx, object[] args)
        {
            DbCommand c = (tx != null ? tx.Connection : Conn).CreateCommand();
            c.CommandText = Translate(sql);
            if (tx != null) c.Transaction = tx;
            c.CommandTimeout = 180;
            AddParams(c, args);
            return c;
        }

        static bool IsConnectionLost(Exception ex)
        {
            string m = ex.Message;
            return m.IndexOf("network", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   m.IndexOf("transport-level", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   m.IndexOf("connection", StringComparison.OrdinalIgnoreCase) >= 0 && m.IndexOf("broken", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        static T Retry<T>(Func<T> f)
        {
            try { return f(); }
            catch (DbException ex)
            {
                if (!IsConnectionLost(ex)) throw;
                Close();               // reconnect once (e.g. internet dropped for a moment)
                return f();
            }
        }

        static DataTable Fill(DbCommand c)
        {
            DataTable t = new DataTable();
            using (DbDataReader r = c.ExecuteReader()) t.Load(r);
            foreach (DataColumn col in t.Columns) { col.ReadOnly = false; col.AllowDBNull = true; col.MaxLength = -1; col.AutoIncrement = false; col.Unique = false; }
            t.PrimaryKey = null;
            return t;
        }

        public static DataTable Query(string sql, params object[] args)
        {
            return Retry(() => { using (DbCommand c = Cmd(sql, null, args)) return Fill(c); });
        }

        public static object Scalar(string sql, params object[] args)
        {
            return Retry(() => { using (DbCommand c = Cmd(sql, null, args)) return c.ExecuteScalar(); });
        }

        public static int Exec(string sql, params object[] args)
        {
            return Retry(() => { using (DbCommand c = Cmd(sql, null, args)) return c.ExecuteNonQuery(); });
        }

        public static DataTable Q(DbTransaction tx, string sql, params object[] args)
        {
            using (DbCommand c = Cmd(sql, tx, args)) return Fill(c);
        }

        public static object S1(DbTransaction tx, string sql, params object[] args)
        {
            using (DbCommand c = Cmd(sql, tx, args)) return c.ExecuteScalar();
        }

        public static int X(DbTransaction tx, string sql, params object[] args)
        {
            using (DbCommand c = Cmd(sql, tx, args)) return c.ExecuteNonQuery();
        }

        /// <summary>INSERT and return the new auto number.</summary>
        public static int Insert(DbTransaction tx, string sql, params object[] args)
        {
            if (IsSql) return I(S1(tx, sql + "; SELECT CAST(SCOPE_IDENTITY() AS INT);", args));
            X(tx, sql, args);
            return I(S1(tx, "SELECT @@IDENTITY"));
        }

        public static void InTx(Action<DbTransaction> work)
        {
            DbTransaction tx = Conn.BeginTransaction(IsolationLevel.ReadCommitted);
            try
            {
                if (IsSql)
                {
                    // one writer at a time across all computers: balances, FIFO links and numbers stay consistent
                    // (saves take milliseconds, so others wait at most a moment)
                    int r = I(S1(tx, "DECLARE @r INT; EXEC @r = sp_getapplock @Resource = 'EngStores.write', @LockMode = 'Exclusive', @LockOwner = 'Transaction', @LockTimeout = 20000; SELECT @r;"));
                    if (r < 0) throw new UserError("البيانات مشغولة بحفظ من حاسوب آخر. حاول مرة ثانية بعد لحظة.");
                }
                work(tx); tx.Commit();
            }
            catch { try { tx.Rollback(); } catch { } throw; }
        }

        // ---------------------------------------------------------------- dialect helpers
        /// <summary>Numeric value of a text column such as "7898" or "7898C".</summary>
        public static string NumOf(string col)
        {
            if (IsSql) return "TRY_CAST(REPLACE(" + col + ",'C','') AS FLOAT)";
            return "VAL(IIF(" + col + " IS NULL, '0', " + col + "))";
        }

        public static bool TableExists(string name)
        {
            try
            {
                if (IsSql) return I(Scalar("SELECT COUNT(*) FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_NAME = ?", name)) > 0;
                DataTable t = ((OleDbConnection)Conn).GetOleDbSchemaTable(OleDbSchemaGuid.Tables, new object[] { null, null, name, "TABLE" });
                return t != null && t.Rows.Count > 0;
            }
            catch { return false; }
        }

        public static bool ColumnExists(string table, string col)
        {
            try { Scalar("SELECT TOP 1 " + col + " FROM " + table); return true; }
            catch { return false; }
        }

        /// <summary>True when the project / cost centre columns exist (sale.proj_no, sale.doc_kind, shop.proj_no).</summary>
        public static bool HasProj;
        /// <summary>True when project.cc_type (cost centre type, see CostCentres) exists.</summary>
        public static bool HasCcType;
        /// <summary>True when project.p_no (the person row linked to a cost centre, see CostCentres.PersonFor) exists.</summary>
        public static bool HasCcPerson;
        /// <summary>True when item.unit (unit of measure, e.g. متر / لفة / قطعة) exists.</summary>
        public static bool HasUnit;
        public static string SchemaWarning;

        static void Create(string name, string sqlServer, string access)
        {
            if (TableExists(name)) return;
            try { Exec(IsSql ? sqlServer : access); } catch (Exception ex) { SchemaWarning = name + ": " + ex.Message; }
        }

        static void AddColumn(string table, string col) { AddColumn(table, col, "INT", "INT"); }

        /// <summary>Adds a nullable column; sqlType / accessType e.g. "NVARCHAR(20)" / "TEXT(20)".</summary>
        static void AddColumn(string table, string col, string sqlType, string accessType)
        {
            if (ColumnExists(table, col)) return;
            try { Exec(IsSql ? "ALTER TABLE " + table + " ADD " + col + " " + sqlType + " NULL" : "ALTER TABLE " + table + " ADD COLUMN " + col + " " + accessType); }
            catch (Exception ex) { SchemaWarning = table + "." + col + ": " + ex.Message; }
        }

        /// <summary>Creates the tables/columns added by this program (log, online users, settings,
        /// projects, reservations). The original program simply ignores them.</summary>
        public static void EnsureAppTables()
        {
            SchemaWarning = null;
            Create("app_log",
                "CREATE TABLE app_log (id INT IDENTITY(1,1) PRIMARY KEY, log_at DATETIME, user_name NVARCHAR(50), computer NVARCHAR(64), act NVARCHAR(100), details NVARCHAR(400))",
                "CREATE TABLE app_log (id COUNTER PRIMARY KEY, log_at DATETIME, user_name TEXT(50), computer TEXT(64), act TEXT(100), details TEXT(255))");
            Create("app_online",
                "CREATE TABLE app_online (computer NVARCHAR(64) PRIMARY KEY, user_name NVARCHAR(50), last_seen DATETIME, started DATETIME)",
                "CREATE TABLE app_online (computer TEXT(64) PRIMARY KEY, user_name TEXT(50), last_seen DATETIME, started DATETIME)");
            Create("app_settings",
                "CREATE TABLE app_settings (k NVARCHAR(50) PRIMARY KEY, v NVARCHAR(255))",
                "CREATE TABLE app_settings (k TEXT(50) PRIMARY KEY, v TEXT(255))");
            Create("app_blob",
                "CREATE TABLE app_blob (k NVARCHAR(50) PRIMARY KEY, v NVARCHAR(MAX))",
                "CREATE TABLE app_blob (k TEXT(50) PRIMARY KEY, v MEMO)");
            Create("project",
                "CREATE TABLE project (proj_no INT IDENTITY(1,1) PRIMARY KEY, proj_code NVARCHAR(30), proj_name NVARCHAR(100) NOT NULL, client NVARCHAR(100), status INT, start_date DATETIME, notes NVARCHAR(255), cc_type INT)",
                "CREATE TABLE project (proj_no COUNTER PRIMARY KEY, proj_code TEXT(30), proj_name TEXT(100), client TEXT(100), status INT, start_date DATETIME, notes TEXT(255), cc_type INT)");
            Create("reserve",
                "CREATE TABLE reserve (res_no INT IDENTITY(1,1) PRIMARY KEY, menu_no NVARCHAR(10), res_date DATETIME, proj_no INT, stor INT, notes NVARCHAR(255), status INT, user_name NVARCHAR(50))",
                "CREATE TABLE reserve (res_no COUNTER PRIMARY KEY, menu_no TEXT(10), res_date DATETIME, proj_no INT, stor INT, notes TEXT(255), status INT, user_name TEXT(50))");
            Create("reserve_menu",
                "CREATE TABLE reserve_menu (id INT IDENTITY(1,1) PRIMARY KEY, seq INT, item_no INT, qty FLOAT)",
                "CREATE TABLE reserve_menu (id COUNTER PRIMARY KEY, seq INT, item_no INT, qty DOUBLE)");
            // new columns on the original document tables (needs nobody else to have the table open in Access)
            AddColumn("sale", "proj_no");
            AddColumn("sale", "doc_kind");
            AddColumn("shop", "proj_no");
            AddColumn("item", "unit", "NVARCHAR(20)", "TEXT(20)");
            HasUnit = ColumnExists("item", "unit");
            HasProj = ColumnExists("sale", "proj_no") && ColumnExists("sale", "doc_kind") && ColumnExists("shop", "proj_no") && TableExists("project");
            // cost centre type (مشروع / قسم / صيانة / إدارة / أخرى); NULL on old rows = مشروع
            if (HasProj) AddColumn("project", "cc_type");
            HasCcType = HasProj && ColumnExists("project", "cc_type");
            // the person row (table person) that stands for this cost centre in the original tables: issues store it
            // in custom_no, so the old program keeps showing the recipient (owner: «المستلم = مركز الكلفة»)
            if (HasProj) AddColumn("project", "p_no");
            HasCcPerson = HasProj && ColumnExists("project", "p_no");
            // physical stock count (الجرد الفعلي): count sessions + shop.doc_kind (2 = count surplus; sale.doc_kind 2 = count shortage)
            Create("count_head",
                "CREATE TABLE count_head (count_no INT IDENTITY(1,1) PRIMARY KEY, menu_no NVARCHAR(10), count_date DATETIME, stor INT, type_no INT, notes NVARCHAR(255), status INT, user_name NVARCHAR(50), created DATETIME, posted_at DATETIME, posted_by NVARCHAR(50), sale_id INT, shop_id INT)",
                "CREATE TABLE count_head (count_no COUNTER PRIMARY KEY, menu_no TEXT(10), count_date DATETIME, stor INT, type_no INT, notes TEXT(255), status INT, user_name TEXT(50), created DATETIME, posted_at DATETIME, posted_by TEXT(50), sale_id INT, shop_id INT)");
            Create("count_menu",
                "CREATE TABLE count_menu (id INT IDENTITY(1,1) PRIMARY KEY, seq INT, item_no INT, book_qty FLOAT, count_qty FLOAT, added INT)",
                "CREATE TABLE count_menu (id COUNTER PRIMARY KEY, seq INT, item_no INT, book_qty DOUBLE, count_qty DOUBLE, added INT)");
            AddColumn("shop", "doc_kind");
            HasCount = HasProj && ColumnExists("shop", "doc_kind") && TableExists("count_head") && TableExists("count_menu");
            // material requisitions (طلبات صرف المواد) with approval: head, lines, link to the issues made from them, see Requisitions
            Create("mreq",
                "CREATE TABLE mreq (mreq_no INT IDENTITY(1,1) PRIMARY KEY, menu_no NVARCHAR(10), mreq_date DATETIME, proj_no INT, stor INT, requested_by NVARCHAR(100), status INT, approved_by NVARCHAR(50), approved_at DATETIME, reject_reason NVARCHAR(255), notes NVARCHAR(255), user_name NVARCHAR(50), created DATETIME, est_value FLOAT, closed_by NVARCHAR(50))",
                "CREATE TABLE mreq (mreq_no COUNTER PRIMARY KEY, menu_no TEXT(10), mreq_date DATETIME, proj_no INT, stor INT, requested_by TEXT(100), status INT, approved_by TEXT(50), approved_at DATETIME, reject_reason TEXT(255), notes TEXT(255), user_name TEXT(50), created DATETIME, est_value DOUBLE, closed_by TEXT(50))");
            Create("mreq_menu",
                "CREATE TABLE mreq_menu (id INT IDENTITY(1,1) PRIMARY KEY, seq INT, item_no INT, qty FLOAT, issued_qty FLOAT)",
                "CREATE TABLE mreq_menu (id COUNTER PRIMARY KEY, seq INT, item_no INT, qty DOUBLE, issued_qty DOUBLE)");
            Create("mreq_issue",
                "CREATE TABLE mreq_issue (id INT IDENTITY(1,1) PRIMARY KEY, mreq_no INT, sale_id INT, linked_at DATETIME)",
                "CREATE TABLE mreq_issue (id COUNTER PRIMARY KEY, mreq_no INT, sale_id INT, linked_at DATETIME)");
            Requisitions.EnsurePermission();
            Requisitions.Ready = HasProj && TableExists("mreq") && TableExists("mreq_menu") && TableExists("mreq_issue");
            // purchasing: shelf location + min / max per item and store, lead time per item, purchase requests (see PurchaseRequests)
            AddColumn("item_stor", "bin", "NVARCHAR(20)", "TEXT(20)");
            AddColumn("item_stor", "min_qty", "FLOAT", "DOUBLE");
            AddColumn("item_stor", "max_qty", "FLOAT", "DOUBLE");
            AddColumn("item", "lead_days");
            HasBin = ColumnExists("item_stor", "bin");
            HasMinMax = ColumnExists("item_stor", "min_qty") && ColumnExists("item_stor", "max_qty") && ColumnExists("item", "lead_days");
            Create("preq",
                "CREATE TABLE preq (preq_no INT IDENTITY(1,1) PRIMARY KEY, menu_no NVARCHAR(10), preq_date DATETIME, stor INT, status INT, p_no INT, notes NVARCHAR(255), user_name NVARCHAR(50))",
                "CREATE TABLE preq (preq_no COUNTER PRIMARY KEY, menu_no TEXT(10), preq_date DATETIME, stor INT, status INT, p_no INT, notes TEXT(255), user_name TEXT(50))");
            Create("preq_menu",
                "CREATE TABLE preq_menu (id INT IDENTITY(1,1) PRIMARY KEY, seq INT, item_no INT, qty FLOAT, received_qty FLOAT)",
                "CREATE TABLE preq_menu (id COUNTER PRIMARY KEY, seq INT, item_no INT, qty DOUBLE, received_qty DOUBLE)");
            AddColumn("shop", "preq_no");
            HasPreq = TableExists("preq") && TableExists("preq_menu") && ColumnExists("shop", "preq_no");
            WriteOffs.EnsureSchema();   // write-offs «سندات الإتلاف» (wo_head / wo_menu, permission 83), posted as sale.doc_kind 3
            // audit trail with before / after values (app_log_detail) and the month lock permission (command 84)
            AuditTrail.EnsureSchema();
            PeriodLock.EnsurePermission();
        }

        /// <summary>True when item_stor.bin (shelf location «الموقع») exists.</summary>
        public static bool HasBin;
        /// <summary>True when item_stor.min_qty / max_qty and item.lead_days exist.</summary>
        public static bool HasMinMax;
        /// <summary>True when the purchase request tables (preq, preq_menu) and shop.preq_no exist.</summary>
        public static bool HasPreq;

        /// <summary>True when the physical stock count tables and shop.doc_kind exist.</summary>
        public static bool HasCount;

        /// <summary>SQL fragment: rows that are not stock count adjustments (shop / sale doc_kind 2) and not posted
        /// write-offs (sale doc_kind 3, see WriteOffs).</summary>
        public static string NotCountAdj(string alias)
        {
            if (!HasCount) return " 1=1 ";
            string a = string.IsNullOrEmpty(alias) ? "" : alias + ".";
            return " (" + a + "doc_kind IS NULL OR " + a + "doc_kind NOT IN (2, " + WriteOffs.SaleKind + ")) ";
        }

        // ---------------------------------------------------------------- shared settings (same for all computers)
        public static string GetSetting(string key, string def)
        {
            try
            {
                object o = Scalar("SELECT v FROM app_settings WHERE k = ?", key);
                return o == null || o is DBNull ? def : Convert.ToString(o);
            }
            catch { return def; }
        }

        public static void SetSetting(string key, string value)
        {
            try
            {
                if (Exec("UPDATE app_settings SET v = ? WHERE k = ?", value ?? "", key) == 0)
                    Exec("INSERT INTO app_settings (k, v) VALUES (?, ?)", key, value ?? "");
            }
            catch { }
        }

        /// <summary>SQL fragment: sale rows that are normal issues (not purchase returns, not stock count shortages, not write-offs).</summary>
        public static string IssueOnly(string alias)
        {
            if (!HasProj) return " 1=1 ";
            string a = string.IsNullOrEmpty(alias) ? "" : alias + ".";
            return " (" + a + "doc_kind IS NULL OR " + a + "doc_kind = 0) ";
        }

        public static void Log(DbTransaction tx, string action, string details)
        {
            try
            {
                if (details != null && details.Length > 250) details = details.Substring(0, 250);
                string sql = "INSERT INTO app_log (log_at, user_name, computer, act, details) VALUES (?,?,?,?,?)";
                object[] a = { Now(), Session.UserName ?? "", Environment.MachineName, action, details ?? "" };
                if (tx != null) X(tx, sql, a); else Exec(sql, a);
            }
            catch { }
        }

        public static DateTime Now()
        {
            DateTime n = DateTime.Now;
            return new DateTime(n.Year, n.Month, n.Day, n.Hour, n.Minute, n.Second);
        }

        // ---------------------------------------------------------------- online users
        public static void Heartbeat()
        {
            try
            {
                string pc = Environment.MachineName;
                int n = Exec("UPDATE app_online SET user_name = ?, last_seen = ? WHERE computer = ?", Session.UserName ?? "", Now(), pc);
                if (n == 0) Exec("INSERT INTO app_online (computer, user_name, last_seen, started) VALUES (?,?,?,?)", pc, Session.UserName ?? "", Now(), Now());
            }
            catch { }
        }

        public static void SignOff()
        {
            try { Exec("DELETE FROM app_online WHERE computer = ?", Environment.MachineName); } catch { }
        }

        public static DataTable OnlineUsers()
        {
            try { return Query("SELECT computer, user_name, last_seen, started FROM app_online WHERE last_seen >= ? ORDER BY user_name", Now().AddMinutes(-3)); }
            catch { return new DataTable(); }
        }

        /// <summary>Warns when an Access file is used from a local disk mirrored by a sync client.</summary>
        public static string SyncWarning()
        {
            if (IsSql) return null;
            try
            {
                string path = DataPath;
                if (path.StartsWith(@"\\")) return null;
                DriveInfo di = new DriveInfo(Path.GetPathRoot(path));
                if (di.DriveType == DriveType.Network) return null;
                string[] syncProcs = { "cloud-drive-daemon", "OneDrive", "Dropbox", "GoogleDriveFS", "Resilio Sync" };
                foreach (string p in syncProcs)
                {
                    if (Process.GetProcessesByName(p).Length > 0)
                        return "تنبيه: ملف البيانات على قرص محلي تتم مزامنته تلقائيا. إذا عمل أكثر من حاسوب في نفس الوقت ستحصل تعارضات. " +
                               "للعمل المشترك استخدم السيرفر (SQL Server) (شاشة الدخول ← إعدادات الاتصال والسيرفر).";
                }
            }
            catch { }
            return null;
        }

        // ---------------------------------------------------------------- converters
        public static double D(object o) { if (o == null || o is DBNull) return 0; try { return Convert.ToDouble(o); } catch { return 0; } }
        public static int I(object o) { if (o == null || o is DBNull) return 0; try { return Convert.ToInt32(o); } catch { return 0; } }
        public static string S(object o) { return (o == null || o is DBNull) ? "" : Convert.ToString(o); }
        public static bool B(object o) { if (o == null || o is DBNull) return false; try { return Convert.ToBoolean(o); } catch { return false; } }
        public static DateTime Dt(object o) { if (o == null || o is DBNull) return DateTime.MinValue; try { return Convert.ToDateTime(o); } catch { return DateTime.MinValue; } }
        public static object Null(string s) { return string.IsNullOrEmpty(s) ? (object)DBNull.Value : s; }
    }
}
