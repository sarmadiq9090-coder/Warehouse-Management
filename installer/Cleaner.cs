using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Text;
using System.Threading;
using System.Windows.Forms;
using Microsoft.Win32;

namespace Stores.Installer
{
    /// <summary>«Inventory Management Cleaner.exe»: finds every copy of the program on this computer's disks and removes the ones
    /// the user leaves ticked, so it can be installed afresh. Program files, shortcuts and registry entries only: data files,
    /// backups and SQL Server are never touched; settings.ini and errors.log are kept in Documents. Nothing else is removed.</summary>
    static class Clean
    {
        public const string KeepName = "إعدادات قديمة - مخازن";

        /// <summary>Copies on the fixed and removable disks (and the ones InstallFind knows). stop: the user closed the window.</summary>
        public static List<InstallCopy> Scan(InstallEnv env, IEnumerable<string> roots, Action<string> progress, Func<bool> stop)
        {
            List<InstallCopy> res = InstallFind.All(env);
            res.RemoveAll(delegate(InstallCopy c) { return InCheckout(c.Dir); });
            string windows = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
            Stack<string> dirs = new Stack<string>(roots);
            int n = 0;
            while (dirs.Count > 0 && !stop())
            {
                string d = dirs.Pop();
                if (++n % 200 == 0 && progress != null) progress(d);
                try
                {
                    if (File.Exists(env.ExePath(d)) && !InCheckout(d)) Add(res, env, d);
                    foreach (string s in Directory.GetDirectories(d))
                    {
                        string name = Path.GetFileName(s);
                        if (name.StartsWith("$") || name == "WinSxS" || name == "System Volume Information" || InstallEnv.IsUnder(s, windows)) continue;
                        if (InstallFind.IsTemp(s) && !env.AllowTemp) continue;
                        if (name == InstallEnv.UpdateBackupDir || name == ".git" || name == "node_modules") continue;
                        if ((File.GetAttributes(s) & FileAttributes.ReparsePoint) != 0) continue;
                        dirs.Push(s);
                    }
                }
                catch { }   // a folder this user may not read
            }
            return res;
        }

        static void Add(List<InstallCopy> res, InstallEnv env, string dir)
        {
            foreach (InstallCopy c in res) if (InstallEnv.SamePath(c.Dir, dir)) return;
            InstallCopy n = InstallFind.At(env, dir);
            if (n == null || n.Version.Length == 0) return;      // another program with the same name
            if (File.Exists(Path.Combine(dir, "Inventory Management Setup.exe"))) return;      // the offline folder (a copy to install from)
            n.Source = "disk"; res.Add(n);
        }

        /// <summary>A developer's source folder is not an installation.</summary>
        static bool InCheckout(string dir)
        {
            for (string d = dir; !string.IsNullOrEmpty(d); d = Path.GetDirectoryName(d)) if (InstallFind.IsCheckout(d)) return true;
            return false;
        }

        /// <summary>The data file this copy works with ("" = none or a server).</summary>
        public static string DataPath(string dir)
        {
            try
            {
                foreach (string l in File.ReadAllLines(Path.Combine(dir, "settings.ini"), Encoding.UTF8))
                    if (l.StartsWith("DataPath=", StringComparison.OrdinalIgnoreCase)) return l.Substring(9).Trim();
            }
            catch { }
            return "";
        }

        /// <summary>null = removed, otherwise why not.</summary>
        public static string Remove(InstallEnv env, string dir, string keepRoot)
        {
            string data = DataPath(dir);
            SetupEngine en = new SetupEngine(env, "", null, "");
            int code = en.Uninstall(dir);
            if (code == SetupCode.ProgramOpen) return "ما زال مفتوحا";
            if (code == SetupCode.NotWritable) return "يحتاج صلاحيات المدير";
            if (code != SetupCode.Ok) return en.Error.Length > 0 ? en.Error : "غير موجود";
            // the new installation finds the same data again
            if (data.Length > 0 && File.Exists(data))
                try { using (RegistryKey k = Registry.CurrentUser.CreateSubKey(env.RegKey)) k.SetValue("LastData", data); } catch { }
            string keep = Path.Combine(keepRoot, DateTime.Now.ToString("yyyy-MM-dd HHmmss"));
            foreach (string f in new[] { "settings.ini", "errors.log" })
            {
                string src = Path.Combine(dir, f);
                if (!File.Exists(src)) continue;
                try
                {
                    string to = keep; for (int i = 2; File.Exists(Path.Combine(to, f)); i++) to = keep + " (" + i + ")";
                    Directory.CreateDirectory(to); File.Move(src, Path.Combine(to, f)); File.WriteAllText(Path.Combine(to, "المكان.txt"), dir, Encoding.UTF8);
                }
                catch { }
            }
            string comp = Path.Combine(dir, InstallEnv.ComponentsDir);
            foreach (string f in new[] { InstallEnv.ComponentsMain, InstallEnv.ComponentsMain + ".part" }) try { File.Delete(Path.Combine(comp, f)); } catch { }
            foreach (string d in new[] { comp, dir }) try { if (Directory.Exists(d) && Directory.GetFileSystemEntries(d).Length == 0) Directory.Delete(d, false); } catch { }
            return null;
        }

        /// <summary>An old program: asked to close its window first (its data file is closed properly), then ended.</summary>
        public static void End(int pid)
        {
            try { using (Process p = Process.GetProcessById(pid)) if (p.CloseMainWindow() && p.WaitForExit(5000)) return; } catch { }
            ProgramExit.Kill(pid);
        }

        /// <summary>What is on this computer, for support (a text file on the Desktop; nothing is sent).</summary>
        public static string Report(InstallEnv env, IList<InstallCopy> copies)
        {
            StringBuilder s = new StringBuilder();
            s.AppendLine("تقرير نظام إدارة المخازن • " + DateTime.Now.ToString("yyyy-MM-dd HH:mm"));
            s.AppendLine(Environment.OSVersion.VersionString + (Environment.Is64BitOperatingSystem ? " 64" : " 32") + " • .NET " + Environment.Version);
            foreach (string svc in new[] { "MSSQL$SQLEXPRESS", "SQLBrowser" })
                using (RegistryKey k = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Services\" + svc)) s.AppendLine(svc + ": " + (k == null ? "غير منصّب" : "منصّب"));
            foreach (KeyValuePair<int, string> p in InstallFind.Running(env)) s.AppendLine("يعمل الآن: " + p.Value);
            foreach (InstallCopy c in copies)
            {
                s.AppendLine().AppendLine(c.Version + "  " + c.Dir + "  (" + c.Source + ")");
                string d = DataPath(c.Dir); if (d.Length > 0) s.AppendLine("  بيانات: " + d + (File.Exists(d) ? "" : "  (غير موجود)"));
                try
                {
                    string log = Path.Combine(c.Dir, "errors.log");
                    if (File.Exists(log)) { string[] l = File.ReadAllLines(log, Encoding.UTF8); for (int i = Math.Max(0, l.Length - 60); i < l.Length; i++) s.AppendLine("  | " + l[i]); }
                }
                catch { }
            }
            try
            {
                using (EventLog e = new EventLog("Application"))
                {
                    int n = 0;
                    for (int i = e.Entries.Count - 1; i >= 0 && i > e.Entries.Count - 3000 && n < 5; i--)
                    {
                        EventLogEntry x = e.Entries[i];
                        if ((x.Source == "Application Error" || x.Source == ".NET Runtime") && x.Message.Contains(env.ExeName)) { n++; s.AppendLine().AppendLine(x.TimeGenerated + " " + x.Source).AppendLine(x.Message); }
                    }
                }
            }
            catch { }
            return s.ToString();
        }
    }

    class CleanerForm : Form
    {
        readonly InstallEnv env = InstallEnv.Current;
        readonly CheckedListBox list = new CheckedListBox();
        readonly Label status = new Label();
        readonly Button go, close;
        List<InstallCopy> copies = new List<InstallCopy>();
        bool closing;

        public CleanerForm()
        {
            Text = "مسح نسخ نظام إدارة المخازن"; RightToLeft = RightToLeft.Yes; RightToLeftLayout = true;
            Font = new Font("Segoe UI", 10.5f); BackColor = Color.White; StartPosition = FormStartPosition.CenterScreen;
            ClientSize = new Size(760, 470); MinimumSize = new Size(600, 400);
            try { Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); } catch { }
            Label top = new Label(); top.Dock = DockStyle.Top; top.Height = 78; top.Padding = new Padding(16, 12, 16, 4); top.Font = new Font(Font.FontFamily, 11f);
            top.Text = "تُمسح ملفات البرنامج واختصاراته فقط.\nملفات البيانات والنسخ الاحتياطية وSQL Server لا تُمسح.";
            list.Dock = DockStyle.Fill; list.CheckOnClick = true; list.IntegralHeight = false; list.BorderStyle = BorderStyle.FixedSingle; list.RightToLeft = RightToLeft.No; list.HorizontalScrollbar = true;
            Panel mid = new Panel(); mid.Dock = DockStyle.Fill; mid.Padding = new Padding(16, 4, 16, 4); mid.Controls.Add(list);
            Panel bottom = new Panel(); bottom.Dock = DockStyle.Bottom; bottom.Height = 92; bottom.BackColor = Color.FromArgb(246, 247, 250);
            status.Dock = DockStyle.Top; status.Height = 36; status.Padding = new Padding(16, 8, 16, 0); status.AutoEllipsis = true; status.ForeColor = Color.FromArgb(96, 103, 115);
            go = new Button(); go.Text = "مسح المحدد"; go.SetBounds(16, 42, 180, 40); go.BackColor = Color.FromArgb(190, 30, 40); go.ForeColor = Color.White; go.FlatStyle = FlatStyle.Flat; go.Font = new Font(Font, FontStyle.Bold); go.Enabled = false;
            close = new Button(); close.Text = "إغلاق"; close.SetBounds(206, 42, 110, 40); close.FlatStyle = FlatStyle.Flat;
            bottom.Controls.AddRange(new Control[] { status, go, close });
            Controls.Add(mid); Controls.Add(bottom); Controls.Add(top);
            go.Click += delegate { DoClean(); };
            close.Click += delegate { Close(); };
            FormClosing += delegate { closing = true; };
            Shown += delegate { Search(); };
        }

        void Search()
        {
            go.Enabled = false; list.Items.Clear(); status.Text = "جاري البحث عن النسخ…";
            List<string> roots = new List<string>();
            foreach (DriveInfo d in DriveInfo.GetDrives()) try { if (d.IsReady && (d.DriveType == DriveType.Fixed || d.DriveType == DriveType.Removable)) roots.Add(d.Name); } catch { }
            BackgroundWorker w = new BackgroundWorker();
            w.DoWork += delegate(object s, DoWorkEventArgs e)
            {
                e.Result = Clean.Scan(env, roots, delegate(string d) { try { BeginInvoke((Action)delegate { if (!closing) status.Text = "جاري البحث: " + (d.Length > 70 ? d.Substring(0, 70) + "…" : d); }); } catch { } }, delegate { return closing; });
            };
            w.RunWorkerCompleted += delegate(object s, RunWorkerCompletedEventArgs e)
            {
                if (closing) return;
                copies = e.Error != null ? new List<InstallCopy>() : (List<InstallCopy>)e.Result;
                foreach (InstallCopy c in copies) list.Items.Add((c.Version.Length > 0 ? c.Version : "?") + "    " + c.Dir, true);
                status.Text = copies.Count == 0 ? "لا توجد نسخ على هذا الحاسوب." : "وُجدت " + copies.Count + " نسخة. أزل العلامة عن أي نسخة تريد إبقاءها.";
                go.Enabled = copies.Count > 0;
            };
            w.RunWorkerAsync();
        }

        void DoClean()
        {
            List<InstallCopy> sel = new List<InstallCopy>();
            foreach (int i in list.CheckedIndices) sel.Add(copies[i]);
            if (sel.Count == 0) return;
            MessageBoxOptions rtl = MessageBoxOptions.RtlReading | MessageBoxOptions.RightAlign;
            if (MessageBox.Show(this, "ستُمسح " + sel.Count + " نسخة من البرنامج، ويُغلق البرنامج إذا كان مفتوحا.\nاحفظ عملك فيه أولا.\n\nبياناتك لا تُمسح. هل تتابع؟", Text, MessageBoxButtons.YesNo, MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2, rtl) != DialogResult.Yes) return;
            go.Enabled = close.Enabled = false; Application.DoEvents();
            try { File.WriteAllText(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), "تقرير المخازن.txt"), Clean.Report(env, copies), Encoding.UTF8); } catch { }
            string keep = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), Clean.KeepName);
            StringBuilder bad = new StringBuilder(); int ok = 0;
            foreach (InstallCopy c in sel)
            {
                status.Text = "مسح: " + c.Dir; Application.DoEvents();
                foreach (int pid in InstallFind.RunningIn(env, c.Dir)) if (ProgramExit.Ask(pid, 8000, delegate { Application.DoEvents(); return true; }) != ExitAnswer.Closed) Clean.End(pid);
                string err = Clean.Remove(env, c.Dir, keep);
                if (err == null) ok++; else bad.AppendLine(c.Dir + "  •  " + err);
            }
            close.Enabled = true;
            MessageBox.Show(this, "✓ مُسحت " + ok + " نسخة.\nبياناتك كما هي، والإعدادات القديمة محفوظة في: المستندات ← " + Clean.KeepName + (bad.Length > 0 ? "\n\nلم تُمسح:\n" + bad : "") + "\n\nيمكنك الآن التنصيب من جديد.", Text, MessageBoxButtons.OK, bad.Length > 0 ? MessageBoxIcon.Warning : MessageBoxIcon.Information, MessageBoxDefaultButton.Button1, rtl);
            Search();
        }
    }

    static class CleanerMain
    {
        [STAThread]
        static void Main()
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.Run(new CleanerForm());
        }
    }
}
