using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading;
using System.Windows.Forms;
using Microsoft.Win32;

namespace Stores
{
    /// <summary>The program's part of installing: remembers its folder for the setup, and when a freshly downloaded copy is
    /// started while an older copy is installed, offers once to replace that one (same logic as the setup).</summary>
    static class InstallOffer
    {
        /// <summary>A normal start of the installed program: not a test program, not a data file given on the command line,
        /// not a copy opened inside a zip (%TEMP%), not a source checkout.</summary>
        static bool NormalStart(InstallEnv env, string[] args, string exe)
        {
            foreach (string a in args) if (a.StartsWith("/data=", StringComparison.OrdinalIgnoreCase)) return false;
            if (!string.Equals(Path.GetFileName(exe), env.ExeName, StringComparison.OrdinalIgnoreCase)) return false;
            string dir = Path.GetDirectoryName(exe);
            if (env.OnlyUnder != null && !InstallEnv.IsUnder(dir, env.OnlyUnder)) return false;
            if (!env.AllowTemp && InstallFind.IsTemp(dir)) return false;
            return !InstallFind.IsCheckout(dir);
        }

        /// <summary>After the data is connected: this is the installed program from now on.</summary>
        public static void Register(string[] args)
        {
            if (NormalStart(InstallEnv.Current, args, Application.ExecutablePath))
                InstallFind.Register(InstallEnv.Current, Settings.AppDir, Program.Version);
        }

        /// <summary>The older copy to offer, or null. Only from a never configured folder, and not after «لا» for this folder and version.</summary>
        public static InstallCopy Candidate(InstallEnv env, string[] args, string exe, string version)
        {
            string dir = Path.GetDirectoryName(exe);
            if (!NormalStart(env, args, exe) || !InstallFind.NeverConfigured(dir)) return null;
            if (InstallFind.RegValue(DeclinedKey(env), Key(dir, version)).Length > 0) return null;
            foreach (InstallCopy c in InstallFind.All(env))
                if (!InstallEnv.SamePath(c.Dir, dir) && !InstallFind.IsCheckout(c.Dir))
                    return InstallFind.Compare(c.Version, version) < 0 ? c : null;   // the first one found is «the» installation
            return null;
        }

        static string DeclinedKey(InstallEnv env) { return env.RegKey + @"\Declined"; }
        static string Key(string dir, string version) { return (InstallEnv.Norm(dir) ?? "").ToLowerInvariant() + "|" + version; }

        public static void Decline(InstallEnv env, string dir, string version)
        {
            try { using (RegistryKey k = Registry.CurrentUser.CreateSubKey(DeclinedKey(env))) k.SetValue(Key(dir, version), DateTime.Today.ToString("yyyy-MM-dd")); }
            catch { }
        }

        /// <summary>Program start (before the language question). true = the installed copy was replaced and started: exit.</summary>
        public static bool AtStart(string[] args)
        {
            InstallEnv env = InstallEnv.Current;
            InstallCopy old;
            try { old = Candidate(env, args, Application.ExecutablePath, Program.Version); } catch { return false; }
            if (old == null) return false;
            Lang.English = OldLanguage(old.Dir) == "en";   // the language of the installed copy (this one has none yet)
            if (!UI.Confirm("لديك نسخة أقدم " + old.Version + " في المجلد:\n" + old.Dir + "\n\nهل تريد استبدالها بهذه النسخة " + Program.Version + "؟\nالبيانات والإعدادات والنسخ الاحتياطية لا تتغير."))
            {
                Decline(env, Settings.AppDir, Program.Version);
                return false;
            }
            if (!CloseOld(env, old.Dir)) return false;
            InstallReplace r = new InstallReplace(env);
            UI.WaitCursor(true);
            ReplaceResult res;
            try { res = r.Run(Settings.AppDir, InstallReplace.ProgramFiles(env, Settings.AppDir), old.Dir); }
            finally { UI.WaitCursor(false); }
            if (res == ReplaceResult.Ok)
            {
                InstallFind.Register(env, old.Dir, Program.Version);
                try { Process.Start(new ProcessStartInfo(env.ExePath(old.Dir)) { WorkingDirectory = old.Dir, UseShellExecute = false }); }
                catch (Exception ex) { UI.Warn("تم استبدال النسخة القديمة، لكن تعذر تشغيلها:\n" + ex.Message); }
                return true;
            }
            string why = res == ReplaceResult.NotWritable ? "لا توجد صلاحية كتابة في مجلد البرنامج القديم."
                       : res == ReplaceResult.InUse ? "ملف البرنامج القديم مفتوح (حساب آخر أو حاسوب آخر). أغلقه ثم أعد المحاولة." : r.Error;
            UI.Warn("لم تُستبدل النسخة القديمة" + (res == ReplaceResult.RollbackIncomplete ? "، وبعض ملفاتها لم تُسترجع. نسخة منها في:\n" + r.BackupDir : ".") + "\n\n" + why);
            return false;
        }

        static string OldLanguage(string dir)
        {
            try
            {
                foreach (string l in File.ReadAllLines(Path.Combine(dir, "settings.ini"), Encoding.UTF8))
                    if (l.StartsWith("Language=", StringComparison.OrdinalIgnoreCase)) return l.Substring(9).Trim();
            }
            catch { }
            return "ar";
        }

        /// <summary>The old program must not run while its files are replaced: it is asked to exit, or the user closes it.</summary>
        static bool CloseOld(InstallEnv env, string dir)
        {
            while (true)
            {
                List<int> run = InstallFind.RunningIn(env, dir);
                if (run.Count == 0) return true;
                bool old = false;
                foreach (int pid in run)
                    if (ProgramExit.Ask(pid, 60000, delegate { Application.DoEvents(); return true; }) == ExitAnswer.NoListener) old = true;
                if (InstallFind.RunningIn(env, dir).Count == 0) return true;
                if (!UI.Confirm("البرنامج القديم مفتوح (قد يكون بجانب الساعة).\nاحفظ عملك فيه وأغلقه: زر الفأرة الأيمن على أيقونته ← خروج.\n\nهل أغلقته وتريد المتابعة؟")) return false;
                Thread.Sleep(500);
                run = InstallFind.RunningIn(env, dir);
                if (run.Count > 0 && old && UI.Confirm("ما زال البرنامج القديم مفتوحا.\nهل حفظت عملك وتريد إنهاءه الآن؟\nما لم يُحفظ يضيع."))
                    foreach (int pid in run) ProgramExit.Kill(pid);
            }
        }
    }
}
