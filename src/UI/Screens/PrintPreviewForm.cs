using System.Drawing.Printing;
using System.Drawing;
using System.Windows.Forms;
using System;

namespace Stores
{
    /// <summary>Print preview: the pages exactly as they will be printed, then print / PDF / Excel.
    /// Works without any printer installed (the pages are drawn by the program, see ReportPages).</summary>
    class PrintPreviewForm : Form
    {
        readonly ReportData data;
        readonly ReportPages pages;
        readonly Panel area = new Panel();
        readonly PictureBox pic = new PictureBox();
        readonly Label lblPage = new Label();
        readonly Button btnPrev, btnNext, btnTurn;
        readonly ComboBox cmbPaper;
        int page; float zoom = 0;       // 0 = fit the window's width
        DateTime lastTurn;

        /// <summary>The user asked to open the document for editing (RowLinks opens it after this window closes).</summary>
        public bool EditRequested;

        static readonly object[][] Papers = { new object[] { "A4", 827f, 1169f }, new object[] { "A5", 583f, 827f }, new object[] { "A3", 1169f, 1654f }, new object[] { "Letter", 850f, 1100f } };

        public PrintPreviewForm(ReportData r, bool canEdit)
        {
            data = r; pages = new ReportPages(r);
            Text = "معاينة قبل الطباعة • " + r.Title;
            RightToLeft = RightToLeft.Yes; Font = Theme.Font; Icon = Theme.AppIcon; BackColor = Theme.Bg;
            StartPosition = FormStartPosition.CenterParent; ShowInTaskbar = false; KeyPreview = true;
            Rectangle wa = Screen.FromPoint(Cursor.Position).WorkingArea;
            Size = new Size(Math.Min(1120, wa.Width - 40), Math.Min(900, wa.Height - 30));
            MinimumSize = new Size(760, 480);

            FlowLayoutPanel bar = UI.Flow(); bar.BackColor = Theme.Card; bar.Padding = new Padding(6);
            Button print = UI.Btn("طباعة  (Ctrl+P)", Theme.Primary, 140), pdf = UI.Btn("حفظ PDF", Theme.Teal, 110), xl = UI.Btn("حفظ Excel", Theme.Accent, 110);
            Button edit = UI.BtnLight("فتح المستند للتعديل", 160); edit.Visible = canEdit;
            btnPrev = UI.BtnLight("الصفحة السابقة", 112); btnNext = UI.BtnLight("الصفحة التالية", 112);
            lblPage.AutoSize = false; lblPage.Size = new Size(78, 32); lblPage.TextAlign = ContentAlignment.MiddleCenter; lblPage.ForeColor = Theme.Text; lblPage.Margin = new Padding(2, 6, 2, 0);
            Button zin = UI.BtnLight("تكبير +", 78), zout = UI.BtnLight("تصغير −", 78), fit = UI.BtnLight("ملء العرض", 92);
            btnTurn = UI.BtnLight("عرضي", 80);
            cmbPaper = UI.Combo(80);
            foreach (object[] p in Papers) cmbPaper.Items.Add(p[0]);
            cmbPaper.SelectedIndex = 0;
            Button close = UI.BtnLight("إغلاق", 80);
            bar.Controls.AddRange(new Control[] { print, pdf, xl, edit, btnPrev, lblPage, btnNext, zin, zout, fit, btnTurn, UI.Field("الورق", cmbPaper), close });

            area.Dock = DockStyle.Fill; area.AutoScroll = true; area.BackColor = Theme.Bg; area.RightToLeft = RightToLeft.No;
            pic.SizeMode = PictureBoxSizeMode.AutoSize; pic.BackColor = Color.White;
            area.Controls.Add(pic);
            Controls.Add(area); Controls.Add(bar);
            Lang.Apply(this);
            ThemePainter.Attach(this);
            pic.BackColor = Color.White;

            print.Click += delegate { Print(); };
            pdf.Click += delegate { Pdf.Export(this, data, pages); };
            xl.Click += delegate { Excel.Export(this, data); };
            edit.Click += delegate { EditRequested = true; Close(); };
            btnPrev.Click += delegate { Go(page - 1); };
            btnNext.Click += delegate { Go(page + 1); };
            zin.Click += delegate { Zoom(1.2f); };
            zout.Click += delegate { Zoom(1 / 1.2f); };
            fit.Click += delegate { zoom = 0; Draw(); };
            btnTurn.Click += delegate { pages.Landscape = !pages.Landscape; Rebuild(); };
            cmbPaper.SelectedIndexChanged += delegate
            {
                object[] p = Papers[Math.Max(0, cmbPaper.SelectedIndex)];
                pages.PaperW = (float)p[1]; pages.PaperH = (float)p[2]; Rebuild();
            };
            close.Click += delegate { Close(); };
            area.Resize += delegate { if (zoom == 0) Draw(); else Center(); };
            MouseWheel += Wheel; area.MouseWheel += Wheel;
            pic.MouseDown += delegate { area.Focus(); };
            Load += delegate { Rebuild(); area.Focus(); };
            FormClosed += delegate { if (pic.Image != null) { pic.Image.Dispose(); pic.Image = null; } };
        }

        public static void Show(IWin32Window owner, ReportData r)
        {
            if (r == null) return;
            using (PrintPreviewForm f = new PrintPreviewForm(r, false)) f.ShowDialog(owner);
        }

        public static void ShowVoucher(IWin32Window owner, Doc d) { Show(owner, Reports.Voucher(d)); }

        void Rebuild()
        {
            UI.WaitCursor(true);
            try { pages.Build(); } catch (Exception ex) { UI.Error(ex); } finally { UI.WaitCursor(false); }
            btnTurn.Text = pages.Landscape ? "طولي" : "عرضي";      // the orientation it changes to
            Go(0);
        }

        void Go(int p)
        {
            if (pages.Count == 0) return;
            page = Math.Max(0, Math.Min(pages.Count - 1, p));
            area.AutoScrollPosition = Point.Empty;
            Draw();
            if (zoom > 0) StartSide();
        }

        /// <summary>The text starts at the right in Arabic: show that side of a zoomed page.</summary>
        void StartSide() { if (!Lang.English) area.AutoScrollPosition = new Point(area.HorizontalScroll.Maximum, -area.AutoScrollPosition.Y); }

        float Dpi { get { return zoom > 0 ? zoom : Math.Max(40, (area.ClientSize.Width - 48) / pages.W * 100); } }

        void Zoom(float k)
        {
            zoom = Math.Max(40, Math.Min(240, Dpi * k)); Draw();
            StartSide();
        }

        void Draw()
        {
            if (pages.Count == 0 || area.ClientSize.Width < 60) return;
            Image old = pic.Image;
            float dpi = Math.Min(Dpi, zoom > 0 ? 240 : 130);
            pic.Image = pages.Image(page, dpi);
            if (old != null) old.Dispose();
            lblPage.Text = (page + 1) + " / " + pages.Count;
            btnPrev.Enabled = page > 0; btnNext.Enabled = page < pages.Count - 1;
            Center();
        }

        void Center()
        {
            Point s = area.AutoScrollPosition;
            pic.Location = new Point(Math.Max(12, (area.ClientSize.Width - pic.Width) / 2) + s.X, 12 + s.Y);
        }

        /// <summary>Wheel: scroll inside the page; at its end the next page. Ctrl + wheel: zoom.</summary>
        void Wheel(object sender, MouseEventArgs e)
        {
            HandledMouseEventArgs h = e as HandledMouseEventArgs;
            if ((ModifierKeys & Keys.Control) != 0) { if (h != null) h.Handled = true; Zoom(e.Delta > 0 ? 1.15f : 1 / 1.15f); return; }
            int pos = -area.AutoScrollPosition.Y, max = Math.Max(0, area.VerticalScroll.Maximum - area.VerticalScroll.LargeChange + 1);
            if ((DateTime.Now - lastTurn).TotalMilliseconds < 350) return;
            if (e.Delta < 0 && pos >= max - 2 && page < pages.Count - 1) { lastTurn = DateTime.Now; if (h != null) h.Handled = true; Go(page + 1); }
            else if (e.Delta > 0 && pos <= 2 && page > 0) { lastTurn = DateTime.Now; if (h != null) h.Handled = true; Go(page - 1); }
        }

        void Print()
        {
            try
            {
                if (PrinterSettings.InstalledPrinters.Count == 0) { UI.Warn("لا توجد طابعة معرّفة في هذا الحاسوب.\nيمكنك حفظ المستند كملف PDF أو Excel."); return; }
                using (PrintDialog d = new PrintDialog())
                {
                    d.UseEXDialog = true; d.AllowSomePages = pages.Count > 1;
                    d.PrinterSettings = new PrinterSettings();
                    d.PrinterSettings.MinimumPage = 1; d.PrinterSettings.MaximumPage = pages.Count; d.PrinterSettings.FromPage = 1; d.PrinterSettings.ToPage = pages.Count;
                    if (d.ShowDialog(this) != DialogResult.OK) return;
                    using (PrintDocument doc = pages.Document(d.PrinterSettings)) doc.Print();
                }
            }
            catch (InvalidPrinterException ex) { UI.Warn("تعذر استعمال الطابعة:\n" + ex.Message); }
            catch (System.ComponentModel.Win32Exception ex) { if (ex.NativeErrorCode != 1223) UI.Warn("تعذرت الطباعة:\n" + ex.Message); }   // 1223: cancelled in the printer's own window
            catch (Exception ex) { UI.Error(ex); }
        }

        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            switch (keyData)
            {
                case Keys.Escape: Close(); return true;
                case Keys.Control | Keys.P: Print(); return true;
                case Keys.PageDown: Go(page + 1); return true;
                case Keys.PageUp: Go(page - 1); return true;
                case Keys.Control | Keys.Add: case Keys.Control | Keys.Oemplus: Zoom(1.2f); return true;
                case Keys.Control | Keys.Subtract: case Keys.Control | Keys.OemMinus: Zoom(1 / 1.2f); return true;
            }
            return base.ProcessCmdKey(ref msg, keyData);
        }
    }
}
