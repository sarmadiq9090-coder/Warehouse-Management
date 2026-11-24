using System.Collections.Generic;
using System.Data;
using System.Diagnostics;
using System.Drawing;
using System.Globalization;
using System.IO.Compression;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Windows.Forms;
using System;

namespace Stores
{
    // ================================================================== Excel (.xlsx) export
    static class Excel
    {
        static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

        static string ColName(int i) { string s = ""; i++; while (i > 0) { int m = (i - 1) % 26; s = (char)('A' + m) + s; i = (i - 1) / 26; } return s; }
        static string X(string s) { return System.Security.SecurityElement.Escape(s ?? ""); }

        /// <summary>Text safe for the xlsx XML: drops control characters that old data can contain (they would make
        /// Excel refuse the file), keeps Excel's 32,767-character cell limit.</summary>
        static string Clean(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            StringBuilder b = new StringBuilder(s.Length);
            for (int i = 0; i < s.Length; i++)
            {
                char ch = s[i];
                if (ch == '\t' || ch == '\n' || ch == '\r') { b.Append(ch); continue; }
                if (ch < 0x20 || ch == 0xFFFE || ch == 0xFFFF) continue;
                if (char.IsHighSurrogate(ch)) { if (i + 1 < s.Length && char.IsLowSurrogate(s[i + 1])) { b.Append(ch).Append(s[++i]); } continue; }
                if (char.IsLowSurrogate(ch)) continue;
                b.Append(ch);
            }
            return b.Length > 32767 ? b.ToString(0, 32767) : b.ToString();
        }

        static string SheetName(string title)
        {
            string s = new string((title ?? "").Where(ch => "[]:*?/\\'".IndexOf(ch) < 0).ToArray()).Trim();
            if (s.Length == 0) s = "تقرير";
            return s.Length > 31 ? s.Substring(0, 31).Trim() : s;
        }

        static bool IsNumber(object v) { return v is double || v is int || v is decimal || v is float || v is long || v is short; }

        // cell styles (see styles.xml below)
        const int SHeader = 1, SNum = 2, SDate = 3, STitle = 4, SText = 5, STotLabel = 6, STotNum = 7, SSub = 8, SInt = 9, STotInt = 10;
        static int NumStyle(double d, bool total) { bool whole = Math.Abs(d - Math.Round(d)) < 1e-9; return total ? (whole ? STotInt : STotNum) : (whole ? SInt : SNum); }

        public static void Write(string path, string title, string subtitle, List<string> headers, List<ColKind> kinds, List<object[]> rows)
        { Write(path, title, subtitle, headers, kinds, rows, null); }

        /// <summary>Writes a real .xlsx: right-to-left sheet, title, frozen + filterable header row, numbers and dates as
        /// real values, a totals row (SUM formulas) for the total columns, and print settings (fit to page width,
        /// header row repeated on every page, page numbers).</summary>
        public static void Write(string path, string title, string subtitle, List<string> headers, List<ColKind> kinds, List<object[]> rows, List<bool> totals)
        {
            if (Lang.English)
            {
                // English: translated title / headers / known cell texts, left-to-right sheet
                title = Lang.T(title); subtitle = Lang.T(subtitle);
                headers = headers.Select(h => Lang.T(h)).ToList();
                rows = rows.Select(r => r.Select(v => v is string ? (object)Lang.TCell((string)v) : v).ToArray()).ToList();
            }
            int nc = Math.Max(1, headers.Count);
            string lastCol = ColName(nc - 1);
            const int headRow = 4, firstData = 5;
            int lastData = firstData + rows.Count - 1;
            bool hasTotals = totals != null && totals.Any(t => t) && rows.Count > 0;
            string sheet = SheetName(title);
            DateTime minDate = new DateTime(1900, 3, 1);

            StringBuilder sh = new StringBuilder();
            sh.Append("<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>");
            sh.Append("<worksheet xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\" xmlns:r=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships\">");
            sh.Append("<sheetPr><pageSetUpPr fitToPage=\"1\"/></sheetPr>");
            sh.Append("<sheetViews><sheetView rightToLeft=\"" + (Lang.English ? "0" : "1") + "\" workbookViewId=\"0\"><pane ySplit=\"").Append(headRow).Append("\" topLeftCell=\"A").Append(firstData)
              .Append("\" activePane=\"bottomLeft\" state=\"frozen\"/></sheetView></sheetViews>");
            sh.Append("<cols>");
            for (int c = 0; c < headers.Count; c++)
            {
                int max = headers[c].Length;
                foreach (object[] r in rows.Take(1000))
                {
                    if (c >= r.Length || r[c] == null || r[c] is DBNull) continue;
                    object v = r[c];
                    int len = v is DateTime ? 10 : IsNumber(v) ? UI.Num(Convert.ToDouble(v)).Length + 1 : Convert.ToString(v).Length;
                    if (len > max) max = len;
                }
                int w = Math.Min(60, Math.Max(kinds[c] == ColKind.Text ? 10 : 9, max + 3));
                sh.Append("<col min=\"").Append(c + 1).Append("\" max=\"").Append(c + 1).Append("\" width=\"").Append(w).Append("\" customWidth=\"1\"/>");
            }
            sh.Append("</cols><sheetData>");

            // title + subtitle (merged across the table width)
            sh.Append("<row r=\"1\" ht=\"24\" customHeight=\"1\"><c r=\"A1\" t=\"inlineStr\" s=\"").Append(STitle).Append("\"><is><t xml:space=\"preserve\">")
              .Append(X(Clean((Program.Company.Length > 0 ? Program.Company + " — " : "") + title))).Append("</t></is></c></row>");
            sh.Append("<row r=\"2\"><c r=\"A2\" t=\"inlineStr\" s=\"").Append(SSub).Append("\"><is><t xml:space=\"preserve\">")
              .Append(X(Clean((subtitle ?? "") + (string.IsNullOrEmpty(subtitle) ? "" : "   —   ") + Lang.T("تاريخ الإخراج: ") + DateTime.Now.ToString("yyyy/MM/dd HH:mm", Inv))))
              .Append("</t></is></c></row>");

            // header row
            sh.Append("<row r=\"").Append(headRow).Append("\" ht=\"30\" customHeight=\"1\">");
            for (int c = 0; c < headers.Count; c++)
                sh.Append("<c r=\"").Append(ColName(c)).Append(headRow).Append("\" t=\"inlineStr\" s=\"").Append(SHeader).Append("\"><is><t>").Append(X(Clean(headers[c]))).Append("</t></is></c>");
            sh.Append("</row>");

            // data
            double[] sums = new double[nc];
            int rn = firstData;
            foreach (object[] r in rows)
            {
                sh.Append("<row r=\"").Append(rn).Append("\">");
                for (int c = 0; c < headers.Count; c++)
                {
                    object v = c < r.Length ? r[c] : null;
                    string refc = ColName(c) + rn;
                    if (v == null || v is DBNull) { sh.Append("<c r=\"").Append(refc).Append("\" s=\"").Append(SText).Append("\"/>"); continue; }
                    if (v is DateTime && (DateTime)v >= minDate)
                        sh.Append("<c r=\"").Append(refc).Append("\" s=\"").Append(SDate).Append("\"><v>").Append(((DateTime)v).ToOADate().ToString("R", Inv)).Append("</v></c>");
                    else if (IsNumber(v) && !double.IsNaN(Convert.ToDouble(v)) && !double.IsInfinity(Convert.ToDouble(v)))
                    {
                        double d = Convert.ToDouble(v);
                        if (hasTotals && c < totals.Count && totals[c]) sums[c] += d;
                        sh.Append("<c r=\"").Append(refc).Append("\" s=\"").Append(NumStyle(d, false)).Append("\"><v>").Append(d.ToString("R", Inv)).Append("</v></c>");
                    }
                    else
                    {
                        string t = v is DateTime ? ((DateTime)v).ToString("yyyy/MM/dd", Inv) : Convert.ToString(v);
                        sh.Append("<c r=\"").Append(refc).Append("\" t=\"inlineStr\" s=\"").Append(SText).Append("\"><is><t xml:space=\"preserve\">").Append(X(Clean(t))).Append("</t></is></c>");
                    }
                }
                sh.Append("</row>");
                rn++;
            }

            // totals row: SUBTOTAL(109) = sum of the rows visible after filtering in Excel
            if (hasTotals)
            {
                sh.Append("<row r=\"").Append(rn).Append("\" ht=\"20\" customHeight=\"1\">");
                bool label = false;
                for (int c = 0; c < headers.Count; c++)
                {
                    string refc = ColName(c) + rn;
                    if (c < totals.Count && totals[c])
                        sh.Append("<c r=\"").Append(refc).Append("\" s=\"").Append(NumStyle(sums[c], true)).Append("\"><f>SUBTOTAL(109,").Append(ColName(c)).Append(firstData).Append(":")
                          .Append(ColName(c)).Append(lastData).Append(")</f><v>").Append(sums[c].ToString("R", Inv)).Append("</v></c>");
                    else if (!label) { sh.Append("<c r=\"").Append(refc).Append("\" t=\"inlineStr\" s=\"").Append(STotLabel).Append("\"><is><t>" + Lang.T("المجموع") + "</t></is></c>"); label = true; }
                    else sh.Append("<c r=\"").Append(refc).Append("\" s=\"").Append(STotLabel).Append("\"/>");
                }
                sh.Append("</row>");
            }
            sh.Append("</sheetData>");
            if (rows.Count > 0) sh.Append("<autoFilter ref=\"A").Append(headRow).Append(":").Append(lastCol).Append(lastData).Append("\"/>");
            sh.Append("<mergeCells count=\"2\"><mergeCell ref=\"A1:").Append(nc > 1 ? lastCol : "B").Append("1\"/><mergeCell ref=\"A2:").Append(nc > 1 ? lastCol : "B").Append("2\"/></mergeCells>");
            sh.Append("<printOptions horizontalCentered=\"1\"/>");
            sh.Append("<pageMargins left=\"0.35\" right=\"0.35\" top=\"0.5\" bottom=\"0.55\" header=\"0.25\" footer=\"0.25\"/>");
            sh.Append("<pageSetup paperSize=\"9\" orientation=\"").Append(nc >= 8 ? "landscape" : "portrait").Append("\" fitToWidth=\"1\" fitToHeight=\"0\"/>");
            sh.Append("<headerFooter><oddFooter>&amp;L&amp;P / &amp;N&amp;R").Append(X(Clean(Program.AppName))).Append("</oddFooter></headerFooter>");
            sh.Append("</worksheet>");

            const string styles = "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>" +
                "<styleSheet xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\">" +
                "<numFmts count=\"3\"><numFmt numFmtId=\"164\" formatCode=\"#,##0.###;[Red]\\-#,##0.###\"/><numFmt numFmtId=\"165\" formatCode=\"yyyy/mm/dd\"/><numFmt numFmtId=\"166\" formatCode=\"#,##0;[Red]\\-#,##0\"/></numFmts>" +
                "<fonts count=\"5\">" +
                "<font><sz val=\"11\"/><name val=\"Segoe UI\"/></font>" +
                "<font><b/><sz val=\"11\"/><color rgb=\"FFFFFFFF\"/><name val=\"Segoe UI\"/></font>" +
                "<font><b/><sz val=\"14\"/><color rgb=\"FF2F56A3\"/><name val=\"Segoe UI\"/></font>" +
                "<font><b/><sz val=\"11\"/><color rgb=\"FF1E2A38\"/><name val=\"Segoe UI\"/></font>" +
                "<font><sz val=\"10\"/><color rgb=\"FF6B7785\"/><name val=\"Segoe UI\"/></font></fonts>" +
                "<fills count=\"4\"><fill><patternFill patternType=\"none\"/></fill><fill><patternFill patternType=\"gray125\"/></fill>" +
                "<fill><patternFill patternType=\"solid\"><fgColor rgb=\"FF2F56A3\"/><bgColor indexed=\"64\"/></patternFill></fill>" +
                "<fill><patternFill patternType=\"solid\"><fgColor rgb=\"FFE6EEF9\"/><bgColor indexed=\"64\"/></patternFill></fill></fills>" +
                "<borders count=\"2\"><border><left/><right/><top/><bottom/><diagonal/></border>" +
                "<border><left style=\"thin\"><color rgb=\"FFD0D7E2\"/></left><right style=\"thin\"><color rgb=\"FFD0D7E2\"/></right>" +
                "<top style=\"thin\"><color rgb=\"FFD0D7E2\"/></top><bottom style=\"thin\"><color rgb=\"FFD0D7E2\"/></bottom><diagonal/></border></borders>" +
                "<cellStyleXfs count=\"1\"><xf numFmtId=\"0\" fontId=\"0\" fillId=\"0\" borderId=\"0\"/></cellStyleXfs>" +
                "<cellXfs count=\"11\">" +
                "<xf numFmtId=\"0\" fontId=\"0\" fillId=\"0\" borderId=\"0\" xfId=\"0\"/>" +                                                                   // 0 default
                "<xf numFmtId=\"0\" fontId=\"1\" fillId=\"2\" borderId=\"1\" xfId=\"0\" applyFont=\"1\" applyFill=\"1\" applyBorder=\"1\" applyAlignment=\"1\">" +
                    "<alignment horizontal=\"center\" vertical=\"center\" wrapText=\"1\"/></xf>" +                                                                  // 1 header
                "<xf numFmtId=\"164\" fontId=\"0\" fillId=\"0\" borderId=\"1\" xfId=\"0\" applyNumberFormat=\"1\" applyBorder=\"1\"/>" +                         // 2 number
                "<xf numFmtId=\"165\" fontId=\"0\" fillId=\"0\" borderId=\"1\" xfId=\"0\" applyNumberFormat=\"1\" applyBorder=\"1\" applyAlignment=\"1\"><alignment horizontal=\"center\"/></xf>" + // 3 date
                "<xf numFmtId=\"0\" fontId=\"2\" fillId=\"0\" borderId=\"0\" xfId=\"0\" applyFont=\"1\"/>" +                                                    // 4 title
                "<xf numFmtId=\"0\" fontId=\"0\" fillId=\"0\" borderId=\"1\" xfId=\"0\" applyBorder=\"1\"/>" +                                                  // 5 text
                "<xf numFmtId=\"0\" fontId=\"3\" fillId=\"3\" borderId=\"1\" xfId=\"0\" applyFont=\"1\" applyFill=\"1\" applyBorder=\"1\"/>" +                  // 6 totals label
                "<xf numFmtId=\"164\" fontId=\"3\" fillId=\"3\" borderId=\"1\" xfId=\"0\" applyNumberFormat=\"1\" applyFont=\"1\" applyFill=\"1\" applyBorder=\"1\"/>" + // 7 totals number
                "<xf numFmtId=\"0\" fontId=\"4\" fillId=\"0\" borderId=\"0\" xfId=\"0\" applyFont=\"1\"/>" +                                                    // 8 subtitle
                "<xf numFmtId=\"166\" fontId=\"0\" fillId=\"0\" borderId=\"1\" xfId=\"0\" applyNumberFormat=\"1\" applyBorder=\"1\"/>" +                         // 9 whole number
                "<xf numFmtId=\"166\" fontId=\"3\" fillId=\"3\" borderId=\"1\" xfId=\"0\" applyNumberFormat=\"1\" applyFont=\"1\" applyFill=\"1\" applyBorder=\"1\"/>" + // 10 totals whole number
                "</cellXfs>" +
                "<cellStyles count=\"1\"><cellStyle name=\"Normal\" xfId=\"0\" builtinId=\"0\"/></cellStyles></styleSheet>";

            string q = "'" + sheet.Replace("'", "''") + "'";
            string workbook = "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?><workbook xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\" " +
                "xmlns:r=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships\"><sheets><sheet name=\"" + X(sheet) + "\" sheetId=\"1\" r:id=\"rId1\"/></sheets>" +
                "<definedNames>" +
                (rows.Count > 0 ? "<definedName name=\"_xlnm._FilterDatabase\" localSheetId=\"0\" hidden=\"1\">" + X(q) + "!$A$" + headRow + ":$" + lastCol + "$" + lastData + "</definedName>" : "") +
                "<definedName name=\"_xlnm.Print_Titles\" localSheetId=\"0\">" + X(q) + "!$" + headRow + ":$" + headRow + "</definedName>" +
                "</definedNames></workbook>";

            string tmp = path + ".tmp";
            if (File.Exists(tmp)) File.Delete(tmp);
            using (FileStream fs = new FileStream(tmp, FileMode.CreateNew))
            using (ZipArchive z = new ZipArchive(fs, ZipArchiveMode.Create))
            {
                Add(z, "[Content_Types].xml", "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?><Types xmlns=\"http://schemas.openxmlformats.org/package/2006/content-types\">" +
                    "<Default Extension=\"rels\" ContentType=\"application/vnd.openxmlformats-package.relationships+xml\"/><Default Extension=\"xml\" ContentType=\"application/xml\"/>" +
                    "<Override PartName=\"/xl/workbook.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml\"/>" +
                    "<Override PartName=\"/xl/worksheets/sheet1.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml\"/>" +
                    "<Override PartName=\"/xl/styles.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.spreadsheetml.styles+xml\"/></Types>");
                Add(z, "_rels/.rels", "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?><Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\">" +
                    "<Relationship Id=\"rId1\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument\" Target=\"xl/workbook.xml\"/></Relationships>");
                Add(z, "xl/workbook.xml", workbook);
                Add(z, "xl/_rels/workbook.xml.rels", "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?><Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\">" +
                    "<Relationship Id=\"rId1\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/worksheet\" Target=\"worksheets/sheet1.xml\"/>" +
                    "<Relationship Id=\"rId2\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/styles\" Target=\"styles.xml\"/></Relationships>");
                Add(z, "xl/styles.xml", styles);
                Add(z, "xl/worksheets/sheet1.xml", sh.ToString());
            }
            // replace the old file only when the new one is complete (a file open in Excel gives a clear message)
            try { if (File.Exists(path)) File.Delete(path); }
            catch (IOException) { File.Delete(tmp); throw new UserError("الملف مفتوح في Excel:\n" + path + "\nأغلقه ثم أعد التصدير، أو اختر اسما آخر."); }
            File.Move(tmp, path);
        }

        static void Add(ZipArchive z, string name, string content)
        {
            ZipArchiveEntry e = z.CreateEntry(name, CompressionLevel.Optimal);
            using (Stream s = e.Open())
            {
                byte[] b = new UTF8Encoding(false).GetBytes(content);
                s.Write(b, 0, b.Length);
            }
        }

        static string AskPath(IWin32Window owner, string title)
        {
            using (SaveFileDialog d = new SaveFileDialog())
            {
                d.Filter = "Excel (*.xlsx)|*.xlsx";
                string safe = new string(title.Where(ch => Array.IndexOf(Path.GetInvalidFileNameChars(), ch) < 0).ToArray());
                d.FileName = safe + " " + DateTime.Today.ToString("yyyy-MM-dd", Inv) + ".xlsx";
                d.InitialDirectory = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
                return d.ShowDialog(owner) == DialogResult.OK ? d.FileName : null;
            }
        }

        static void Done(string path)
        {
            if (UI.Confirm("تم التصدير:\n" + path + "\n\nهل تريد فتح الملف الآن؟"))
            {
                try { Process.Start(path); } catch (Exception ex) { UI.Warn("تعذر فتح الملف: " + ex.Message); }
            }
        }

        public static void Export(IWin32Window owner, ReportData r)
        {
            if (r == null) return;
            string path = AskPath(owner, r.Title); if (path == null) return;
            try
            {
                Write(path, r.Title, r.Subtitle, r.Cols.Select(c => c.Header).ToList(), r.Cols.Select(c => c.Kind).ToList(), r.Rows, r.Cols.Select(c => c.Total).ToList());
                Done(path);
            }
            catch (Exception ex) { UI.Error(ex); }
        }

        public static void ExportGrid(IWin32Window owner, DataGridView g, string title)
        {
            string path = AskPath(owner, title); if (path == null) return;
            try
            {
                List<DataGridViewColumn> cols = g.Columns.Cast<DataGridViewColumn>().Where(c => c.Visible).OrderBy(c => c.DisplayIndex).ToList();
                List<object[]> rows = new List<object[]>();
                foreach (DataGridViewRow r in g.Rows)
                {
                    if (r.IsNewRow) continue;
                    rows.Add(cols.Select(c => r.Cells[c.Index].Value).ToArray());
                }
                List<ColKind> kinds = cols.Select(c => c.ValueType == typeof(DateTime) ? ColKind.Date : (c.ValueType == typeof(double) || c.ValueType == typeof(int) ? ColKind.Num : ColKind.Text)).ToList();
                Write(path, title, "", cols.Select(c => c.HeaderText).ToList(), kinds, rows);
                Done(path);
            }
            catch (Exception ex) { UI.Error(ex); }
        }
    }
}
