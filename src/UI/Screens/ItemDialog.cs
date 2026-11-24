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
    class ItemDialog : BaseDialog
    {
        int id;
        TextBox txtName, txtCode; ComboBox cmbType, cmbGroup, cmbUnit; TextBox txtRet, txtOrder, txtLead; CheckBox chkFreeze; DataGridView stores;

        public ItemDialog(int itemNo)
        {
            id = itemNo;
            Text = id == 0 ? "مادة جديدة" : "تعديل مادة";
            ClientSize = new Size(780, 640);
            TableLayoutPanel t = FormTable(); t.Dock = DockStyle.Top; t.Height = 352;
            txtName = UI.Text(400); txtName.MaxLength = 70;
            Panel codeP = new Panel(); codeP.Height = 32; codeP.Width = 400;
            txtCode = UI.Text(250); txtCode.MaxLength = 20; txtCode.Dock = DockStyle.Fill; txtCode.RightToLeft = RightToLeft.No;
            Button sug = UI.BtnLight("اقتراح رمز", 110); sug.Dock = DockStyle.Left; sug.Height = 30;
            codeP.Controls.Add(txtCode); codeP.Controls.Add(sug);
            // own binding context: the lists are filled now, so SetSel below works before the dialog is shown
            // (without it the type / group showed «بدون» and saving the item cleared them)
            cmbType = UI.Combo(260); cmbType.BindingContext = new BindingContext(); UI.Fill(cmbType, Cache.Types, "type_no", "type_name", "— بدون —");
            cmbGroup = UI.Combo(260); cmbGroup.BindingContext = new BindingContext(); UI.Fill(cmbGroup, Cache.Groups, "group_no", "group_name", "— بدون —");
            cmbUnit = UI.Combo(180); cmbUnit.DropDownStyle = ComboBoxStyle.DropDown; cmbUnit.MaxLength = 20;
            cmbUnit.Items.AddRange(UnitChoices());
            cmbUnit.Enabled = Db.HasUnit;
            txtRet = UI.Text(120); txtOrder = UI.Text(120); txtLead = UI.Text(120);
            txtLead.Enabled = Db.HasMinMax;
            chkFreeze = new CheckBox(); chkFreeze.Text = "مادة مجمدة (لا تظهر في السندات الجديدة)"; chkFreeze.AutoSize = true;
            AddRow(t, "اسم المادة *", txtName);
            AddRow(t, "الرمز", codeP);
            AddRow(t, "النوع", cmbType);
            AddRow(t, "المجموعة", cmbGroup);
            AddRow(t, "وحدة القياس", cmbUnit);
            AddRow(t, "حد إعادة الطلب", txtRet);
            AddRow(t, "مدة التوريد (يوم)", txtLead);
            AddRow(t, "الترتيب في التقارير", txtOrder);
            AddRow(t, "", chkFreeze);

            Label sl = new Label(); sl.Text = "  المخازن: الرصيد الافتتاحي، موقع المادة على الرف، والحد الأدنى (مخزون الأمان) والأعلى"; sl.Dock = DockStyle.Top; sl.Height = 28; sl.Font = Theme.FontBold; sl.ForeColor = Theme.Primary;
            stores = new DataGridView(); stores.Dock = DockStyle.Fill; UI.StyleGrid(stores);
            stores.AllowUserToAddRows = false; stores.AllowUserToDeleteRows = false; stores.SelectionMode = DataGridViewSelectionMode.CellSelect;
            stores.Columns.Add(UI.Col("stor", "", null, 10, false)); stores.Columns["stor"].Visible = false;
            stores.Columns.Add(UI.Col("name", "المخزن", null, 0, false)); stores.Columns["name"].ReadOnly = true;
            stores.Columns.Add(UI.Col("open", "الرصيد الافتتاحي", null, 160, true));
            stores.Columns.Add(UI.Col("old", "", null, 10, true)); stores.Columns["old"].Visible = false;
            stores.Columns.Add(UI.Col("bal", "الرصيد الحالي", null, 110, true)); stores.Columns["bal"].ReadOnly = true;
            // shelf location «الموقع» and min / max per store (purchasing); o* = values as loaded, to save only changes
            stores.Columns.Add(UI.Col("bin", "الموقع", null, 100, false)); ((DataGridViewTextBoxColumn)stores.Columns["bin"]).MaxInputLength = 20;
            stores.Columns.Add(UI.Col("min", "الحد الأدنى", null, 95, true));
            stores.Columns.Add(UI.Col("max", "الحد الأعلى", null, 95, true));
            foreach (string h in new[] { "obin", "omin", "omax" }) { stores.Columns.Add(UI.Col(h, "", null, 10, false)); stores.Columns[h].Visible = false; }
            stores.Columns["open"].Width = 120;
            stores.Columns["bin"].Visible = Db.HasBin; stores.Columns["min"].Visible = stores.Columns["max"].Visible = Db.HasMinMax;
            Panel sp = new Panel(); sp.Dock = DockStyle.Fill; sp.Padding = new Padding(16, 0, 16, 8);
            sp.Controls.Add(stores); sp.Controls.Add(sl);

            Button ok = UI.Btn("حفظ", Theme.Accent, 110);
            Button cancel = UI.BtnLight("إلغاء", 90);
            Controls.Add(sp); Controls.Add(t); Controls.Add(ButtonBar(ok, cancel));

            Dictionary<int, double[]> bal = new Dictionary<int, double[]>();
            Dictionary<int, object[]> plan = new Dictionary<int, object[]>();
            if (id > 0)
            {
                DataRow r = Db.Query("SELECT * FROM item WHERE item_no = ?", id).Rows[0];
                txtName.Text = Db.S(r["item_name"]); txtCode.Text = Db.S(r["item_code"]);
                // the bound lists fill only when the window is created: select again on Load, otherwise type and group
                // show «بدون» and saving clears them (found by the audit trail test)
                int ty = Db.I(r["type"]), gr = Db.I(r["group"]);
                UI.SetSel(cmbType, ty); UI.SetSel(cmbGroup, gr);
                Load += delegate { UI.SetSel(cmbType, ty); UI.SetSel(cmbGroup, gr); };
                txtRet.Text = Db.D(r["item_bal_ret"]) == 0 ? "" : UI.Num(Db.D(r["item_bal_ret"]));
                txtOrder.Text = Db.I(r["tslsl"]) == 0 ? "" : Db.I(r["tslsl"]).ToString();
                chkFreeze.Checked = Db.B(r["freez"]);
                if (Db.HasUnit) cmbUnit.Text = Db.S(r["unit"]).Trim();
                if (Db.HasMinMax && Db.I(r["lead_days"]) > 0) txtLead.Text = Db.I(r["lead_days"]).ToString();
                foreach (DataRow b in Db.Query("SELECT * FROM item_stor WHERE item_no = ?", id).Rows)
                {
                    bal[Db.I(b["stor"])] = new double[] { Db.D(b["item_bal_f"]), Db.D(b["item_bal"]) };
                    plan[Db.I(b["stor"])] = new object[] { Db.HasBin ? Db.S(b["bin"]).Trim() : "",
                        Db.HasMinMax && Db.D(b["min_qty"]) > 0 ? (object)Db.D(b["min_qty"]) : null, Db.HasMinMax && Db.D(b["max_qty"]) > 0 ? (object)Db.D(b["max_qty"]) : null };
                }
            }
            foreach (DataRow s in Cache.Stores.Rows)
            {
                int sid = Db.I(s["stor_no"]);
                double[] v; bal.TryGetValue(sid, out v);
                object[] pl; if (!plan.TryGetValue(sid, out pl)) pl = new object[] { "", null, null };
                stores.Rows.Add(sid, Db.S(s["stor_name"]), v == null ? 0 : v[0], v == null ? 0 : v[0], v == null ? 0 : v[1], pl[0], pl[1], pl[2], pl[0], pl[1], pl[2]);
            }
            stores.ReadOnly = !Session.Can(Session.Items);
            stores.Columns["name"].ReadOnly = true; stores.Columns["bal"].ReadOnly = true;

            sug.Click += delegate { txtCode.Text = SuggestCode(UI.Sel(cmbType)); };
            cancel.Click += delegate { Close(); };
            ok.Click += delegate { if (SaveItem()) { DialogResult = DialogResult.OK; Close(); } };
            AcceptButton = ok;
        }

        public void SetName(string n) { txtName.Text = n; }
        public void SetCode(string c) { txtCode.Text = c; }
        /// <summary>The item (after «حفظ» of a new item: its new number).</summary>
        public int ItemNo { get { return id; } }

        static readonly string[] CommonUnits = { "قطعة", "متر", "لفة", "علبة", "كغم", "طن", "لتر", "م²", "م³", "كيس", "صندوق", "طقم", "زوج", "لوح", "برميل", "رزمة" };

        /// <summary>Common Arabic units plus any other unit already used for an item.</summary>
        static object[] UnitChoices()
        {
            List<string> l = new List<string>(CommonUnits);
            if (Cache.Items != null)
                foreach (DataRow r in Cache.Items.Rows)
                {
                    string u = Db.S(r["unit"]).Trim();
                    if (u.Length > 0 && !l.Contains(u)) l.Add(u);
                }
            return l.ToArray();
        }

        static string SuggestCode(int type)
        {
            DataTable t = type > 0 ? Db.Query("SELECT item_code FROM item WHERE [type] = ? AND item_code IS NOT NULL", type)
                                   : Db.Query("SELECT item_code FROM item WHERE item_code IS NOT NULL");
            long best = 0; int len = 0;
            foreach (DataRow r in t.Rows)
            {
                string c = Db.S(r[0]).Trim(); long v;
                if (c.Length > 0 && long.TryParse(c, out v) && v > best) { best = v; len = c.Length; }
            }
            if (best == 0) return "";
            string next = (best + 1).ToString().PadLeft(len, '0');
            while (Db.I(Db.Scalar("SELECT COUNT(*) FROM item WHERE item_code = ?", next)) > 0) { best++; next = (best + 1).ToString().PadLeft(len, '0'); }
            return next;
        }

        bool SaveItem()
        {
            if (!Session.Demand(Session.Items)) return false;
            stores.EndEdit();
            string name = txtName.Text.Trim(), code = UI.NormDigits(txtCode.Text.Trim()), unit = cmbUnit.Text.Trim();
            if (unit.Length > 20) unit = unit.Substring(0, 20);
            if (name.Length == 0) { UI.Warn("أدخل اسم المادة."); return false; }
            if (Db.I(Db.Scalar("SELECT COUNT(*) FROM item WHERE item_name = ? AND item_no <> ?", name, id)) > 0) { UI.Warn("يوجد مادة أخرى بنفس الاسم."); return false; }
            if (code.Length > 0 && Db.I(Db.Scalar("SELECT COUNT(*) FROM item WHERE item_code = ? AND item_no <> ?", code, id)) > 0) { UI.Warn("الرمز مستخدم لمادة أخرى."); return false; }
            double ret = 0; if (txtRet.Text.Trim().Length > 0 && !UI.TryNum(txtRet.Text, out ret)) { UI.Warn("حد إعادة الطلب غير صحيح."); return false; }
            double ord = 0; if (txtOrder.Text.Trim().Length > 0) UI.TryNum(txtOrder.Text, out ord);
            double lead = 0; if (txtLead.Text.Trim().Length > 0 && (!UI.TryNum(txtLead.Text, out lead) || lead < 0 || lead > 3650)) { UI.Warn("مدة التوريد غير صحيحة (عدد الأيام من طلب المادة حتى وصولها)."); return false; }
            foreach (DataGridViewRow r in stores.Rows)
            {
                double mn, mx;
                if (!OptNum(r.Cells["min"].Value, out mn) || !OptNum(r.Cells["max"].Value, out mx)) { UI.Warn("الحد الأدنى أو الأعلى غير صحيح في " + r.Cells["name"].Value); return false; }
                if (mn > 0 && mx > 0 && mx < mn) { UI.Warn("الحد الأعلى أقل من الحد الأدنى في " + r.Cells["name"].Value); return false; }
            }
            int type = UI.Sel(cmbType), grp = UI.Sel(cmbGroup);
            object typeV = type == 0 ? (object)DBNull.Value : type, grpV = grp == 0 ? (object)DBNull.Value : grp;
            object codeV = code.Length == 0 ? (object)DBNull.Value : code;
            DateTime lockedTo = PeriodLock.Limit();   // year closing / month lock, read before the transaction starts
            int oldId = id;                           // a refused save rolls the new row back: its number must not stay
            bool saved = false;
            try
            {
                Db.InTx(delegate(DbTransaction tx)
                {
                    List<KeyValuePair<string, string>> before = id == 0 ? null : AuditTrail.ItemSnap(tx, id);
                    int logId = 0;
                    if (id == 0)
                    {
                        id = Db.Insert(tx, "INSERT INTO item (item_name, item_code, [group], [type], item_bal1, item_bal, item_price_dl1, item_bal_ret, qty_f, sale_pr, tslsl, freez) " +
                                           "VALUES (?, ?, ?, ?, 0, 0, 0, ?, 0, 0, ?, ?)", name, codeV, grpV, typeV, ret, (double)ord, chkFreeze.Checked);
                        Db.Log(tx, "إضافة مادة", name);
                    }
                    else
                    {
                        if (Db.X(tx, "UPDATE item SET item_name = ?, item_code = ?, [group] = ?, [type] = ?, item_bal_ret = ?, tslsl = ?, freez = ? WHERE item_no = ?",
                            name, codeV, grpV, typeV, ret, (double)ord, chkFreeze.Checked, id) == 0)
                            throw new UserError("هذه المادة غير موجودة (ربما حُذفت من حاسوب آخر).");
                        logId = AuditTrail.Log(tx, "تعديل مادة", name);
                    }
                    if (Db.HasUnit) Db.X(tx, "UPDATE item SET unit = ? WHERE item_no = ?", Db.Null(unit), id);
                    if (Db.HasMinMax) Db.X(tx, "UPDATE item SET lead_days = ? WHERE item_no = ?", lead > 0 ? (object)(int)Math.Round(lead) : DBNull.Value, id);
                    AuditTrail.Changes(tx, logId, "item", code.Length > 0 ? code : name, before, AuditTrail.ItemSnap(tx, id));
                    foreach (DataGridViewRow r in stores.Rows)
                    {
                        double nv, ov = Db.D(r.Cells["old"].Value);
                        if (!UI.TryNum(Db.S(r.Cells["open"].Value), out nv)) throw new UserError("رصيد افتتاحي غير صحيح في " + r.Cells["name"].Value);
                        if (Math.Abs(nv - ov) < 1e-9) continue;
                        if (lockedTo != DateTime.MinValue)   // a closed year or a locked month: opening balances change every past balance
                            throw new UserError("لا يمكن تغيير الرصيد الافتتاحي بعد قفل الفترة حتى " + lockedTo.ToString("yyyy/MM/dd") + " (يغيّر أرصدة الفترة المقفلة).\n" +
                                                "لإضافة كمية استعمل «إدخال مخزني»، ولإنقاصها «إخراج مخزني».");
                        int st = Db.I(r.Cells["stor"].Value);
                        int n = Db.X(tx, "UPDATE item_stor SET item_bal_f = ?, item_bal = item_bal + ? WHERE item_no = ? AND stor = ?", nv, nv - ov, id, st);
                        if (n == 0) Db.X(tx, "INSERT INTO item_stor (item_no, stor, item_bal_f, item_bal, qty_f) VALUES (?, ?, ?, ?, 0)", id, st, nv, nv);
                        AuditTrail.Detail(tx, AuditTrail.Log(tx, "رصيد افتتاحي", name + " / " + r.Cells["name"].Value + ": " + UI.Num(ov) + " ← " + UI.Num(nv)),
                                          "item_stor", code.Length > 0 ? code : name, "الرصيد الافتتاحي: " + r.Cells["name"].Value, UI.Num(ov), UI.Num(nv));
                    }
                    SavePlan(tx, name);
                });
                saved = true;
            }
            catch (UserError ue) { UI.Warn(ue.Message); return false; }
            finally { if (!saved) id = oldId; }
            Cache.LoadItems();
            return true;
        }

        /// <summary>Empty = not set (0); false when the text is not a number or negative.</summary>
        static bool OptNum(object v, out double d)
        {
            d = 0;
            string s = Db.S(v).Trim();
            if (s.Length == 0) return true;
            return UI.TryNum(s, out d) && d >= 0;
        }

        /// <summary>Shelf location and min / max per store: only changed rows are written; a store row is created when needed.</summary>
        void SavePlan(DbTransaction tx, string name)
        {
            if (!Db.HasBin && !Db.HasMinMax) return;
            foreach (DataGridViewRow r in stores.Rows)
            {
                string bin = Db.S(r.Cells["bin"].Value).Trim(), obin = Db.S(r.Cells["obin"].Value).Trim();
                if (bin.Length > 20) bin = bin.Substring(0, 20);
                double mn, mx, omn, omx;
                OptNum(r.Cells["min"].Value, out mn); OptNum(r.Cells["max"].Value, out mx);
                OptNum(r.Cells["omin"].Value, out omn); OptNum(r.Cells["omax"].Value, out omx);
                if (bin == obin && Math.Abs(mn - omn) < 1e-9 && Math.Abs(mx - omx) < 1e-9) continue;
                int st = Db.I(r.Cells["stor"].Value);
                if (Db.I(Db.S1(tx, "SELECT COUNT(*) FROM item_stor WHERE item_no = ? AND stor = ?", id, st)) == 0)
                    Db.X(tx, "INSERT INTO item_stor (item_no, stor, item_bal_f, item_bal, qty_f) VALUES (?, ?, 0, 0, 0)", id, st);
                if (Db.HasBin) Db.X(tx, "UPDATE item_stor SET bin = ? WHERE item_no = ? AND stor = ?", Db.Null(bin), id, st);
                if (Db.HasMinMax) Db.X(tx, "UPDATE item_stor SET min_qty = ?, max_qty = ? WHERE item_no = ? AND stor = ?",
                    mn > 0 ? (object)mn : DBNull.Value, mx > 0 ? (object)mx : DBNull.Value, id, st);
                Db.Log(tx, "موقع وحدود المادة", name + " / " + r.Cells["name"].Value + ": الموقع " + (bin.Length > 0 ? bin : "-") +
                       "، الأدنى " + UI.Num(mn) + "، الأعلى " + UI.Num(mx));
            }
        }

        public static int AddNew(IWin32Window owner, string name)
        {
            if (!Session.Demand(Session.Items)) return 0;
            using (ItemDialog d = new ItemDialog(0))
            {
                if (!string.IsNullOrEmpty(name) && Cache.FindItemByCode(name) == 0) d.SetName(name);
                return d.ShowDialog(owner) == DialogResult.OK ? d.id : 0;
            }
        }
    }
}
