using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;
using System;

namespace Stores
{
    /// <summary>
    /// Result list that opens under the item search box ON the document page (a child control drawn over the grid,
    /// not a window): code, name, unit and the available quantity of each item. Keyboard stays in the search box;
    /// the box moves the selection with Move(), a click picks.
    /// </summary>
    class ItemDropDown : Panel
    {
        const int RowH = 30, HeadH = 28, FootH = 24, MaxRows = 10;
        const int CodeW = 140, UnitW = 80, AvailW = 140;

        readonly ListBox list;
        readonly Panel head;
        readonly Label foot;
        List<int> ids = new List<int>();

        public Func<int, double> Avail;        // available quantity of an item in the document's store
        public Func<int, double> InDoc;        // quantity of the item already in the document
        public bool WarnShort;                 // out documents: red when nothing is left
        public string AvailTitle = "المتوفر";
        public event Action<int> Picked;

        public ItemDropDown()
        {
            Visible = false;
            BackColor = Theme.Primary;
            Padding = new Padding(1);
            RightToLeft = RightToLeft.Yes;

            head = new Panel(); head.Dock = DockStyle.Top; head.Height = HeadH; head.BackColor = Theme.HeadTint;
            head.Paint += Head_Paint;
            foot = new Label(); foot.Dock = DockStyle.Bottom; foot.Height = FootH; foot.BackColor = Theme.FootTint;
            foot.ForeColor = Theme.Muted; foot.Font = Theme.FontSmall; foot.TextAlign = ContentAlignment.MiddleLeft; foot.Padding = new Padding(8, 0, 8, 0);

            list = new ListBox();
            list.Dock = DockStyle.Fill;
            list.BorderStyle = BorderStyle.None;
            list.DrawMode = DrawMode.OwnerDrawFixed;
            list.ItemHeight = RowH;
            list.IntegralHeight = false;
            list.Font = Theme.Font;
            list.TabStop = false;
            list.RightToLeft = RightToLeft.Yes;
            list.DrawItem += List_DrawItem;
            list.MouseMove += delegate(object s, MouseEventArgs e)
            {
                int i = list.IndexFromPoint(e.Location);
                if (i >= 0 && i != list.SelectedIndex) list.SelectedIndex = i;
            };
            list.MouseDown += delegate(object s, MouseEventArgs e)
            {
                int i = list.IndexFromPoint(e.Location);
                if (e.Button == MouseButtons.Left && i >= 0 && i < ids.Count && Picked != null) Picked(ids[i]);
            };

            Panel inner = new Panel(); inner.Dock = DockStyle.Fill; inner.BackColor = Theme.Card;
            inner.Controls.Add(list); inner.Controls.Add(head); inner.Controls.Add(foot);
            Controls.Add(inner);
        }

        public int Count { get { return ids.Count; } }
        public int SelectedId { get { int i = list.SelectedIndex; return i >= 0 && i < ids.Count ? ids[i] : 0; } }
        public List<int> Ids { get { return ids; } }

        /// <summary>Height for the given number of rows (at most 10), within maxHeight (small screens: fewer rows, it scrolls).</summary>
        public int HeightFor(int rows, int maxHeight)
        {
            int fit = Math.Max(3, (maxHeight - HeadH - FootH - 2) / RowH);
            return HeadH + FootH + 2 + RowH * Math.Max(1, Math.Min(Math.Min(MaxRows, fit), rows));
        }

        public void SetItems(List<int> items, string footer)
        {
            ids = items ?? new List<int>();
            list.BeginUpdate();
            list.Items.Clear();
            foreach (int id in ids) list.Items.Add(id);
            if (ids.Count > 0) list.SelectedIndex = 0;
            list.EndUpdate();
            foot.Text = footer ?? "";
            head.Invalidate();
        }

        public void MoveSelection(int delta)
        {
            if (ids.Count == 0) return;
            int i = list.SelectedIndex < 0 ? 0 : list.SelectedIndex + delta;
            list.SelectedIndex = Math.Max(0, Math.Min(ids.Count - 1, i));
        }

        // ------------------------------------------------------------ drawing (columns from the right: code | name | unit | available)
        static Rectangle Cell(Rectangle b, int col)
        {
            int nameW = Math.Max(80, b.Width - CodeW - UnitW - AvailW);
            switch (col)
            {
                case 0: return Lang.Mirror(new Rectangle(b.Right - CodeW, b.Top, CodeW, b.Height), b);
                case 1: return Lang.Mirror(new Rectangle(b.Right - CodeW - nameW, b.Top, nameW, b.Height), b);
                case 2: return Lang.Mirror(new Rectangle(b.Right - CodeW - nameW - UnitW, b.Top, UnitW, b.Height), b);
                default: return Lang.Mirror(new Rectangle(b.Left, b.Top, Math.Max(0, b.Right - CodeW - nameW - UnitW - b.Left), b.Height), b);
            }
        }

        static void Draw(Graphics g, string s, Font f, Rectangle r, Color c, bool rtl)
        {
            r.Inflate(-8, 0);
            TextFormatFlags fl = TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix | TextFormatFlags.Right;
            if (rtl) fl |= TextFormatFlags.RightToLeft;
            fl = Lang.Flags(fl);
            TextRenderer.DrawText(g, s, f, r, c, fl);
        }

        void Head_Paint(object sender, PaintEventArgs e)
        {
            // align with the list's client area (its scroll bar is on the left)
            Rectangle b = new Rectangle(Lang.English ? 0 : head.Width - list.ClientSize.Width, 0, list.ClientSize.Width, head.Height);
            string[] titles = { "الرمز", "اسم المادة", "الوحدة", AvailTitle };
            for (int c = 0; c < 4; c++) Draw(e.Graphics, Lang.T(titles[c]), Theme.FontSmall, Cell(b, c), Theme.Muted, true);
            using (Pen p = new Pen(Theme.Border)) e.Graphics.DrawLine(p, 0, head.Height - 1, head.Width, head.Height - 1);
        }

        void List_DrawItem(object sender, DrawItemEventArgs e)
        {
            if (e.Index < 0 || e.Index >= ids.Count) return;
            int id = ids[e.Index];
            bool sel = (e.State & DrawItemState.Selected) != 0;
            double avail = Avail == null ? 0 : Avail(id);
            double inDoc = InDoc == null ? 0 : InDoc(id);
            bool shortStock = WarnShort && avail - inDoc <= 1e-9;
            Color back = sel ? Theme.Select : (e.Index % 2 == 1 ? Theme.RowAlt : Theme.Card);
            using (SolidBrush br = new SolidBrush(back)) e.Graphics.FillRectangle(br, e.Bounds);
            if (sel) using (SolidBrush br = new SolidBrush(Theme.Primary)) e.Graphics.FillRectangle(br, Lang.Mirror(new Rectangle(e.Bounds.Right - 4, e.Bounds.Top, 4, e.Bounds.Height), e.Bounds));

            System.Data.DataRow r = Cache.Item(id);
            string code = Cache.ItemCode(id), name = Cache.ItemName(id), unit = Cache.ItemUnit(id);
            bool frozen = r != null && Db.B(r["freez"]);
            Draw(e.Graphics, code, Theme.FontBold, Cell(e.Bounds, 0), Theme.Primary, false);
            Rectangle nr = Cell(e.Bounds, 1);
            if (inDoc > 0)
            {
                // «already in this document» tag at the end of the name column
                string tag = Lang.T("في السند: " + UI.Num(inDoc));
                Size ts = TextRenderer.MeasureText(tag, Theme.FontSmall);
                Rectangle tr = Lang.Mirror(new Rectangle(nr.Left + 6, nr.Top + 6, ts.Width + 6, nr.Height - 12), nr);
                using (SolidBrush br = new SolidBrush(Theme.Highlight)) e.Graphics.FillRectangle(br, tr);
                TextRenderer.DrawText(e.Graphics, tag, Theme.FontSmall, tr, Theme.Primary, Lang.Flags(TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.RightToLeft | TextFormatFlags.NoPrefix));
                nr = Lang.English ? new Rectangle(nr.Left, nr.Top, Math.Max(0, tr.Left - nr.Left), nr.Height) : new Rectangle(tr.Right, nr.Top, Math.Max(0, nr.Right - tr.Right), nr.Height);
            }
            Draw(e.Graphics, name + (frozen ? Lang.T("  (مجمدة)") : ""), Theme.Font, nr, frozen ? Theme.Muted : Theme.Text, true);
            Draw(e.Graphics, unit, Theme.Font, Cell(e.Bounds, 2), Theme.Muted, true);
            Color ac = shortStock ? Theme.Danger : (avail > 1e-9 ? Theme.AccentDark : Theme.Muted);
            Draw(e.Graphics, UI.Num(avail) + (shortStock ? "  ⚠" : ""), shortStock ? Theme.FontBold : Theme.Font, Cell(e.Bounds, 3), ac, false);
        }
    }
}
