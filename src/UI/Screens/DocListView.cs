using System.Collections.Generic;
using System.Data;
using System.Drawing;
using System.Windows.Forms;
using System;

namespace Stores
{
    // ================================================================== documents register
    class DocListView : UserControl
    {
        MainForm main;
        ComboBox cmbKind; DateTimePicker d1, d2; PersonPicker person; TextBox txt; DataGridView grid; Label lblCount;
        DataTable data;

        public DocListView(MainForm m)
        {
            main = m;
            BackColor = Theme.Bg;
            Label title = new Label(); title.Text = "سجل السندات"; title.Font = Theme.FontTitle; title.ForeColor = Theme.Primary; title.Dock = DockStyle.Top; title.Height = 40;

            Panel filt = UI.Card(); filt.Dock = DockStyle.Top; filt.Height = 64;
            FlowLayoutPanel f = UI.Flow(); f.WrapContents = true;
            cmbKind = UI.Combo(185);
            List<ComboItem> kinds = new List<ComboItem>();
            if (Session.Can(Session.IssView)) kinds.Add(new ComboItem(0, "الإخراج المخزني"));
            if (Session.Can(Session.RecView)) { kinds.Add(new ComboItem(1, "الإدخال المخزني")); kinds.Add(new ComboItem(2, "مردود الإخراج المخزني")); kinds.Add(new ComboItem(4, "مردود المشتريات")); }
            if (Session.Can(Session.IssView) && Db.HasProj) kinds.Add(new ComboItem(5, "حجوزات مراكز الكلفة"));
            if (Session.Can(Session.Transfer)) kinds.Add(new ComboItem(3, "النقل المخزني"));
            cmbKind.DisplayMember = "Name"; cmbKind.DataSource = kinds;
            DateTime today = DateTime.Today;
            d1 = UI.DatePicker(new DateTime(today.Year, today.Month, 1)); d2 = UI.DatePicker(today);
            person = new PersonPicker(240);
            txt = UI.Text(150);
            Button go = UI.Btn("عرض", Theme.Primary, 90); go.Height = 30;
            Button xl = UI.BtnLight("تصدير Excel", 110); xl.Height = 30;
            f.Controls.AddRange(new Control[] { UI.Field("النوع", cmbKind), UI.Field("من", d1), UI.Field("إلى", d2), UI.Field("الجهة", person), UI.Field("رقم/ملاحظة", txt), go, xl });
            filt.Controls.Add(f); UI.FitHeight(filt);

            Panel gc = UI.Card(); gc.Dock = DockStyle.Fill; gc.Padding = new Padding(1);
            grid = new DataGridView(); grid.Dock = DockStyle.Fill; UI.ReadOnlyGrid(grid);
            grid.Columns.Add(UI.Col("no", "الرقم", "menu_no", 90, false));
            grid.Columns.Add(UI.Col("d", "التاريخ", "menu_date", 110, false)); grid.Columns["d"].DefaultCellStyle.Format = "yyyy/MM/dd";
            grid.Columns.Add(UI.Col("t", "النوع", "tname", 100, false));
            grid.Columns.Add(UI.Col("p", "مركز الكلفة / الجهة", "pname", 300, false));
            grid.Columns.Add(UI.Col("s", "المخزن", "sname", 140, false));
            grid.Columns.Add(UI.Col("n", "عدد المواد", "n", 100, true));
            grid.Columns.Add(UI.Col("q", "مجموع الكميات", "q", 130, true));
            grid.Columns.Add(UI.Col("note", "ملاحظات", "note", 0, false));
            // every column stretches with the window but never below its header text: no horizontal scrolling at the minimum window size
            int[] minW = { 70, 95, 120, 160, 60, 100, 125, 60 };
            for (int i = 0; i < grid.Columns.Count; i++)
            {
                DataGridViewColumn c = grid.Columns[i];
                c.FillWeight = i == 3 ? 260 : i == 7 ? 140 : c.Width;
                c.AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill; c.MinimumWidth = minW[i];
            }
            gc.Controls.Add(grid);
            lblCount = new Label(); lblCount.Dock = DockStyle.Bottom; lblCount.Height = 26; lblCount.ForeColor = Theme.Muted; lblCount.TextAlign = ContentAlignment.MiddleLeft;

            Controls.Add(gc); Controls.Add(lblCount); Controls.Add(filt); Controls.Add(title);

            go.Click += delegate { Run(); };
            txt.KeyDown += delegate(object s, KeyEventArgs e) { if (e.KeyCode == Keys.Enter) { e.SuppressKeyPress = true; Run(); } };
            xl.Click += delegate { if (data != null) Excel.ExportGrid(FindForm(), grid, "سجل السندات"); };
            grid.CellDoubleClick += delegate(object s, DataGridViewCellEventArgs e)
            {
                if (e.RowIndex < 0) return;
                DataRowView rv = (DataRowView)grid.Rows[e.RowIndex].DataBoundItem;
                int k = UI.Sel(cmbKind);
                main.OpenDoc(KindOf(k), Db.I(rv["shop_no"]));
            };
            Load += delegate { if (cmbKind.Items.Count > 0) Run(); };
        }

        static DocKind KindOf(int k)
        {
            switch (k) { case 0: return DocKind.Issue; case 1: return DocKind.Receipt; case 2: return DocKind.Return; case 3: return DocKind.Transfer; case 4: return DocKind.PurchaseReturn; default: return DocKind.Reserve; }
        }

        void Run()
        {
            if (cmbKind.Items.Count == 0) return;      // no right to see any kind
            try
            {
                UI.WaitCursor(true);
                int k = UI.Sel(cmbKind);
                DateTime a = d1.Value.Date, b = d2.Value.Date.AddDays(1);
                string sql, noteCol = "h.[note]", idCol = "h.shop_no", dateCol = "h.menu_date";
                List<object> args = new List<object>();
                if (k == 3)
                {
                    noteCol = "h.notes";
                    sql = "SELECT h.shop_no, h.menu_no, h.menu_date, 0 AS custom_no, 0 AS proj_no, h.stor_out AS stor, h.stor_in, h.notes AS [note], 0 AS st, " +
                          "(SELECT COUNT(*) FROM trans_menu m WHERE m.seq = h.shop_no) AS n, (SELECT SUM(qty) FROM trans_menu m WHERE m.seq = h.shop_no) AS q " +
                          "FROM trans h WHERE h.menu_date >= ? AND h.menu_date < ?";
                }
                else if (k == 5)
                {
                    noteCol = "h.notes"; idCol = "h.res_no"; dateCol = "h.res_date";
                    sql = "SELECT h.res_no AS shop_no, h.menu_no, h.res_date AS menu_date, 0 AS custom_no, h.proj_no, h.stor, 0 AS stor_in, h.notes AS [note], h.status AS st, " +
                          "(SELECT COUNT(*) FROM reserve_menu m WHERE m.seq = h.res_no) AS n, (SELECT SUM(qty) FROM reserve_menu m WHERE m.seq = h.res_no) AS q " +
                          "FROM reserve h WHERE h.res_date >= ? AND h.res_date < ?";
                }
                else
                {
                    bool sale = k == 0 || k == 4;
                    string t = sale ? "sale" : "shop", l = sale ? "sale_menu" : "shop_menu";
                    sql = "SELECT h.shop_no, h.menu_no, h.menu_date, h.custom_no, " + (Db.HasProj ? "h.proj_no" : "0 AS proj_no") + ", h.stor, 0 AS stor_in, h.[note], " + (sale ? "0" : "h.shop_type") + " AS st, " +
                          "(SELECT COUNT(*) FROM " + l + " m WHERE m.seq = h.shop_no) AS n, (SELECT SUM(qty) FROM " + l + " m WHERE m.seq = h.shop_no) AS q " +
                          "FROM " + t + " h WHERE h.menu_date >= ? AND h.menu_date < ?";
                    if (k == 0) sql += " AND " + Db.IssueOnly("h");
                    if (k == 4) sql += Db.HasProj ? " AND h.doc_kind = 1" : " AND 1=0";
                    if (k == 1) sql += " AND h.shop_type <> 3 AND " + Db.NotCountAdj("h") + " AND " + Db.MainShop("h");
                    if (k == 2) sql += " AND h.shop_type = 3";
                    if (person.Value != 0) sql += " AND h.custom_no = ?";
                }
                args.Add(a); args.Add(b);
                if (k != 3 && k != 5 && person.Value != 0) args.Add(person.Value);
                string s = UI.NormDigits(txt.Text.Trim());
                if (s.Length > 0) { sql += " AND (h.menu_no = ? OR " + noteCol + " LIKE ?)"; args.Add(s); args.Add(Db.Like(s)); }
                sql += " ORDER BY " + dateCol + " DESC, " + idCol + " DESC";
                data = Db.Query(sql, args.ToArray());
                Dictionary<int, Posting.GroupInfo> groups = k == 1 ? Posting.Groups() : new Dictionary<int, Posting.GroupInfo>();   // receipts into several stores
                if (groups.Count > 0) data.Columns["n"].ReadOnly = data.Columns["q"].ReadOnly = false;
                data.Columns.Add("pname", typeof(string)); data.Columns.Add("sname", typeof(string)); data.Columns.Add("tname", typeof(string));
                foreach (DataRow r in data.Rows)
                {
                    int pj = Db.I(r["proj_no"]);
                    if (k == 3) r["pname"] = "إلى: " + Cache.Store(Db.I(r["stor_in"]));
                    else if (k == 5) r["pname"] = Cache.Project(pj);
                    else if (pj > 0 && (k == 0 || k == 2) && Db.HasCcPerson) r["pname"] = Cache.Project(pj);   // «المستلم = مركز الكلفة»
                    else r["pname"] = pj > 0 ? Cache.Project(pj) + " • " + Cache.Person(Db.I(r["custom_no"])) : Cache.Person(Db.I(r["custom_no"]));
                    r["sname"] = Cache.Store(Db.I(r["stor"]));
                    Posting.GroupInfo gi;
                    if (groups.TryGetValue(Db.I(r["shop_no"]), out gi)) { r["sname"] = gi.Stores; r["n"] = gi.Lines; r["q"] = gi.Qty; }
                    int st = Db.I(r["st"]);
                    r["tname"] = k == 0 ? "إخراج مخزني" : k == 3 ? "نقل مخزني" : k == 4 ? "مردود مشتريات" : k == 5 ? (st == 0 ? "حجز ملغى" : "حجز") : st == 3 ? "مردود إخراج مخزني" : Cache.ReceiptTypes[Math.Max(0, Math.Min(3, st))];
                }
                grid.DataSource = data;
                lblCount.Text = "عدد السندات: " + data.Rows.Count + "    •    انقر مرتين على السند لفتحه";
            }
            catch (Exception ex) { UI.Error(ex); }
            finally { UI.WaitCursor(false); }
        }
    }
}
