using System;
using System.Collections.Generic;
using System.Data;
using System.IO;
using System.Text;

namespace Stores
{
    /// <summary>
    /// Fills a copy of tests/demo/empty-template.mdb with realistic FAKE data (no real company data):
    ///   DemoData.exe &lt;empty-template.mdb&gt; &lt;demo.mdb&gt;
    /// 3 stores, item types, ~50 generic electrical items, ~20 persons, 4 projects, receipts / issues / returns /
    /// transfers / purchase returns / reservations spread over the last 18 months. Every document is saved through
    /// Posting.Save, so balances, FIFO links and numbering are exactly what the program produces.
    /// Login: user «1», password «1» (users 2-5: password = their number).
    /// The same seed always gives the same data (relative to today's date).
    /// </summary>
    static class DemoMain
    {
        static Random rnd = new Random(20250501);
        static int R(int a, int b) { return rnd.Next(a, b + 1); }
        static T Pick<T>(IList<T> l) { return l[rnd.Next(l.Count)]; }

        class Item { public int Id; public string Name, Code; public int Type; public double Price; public bool Cable; }

        [STAThread]
        static int Main(string[] args)
        {
            Console.OutputEncoding = Encoding.UTF8;
            if (args.Length < 2) { Console.WriteLine("usage: DemoData <empty-template.mdb> <demo.mdb>"); return 2; }
            try
            {
                File.Copy(args[0], args[1], true);
                File.SetAttributes(args[1], FileAttributes.Normal);
                string err = Db.InitAccess(Path.GetFullPath(args[1]));
                if (err != null) { Console.WriteLine("open failed: " + err); return 1; }
                Db.EnsureAppTables();
                if (!Db.HasProj) { Console.WriteLine("program tables missing: " + Db.SchemaWarning); return 1; }
                if (Db.I(Db.Scalar("SELECT COUNT(*) FROM item")) > 0) { Console.WriteLine("the file is not empty"); return 1; }
                Session.UserNo = 1; Session.UserName = "1";
                Seed();
                Console.WriteLine("demo data ready: " + args[1]);
                Db.Close();
                return 0;
            }
            catch (Exception ex) { Console.WriteLine("FAILED: " + ex); return 1; }
        }

        static void Seed()
        {
            // ---------------------------------------------------------------- users and permissions
            string[] users = { "1", "أمين المخزن", "مسؤول المشتريات", "مشرف المشاريع", "مستخدم 5" };
            for (int i = 0; i < users.Length; i++)
                Db.Exec("INSERT INTO pass (pass_no, user_name, pass) VALUES (?, ?, ?)", i + 1, users[i], (i + 1).ToString());
            // command numbers of the original program (Session.RecView ...); user1 = administrator
            object[][] cmds =
            {
                new object[] { 11, "إدخال مخزني - عرض", 1, 1, 1, 0 }, new object[] { 12, "إدخال مخزني - تعديل", 1, 1, 0, 0 },
                new object[] { 13, "إدخال مخزني - حذف", 1, 0, 0, 0 }, new object[] { 14, "إدخال مخزني - إضافة", 1, 1, 0, 0 },
                new object[] { 21, "إخراج مخزني - عرض", 1, 0, 1, 1 }, new object[] { 22, "إخراج مخزني - تعديل", 1, 0, 0, 0 },
                new object[] { 23, "إخراج مخزني - حذف", 1, 0, 0, 0 }, new object[] { 24, "إخراج مخزني - إضافة", 1, 0, 0, 1 },
                new object[] { 51, "الصندوق - عرض", 0, 0, 0, 0 }, new object[] { 52, "الصندوق - تعديل", 0, 0, 0, 0 },
                new object[] { 53, "الصندوق - حذف", 0, 0, 0, 0 }, new object[] { 54, "الصندوق - إضافة", 0, 0, 0, 0 },
                new object[] { 61, "نقل مخزني", 1, 0, 0, 0 }, new object[] { 71, "إعداد النظام", 0, 0, 0, 0 },
                new object[] { 72, "الأرصدة الأولية للصندوق", 0, 0, 0, 0 }, new object[] { 74, "ملف الجهات", 1, 1, 0, 0 },
                new object[] { 75, "ملف المواد", 1, 1, 0, 0 }, new object[] { 81, "التقارير", 1, 1, 1, 1 }
            };
            foreach (object[] c in cmds)
                Db.Exec("INSERT INTO disply_command (command_no, command_name, user1, user2, user3, user4, user5) VALUES (?, ?, ?, ?, ?, ?, ?)",
                    c[0], c[1], true, (int)c[2] == 1, (int)c[3] == 1, (int)c[4] == 1, (int)c[5] == 1);
            Db.SetSetting("company_name", "شركة تجريبية للمقاولات الكهربائية");

            // ---------------------------------------------------------------- lookups
            int[] stores = new int[3];
            string[] storeNames = { "المخزن الرئيسي", "مخزن الموقع", "مخزن الكابلات" };
            for (int i = 0; i < 3; i++) stores[i] = Ins("INSERT INTO stor (stor_name) VALUES (?)", storeNames[i]);
            int gElec = Ins("INSERT INTO [group] (group_name) VALUES (?)", "مواد كهربائية");
            int gGen = Ins("INSERT INTO [group] (group_name) VALUES (?)", "مواد عامة");
            string[] typeNames = { "قواطع", "كونتكترات", "كابلات", "إنارة", "مفاتيح ومقابس", "لوحات توزيع", "أنابيب وملحقات" };
            int[] types = new int[typeNames.Length];
            for (int i = 0; i < types.Length; i++) types[i] = Ins("INSERT INTO [type] (type_name) VALUES (?)", typeNames[i]);

            List<Item> items = new List<Item>();
            Action<string, string, int, double, bool> add = delegate(string name, string code, int type, double price, bool cable)
            {
                items.Add(new Item { Name = name, Code = code, Type = types[type], Price = price, Cable = cable });
            };
            foreach (int a in new[] { 6, 10, 16, 20, 25, 32 }) add("قاطع دورة MCB 1P " + a + "A", "MCB-1P" + a, 0, 3500 + a * 50, false);
            foreach (int a in new[] { 16, 32, 63 }) add("قاطع دورة MCB 3P " + a + "A", "MCB-3P" + a, 0, 14000 + a * 150, false);
            foreach (int a in new[] { 100, 250 }) add("قاطع علبة MCCB 3P " + a + "A", "MCCB-" + a, 0, 85000 + a * 300, false);
            add("قاطع تسرب RCCB 2P 40A 30mA", "RCCB-2P40", 0, 18000, false);
            add("قاطع تسرب RCCB 4P 63A 30mA", "RCCB-4P63", 0, 32000, false);
            foreach (int a in new[] { 9, 12, 18, 25, 32, 40, 65 }) add("كونتكتر 3P " + a + "A ملف 220V", "CT-" + a, 1, 12000 + a * 400, false);
            add("أوفرلود حراري 9-13A", "OL-0913", 1, 11000, false);
            foreach (string s in new[] { "1.5", "2.5", "4", "6", "10", "16" }) add("كيبل نحاس مفرد " + s + " ملم²", "CU-1X" + s, 2, 350 + double.Parse(s, System.Globalization.CultureInfo.InvariantCulture) * 220, true);
            foreach (string s in new[] { "4×16", "4×25", "4×35", "4×70" }) add("كيبل نحاس مسلح " + s + " ملم²", "CU-" + s.Replace("×", "X"), 2, 9000 + int.Parse(s.Substring(2)) * 450, true);
            add("كيبل تحكم 7×1.5 ملم²", "CTRL-7X1.5", 2, 2600, true);
            add("مصباح LED بانل 60×60 40W", "LED-P40", 3, 16000, false);
            add("مصباح LED دائري 18W", "LED-R18", 3, 6500, false);
            add("كشاف LED خارجي 100W", "LED-F100", 3, 38000, false);
            add("كشاف LED خارجي 200W", "LED-F200", 3, 64000, false);
            add("مصباح طوارئ LED", "LED-EM", 3, 21000, false);
            add("مفتاح إنارة مفرد", "SW-1G", 4, 2500, false);
            add("مفتاح إنارة مزدوج", "SW-2G", 4, 3500, false);
            add("بريزة 13A مفردة", "SK-13A", 4, 3000, false);
            add("بريزة صناعية 32A 5P", "SK-IND32", 4, 15000, false);
            add("لوحة توزيع 12 خط", "DB-12", 5, 45000, false);
            add("لوحة توزيع 24 خط", "DB-24", 5, 78000, false);
            add("لوحة تشغيل محرك مع حماية", "MCC-01", 5, 240000, false);
            add("أنبوب PVC قطر 20 ملم", "PVC-20", 6, 1200, false);
            add("أنبوب PVC قطر 25 ملم", "PVC-25", 6, 1600, false);
            add("علبة توزيع 10×10", "JB-1010", 6, 1800, false);
            add("برغي ستيل M8 مع صامولة", "BLT-M8", 6, 250, false);
            add("رابط بلاستيك 30 سم (كيس 100)", "TIE-30", 6, 3000, false);
            add("شريط عازل كهربائي", "TAPE-PVC", 6, 750, false);
            foreach (Item it in items)
            {
                it.Id = Ins("INSERT INTO item (item_name, item_code, [group], [type], item_bal1, item_bal, item_price_dl1, item_bal_ret, qty_f, sale_pr, tslsl, freez) " +
                            "VALUES (?, ?, ?, ?, 0, 0, 0, ?, 0, 0, ?, ?)",
                            it.Name, it.Code, it.Type == types[6] ? gGen : gElec, it.Type, (double)(it.Cable ? 200 : R(5, 20)), 0.0, false);
            }
            // a frozen (no longer used) item
            Ins("INSERT INTO item (item_name, item_code, [group], [type], item_bal1, item_bal, item_price_dl1, item_bal_ret, qty_f, sale_pr, tslsl, freez) VALUES (?, ?, ?, ?, 0, 0, 0, 0, 0, 0, 0, ?)",
                "مصباح فلورسنت 36W (متوقف)", "FL-36", gElec, types[3], true);

            string[] suppliers = { "شركة الأفق للتجهيزات الكهربائية", "مؤسسة النور للكابلات", "شركة الضياء للإنارة", "مكتب الرافدين للمواد الإنشائية", "شركة البيان للتجارة العامة", "مجهز محلي - سوق الكهربائيات" };
            string[] receivers = { "أحمد علي حسن", "محمد جاسم كاظم", "علي حسين عباس", "حسن كريم مهدي", "عمر خالد سعيد", "مصطفى عادل جواد", "يوسف ناصر حميد",
                                   "كرار سلمان داود", "زيد فاضل عبد الله", "سيف رعد مجيد", "مهند صباح ياسين", "فراس طارق نوري", "قسم الصيانة", "ورشة الكهرباء" };
            List<int> sup = new List<int>(), rec = new List<int>();
            foreach (string s in suppliers) sup.Add(Ins("INSERT INTO person (p_name, p_bal_id, p_bal_dl, p_bal_id_f, p_bal_dl_f, p_addrs, p_tel, p_note, p_type, p_sjl) VALUES (?, 0, 0, 0, 0, ?, ?, ?, 2, 0)",
                s, "بغداد", "0770" + R(1000000, 9999999), "بيانات تجريبية"));
            foreach (string s in receivers) rec.Add(Ins("INSERT INTO person (p_name, p_bal_id, p_bal_dl, p_bal_id_f, p_bal_dl_f, p_addrs, p_tel, p_note, p_type, p_sjl) VALUES (?, 0, 0, 0, 0, ?, ?, ?, 1, 0)",
                s, DBNull.Value, DBNull.Value, "بيانات تجريبية"));

            DateTime start = new DateTime(DateTime.Today.Year, DateTime.Today.Month, 1).AddMonths(-18);
            object[][] projs =
            {
                new object[] { "PRJ-101", "مجمع سكني - المرحلة الأولى", "جهة تجريبية أ", 1, start },
                new object[] { "PRJ-102", "مدرسة نموذجية", "جهة تجريبية ب", 1, start.AddMonths(3) },
                new object[] { "PRJ-103", "صيانة المبنى الإداري", "داخلي", 2, start },
                new object[] { "PRJ-104", "محطة ضخ المياه", "جهة تجريبية ج", 1, start.AddMonths(8) }
            };
            List<int> projects = new List<int>();
            foreach (object[] p in projs)
                projects.Add(Ins("INSERT INTO project (proj_code, proj_name, client, status, start_date, notes) VALUES (?, ?, ?, ?, ?, ?)", p[0], p[1], p[2], p[3], p[4], "مشروع تجريبي"));
            Cache.LoadAll();

            // opening balances (item_bal_f) in the main store for some items
            foreach (Item it in items)
                if (rnd.NextDouble() < 0.5)
                {
                    double q = it.Cable ? R(2, 6) * 100 : R(5, 40);
                    Db.Exec("INSERT INTO item_stor (item_no, stor, item_bal_f, item_bal, qty_f) VALUES (?, ?, ?, ?, 0)", it.Id, stores[0], q, q);
                }

            // ---------------------------------------------------------------- documents, month by month
            int nRec = 0, nIss = 0, nRet = 0, nTr = 0, nPr = 0, nRes = 0;
            List<Doc> issues = new List<Doc>();
            for (DateTime m = start; m <= DateTime.Today; m = m.AddMonths(1))
            {
                int days = (m.AddMonths(1) > DateTime.Today ? DateTime.Today.Day : DateTime.DaysInMonth(m.Year, m.Month));
                Func<int, int, DateTime> day = delegate(int a, int b) { return m.AddDays(Math.Min(days, R(a, b)) - 1); };

                // receipts at the start of the month
                for (int k = R(2, 3); k > 0; k--)
                {
                    Doc d = new Doc(); d.Kind = DocKind.Receipt; d.Date = day(1, 8); d.Person = Pick(sup); d.RType = rnd.NextDouble() < 0.25 ? 1 : 2;
                    d.Store = rnd.NextDouble() < 0.2 ? stores[2] : stores[0];
                    HashSet<int> used = new HashSet<int>();
                    for (int n = R(4, 8); n > 0; n--)
                    {
                        Item it = Pick(items); if (!used.Add(it.Id)) continue;
                        double q = it.Cable ? R(1, 5) * 100 : R(2, 12) * 10;
                        d.Lines.Add(new DocLine { ItemNo = it.Id, Qty = q, Price = Math.Round(it.Price * (0.9 + rnd.NextDouble() * 0.2)) });
                    }
                    if (rnd.NextDouble() < 0.3) d.Note = "فاتورة رقم " + R(1000, 9999);
                    Posting.Save(d); nRec++;
                }

                // a transfer from the main store to the site store every few months
                if (m.Month % 4 == 0)
                {
                    Doc t = new Doc(); t.Kind = DocKind.Transfer; t.Date = day(9, 11); t.Store = stores[0]; t.Store2 = stores[1]; t.Note = "تجهيز مخزن الموقع";
                    AddAvailable(t, items, stores[0], 3, 0.3);
                    if (t.Lines.Count > 0) { Posting.Save(t); nTr++; }
                }

                // issues to projects during the month
                List<int> open = new List<int>();
                for (int i = 0; i < projs.Length; i++) if ((DateTime)projs[i][4] <= m && ((int)projs[i][3] == 1 || m < start.AddMonths(10))) open.Add(projects[i]);
                for (int k = R(6, 10); k > 0; k--)
                {
                    Doc d = new Doc(); d.Kind = DocKind.Issue; d.Date = day(10, 28); d.Person = Pick(rec); d.Project = Pick(open);
                    d.Store = rnd.NextDouble() < 0.8 ? stores[0] : (rnd.NextDouble() < 0.5 ? stores[1] : stores[2]);
                    AddAvailable(d, items, d.Store, R(1, 5), 0.25);
                    if (d.Lines.Count == 0) continue;
                    Posting.Save(d); nIss++; issues.Add(d);
                }

                // now and then materials come back from a project
                if (m.Month % 3 == 1 && issues.Count > 0)
                {
                    Doc src = issues[issues.Count - 1 - rnd.Next(Math.Min(5, issues.Count))];
                    Doc r = new Doc(); r.Kind = DocKind.Return; r.Date = day(28, 31); if (r.Date < src.Date) r.Date = src.Date;
                    r.Person = src.Person; r.Project = src.Project; r.Store = src.Store; r.Note = "فائض من الموقع";
                    r.Lines.Add(new DocLine { ItemNo = src.Lines[0].ItemNo, Qty = Math.Max(1, Math.Floor(src.Lines[0].Qty / 3)) });
                    Posting.Save(r); nRet++;
                }

                // a purchase return twice a year
                if (m.Month == 3 || m.Month == 9)
                {
                    Doc p = new Doc(); p.Kind = DocKind.PurchaseReturn; p.Date = day(20, 25); p.Person = Pick(sup); p.Store = stores[0]; p.Note = "مواد غير مطابقة";
                    AddAvailable(p, items, stores[0], 1, 0.1);
                    if (p.Lines.Count > 0) { Posting.Save(p); nPr++; }
                }
            }

            // reservations: two active, one cancelled
            for (int k = 0; k < 3; k++)
            {
                Doc rs = new Doc(); rs.Kind = DocKind.Reserve; rs.Date = DateTime.Today.AddDays(-R(1, 20)); rs.Project = projects[k == 2 ? 1 : (k == 0 ? 0 : 3)]; rs.Store = stores[0];
                rs.Note = "حجز للمرحلة القادمة";
                AddAvailable(rs, items, stores[0], 2, 0.2);
                if (rs.Lines.Count == 0) continue;
                Posting.Save(rs); nRes++;
                if (k == 2) Posting.Delete(Posting.Load(DocKind.Reserve, rs.Id));
            }

            int fixedRows = Posting.RecalcBalances();
            Console.WriteLine("stores " + stores.Length + ", types " + types.Length + ", items " + (items.Count + 1) + ", persons " + (sup.Count + rec.Count) + ", projects " + projects.Count +
                              ", users " + users.Length);
            Console.WriteLine("receipts " + nRec + ", issues " + nIss + ", returns " + nRet + ", transfers " + nTr + ", purchase returns " + nPr + ", reservations " + nRes +
                              " (" + UI.Date(start) + " - " + UI.Date(DateTime.Today) + "), balance corrections " + fixedRows);
            if (fixedRows != 0) throw new Exception("balances did not match the movements");
        }

        /// <summary>Adds up to n lines of items that have stock in the store (at most share of what is there).</summary>
        static void AddAvailable(Doc d, List<Item> items, int store, int n, double share)
        {
            HashSet<int> used = new HashSet<int>();
            for (int tries = 0; tries < n * 6 && d.Lines.Count < n; tries++)
            {
                Item it = Pick(items);
                if (used.Contains(it.Id)) continue;
                double have = Cache.Stock(it.Id, store) - Reservations.ForOthers(it.Id, store, d.Project);
                double q = Math.Floor(have * share * rnd.NextDouble());
                if (it.Cable) q = Math.Floor(q / 10) * 10;
                if (q < 1) continue;
                used.Add(it.Id);
                d.Lines.Add(new DocLine { ItemNo = it.Id, Qty = q, Price = d.Kind == DocKind.Reserve || d.Kind == DocKind.Transfer ? 0 : it.Price });
            }
        }

        static int Ins(string sql, params object[] a)
        {
            int id = 0;
            Db.InTx(delegate(System.Data.Common.DbTransaction tx) { id = Db.Insert(tx, sql, a); });
            return id;
        }
    }
}
