using System.Collections.Generic;
using System.Data.Common;
using System.Data;
using System.Linq;
using System.Windows.Forms;
using System;

namespace Stores
{
    /// <summary>
    /// Purchase requests «طلبات الشراء» (tables preq / preq_menu). A request is NOT a stock movement: nothing is posted,
    /// balances and year closing are not affected. A receipt (إدخال مخزني) can be linked to a request after it is saved
    /// (shop.preq_no); the received quantity of each line is then recomputed from all linked receipts, so editing or
    /// re-saving a receipt keeps the request right. Status: 0 مسودة، 1 مُرسل، 2 مستلم جزئيا، 3 مكتمل، 9 ملغى.
    /// Creating / editing needs the receipt-add permission (Session.RecAdd).
    /// </summary>
    static class PurchaseRequests
    {
        public const int Draft = 0, Sent = 1, Partial = 2, Done = 3, Cancelled = 9;
        public const string NotReady = "ميزة طلبات الشراء غير مفعّلة بعد في ملف البيانات (يجب أن يفتح البرنامج الملف دون أن يكون مفتوحا في برنامج Access القديم).";

        public static string StatusName(int s)
        {
            switch (s) { case Draft: return "مسودة"; case Sent: return "مُرسل"; case Partial: return "مستلم جزئيا"; case Done: return "مكتمل"; case Cancelled: return "ملغى"; default: return ""; }
        }

        public static bool CanEdit { get { return Session.Can(Session.RecAdd); } }

        static void Demand()
        {
            if (!Db.HasPreq) throw new UserError(NotReady);
            if (!CanEdit) throw new UserError("إنشاء وتعديل طلبات الشراء لمن لديه صلاحية إضافة سندات الإدخال.");
        }

        // ------------------------------------------------------------ read
        /// <summary>All requests (newest first) with line count, ordered / received totals, names and age in days.</summary>
        public static DataTable List(bool includeClosed)
        {
            DataTable t = Db.Query("SELECT h.preq_no, h.menu_no, h.preq_date, h.stor, h.status, h.p_no, h.notes, h.user_name, " +
                "(SELECT COUNT(*) FROM preq_menu m WHERE m.seq = h.preq_no) AS n, (SELECT SUM(qty) FROM preq_menu m WHERE m.seq = h.preq_no) AS q, " +
                "(SELECT SUM(received_qty) FROM preq_menu m WHERE m.seq = h.preq_no) AS rq FROM preq AS h" +
                (includeClosed ? "" : " WHERE h.status IN (0, 1, 2)") + " ORDER BY h.preq_no DESC");
            t.Columns.Add("sname"); t.Columns.Add("pname"); t.Columns.Add("status_name"); t.Columns.Add("age", typeof(int));
            foreach (DataRow r in t.Rows)
            {
                r["sname"] = Db.I(r["stor"]) == 0 ? "" : Cache.Store(Db.I(r["stor"]));
                r["pname"] = Cache.Person(Db.I(r["p_no"]));
                r["status_name"] = StatusName(Db.I(r["status"]));
                r["age"] = Math.Max(0, (int)(DateTime.Today - Db.Dt(r["preq_date"]).Date).TotalDays);
                if (r["rq"] is DBNull) r["rq"] = 0.0;
            }
            t.AcceptChanges();
            return t;
        }

        public static PReq Load(int id)
        {
            DataTable h = Db.Query("SELECT * FROM preq WHERE preq_no = ?", id);
            if (h.Rows.Count == 0) return null;
            DataRow x = h.Rows[0];
            PReq r = new PReq();
            r.Id = id; r.No = Db.S(x["menu_no"]); r.Date = Db.Dt(x["preq_date"]); r.Store = Db.I(x["stor"]); r.Status = Db.I(x["status"]);
            r.Supplier = Db.I(x["p_no"]); r.Note = Db.S(x["notes"]); r.User = Db.S(x["user_name"]);
            foreach (DataRow l in Db.Query("SELECT id, item_no, qty, received_qty FROM preq_menu WHERE seq = ? ORDER BY id", id).Rows)
            {
                PReqLine pl = new PReqLine();
                pl.LineId = Db.I(l["id"]); pl.ItemNo = Db.I(l["item_no"]); pl.Qty = Db.D(l["qty"]); pl.Received = Db.D(l["received_qty"]);
                r.Lines.Add(pl);
            }
            return r;
        }

        /// <summary>Numbers of the receipts linked to a request.</summary>
        public static List<string> Receipts(int id)
        {
            List<string> l = new List<string>();
            foreach (DataRow r in Db.Query("SELECT menu_no FROM shop WHERE preq_no = ? ORDER BY menu_date, shop_no", id).Rows) l.Add(Db.S(r[0]));
            return l;
        }

        /// <summary>Quantity still to come per item from open requests (draft, sent, partly received) of a store (0 = all).</summary>
        public static Dictionary<int, double> OnOrder(int store)
        {
            Dictionary<int, double> d = new Dictionary<int, double>();
            if (!Db.HasPreq) return d;
            foreach (DataRow x in Db.Query("SELECT h.stor, l.item_no, l.qty, l.received_qty FROM preq AS h INNER JOIN preq_menu AS l ON l.seq = h.preq_no WHERE h.status IN (0, 1, 2)").Rows)
            {
                int st = Db.I(x[0]);
                if (store > 0 && st > 0 && st != store) continue;
                double rem = Db.D(x[2]) - Db.D(x[3]);
                if (rem <= 1e-9) continue;
                double v; d.TryGetValue(Db.I(x[1]), out v); d[Db.I(x[1])] = v + rem;
            }
            return d;
        }

        /// <summary>Open requests (draft / sent / partly received) of the store (or without a store) that still wait for
        /// at least one of the items: preq_no, menu_no, preq_date, p_no, pname, status_name, n (matching items), rem (their remaining qty).</summary>
        public static DataTable OpenFor(int store, IEnumerable<int> items)
        {
            DataTable t = new DataTable();
            foreach (string c in new[] { "menu_no", "pname", "status_name" }) t.Columns.Add(c);
            t.Columns.Add("preq_no", typeof(int)); t.Columns.Add("p_no", typeof(int)); t.Columns.Add("preq_date", typeof(DateTime));
            t.Columns.Add("n", typeof(int)); t.Columns.Add("rem", typeof(double));
            if (!Db.HasPreq) return t;
            HashSet<int> want = new HashSet<int>(items);
            Dictionary<int, DataRow> byId = new Dictionary<int, DataRow>();
            foreach (DataRow x in Db.Query("SELECT h.preq_no, h.menu_no, h.preq_date, h.stor, h.status, h.p_no, l.item_no, l.qty, l.received_qty FROM preq AS h INNER JOIN preq_menu AS l ON l.seq = h.preq_no " +
                                           "WHERE h.status IN (0, 1, 2) ORDER BY h.preq_date DESC, h.preq_no DESC").Rows)
            {
                int st = Db.I(x["stor"]);
                if (store > 0 && st > 0 && st != store) continue;
                if (!want.Contains(Db.I(x["item_no"]))) continue;
                double rem = Db.D(x["qty"]) - Db.D(x["received_qty"]);
                if (rem <= 1e-9) continue;
                int id = Db.I(x["preq_no"]);
                DataRow r;
                if (!byId.TryGetValue(id, out r))
                {
                    r = t.NewRow();
                    r["preq_no"] = id; r["menu_no"] = Db.S(x["menu_no"]); r["preq_date"] = Db.Dt(x["preq_date"]); r["p_no"] = Db.I(x["p_no"]);
                    r["pname"] = Cache.Person(Db.I(x["p_no"])); r["status_name"] = StatusName(Db.I(x["status"])); r["n"] = 0; r["rem"] = 0.0;
                    t.Rows.Add(r); byId[id] = r;
                }
                r["n"] = Db.I(r["n"]) + 1; r["rem"] = Db.D(r["rem"]) + rem;
            }
            return t;
        }

        // ------------------------------------------------------------ write
        /// <summary>Adds or updates a request (head + lines). Lines of the same item are merged; a line cannot go below what
        /// was already received and a received line cannot be removed. Completed / cancelled requests are read-only.</summary>
        public static int Save(PReq r)
        {
            Demand();
            Dictionary<int, PReqLine> merged = new Dictionary<int, PReqLine>();
            List<PReqLine> lines = new List<PReqLine>();
            foreach (PReqLine l in r.Lines)
            {
                if (l.ItemNo <= 0) continue;
                if (l.Qty <= 0) throw new UserError("الكمية المطلوبة يجب أن تكون أكبر من صفر: " + Cache.ItemName(l.ItemNo));
                PReqLine m;
                if (merged.TryGetValue(l.ItemNo, out m)) { m.Qty += l.Qty; continue; }
                m = new PReqLine(); m.LineId = l.LineId; m.ItemNo = l.ItemNo; m.Qty = l.Qty;
                merged[l.ItemNo] = m; lines.Add(m);
            }
            if (lines.Count == 0) throw new UserError("أضف مادة واحدة على الأقل إلى طلب الشراء.");
            Db.InTx(delegate(DbTransaction tx)
            {
                object sup = r.Supplier > 0 ? (object)r.Supplier : DBNull.Value, stor = r.Store > 0 ? (object)r.Store : DBNull.Value;
                string note = (r.Note ?? "").Trim(); if (note.Length > 255) note = note.Substring(0, 255);
                if (r.Id == 0)
                {
                    int next = (int)Db.D(Db.S1(tx, "SELECT MAX(" + Db.NumOf("menu_no") + ") FROM preq")) + 1;
                    r.No = next.ToString();
                    r.User = Session.UserName ?? "";
                    r.Id = Db.Insert(tx, "INSERT INTO preq (menu_no, preq_date, stor, status, p_no, notes, user_name) VALUES (?, ?, ?, ?, ?, ?, ?)",
                        r.No, r.Date.Date, stor, Draft, sup, Db.Null(note), r.User);
                    r.Status = Draft;
                    Db.Log(tx, "إضافة طلب شراء", "رقم " + r.No + " - " + lines.Count + " مادة");
                }
                else
                {
                    DataTable h = Db.Q(tx, "SELECT status FROM preq WHERE preq_no = ?", r.Id);
                    if (h.Rows.Count == 0) throw new UserError("طلب الشراء حُذف من حاسوب آخر.");
                    int st = Db.I(h.Rows[0][0]);
                    if (st == Done || st == Cancelled) throw new UserError("طلب الشراء " + StatusName(st) + " ولا يمكن تعديله.");
                    Db.X(tx, "UPDATE preq SET preq_date = ?, stor = ?, p_no = ?, notes = ? WHERE preq_no = ?", r.Date.Date, stor, sup, Db.Null(note), r.Id);
                    Db.Log(tx, "تعديل طلب شراء", "رقم " + r.No + " - " + lines.Count + " مادة");
                }
                Dictionary<int, DataRow> old = new Dictionary<int, DataRow>();
                foreach (DataRow x in Db.Q(tx, "SELECT id, item_no, qty, received_qty FROM preq_menu WHERE seq = ?", r.Id).Rows) old[Db.I(x["id"])] = x;
                HashSet<int> kept = new HashSet<int>();
                foreach (PReqLine l in lines)
                {
                    DataRow o;
                    if (l.LineId > 0 && old.TryGetValue(l.LineId, out o) && Db.I(o["item_no"]) == l.ItemNo)
                    {
                        if (l.Qty < Db.D(o["received_qty"]) - 1e-9)
                            throw new UserError("لا يمكن أن تكون الكمية المطلوبة أقل مما استُلم: " + Cache.ItemName(l.ItemNo) + " (المستلم " + UI.Num(Db.D(o["received_qty"])) + ")");
                        kept.Add(l.LineId);
                        if (Math.Abs(Db.D(o["qty"]) - l.Qty) > 1e-9) Db.X(tx, "UPDATE preq_menu SET qty = ? WHERE id = ?", l.Qty, l.LineId);
                    }
                    else l.LineId = Db.Insert(tx, "INSERT INTO preq_menu (seq, item_no, qty, received_qty) VALUES (?, ?, ?, 0)", r.Id, l.ItemNo, l.Qty);
                }
                foreach (KeyValuePair<int, DataRow> kv in old)
                {
                    if (kept.Contains(kv.Key)) continue;
                    if (Db.D(kv.Value["received_qty"]) > 1e-9)
                        throw new UserError("لا يمكن حذف مادة استُلم منها شيء: " + Cache.ItemName(Db.I(kv.Value["item_no"])));
                    Db.X(tx, "DELETE FROM preq_menu WHERE id = ?", kv.Key);
                }
                Recalc(tx, r.Id);
            });
            PReq fresh = Load(r.Id);
            if (fresh != null) { r.Lines = fresh.Lines; r.Status = fresh.Status; }
            return r.Id;
        }

        /// <summary>Draft → sent, sent → draft (nothing received yet), open → cancelled.</summary>
        public static void SetStatus(int id, int status)
        {
            Demand();
            Db.InTx(delegate(DbTransaction tx)
            {
                DataTable h = Db.Q(tx, "SELECT status, menu_no FROM preq WHERE preq_no = ?", id);
                if (h.Rows.Count == 0) throw new UserError("طلب الشراء غير موجود.");
                int st = Db.I(h.Rows[0][0]);
                double rec = Db.D(Db.S1(tx, "SELECT SUM(received_qty) FROM preq_menu WHERE seq = ?", id));
                bool ok = (st == Draft && status == Sent) || (st == Sent && status == Draft && rec <= 1e-9) ||
                          ((st == Draft || st == Sent || st == Partial) && status == Cancelled);
                if (!ok) throw new UserError("لا يمكن تغيير حالة الطلب من «" + StatusName(st) + "» إلى «" + StatusName(status) + "».");
                Db.X(tx, "UPDATE preq SET status = ? WHERE preq_no = ?", status, id);
                Db.Log(tx, "حالة طلب شراء", "رقم " + Db.S(h.Rows[0][1]) + ": " + StatusName(status));
            });
        }

        /// <summary>Deletes a draft request that has no linked receipt.</summary>
        public static void Delete(int id)
        {
            Demand();
            Db.InTx(delegate(DbTransaction tx)
            {
                DataTable h = Db.Q(tx, "SELECT status, menu_no FROM preq WHERE preq_no = ?", id);
                if (h.Rows.Count == 0) return;
                if (Db.I(h.Rows[0][0]) != Draft) throw new UserError("يمكن حذف المسودات فقط. الطلب المُرسل يُلغى (يبقى محفوظا كملغى).");
                if (Db.I(Db.S1(tx, "SELECT COUNT(*) FROM shop WHERE preq_no = ?", id)) > 0) throw new UserError("هذا الطلب مرتبط بسند إدخال ولا يمكن حذفه.");
                Db.X(tx, "DELETE FROM preq_menu WHERE seq = ?", id);
                Db.X(tx, "DELETE FROM preq WHERE preq_no = ?", id);
                Db.Log(tx, "حذف طلب شراء", "رقم " + Db.S(h.Rows[0][1]));
            });
        }

        /// <summary>Creates a draft request with the suggested quantities of the given critical-report rows (Suggest &gt; 0).
        /// skipOpen: leave out items that already wait in an open request of the store. Returns the new id (0 = nothing to request).</summary>
        public static int CreateFromCritical(IEnumerable<Reports.CriticalRow> rows, int store, bool skipOpen, out int skipped)
        {
            skipped = 0;
            Dictionary<int, double> onOrder = skipOpen ? OnOrder(store) : new Dictionary<int, double>();
            PReq r = new PReq();
            r.Store = store;
            r.Note = "من تقرير الأرصدة الحرجة " + UI.Date(DateTime.Today);
            foreach (Reports.CriticalRow c in rows)
            {
                if (c.Suggest <= 0) continue;
                if (onOrder.ContainsKey(c.Item)) { skipped++; continue; }
                PReqLine l = new PReqLine(); l.ItemNo = c.Item; l.Qty = c.Suggest;
                r.Lines.Add(l);
            }
            if (r.Lines.Count == 0) return 0;
            return Save(r);
        }

        // ------------------------------------------------------------ receipts
        /// <summary>Recomputes received_qty of every line from the receipts linked to the request (shop.preq_no) and the
        /// status (2 partly received, 3 complete; back to 1 when nothing is received any more). Inside the caller's transaction.</summary>
        public static void Recalc(DbTransaction tx, int id)
        {
            DataTable h = Db.Q(tx, "SELECT status FROM preq WHERE preq_no = ?", id);
            if (h.Rows.Count == 0) return;
            int st = Db.I(h.Rows[0][0]);
            Dictionary<int, double> rec = new Dictionary<int, double>();
            foreach (DataRow x in Db.Q(tx, "SELECT l.item_no, SUM(l.qty) FROM shop AS h INNER JOIN shop_menu AS l ON h.shop_no = l.seq WHERE h.preq_no = ? GROUP BY l.item_no", id).Rows)
                rec[Db.I(x[0])] = Db.D(x[1]);
            DataTable lines = Db.Q(tx, "SELECT id, item_no, qty, received_qty FROM preq_menu WHERE seq = ? ORDER BY id", id);
            bool any = false, all = lines.Rows.Count > 0;
            for (int i = 0; i < lines.Rows.Count; i++)
            {
                DataRow x = lines.Rows[i];
                int it = Db.I(x["item_no"]);
                double avail; rec.TryGetValue(it, out avail);
                bool lastOfItem = true;
                for (int j = i + 1; j < lines.Rows.Count; j++) if (Db.I(lines.Rows[j]["item_no"]) == it) { lastOfItem = false; break; }
                double give = lastOfItem ? avail : Math.Min(avail, Db.D(x["qty"]));
                rec[it] = avail - give;
                if (Math.Abs(give - Db.D(x["received_qty"])) > 1e-9) Db.X(tx, "UPDATE preq_menu SET received_qty = ? WHERE id = ?", give, Db.I(x["id"]));
                if (give > 1e-9) any = true;
                if (give < Db.D(x["qty"]) - 1e-9) all = false;
            }
            if (st == Cancelled) return;
            int ns = any && all ? Done : any ? Partial : (st == Partial || st == Done ? Sent : st);
            if (ns != st) Db.X(tx, "UPDATE preq SET status = ? WHERE preq_no = ?", ns, id);
        }

        /// <summary>Links a saved receipt to a request (or unlinks it: preqNo 0) and updates both requests.</summary>
        public static void Link(int receiptId, int preqNo)
        {
            if (!Db.HasPreq) throw new UserError(NotReady);
            Db.InTx(delegate(DbTransaction tx)
            {
                int old = Db.I(Db.S1(tx, "SELECT preq_no FROM shop WHERE shop_no = ?", receiptId));
                Db.X(tx, "UPDATE shop SET preq_no = ? WHERE shop_no = ?", preqNo > 0 ? (object)preqNo : DBNull.Value, receiptId);
                if (preqNo > 0) Recalc(tx, preqNo);
                if (old > 0 && old != preqNo) Recalc(tx, old);
                Db.Log(tx, "ربط إدخال بطلب شراء", "سند " + Db.S(Db.S1(tx, "SELECT menu_no FROM shop WHERE shop_no = ?", receiptId)) + " ← طلب " +
                       (preqNo > 0 ? Db.S(Db.S1(tx, "SELECT menu_no FROM preq WHERE preq_no = ?", preqNo)) : "-"));
            });
        }

        /// <summary>After a receipt was saved: updates received_qty and status of the request it is linked to (if any).</summary>
        public static void ApplyReceipt(int receiptId)
        {
            if (!Db.HasPreq || receiptId <= 0) return;
            Db.InTx(delegate(DbTransaction tx)
            {
                int pq = Db.I(Db.S1(tx, "SELECT preq_no FROM shop WHERE shop_no = ?", receiptId));
                if (pq > 0) Recalc(tx, pq);
            });
        }

        /// <summary>Recomputes all requests that are not cancelled (receipts may have been edited or deleted). Writes only changes.</summary>
        public static void RecalcAll()
        {
            if (!Db.HasPreq) return;
            List<int> ids = new List<int>();
            foreach (DataRow x in Db.Query("SELECT preq_no FROM preq WHERE status IN (0, 1, 2, 3)").Rows) ids.Add(Db.I(x[0]));
            if (ids.Count == 0) return;
            Db.InTx(delegate(DbTransaction tx) { foreach (int id in ids) Recalc(tx, id); });
        }

        /// <summary>One-line hook for the receipt screen, called after a successful save. A receipt already linked is simply
        /// re-applied. Otherwise (advanced mode only, never in beginner mode) a small question is shown when open requests
        /// wait for items of this receipt.</summary>
        public static void AfterReceiptSave(IWin32Window owner, Doc d)
        {
            if (!Db.HasPreq || d == null || d.Kind != DocKind.Receipt || d.Id <= 0) return;
            try
            {
                if (Db.I(Db.Scalar("SELECT preq_no FROM shop WHERE shop_no = ?", d.Id)) > 0) { ApplyReceipt(d.Id); return; }
                if (!Session.Advanced || !CanEdit) return;
                DataTable open = OpenFor(d.Store, d.Lines.Select(l => l.ItemNo));
                if (open.Rows.Count == 0) return;
                using (PreqLinkDialog dlg = new PreqLinkDialog(open, d))
                    if (dlg.ShowDialog(owner) == DialogResult.OK && dlg.Chosen > 0) Link(d.Id, dlg.Chosen);
            }
            catch (UserError ue) { UI.Warn(ue.Message); }
            catch (Exception ex) { UI.Error(ex); }
        }
    }
}
