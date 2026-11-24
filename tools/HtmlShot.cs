using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;

// Renders HTML files with the same engine the program uses (WebBrowser / IE11 mode) and saves PNGs.
// Usage: HtmlShot.exe <width> <height> <out dir> <file.html>...
[StructLayout(LayoutKind.Sequential)] struct RECT { public int Left, Top, Right, Bottom; }

[ComImport, Guid("0000010d-0000-0000-C000-000000000046"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
interface IViewObject
{
    [PreserveSig] int Draw(uint aspect, int index, IntPtr pvAspect, IntPtr ptd, IntPtr hdcTarget, IntPtr hdcDraw, ref RECT bounds, IntPtr wBounds, IntPtr cont, uint dwCont);
}

static class HtmlShotMain
{
    [DllImport("user32.dll")] static extern bool PrintWindow(IntPtr hwnd, IntPtr hdc, uint flags);

    [STAThread]
    static void Main(string[] args)
    {
        int w = int.Parse(args[0]), h = int.Parse(args[1]); string outDir = args[2];
        Form f = new Form(); f.FormBorderStyle = FormBorderStyle.None; f.StartPosition = FormStartPosition.Manual;
        f.Location = new Point(-5000, -5000); f.ClientSize = new Size(w, h); f.ShowInTaskbar = false;
        WebBrowser wb = new WebBrowser(); wb.Dock = DockStyle.Fill; wb.ScriptErrorsSuppressed = true; wb.ScrollBarsEnabled = true;
        f.Controls.Add(wb); f.Show();
        for (int i = 3; i < args.Length; i++)
        {
            bool done = false;
            WebBrowserDocumentCompletedEventHandler h1 = delegate { done = true; };
            wb.DocumentCompleted += h1;
            wb.DocumentText = File.ReadAllText(args[i]);
            DateTime end = DateTime.Now.AddSeconds(20);
            while (!done && DateTime.Now < end) { Application.DoEvents(); Thread.Sleep(20); }
            wb.DocumentCompleted -= h1;
            for (int k = 0; k < 15; k++) { Application.DoEvents(); Thread.Sleep(20); }
            using (Bitmap b = new Bitmap(w, h))
            {
                using (Graphics g = Graphics.FromImage(b))
                {
                    g.Clear(Color.White);
                    IViewObject vo = wb.Document.DomDocument as IViewObject;
                    IntPtr dc = g.GetHdc();
                    RECT rc = new RECT(); rc.Right = w; rc.Bottom = h;
                    vo.Draw(1, -1, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, dc, ref rc, IntPtr.Zero, IntPtr.Zero, 0);
                    g.ReleaseHdc(dc);
                }
                b.Save(Path.Combine(outDir, Path.GetFileNameWithoutExtension(args[i]) + ".png"), ImageFormat.Png);
            }
            string mode = "";
            try { mode = Convert.ToString(wb.Document.InvokeScript("eval", new object[] { "document.documentMode" })); } catch { }
            Console.WriteLine(Path.GetFileName(args[i]) + " documentMode=" + mode);
        }
    }
}
