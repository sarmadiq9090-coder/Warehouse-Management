using System.Collections.Generic;
using System.Data;
using System.Drawing.Drawing2D;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using System;

namespace Stores
{
    /// <summary>Home screen, kept simple (owner: "the main screen looks crowded"): a greeting, one big item search,
    /// one row of large action tiles, alert chips that appear only when something needs attention, and the latest
    /// documents. Advanced mode: more (compact) tiles and a small monthly chart beside the documents.</summary>
    class DashboardView : UserControl
    {
        MainForm main;

        public DashboardView(MainForm m)
        {
            main = m;
            BackColor = Theme.Bg;
            AutoScroll = true;
            bool adv = Session.Advanced;
            const int side = 4;   // the same small inset for every row, so all edges line up

            // ---- greeting
            Panel head = new Panel(); head.Dock = DockStyle.Top; head.Height = 62; head.Padding = new Padding(side, 4, side, 0);
            Label hello = new Label(); hello.Text = Greeting(); hello.Font = new Font(Theme.FontName, 18f, FontStyle.Bold); hello.ForeColor = Theme.Text;
            hello.Dock = DockStyle.Top; hello.Height = 36; hello.TextAlign = ContentAlignment.MiddleLeft; hello.AutoEllipsis = true;
            Label today = new Label(); today.Text = DateTime.Today.ToString("dddd  yyyy/MM/dd", new System.Globalization.CultureInfo("ar-IQ"));
            today.Font = Theme.Font; today.ForeColor = Theme.Muted; today.Dock = DockStyle.Top; today.Height = 22; today.TextAlign = ContentAlignment.MiddleLeft;
            head.Controls.Add(today); head.Controls.Add(hello);

            // ---- one big search box; the user chooses what to look for (remembered per user)
            Panel sp = new Panel(); sp.Dock = DockStyle.Top; sp.Height = 64; sp.Padding = new Padding(side, 8, side, 10);
            SearchBox search = new SearchBox(""); search.Dock = DockStyle.Right;
            string pick = "home_search_user" + Session.UserNo;
            int choice; int.TryParse(Db.GetSetting(pick, "0"), out choice);
            search.SetChoices(new[] { Lang.English ? "Item" : "مادة", "مستند", "مركز كلفة", "مجهز / جهة" },
                new[] { "كشف حساب مادة: اكتب الرمز أو الاسم…   (Ctrl+F)", "بحث عن مستند: الرقم أو مركز الكلفة أو المجهز أو الملاحظات…",
                        "كشف مركز كلفة: اكتب الاسم أو الرمز…", "كشف مجهز أو جهة: اكتب الاسم…" },
                choice, delegate(int i) { Db.SetSetting(pick, i.ToString()); });
            sp.Controls.Add(search);
            sp.Resize += delegate { search.Width = Math.Min(760, sp.ClientSize.Width - sp.Padding.Horizontal); };
            TextBox box = search.Box;
            Action<string> go = delegate(string t)
            {
                switch (search.Choice)
                {
                    case 1: main.FindDoc(t); break;
                    case 2: main.FindProject(t); break;
                    case 3: main.FindPerson(t); break;
                    default: main.QuickFind(t); break;
                }
            };
            box.KeyDown += delegate(object s, KeyEventArgs e) { if (e.KeyCode == Keys.Enter) { e.SuppressKeyPress = true; string t = box.Text.Trim(); box.Text = ""; go(t); } };
            box.TextChanged += delegate { if (box.Text.Length >= 2) { string t = box.Text; box.Text = ""; go(t); } };

            // ---- what to do: one row of large tiles (beginner: 4 explained tiles; advanced: up to 6 compact ones)
            Label ask = new Label(); ask.Text = "ماذا تريد أن تفعل؟"; ask.Font = Theme.FontSubtitle; ask.ForeColor = Theme.Text;
            ask.Dock = DockStyle.Top; ask.Height = 30; ask.TextAlign = ContentAlignment.MiddleLeft; ask.Padding = new Padding(side, 0, side, 0);
            List<ActionTile> tiles = new List<ActionTile>();
            AddTile(tiles, "", "إخراج مخزني", "تسليم مواد إلى مركز كلفة", Theme.Primary, Session.IssAdd, true, delegate { main.OpenDoc(DocKind.Issue, 0); });
            AddTile(tiles, "", "إدخال مخزني", "استلام مواد من مجهز", Theme.Accent, Session.RecAdd, true, delegate { main.OpenDoc(DocKind.Receipt, 0); });
            AddTile(tiles, "", "مردود إخراج مخزني", "مواد رجعت من مركز كلفة", Theme.Teal, Session.RecAdd, true, delegate { main.OpenDoc(DocKind.Return, 0); });
            if (adv) AddTile(tiles, "", "مردود مشتريات", "إرجاع مواد إلى المجهز", Theme.Warning, Session.RecAdd, true, delegate { main.OpenDoc(DocKind.PurchaseReturn, 0); });
            AddTile(tiles, "", "حجز مواد", "تثبيت كميات لمركز كلفة", Theme.Purple, Session.IssAdd, Db.HasProj, delegate { main.OpenDoc(DocKind.Reserve, 0); });
            if (!adv) AddTile(tiles, "", "مردود مشتريات", "إرجاع مواد إلى المجهز", Theme.Warning, Session.RecAdd, true, delegate { main.OpenDoc(DocKind.PurchaseReturn, 0); });
            if (adv) AddTile(tiles, "", "نقل مخزني", "نقل مواد بين المخازن", Theme.PrimaryDark, Session.Transfer, true, delegate { main.OpenDoc(DocKind.Transfer, 0); });
            // the user's own order (drag a tile onto another to swap them; saved per user)
            string orderKey = "tiles_user" + Session.UserNo + (adv ? "_adv" : "");
            List<string> order = new List<string>(Db.GetSetting(orderKey, "").Split('|'));
            // stable: without a saved order the tiles stay as listed above (List.Sort may reorder equal ones and drop a main tile)
            tiles = tiles.OrderBy(a => { int i = order.IndexOf(a.Title); return i < 0 ? 1000 : i; }).ToList();
            int max = adv ? 6 : 4;
            if (tiles.Count > max) tiles.RemoveRange(max, tiles.Count - max);
            ask.Text = "ماذا تريد أن تفعل؟   (اسحب أي بطاقة لتغيير ترتيبها)";
            Point downAt = Point.Empty;
            foreach (ActionTile tile in tiles)
            {
                ActionTile tl = tile;
                tl.AllowDrop = true;
                tl.MouseDown += delegate(object s, MouseEventArgs e) { if (e.Button == MouseButtons.Left) downAt = e.Location; };
                tl.MouseMove += delegate(object s, MouseEventArgs e)
                {
                    if (e.Button != MouseButtons.Left || downAt == Point.Empty) return;
                    if (Math.Abs(e.X - downAt.X) + Math.Abs(e.Y - downAt.Y) < 10) return;
                    downAt = Point.Empty;
                    tl.DoDragDrop(tl, DragDropEffects.Move);
                };
                tl.DragEnter += delegate(object s, DragEventArgs e) { e.Effect = e.Data.GetDataPresent(typeof(ActionTile)) ? DragDropEffects.Move : DragDropEffects.None; };
                tl.DragDrop += delegate(object s, DragEventArgs e)
                {
                    ActionTile src = e.Data.GetData(typeof(ActionTile)) as ActionTile;
                    if (src == null || src == tl) return;
                    List<string> now = new List<string>();
                    foreach (ActionTile x in tiles) now.Add(x.Title);
                    int i = now.IndexOf(src.Title), j = now.IndexOf(tl.Title);
                    now[i] = tl.Title; now[j] = src.Title;   // swap the two tiles
                    Db.SetSetting(orderKey, string.Join("|", now.ToArray()));
                    main.Show(new DashboardView(main));
                };
            }
            TableLayoutPanel row = new TableLayoutPanel(); row.Dock = DockStyle.Top; row.RowCount = 1; row.Height = adv ? 98 : 134;
            row.Padding = new Padding(0); row.ColumnCount = Math.Max(1, tiles.Count);
            row.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            for (int i = 0; i < tiles.Count; i++)
            {
                row.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f / tiles.Count));
                tiles[i].Compact = adv; tiles[i].Dock = DockStyle.Fill; tiles[i].Margin = new Padding(side, 0, side, 0);
                row.Controls.Add(tiles[i], i, 0);
            }
            if (tiles.Count == 0) { ask.Visible = false; row.Visible = false; }

            // ---- alerts: only the ones that are not zero (filled in on Load)
            FlowLayoutPanel alerts = new FlowLayoutPanel(); alerts.Dock = DockStyle.Top; alerts.Height = 50; alerts.WrapContents = false;
            alerts.FlowDirection = FlowDirection.LeftToRight; alerts.Padding = new Padding(side, 14, side, 0); alerts.Visible = false;
            AlertChip cCrit = Chip(alerts, "", Theme.Danger, "critical");
            AlertChip cNeg = Chip(alerts, "", Theme.Warning, "negative");
            AlertChip cResv = Chip(alerts, "", Theme.Purple, "reservations");

            // ---- latest documents (+ a small chart in advanced mode)
            Panel recentCard = UI.Card(); recentCard.Dock = DockStyle.Fill; recentCard.Margin = new Padding(side, 0, side, 0);
            Panel rh = new Panel(); rh.Dock = DockStyle.Top; rh.Height = 30; rh.BackColor = Theme.Card;
            Label rt = new Label(); rt.Text = "آخر السندات"; rt.Font = Theme.FontSubtitle; rt.ForeColor = Theme.Text; rt.Dock = DockStyle.Right; rt.AutoSize = true; rt.Padding = new Padding(0, 2, 0, 0);
            Label rhint = new Label(); rhint.Text = "انقر مرتين على سند لفتحه"; rhint.Font = Theme.FontSmall; rhint.ForeColor = Theme.Muted; rhint.Dock = DockStyle.Fill;
            rhint.TextAlign = ContentAlignment.MiddleRight;
            rh.Controls.Add(rhint); rh.Controls.Add(rt);
            DataGridView grid = new DataGridView(); grid.Dock = DockStyle.Fill; UI.ReadOnlyGrid(grid);
            grid.Columns.Add(UI.Col("k", "النوع", "kname", 0, false));
            grid.Columns.Add(UI.Col("no", "الرقم", "menu_no", 70, false));
            grid.Columns.Add(UI.Col("d", "التاريخ", "menu_date", 100, false));
            grid.Columns["d"].DefaultCellStyle.Format = "yyyy/MM/dd";
            grid.Columns.Add(UI.Col("p", "مركز الكلفة / الجهة", "pname", 0, false));
            grid.Columns["k"].FillWeight = 100; grid.Columns["k"].MinimumWidth = 140;   // «مردود إخراج مخزني» fits
            grid.Columns["p"].FillWeight = 220; grid.Columns["p"].MinimumWidth = 140;
            grid.ColumnHeadersDefaultCellStyle.WrapMode = DataGridViewTriState.False;
            Panel gap = new Panel(); gap.Dock = DockStyle.Top; gap.Height = 6; gap.BackColor = Theme.Card;
            recentCard.Controls.Add(grid); recentCard.Controls.Add(gap); recentCard.Controls.Add(rh);

            TableLayoutPanel bottom = new TableLayoutPanel(); bottom.Dock = DockStyle.Fill; bottom.RowCount = 1; bottom.Padding = new Padding(0, 16, 0, 0);
            bottom.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            MonthChart chart = null;
            if (adv)
            {
                bottom.ColumnCount = 2;
                bottom.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 62)); bottom.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 38));
                Panel chartCard = UI.Card(); chartCard.Dock = DockStyle.Fill; chartCard.Margin = new Padding(side, 0, side, 0);
                Label ct = new Label(); ct.Text = "السندات خلال آخر 12 شهرا"; ct.Font = Theme.FontSubtitle; ct.ForeColor = Theme.Text; ct.Dock = DockStyle.Top; ct.Height = 30;
                chart = new MonthChart(); chart.Dock = DockStyle.Fill;
                chartCard.Controls.Add(chart); chartCard.Controls.Add(ct);
                bottom.Controls.Add(recentCard, 0, 0); bottom.Controls.Add(chartCard, 1, 0);
            }
            else
            {
                bottom.ColumnCount = 1; bottom.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
                bottom.Controls.Add(recentCard, 0, 0);
            }

            Controls.Add(bottom); Controls.Add(alerts); Controls.Add(row); Controls.Add(ask); Controls.Add(sp); Controls.Add(head);
            // very small windows: scroll instead of squeezing the documents list to nothing
            int fixedH = head.Height + sp.Height + ask.Height + row.Height + alerts.Height + 16 + 150;
            AutoScrollMinSize = new Size(0, fixedH);

            Load += delegate
            {
                try
                {
                    int crit = Reports.CriticalItems(0, 0, 6).Count;
                    int neg = Db.I(Db.Scalar("SELECT COUNT(*) FROM item_stor WHERE item_bal < -0.0001"));
                    int resv = 0;
                    if (Db.HasProj) foreach (ResRow rr in Reservations.Get(0, 0)) if (rr.Outstanding > 0) resv++;
                    cCrit.Text = crit.ToString("#,##0") + "  أرصدة حرجة تحتاج تعزيز"; cCrit.Visible = crit > 0;
                    cNeg.Text = neg.ToString("#,##0") + "  أرصدة سالبة"; cNeg.Visible = neg > 0;
                    cResv.Text = resv.ToString("#,##0") + "  حجوزات قائمة"; cResv.Visible = resv > 0;
                    alerts.Visible = crit > 0 || neg > 0 || resv > 0;
                    if (RequisitionsView.AddAlert(alerts, main)) alerts.Visible = true;   // «طلبات صرف بانتظار موافقتك»
                    if (WriteOffsView.AddAlert(alerts, main)) alerts.Visible = true;     // «سندات إتلاف بانتظار اعتمادك»

                    string kSale = Db.HasProj ? "IIF(doc_kind = 1, 4, 1)" : "1";
                    string pSale = Db.HasProj ? "proj_no" : "0";
                    string pShop = Db.HasProj ? "proj_no" : "0";
                    DataTable rec = Db.Query(
                        "SELECT TOP 25 k, id, menu_no, menu_date, custom_no, pj FROM (" +
                        "SELECT " + kSale + " AS k, shop_no AS id, menu_no, menu_date, custom_no, " + pSale + " AS pj FROM sale WHERE " + Db.NotCountAdj("") + " UNION ALL " +
                        "SELECT IIF(shop_type = 3, 3, 2) AS k, shop_no AS id, menu_no, menu_date, custom_no, " + pShop + " AS pj FROM shop WHERE " + Db.NotCountAdj("") + ") AS u ORDER BY menu_date DESC, id DESC");
                    rec.Columns.Add("kname", typeof(string)); rec.Columns.Add("pname", typeof(string));
                    foreach (DataRow r in rec.Rows)
                    {
                        int k = Db.I(r["k"]);
                        r["kname"] = k == 1 ? "إخراج مخزني" : k == 2 ? "إدخال مخزني" : k == 3 ? "مردود إخراج مخزني" : "مردود مشتريات";
                        int pj = Db.I(r["pj"]);
                        // issues and issue returns: the cost centre is the recipient («المستلم = مركز الكلفة»)
                        bool cc = pj > 0 && (k == 1 || k == 3) && Db.HasCcPerson;
                        r["pname"] = cc ? Cache.Project(pj) : pj > 0 ? Cache.Project(pj) + " • " + Cache.Person(Db.I(r["custom_no"])) : Cache.Person(Db.I(r["custom_no"]));
                    }
                    grid.DataSource = rec;
                    grid.CellDoubleClick += delegate(object s, DataGridViewCellEventArgs e)
                    {
                        if (e.RowIndex < 0) return;
                        DataRowView rv = (DataRowView)grid.Rows[e.RowIndex].DataBoundItem;
                        int k = Db.I(rv["k"]);
                        main.OpenDoc(k == 1 ? DocKind.Issue : k == 2 ? DocKind.Receipt : k == 3 ? DocKind.Return : DocKind.PurchaseReturn, Db.I(rv["id"]));
                    };

                    if (chart != null)
                    {
                        DateTime m0 = new DateTime(DateTime.Today.Year, DateTime.Today.Month, 1), from = m0.AddMonths(-11);
                        chart.Load(Db.Query("SELECT YEAR(menu_date) AS y, MONTH(menu_date) AS m, COUNT(*) AS n FROM sale AS h WHERE " + Db.IssueOnly("h") + " AND menu_date >= ? GROUP BY YEAR(menu_date), MONTH(menu_date)", from),
                                   Db.Query("SELECT YEAR(menu_date) AS y, MONTH(menu_date) AS m, COUNT(*) AS n FROM shop WHERE " + Db.NotCountAdj("") + " AND menu_date >= ? AND menu_date < ? GROUP BY YEAR(menu_date), MONTH(menu_date)", from, m0.AddMonths(1)),
                                   from);
                    }
                }
                catch (Exception ex) { UI.Error(ex); }
            };
        }

        void AddTile(List<ActionTile> list, string glyph, string title, string desc, Color c, int perm, bool on, Action act)
        {
            if (!on || (perm != 0 && !Session.Can(perm))) return;
            ActionTile t = new ActionTile(glyph, title, desc, c);
            t.Click += delegate { act(); };
            list.Add(t);
        }

        AlertChip Chip(FlowLayoutPanel host, string glyph, Color c, string report)
        {
            AlertChip a = new AlertChip(glyph, c); a.Visible = false;
            a.Click += delegate { main.OpenReport(report, 0, 0); };
            host.Controls.Add(a);
            return a;
        }

        /// <summary>«أهلا بك، اسم»; the bare greeting when the login name is only a number (e.g. «1»).</summary>
        static string Greeting()
        {
            string n = (Session.UserName ?? "").Trim();
            bool number = true;
            foreach (char ch in n) if (!char.IsDigit(ch)) { number = false; break; }
            return n.Length == 0 || number ? "أهلا بك" : "أهلا بك، " + n;
        }
    }
}
