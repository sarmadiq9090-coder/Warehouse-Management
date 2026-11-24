using System.Collections.Generic;
using System.Data.Common;
using System.Data;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Windows.Forms;
using System;

namespace Stores
{
    // ================================================================== stores / types / groups
    class LookupsView : UserControl
    {
        public LookupsView()
        {
            BackColor = Theme.Bg;
            TableLayoutPanel t = new TableLayoutPanel(); t.Dock = DockStyle.Fill; t.ColumnCount = 3; t.RowCount = 1;
            for (int i = 0; i < 3; i++) t.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 33.3f));
            t.Controls.Add(new LookupPanel("المخازن", "stor", "stor_no", "stor_name",
                new string[] { "SELECT COUNT(*) FROM sale WHERE stor = ?", "SELECT COUNT(*) FROM shop WHERE stor = ?", "SELECT COUNT(*) FROM trans WHERE stor_out = ? OR stor_in = ?", "SELECT COUNT(*) FROM item_stor WHERE stor = ? AND (item_bal <> 0 OR item_bal_f <> 0)" }), 0, 0);
            t.Controls.Add(new LookupPanel("أنواع المواد", "[type]", "type_no", "type_name", new string[] { "SELECT COUNT(*) FROM item WHERE [type] = ?" }), 1, 0);
            t.Controls.Add(new LookupPanel("مجموعات المواد", "[group]", "group_no", "group_name", new string[] { "SELECT COUNT(*) FROM item WHERE [group] = ?" }), 2, 0);
            Controls.Add(t); Controls.Add(ViewHelp.Title("المخازن والأنواع والمجموعات"));
        }
    }

    class LookupPanel : Panel
    {
        string table, idCol, nameCol; string[] usage; ListBox list;

        public LookupPanel(string title, string tbl, string id, string name, string[] usageSql)
        {
            table = tbl; idCol = id; nameCol = name; usage = usageSql;
            Dock = DockStyle.Fill; Margin = new Padding(6); BackColor = Theme.Card; Padding = new Padding(10);
            Label l = new Label(); l.Text = title; l.Font = Theme.FontBold; l.ForeColor = Theme.Primary; l.Dock = DockStyle.Top; l.Height = 30;
            list = new ListBox(); list.Dock = DockStyle.Fill; list.Font = new Font("Segoe UI", 11f); list.IntegralHeight = false; list.BorderStyle = BorderStyle.FixedSingle;
            list.DisplayMember = "Name";
            FlowLayoutPanel bar = UI.Flow(); bar.Dock = DockStyle.Bottom;
            Button add = UI.Btn("إضافة", Theme.Accent, 90), ren = UI.Btn("تعديل الاسم", Theme.Primary, 110), del = UI.Btn("حذف", Theme.Danger, 80);
            bar.Controls.AddRange(new Control[] { add, ren, del });
            Controls.Add(list); Controls.Add(bar); Controls.Add(l);
            Reload();
            add.Click += delegate
            {
                string n = InputDialog.Ask(FindForm(), title, "الاسم الجديد:", "");
                if (string.IsNullOrEmpty(n)) return;
                if (Exists(n, 0)) { UI.Warn("الاسم موجود."); return; }
                Db.Exec("INSERT INTO " + table + " (" + nameCol + ") VALUES (?)", n);
                Db.Log(null, "إضافة " + title, n);
                Done();
            };
            ren.Click += delegate
            {
                ComboItem it = list.SelectedItem as ComboItem; if (it == null) { UI.Warn("اختر اسما من القائمة أولا."); return; }
                string n = InputDialog.Ask(FindForm(), title, "الاسم:", it.Name);
                if (string.IsNullOrEmpty(n) || n == it.Name) return;
                if (Exists(n, it.Id)) { UI.Warn("الاسم موجود."); return; }
                Db.Exec("UPDATE " + table + " SET " + nameCol + " = ? WHERE " + idCol + " = ?", n, it.Id);
                Db.Log(null, "تعديل " + title, it.Name + " ← " + n);
                Done();
            };
            del.Click += delegate
            {
                ComboItem it = list.SelectedItem as ComboItem; if (it == null) { UI.Warn("اختر اسما من القائمة أولا."); return; }
                int used = 0;
                foreach (string u in usage) used += u.Split('?').Length == 3 ? Db.I(Db.Scalar(u, it.Id, it.Id)) : Db.I(Db.Scalar(u, it.Id));
                if (used > 0) { UI.Warn("لا يمكن الحذف لأنه مستخدم (" + used + ")."); return; }
                if (!UI.Confirm("حذف: " + it.Name + " ؟")) return;
                Db.Exec("DELETE FROM " + table + " WHERE " + idCol + " = ?", it.Id);
                Db.Log(null, "حذف " + title, it.Name);
                Done();
            };
        }

        bool Exists(string n, int except) { return Db.I(Db.Scalar("SELECT COUNT(*) FROM " + table + " WHERE " + nameCol + " = ? AND " + idCol + " <> ?", n, except)) > 0; }

        void Done() { Cache.LoadLookups(); Cache.LoadItems(); Reload(); }

        void Reload()
        {
            list.Items.Clear();
            foreach (DataRow r in Db.Query("SELECT " + idCol + ", " + nameCol + " FROM " + table + " ORDER BY " + nameCol).Rows)
                list.Items.Add(new ComboItem(Db.I(r[0]), Db.S(r[1])));
        }
    }
}
