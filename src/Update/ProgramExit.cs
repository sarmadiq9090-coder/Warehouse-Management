using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading;

namespace Stores
{
    enum ExitAnswer
    {
        /// <summary>The program has ended.</summary>
        Closed,
        /// <summary>The program was asked, but the user cancelled (unsaved document) or has a window open in it.</summary>
        Refused,
        /// <summary>Nobody answered in the given time (the question «save?» is still open).</summary>
        Waiting,
        /// <summary>An old program (up to 1.2.0) or one running as administrator: the user must close it.</summary>
        NoListener
    }

    /// <summary>Asking a running program to exit properly, from the setup (the program's side is ExitListener). Two events per
    /// process, only inside the user's own Windows session. Compiled into the program and into the setup.</summary>
    static class ProgramExit
    {
        public static string ExitEvent(int pid) { return @"Local\EngStores.Exit." + pid; }
        public static string RefusedEvent(int pid) { return @"Local\EngStores.ExitRefused." + pid; }

        [DllImport("user32.dll")] static extern bool AllowSetForegroundWindow(int pid);

        static EventWaitHandle Open(string name)
        {
            try { return EventWaitHandle.OpenExisting(name); } catch { return null; }
        }

        public static bool CanAsk(int pid)
        {
            using (EventWaitHandle e = Open(ExitEvent(pid))) return e != null;
        }

        public static bool IsRunning(int pid)
        {
            try { using (Process p = Process.GetProcessById(pid)) return !p.HasExited; }
            catch (ArgumentException) { return false; }
            catch (InvalidOperationException) { return false; }
            catch { return true; }   // running, but not ours to look at (administrator)
        }

        /// <summary>Asks once and waits for the answer. tick (may be null) is called about ten times a second; false = stop waiting.</summary>
        public static ExitAnswer Ask(int pid, int timeoutMs, Func<bool> tick)
        {
            if (!IsRunning(pid)) return ExitAnswer.Closed;
            using (EventWaitHandle ask = Open(ExitEvent(pid)))
            using (EventWaitHandle refused = Open(RefusedEvent(pid)))
            {
                if (ask == null || refused == null) return IsRunning(pid) ? ExitAnswer.NoListener : ExitAnswer.Closed;
                refused.WaitOne(0);                                // an old answer
                try { AllowSetForegroundWindow(pid); } catch { }   // its «save?» question may come to the front
                ask.Set();
                Stopwatch w = Stopwatch.StartNew();
                while (w.ElapsedMilliseconds < timeoutMs)
                {
                    if (!IsRunning(pid)) return ExitAnswer.Closed;
                    if (refused.WaitOne(100)) return ExitAnswer.Refused;
                    if (tick != null && !tick()) return ExitAnswer.Waiting;
                }
                return IsRunning(pid) ? ExitAnswer.Waiting : ExitAnswer.Closed;
            }
        }

        /// <summary>Ends a program that cannot be asked. Only after the user confirmed that the work is saved.</summary>
        public static bool Kill(int pid)
        {
            try
            {
                using (Process p = Process.GetProcessById(pid)) { p.Kill(); return p.WaitForExit(10000); }
            }
            catch (ArgumentException) { return true; }
            catch { return !IsRunning(pid); }
        }
    }
}
