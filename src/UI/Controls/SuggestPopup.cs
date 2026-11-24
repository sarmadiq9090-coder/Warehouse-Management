using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;

namespace Stores
{
    /// <summary>Suggestions under a text box while typing (like web search boxes): shows every entry that CONTAINS
    /// all typed words (not only names that start with them), ↑/↓ to move, Enter or click to choose, Esc to close.
    /// Used by PersonPicker and ItemPicker.</summary>
    class SuggestPopup
    {
        readonly TextBox txt;
        readonly Func<string, List<KeyValuePair<int, string>>> search;
        readonly Action<int> pick;
        readonly ToolStripDropDown drop;
        readonly ListBox list;
        bool suppress;

        public SuggestPopup(TextBox box, Func<string, List<KeyValuePair<int, string>>> searchFn, Action<int> onPick)
        {
            txt = box; search = searchFn; pick = onPick;
            list = new ListBox();
            list.BorderStyle = BorderStyle.None; list.IntegralHeight = false; list.Font = Theme.Font;
            list.RightToLeft = Lang.English ? RightToLeft.No : RightToLeft.Yes; list.DisplayMember = "Value"; list.ItemHeight = 24;
            list.DrawMode = DrawMode.OwnerDrawFixed; list.BackColor = Theme.Card;
            list.DrawItem += delegate(object s, DrawItemEventArgs e)
            {
                if (e.Index < 0) return;
                bool sel = (e.State & DrawItemState.Selected) != 0;
                using (SolidBrush b = new SolidBrush(sel ? Theme.Select : Theme.Card)) e.Graphics.FillRectangle(b, e.Bounds);
                string t = ((KeyValuePair<int, string>)list.Items[e.Index]).Value;
                TextRenderer.DrawText(e.Graphics, t, Theme.Font, Rectangle.Inflate(e.Bounds, -8, 0), Theme.Text,
                    Lang.Flags(TextFormatFlags.Right | TextFormatFlags.VerticalCenter | TextFormatFlags.RightToLeft | TextFormatFlags.EndEllipsis));
            };
            ToolStripControlHost host = new ToolStripControlHost(list);
            host.Margin = Padding.Empty; host.Padding = Padding.Empty; host.AutoSize = false;
            drop = new ToolStripDropDown();
            drop.Padding = new Padding(1); drop.AutoClose = false; drop.DropShadowEnabled = true;
            drop.Items.Add(host);

            txt.TextChanged += delegate { if (!suppress) Refresh(); };
            txt.KeyDown += delegate(object s, KeyEventArgs e)
            {
                if (!drop.Visible) { if (e.KeyCode == Keys.Down && txt.Text.Trim().Length == 0) { Refresh(); } return; }
                if (e.KeyCode == Keys.Down) { if (list.SelectedIndex < list.Items.Count - 1) list.SelectedIndex++; e.Handled = e.SuppressKeyPress = true; }
                else if (e.KeyCode == Keys.Up) { if (list.SelectedIndex > 0) list.SelectedIndex--; e.Handled = e.SuppressKeyPress = true; }
                else if (e.KeyCode == Keys.Enter) { Choose(); e.Handled = e.SuppressKeyPress = true; }
                else if (e.KeyCode == Keys.Escape) { Hide(); e.Handled = e.SuppressKeyPress = true; }
            };
            txt.Leave += delegate { txt.BeginInvoke(new Action(delegate { if (!list.Focused) Hide(); })); };
            list.MouseClick += delegate { Choose(); };
            list.LostFocus += delegate { if (!txt.Focused) Hide(); };
            txt.Disposed += delegate { drop.Dispose(); };
        }

        public bool Visible { get { return drop.Visible; } }

        void Refresh()
        {
            string q = txt.Text.Trim();
            if (q.Length == 0 || !txt.Focused) { Hide(); return; }
            List<KeyValuePair<int, string>> r = search(q);
            if (r.Count == 0) { Hide(); return; }
            list.BeginUpdate();
            list.Items.Clear();
            foreach (KeyValuePair<int, string> kv in r) list.Items.Add(kv);
            list.SelectedIndex = 0;
            list.EndUpdate();
            int rows = Math.Min(10, r.Count);
            list.Size = new Size(Math.Max(txt.Width + 40, 300), rows * list.ItemHeight + 2);
            ((ToolStripControlHost)drop.Items[0]).Size = list.Size;
            if (!drop.Visible)
            {
                Point p = txt.PointToScreen(new Point(txt.Width - list.Width, txt.Height));
                drop.Show(p);
                txt.Focus();
            }
        }

        void Choose()
        {
            if (list.SelectedIndex < 0) return;
            int id = ((KeyValuePair<int, string>)list.Items[list.SelectedIndex]).Key;
            Hide();
            suppress = true;
            try { pick(id); } finally { suppress = false; }
            txt.Focus();
        }

        public void Hide() { if (drop.Visible) drop.Close(); }
    }
}
