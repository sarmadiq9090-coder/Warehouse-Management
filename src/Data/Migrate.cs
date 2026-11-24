using System.Collections.Generic;
using System.Data.OleDb;
using System.Data.SqlClient;
using System.Data;
using System.Drawing;
using System.IO;
using System.Windows.Forms;
using System;

namespace Stores
{
    /// <summary>Copies all data from the Access (.AMR) file into the SQL Server database
    /// (creates the tables, keeps every number/ID exactly as it was).</summary>
    static class Migrate
    {
        static readonly string[] Order = { "stor", "type", "group", "item", "item_stor", "person", "shop", "shop_menu", "sale", "sale_menu", "sale_menu_1", "trans", "trans_menu", "pass", "disply_command", "project", "reserve", "reserve_menu", "count_head", "count_menu", "mreq", "mreq_menu", "mreq_issue", "preq", "preq_menu", "wo_head", "wo_menu", "app_settings", "app_blob", "app_log", "app_log_detail" };

        const string Schema = @"
CREATE TABLE stor (stor_no INT IDENTITY(1,1) PRIMARY KEY, stor_name NVARCHAR(50) NOT NULL);
CREATE TABLE [type] (type_no INT IDENTITY(1,1) PRIMARY KEY, type_name NVARCHAR(50) NOT NULL);
CREATE TABLE [group] (group_no INT IDENTITY(1,1) PRIMARY KEY, group_name NVARCHAR(50) NOT NULL);
CREATE TABLE item (item_no INT IDENTITY(1,1) PRIMARY KEY, item_name NVARCHAR(70) NOT NULL, [group] INT NULL, [type] INT NULL,
  item_bal1 FLOAT NOT NULL DEFAULT 0, item_bal FLOAT NOT NULL DEFAULT 0, item_price_dl1 FLOAT NOT NULL DEFAULT 0, item_bal_ret FLOAT NOT NULL DEFAULT 0,
  qty_f FLOAT NULL DEFAULT 0, item_code NVARCHAR(20) NULL, sale_pr FLOAT NOT NULL DEFAULT 0, tslsl REAL NULL DEFAULT 0, freez BIT NOT NULL DEFAULT 0, unit NVARCHAR(20) NULL, lead_days INT NULL);
CREATE TABLE item_stor (seq_no INT IDENTITY(1,1) PRIMARY KEY, item_no INT NOT NULL, stor INT NULL, item_bal_f FLOAT NULL DEFAULT 0, item_bal FLOAT NULL DEFAULT 0, qty_f FLOAT NULL DEFAULT 0, bin NVARCHAR(20) NULL, min_qty FLOAT NULL, max_qty FLOAT NULL);
CREATE TABLE person (p_no INT IDENTITY(1,1) PRIMARY KEY, p_name NVARCHAR(70) NOT NULL, p_bal_id MONEY NOT NULL DEFAULT 0, p_bal_dl MONEY NOT NULL DEFAULT 0,
  p_bal_id_f MONEY NOT NULL DEFAULT 0, p_bal_dl_f MONEY NOT NULL DEFAULT 0, p_addrs NVARCHAR(50) NULL, p_tel NVARCHAR(50) NULL, p_note NVARCHAR(50) NULL,
  p_fraa INT NULL, p_type INT NOT NULL DEFAULT 1, p_sjl INT NULL DEFAULT 0);
CREATE TABLE shop (shop_no INT IDENTITY(1,1) PRIMARY KEY, menu_no NVARCHAR(10) NOT NULL, menu_date DATETIME NOT NULL, custom_no INT NOT NULL, pay_type INT NOT NULL,
  note NVARCHAR(MAX) NULL, mony_no NVARCHAR(10) NULL, shop_type INT NOT NULL, stor INT NOT NULL, proj_no INT NULL, doc_kind INT NULL, preq_no INT NULL, doc_grp INT NULL);
CREATE TABLE shop_menu (auto_seq INT IDENTITY(1,1) PRIMARY KEY, seq INT NOT NULL, item_no INT NOT NULL, qty FLOAT NOT NULL DEFAULT 0, price_dl MONEY NOT NULL DEFAULT 0,
  cost MONEY NOT NULL DEFAULT 0, qty_sale FLOAT NOT NULL DEFAULT 0, sale_pr FLOAT NOT NULL DEFAULT 0);
CREATE TABLE sale (shop_no INT IDENTITY(1,1) PRIMARY KEY, menu_no NVARCHAR(10) NOT NULL, menu_date DATETIME NOT NULL, p_fraa INT NULL, custom_no INT NOT NULL,
  pay_type INT NOT NULL, ksm_dl MONEY NOT NULL DEFAULT 0, note NVARCHAR(MAX) NULL, mony_no NVARCHAR(10) NULL, wasl_dl FLOAT NOT NULL DEFAULT 0,
  old_bal FLOAT NULL DEFAULT 0, ksm_per REAL NULL, stor INT NOT NULL, proj_no INT NULL, doc_kind INT NULL);
CREATE TABLE sale_menu (auto_seq INT IDENTITY(1,1) PRIMARY KEY, seq INT NOT NULL DEFAULT 0, item_no INT NOT NULL, qty FLOAT NOT NULL DEFAULT 0, price_dl FLOAT NOT NULL DEFAULT 0);
CREATE TABLE sale_menu_1 (auto_seq INT IDENTITY(1,1) PRIMARY KEY, seq_sale INT NULL, seq_shop INT NULL, qty FLOAT NULL, seq_sale1 INT NULL);
CREATE TABLE trans (shop_no INT IDENTITY(1,1) PRIMARY KEY, menu_no NVARCHAR(10) NULL, menu_date DATETIME NULL, stor_out INT NOT NULL, stor_in INT NOT NULL, notes NVARCHAR(MAX) NULL);
CREATE TABLE trans_menu (id INT IDENTITY(1,1) PRIMARY KEY, seq INT NOT NULL, item_no INT NOT NULL, qty FLOAT NOT NULL DEFAULT 0);
CREATE TABLE pass (pass_no INT IDENTITY(1,1) PRIMARY KEY, user_name NVARCHAR(30) NOT NULL, pass NVARCHAR(20) NULL);
CREATE TABLE disply_command (command_no INT PRIMARY KEY, command_name NVARCHAR(50) NULL, user1 BIT NOT NULL DEFAULT 0, user2 BIT NOT NULL DEFAULT 0,
  user3 BIT NOT NULL DEFAULT 0, user4 BIT NOT NULL DEFAULT 0, user5 BIT NOT NULL DEFAULT 0);
CREATE TABLE project (proj_no INT IDENTITY(1,1) PRIMARY KEY, proj_code NVARCHAR(30), proj_name NVARCHAR(100) NOT NULL, client NVARCHAR(100), status INT, start_date DATETIME, notes NVARCHAR(255), cc_type INT, p_no INT);
CREATE TABLE reserve (res_no INT IDENTITY(1,1) PRIMARY KEY, menu_no NVARCHAR(10), res_date DATETIME, proj_no INT, stor INT, notes NVARCHAR(255), status INT, user_name NVARCHAR(50));
CREATE TABLE reserve_menu (id INT IDENTITY(1,1) PRIMARY KEY, seq INT, item_no INT, qty FLOAT);
CREATE TABLE count_head (count_no INT IDENTITY(1,1) PRIMARY KEY, menu_no NVARCHAR(10), count_date DATETIME, stor INT, type_no INT, notes NVARCHAR(255), status INT, user_name NVARCHAR(50), created DATETIME, posted_at DATETIME, posted_by NVARCHAR(50), sale_id INT, shop_id INT);
CREATE TABLE count_menu (id INT IDENTITY(1,1) PRIMARY KEY, seq INT, item_no INT, book_qty FLOAT, count_qty FLOAT, added INT);
CREATE TABLE mreq (mreq_no INT IDENTITY(1,1) PRIMARY KEY, menu_no NVARCHAR(10), mreq_date DATETIME, proj_no INT, stor INT, requested_by NVARCHAR(100), status INT, approved_by NVARCHAR(50), approved_at DATETIME, reject_reason NVARCHAR(255), notes NVARCHAR(255), user_name NVARCHAR(50), created DATETIME, est_value FLOAT, closed_by NVARCHAR(50));
CREATE TABLE mreq_menu (id INT IDENTITY(1,1) PRIMARY KEY, seq INT, item_no INT, qty FLOAT, issued_qty FLOAT);
CREATE TABLE mreq_issue (id INT IDENTITY(1,1) PRIMARY KEY, mreq_no INT, sale_id INT, linked_at DATETIME);
CREATE TABLE preq (preq_no INT IDENTITY(1,1) PRIMARY KEY, menu_no NVARCHAR(10), preq_date DATETIME, stor INT, status INT, p_no INT, notes NVARCHAR(255), user_name NVARCHAR(50));
CREATE TABLE preq_menu (id INT IDENTITY(1,1) PRIMARY KEY, seq INT, item_no INT, qty FLOAT, received_qty FLOAT);
CREATE TABLE wo_head (wo_no INT IDENTITY(1,1) PRIMARY KEY, menu_no NVARCHAR(10), wo_date DATETIME, stor INT, reason INT, notes NVARCHAR(255), status INT, user_name NVARCHAR(50), created DATETIME, approved_by NVARCHAR(50), approved_at DATETIME, reject_reason NVARCHAR(255), sale_id INT, wo_value FLOAT);
CREATE TABLE wo_menu (id INT IDENTITY(1,1) PRIMARY KEY, seq INT, item_no INT, qty FLOAT, reason INT, sale_line INT, cost FLOAT);
CREATE TABLE app_settings (k NVARCHAR(50) PRIMARY KEY, v NVARCHAR(255));
CREATE TABLE app_blob (k NVARCHAR(50) PRIMARY KEY, v NVARCHAR(MAX));
CREATE TABLE app_log (id INT IDENTITY(1,1) PRIMARY KEY, log_at DATETIME, user_name NVARCHAR(50), computer NVARCHAR(64), act NVARCHAR(100), details NVARCHAR(400));
CREATE TABLE app_online (computer NVARCHAR(64) PRIMARY KEY, user_name NVARCHAR(50), last_seen DATETIME, started DATETIME);
CREATE TABLE app_log_detail (id INT IDENTITY(1,1) PRIMARY KEY, log_id INT NOT NULL, tbl NVARCHAR(30), rec_key NVARCHAR(50), fld NVARCHAR(150), old_v NVARCHAR(255), new_v NVARCHAR(255));";

        const string Indexes = @"
CREATE UNIQUE INDEX ux_item_name ON item(item_name);
CREATE INDEX ix_item_code ON item(item_code);
CREATE UNIQUE INDEX ux_item_stor ON item_stor(item_no, stor);
CREATE INDEX ix_person_name ON person(p_name);
CREATE INDEX ix_shop_date ON shop(menu_date); CREATE INDEX ix_shop_cust ON shop(custom_no); CREATE INDEX ix_shop_no ON shop(menu_no);
CREATE INDEX ix_shopm_seq ON shop_menu(seq); CREATE INDEX ix_shopm_item ON shop_menu(item_no);
CREATE INDEX ix_sale_date ON sale(menu_date); CREATE INDEX ix_sale_cust ON sale(custom_no); CREATE INDEX ix_sale_no ON sale(menu_no);
CREATE INDEX ix_salem_seq ON sale_menu(seq); CREATE INDEX ix_salem_item ON sale_menu(item_no);
CREATE INDEX ix_sm1_sale ON sale_menu_1(seq_sale); CREATE INDEX ix_sm1_shop ON sale_menu_1(seq_shop);
CREATE INDEX ix_trans_date ON trans(menu_date); CREATE INDEX ix_transm_seq ON trans_menu(seq);
CREATE INDEX ix_sale_proj ON sale(proj_no); CREATE INDEX ix_shop_proj ON shop(proj_no); CREATE INDEX ix_resm_seq ON reserve_menu(seq); CREATE INDEX ix_cntm_seq ON count_menu(seq); CREATE INDEX ix_mreqm_seq ON mreq_menu(seq); CREATE INDEX ix_mreqi_no ON mreq_issue(mreq_no);
CREATE INDEX ix_preqm_seq ON preq_menu(seq); CREATE INDEX ix_shop_preq ON shop(preq_no); CREATE INDEX ix_wom_seq ON wo_menu(seq);
CREATE INDEX ix_logd_log ON app_log_detail(log_id); CREATE INDEX ix_log_at ON app_log(log_at);";

        public static bool Run(IWin32Window owner)
        {
            if (!Db.IsSql) { UI.Warn("يجب أولا الاتصال بخادم SQL Server."); return false; }
            string src = Settings.DataPath;
            using (OpenFileDialog d = new OpenFileDialog())
            {
                d.Title = "اختر ملف بيانات Access المراد نقله إلى الخادم";
                d.Filter = "ملفات البيانات (*.AMR;*.mdb)|*.AMR;*.mdb|كل الملفات|*.*";
                if (!string.IsNullOrEmpty(src) && File.Exists(src)) { d.InitialDirectory = Path.GetDirectoryName(src); d.FileName = Path.GetFileName(src); }
                if (d.ShowDialog(owner) != DialogResult.OK) return false;
                src = d.FileName;
            }
            if (!UI.Confirm("سيتم نقل كل البيانات من:\n" + src + "\n\nإلى الخادم:\n" + Db.Describe + "\n\nأي بيانات موجودة على الخادم ستُستبدل. متابعة؟")) return false;

            Form prog = new Form();
            prog.Text = "نقل البيانات"; prog.RightToLeft = RightToLeft.Yes; prog.Font = Theme.Font; prog.Size = new Size(520, 160);
            prog.FormBorderStyle = FormBorderStyle.FixedDialog; prog.ControlBox = false; prog.StartPosition = FormStartPosition.CenterScreen;
            Label lbl = new Label(); lbl.Dock = DockStyle.Top; lbl.Height = 50; lbl.TextAlign = ContentAlignment.MiddleCenter;
            ProgressBar bar = new ProgressBar(); bar.Dock = DockStyle.Top; bar.Height = 26; bar.Maximum = Order.Length + 2;
            prog.Controls.Add(bar); prog.Controls.Add(lbl);
            prog.Show(owner);
            Action<string> step = delegate(string s) { lbl.Text = s; bar.Value = Math.Min(bar.Maximum, bar.Value + 1); Application.DoEvents(); };

            try
            {
                UI.WaitCursor(true);
                List<string> report = Copy(src, step);
                prog.Close();
                UI.Info("تم نقل البيانات بنجاح ✓\n\n" + string.Join("\n", report.ToArray()));
                return true;
            }
            catch (Exception ex)
            {
                prog.Close();
                UI.Error(ex);
                return false;
            }
            finally { UI.WaitCursor(false); prog.Dispose(); }
        }

        /// <summary>Copies everything from the Access file into the connected SQL Server database.
        /// Returns one line per table with the row count check.</summary>
        public static List<string> Copy(string original, Action<string> step)
        {
            List<string> report = new List<string>();
            step("تجهيز نسخة مضغوطة ومُصلحة من الملف…");
            string temp1 = null, temp2 = null, src = original;
            try
            {
                // work on a compacted + repaired temporary copy: the original file is never touched,
                // and damaged records/indexes in old Access files are fixed before copying
                temp1 = Path.Combine(Path.GetTempPath(), "engstores_src_" + Guid.NewGuid().ToString("N") + ".mdb");
                temp2 = Path.Combine(Path.GetTempPath(), "engstores_fix_" + Guid.NewGuid().ToString("N") + ".mdb");
                using (FileStream a = new FileStream(original, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
                using (FileStream b = new FileStream(temp1, FileMode.Create, FileAccess.Write)) a.CopyTo(b);
                Type jt = Type.GetTypeFromProgID("JRO.JetEngine");
                if (jt != null)
                {
                    object je = Activator.CreateInstance(jt);
                    jt.InvokeMember("CompactDatabase", System.Reflection.BindingFlags.InvokeMethod, null, je, new object[] {
                        "Provider=Microsoft.Jet.OLEDB.4.0;Data Source=" + temp1,
                        "Provider=Microsoft.Jet.OLEDB.4.0;Data Source=" + temp2 + ";Jet OLEDB:Engine Type=5" });
                    src = temp2;
                    report.Add("تم ضغط وإصلاح نسخة من الملف قبل النقل (الملف الأصلي لم يتغير) ✓");
                }
                else src = temp1;
            }
            catch (Exception ex)
            {
                report.Add("تعذر ضغط نسخة الملف (" + ex.Message + ") — النقل من الملف مباشرة.");
                src = temp1 != null && File.Exists(temp1) ? temp1 : original;
            }
            try { return CopyFrom(src, report, step); }
            finally
            {
                OleDbConnection.ReleaseObjectPool();
                foreach (string t in new string[] { temp1, temp2 }) { try { if (t != null && File.Exists(t)) File.Delete(t); } catch { } }
            }
        }

        static List<string> CopyFrom(string src, List<string> report, Action<string> step)
        {
            string srcCs = "Provider=Microsoft.Jet.OLEDB.4.0;Data Source=" + src + ";Mode=Read;";
            {
                SqlConnection target = (SqlConnection)Db.Conn;
                step("إنشاء الجداول…");
                string[] all = { "sale_menu_1", "sale_menu", "sale", "shop_menu", "shop", "trans_menu", "trans", "item_stor", "item", "person", "stor", "type", "group", "pass", "disply_command", "app_log", "app_online", "project", "reserve", "reserve_menu", "count_head", "count_menu", "mreq", "mreq_menu", "mreq_issue", "preq", "preq_menu", "wo_head", "wo_menu", "app_settings", "app_blob", "app_log_detail" };
                foreach (string t in all) Exec(target, "IF OBJECT_ID('dbo.[" + t + "]','U') IS NOT NULL DROP TABLE dbo.[" + t + "]");
                foreach (string stmt in Schema.Split(';')) if (stmt.Trim().Length > 0) Exec(target, stmt);

                using (OleDbConnection s = new OleDbConnection(srcCs))
                {
                    s.Open();
                    foreach (string t in Order)
                    {
                        DataTable exists = s.GetOleDbSchemaTable(OleDbSchemaGuid.Tables, new object[] { null, null, t, "TABLE" });
                        if (exists == null || exists.Rows.Count == 0) { report.Add(t + ": غير موجود في المصدر (تم إنشاؤه فارغا)"); continue; }
                        step("نقل الجدول: " + t + " …");
                        DataTable targetCols = new DataTable();
                        using (SqlDataAdapter a = new SqlDataAdapter("SELECT TOP 0 * FROM [" + t + "]", target)) a.Fill(targetCols);
                        int srcCount;
                        using (OleDbCommand c = new OleDbCommand("SELECT COUNT(*) FROM [" + t + "]", s)) srcCount = Convert.ToInt32(c.ExecuteScalar());
                        using (OleDbCommand c = new OleDbCommand("SELECT * FROM [" + t + "]", s))
                        using (OleDbDataReader r = c.ExecuteReader())
                        using (SqlBulkCopy bc = new SqlBulkCopy(target, SqlBulkCopyOptions.KeepIdentity | SqlBulkCopyOptions.KeepNulls, null))
                        {
                            bc.DestinationTableName = "[" + t + "]";
                            bc.BulkCopyTimeout = 0;
                            bc.BatchSize = 5000;
                            for (int i = 0; i < r.FieldCount; i++)
                                if (targetCols.Columns.Contains(r.GetName(i))) bc.ColumnMappings.Add(r.GetName(i), r.GetName(i));
                            bc.WriteToServer(r);
                        }
                        int dst = Convert.ToInt32(Scalar(target, "SELECT COUNT(*) FROM [" + t + "]"));
                        report.Add(t + ": " + dst.ToString("#,##0") + (dst == srcCount ? " ✓" : " ✗ (المصدر " + srcCount + ")"));
                        if (dst != srcCount) throw new UserError("عدد السجلات في الجدول " + t + " غير مطابق بعد النقل.");
                    }
                }
                step("إنشاء الفهارس…");
                foreach (string stmt in Indexes.Split(';')) if (stmt.Trim().Length > 0) Exec(target, stmt);
                Db.EnsureAppTables();
                Db.Log(null, "نقل البيانات إلى الخادم", "من ملف Access");
            }
            return report;
        }

        static void Exec(SqlConnection c, string sql)
        {
            using (SqlCommand cmd = new SqlCommand(sql, c)) { cmd.CommandTimeout = 600; cmd.ExecuteNonQuery(); }
        }

        static object Scalar(SqlConnection c, string sql)
        {
            using (SqlCommand cmd = new SqlCommand(sql, c)) { cmd.CommandTimeout = 600; return cmd.ExecuteScalar(); }
        }
    }
}
