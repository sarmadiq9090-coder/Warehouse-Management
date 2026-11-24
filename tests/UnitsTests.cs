using System;
using System.Collections.Generic;
using System.Data;
using System.Drawing;
using System.Reflection;
using System.Windows.Forms;

namespace Stores
{
    /// <summary>
    /// Units of measure (item.unit): column added to an old file, item dialog saves it, cache, document grid,
    /// voucher and reports show it, Access → SQL Server migration keeps it.
    /// Usage: UnitsTests.exe &lt;copy.mdb&gt; &lt;out dir&gt; [&lt;sql instance, e.g. (localdb)\EngStoresTest&gt;]
    /// </summary>
    static class UnitsTestsMain
    {
        static int fails = 0;
        static void Check(bool ok, string what) { Console.WriteLine((ok ? "PASS  " : "FAIL  ") + what); if (!ok) fails++; }

        static object Field(object o, string name) { return o.GetType().GetField(name, BindingFlags.NonPublic | BindingFlags.Instance).GetValue(o); }

        /// <summary>Saves an item through the real item dialog (name, unit) and returns its number.</summary>
        static int SaveViaDialog(int id, string name, string unit)
        {
            using (ItemDialog d = new ItemDialog(id))
            {
                if (name != null) ((TextBox)Field(d, "txtName")).Text = name;
                ((ComboBox)Field(d, "cmbUnit")).Text = unit;
                bool ok = (bool)typeof(ItemDialog).GetMethod("SaveItem", BindingFlags.NonPublic | BindingFlags.Instance).Invoke(d, null);
                return ok ? (int)Field(d, "id") : 0;
            }
        }

        static int ColOf(ReportData r, string header) { for (int i = 0; i < r.Cols.Count; i++) if (r.Cols[i].Header == header) return i; return -1; }

        /// <summary>Every row has one value per column (catches a missed value after inserting a column).</summary>
        static bool RowsMatchCols(ReportData r) { foreach (object[] o in r.Rows) if (o.Length != r.Cols.Count) return false; return true; }

        static string RowUnit(ReportData r, int item)
        {
            int cu = ColOf(r, "الوحدة"), cn = ColOf(r, "اسم المادة");
            if (cu < 0 || cn < 0) return null;
            foreach (object[] o in r.Rows) if (Convert.ToString(o[cn]) == Cache.ItemName(item)) return Convert.ToString(o[cu]);
            return null;
        }

        [STAThread]
        static int Main(string[] args)
        {
            Console.OutputEncoding = System.Text.Encoding.UTF8;
            string mdb = args[0], outDir = args[1], sql = args.Length > 2 ? args[2] : null;
            string err = Db.InitAccess(mdb);
            if (err != null) { Console.WriteLine("open failed: " + err); return 1; }

            // ---- old file: the column is created additively
            bool before = Db.ColumnExists("item", "unit");
            Console.WriteLine("item.unit before EnsureAppTables: " + (before ? "exists" : "missing (old file)"));
            Db.EnsureAppTables();
            Check(Db.HasUnit && Db.ColumnExists("item", "unit"), "item.unit created on the Access file (" + (Db.SchemaWarning ?? "ok") + ")");
            Check(Db.I(Db.Scalar("SELECT COUNT(*) FROM item WHERE unit IS NOT NULL")) == 0 || before, "existing items start without a unit");
            Db.EnsureAppTables();
            Check(Db.HasUnit && Db.SchemaWarning == null, "running EnsureAppTables again is harmless");

            Session.UserNo = 1; Session.UserName = "1"; Session.LoadPerms(); Session.LoadMode();
            Cache.LoadAll();
            Check(Cache.Items.Columns.Contains("unit"), "Cache.Items has the unit column");
            Console.WriteLine("recalc fixed (historic mismatches): " + Posting.RecalcBalances());

            // ---- item dialog: new item with a unit, edit, clear, 20-character limit
            string name = "مادة اختبار الوحدات " + DateTime.Now.ToString("HHmmss");
            int id = SaveViaDialog(0, name, "لفة");
            Check(id > 0 && Db.S(Db.Scalar("SELECT unit FROM item WHERE item_no = ?", id)) == "لفة", "new item saved with unit «لفة» (item " + id + ")");
            Check(Cache.ItemUnit(id) == "لفة", "Cache.ItemUnit after save = «" + Cache.ItemUnit(id) + "»");
            using (ItemDialog d = new ItemDialog(id)) Check(((ComboBox)Field(d, "cmbUnit")).Text == "لفة", "item dialog shows the saved unit when editing");
            using (ItemDialog d = new ItemDialog(id)) Check(((ComboBox)Field(d, "cmbUnit")).Items.Contains("متر") && ((ComboBox)Field(d, "cmbUnit")).Items.Contains("قطعة"), "unit list offers the common Arabic units");
            Check(SaveViaDialog(id, null, "") == id && Db.Scalar("SELECT unit FROM item WHERE item_no = ?", id) is DBNull && Cache.ItemUnit(id) == "", "clearing the unit stores NULL");
            string free = "وحدة حرة طويلة جدا للاختبار ١٢٣٤٥";
            Check(SaveViaDialog(id, null, free) == id && Db.S(Db.Scalar("SELECT unit FROM item WHERE item_no = ?", id)) == free.Substring(0, 20), "free text unit kept (cut to 20 characters)");
            Check(SaveViaDialog(id, null, "متر") == id && Cache.ItemUnit(id) == "متر", "unit changed to «متر»");
            Check(Cache.ItemUnit(0) == "" && Cache.ItemUnit(-5) == "", "Cache.ItemUnit of an unknown item is empty");

            // ---- a receipt with this item: document grid and voucher
            int st = Cache.DefaultStore;
            int person = Db.I(Db.Scalar("SELECT TOP 1 custom_no FROM shop ORDER BY shop_no DESC"));
            Doc rc = new Doc(); rc.Kind = DocKind.Receipt; rc.Person = person; rc.Store = st; rc.RType = 2;
            rc.Lines.Add(new DocLine { ItemNo = id, Qty = 12 });
            Posting.Save(rc);
            Check(rc.Id > 0, "receipt saved (" + rc.No + ")");
            ReportData v = Reports.Voucher(Posting.Load(DocKind.Receipt, rc.Id));
            int vu = ColOf(v, "الوحدة"), vq = ColOf(v, "الكمية");
            Check(vu >= 0 && vu == vq - 1 && RowsMatchCols(v) && Convert.ToString(v.Rows[0][vu]) == "متر", "voucher: «الوحدة» column next to the quantity = متر");
            string vh = Html.Render(v);
            Check(vh.Contains("الوحدة") && vh.Contains(">متر<"), "printed voucher HTML contains the unit");
            System.IO.File.WriteAllText(System.IO.Path.Combine(outDir, "u_voucher.html"), vh);

            using (Form f = new Form())
            {
                f.StartPosition = FormStartPosition.Manual; f.Location = new Point(-3000, -3000); f.Size = new Size(1200, 700); f.ShowInTaskbar = false;
                DocView dv = new DocView(null, DocKind.Receipt, rc.Id, 0); dv.Dock = DockStyle.Fill;
                f.Controls.Add(dv); f.Show(); Application.DoEvents();
                DataGridView g = (DataGridView)Field(dv, "grid");
                DataGridViewColumn uc = g.Columns["unit"];
                Check(uc != null && uc.ReadOnly && uc.HeaderText == "الوحدة" && uc.DisplayIndex == g.Columns["qty"].DisplayIndex - 1, "document grid: read-only «الوحدة» column next to the quantity");
                Check(uc != null && Convert.ToString(g.Rows[0].Cells["unit"].FormattedValue) == "متر", "document grid shows the item's unit (" + (uc == null ? "" : Convert.ToString(g.Rows[0].Cells["unit"].FormattedValue)) + ")");
                f.Close();
            }

            // ---- reports
            ReportParams sp = new ReportParams(); sp.Text = name; sp.Flag = true;
            ReportData stock = Reports.Get("stock").Run(sp);
            Check(RowUnit(stock, id) == "متر" && RowsMatchCols(stock), "stock count (جرد أرصدة المواد) shows the unit");
            System.IO.File.WriteAllText(System.IO.Path.Combine(outDir, "u_stock.html"), Html.Render(stock));
            string[] withCol = { "stock", "critical", "stagnant", "negative", "issuedetail", "receiptdetail", "project", "topitems", "itemlist", "transfers", "person", "reservations", "purchreturns" };
            foreach (ReportDef def in Reports.All)
            {
                ReportParams p = new ReportParams(); p.From = DateTime.Today.AddYears(-2); p.To = DateTime.Today; p.ItemNo = id; p.PersonNo = person;
                p.ProjNo = Db.HasProj ? Db.I(Db.Scalar("SELECT TOP 1 proj_no FROM project")) : 0;
                try
                {
                    ReportData r = def.Run(p);
                    bool needs = Array.IndexOf(withCol, def.Key) >= 0;
                    bool ok = RowsMatchCols(r) && (!needs || ColOf(r, "الوحدة") >= 0);
                    Html.Render(r);
                    Check(ok, "report " + def.Key + ": rows=" + r.Rows.Count + (needs ? " has «الوحدة»" : "") + ", every row matches the columns");
                    if (def.Key == "card") Check(r.Subtitle.Contains("الوحدة: متر"), "item card subtitle shows the unit");
                    if (def.Key == "receiptdetail") Check(RowUnit(r, id) == "متر", "receipt detail row shows the unit");
                    if (def.Key == "itemlist") Check(RowUnit(r, id) == "متر", "item list row shows the unit");
                }
                catch (Exception ex) { Check(false, "report " + def.Key + ": " + ex.Message); }
            }

            // ---- clean up (copy only)
            Posting.Delete(Posting.Load(DocKind.Receipt, rc.Id));
            Db.InTx(delegate(System.Data.Common.DbTransaction tx)
            {
                Db.X(tx, "DELETE FROM item_stor WHERE item_no = ?", id);
                Db.X(tx, "DELETE FROM item WHERE item_no = ?", id);
            });
            Check(Posting.RecalcBalances() == 0, "balances consistent after the test");

            // ---- Access → SQL Server migration keeps the unit
            if (sql != null)
            {
                int it1 = Db.I(Db.Scalar("SELECT MIN(item_no) FROM item")), it2 = Db.I(Db.Scalar("SELECT MAX(item_no) FROM item"));
                Db.Exec("UPDATE item SET unit = ? WHERE item_no = ?", "قطعة", it1);
                Db.Exec("UPDATE item SET unit = ? WHERE item_no = ?", "م²", it2);
                int withUnit = Db.I(Db.Scalar("SELECT COUNT(*) FROM item WHERE unit IS NOT NULL"));
                Db.Close();
                const string dbName = "EngStoresUnitsTest";
                err = Db.InitSql(Db.BuildSqlConnStr(sql, "master", "", "", true));
                if (err != null) { Console.WriteLine("sql open failed: " + err); return 1; }
                Db.Exec("IF DB_ID('" + dbName + "') IS NOT NULL BEGIN ALTER DATABASE " + dbName + " SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE " + dbName + " END");
                Db.Exec("CREATE DATABASE " + dbName);
                err = Db.InitSql(Db.BuildSqlConnStr(sql, dbName, "", "", true));
                if (err != null) { Console.WriteLine("sql open failed: " + err); return 1; }
                List<string> rep = Migrate.Copy(mdb, delegate(string s) { });
                Check(rep.TrueForAll(l => l.Contains("✓") || l.Contains("غير موجود")), "migration: every table row count matches");
                Check(Db.S(Db.Scalar("SELECT unit FROM item WHERE item_no = ?", it1)) == "قطعة" && Db.S(Db.Scalar("SELECT unit FROM item WHERE item_no = ?", it2)) == "م²",
                      "migration keeps the units (قطعة / م²)");
                Check(Db.I(Db.Scalar("SELECT COUNT(*) FROM item WHERE unit IS NOT NULL")) == withUnit, "same number of items with a unit on the server (" + withUnit + ")");
                Check(Db.S(Db.Scalar("SELECT DATA_TYPE + ':' + CAST(CHARACTER_MAXIMUM_LENGTH AS VARCHAR(10)) FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME = 'item' AND COLUMN_NAME = 'unit'")) == "nvarchar:20",
                      "server column is NVARCHAR(20)");
                Db.EnsureAppTables(); Cache.LoadAll();
                Check(Db.HasUnit && Cache.ItemUnit(it1) == "قطعة", "SQL Server mode: cache reads the unit");
                int sid = SaveViaDialog(it2, null, "متر");
                Check(sid == it2 && Db.S(Db.Scalar("SELECT unit FROM item WHERE item_no = ?", it2)) == "متر", "SQL Server mode: item dialog saves the unit");
                ReportParams p2 = new ReportParams(); p2.Text = Cache.ItemName(it2); p2.Flag = true;
                Check(RowUnit(Reports.Get("stock").Run(p2), it2) == "متر", "SQL Server mode: stock report shows the unit");
                // a server database made before this version: the column is added by EnsureAppTables
                Db.Exec("ALTER TABLE item DROP COLUMN unit");
                Db.EnsureAppTables();
                Check(Db.HasUnit && Db.S(Db.Scalar("SELECT DATA_TYPE + ':' + CAST(CHARACTER_MAXIMUM_LENGTH AS VARCHAR(10)) FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME = 'item' AND COLUMN_NAME = 'unit'")) == "nvarchar:20",
                      "old server database gets item.unit NVARCHAR(20) (" + (Db.SchemaWarning ?? "ok") + ")");
                Db.Close();
                Db.InitSql(Db.BuildSqlConnStr(sql, "master", "", "", true));
                Db.Exec("ALTER DATABASE " + dbName + " SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE " + dbName);
            }

            Console.WriteLine(fails == 0 ? "\nALL TESTS PASSED" : "\nFAILURES: " + fails);
            Db.Close();
            return fails;
        }
    }
}
