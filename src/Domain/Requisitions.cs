using System.Collections.Generic;
using System.Data.Common;
using System.Data;
using System.Globalization;
using System.Linq;
using System;

namespace Stores
{
    /// <summary>
    /// Material requisitions «طلبات صرف المواد» with approval (like SAP MM reservations / Odoo approvals):
    ///   مسودة → إرسال للموافقة → (تلقائيا دون «حد الاعتماد») موافق عليه / بانتظار الموافقة → موافق عليه أو مرفوض
    ///   → «صرف الطلب» opens a new issue pre-filled with the remaining quantities → صُرف جزئيا / مكتمل.
    /// The issued quantities are taken from the issues linked to the request (table mreq_issue), so editing or deleting
    /// such an issue corrects the request the next time it is recalculated. Posting / FIFO / balances are not touched.
    /// Every status change is written to app_log.
    /// </summary>
    static class Requisitions
    {
        public const int Draft = 0, Pending = 1, Approved = 2, Rejected = 3, Partial = 4, Done = 5, Cancelled = 9;
        /// <summary>New permission (row added to disply_command): «اعتماد طلبات الصرف».</summary>
        public const int PermApprove = 82;
        public const string PermName = "اعتماد طلبات الصرف";
        const string LimitKey = "approval_limit_value";
        const double Eps = 1e-9;
        static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

        /// <summary>True when the tables exist (set by Db.EnsureAppTables).</summary>
        public static bool Ready;

        public static bool CanApprove { get { return Session.Can(PermApprove); } }

        public static string StatusName(int s)
        {
            switch (s)
            {
                case Draft: return "مسودة";
                case Pending: return "بانتظار الموافقة";
                case Approved: return "موافق عليه";
                case Rejected: return "مرفوض";
                case Partial: return "صُرف جزئيا";
                case Done: return "مكتمل";
                case Cancelled: return "ملغى";
                default: return "";
            }
        }

        public static readonly int[] AllStatuses = { Draft, Pending, Approved, Rejected, Partial, Done, Cancelled };

        /// <summary>Adds the permission row (command 82) once; the administrator (user1) has it.</summary>
        public static void EnsurePermission()
        {
            try
            {
                if (!Db.TableExists("disply_command")) return;
                if (Db.I(Db.Scalar("SELECT COUNT(*) FROM disply_command WHERE command_no = ?", PermApprove)) > 0) return;
                Db.Exec("INSERT INTO disply_command (command_no, command_name, user1, user2, user3, user4, user5) VALUES (?, ?, ?, ?, ?, ?, ?)",
                        PermApprove, PermName, true, false, false, false, false);
            }
            catch (Exception ex) { Db.SchemaWarning = "disply_command " + PermApprove + ": " + ex.Message; }
        }

        // ------------------------------------------------------------ approval limit «حد الاعتماد»
        /// <summary>Requests whose estimated value is below this limit are approved automatically when sent (0 = every request needs approval).</summary>
        public static double ApprovalLimit
        {
            get
            {
                double v;
                return double.TryParse(Db.GetSetting(LimitKey, "0"), NumberStyles.Any, Inv, out v) && v > 0 ? v : 0;
            }
        }

        public static void SetApprovalLimit(double v)
        {
            if (!Session.IsAdmin) throw new UserError("تعديل حد الاعتماد للمدير فقط.");
            if (v < 0) v = 0;
            Db.SetSetting(LimitKey, v.ToString("0.####", Inv));
            Db.Log(null, "حد اعتماد طلبات الصرف", v == 0 ? "كل الطلبات تحتاج موافقة" : UI.Num(v));
        }

        /// <summary>Last receipt cost of an item (read only: إدخال مخزني lines with a price, newest first).</summary>
        public static double LastCost(int item)
        {
            return Db.D(Db.Scalar("SELECT TOP 1 l.cost FROM shop_menu AS l INNER JOIN shop AS h ON h.shop_no = l.seq " +
                                  "WHERE l.item_no = ? AND l.cost > 0 AND h.shop_type <> 3 AND " + Db.NotCountAdj("h") + " ORDER BY h.menu_date DESC, l.auto_seq DESC", item));
        }

        /// <summary>Estimated value = Σ qty × last receipt cost; <paramref name="unknown"/> = lines whose cost is not known.</summary>
        public static double Value(MreqDoc d, out int unknown)
        {
            double v = 0; unknown = 0;
            foreach (MreqLine l in d.Lines)
            {
                double c = LastCost(l.ItemNo);
                if (c <= 0) unknown++;
                v += l.Qty * c;
            }
            return Math.Round(v, 4);
        }

        static void RequireReady()
        {
            if (!Ready) throw new UserError("طلبات الصرف غير مفعّلة بعد في ملف البيانات (أعد تشغيل البرنامج على حاسوب لا يفتح عليه أحد الملف ببرنامج Access).");
        }

        // ------------------------------------------------------------ read
        public static MreqDoc Load(int no)
        {
            if (!Ready) return null;
            DataTable h = Db.Query("SELECT * FROM mreq WHERE mreq_no = ?", no);
            if (h.Rows.Count == 0) return null;
            DataRow r = h.Rows[0];
            MreqDoc d = new MreqDoc();
            d.No = no; d.MenuNo = Db.S(r["menu_no"]); d.Date = Db.Dt(r["mreq_date"]); d.Project = Db.I(r["proj_no"]); d.Store = Db.I(r["stor"]);
            d.RequestedBy = Db.S(r["requested_by"]); d.Status = Db.I(r["status"]); d.ApprovedBy = Db.S(r["approved_by"]); d.ApprovedAt = Db.Dt(r["approved_at"]);
            d.RejectReason = Db.S(r["reject_reason"]); d.Notes = Db.S(r["notes"]); d.UserName = Db.S(r["user_name"]); d.Value = Db.D(r["est_value"]);
            d.ClosedBy = Db.S(r["closed_by"]);
            foreach (DataRow x in Db.Query("SELECT id, item_no, qty, issued_qty FROM mreq_menu WHERE seq = ? ORDER BY id", no).Rows)
            {
                MreqLine l = new MreqLine();
                l.Id = Db.I(x["id"]); l.ItemNo = Db.I(x["item_no"]); l.Qty = Db.D(x["qty"]); l.Issued = Db.D(x["issued_qty"]);
                d.Lines.Add(l);
            }
            return d;
        }

        /// <summary>Requests for the list screen. status: -1 all, -2 waiting for my approval, -3 open (not finished), else that status.</summary>
        public static DataTable List(int status, string text)
        {
            if (!Ready) return new DataTable();
            string sql = "SELECT h.mreq_no, h.menu_no, h.mreq_date, h.proj_no, h.stor, h.requested_by, h.status, h.approved_by, h.est_value, h.user_name, h.notes, " +
                         "(SELECT COUNT(*) FROM mreq_menu m WHERE m.seq = h.mreq_no) AS n, (SELECT SUM(qty) FROM mreq_menu m WHERE m.seq = h.mreq_no) AS q, " +
                         "(SELECT SUM(issued_qty) FROM mreq_menu m WHERE m.seq = h.mreq_no) AS iq FROM mreq h WHERE 1=1";
            List<object> a = new List<object>();
            if (status == -2) { sql += " AND h.status = ?"; a.Add(Pending); }
            else if (status == -3) sql += " AND h.status IN (0, 1, 2, 3, 4)";
            else if (status >= 0) { sql += " AND h.status = ?"; a.Add(status); }
            string s = UI.NormDigits((text ?? "").Trim());
            if (s.Length > 0) { sql += " AND (h.menu_no = ? OR h.requested_by LIKE ? OR h.notes LIKE ?)"; a.Add(s); a.Add("%" + s + "%"); a.Add("%" + s + "%"); }
            sql += " ORDER BY h.mreq_date DESC, h.mreq_no DESC";
            DataTable t = Db.Query(sql, a.ToArray());
            t.Columns.Add("pname", typeof(string)); t.Columns.Add("sname", typeof(string)); t.Columns.Add("status_name", typeof(string));
            foreach (DataRow r in t.Rows)
            {
                r["pname"] = Cache.Project(Db.I(r["proj_no"]));
                r["sname"] = Cache.Store(Db.I(r["stor"]));
                r["status_name"] = StatusName(Db.I(r["status"]));
            }
            if (status == -2 && !Session.IsAdmin)
                foreach (DataRow r in t.Select()) if (Db.S(r["user_name"]) == (Session.UserName ?? "")) t.Rows.Remove(r);   // not my own requests
            return t;
        }

        /// <summary>Number of requests waiting for the current user's approval (0 without the permission).</summary>
        public static int PendingForMe()
        {
            if (!Ready || !CanApprove) return 0;
            try
            {
                if (Session.IsAdmin) return Db.I(Db.Scalar("SELECT COUNT(*) FROM mreq WHERE status = ?", Pending));
                return Db.I(Db.Scalar("SELECT COUNT(*) FROM mreq WHERE status = ? AND (user_name IS NULL OR user_name <> ?)", Pending, Session.UserName ?? ""));
            }
            catch { return 0; }
        }

        /// <summary>Lines still to issue (item, remaining quantity) of an approved / partly issued request.</summary>
        public static List<KeyValuePair<int, double>> Remaining(int no)
        {
            List<KeyValuePair<int, double>> l = new List<KeyValuePair<int, double>>();
            MreqDoc d = Load(no);
            if (d == null) return l;
            foreach (MreqLine x in d.Lines) if (x.Remaining > Eps) l.Add(new KeyValuePair<int, double>(x.ItemNo, x.Remaining));
            return l;
        }

        static bool Mine(MreqDoc d) { return Session.IsAdmin || d.UserName == (Session.UserName ?? ""); }

        public static bool CanEdit(MreqDoc d) { return (d.Status == Draft || d.Status == Rejected) && (d.No == 0 || Mine(d) || CanApprove); }

        // ------------------------------------------------------------ write
        /// <summary>Saves a draft (new, or a draft / rejected request: a rejected one becomes a draft again). Returns mreq_no.</summary>
        public static int Save(MreqDoc d)
        {
            RequireReady();
            if (!Session.Can(Session.IssView) && !CanApprove) throw new UserError("ليس لديك صلاحية لطلبات الصرف.");
            if (d.Project == 0) throw new UserError("اختر مركز الكلفة الذي تُطلب له المواد.");
            if (d.Store == 0) throw new UserError("اختر المخزن.");
            if (string.IsNullOrEmpty((d.RequestedBy ?? "").Trim())) throw new UserError("اكتب اسم طالب المواد.");
            // one line per item
            List<MreqLine> lines = new List<MreqLine>();
            foreach (MreqLine l in d.Lines)
            {
                if (l.ItemNo == 0) continue;
                if (l.Qty <= 0) throw new UserError("الكمية يجب أن تكون أكبر من صفر: " + Cache.ItemName(l.ItemNo));
                MreqLine m = lines.FirstOrDefault(x => x.ItemNo == l.ItemNo);
                if (m != null) m.Qty += l.Qty; else lines.Add(new MreqLine { ItemNo = l.ItemNo, Qty = l.Qty });
            }
            if (lines.Count == 0) throw new UserError("أضف مادة واحدة على الأقل إلى الطلب.");
            if (d.No > 0)
            {
                MreqDoc cur = Load(d.No);
                if (cur == null) throw new UserError("هذا الطلب غير موجود (ربما حُذف من حاسوب آخر).");
                if (!CanEdit(cur)) throw new UserError("لا يمكن تعديل طلب حالته «" + StatusName(cur.Status) + "».");
            }
            DateTime now = Db.Now();
            Db.InTx(delegate(DbTransaction tx)
            {
                if (d.No == 0)
                {
                    d.MenuNo = NextNo(tx);
                    d.UserName = Session.UserName ?? "";
                    d.No = Db.Insert(tx, "INSERT INTO mreq (menu_no, mreq_date, proj_no, stor, requested_by, status, notes, user_name, created) VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?)",
                        d.MenuNo, d.Date.Date, d.Project, d.Store, d.RequestedBy.Trim(), Draft, Db.Null(d.Notes), d.UserName, now);
                    Db.Log(tx, "طلب صرف: مسودة جديدة", "رقم " + d.MenuNo + " - " + lines.Count + " مادة - مركز الكلفة: " + Cache.Project(d.Project));
                }
                else
                {
                    int old = Db.I(Db.S1(tx, "SELECT status FROM mreq WHERE mreq_no = ?", d.No));
                    int n = Db.X(tx, "UPDATE mreq SET mreq_date = ?, proj_no = ?, stor = ?, requested_by = ?, status = ?, notes = ? WHERE mreq_no = ? AND status IN (0, 3)",
                        d.Date.Date, d.Project, d.Store, d.RequestedBy.Trim(), Draft, Db.Null(d.Notes), d.No);
                    if (n == 0) throw new UserError("تغيّرت حالة الطلب من حاسوب آخر. أعد فتحه.");
                    Db.X(tx, "DELETE FROM mreq_menu WHERE seq = ?", d.No);
                    Db.Log(tx, old == Rejected ? "طلب صرف: إعادة إلى مسودة" : "طلب صرف: تعديل", "رقم " + d.MenuNo + " - " + lines.Count + " مادة");
                }
                foreach (MreqLine l in lines)
                    l.Id = Db.Insert(tx, "INSERT INTO mreq_menu (seq, item_no, qty, issued_qty) VALUES (?, ?, ?, 0)", d.No, l.ItemNo, l.Qty);
            });
            d.Lines = lines; d.Status = Draft;
            return d.No;
        }

        static string NextNo(DbTransaction tx)
        {
            return (Db.I(Db.D(Db.S1(tx, "SELECT MAX(" + Db.NumOf("menu_no") + ") FROM mreq"))) + 1).ToString(Inv);
        }

        /// <summary>«إرسال للموافقة»: a draft goes to the approvers, or is approved at once when its value is below
        /// the approval limit and every item has a known cost. Returns the new status.</summary>
        public static int Send(int no)
        {
            RequireReady();
            MreqDoc d = Load(no);
            if (d == null) throw new UserError("هذا الطلب غير موجود.");
            if (d.Status != Draft) throw new UserError("يُرسل للموافقة طلب في حالة «مسودة» فقط (حالته الآن «" + StatusName(d.Status) + "»).");
            if (!Mine(d) && !CanApprove) throw new UserError("يرسل الطلب من أنشأه.");
            if (d.Lines.Count == 0) throw new UserError("لا توجد مواد في الطلب.");
            int unknown;
            double value = Value(d, out unknown);
            double limit = ApprovalLimit;                          // settings are read before the transaction
            bool auto = limit > 0 && value < limit && unknown == 0;
            int st = auto ? Approved : Pending;
            DateTime now = Db.Now();
            Db.InTx(delegate(DbTransaction tx)
            {
                int n = auto
                    ? Db.X(tx, "UPDATE mreq SET status = ?, est_value = ?, approved_by = ?, approved_at = ?, reject_reason = NULL WHERE mreq_no = ? AND status = ?", st, value, "تلقائي (دون حد الاعتماد)", now, no, Draft)
                    : Db.X(tx, "UPDATE mreq SET status = ?, est_value = ?, approved_by = NULL, approved_at = NULL, reject_reason = NULL WHERE mreq_no = ? AND status = ?", st, value, no, Draft);
                if (n == 0) throw new UserError("تغيّرت حالة الطلب من حاسوب آخر. أعد فتحه.");
                Db.Log(tx, auto ? "طلب صرف: موافقة تلقائية" : "طلب صرف: إرسال للموافقة",
                       "رقم " + d.MenuNo + " - القيمة " + UI.Num(value) + (limit > 0 ? " - حد الاعتماد " + UI.Num(limit) : "") + (unknown > 0 ? " - " + unknown + " مادة بلا كلفة" : ""));
            });
            return st;
        }

        static void CheckApprover(MreqDoc d)
        {
            if (!CanApprove) throw new UserError("ليس لديك صلاحية «" + PermName + "».");
            if (!Session.IsAdmin && d.UserName == (Session.UserName ?? "")) throw new UserError("لا يمكن اعتماد أو رفض طلب أنشأته بنفسك (فصل المهام).");
            if (d.Status != Pending) throw new UserError("الطلب ليس بانتظار الموافقة (حالته «" + StatusName(d.Status) + "»).");
        }

        public static void Approve(int no)
        {
            RequireReady();
            MreqDoc d = Load(no);
            if (d == null) throw new UserError("هذا الطلب غير موجود.");
            CheckApprover(d);
            DateTime now = Db.Now();
            Db.InTx(delegate(DbTransaction tx)
            {
                if (Db.X(tx, "UPDATE mreq SET status = ?, approved_by = ?, approved_at = ?, reject_reason = NULL WHERE mreq_no = ? AND status = ?", Approved, Session.UserName ?? "", now, no, Pending) == 0)
                    throw new UserError("تغيّرت حالة الطلب من حاسوب آخر. أعد فتحه.");
                Db.Log(tx, "طلب صرف: موافقة", "رقم " + d.MenuNo + " - مركز الكلفة: " + Cache.Project(d.Project));
            });
        }

        public static void Reject(int no, string reason)
        {
            RequireReady();
            reason = (reason ?? "").Trim();
            if (reason.Length == 0) throw new UserError("اكتب سبب الرفض.");
            if (reason.Length > 250) reason = reason.Substring(0, 250);
            MreqDoc d = Load(no);
            if (d == null) throw new UserError("هذا الطلب غير موجود.");
            CheckApprover(d);
            DateTime now = Db.Now();
            Db.InTx(delegate(DbTransaction tx)
            {
                if (Db.X(tx, "UPDATE mreq SET status = ?, approved_by = ?, approved_at = ?, reject_reason = ? WHERE mreq_no = ? AND status = ?", Rejected, Session.UserName ?? "", now, reason, no, Pending) == 0)
                    throw new UserError("تغيّرت حالة الطلب من حاسوب آخر. أعد فتحه.");
                Db.Log(tx, "طلب صرف: رفض", "رقم " + d.MenuNo + " - السبب: " + reason);
            });
        }

        /// <summary>«إلغاء الطلب»: only before anything was issued from it.</summary>
        public static void Cancel(int no)
        {
            RequireReady();
            MreqDoc d = Load(no);
            if (d == null) throw new UserError("هذا الطلب غير موجود.");
            if (!(d.Status == Draft || d.Status == Pending || d.Status == Approved || d.Status == Rejected))
                throw new UserError(d.Status == Partial ? "صُرف جزء من الطلب: استعمل «إغلاق الطلب» بدلا من الإلغاء." : "لا يمكن إلغاء طلب حالته «" + StatusName(d.Status) + "».");
            if (!Mine(d) && !CanApprove) throw new UserError("يلغي الطلب من أنشأه أو من لديه صلاحية «" + PermName + "».");
            Db.InTx(delegate(DbTransaction tx)
            {
                if (Db.X(tx, "UPDATE mreq SET status = ? WHERE mreq_no = ? AND status = ?", Cancelled, no, d.Status) == 0)
                    throw new UserError("تغيّرت حالة الطلب من حاسوب آخر. أعد فتحه.");
                Db.Log(tx, "طلب صرف: إلغاء", "رقم " + d.MenuNo + " (كان " + StatusName(d.Status) + ")");
            });
        }

        /// <summary>«إغلاق الطلب»: a partly issued request is finished without issuing the rest.</summary>
        public static void Close(int no)
        {
            RequireReady();
            MreqDoc d = Load(no);
            if (d == null) throw new UserError("هذا الطلب غير موجود.");
            if (d.Status != Partial) throw new UserError("يُغلق طلب «صُرف جزئيا» فقط.");
            if (!CanApprove && !Session.Can(Session.IssAdd)) throw new UserError("ليس لديك صلاحية لإغلاق الطلب.");
            Db.InTx(delegate(DbTransaction tx)
            {
                if (Db.X(tx, "UPDATE mreq SET status = ?, closed_by = ? WHERE mreq_no = ? AND status = ?", Done, Session.UserName ?? "", no, Partial) == 0)
                    throw new UserError("تغيّرت حالة الطلب من حاسوب آخر. أعد فتحه.");
                Db.Log(tx, "طلب صرف: إغلاق", "رقم " + d.MenuNo + " دون صرف المتبقي");
            });
        }

        // ------------------------------------------------------------ issues made from a request
        /// <summary>Called once by DocView after an issue opened with «صرف الطلب» was saved: links the issue to the request
        /// and updates the issued quantities / status. <paramref name="linked"/> = issue already linked from this screen (0 = none),
        /// <paramref name="oldId"/> = the document id before saving (0 = it was new). Returns the linked issue id.
        /// Never throws: the issue itself is already saved.</summary>
        public static int IssueSaved(int no, int linked, int oldId, Doc d)
        {
            if (no == 0 || d == null || d.Kind != DocKind.Issue || d.Id == 0) return linked;
            if (linked == 0 ? oldId != 0 : linked != d.Id) return linked;      // another document opened in the same screen
            try
            {
                MreqDoc m = Load(no);
                if (m == null) return linked;
                if (m.Project != d.Project || m.Store != d.Store)
                {
                    UI.Warn("حُفظ السند، لكنه لم يُربط بطلب الصرف رقم " + m.MenuNo + " لأن مركز الكلفة أو المخزن يختلف عن الطلب.");
                    return linked;
                }
                if (linked == 0 && m.Status != Approved && m.Status != Partial)
                {
                    UI.Warn("حُفظ السند، لكن طلب الصرف رقم " + m.MenuNo + " حالته «" + StatusName(m.Status) + "» فلم يُربط به.");
                    return linked;
                }
                DateTime now = Db.Now();
                Db.InTx(delegate(DbTransaction tx)
                {
                    if (Db.I(Db.S1(tx, "SELECT COUNT(*) FROM mreq_issue WHERE mreq_no = ? AND sale_id = ?", no, d.Id)) == 0)
                    {
                        Db.X(tx, "INSERT INTO mreq_issue (mreq_no, sale_id, linked_at) VALUES (?, ?, ?)", no, d.Id, now);
                        Db.Log(tx, "طلب صرف: صرف", "طلب رقم " + m.MenuNo + " - سند إخراج رقم " + d.No);
                    }
                    Recalc(tx, no);
                });
                return d.Id;
            }
            catch (Exception ex)
            {
                UI.Warn("حُفظ سند الإخراج، لكن تعذر تحديث طلب الصرف: " + ex.Message);
                return linked;
            }
        }

        /// <summary>Recomputes issued_qty and the status of one request from its linked issues.</summary>
        public static void Recalc(int no)
        {
            if (!Ready) return;
            Db.InTx(delegate(DbTransaction tx) { Recalc(tx, no); });
        }

        /// <summary>Recalculates the approved / partly issued requests (the list screen calls it, so an issue edited or
        /// deleted later is reflected).</summary>
        public static void RecalcOpen()
        {
            if (!Ready) return;
            List<int> open = new List<int>();
            foreach (DataRow r in Db.Query("SELECT DISTINCT h.mreq_no FROM mreq AS h INNER JOIN mreq_issue AS x ON x.mreq_no = h.mreq_no WHERE h.status IN (2, 4) OR (h.status = 5 AND (h.closed_by IS NULL OR h.closed_by = ''))").Rows) open.Add(Db.I(r[0]));
            if (open.Count == 0) return;
            Db.InTx(delegate(DbTransaction tx) { foreach (int no in open) Recalc(tx, no); });
        }

        static void Recalc(DbTransaction tx, int no)
        {
            DataTable h = Db.Q(tx, "SELECT menu_no, status, closed_by FROM mreq WHERE mreq_no = ?", no);
            if (h.Rows.Count == 0) return;
            int status = Db.I(h.Rows[0]["status"]);
            Dictionary<int, double> issued = new Dictionary<int, double>();
            foreach (DataRow r in Db.Q(tx,
                "SELECT l.item_no, SUM(l.qty) AS q FROM ((mreq_issue AS x INNER JOIN mreq AS m ON m.mreq_no = x.mreq_no) " +
                "INNER JOIN sale AS s ON s.shop_no = x.sale_id) INNER JOIN sale_menu AS l ON l.seq = s.shop_no " +
                "WHERE x.mreq_no = ? AND s.proj_no = m.proj_no AND s.stor = m.stor AND " + Db.IssueOnly("s") + " GROUP BY l.item_no", no).Rows)
                issued[Db.I(r[0])] = Db.D(r[1]);
            DataTable lines = Db.Q(tx, "SELECT id, item_no, qty, issued_qty FROM mreq_menu WHERE seq = ? ORDER BY id", no);
            bool any = false, all = lines.Rows.Count > 0;
            for (int i = 0; i < lines.Rows.Count; i++)
            {
                DataRow r = lines.Rows[i];
                int it = Db.I(r["item_no"]); double q = Db.D(r["qty"]);
                double avail; issued.TryGetValue(it, out avail);
                bool lastOfItem = true;
                for (int j = i + 1; j < lines.Rows.Count; j++) if (Db.I(lines.Rows[j]["item_no"]) == it) { lastOfItem = false; break; }
                double give = lastOfItem ? avail : Math.Min(avail, q);       // more than requested: shown on the item's last line
                issued[it] = avail - give;
                give = Math.Round(Math.Max(0, give), 6);
                if (give > Eps) any = true;
                if (give < q - Eps) all = false;
                if (Math.Abs(give - Db.D(r["issued_qty"])) > Eps) Db.X(tx, "UPDATE mreq_menu SET issued_qty = ? WHERE id = ?", give, Db.I(r["id"]));
            }
            bool closed = Db.S(h.Rows[0]["closed_by"]).Length > 0;
            if (!(status == Approved || status == Partial || (status == Done && !closed))) return;
            int ns = any ? (all ? Done : Partial) : Approved;
            if (ns == status) return;
            Db.X(tx, "UPDATE mreq SET status = ? WHERE mreq_no = ?", ns, no);
            Db.Log(tx, "طلب صرف: " + StatusName(ns), "رقم " + Db.S(h.Rows[0]["menu_no"]) + " (كان " + StatusName(status) + ")");
        }

        /// <summary>Issues linked to a request (id, number, date) for the request screen.</summary>
        public static DataTable LinkedIssues(int no)
        {
            if (!Ready) return new DataTable();
            return Db.Query("SELECT s.shop_no, s.menu_no, s.menu_date FROM mreq_issue AS x INNER JOIN sale AS s ON s.shop_no = x.sale_id WHERE x.mreq_no = ? ORDER BY s.menu_date, s.shop_no", no);
        }

        // ------------------------------------------------------------ print / report
        /// <summary>The printed request with the signatures «طالب المواد / المعتمد / أمين المخزن».</summary>
        public static ReportData Print(MreqDoc d)
        {
            ReportData r = new ReportData();
            r.Title = "طلب صرف مواد رقم " + d.MenuNo;
            r.Info = new List<KeyValuePair<string, string>>();
            Action<string, string> info = delegate(string k, string v) { if (!string.IsNullOrEmpty(v)) r.Info.Add(new KeyValuePair<string, string>(k, v)); };
            info("التاريخ", UI.Date(d.Date));
            info("مركز الكلفة", Cache.Project(d.Project));
            info("من مخزن", Cache.Store(d.Store));
            info("طالب المواد", d.RequestedBy);
            info("الحالة", StatusName(d.Status));
            if (d.Status == Approved || d.Status == Partial || d.Status == Done)
            {
                info("المعتمد", d.ApprovedBy);
                if (d.ApprovedAt > DateTime.MinValue) info("تاريخ الاعتماد", UI.Date(d.ApprovedAt));
            }
            if (d.Status == Rejected) { info("رفضه", d.ApprovedBy); info("سبب الرفض", d.RejectReason); }
            if (d.Value > 0) info("القيمة التقديرية", UI.Num(d.Value));
            List<string> sub = new List<string>();
            foreach (KeyValuePair<string, string> kv in r.Info) sub.Add(kv.Key + ": " + kv.Value);
            r.Subtitle = string.Join("     |     ", sub.ToArray());
            r.Col("ت", ColKind.Int); r.Col("الرمز", ColKind.Text); r.Col("اسم المادة", ColKind.Text); r.Col("الوحدة", ColKind.Text);
            r.ColT("الكمية المطلوبة"); r.ColT("المصروف"); r.ColT("المتبقي");
            int n = 0;
            foreach (MreqLine l in d.Lines)
                r.Row(null, ++n, Cache.ItemCode(l.ItemNo), Cache.ItemName(l.ItemNo), Cache.ItemUnit(l.ItemNo), l.Qty, l.Issued, l.Remaining);
            if (!string.IsNullOrEmpty(d.Notes)) r.Footer = "ملاحظات: " + d.Notes;
            r.SigLabels = new[] { "طالب المواد", "المعتمد", "أمين المخزن" };
            r.Signatures = true;
            return r;
        }

        /// <summary>Report «طلبات الصرف» (group «الإخراج»): every requested item with its status, requested / issued / remaining.</summary>
        public static ReportData Report(ReportParams p)
        {
            ReportData r = new ReportData();
            r.Title = "طلبات الصرف";
            List<string> sub = new List<string> { "من " + UI.Date(p.From) + " إلى " + UI.Date(p.To) };
            if (p.Store > 0) sub.Add("مخزن: " + Cache.Store(p.Store));
            if (p.ProjNo > 0) sub.Add("مركز الكلفة: " + Cache.Project(p.ProjNo));
            if (p.ItemNo > 0) sub.Add("المادة: " + Cache.ItemName(p.ItemNo));
            r.Subtitle = string.Join("   •   ", sub.ToArray());
            r.Col("ت", ColKind.Int); r.Col("رقم الطلب", ColKind.Text); r.Col("التاريخ", ColKind.Date); r.Col("مركز الكلفة", ColKind.Text); r.Col("طالب المواد", ColKind.Text);
            r.Col("الحالة", ColKind.Text); r.Col("الرمز", ColKind.Text); r.Col("اسم المادة", ColKind.Text); r.Col("الوحدة", ColKind.Text);
            r.ColT("المطلوب"); r.ColT("المصروف"); r.ColT("المتبقي");
            if (!Ready) { r.Footer = "طلبات الصرف غير مفعّلة في ملف البيانات."; return r; }
            try { RecalcOpen(); } catch { }
            string sql = "SELECT h.mreq_no, h.menu_no, h.mreq_date, h.proj_no, h.requested_by, h.status, l.item_no, l.qty, l.issued_qty FROM mreq AS h INNER JOIN mreq_menu AS l ON l.seq = h.mreq_no " +
                         "WHERE h.mreq_date >= ? AND h.mreq_date < ?";
            List<object> a = new List<object> { p.From.Date, p.ToEx };
            if (p.Store > 0) { sql += " AND h.stor = ?"; a.Add(p.Store); }
            if (p.ProjNo > 0) { sql += " AND h.proj_no = ?"; a.Add(p.ProjNo); }
            if (p.ItemNo > 0) { sql += " AND l.item_no = ?"; a.Add(p.ItemNo); }
            sql += " ORDER BY h.mreq_date, h.mreq_no, l.id";
            int n = 0; double tq = 0, ti = 0, tr = 0;
            Dictionary<int, int> byStatus = new Dictionary<int, int>();
            HashSet<int> seen = new HashSet<int>();
            foreach (DataRow x in Db.Query(sql, a.ToArray()).Rows)
            {
                int st = Db.I(x["status"]); int it = Db.I(x["item_no"]);
                double q = Db.D(x["qty"]), iq = Db.D(x["issued_qty"]);
                double rem = st == Cancelled || st == Rejected || st == Done ? 0 : Math.Max(0, q - iq);
                if (seen.Add(Db.I(x["mreq_no"]))) { int c; byStatus.TryGetValue(st, out c); byStatus[st] = c + 1; }
                string cls = st == Cancelled || st == Rejected ? "muted" : st == Pending ? "open" : null;
                r.Row(cls, ++n, Db.S(x["menu_no"]), Db.Dt(x["mreq_date"]), Cache.Project(Db.I(x["proj_no"])), Db.S(x["requested_by"]), StatusName(st),
                      Cache.ItemCode(it), Cache.ItemName(it), Cache.ItemUnit(it), q, iq, rem);
                tq += q; ti += iq; tr += rem;
            }
            r.Sum("عدد الطلبات", seen.Count.ToString("#,##0", Inv));
            foreach (int st in AllStatuses) { int c; if (byStatus.TryGetValue(st, out c)) r.Sum(StatusName(st), c.ToString("#,##0", Inv)); }
            r.Sum("المتبقي للصرف", UI.Num(tr));
            if (n == 0) r.Footer = "لا توجد طلبات صرف في هذه الفترة.";
            return r;
        }
    }
}
