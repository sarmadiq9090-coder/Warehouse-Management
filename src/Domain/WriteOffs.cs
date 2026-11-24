using System.Collections.Generic;
using System.Data.Common;
using System.Data;
using System.Globalization;
using System.Linq;
using System;

namespace Stores
{
    /// <summary>
    /// Write-off / scrap «سند إتلاف / شطب مواد» with approval (as in SAP MM movement type 551 «scrapping» / Odoo scrap orders):
    ///   مسودة → إرسال للاعتماد → بانتظار الاعتماد → «اعتماد وترحيل» (معتمد ومُرحّل) أو مرفوض (يُعدَّل فيعود مسودة) / ملغى.
    /// Until it is approved nothing moves. Approval (a user with «اعتماد سندات الإتلاف», never the one who prepared it, except
    /// the administrator) posts it through the normal posting code (Posting.SaveNewIn: balances, FIFO, numbering) as one sale
    /// row with doc_kind 3 and no cost centre: the stock leaves at FIFO cost like an issue, but it is not an issue to a cost
    /// centre, so Db.IssueOnly / Db.NotCountAdj keep it out of the issue, cost centre and dashboard figures, and the movement
    /// SQL (Reports.MvSql) shows it as its own kind «إتلاف / شطب». The posted sale cannot be edited or deleted (StockCount.CheckNotAdjustment).
    /// Every status change is written to app_log.
    /// </summary>
    static class WriteOffs
    {
        public const int Draft = 0, Pending = 1, Posted = 2, Rejected = 3, Cancelled = 9;
        /// <summary>sale.doc_kind of a posted write-off (0 issue, 1 purchase return, 2 count shortage).</summary>
        public const int SaleKind = 3;
        /// <summary>New permission (row added to disply_command): «اعتماد سندات الإتلاف».</summary>
        public const int PermApprove = 83;
        public const string PermName = "اعتماد سندات الإتلاف";
        public const int Damaged = 1, Expired = 2, Lost = 3, Obsolete = 4, Other = 5;
        public static readonly int[] AllReasons = { Damaged, Expired, Lost, Obsolete, Other };
        public static readonly int[] AllStatuses = { Draft, Pending, Posted, Rejected, Cancelled };
        const double Eps = 1e-9;
        static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

        /// <summary>True when the tables exist (set by EnsureSchema, called from Db.EnsureAppTables).</summary>
        public static bool Ready;

        public static bool CanApprove { get { return Session.Can(PermApprove); } }
        /// <summary>Preparing a write-off: whoever may add issues, or an approver.</summary>
        public static bool CanPrepare { get { return Session.Can(Session.IssAdd) || CanApprove; } }

        public static string ReasonName(int r)
        {
            switch (r)
            {
                case Damaged: return "تالف";
                case Expired: return "منتهي الصلاحية";
                case Lost: return "مفقود";
                case Obsolete: return "تقادم";
                case Other: return "أخرى";
                default: return "";
            }
        }

        public static string StatusName(int s)
        {
            switch (s)
            {
                case Draft: return "مسودة";
                case Pending: return "بانتظار الاعتماد";
                case Posted: return "معتمد ومُرحّل";
                case Rejected: return "مرفوض";
                case Cancelled: return "ملغى";
                default: return "";
            }
        }

        // ------------------------------------------------------------ schema
        /// <summary>Creates wo_head / wo_menu and the permission row (command 83) once. Called by Db.EnsureAppTables.</summary>
        public static void EnsureSchema()
        {
            Create("wo_head",
                "CREATE TABLE wo_head (wo_no INT IDENTITY(1,1) PRIMARY KEY, menu_no NVARCHAR(10), wo_date DATETIME, stor INT, reason INT, notes NVARCHAR(255), status INT, user_name NVARCHAR(50), created DATETIME, approved_by NVARCHAR(50), approved_at DATETIME, reject_reason NVARCHAR(255), sale_id INT, wo_value FLOAT)",
                "CREATE TABLE wo_head (wo_no COUNTER PRIMARY KEY, menu_no TEXT(10), wo_date DATETIME, stor INT, reason INT, notes TEXT(255), status INT, user_name TEXT(50), created DATETIME, approved_by TEXT(50), approved_at DATETIME, reject_reason TEXT(255), sale_id INT, wo_value DOUBLE)");
            Create("wo_menu",
                "CREATE TABLE wo_menu (id INT IDENTITY(1,1) PRIMARY KEY, seq INT, item_no INT, qty FLOAT, reason INT, sale_line INT, cost FLOAT)",
                "CREATE TABLE wo_menu (id COUNTER PRIMARY KEY, seq INT, item_no INT, qty DOUBLE, reason INT, sale_line INT, cost DOUBLE)");
            try
            {
                if (Db.TableExists("disply_command") && Db.I(Db.Scalar("SELECT COUNT(*) FROM disply_command WHERE command_no = ?", PermApprove)) == 0)
                    Db.Exec("INSERT INTO disply_command (command_no, command_name, user1, user2, user3, user4, user5) VALUES (?, ?, ?, ?, ?, ?, ?)",
                            PermApprove, PermName, true, false, false, false, false);
            }
            catch (Exception ex) { Db.SchemaWarning = "disply_command " + PermApprove + ": " + ex.Message; }
            // sale.doc_kind (HasProj) and shop.doc_kind (HasCount) are needed: Db.NotCountAdj filters the write-offs only then
            Ready = Db.HasCount && Db.TableExists("wo_head") && Db.TableExists("wo_menu");
        }

        static void Create(string name, string sqlServer, string access)
        {
            if (Db.TableExists(name)) return;
            try { Db.Exec(Db.IsSql ? sqlServer : access); } catch (Exception ex) { Db.SchemaWarning = name + ": " + ex.Message; }
        }

        static void RequireReady()
        {
            if (!Ready) throw new UserError("سندات الإتلاف غير مفعّلة بعد في ملف البيانات (أعد تشغيل البرنامج على حاسوب لا يفتح عليه أحد الملف ببرنامج Access).");
        }

        // ------------------------------------------------------------ read
        public static WoDoc Load(int no)
        {
            if (!Ready) return null;
            DataTable h = Db.Query("SELECT * FROM wo_head WHERE wo_no = ?", no);
            if (h.Rows.Count == 0) return null;
            DataRow r = h.Rows[0];
            WoDoc d = new WoDoc();
            d.No = no; d.MenuNo = Db.S(r["menu_no"]); d.Date = Db.Dt(r["wo_date"]); d.Store = Db.I(r["stor"]); d.Reason = Db.I(r["reason"]);
            d.Notes = Db.S(r["notes"]); d.Status = Db.I(r["status"]); d.UserName = Db.S(r["user_name"]); d.Created = Db.Dt(r["created"]);
            d.ApprovedBy = Db.S(r["approved_by"]); d.ApprovedAt = Db.Dt(r["approved_at"]); d.RejectReason = Db.S(r["reject_reason"]);
            d.SaleId = Db.I(r["sale_id"]); d.Value = Db.D(r["wo_value"]);
            if (d.SaleId > 0) d.SaleNo = Db.S(Db.Scalar("SELECT menu_no FROM sale WHERE shop_no = ?", d.SaleId));
            foreach (DataRow x in Db.Query("SELECT id, item_no, qty, reason, sale_line, cost FROM wo_menu WHERE seq = ? ORDER BY id", no).Rows)
            {
                WoLine l = new WoLine();
                l.Id = Db.I(x["id"]); l.ItemNo = Db.I(x["item_no"]); l.Qty = Db.D(x["qty"]); l.Reason = Db.I(x["reason"]);
                l.SaleLine = Db.I(x["sale_line"]); l.Cost = Db.D(x["cost"]);
                d.Lines.Add(l);
            }
            return d;
        }

        /// <summary>Write-offs for the list screen. status: -1 all, -2 waiting for my approval, -3 open (not posted / cancelled), else that status.</summary>
        public static DataTable List(int status, string text)
        {
            if (!Ready) return new DataTable();
            string sql = "SELECT h.wo_no, h.menu_no, h.wo_date, h.stor, h.reason, h.status, h.user_name, h.approved_by, h.wo_value, h.notes, h.sale_id, " +
                         "(SELECT COUNT(*) FROM wo_menu m WHERE m.seq = h.wo_no) AS n, (SELECT SUM(qty) FROM wo_menu m WHERE m.seq = h.wo_no) AS q FROM wo_head h WHERE 1=1";
            List<object> a = new List<object>();
            if (status == -2) { sql += " AND h.status = ?"; a.Add(Pending); }
            else if (status == -3) sql += " AND h.status IN (0, 1, 3)";
            else if (status >= 0) { sql += " AND h.status = ?"; a.Add(status); }
            string s = UI.NormDigits((text ?? "").Trim());
            if (s.Length > 0) { sql += " AND (h.menu_no = ? OR h.notes LIKE ? OR h.user_name LIKE ?)"; a.Add(s); a.Add("%" + s + "%"); a.Add("%" + s + "%"); }
            sql += " ORDER BY h.wo_date DESC, h.wo_no DESC";
            DataTable t = Db.Query(sql, a.ToArray());
            t.Columns.Add("sname", typeof(string)); t.Columns.Add("rname", typeof(string)); t.Columns.Add("status_name", typeof(string));
            foreach (DataRow r in t.Rows)
            {
                r["sname"] = Cache.Store(Db.I(r["stor"]));
                r["rname"] = ReasonName(Db.I(r["reason"]));
                r["status_name"] = StatusName(Db.I(r["status"]));
            }
            if (status == -2 && !Session.IsAdmin)
                foreach (DataRow r in t.Select()) if (Db.S(r["user_name"]) == (Session.UserName ?? "")) t.Rows.Remove(r);   // not my own
            return t;
        }

        /// <summary>Number of write-offs waiting for the current user's approval (0 without the permission).</summary>
        public static int PendingForMe()
        {
            if (!Ready || !CanApprove) return 0;
            try
            {
                if (Session.IsAdmin) return Db.I(Db.Scalar("SELECT COUNT(*) FROM wo_head WHERE status = ?", Pending));
                return Db.I(Db.Scalar("SELECT COUNT(*) FROM wo_head WHERE status = ? AND (user_name IS NULL OR user_name <> ?)", Pending, Session.UserName ?? ""));
            }
            catch { return 0; }
        }

        static bool Mine(WoDoc d) { return Session.IsAdmin || d.UserName == (Session.UserName ?? ""); }

        public static bool CanEdit(WoDoc d) { return (d.Status == Draft || d.Status == Rejected) && CanPrepare && (d.No == 0 || Mine(d) || CanApprove); }

        /// <summary>Estimated value before posting = Σ qty × last receipt cost; <paramref name="unknown"/> = lines without a known cost.</summary>
        public static double Estimate(WoDoc d, out int unknown)
        {
            double v = 0; unknown = 0;
            foreach (WoLine l in d.Lines)
            {
                double c = Requisitions.LastCost(l.ItemNo);
                if (c <= 0) unknown++;
                v += l.Qty * c;
            }
            return Math.Round(v, 4);
        }

        // ------------------------------------------------------------ write
        /// <summary>Saves a draft (new, or a draft / rejected write-off: a rejected one becomes a draft again). Returns wo_no.</summary>
        public static int Save(WoDoc d)
        {
            RequireReady();
            if (!CanPrepare) throw new UserError("ليس لديك صلاحية لإعداد سندات الإتلاف (تحتاج صلاحية «إضافة سند إخراج» أو «" + PermName + "»).");
            if (d.Store == 0) throw new UserError("اختر المخزن.");
            if (ReasonName(d.Reason).Length == 0) throw new UserError("اختر سبب الإتلاف.");
            if (d.Date.Date > DateTime.Today) throw new UserError("لا يمكن أن يكون تاريخ سند الإتلاف بعد اليوم.");
            string notes = (d.Notes ?? "").Trim();
            if (notes.Length > 250) notes = notes.Substring(0, 250);
            if (d.Reason == Other && notes.Length == 0) throw new UserError("السبب «أخرى»: اكتب التوضيح في الملاحظات.");
            // one line per item and reason
            List<WoLine> lines = new List<WoLine>();
            foreach (WoLine l in d.Lines)
            {
                if (l.ItemNo == 0) continue;
                if (l.Qty <= 0) throw new UserError("الكمية يجب أن تكون أكبر من صفر: " + Cache.ItemName(l.ItemNo));
                int rs = l.Reason == d.Reason ? 0 : l.Reason;
                WoLine m = lines.FirstOrDefault(x => x.ItemNo == l.ItemNo && x.Reason == rs);
                if (m != null) m.Qty += l.Qty; else lines.Add(new WoLine { ItemNo = l.ItemNo, Qty = l.Qty, Reason = rs });
            }
            if (lines.Count == 0) throw new UserError("أضف مادة واحدة على الأقل إلى سند الإتلاف.");
            if (d.No > 0)
            {
                WoDoc cur = Load(d.No);
                if (cur == null) throw new UserError("هذا السند غير موجود (ربما حُذف من حاسوب آخر).");
                if (!CanEdit(cur)) throw new UserError("لا يمكن تعديل سند إتلاف حالته «" + StatusName(cur.Status) + "».");
            }
            DateTime closed = PeriodLock.Limit();                    // year closing / month lock, read before the transaction starts
            if (closed != DateTime.MinValue && d.Date.Date <= closed) throw PeriodLock.Error(d.Date);
            DateTime now = Db.Now();
            Db.InTx(delegate(DbTransaction tx)
            {
                if (d.No == 0)
                {
                    d.MenuNo = NextNo(tx);
                    d.UserName = Session.UserName ?? "";
                    d.No = Db.Insert(tx, "INSERT INTO wo_head (menu_no, wo_date, stor, reason, notes, status, user_name, created) VALUES (?, ?, ?, ?, ?, ?, ?, ?)",
                        d.MenuNo, d.Date.Date, d.Store, d.Reason, Db.Null(notes), Draft, d.UserName, now);
                    Db.Log(tx, "سند إتلاف: مسودة جديدة", "رقم " + d.MenuNo + " - " + lines.Count + " مادة - " + Cache.Store(d.Store) + " - السبب: " + ReasonName(d.Reason));
                }
                else
                {
                    int old = Db.I(Db.S1(tx, "SELECT status FROM wo_head WHERE wo_no = ?", d.No));
                    int n = Db.X(tx, "UPDATE wo_head SET wo_date = ?, stor = ?, reason = ?, notes = ?, status = ? WHERE wo_no = ? AND status IN (0, 3)",
                        d.Date.Date, d.Store, d.Reason, Db.Null(notes), Draft, d.No);
                    if (n == 0) throw new UserError("تغيّرت حالة سند الإتلاف من حاسوب آخر. أعد فتحه.");
                    Db.X(tx, "DELETE FROM wo_menu WHERE seq = ?", d.No);
                    Db.Log(tx, old == Rejected ? "سند إتلاف: إعادة إلى مسودة" : "سند إتلاف: تعديل", "رقم " + d.MenuNo + " - " + lines.Count + " مادة");
                }
                foreach (WoLine l in lines)
                    l.Id = Db.Insert(tx, "INSERT INTO wo_menu (seq, item_no, qty, reason) VALUES (?, ?, ?, ?)", d.No, l.ItemNo, l.Qty, l.Reason == 0 ? (object)DBNull.Value : l.Reason);
            });
            d.Lines = lines; d.Status = Draft; d.Notes = notes;
            return d.No;
        }

        static string NextNo(DbTransaction tx)
        {
            return ((long)Db.D(Db.S1(tx, "SELECT MAX(" + Db.NumOf("menu_no") + ") FROM wo_head")) + 1).ToString(Inv);
        }

        /// <summary>Quantities above the store balance (item name and shortage), checked when sending and again when posting.</summary>
        static List<string> Shortages(DbTransaction tx, WoDoc d)
        {
            List<string> l = new List<string>();
            foreach (IGrouping<int, WoLine> g in d.Lines.GroupBy(x => x.ItemNo))
            {
                double q = g.Sum(x => x.Qty);
                string sql = "SELECT SUM(item_bal) FROM item_stor WHERE item_no = ? AND stor = ?";
                double b = Db.D(tx == null ? Db.Scalar(sql, g.Key, d.Store) : Db.S1(tx, sql, g.Key, d.Store));
                if (q > b + 1e-6) l.Add(Cache.ItemName(g.Key) + ": المطلوب شطبه " + UI.Num(q) + " والرصيد " + UI.Num(b));
            }
            return l;
        }

        static UserError ShortageError(List<string> s)
        {
            return new UserError("لا يمكن شطب كمية أكبر من رصيد المخزن:\n" + string.Join("\n", s.ToArray()));
        }

        /// <summary>«إرسال للاعتماد»: a draft goes to the approvers.</summary>
        public static void Send(int no)
        {
            RequireReady();
            WoDoc d = Load(no);
            if (d == null) throw new UserError("هذا السند غير موجود.");
            if (d.Status != Draft) throw new UserError("يُرسل للاعتماد سند في حالة «مسودة» فقط (حالته الآن «" + StatusName(d.Status) + "»).");
            if (!Mine(d) && !CanApprove) throw new UserError("يرسل سند الإتلاف من أعدّه.");
            if (d.Lines.Count == 0) throw new UserError("لا توجد مواد في السند.");
            List<string> s = Shortages(null, d);
            if (s.Count > 0) throw ShortageError(s);
            int unknown;
            double est = Estimate(d, out unknown);
            Db.InTx(delegate(DbTransaction tx)
            {
                if (Db.X(tx, "UPDATE wo_head SET status = ?, approved_by = NULL, approved_at = NULL, reject_reason = NULL WHERE wo_no = ? AND status = ?", Pending, no, Draft) == 0)
                    throw new UserError("تغيّرت حالة سند الإتلاف من حاسوب آخر. أعد فتحه.");
                Db.Log(tx, "سند إتلاف: إرسال للاعتماد", "رقم " + d.MenuNo + " - " + d.Lines.Count + " مادة - القيمة التقديرية " + UI.Num(est) + (unknown > 0 ? " - " + unknown + " مادة بلا كلفة" : ""));
            });
        }

        static void CheckApprover(WoDoc d)
        {
            if (!CanApprove) throw new UserError("ليس لديك صلاحية «" + PermName + "».");
            if (!Session.IsAdmin && d.UserName == (Session.UserName ?? "")) throw new UserError("لا يمكن اعتماد أو رفض سند إتلاف أعددته بنفسك (فصل المهام).");
            if (d.Status != Pending) throw new UserError("سند الإتلاف ليس بانتظار الاعتماد (حالته «" + StatusName(d.Status) + "»).");
        }

        /// <summary>«اعتماد وترحيل»: posts the write-off (the stock leaves the store at FIFO cost) and locks it.</summary>
        public static void Approve(int no)
        {
            RequireReady();
            WoDoc d = Load(no);
            if (d == null) throw new UserError("هذا السند غير موجود.");
            CheckApprover(d);
            DateTime closed = PeriodLock.Limit();                    // year closing / month lock, read before the transaction starts
            if (closed != DateTime.MinValue && d.Date.Date <= closed) throw PeriodLock.Error(d.Date);
            DateTime now = Db.Now();
            Db.InTx(delegate(DbTransaction tx)
            {
                // claim it first: a second approver on another computer finds it no longer pending
                if (Db.X(tx, "UPDATE wo_head SET status = ?, approved_by = ?, approved_at = ?, reject_reason = NULL WHERE wo_no = ? AND status = ?", Posted, Session.UserName ?? "", now, no, Pending) == 0)
                    throw new UserError("تغيّرت حالة سند الإتلاف من حاسوب آخر. أعد فتحه.");
                List<string> s = Shortages(tx, d);
                if (s.Count > 0) throw ShortageError(s);
                Doc sale = new Doc(); sale.Kind = DocKind.Issue; sale.Date = d.Date.Date; sale.Store = d.Store;
                sale.Note = "سند إتلاف / شطب مواد رقم " + d.MenuNo + " - " + ReasonName(d.Reason);
                foreach (WoLine l in d.Lines) sale.Lines.Add(new DocLine { ItemNo = l.ItemNo, Qty = l.Qty });
                Posting.SaveNewIn(tx, sale);
                Db.X(tx, "UPDATE sale SET doc_kind = ?, proj_no = NULL WHERE shop_no = ?", SaleKind, sale.Id);
                Dictionary<int, double> cost = new Dictionary<int, double>();
                foreach (DataRow r in Db.Q(tx, "SELECT x.seq_sale, SUM(x.qty * s.cost) AS v FROM (sale_menu AS m INNER JOIN sale_menu_1 AS x ON x.seq_sale = m.auto_seq) " +
                                               "INNER JOIN shop_menu AS s ON s.auto_seq = x.seq_shop WHERE m.seq = ? GROUP BY x.seq_sale", sale.Id).Rows)
                    cost[Db.I(r[0])] = Db.D(r[1]);
                double total = 0;
                for (int i = 0; i < d.Lines.Count; i++)
                {
                    int sl = sale.Lines[i].LineId; double c; cost.TryGetValue(sl, out c);
                    c = Math.Round(c, 4); total += c;
                    Db.X(tx, "UPDATE wo_menu SET sale_line = ?, cost = ? WHERE id = ?", sl, c, d.Lines[i].Id);
                }
                Db.X(tx, "UPDATE wo_head SET sale_id = ?, wo_value = ? WHERE wo_no = ?", sale.Id, Math.Round(total, 4), no);
                Db.Log(tx, "سند إتلاف: اعتماد وترحيل", "رقم " + d.MenuNo + " - " + Cache.Store(d.Store) + " - " + d.Lines.Count + " مادة - الكلفة " + UI.Num(total) + " - سند الترحيل " + sale.No);
            });
        }

        public static void Reject(int no, string reason)
        {
            RequireReady();
            reason = (reason ?? "").Trim();
            if (reason.Length == 0) throw new UserError("اكتب سبب الرفض.");
            if (reason.Length > 250) reason = reason.Substring(0, 250);
            WoDoc d = Load(no);
            if (d == null) throw new UserError("هذا السند غير موجود.");
            CheckApprover(d);
            DateTime now = Db.Now();
            Db.InTx(delegate(DbTransaction tx)
            {
                if (Db.X(tx, "UPDATE wo_head SET status = ?, approved_by = ?, approved_at = ?, reject_reason = ? WHERE wo_no = ? AND status = ?", Rejected, Session.UserName ?? "", now, reason, no, Pending) == 0)
                    throw new UserError("تغيّرت حالة سند الإتلاف من حاسوب آخر. أعد فتحه.");
                Db.Log(tx, "سند إتلاف: رفض", "رقم " + d.MenuNo + " - السبب: " + reason);
            });
        }

        /// <summary>«إلغاء السند»: only before it is posted.</summary>
        public static void Cancel(int no)
        {
            RequireReady();
            WoDoc d = Load(no);
            if (d == null) throw new UserError("هذا السند غير موجود.");
            if (!(d.Status == Draft || d.Status == Pending || d.Status == Rejected))
                throw new UserError("لا يمكن إلغاء سند إتلاف حالته «" + StatusName(d.Status) + "».");
            if (!Mine(d) && !CanApprove) throw new UserError("يلغي السند من أعدّه أو من لديه صلاحية «" + PermName + "».");
            Db.InTx(delegate(DbTransaction tx)
            {
                if (Db.X(tx, "UPDATE wo_head SET status = ? WHERE wo_no = ? AND status = ?", Cancelled, no, d.Status) == 0)
                    throw new UserError("تغيّرت حالة سند الإتلاف من حاسوب آخر. أعد فتحه.");
                Db.Log(tx, "سند إتلاف: إلغاء", "رقم " + d.MenuNo + " (كان " + StatusName(d.Status) + ")");
            });
        }

        // ------------------------------------------------------------ print / report
        /// <summary>FIFO cost of posted write-off lines (wo_menu.id → Σ linked qty × receipt cost), computed now from the FIFO links.</summary>
        static Dictionary<int, double> LineCosts(string where, object[] args)
        {
            Dictionary<int, double> c = new Dictionary<int, double>();
            foreach (DataRow r in Db.Query("SELECT w.id, SUM(x.qty * s.cost) AS v FROM ((wo_menu AS w INNER JOIN wo_head AS h ON h.wo_no = w.seq) " +
                                           "INNER JOIN sale_menu_1 AS x ON x.seq_sale = w.sale_line) INNER JOIN shop_menu AS s ON s.auto_seq = x.seq_shop WHERE " + where +
                                           " GROUP BY w.id", args).Rows)
                c[Db.I(r[0])] = Db.D(r[1]);
            return c;
        }

        /// <summary>The printed write-off with the signatures «أعدّه / اعتمده / أمين المخزن».</summary>
        public static ReportData Print(WoDoc d)
        {
            ReportData r = new ReportData();
            r.Title = "سند إتلاف / شطب مواد رقم " + d.MenuNo;
            r.Info = new List<KeyValuePair<string, string>>();
            Action<string, string> info = delegate(string k, string v) { if (!string.IsNullOrEmpty(v)) r.Info.Add(new KeyValuePair<string, string>(k, v)); };
            info("التاريخ", UI.Date(d.Date));
            info("المخزن", Cache.Store(d.Store));
            info("سبب الإتلاف", ReasonName(d.Reason));
            info("الحالة", StatusName(d.Status));
            info("أعدّه", d.UserName);
            if (d.Status == Posted)
            {
                info("المعتمد", d.ApprovedBy);
                if (d.ApprovedAt > DateTime.MinValue) info("تاريخ الاعتماد", UI.Date(d.ApprovedAt));
                info("رقم سند الترحيل", d.SaleNo);
            }
            if (d.Status == Rejected) { info("رفضه", d.ApprovedBy); info("سبب الرفض", d.RejectReason); }
            List<string> sub = new List<string>();
            foreach (KeyValuePair<string, string> kv in r.Info) sub.Add(kv.Key + ": " + kv.Value);
            r.Subtitle = string.Join("     |     ", sub.ToArray());
            bool posted = d.Status == Posted;
            Dictionary<int, double> cost = posted ? LineCosts("w.seq = ?", new object[] { d.No }) : new Dictionary<int, double>();
            r.Col("ت", ColKind.Int); r.Col("الرمز", ColKind.Text); r.Col("اسم المادة", ColKind.Text); r.Col("الوحدة", ColKind.Text); r.ColT("الكمية"); r.Col("السبب", ColKind.Text);
            r.Col(posted ? "كلفة الوحدة" : "آخر كلفة", ColKind.Num); r.ColT(posted ? "الكلفة" : "القيمة التقديرية");
            int n = 0;
            foreach (WoLine l in d.Lines)
            {
                double v;
                if (posted) { if (!cost.TryGetValue(l.Id, out v)) v = l.Cost; }
                else v = l.Qty * Requisitions.LastCost(l.ItemNo);
                r.Row(null, ++n, Cache.ItemCode(l.ItemNo), Cache.ItemName(l.ItemNo), Cache.ItemUnit(l.ItemNo), l.Qty, ReasonName(l.Reason == 0 ? d.Reason : l.Reason),
                      l.Qty > 0 ? (object)Math.Round(v / l.Qty, 4) : null, Math.Round(v, 4));
            }
            List<string> foot = new List<string>();
            if (!string.IsNullOrEmpty(d.Notes)) foot.Add("ملاحظات: " + d.Notes);
            if (!posted) foot.Add("السند غير مُرحّل: لم تخرج المواد من المخزن بعد، والقيمة تقديرية (الكمية × آخر كلفة إدخال).");
            if (foot.Count > 0) r.Footer = string.Join("   •   ", foot.ToArray());
            r.SigLabels = new[] { "أعدّه" + (d.UserName.Length > 0 ? ": " + d.UserName : ""), "اعتمده" + (posted && d.ApprovedBy.Length > 0 ? ": " + d.ApprovedBy : ""), "أمين المخزن" };
            r.Signatures = true;
            return r;
        }

        /// <summary>Report «الإتلاف حسب السبب» (group «المخزون»): posted write-offs of the period per reason with quantities and FIFO cost;
        /// with the flag, every line.</summary>
        public static ReportData ByReason(ReportParams p)
        {
            ReportData r = new ReportData();
            r.Title = "الإتلاف حسب السبب";
            List<string> sub = new List<string> { "من " + UI.Date(p.From) + " إلى " + UI.Date(p.To), p.Store > 0 ? "مخزن: " + Cache.Store(p.Store) : "كل المخازن" };
            if (p.ItemNo > 0) sub.Add("المادة: " + Cache.ItemName(p.ItemNo));
            r.Subtitle = string.Join("   •   ", sub.ToArray());
            if (p.Flag)
            {
                r.Col("ت", ColKind.Int); r.Col("التاريخ", ColKind.Date); r.Col("رقم السند", ColKind.Text); r.Col("المخزن", ColKind.Text); r.Col("السبب", ColKind.Text);
                r.Col("الرمز", ColKind.Text); r.Col("اسم المادة", ColKind.Text); r.Col("الوحدة", ColKind.Text); r.ColT("الكمية"); r.ColT("الكلفة"); r.Col("المعتمد", ColKind.Text);
            }
            else
            {
                r.Col("ت", ColKind.Int); r.Col("السبب", ColKind.Text); r.Col("عدد السندات", ColKind.Int); r.ColT("عدد الأسطر"); r.ColT("مجموع الكميات"); r.ColT("الكلفة"); r.Col("النسبة من الكلفة %", ColKind.Num);
            }
            if (!Ready) { r.Footer = "سندات الإتلاف غير مفعّلة في ملف البيانات."; return r; }
            string w = "h.status = " + Posted + " AND h.wo_date >= ? AND h.wo_date < ?";
            List<object> a = new List<object> { p.From.Date, p.ToEx };
            if (p.Store > 0) { w += " AND h.stor = ?"; a.Add(p.Store); }
            if (p.ItemNo > 0) { w += " AND w.item_no = ?"; a.Add(p.ItemNo); }
            Dictionary<int, double> cost = LineCosts(w, a.ToArray());
            DataTable t = Db.Query("SELECT w.id, h.wo_no, h.menu_no, h.wo_date, h.stor, h.reason AS hr, w.reason AS lr, w.item_no, w.qty, w.cost, h.approved_by FROM wo_head AS h INNER JOIN wo_menu AS w ON w.seq = h.wo_no " +
                                   "WHERE " + w + " ORDER BY h.wo_date, h.wo_no, w.id", a.ToArray());
            Dictionary<int, double[]> g = new Dictionary<int, double[]>();     // reason → docs, lines, qty, cost
            Dictionary<int, HashSet<int>> docs = new Dictionary<int, HashSet<int>>();
            HashSet<int> allDocs = new HashSet<int>();
            double tq = 0, tc = 0; int n = 0;
            foreach (DataRow x in t.Rows)
            {
                int rs = Db.I(x["lr"]); if (rs == 0) rs = Db.I(x["hr"]);
                int it = Db.I(x["item_no"]); double q = Db.D(x["qty"]);
                double c; if (!cost.TryGetValue(Db.I(x["id"]), out c)) c = Db.D(x["cost"]);
                c = Math.Round(c, 4);
                tq += q; tc += c; allDocs.Add(Db.I(x["wo_no"]));
                double[] v; if (!g.TryGetValue(rs, out v)) { v = new double[4]; g[rs] = v; docs[rs] = new HashSet<int>(); }
                docs[rs].Add(Db.I(x["wo_no"])); v[1]++; v[2] += q; v[3] += c;
                if (p.Flag)
                    r.Row(null, ++n, Db.Dt(x["wo_date"]), Db.S(x["menu_no"]), Cache.Store(Db.I(x["stor"])), ReasonName(rs), Cache.ItemCode(it), Cache.ItemName(it), Cache.ItemUnit(it), q, c, Db.S(x["approved_by"]));
            }
            r.Chart = new List<KeyValuePair<string, double[]>>(); r.ChartSeries = new string[] { "الكلفة" };
            foreach (int rs in AllReasons)
            {
                double[] v; if (!g.TryGetValue(rs, out v)) continue;
                v[0] = docs[rs].Count;
                if (!p.Flag) r.Row(null, ++n, ReasonName(rs), v[0], v[1], v[2], v[3], tc > 0 ? (object)Math.Round(v[3] * 100 / tc, 1) : null);
                r.Chart.Add(new KeyValuePair<string, double[]>(ReasonName(rs), new double[] { v[3] }));
            }
            r.Sum("عدد سندات الإتلاف", allDocs.Count.ToString("#,##0", Inv));
            r.Sum("مجموع الكميات", UI.Num(tq));
            r.Sum("مجموع الكلفة", UI.Num(tc));
            List<object> pa = new List<object> { p.From.Date, p.ToEx };
            string ps = "SELECT COUNT(*) FROM wo_head WHERE status IN (0, 1) AND wo_date >= ? AND wo_date < ?";
            if (p.Store > 0) { ps += " AND stor = ?"; pa.Add(p.Store); }
            int pend = Db.I(Db.Scalar(ps, pa.ToArray()));
            List<string> foot = new List<string>();
            if (t.Rows.Count == 0) foot.Add("لا توجد سندات إتلاف معتمدة في هذه الفترة.");
            foot.Add("الكلفة = كلفة الدفعات التي خرجت منها المواد (الوارد أولا يصرف أولا).");
            if (pend > 0) foot.Add("سندات إتلاف غير معتمدة بعد في هذه الفترة: " + pend + " (لا تدخل في التقرير قبل اعتمادها).");
            r.Footer = string.Join("   •   ", foot.ToArray());
            r.SigLabels = new[] { "أعدّه", "أمين المخزن", "المدير" };
            r.Signatures = true;
            return r;
        }
    }
}
