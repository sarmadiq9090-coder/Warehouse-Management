using System.Diagnostics;
using System.Drawing.Printing;
using System.IO;
using System.Text;
using System.Threading;
using System.Windows.Forms;
using System;

namespace Stores
{
    /// <summary>Saves a report or document as a PDF file without asking anything and without internet: Windows' own
    /// «Microsoft Print to PDF» printer writes the pages to the file. When that printer was removed from Windows,
    /// Microsoft Edge (part of Windows 10 / 11) makes the file from the report's HTML.</summary>
    static class Pdf
    {
        public static void Export(IWin32Window owner, ReportData r, ReportPages pages)
        {
            if (r == null) return;
            string path = Excel.AskPath(owner, r.Title, "pdf"); if (path == null) return;
            UI.WaitCursor(true);
            try { if (pages == null) { pages = new ReportPages(r); pages.Build(); } }
            catch (Exception ex) { UI.Error(ex); return; }
            finally { UI.WaitCursor(false); }
            if (pages.Count > 150 && !UI.Confirm("التقرير طويل: " + pages.Count + " صفحة.\nحفظه كملف PDF قد يستغرق عدة دقائق، أما «Excel» فيُحفظ أسرع.\n\nهل تريد المتابعة؟")) return;

            if (SaveWithWindow(owner, path, r, pages)) Excel.Done(path);
        }

        /// <summary>Saves behind a window with the progress and «إلغاء». False: cancelled or failed (the reason was shown).</summary>
        static bool SaveWithWindow(IWin32Window owner, string path, ReportData r, ReportPages pages)
        {
            Exception error = null; bool done = false;
            using (WaitForm w = new WaitForm("جاري حفظ ملف PDF", "", "إلغاء"))
            {
                DateTime t0 = DateTime.Now;
                w.Shown += delegate
                {
                    try
                    {
                        Save(path, r, pages, delegate(int page, int count)
                        {
                            double sec = (DateTime.Now - t0).TotalSeconds;
                            w.Progress(page < count ? "الصفحة " + (page + 1) + " من " + count : "جاري إكمال الملف…", 0.95 * page / Math.Max(1, count),
                                       page > 3 && page < count ? sec / page * (count - page) : -1);
                            Application.DoEvents();
                            return !w.CancelPressed;
                        });
                        done = true;
                    }
                    catch (OperationCanceledException) { }
                    catch (Exception ex) { error = ex; }
                    finally { w.Close(); }
                };
                w.ShowDialog(owner);       // modal: nothing else can be clicked meanwhile
            }
            if (error is UserError) UI.Warn(error.Message);
            else if (error != null) UI.Error(error);
            return done;
        }

        /// <summary>pages: an already built layout (the preview's orientation / paper), null = the default one.</summary>
        public static void Save(string path, ReportData r, ReportPages pages) { Save(path, r, pages, null); }

        /// <summary>progress(page, pages): also called while the file is completed (page = pages); false = stop, which
        /// throws OperationCanceledException.</summary>
        public static void Save(string path, ReportData r, ReportPages pages, Func<int, int, bool> progress)
        {
            if (pages == null) { pages = new ReportPages(r); pages.Build(); }
            string tmp = Path.Combine(Path.GetTempPath(), "EngStores_" + Guid.NewGuid().ToString("N") + ".pdf");
            try
            {
                string printer = PdfPrinter();
                if (printer != null) ByPrinter(printer, pages, tmp, progress);
                else if (!ByEdge(r, tmp, progress))
                    throw new UserError("تعذر إنشاء ملف PDF: طابعة «Microsoft Print to PDF» غير موجودة في هذا الحاسوب.\nفعّلها من: إعدادات ويندوز ← الميزات الاختيارية، أو احفظ السند كملف Excel.");
                try { File.Copy(tmp, path, true); }        // an open old file is refused before anything is lost
                catch (IOException) { throw new UserError("الملف مفتوح في برنامج آخر:\n" + path + "\nأغلقه ثم أعد الحفظ، أو اختر اسما آخر."); }
                catch (UnauthorizedAccessException) { throw new UserError("لا يمكن الحفظ في هذا المكان:\n" + path + "\nاختر مجلدا آخر."); }
            }
            finally { try { if (File.Exists(tmp)) File.Delete(tmp); } catch { } }
        }

        static string PdfPrinter()
        {
            try
            {
                foreach (string p in PrinterSettings.InstalledPrinters)
                    if (p.IndexOf("Microsoft Print to PDF", StringComparison.OrdinalIgnoreCase) >= 0) return p;
            }
            catch { }        // the print spooler is stopped
            return null;
        }

        static void ByPrinter(string printer, ReportPages pages, string file, Func<int, int, bool> progress)
        {
            PrinterSettings ps = new PrinterSettings();
            ps.PrinterName = printer; ps.PrintToFile = true; ps.PrintFileName = file;
            bool stopped = false;
            Func<int, int, bool> step = null;
            if (progress != null) step = delegate(int p, int n) { if (!progress(p, n)) stopped = true; return !stopped; };
            using (PrintDocument d = pages.Document(ps, step))
            {
                d.PrintController = new StandardPrintController();   // no "printing page…" window
                d.Print();
            }
            if (stopped) throw new OperationCanceledException();
            Func<bool> alive = null;
            if (progress != null) alive = delegate { return progress(pages.Count, pages.Count); };
            // long reports: the spooler needs time after the last page
            if (!WaitComplete(file, 60 + pages.Count, alive)) throw new UserError("لم يكتمل إنشاء ملف PDF. أعد المحاولة.");
        }

        /// <summary>The spooler writes the file a moment after Print returns. Gives up when the file has not grown for the
        /// given number of seconds.</summary>
        static bool WaitComplete(string file, int seconds, Func<bool> alive)
        {
            DateTime end = DateTime.Now.AddSeconds(seconds); long last = -1;
            while (DateTime.Now < end)
            {
                Thread.Sleep(120);
                if (alive != null && !alive()) throw new OperationCanceledException();
                if (!File.Exists(file)) continue;
                try
                {
                    using (FileStream f = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
                    {
                        if (f.Length < 100 || f.Length != last) { if (f.Length != last) end = DateTime.Now.AddSeconds(seconds); last = f.Length; continue; }
                        byte[] tail = new byte[Math.Min(1024, (int)f.Length)];
                        f.Seek(-tail.Length, SeekOrigin.End); f.Read(tail, 0, tail.Length);
                        if (Encoding.ASCII.GetString(tail).Contains("%%EOF")) return true;
                    }
                }
                catch (IOException) { }
            }
            return false;
        }

        static string EdgePath()
        {
            foreach (string pf in new[] { Environment.GetEnvironmentVariable("ProgramFiles(x86)"), Environment.GetEnvironmentVariable("ProgramW6432"), Environment.GetEnvironmentVariable("ProgramFiles") })
            {
                if (string.IsNullOrEmpty(pf)) continue;
                string p = Path.Combine(pf, @"Microsoft\Edge\Application\msedge.exe");
                if (File.Exists(p)) return p;
            }
            return null;
        }

        static bool ByEdge(ReportData r, string file, Func<int, int, bool> progress)
        {
            string edge = EdgePath(); if (edge == null) return false;
            string work = Path.Combine(Path.GetTempPath(), "EngStoresPdf_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(work);
            try
            {
                string html = Path.Combine(work, "r.html");
                File.WriteAllText(html, Html.Render(r), new UTF8Encoding(true));
                ProcessStartInfo si = new ProcessStartInfo(edge, "--headless --disable-gpu --no-first-run --no-pdf-header-footer --user-data-dir=\"" + Path.Combine(work, "p") +
                    "\" --print-to-pdf=\"" + file + "\" \"" + new Uri(html).AbsoluteUri + "\"");
                si.UseShellExecute = false; si.CreateNoWindow = true;
                using (Process p = Process.Start(si))
                {
                    DateTime end = DateTime.Now.AddSeconds(45 + r.Rows.Count / 100); bool stop = false;
                    while (!p.WaitForExit(200) && !stop && DateTime.Now < end) stop = progress != null && !progress(1, 1);
                    if (!p.HasExited) { try { p.Kill(); } catch { } if (stop) throw new OperationCanceledException(); return false; }
                }
                return WaitComplete(file, 10, null);
            }
            catch (OperationCanceledException) { throw; }
            catch { return false; }
            finally { try { Directory.Delete(work, true); } catch { } }
        }
    }
}
