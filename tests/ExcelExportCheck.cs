using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;

namespace Stores
{
    // Exports every report exactly like the program does and writes the expected cell values next to each file,
    // so a PowerShell + real Excel step can open the files and compare cell by cell.
    static class XlCheckMain
    {
        static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

        static string Enc(object v)
        {
            if (v == null || v is DBNull) return "e:";
            if (v is DateTime) return "d:" + ((DateTime)v).ToOADate().ToString("R", Inv);
            if (v is double || v is int || v is decimal || v is float || v is long || v is short) return "n:" + Convert.ToDouble(v).ToString("R", Inv);
            return "t:" + Convert.ToString(v).Replace("\\", "\\\\").Replace("\t", "\\t").Replace("\r", "\\r").Replace("\n", "\\n");
        }

        [STAThread]
        static int Main(string[] args)
        {
            string mdb = args[0], outDir = args[1];
            Directory.CreateDirectory(outDir);
            string err = Db.InitAccess(mdb); if (err != null) { Console.WriteLine(err); return 1; }
            Db.EnsureAppTables();
            Session.UserNo = 1; Session.UserName = "1"; Session.LoadPerms(); Session.Advanced = true;
            Cache.LoadAll();

            int item = Db.I(Db.Scalar("SELECT TOP 1 item_no FROM sale_menu GROUP BY item_no ORDER BY COUNT(*) DESC"));
            int person = Db.I(Db.Scalar("SELECT TOP 1 custom_no FROM sale GROUP BY custom_no ORDER BY COUNT(*) DESC"));
            // a project charged with the 25 latest issue documents (this is a copy of the data)
            int proj = Db.I(Db.Scalar("SELECT MAX(proj_no) FROM project"));
            if (proj == 0)
            {
                Db.Exec("INSERT INTO project (proj_code, proj_name, client, status, start_date) VALUES (?, ?, ?, ?, ?)", "P-TEST", "مشروع اختبار التصدير", "جهة اختبار", 1, new DateTime(2025, 1, 1));
                proj = Db.I(Db.Scalar("SELECT MAX(proj_no) FROM project"));
                Db.Exec("UPDATE sale SET proj_no = ? WHERE shop_no IN (SELECT TOP 25 shop_no FROM sale ORDER BY shop_no DESC)", proj);
                Cache.LoadAll();
            }
            Console.WriteLine("item=" + item + " person=" + person + " project=" + proj);

            StringBuilder index = new StringBuilder();
            foreach (ReportDef d in Reports.All)
            {
                ReportParams p = new ReportParams(); p.From = new DateTime(2024, 1, 1); p.To = DateTime.Today;
                if ((d.P & RP.AsOf) != 0) p.From = p.To;
                if ((d.P & RP.Since) != 0 && (d.P & RP.Dates) == 0) p.From = DateTime.Today.AddMonths(-6);
                if ((d.P & RP.ItemReq) != 0) p.ItemNo = item;
                if ((d.P & RP.PersonReq) != 0) p.PersonNo = person;
                if ((d.P & RP.ProjectReq) != 0) p.ProjNo = proj;
                ReportData r = d.Run(p);
                string x = Path.Combine(outDir, d.Key + ".xlsx");
                Excel.Write(x, r.Title, r.Subtitle, r.Cols.ConvertAll(c => c.Header), r.Cols.ConvertAll(c => c.Kind), r.Rows, r.Cols.ConvertAll(c => c.Total));
                using (StreamWriter w = new StreamWriter(Path.Combine(outDir, d.Key + ".expected.tsv"), false, new UTF8Encoding(false)))
                {
                    w.WriteLine(string.Join("\t", r.Cols.Select(c => Enc(c.Header))));
                    foreach (object[] row in r.Rows)
                        w.WriteLine(string.Join("\t", Enumerable.Range(0, r.Cols.Count).Select(i => Enc(i < row.Length ? row[i] : null))));
                }
                double[] sums = new double[r.Cols.Count];
                foreach (object[] row in r.Rows) for (int i = 0; i < r.Cols.Count && i < row.Length; i++) if (r.Cols[i].Total && row[i] != null && !(row[i] is string) && !(row[i] is DBNull)) sums[i] += Convert.ToDouble(row[i]);
                File.WriteAllText(Path.Combine(outDir, d.Key + ".totals.tsv"), r.Rows.Count > 0 && r.Cols.Any(c => c.Total) ? string.Join("\t", Enumerable.Range(0, r.Cols.Count).Select(i => r.Cols[i].Total ? sums[i].ToString("R", Inv) : "")) : "", new UTF8Encoding(false));
                File.WriteAllText(Path.Combine(outDir, d.Key + ".html"), Html.Render(r), new UTF8Encoding(false));
                index.AppendLine(d.Key + "\t" + r.Rows.Count + "\t" + r.Cols.Count + "\t" + r.Title);
                Console.WriteLine(d.Key + ": rows=" + r.Rows.Count + " cols=" + r.Cols.Count);
            }
            // a voucher (printed document) too
            int lastIssue = Db.I(Db.Scalar("SELECT MAX(shop_no) FROM sale"));
            File.WriteAllText(Path.Combine(outDir, "voucher.html"), Html.Render(Reports.Voucher(Posting.Load(DocKind.Issue, lastIssue))), new UTF8Encoding(false));
            File.WriteAllText(Path.Combine(outDir, "index.tsv"), index.ToString(), new UTF8Encoding(false));
            return 0;
        }
    }
}
