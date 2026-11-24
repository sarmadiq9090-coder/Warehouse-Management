using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Windows.Forms;

namespace Stores
{
    // Prints report HTML files with the program's own engine and print settings (PrintSetup) to the default printer
    // ("Microsoft Print to PDF"), answering its "Save Print Output As" dialog with a file name.
    static class PrintTestMain
    {
        delegate bool EnumProc(IntPtr h, IntPtr l);
        [DllImport("user32.dll")] static extern bool EnumWindows(EnumProc f, IntPtr l);
        [DllImport("user32.dll")] static extern bool EnumChildWindows(IntPtr p, EnumProc f, IntPtr l);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern int GetClassName(IntPtr h, StringBuilder s, int n);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern int GetWindowText(IntPtr h, StringBuilder s, int n);
        [DllImport("user32.dll")] static extern bool IsWindowVisible(IntPtr h);
        [DllImport("user32.dll")] static extern int GetDlgCtrlID(IntPtr h);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern IntPtr SendMessage(IntPtr h, int msg, IntPtr w, string l);
        [DllImport("user32.dll")] static extern IntPtr SendMessage(IntPtr h, int msg, IntPtr w, IntPtr l);
        [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);

        static string Cls(IntPtr h) { StringBuilder s = new StringBuilder(256); GetClassName(h, s, 256); return s.ToString(); }
        static string Txt(IntPtr h) { StringBuilder s = new StringBuilder(512); GetWindowText(h, s, 512); return s.ToString(); }

        /// <summary>Finds a visible save dialog (#32770 with an Edit box) owned by this process or the print helper.</summary>
        static IntPtr FindSaveDialog()
        {
            IntPtr found = IntPtr.Zero;
            int me = Process.GetCurrentProcess().Id;
            EnumWindows(delegate(IntPtr h, IntPtr l)
            {
                if (!IsWindowVisible(h) || Cls(h) != "#32770") return true;
                uint pid; GetWindowThreadProcessId(h, out pid);
                string pn = ""; try { pn = Process.GetProcessById((int)pid).ProcessName.ToLowerInvariant(); } catch { }
                if (pid != me && pn != "splwow64") return true;
                found = h; return false;
            }, IntPtr.Zero);
            return found;
        }

        static bool AnswerSave(IntPtr dlg, string file)
        {
            IntPtr edit = IntPtr.Zero;
            EnumChildWindows(dlg, delegate(IntPtr h, IntPtr l) { if (Cls(h) == "Edit" && IsWindowVisible(h)) { edit = h; return false; } return true; }, IntPtr.Zero);
            if (edit == IntPtr.Zero) return false;
            SendMessage(edit, 0x000C /*WM_SETTEXT*/, IntPtr.Zero, file);
            IntPtr ok = IntPtr.Zero;
            EnumChildWindows(dlg, delegate(IntPtr h, IntPtr l) { if (Cls(h) == "Button" && GetDlgCtrlID(h) == 1) { ok = h; return false; } return true; }, IntPtr.Zero);
            if (ok == IntPtr.Zero) return false;
            SendMessage(ok, 0x00F5 /*BM_CLICK*/, IntPtr.Zero, IntPtr.Zero);
            return true;
        }

        static void Pump(int ms) { DateTime end = DateTime.Now.AddMilliseconds(ms); while (DateTime.Now < end) { Application.DoEvents(); Thread.Sleep(20); } }

        [STAThread]
        static int Main(string[] args)
        {
            string outDir = args[0];
            Form f = new Form(); f.StartPosition = FormStartPosition.Manual; f.Location = new System.Drawing.Point(-5000, -5000); f.ShowInTaskbar = false;
            WebBrowser wb = new WebBrowser(); wb.Dock = DockStyle.Fill; wb.ScriptErrorsSuppressed = true; f.Controls.Add(wb); f.Show();
            int fails = 0;
            for (int i = 1; i < args.Length; i++)
            {
                string html = args[i];
                string pdf = Path.Combine(outDir, "print_" + Path.GetFileNameWithoutExtension(html) + ".pdf");
                bool done = false;
                WebBrowserDocumentCompletedEventHandler h1 = delegate { done = true; };
                wb.DocumentCompleted += h1;
                wb.DocumentText = File.ReadAllText(html);
                DateTime end = DateTime.Now.AddSeconds(30);
                while (!done && DateTime.Now < end) Pump(50);
                wb.DocumentCompleted -= h1;
                PrintSetup.Apply();                  // the program's print settings
                wb.Print();                          // default printer, no dialog
                bool answered = false;
                end = DateTime.Now.AddSeconds(60);
                while (DateTime.Now < end && !(File.Exists(pdf) && new FileInfo(pdf).Length > 0 && answered))
                {
                    Pump(200);
                    if (!answered) { IntPtr d = FindSaveDialog(); if (d != IntPtr.Zero) answered = AnswerSave(d, pdf); }
                }
                Pump(1500);
                bool ok = File.Exists(pdf) && new FileInfo(pdf).Length > 0;
                if (!ok) fails++;
                Console.WriteLine((ok ? "PRINTED " : "FAILED  ") + Path.GetFileName(html) + " -> " + pdf + (ok ? " (" + new FileInfo(pdf).Length + " bytes)" : answered ? " (dialog answered, no file)" : " (no save dialog seen)"));
            }
            Application.Exit();   // the program restores the user's print settings on exit
            return fails;
        }
    }
}
