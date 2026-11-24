using System.Collections.Generic;
using System.Data;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using System;

namespace Stores
{
    /// <summary>Find a document by its number, its cost centre / supplier or its notes (home screen, both modes).</summary>
    class DocFindDialog : BaseDialog
    {
        readonly TextBox txt; readonly DataGridView grid; readonly Label count; readonly Timer timer = new Timer();
        public DocKind Kind; public int Id;

        public DocFindDialog(string initial)
        {
            Text = "بحث عن مستند";
            Size = new Size(960, 560); FormBorderStyle = FormBorderStyle.Sizable; MinimumSize = new Size(640, 400);
            Panel top = new Panel(); top.Dock = DockStyle.Top; top.Height = 56; top.Padding = new Padding(12, 12, 12, 8); top.BackColor = Theme.Card;
            txt = UI.Text(400); txt.Dock = DockStyle.Fill; txt.Font = new Font(Theme.FontName, 12f);
            Label l = UI.Lbl("رقم السند أو مركز الكلفة أو المجهز أو الملاحظات:"); l.Dock = DockStyle.Right; l.Margin = new Padding(0); l.Padding = new Padding(6, 6, 6, 0);
            top.Controls.Add(txt); top.Controls.Add(l);
            grid = new DataGridView(); grid.Dock = DockStyle.Fill; UI.ReadOnlyGrid(grid);
            grid.Columns.Add(UI.Col("k", "النوع", "kname", 150, false));
            grid.Columns.Add(UI.Col("no", "الرقم", "menu_no", 90, false));
            grid.Columns.Add(UI.Col("d", "التاريخ", "menu_date", 110, false)); grid.Columns["d"].DefaultCellStyle.Format = "yyyy/MM/dd";
            grid.Columns.Add(UI.Col("p", "مركز الكلفة / الجهة", "pname", 0, false));
            grid.Columns.Add(UI.Col("s", "المخزن", "sname", 130, false));
            grid.Columns.Add(UI.Col("n", "ملاحظات", "note", 180, false));
            count = UI.Lbl("");
            Button ok = UI.Btn("فتح المستند", Theme.Primary, 130), view = UI.BtnLight("معاينة", 100), cancel = UI.BtnLight("إلغاء", 90);
            FlowLayoutPanel bar = ButtonBar(ok, view, cancel); bar.Controls.Add(count);
            Controls.Add(grid); Controls.Add(top); Controls.Add(bar);

            timer.Interval = 300; timer.Tick += delegate { timer.Stop(); Run(); };
            txt.TextChanged += delegate { timer.Stop(); timer.Start(); };
            txt.KeyDown += delegate(object s, KeyEventArgs e)
            {
                if (e.KeyCode == Keys.Enter) { e.Handled = e.SuppressKeyPress = true; if (timer.Enabled) { timer.Stop(); Run(); } if (grid.Rows.Count == 1 || (grid.Rows.Count > 0 && ranFor == txt.Text)) Choose(); ranFor = txt.Text; }
                else if ((e.KeyCode == Keys.Down || e.KeyCode == Keys.Up) && grid.Rows.Count > 0)
                {
                    int i = grid.CurrentRow == null ? 0 : grid.CurrentRow.Index + (e.KeyCode == Keys.Down ? 1 : -1);
                    grid.CurrentCell = grid.Rows[Math.Max(0, Math.Min(grid.Rows.Count - 1, i))].Cells[0];
                    ranFor = txt.Text; e.Handled = true;
                }
            };
            grid.CellDoubleClick += delegate(object s, DataGridViewCellEventArgs e) { if (e.RowIndex >= 0) Choose(); };
            grid.KeyDown += delegate(object s, KeyEventArgs e) { if (e.KeyCode == Keys.Enter) { e.Handled = true; Choose(); } };
            ok.Click += delegate { Choose(); };
            view.Click += delegate
            {
                DataRowView rv = Current; if (rv == null) return;
                Doc d = Posting.Load((DocKind)Db.I(rv["k"]), Db.I(rv["id"]));
                if (d != null) PrintPreviewForm.ShowVoucher(this, d);
            };
            cancel.Click += delegate { DialogResult = DialogResult.Cancel; Close(); };
            txt.Text = initial ?? ""; timer.Stop();
            Load += delegate { Run(); ranFor = txt.Text; txt.SelectionStart = txt.TextLength; txt.Focus(); };
            FormClosed += delegate { timer.Dispose(); };
        }

        string ranFor;      // the text Enter was pressed for (a second Enter opens the selected row)

        DataRowView Current { get { return grid.CurrentRow == null ? null : grid.CurrentRow.DataBoundItem as DataRowView; } }

        void Run()
        {
            try
            {
                DataTable t = Find(txt.Text);
                grid.DataSource = t;
                count.Text = txt.Text.Trim().Length == 0 ? "آخر السندات" : t.Rows.Count == 0 ? "لا يوجد مستند مطابق" : "النتائج: " + t.Rows.Count;
            }
            catch (Exception ex) { UI.Error(ex); }
        }

        void Choose()
        {
            DataRowView rv = Current; if (rv == null) return;
            Kind = (DocKind)Db.I(rv["k"]); Id = Db.I(rv["id"]);
            DialogResult = DialogResult.OK; Close();
        }

        static string Ids(DataTable t, string idCol, string[] words)
        {
            if (t == null || words.Length == 0) return "0";
            List<string> ids = new List<string>();
            foreach (DataRow r in t.Rows)
            {
                string s = Db.S(r["_s"]);
                if (words.All(w => s.IndexOf(w, StringComparison.Ordinal) >= 0)) { ids.Add(Db.I(r[idCol]).ToString()); if (ids.Count >= 1000) break; }
            }
            return ids.Count == 0 ? "0" : string.Join(",", ids.ToArray());
        }

        /// <summary>Documents the user may see that match the text (number, cost centre / party name, notes); newest first.
        /// Empty text: the latest documents.</summary>
        public static DataTable Find(string text)
        {
            text = UI.NormDigits((text ?? "").Trim());
            string[] words = UI.Norm(text).Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            string persons = Ids(Cache.Persons, "p_no", words), projects = Db.HasProj ? Ids(Cache.Projects, "proj_no", words) : "0";
            string like = "%" + text.Replace("[", "[[]").Replace("%", "[%]").Replace("_", "[_]") + "%";
            DataTable res = new DataTable();
            foreach (string c in new[] { "kname", "menu_no", "pname", "sname", "note" }) res.Columns.Add(c, typeof(string));
            res.Columns.Add("menu_date", typeof(DateTime)); res.Columns.Add("k", typeof(int)); res.Columns.Add("id", typeof(int));

            // may: the part of this table the user may see ("" = all, null = nothing)
            Action<string, string, string, string, string, Func<DataRow, DocKind>, bool, string> part = delegate(string table, string idCol, string dateCol, string noteCol, string cols, Func<DataRow, DocKind> kind, bool party, string may)
            {
                if (may == null) return;
                HashSet<int> seen = new HashSet<int>();
                Action<string, object[]> run = delegate(string w, object[] a)
                {
                    if (may.Length > 0) w += " AND " + may;
                    if (table == "sale" || table == "shop") w += " AND " + Db.NotCountAdj("h");
                    foreach (DataRow r in Db.Query("SELECT TOP 100 h." + idCol + " AS id, h.menu_no, h." + dateCol + " AS d, " + noteCol + " AS nt, " + cols + " FROM " + table + " AS h WHERE " + w + " ORDER BY h." + dateCol + " DESC, h." + idCol + " DESC", a).Rows)
                    {
                        DocKind k = kind(r);
                        if (!seen.Add(Db.I(r["id"])) || !Session.Can(MainForm.ViewPerm(k))) continue;
                        int pj = Db.I(r["pj"]), pe = Db.I(r["pe"]);
                        string who = k == DocKind.Transfer ? "إلى: " + Cache.Store(pe) : pj > 0 && (Posting.CcIsRecipient(k) || pe == 0) ? Cache.Project(pj) : pj > 0 ? Cache.Project(pj) + " • " + Cache.Person(pe) : Cache.Person(pe);
                        res.Rows.Add(Posting.KindName(k), Db.S(r["menu_no"]), who, Cache.Store(Db.I(r["st"])), Db.S(r["nt"]), Db.Dt(r["d"]), (int)k, Db.I(r["id"]));
                    }
                };
                if (text.Length == 0) { run("1=1", new object[0]); return; }
                run("h.menu_no = ?", new object[] { text });       // the number itself is never pushed out by the newest 100
                run("(" + noteCol + " LIKE ?" + (party ? " OR h.custom_no IN (" + persons + ")" : "") + (cols.Contains("h.proj_no AS pj") ? " OR h.proj_no IN (" + projects + ")" : "") + ")", new object[] { like });
            };
            string pjCol = Db.HasProj ? "h.proj_no AS pj" : "0 AS pj";
            bool iss = Session.Can(MainForm.ViewPerm(DocKind.Issue)), pret = Session.Can(MainForm.ViewPerm(DocKind.PurchaseReturn));
            part("sale", "shop_no", "menu_date", "h.[note]", "h.custom_no AS pe, " + pjCol + ", h.stor AS st, " + (Db.HasProj ? "h.doc_kind AS x" : "0 AS x"), r => Db.I(r["x"]) == 1 ? DocKind.PurchaseReturn : DocKind.Issue, true,
                 iss && pret ? "" : !Db.HasProj ? (iss ? "" : null) : iss ? "(h.doc_kind IS NULL OR h.doc_kind <> 1)" : pret ? "h.doc_kind = 1" : null);
            part("shop", "shop_no", "menu_date", "h.[note]", "h.custom_no AS pe, " + pjCol + ", h.stor AS st, h.shop_type AS x", r => Db.I(r["x"]) == 3 ? DocKind.Return : DocKind.Receipt, true, Session.Can(MainForm.ViewPerm(DocKind.Receipt)) ? "" : null);
            part("trans", "shop_no", "menu_date", "h.notes", "h.stor_in AS pe, 0 AS pj, h.stor_out AS st, 0 AS x", r => DocKind.Transfer, false, Session.Can(MainForm.ViewPerm(DocKind.Transfer)) ? "" : null);
            if (Db.HasProj) part("reserve", "res_no", "res_date", "h.notes", "0 AS pe, h.proj_no AS pj, h.stor AS st, 0 AS x", r => DocKind.Reserve, false, Session.Can(MainForm.ViewPerm(DocKind.Reserve)) ? "" : null);

            // the exact number first, then the newest
            DataView v = res.DefaultView;
            res.Columns.Add("o", typeof(int));
            foreach (DataRow r in res.Rows) r["o"] = text.Length > 0 && Db.S(r["menu_no"]) == text ? 0 : 1;
            v.Sort = "o, menu_date DESC, id DESC";
            return v.ToTable();
        }
    }
}
