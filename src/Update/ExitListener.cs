using System;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;

namespace Stores
{
    /// <summary>The program's side of ProgramExit: the setup of a new version asks the running program (also when it sits next
    /// to the clock) to exit the normal way. Unsaved documents are asked about as usual; when the user cancels, or a window is
    /// open over the main one, the setup is told and nothing is closed.</summary>
    static class ExitListener
    {
        static EventWaitHandle ask, refused;
        static Control ui;

        [DllImport("user32.dll")] static extern bool IsWindowEnabled(IntPtr h);

        /// <summary>Call once on the main thread at program start.</summary>
        public static void Start()
        {
            if (ask != null) return;
            try
            {
                int pid; using (System.Diagnostics.Process p = System.Diagnostics.Process.GetCurrentProcess()) pid = p.Id;
                ui = new Control(); IntPtr h = ui.Handle;   // a window of the main thread to call back on
                refused = new EventWaitHandle(false, EventResetMode.AutoReset, ProgramExit.RefusedEvent(pid));
                ask = new EventWaitHandle(false, EventResetMode.AutoReset, ProgramExit.ExitEvent(pid));
                ThreadPool.RegisterWaitForSingleObject(ask, delegate { try { ui.BeginInvoke(new Action(Requested)); } catch { } }, null, -1, false);
            }
            catch { }   // the program works without it
        }

        static void Requested()
        {
            MainForm main = null; Form other = null; int open = 0;
            foreach (Form f in Application.OpenForms)
            {
                if (f is MainForm) main = (MainForm)f;
                else if (f.Visible) { other = f; open++; }
            }
            if (main != null)
            {
                main.FromTray();
                if (!IsWindowEnabled(main.Handle)) { refused.Set(); return; }   // a dialog or a question is open
                main.ExitProgram();
                if (!main.IsDisposed) refused.Set();                            // the user kept the document
                return;
            }
            if (open == 1 && (other is LoginForm || other is LanguageForm) && IsWindowEnabled(other.Handle))
            {
                Db.Close();
                Environment.Exit(0);   // nothing is open yet
            }
            refused.Set();
        }
    }
}
