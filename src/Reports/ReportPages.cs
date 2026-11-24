using System.Collections.Generic;
using System.Drawing.Drawing2D;
using System.Drawing.Printing;
using System.Drawing.Text;
using System.Drawing;
using System.Globalization;
using System.Linq;
using System;

namespace Stores
{
    /// <summary>A report or document laid out in pages: the same pages are shown in the preview, sent to the printer and
    /// saved as PDF. Drawn by the program itself (GDI+), so it needs no browser, no internet and — for the preview — no
    /// printer. All sizes are in 1/100 inch; Prepare(g) sets that unit on any screen, bitmap or printer.</summary>
    class ReportPages
    {
        static readonly CultureInfo Inv = CultureInfo.InvariantCulture;
        static readonly Color Blue = Color.FromArgb(31, 63, 122), Brand = Color.FromArgb(47, 86, 163), Green = Color.FromArgb(1, 165, 82), Grey = Color.FromArgb(95, 107, 122),
                              Line = Color.FromArgb(150, 158, 170), Head = Color.FromArgb(238, 243, 251), Ink = Color.FromArgb(27, 27, 27);
        static readonly Color[] Series = { Brand, Green, Color.FromArgb(17, 122, 101), Color.FromArgb(214, 137, 16) };

        class Block { public float H; public Action<Graphics, float> Draw; public bool Row; }
        class Placed { public float Y; public Block B; }

        readonly ReportData r;
        readonly bool ltr = Lang.English;
        readonly List<List<Placed>> pages = new List<List<Placed>>();
        float[] widths, starts;        // table columns, measured from the start edge (right in Arabic)
        bool[] wrap;
        float fontSize;
        Block tableHead;
        const float Margin = 40, FootH = 24;

        public bool Landscape;
        public float PaperW = 827, PaperH = 1169;      // portrait size of the sheet (A4)
        public float TextScale = 1f;

        public string Title { get { return r.Title; } }
        public int Count { get { return pages.Count; } }
        public float W { get { return Landscape ? PaperH : PaperW; } }
        public float H { get { return Landscape ? PaperW : PaperH; } }
        float CW { get { return W - 2 * Margin; } }

        public ReportPages(ReportData data)
        {
            r = Lang.Report(data);       // English: translated copy
            Landscape = r.Cols.Count >= 9;
        }

        // ------------------------------------------------------------ helpers
        public static void Prepare(Graphics g)
        {
            g.PageUnit = GraphicsUnit.Inch; g.PageScale = 0.01f;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.InterpolationMode = InterpolationMode.HighQualityBicubic;
            g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
        }

        static Font F(float pt, bool bold) { return new Font("Segoe UI", pt, bold ? FontStyle.Bold : FontStyle.Regular, GraphicsUnit.Point); }

        /// <summary>A rectangle given from the start edge of the content (right in Arabic, left in English).</summary>
        RectangleF Box(float start, float y, float w, float h) { return new RectangleF(ltr ? Margin + start : Margin + CW - start - w, y, w, h); }

        StringFormat Fmt(StringAlignment a, bool numbers, bool wrapText)
        {
            StringFormat f = new StringFormat();
            f.Alignment = a; f.LineAlignment = StringAlignment.Center;
            if (!ltr && !numbers) f.FormatFlags |= StringFormatFlags.DirectionRightToLeft;
            if (!wrapText) { f.FormatFlags |= StringFormatFlags.NoWrap; f.Trimming = StringTrimming.EllipsisCharacter; }
            return f;
        }

        void Text(Graphics g, string s, float pt, bool bold, Color c, RectangleF b, StringAlignment a, bool numbers, bool wrapText)
        {
            if (string.IsNullOrEmpty(s)) return;
            using (Font f = F(pt, bold)) using (SolidBrush br = new SolidBrush(c)) using (StringFormat sf = Fmt(a, numbers, wrapText)) g.DrawString(s, f, br, b, sf);
        }

        float TextH(Graphics g, string s, float pt, bool bold, float w)
        {
            if (string.IsNullOrEmpty(s)) return 0;
            using (Font f = F(pt, bold)) using (StringFormat sf = Fmt(StringAlignment.Near, false, true)) return g.MeasureString(s, f, new SizeF(w, 5000), sf).Height;
        }

        static float TextW(Graphics g, string s, Font f) { return string.IsNullOrEmpty(s) ? 0 : g.MeasureString(s, f).Width; }

        static string Sep(string s) { return (s ?? "").Replace(" — ", "  •  "); }

        static string Cell(object v, ColKind k)
        {
            if (v == null || v is DBNull) return "";
            if (v is string) return (string)v;
            switch (k)
            {
                case ColKind.Num: return UI.Num(Convert.ToDouble(v));
                case ColKind.Int: return Convert.ToDouble(v).ToString("#,##0", Inv);
                case ColKind.Date: return v is DateTime ? UI.Date((DateTime)v) : Convert.ToString(v);
                default: return Convert.ToString(v);
            }
        }

        static bool IsNum(ColKind k) { return k != ColKind.Text; }

        // ------------------------------------------------------------ layout
        /// <summary>Divides the report into pages. Call again after changing Landscape, the paper or TextScale.</summary>
        public void Build()
        {
            pages.Clear();
            using (Bitmap bmp = new Bitmap(8, 8))
            {
                bmp.SetResolution(300, 300);       // measure like a printer, so screen and paper break lines alike
                using (Graphics g = Graphics.FromImage(bmp))
                {
                    Prepare(g);
                    List<Block> blocks = new List<Block>();
                    TopBlocks(g, blocks);
                    TableBlocks(g, blocks);
                    EndBlocks(g, blocks);
                    Place(blocks);
                }
            }
        }

        void Place(List<Block> blocks)
        {
            float top = Margin, bottom = H - Margin - FootH, y = top;
            List<Placed> page = new List<Placed>(); pages.Add(page);
            bool headOnPage = false;
            foreach (Block b in blocks)
            {
                float need = b.H + (b.Row && !headOnPage ? tableHead.H : 0);
                if (y + need > bottom && page.Count > 0) { page = new List<Placed>(); pages.Add(page); y = top; headOnPage = false; }
                if (b.Row && !headOnPage) { page.Add(new Placed { Y = y, B = tableHead }); y += tableHead.H; headOnPage = true; }
                page.Add(new Placed { Y = y, B = b }); y += b.H;
            }
        }

        void TopBlocks(Graphics m, List<Block> list)
        {
            string meta = Lang.T("تاريخ الطباعة: ") + DateTime.Now.ToString("yyyy/MM/dd  HH:mm", Inv) + "\n" + Lang.T("المستخدم: ") + Session.UserName;
            list.Add(new Block { H = 58, Draw = delegate(Graphics g, float y)
            {
                Image logo = Theme.Logo;
                if (logo != null)
                {
                    float h = 36, w = Math.Min(CW * 0.45f, h * logo.Width / Math.Max(1, logo.Height)); h = w * logo.Height / Math.Max(1, logo.Width);
                    g.DrawImage(logo, Box(0, y + (36 - h) / 2, w, h));
                }
                else Text(g, Program.Company, 12.5f, true, Brand, Box(0, y, CW * 0.6f, 24), StringAlignment.Near, false, false);
                Text(g, Program.AppName, 8f, false, Grey, Box(0, logo != null ? y + 36 : Program.Company.Length > 0 ? y + 24 : y + 6, CW * 0.6f, 15), StringAlignment.Near, false, false);
                Text(g, meta, 8f, false, Grey, Box(CW * 0.6f, y + 2, CW * 0.4f, 34), StringAlignment.Far, false, true);
                using (Pen p = new Pen(Green, 2.4f)) g.DrawLine(p, Margin, y + 53, Margin + CW, y + 53);
            } });

            string title = Sep(r.Title);
            float th = TextH(m, title, 15, true, CW) + 6;
            list.Add(new Block { H = th, Draw = delegate(Graphics g, float y) { Text(g, title, 15, true, Blue, Box(0, y, CW, th), StringAlignment.Near, false, true); } });

            if (r.Info != null) InfoBlocks(m, list);
            else if (!string.IsNullOrEmpty(r.Subtitle))
            {
                string sub = Sep(r.Subtitle);
                float sh = TextH(m, sub, 9, false, CW) + 6;
                list.Add(new Block { H = sh, Draw = delegate(Graphics g, float y) { Text(g, sub, 9, false, Grey, Box(0, y, CW, sh), StringAlignment.Near, false, true); } });
            }
            SummaryBlocks(list);
            ChartBlocks(list);
            list.Add(new Block { H = 6, Draw = delegate { } });
        }

        /// <summary>Document header fields in a boxed grid, three in a row.</summary>
        void InfoBlocks(Graphics m, List<Block> list)
        {
            float cw = CW / 3;
            for (int i = 0; i < r.Info.Count; i += 3)
            {
                List<KeyValuePair<string, string>> row = r.Info.Skip(i).Take(3).ToList();
                float h = 26; List<float> keyW = new List<float>();
                using (Font fk = F(10, true))
                    foreach (KeyValuePair<string, string> kv in row)
                    {
                        float kw = Math.Min(cw * 0.6f, TextW(m, kv.Key + ":", fk) + 4); keyW.Add(kw);
                        h = Math.Max(h, TextH(m, kv.Value, 10, false, cw - kw - 14) + 10);
                    }
                list.Add(new Block { H = h, Draw = delegate(Graphics g, float y)
                {
                    using (Pen p = new Pen(Line, 0.6f))
                        for (int c = 0; c < 3; c++)
                        {
                            RectangleF b = Box(c * cw, y, cw, h);
                            g.DrawRectangle(p, b.X, b.Y, b.Width, b.Height);
                            if (c >= row.Count) continue;
                            Text(g, row[c].Key + ":", 10, true, Blue, Box(c * cw + 6, y, keyW[c], h), StringAlignment.Near, false, false);
                            Text(g, row[c].Value, 10, false, Ink, Box(c * cw + 6 + keyW[c], y, cw - keyW[c] - 10, h), StringAlignment.Near, false, true);
                        }
                } });
            }
        }

        void SummaryBlocks(List<Block> list)
        {
            List<KeyValuePair<string, string>> sum = r.Summary.Select(kv => new KeyValuePair<string, string>(Lang.T(kv.Key), kv.Value)).ToList();
            if (r.Info != null || sum.Count == 0) return;
            int per = Landscape ? 6 : 4; float gap = 6, w = (CW - gap * (per - 1)) / per;
            for (int i = 0; i < sum.Count; i += per)
            {
                List<KeyValuePair<string, string>> row = sum.Skip(i).Take(per).ToList();
                list.Add(new Block { H = 46, Draw = delegate(Graphics g, float y)
                {
                    for (int c = 0; c < row.Count; c++)
                    {
                        RectangleF b = Box(c * (w + gap), y + 2, w, 39);
                        using (SolidBrush br = new SolidBrush(Color.FromArgb(250, 251, 252))) g.FillRectangle(br, b);
                        using (Pen p = new Pen(Color.FromArgb(215, 220, 228), 0.6f)) g.DrawRectangle(p, b.X, b.Y, b.Width, b.Height);
                        Text(g, row[c].Key, 7.5f, false, Grey, Box(c * (w + gap) + 6, y + 3, w - 12, 15), StringAlignment.Near, false, false);
                        Text(g, row[c].Value, 10.5f, true, Blue, Box(c * (w + gap) + 6, y + 18, w - 12, 21), StringAlignment.Near, false, false);
                    }
                } });
            }
        }

        void ChartBlocks(List<Block> list)
        {
            if (r.Chart == null || r.Chart.Count == 0) return;
            double max = 1;
            foreach (KeyValuePair<string, double[]> kv in r.Chart) foreach (double v in kv.Value) max = Math.Max(max, v);
            if (r.ChartSeries != null && r.ChartSeries.Length > 1)
                list.Add(new Block { H = 18, Draw = delegate(Graphics g, float y)
                {
                    for (int s = 0; s < r.ChartSeries.Length; s++)
                    {
                        using (SolidBrush br = new SolidBrush(Series[s % 4])) g.FillRectangle(br, Box(s * 110, y + 4, 9, 9));
                        Text(g, r.ChartSeries[s], 8, false, Ink, Box(s * 110 + 13, y, 95, 17), StringAlignment.Near, false, false);
                    }
                } });
            foreach (KeyValuePair<string, double[]> kv0 in r.Chart.Take(60))
            {
                KeyValuePair<string, double[]> kv = kv0;
                float h = Math.Max(16, kv.Value.Length * 7 + 5);
                list.Add(new Block { H = h, Draw = delegate(Graphics g, float y)
                {
                    float lw = CW * 0.24f, vw = CW * 0.16f, bw = CW - lw - vw - 12;
                    Text(g, kv.Key, 8, false, Ink, Box(0, y, lw, h), StringAlignment.Near, false, false);
                    float bh = (h - 5) / kv.Value.Length;
                    for (int s = 0; s < kv.Value.Length; s++)
                        using (SolidBrush br = new SolidBrush(Series[s % 4])) g.FillRectangle(br, Box(lw + 4, y + 2 + s * bh, Math.Max(1, (float)(kv.Value[s] / max) * bw), Math.Max(3, bh - 1)));
                    Text(g, string.Join(" / ", kv.Value.Select(v => UI.Num(v)).ToArray()), 8, false, Ink, Box(CW - vw, y, vw, h), StringAlignment.Far, true, false);
                } });
            }
        }

        void Columns(Graphics g)
        {
            int n = r.Cols.Count;
            fontSize = (n >= 14 ? 6.6f : n >= 11 ? 7.4f : n >= 9 ? 8f : r.Info != null ? 10.5f : 9f) * TextScale;
            widths = new float[n]; wrap = new bool[n]; starts = new float[n];
            float[] min = new float[n];
            using (Font f = F(fontSize, false)) using (Font fb = F(fontSize, true))
            {
                for (int c = 0; c < n; c++)
                {
                    bool text = r.Cols[c].Kind == ColKind.Text;
                    List<string> cells = new List<string>();
                    // numbers must never be cut: the longest of all rows; texts (they wrap): a sample is enough
                    foreach (object[] row in text ? r.Rows.Take(3000) : r.Rows) if (c < row.Length) { string s = Cell(row[c], r.Cols[c].Kind); if (s.Length > 0) cells.Add(s); }
                    // text: nine of ten rows fit on one line, the few longer ones wrap; numbers always fit
                    List<string> byLen = cells.OrderByDescending(s => s.Length).ToList();
                    if (text && byLen.Count > 20) byLen = byLen.Skip(byLen.Count / 10).ToList();
                    float w = 0;
                    foreach (string s in byLen.Take(12)) w = Math.Max(w, TextW(g, s, f));
                    if (r.Cols[c].Total) w = Math.Max(w, TextW(g, UI.Num(Total(c)), fb));
                    float hw = 0; foreach (string word in r.Cols[c].Header.Split(' ')) hw = Math.Max(hw, TextW(g, word, fb));
                    wrap[c] = text && (w > 130 || byLen.Count < cells.Count);
                    widths[c] = Math.Max(Math.Min(w, text ? 300 : 400), hw) + 8;
                    min[c] = Math.Max(hw + 8, text ? Math.Min(widths[c], 80) : widths[c]);
                }
            }
            float sum = widths.Sum();
            if (sum > CW)
            {
                // narrow the long text columns first (they wrap), then everything in proportion
                float over = sum - CW, room = 0;
                for (int c = 0; c < n; c++) room += widths[c] - min[c];
                if (room > 0)
                {
                    float k = Math.Min(1, over / room);
                    for (int c = 0; c < n; c++) { if (widths[c] > min[c]) { widths[c] -= (widths[c] - min[c]) * k; wrap[c] = r.Cols[c].Kind == ColKind.Text; } }
                }
                sum = widths.Sum();
                if (sum > CW) { float k = CW / sum; fontSize = Math.Max(5.5f, fontSize * Math.Max(0.75f, k)); for (int c = 0; c < n; c++) widths[c] *= k; }
            }
            else
            {
                float extra = CW - sum, tw = 0;
                for (int c = 0; c < n; c++) if (r.Cols[c].Kind == ColKind.Text) tw += widths[c];
                for (int c = 0; c < n; c++)
                    if (tw > 0) { if (r.Cols[c].Kind == ColKind.Text) widths[c] += extra * widths[c] / tw; }
                    else widths[c] += extra / n;
            }
            float x = 0; for (int c = 0; c < n; c++) { starts[c] = x; x += widths[c]; }
        }

        double Total(int c)
        {
            double t = 0;
            for (int i = 0; i < r.Rows.Count; i++)
            {
                object v = c < r.Rows[i].Length ? r.Rows[i][c] : null;
                if (v == null || v is DBNull || v is string || r.RowClass[i] == "open") continue;
                t += Convert.ToDouble(v);
            }
            return t;
        }

        void TableBlocks(Graphics m, List<Block> list)
        {
            int n = r.Cols.Count;
            if (n == 0) return;
            Columns(m);
            float line; using (Font f = F(fontSize, false)) line = f.GetHeight(m) + (r.Info != null ? 10f : 5.5f);

            // the header row (repeated on every page)
            float hh = line;
            for (int c = 0; c < n; c++) hh = Math.Max(hh, TextH(m, r.Cols[c].Header, fontSize, true, widths[c] - 4) + 6);
            tableHead = new Block { H = hh, Draw = delegate(Graphics g, float y)
            {
                using (SolidBrush br = new SolidBrush(Head)) g.FillRectangle(br, Margin, y, CW, hh);
                using (Pen p = new Pen(Line, 0.6f)) for (int c = 0; c < n; c++) { RectangleF b = Box(starts[c], y, widths[c], hh); g.DrawRectangle(p, b.X, b.Y, b.Width, b.Height); }
                for (int c = 0; c < n; c++) Text(g, r.Cols[c].Header, fontSize, true, Blue, Box(starts[c] + 1, y, widths[c] - 2, hh), StringAlignment.Center, false, true);
                using (Pen p = new Pen(Brand, 1.4f)) g.DrawLine(p, Margin, y + hh, Margin + CW, y + hh);
            } };

            if (r.Rows.Count == 0)
            {
                string none = Lang.T("لا توجد بيانات");
                list.Add(new Block { H = 40, Row = true, Draw = delegate(Graphics g, float y) { Text(g, none, 10, false, Grey, Box(0, y, CW, 40), StringAlignment.Center, false, false); } });
                return;
            }
            using (Font f = F(fontSize, false))
                for (int i = 0; i < r.Rows.Count; i++)
                {
                    object[] row = r.Rows[i]; string cls = r.RowClass[i]; bool alt = i % 2 == 1; float h = line;
                    string[] txt = new string[n];
                    for (int c = 0; c < n; c++)
                    {
                        txt[c] = c < row.Length ? Cell(row[c], r.Cols[c].Kind) : "";
                        // measured only when it may not fit on one line
                        if (wrap[c] && txt[c].Length > 8 && TextW(m, txt[c], f) > widths[c] - 8) h = Math.Max(h, TextH(m, txt[c], fontSize, false, widths[c] - 6) + 5);
                    }
                    // a row cannot be higher than a page: very long texts end with «…»
                    float most = H - 2 * Margin - FootH - hh - 4;
                    if (h > most) { h = most; for (int c = 0; c < n; c++) if (wrap[c]) txt[c] = Cut(m, txt[c], widths[c] - 6, most - 5); }
                    float rh = h;
                    list.Add(new Block { H = rh, Row = true, Draw = delegate(Graphics g, float y) { DrawRow(g, y, rh, row, txt, cls, alt, false); } });
                }
            if (r.Cols.Any(c => c.Total))
            {
                string[] tot = new string[n]; bool label = false;
                for (int c = 0; c < n; c++)
                {
                    if (r.Cols[c].Total) tot[c] = UI.Num(Total(c));
                    else if (!label && (widths[c] > 45 || c == n - 1)) { tot[c] = Lang.T("المجموع"); label = true; }
                    else tot[c] = "";
                }
                float lh = line + 3;
                list.Add(new Block { H = lh, Row = true, Draw = delegate(Graphics g, float y) { DrawRow(g, y, lh, null, tot, null, false, true); } });
            }
        }

        /// <summary>The longest beginning of the text that fits into the height.</summary>
        string Cut(Graphics m, string s, float w, float h)
        {
            if (TextH(m, s, fontSize, false, w) <= h) return s;
            int lo = 0, hi = s.Length;
            while (hi - lo > 1) { int mid = (lo + hi) / 2; if (TextH(m, s.Substring(0, mid) + "…", fontSize, false, w) <= h) lo = mid; else hi = mid; }
            return s.Substring(0, lo) + "…";
        }

        void DrawRow(Graphics g, float y, float h, object[] row, string[] txt, string cls, bool alt, bool total)
        {
            Color back = total || cls == "open" ? Head : cls == "neg" ? Color.FromArgb(255, 246, 245) : alt ? Color.FromArgb(250, 251, 253) : Color.White;
            if (back != Color.White) using (SolidBrush br = new SolidBrush(back)) g.FillRectangle(br, Margin, y, CW, h);
            using (Pen p = new Pen(Color.FromArgb(187, 187, 187), 0.5f))
                for (int c = 0; c < r.Cols.Count; c++) { RectangleF b = Box(starts[c], y, widths[c], h); g.DrawRectangle(p, b.X, b.Y, b.Width, b.Height); }
            if (total) using (Pen p = new Pen(Brand, 1.2f)) g.DrawLine(p, Margin, y, Margin + CW, y);
            bool bold = total || cls == "open";
            for (int c = 0; c < r.Cols.Count; c++)
            {
                if (txt[c].Length == 0) continue;
                bool num = IsNum(r.Cols[c].Kind) && !(total && !r.Cols[c].Total);
                Color col = cls == "in" ? Color.FromArgb(15, 123, 69) : cls == "muted" ? Color.FromArgb(120, 127, 136) : Ink;
                if (!total && num && row != null && c < row.Length && r.Cols[c].Kind != ColKind.Date && row[c] != null && !(row[c] is string) && !(row[c] is DBNull))
                {
                    double d; try { d = Convert.ToDouble(row[c]); } catch { d = 1; }
                    if (Math.Abs(d) < 1e-9) col = Color.FromArgb(136, 136, 136); else if (d < 0) col = Color.FromArgb(196, 43, 28);
                }
                Text(g, txt[c], fontSize, bold, col, Box(starts[c] + (num ? 1 : 3), y, widths[c] - (num ? 2 : 5), h), num ? StringAlignment.Center : StringAlignment.Near, num, wrap[c]);
            }
        }

        void EndBlocks(Graphics m, List<Block> list)
        {
            if (!string.IsNullOrEmpty(r.Footer))
            {
                string foot = r.Footer;
                // long notes are cut into parts, so a part never exceeds a page
                foreach (string part in foot.Split('\n'))
                {
                    string t = part.Trim(); if (t.Length == 0) continue;
                    float fh = TextH(m, t, 8.5f, false, CW - 18) + 10;
                    list.Add(new Block { H = fh + 6, Draw = delegate(Graphics g, float y)
                    {
                        using (SolidBrush br = new SolidBrush(Color.FromArgb(250, 251, 252))) g.FillRectangle(br, Margin, y + 6, CW, fh);
                        using (SolidBrush br = new SolidBrush(Brand)) g.FillRectangle(br, Box(0, y + 6, 2.4f, fh));
                        Text(g, t, 8.5f, false, Color.FromArgb(60, 60, 60), Box(9, y + 6, CW - 16, fh), StringAlignment.Near, false, true);
                    } });
                }
            }
            if (!r.Signatures) return;
            string[] sig = r.SigLabels ?? new[] { "المستلم", "أمين المخزن", "المدير" };
            list.Add(new Block { H = 100, Draw = delegate(Graphics g, float y)
            {
                float w = CW / sig.Length;
                for (int i = 0; i < sig.Length; i++)
                {
                    RectangleF b = Box(i * w + 18, y + 70, w - 36, 22);
                    using (Pen p = new Pen(Color.FromArgb(60, 60, 60), 0.8f)) g.DrawLine(p, b.X, b.Y, b.Right, b.Y);
                    Text(g, Lang.T(sig[i]), 9, false, Ink, b, StringAlignment.Center, false, false);
                }
            } });
        }

        // ------------------------------------------------------------ drawing
        /// <summary>Draws one page (g already prepared with Prepare, origin = the sheet's corner).</summary>
        public void Draw(Graphics g, int index)
        {
            if (index < 0 || index >= pages.Count) return;
            foreach (Placed p in pages[index]) p.B.Draw(g, p.Y);
            float y = H - Margin - 14;
            using (Pen pen = new Pen(Color.FromArgb(210, 214, 220), 0.5f)) g.DrawLine(pen, Margin, y - 2, Margin + CW, y - 2);
            Text(g, Sep(r.Title), 7.5f, false, Grey, Box(0, y, CW * 0.7f, 14), StringAlignment.Near, false, false);
            Text(g, (index + 1).ToString(Inv) + " / " + pages.Count.ToString(Inv), 8.5f, false, Grey, Box(CW * 0.7f, y, CW * 0.3f, 14), ltr ? StringAlignment.Far : StringAlignment.Near, true, false);
        }

        public Bitmap Image(int index, float dpi)
        {
            Bitmap b = new Bitmap(Math.Max(1, (int)(W / 100 * dpi)), Math.Max(1, (int)(H / 100 * dpi)));
            b.SetResolution(dpi, dpi);
            using (Graphics g = Graphics.FromImage(b)) { g.Clear(Color.White); Prepare(g); Draw(g, index); }
            return b;
        }

        /// <summary>The pages as a print job (page range, copies and printer come from the settings).</summary>
        public PrintDocument Document(PrinterSettings ps) { return Document(ps, null); }

        /// <summary>progress: called before every page with its number from 0 and the number of pages; false = stop.</summary>
        public PrintDocument Document(PrinterSettings ps, Func<int, int, bool> progress)
        {
            PrintDocument d = new PrintDocument();
            d.PrinterSettings = ps;
            d.DocumentName = new string((r.Title ?? "").Select(ch => char.IsControl(ch) ? ' ' : ch).ToArray());
            d.OriginAtMargins = false;
            PaperSize paper = null;
            try { foreach (PaperSize s in ps.PaperSizes) if (Math.Abs(s.Width - PaperW) < 6 && Math.Abs(s.Height - PaperH) < 6) { paper = s; break; } } catch { }
            if (paper == null) { paper = new PaperSize("Custom", (int)PaperW, (int)PaperH); if (Math.Abs(PaperW - 827) < 2 && Math.Abs(PaperH - 1169) < 2) paper.RawKind = (int)PaperKind.A4; }
            d.DefaultPageSettings.PaperSize = paper;
            d.DefaultPageSettings.Landscape = Landscape;
            d.DefaultPageSettings.Margins = new Margins(0, 0, 0, 0);
            int i = 0, last = pages.Count - 1;
            float hx = float.NaN, hy = 0;        // read once: asking the printer takes 0.2 s
            d.BeginPrint += delegate
            {
                i = 0; last = pages.Count - 1; hx = float.NaN;
                if (ps.PrintRange == PrintRange.SomePages) { i = Math.Max(0, ps.FromPage - 1); last = Math.Min(last, ps.ToPage - 1); }
            };
            d.PrintPage += delegate(object s, PrintPageEventArgs e)
            {
                if (progress != null && !progress(i, pages.Count)) { e.Cancel = true; return; }
                Prepare(e.Graphics);
                e.Graphics.TextRenderingHint = TextRenderingHint.SystemDefault;
                if (float.IsNaN(hx)) { hx = e.PageSettings.HardMarginX; hy = e.PageSettings.HardMarginY; }
                e.Graphics.TranslateTransform(-hx, -hy);
                Draw(e.Graphics, i);
                i++; e.HasMorePages = i <= last;
            };
            return d;
        }
    }
}
