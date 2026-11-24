using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;
using System;

namespace Stores
{
    /// <summary>The program opens once per computer session (owner's request): a second start shows the open window —
    /// also when it sits next to the clock — and ends.</summary>
    static class SingleInstance
    {
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern int RegisterWindowMessage(string s);
        [DllImport("user32.dll")] static extern bool PostMessage(IntPtr h, int msg, IntPtr w, IntPtr l);
        [DllImport("user32.dll")] static extern bool AllowSetForegroundWindow(int pid);
        [DllImport("user32.dll")] static extern bool SetForegroundWindow(IntPtr h);

        static readonly int ShowMessage = RegisterWindowMessage("EngStores.InventoryManagement.Show");
        static Mutex mutex;
        static EventWaitHandle mark;

        static string MarkName(int pid) { return @"Local\EngStores.InventoryManagement.Pid" + pid; }

        /// <summary>First line of the program: other copies know that this process takes part in the lock (so it is not an
        /// older version, also when it is only starting or showing «already open»).</summary>
        public static void Mark()
        {
            try { using (Process me = Process.GetCurrentProcess()) mark = new EventWaitHandle(false, EventResetMode.ManualReset, MarkName(me.Id)); }
            catch { }
        }

        /// <summary>True for the first instance. For a second one: brings the first to the front and returns false.</summary>
        public static bool Acquire() { return Acquire("EngStores.InventoryManagement"); }

        public static bool Acquire(string name)
        {
            bool first;
            mutex = new Mutex(true, @"Local\" + name, out first);
            if (!first)
            {
                try { first = mutex.WaitOne(1500); }              // the other one is just closing (update, language, log-out)
                catch (AbandonedMutexException) { first = true; }
            }
            if (first)
            {
                if (!OlderVersionRuns()) return true;
                // a version before 1.2.1 does not answer: the user decides (it may also hang without a window)
                if (UI.Confirm("توجد نسخة أقدم من البرنامج مفتوحة في هذا الحاسوب (ربما بجانب الساعة).\nأغلقها أولا: زر الفأرة الأيمن على أيقونتها ← خروج.\n\nهل تريد فتح هذه النسخة رغم ذلك؟")) return true;
                Release();
                return false;
            }
            mutex.Close(); mutex = null;
            AllowSetForegroundWindow(-1);
            using (EventWaitHandle shown = new EventWaitHandle(false, EventResetMode.AutoReset, @"Local\" + name + ".Shown"))
            {
                // asked again for some seconds: the first one may still be starting (connecting to the server)
                bool ok = false;
                for (int i = 0; i < 10 && !ok; i++)
                {
                    PostMessage((IntPtr)0xFFFF /*all top windows*/, ShowMessage, IntPtr.Zero, IntPtr.Zero);
                    ok = shown.WaitOne(700);
                }
                if (!ok)
                    UI.Info("البرنامج مفتوح مسبقا في هذا الحاسوب.\nإذا لم تظهر نافذته فابحث عن أيقونته بجانب الساعة وانقر عليها مرتين.");
            }
            return false;
        }

        public static void Release()
        {
            if (mutex == null) return;
            try { mutex.ReleaseMutex(); } catch { }
            mutex.Close(); mutex = null;
        }

        /// <summary>A version before 1.2.1 does not hold the lock: look for its process (same Windows session).</summary>
        static bool OlderVersionRuns()
        {
            // a copy that only just started this one (update, replaced download) is ending: give it a moment
            for (int i = 0; i < 12; i++)
            {
                if (!Other()) return false;
                Thread.Sleep(400);
            }
            return Other();
        }

        static bool Other()
        {
            try
            {
                using (Process me = Process.GetCurrentProcess())
                {
                    if (!me.ProcessName.StartsWith("Inventory Management", StringComparison.OrdinalIgnoreCase)) return false;   // test programs
                    bool found = false;
                    foreach (Process p in Process.GetProcessesByName(me.ProcessName))
                        using (p) { if (p.Id != me.Id && p.SessionId == me.SessionId && !Marked(p.Id) && !p.HasExited) found = true; }
                    return found;
                }
            }
            catch { return false; }
        }

        static bool Marked(int pid)
        {
            try { using (EventWaitHandle.OpenExisting(MarkName(pid))) return true; }
            catch (UnauthorizedAccessException) { return true; }     // it exists, but runs as administrator
            catch { return false; }
        }

        /// <summary>Call from a form's WndProc: true when a second start asks to be shown.</summary>
        public static bool IsShowMessage(ref Message m) { return m.Msg == ShowMessage; }

        public static void Shown(Form f) { Shown(f, "EngStores.InventoryManagement"); }

        public static void Shown(Form f, string name)
        {
            if (f.WindowState == FormWindowState.Minimized) f.WindowState = FormWindowState.Normal;
            f.Activate(); SetForegroundWindow(f.Handle);
            try { using (EventWaitHandle e = EventWaitHandle.OpenExisting(@"Local\" + name + ".Shown")) e.Set(); } catch { }
        }
    }
}
