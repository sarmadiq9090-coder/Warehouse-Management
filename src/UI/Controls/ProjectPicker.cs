using System.Collections.Generic;
using System.Data;
using System.Drawing;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using System;

namespace Stores
{
    // ------------------------------------------------------------------ cost centre picker («مركز الكلفة»)
    /// <summary>
    /// A drop-down list of the active cost centres (code + name + type). Typing filters the list, ↑ ↓ Enter pick,
    /// ▼ opens the whole list, «+» adds a new cost centre on the spot and selects it.
    /// The list is drawn inside the form (on top of the other controls), so it never steals the focus from the text box.
    /// </summary>
    class ProjectPicker : UserControl
    {
        TextBox txt; Button btnList, btnAdd; CcList list; int id; bool setting, canAdd; Form host;
        public event EventHandler Changed;

        [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern IntPtr SendMessage(IntPtr h, int msg, IntPtr w, string l);

        public ProjectPicker(int width)
        {
            Width = width; Height = 30; Margin = new Padding(4, 5, 4, 4);
            txt = UI.Text(width - 64); txt.Dock = DockStyle.Fill; txt.Margin = new Padding(0);
            btnList = UI.BtnLight("▼", 30); btnList.Dock = DockStyle.Left; btnList.Height = 28; btnList.Margin = new Padding(0); btnList.TabStop = false;
            btnAdd = UI.Btn("+", Theme.Accent, 32); btnAdd.Font = new Font(Theme.Font.FontFamily, 13f, FontStyle.Bold); btnAdd.Dock = DockStyle.Left; btnAdd.Height = 28; btnAdd.Margin = new Padding(0); btnAdd.TabStop = false;
            canAdd = Db.HasProj && Session.Can(Session.Persons); btnAdd.Visible = canAdd;
            new ToolTip().SetToolTip(btnAdd, "مركز كلفة جديد");
            new ToolTip().SetToolTip(btnList, "قائمة مراكز الكلفة");
            Controls.Add(txt); Controls.Add(btnList); Controls.Add(btnAdd);
            btnList.Click += delegate { if (ListOpen) HideList(); else { txt.Focus(); ShowList(true); } };
            btnAdd.Click += delegate { AddNew(id == 0 ? txt.Text.Trim() : ""); };
            txt.HandleCreated += delegate { SendMessage(txt.Handle, 0x1501, (IntPtr)1, "اكتب للبحث أو اختر من القائمة ▼"); };
            txt.MouseUp += delegate { if (!ListOpen && txt.SelectionLength == 0) ShowList(true); };
            txt.Leave += delegate
            {
                BeginInvoke((MethodInvoker)delegate { if (!txt.Focused) { HideList(); Resolve(); } });
            };
            txt.TextChanged += delegate
            {
                if (setting) return;
                id = 0; txt.BackColor = Theme.Input;
                if (txt.Focused) ShowList(false);
            };
            VisibleChanged += delegate { if (!Visible) HideList(); };
        }

        public int Value
        {
            get { return id; }
            set { setting = true; id = value; txt.Text = value == 0 ? "" : Cache.Project(value); txt.BackColor = Theme.Input; setting = false; }
        }

        public void FocusText() { txt.Focus(); }

        /// <summary>True while the list is shown (used by tests and the screenshot).</summary>
        public bool ListOpen { get { return list != null && list.Visible; } }

        /// <summary>Opens the list as if the user clicked ▼ (all = ignore the typed text).</summary>
        public void OpenList(bool all) { ShowList(all); }

        /// <summary>The cost centre numbers currently offered by the list (0 = the «add new» line).</summary>
        public List<int> ListedIds { get { List<int> l = new List<int>(); if (list != null) foreach (Entry e in list.Items) l.Add(e.Id); return l; } }

        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            if (txt.Focused)
            {
                switch (keyData)
                {
                    case Keys.Down:
                        if (!ListOpen) ShowList(id != 0 || txt.Text.Trim().Length == 0); else Step(1);
                        return true;
                    case Keys.Alt | Keys.Down:
                    case Keys.F4:
                        if (ListOpen) HideList(); else ShowList(true);
                        return true;
                    case Keys.Up:
                        if (ListOpen) { Step(-1); return true; }
                        break;
                    case Keys.Escape:
                        if (ListOpen) { HideList(); return true; }
                        break;
                    case Keys.Enter:
                        if (ListOpen && list.SelectedIndex >= 0) { Pick(list.SelectedIndex); return true; }
                        HideList(); Resolve();
                        break;
                }
            }
            return base.ProcessCmdKey(ref msg, keyData);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing && list != null)
            {
                if (host != null) host.Resize -= HostResize;
                if (list.Parent != null) list.Parent.Controls.Remove(list);
                list.Dispose(); list = null;
            }
            base.Dispose(disposing);
        }

        void HostResize(object s, EventArgs e) { HideList(); }

        // ---------------------------------------------------------------- list
        class Entry
        {
            public int Id; public string Name, Code, Type; public bool IsAdd, IsInfo;
            public override string ToString() { return Name; }
        }

        /// <summary>A list box that picks on mouse down and never takes the focus.</summary>
        class CcList : ListBox
        {
            public Action<int> Picked;
            protected override void WndProc(ref Message m)
            {
                if (m.Msg == 0x0201 || m.Msg == 0x0203)   // WM_LBUTTONDOWN / WM_LBUTTONDBLCLK
                {
                    int lp = m.LParam.ToInt32();
                    int i = IndexFromPoint((short)(lp & 0xFFFF), (short)((lp >> 16) & 0xFFFF));
                    if (i >= 0 && Picked != null) Picked(i);
                    return;
                }
                base.WndProc(ref m);
            }
        }

        /// <summary>Active cost centres matching the text (every word, Arabic-normalised), best matches first.</summary>
        static List<DataRow> Matches(string text)
        {
            List<DataRow> rows = new List<DataRow>();
            if (Cache.Projects == null) return rows;
            string n = UI.Norm(text).Trim();
            string[] words = n.Split(new char[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            foreach (DataRow r in Cache.Projects.Rows)
            {
                if (Db.I(r["status"]) == 2) continue;
                string s = Db.S(r["_s"]);
                bool ok = true;
                foreach (string w in words) if (s.IndexOf(w, StringComparison.Ordinal) < 0) { ok = false; break; }
                if (ok) rows.Add(r);
            }
            if (n.Length == 0) return rows.OrderBy(r => Db.S(r["proj_name"])).ToList();
            return rows.OrderBy(r => UI.Norm(Db.S(r["proj_code"])) == n ? 0 : UI.Norm(Db.S(r["proj_name"])).StartsWith(n) ? 1 : 2)
                       .ThenBy(r => Db.S(r["proj_name"])).ToList();
        }

        void EnsureList()
        {
            Form f = FindForm();
            if (f == null) return;
            if (list == null)
            {
                list = new CcList();
                list.RightToLeft = RightToLeft.Yes;
                list.Font = Theme.Font;
                list.DrawMode = DrawMode.OwnerDrawFixed;
                list.ItemHeight = 30;
                list.IntegralHeight = false;
                list.BorderStyle = BorderStyle.FixedSingle;
                list.TabStop = false;
                list.Visible = false;
                list.DrawItem += DrawEntry;
                list.MouseMove += delegate(object s, MouseEventArgs e) { int i = list.IndexFromPoint(e.Location); if (i >= 0 && i != list.SelectedIndex) list.SelectedIndex = i; };
                list.GotFocus += delegate { txt.Focus(); };
                list.Picked = Pick;
            }
            if (host != f)
            {
                if (host != null) host.Resize -= HostResize;
                host = f; host.Resize += HostResize;
            }
            if (list.Parent != f) { if (list.Parent != null) list.Parent.Controls.Remove(list); f.Controls.Add(list); }
        }

        void ShowList(bool all)
        {
            if (!Enabled || !Visible) return;
            EnsureList();
            if (list == null) return;
            string t = all ? "" : txt.Text.Trim();
            list.BeginUpdate();
            list.Items.Clear();
            foreach (DataRow r in Matches(t))
                list.Items.Add(new Entry { Id = Db.I(r["proj_no"]), Name = Db.S(r["proj_name"]), Code = Db.S(r["proj_code"]), Type = Db.S(r["type_name"]) });
            bool exact = t.Length > 0 && Cache.FindProject(t) != 0;
            if (t.Length > 0 && !exact && canAdd)
                list.Items.Add(new Entry { IsAdd = true, Name = "+ مركز كلفة جديد باسم «" + t + "»" });
            if (list.Items.Count == 0)
                list.Items.Add(new Entry { IsInfo = true, Name = Db.HasProj ? "لا توجد مراكز كلفة بعد • اضغط + لإضافة مركز كلفة" : "ميزة مراكز الكلفة غير مفعّلة بعد" });
            list.EndUpdate();
            int sel = -1;
            for (int i = 0; i < list.Items.Count; i++) if (((Entry)list.Items[i]).Id == id && id != 0) sel = i;
            if (sel < 0 && t.Length > 0 && !((Entry)list.Items[0]).IsInfo) sel = 0;
            list.SelectedIndex = sel;

            Form f = host;
            Point top = f.PointToClient(PointToScreen(Point.Empty));
            int w = Math.Max(Width, 420);
            int x = top.X + Width - w;                                  // right edges aligned (RTL)
            x = Math.Max(2, Math.Min(x, f.ClientSize.Width - w - 2));
            int rows = Math.Min(list.Items.Count, 10);
            int h = rows * list.ItemHeight + 2;
            int y = top.Y + Height;
            if (y + h > f.ClientSize.Height - 4 && top.Y - h > 4) y = top.Y - h;      // no room below: open upwards
            else if (y + h > f.ClientSize.Height - 4) h = Math.Max(list.ItemHeight * 3, f.ClientSize.Height - 4 - y);
            list.Bounds = new Rectangle(x, y, w, h);
            list.Visible = true;
            list.BringToFront();
            if (sel >= 0) list.TopIndex = Math.Max(0, sel - rows + 1);
        }

        void HideList() { if (list != null && list.Visible) list.Visible = false; }

        void Step(int step)
        {
            if (!ListOpen || list.Items.Count == 0) return;
            int i = list.SelectedIndex + step;
            i = Math.Max(0, Math.Min(list.Items.Count - 1, i));
            if (((Entry)list.Items[i]).IsInfo) return;
            list.SelectedIndex = i;
        }

        void Pick(int index)
        {
            if (list == null || index < 0 || index >= list.Items.Count) return;
            Entry e = (Entry)list.Items[index];
            if (e.IsInfo) { if (canAdd) { HideList(); AddNew(""); } return; }
            HideList();
            if (e.IsAdd) { AddNew(txt.Text.Trim()); return; }
            Value = e.Id; Fire();
            txt.Focus(); txt.SelectionStart = txt.Text.Length;
        }

        void DrawEntry(object sender, DrawItemEventArgs e)
        {
            if (e.Index < 0 || e.Index >= list.Items.Count) return;
            Entry en = (Entry)list.Items[e.Index];
            bool sel = (e.State & DrawItemState.Selected) != 0 && !en.IsInfo;
            Graphics g = e.Graphics;
            using (SolidBrush b = new SolidBrush(sel ? Theme.Select : Theme.Card)) g.FillRectangle(b, e.Bounds);
            Rectangle r = e.Bounds; r.Inflate(-10, 0);
            TextFormatFlags right = Lang.Flags(TextFormatFlags.Right | TextFormatFlags.VerticalCenter | TextFormatFlags.RightToLeft | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine);
            if (en.IsAdd || en.IsInfo)
            {
                TextRenderer.DrawText(g, Lang.T(en.Name), en.IsAdd ? Theme.FontBold : Theme.Font, r, en.IsAdd ? Theme.AccentDark : Theme.Muted, right);
                return;
            }
            string meta = Lang.TCell(en.Type) + (en.Code.Length > 0 ? "  •  " + en.Code : "");
            Size ms = TextRenderer.MeasureText(g, meta, Theme.FontSmall, r.Size, TextFormatFlags.NoPadding | TextFormatFlags.SingleLine);
            Rectangle rm = new Rectangle(r.Left, r.Top, Math.Min(r.Width / 2, ms.Width + 8), r.Height);
            Rectangle rn = Lang.Mirror(new Rectangle(rm.Right + 6, r.Top, r.Right - rm.Right - 6, r.Height), r);
            rm = Lang.Mirror(rm, r);
            TextRenderer.DrawText(g, en.Name, Theme.Font, rn, Theme.Text, right);
            TextRenderer.DrawText(g, meta, Theme.FontSmall, rm, Theme.Muted, Lang.Flags(TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis));
            using (Pen p = new Pen(Theme.Border)) g.DrawLine(p, e.Bounds.Left, e.Bounds.Bottom - 1, e.Bounds.Right, e.Bounds.Bottom - 1);
        }

        // ---------------------------------------------------------------- typed text / new cost centre
        void Resolve()
        {
            if (id != 0) return;
            string t = txt.Text.Trim();
            if (t.Length == 0) { Fire(); return; }
            int f = Cache.FindProject(t);
            if (f == 0) { List<DataRow> m = Matches(t); if (m.Count == 1) f = Db.I(m[0]["proj_no"]); }
            if (f != 0) { Value = f; Fire(); return; }
            txt.BackColor = Theme.RowWarn;
        }

        void AddNew(string name)
        {
            int nid = CostCentreQuickDialog.AddNew(FindForm(), name);
            if (nid > 0) { Value = nid; Fire(); }
            txt.Focus();
        }

        void Fire() { if (Changed != null) Changed(this, EventArgs.Empty); }
    }
}
