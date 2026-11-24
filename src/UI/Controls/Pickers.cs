using System.Collections.Generic;
using System.Data;
using System.Drawing.Drawing2D;
using System.Drawing;
using System.IO;
using System.Windows.Forms;
using System;

namespace Stores
{
    // ------------------------------------------------------------------ pickers
    /// <summary>Text box with auto complete + search button for choosing a person.</summary>
    class PersonPicker : UserControl
    {
        TextBox txt; Button btn; int id; bool setting; SuggestPopup pop;
        public event EventHandler Changed;

        public PersonPicker(int width)
        {
            Width = width; Height = 30; Margin = new Padding(4, 5, 4, 4);
            txt = UI.Text(width - 36); txt.Dock = DockStyle.Fill; txt.Margin = new Padding(0);
            btn = UI.BtnLight("…", 34); btn.Dock = DockStyle.Left; btn.Height = 28; btn.Margin = new Padding(0);
            btn.TabStop = false;
            Controls.Add(txt); Controls.Add(btn);
            // suggestions while typing (any part of the name); registered first so Enter picks from the list
            pop = new SuggestPopup(txt, Find, delegate(int pid) { Value = pid; Fire(); });
            btn.Click += delegate { Search(); };
            txt.Leave += delegate { BeginInvoke(new Action(delegate { if (!pop.Visible && !IsDisposed) Resolve(); })); };
            txt.KeyDown += delegate(object s, KeyEventArgs e)
            {
                if (e.KeyCode == Keys.F2) { Search(); e.Handled = true; }
                else if (e.KeyCode == Keys.Enter && !pop.Visible) { Resolve(); }
            };
            txt.TextChanged += delegate { if (!setting) { id = 0; txt.BackColor = Theme.Input; } };
        }

        public int Value
        {
            get { return id; }
            set
            {
                setting = true;
                id = value;
                txt.Text = value == 0 ? "" : Cache.Person(value);
                txt.BackColor = Theme.Input;
                setting = false;
            }
        }

        public void Clear() { Value = 0; }
        public void FocusText() { txt.Focus(); }

        void Resolve()
        {
            if (id != 0) return;
            string t = txt.Text.Trim();
            if (t.Length == 0) { Fire(); return; }
            int found = Cache.FindPerson(t);
            if (found != 0) { Value = found; Fire(); return; }
            Search();
        }

        void Search()
        {
            int found = SearchDialog.PickPerson(FindForm(), id == 0 ? txt.Text.Trim() : "");
            if (found != 0) { txt.AutoCompleteCustomSource = Cache.PersonNames; Value = found; Fire(); }
            else if (id == 0 && txt.Text.Trim().Length > 0) txt.BackColor = Theme.RowWarn;
        }

        void Fire() { if (Changed != null) Changed(this, EventArgs.Empty); }

        /// <summary>Persons whose name / phone / note contain every typed word; names starting with the text first.</summary>
        static List<KeyValuePair<int, string>> Find(string text)
        {
            string[] words = UI.Norm(text).Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            string first = UI.Norm(text).Trim();
            List<KeyValuePair<int, string>> starts = new List<KeyValuePair<int, string>>(), other = new List<KeyValuePair<int, string>>();
            foreach (DataRow r in Cache.Persons.Rows)
            {
                string s = Convert.ToString(r["_s"]);
                bool ok = true;
                foreach (string w in words) if (s.IndexOf(w, StringComparison.Ordinal) < 0) { ok = false; break; }
                if (!ok) continue;
                KeyValuePair<int, string> kv = new KeyValuePair<int, string>(Db.I(r["p_no"]), Db.S(r["p_name"]));
                if (UI.Norm(kv.Value).StartsWith(first, StringComparison.Ordinal)) starts.Add(kv); else other.Add(kv);
                if (starts.Count >= 30) break;
            }
            starts.AddRange(other);
            return starts.Count > 30 ? starts.GetRange(0, 30) : starts;
        }
    }

    /// <summary>Text box with search for choosing an item (used in reports and dialogs).</summary>
    class ItemPicker : UserControl
    {
        TextBox txt; Button btn; int id; bool setting; SuggestPopup pop;
        public event EventHandler Changed;

        public ItemPicker(int width)
        {
            Width = width; Height = 30; Margin = new Padding(4, 5, 4, 4);
            txt = UI.Text(width - 36); txt.Dock = DockStyle.Fill; txt.Margin = new Padding(0);
            btn = UI.BtnLight("…", 34); btn.Dock = DockStyle.Left; btn.Height = 28; btn.Margin = new Padding(0);
            btn.TabStop = false;
            Controls.Add(txt); Controls.Add(btn);
            pop = new SuggestPopup(txt, delegate(string q)
            {
                int total;
                List<KeyValuePair<int, string>> l = new List<KeyValuePair<int, string>>();
                foreach (int it in ItemIndex.Search(q, false, 30, out total))
                    l.Add(new KeyValuePair<int, string>(it, Cache.ItemCode(it) + "  •  " + Cache.ItemName(it)));
                return l;
            }, delegate(int iid) { Value = iid; if (Changed != null) Changed(this, EventArgs.Empty); });
            btn.Click += delegate { Search(); };
            txt.Leave += delegate { BeginInvoke(new Action(delegate { if (!pop.Visible && !IsDisposed) Resolve(); })); };
            txt.KeyDown += delegate(object s, KeyEventArgs e)
            {
                if (e.KeyCode == Keys.F2) { Search(); e.Handled = true; }
                else if (e.KeyCode == Keys.Enter && !pop.Visible) Resolve();
            };
            txt.TextChanged += delegate { if (!setting) { id = 0; txt.BackColor = Theme.Input; } };
        }

        public int Value
        {
            get { return id; }
            set
            {
                setting = true; id = value;
                txt.Text = value == 0 ? "" : Cache.ItemName(value);
                txt.BackColor = Theme.Input;
                setting = false;
            }
        }

        void Resolve()
        {
            if (id != 0) return;
            string t = txt.Text.Trim();
            if (t.Length == 0) return;
            int f = Cache.FindItemByName(t);
            if (f == 0) f = Cache.FindItemByCode(t);
            if (f != 0) { Value = f; if (Changed != null) Changed(this, EventArgs.Empty); return; }
            Search();
        }

        void Search()
        {
            int f = SearchDialog.PickItem(FindForm(), id == 0 ? txt.Text.Trim() : "", true);
            if (f != 0) { Value = f; if (Changed != null) Changed(this, EventArgs.Empty); }
        }
    }
}
