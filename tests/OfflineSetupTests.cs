using System;
using System.CodeDom.Compiler;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Windows.Forms;

namespace Stores
{
    /// <summary>
    /// Installing the server without internet: finding, checking and downloading the SQL Server package (local test server,
    /// no internet), the package functions of setup-server.ps1 (run alone in a temp folder: nothing is installed, nothing is
    /// elevated), scripts\make-offline.ps1, and screenshots of the two windows.
    /// Usage: OfflineSetupTests.exe <outdir> [real SQLEXPR_x64_ENU.exe]
    ///        OfflineSetupTests.exe shots <outdir> [--en] [dark]      (screenshots only)
    /// With the real package: it is extracted (2 s, 340 MB, deleted afterwards) and make-offline.ps1 is run with it.
    /// </summary>
    static class OfflineSetupTestsMain
    {
        static int fails = 0;
        static void Check(bool ok, string what) { Console.WriteLine((ok ? "PASS  " : "FAIL  ") + what); if (!ok) fails++; }

        static string Hash(byte[] b) { using (SHA256 s = SHA256.Create()) return BitConverter.ToString(s.ComputeHash(b)).Replace("-", ""); }
        static string HashFile(string f) { return Hash(File.ReadAllBytes(f)); }

        // ------------------------------------------------------------ the test server
        class Server : IDisposable
        {
            public byte[] Data;
            public bool Ranges = true;         // answers Range with 206
            public int DropAfter = int.MaxValue; // closes the connection after so many body bytes…
            public int Drops = int.MaxValue;     // …the first so many times
            public int StallAfter = -1;        // stops sending (connection stays open)
            public int Delay = 0;              // ms per 16 KB
            public string Status;              // e.g. "404 Not Found"
            public byte[] StatusBody = new byte[0];
            public int StatusTimes = int.MaxValue;
            public List<string> Requests = new List<string>();
            TcpListener l; volatile bool stop;

            public Server(byte[] data)
            {
                Data = data;
                l = new TcpListener(IPAddress.Loopback, 0); l.Start();
                Thread t = new Thread(delegate() { while (!stop) { try { TcpClient c = l.AcceptTcpClient(); ThreadPool.QueueUserWorkItem(delegate { Serve(c); }); } catch { return; } } });
                t.IsBackground = true; t.Start();
            }

            public string Url { get { return "http://127.0.0.1:" + ((IPEndPoint)l.LocalEndpoint).Port + "/SQLEXPR_x64_ENU.exe"; } }
            public void Dispose() { stop = true; l.Stop(); }

            void Serve(TcpClient c)
            {
                try
                {
                    using (c)
                    using (NetworkStream s = c.GetStream())
                    {
                        StringBuilder sb = new StringBuilder(); int b;
                        while ((b = s.ReadByte()) >= 0) { sb.Append((char)b); if (sb.ToString().EndsWith("\r\n\r\n")) break; }
                        string req = sb.ToString(); if (req.Length == 0) return;
                        int n; lock (Requests) { Requests.Add(req); n = Requests.Count; }
                        if (Status != null && n <= StatusTimes)
                        {
                            Send(s, "HTTP/1.1 " + Status + "\r\nContent-Length: " + StatusBody.Length + "\r\nConnection: close\r\n\r\n"); s.Write(StatusBody, 0, StatusBody.Length); s.Flush(); return;
                        }
                        Match m = Regex.Match(req, @"Range: bytes=(\d+)-", RegexOptions.IgnoreCase);
                        int from = 0;
                        if (m.Success && Ranges)
                        {
                            from = int.Parse(m.Groups[1].Value);
                            if (from >= Data.Length) { Send(s, "HTTP/1.1 416 Range Not Satisfiable\r\nContent-Length: 0\r\nConnection: close\r\n\r\n"); return; }
                            Send(s, "HTTP/1.1 206 Partial Content\r\nContent-Range: bytes " + from + "-" + (Data.Length - 1) + "/" + Data.Length + "\r\nContent-Length: " + (Data.Length - from) + "\r\nConnection: close\r\n\r\n");
                        }
                        else Send(s, "HTTP/1.1 200 OK\r\nContent-Length: " + Data.Length + "\r\nConnection: close\r\n\r\n");
                        int sent = 0, limit = n <= Drops ? DropAfter : int.MaxValue;
                        while (from + sent < Data.Length)
                        {
                            int k = Math.Min(16384, Data.Length - from - sent);
                            if (limit - sent < k) k = limit - sent;
                            if (k <= 0) { c.Client.LingerState = new LingerOption(true, 0); return; }   // connection reset, as «forcibly closed»
                            if (StallAfter >= 0 && sent >= StallAfter) { for (int i = 0; i < 600 && !stop; i++) Thread.Sleep(50); return; }
                            s.Write(Data, from + sent, k); sent += k;
                            if (limit - sent <= 0) { s.Flush(); Thread.Sleep(60); }   // let it arrive before the reset
                            if (Delay > 0) Thread.Sleep(Delay);
                        }
                    }
                }
                catch { }
            }
            static void Send(Stream s, string t) { byte[] b = Encoding.ASCII.GetBytes(t); s.Write(b, 0, b.Length); }
        }

        static SqlPackage Pkg(byte[] data, string url)
        {
            SqlPackage p = new SqlPackage();
            p.Url = url; p.Size = data.Length; p.Sha256 = Hash(data); p.RetryDelay = 10; p.MaxRetryDelay = 60; p.StallTimeout = 1500;
            return p;
        }

        static string NewDir(string root, string name) { string d = Path.Combine(root, name); if (Directory.Exists(d)) Directory.Delete(d, true); Directory.CreateDirectory(d); return d; }
        static string Put(string path, byte[] b) { Directory.CreateDirectory(Path.GetDirectoryName(path)); File.WriteAllBytes(path, b); return path; }

        static bool Arabic(string s) { return s != null && LangKey.HasArabic(s); }

        // ------------------------------------------------------------ the program's part
        static void FindTests(string root, byte[] good)
        {
            Console.WriteLine("\n--- find");
            SqlPackage p = Pkg(good, "http://127.0.0.1:9/");
            string t = NewDir(root, "بحث"), app = NewDir(t, "برنامج المخازن"), user = NewDir(t, "user"), d1 = NewDir(t, "d1"), d2 = NewDir(t, "فلاش");
            List<KeyValuePair<string, bool>> drives = new List<KeyValuePair<string, bool>> {
                new KeyValuePair<string, bool>(d1, false), new KeyValuePair<string, bool>(Path.Combine(t, "no such drive"), true), new KeyValuePair<string, bool>(d2, true) };
            string F = SqlPackage.FileName, M = SqlPackage.FolderName;
            string[] places = {
                Path.Combine(app, M, F), Path.Combine(app, F), Path.Combine(user, F), Path.Combine(d1, M, F), Path.Combine(d1, F),
                Path.Combine(d2, M, F), Path.Combine(d2, F), Path.Combine(d2, "نسخة كاملة بدون إنترنت", M, F) };
            Check(p.Find(app, user, drives, 4000).Count == 0, "nothing found -> empty list (the dialog is shown)");
            string[] names = { "«مكونات» next to the program", "next to the .exe", "an earlier download", "X:\\مكونات", "X:\\", "flash: X:\\مكونات", "flash: X:\\", "flash: X:\\copied folder\\مكونات" };
            for (int i = 0; i < places.Length; i++)
            {
                Put(places[i], good);
                List<string> r = p.Find(app, user, drives, 4000);
                Check(r.Count == 1 && r[0] == places[i], "found: " + names[i]);
                File.Delete(places[i]);
            }
            foreach (string x in places) Put(x, good);
            List<string> all = p.Find(app, user, drives, 4000);
            Check(all.SequenceEqual(places), "precedence: program folder, earlier download, drives (" + all.Count + " places)");
            File.WriteAllBytes(places[0], good.Take(100).ToArray());
            all = p.Find(app, user, drives, 4000);
            Check(all[0] == places[1] && all[all.Count - 1] == places[0], "an incomplete copy comes after every complete one");
            Put(Path.Combine(d1, "x", M, F), good);
            Check(!p.Find(app, user, drives, 4000).Contains(Path.Combine(d1, "x", M, F)), "fixed drives are not searched deeper");
            Stopwatch sw = Stopwatch.StartNew();
            p.Find(app, user, new List<KeyValuePair<string, bool>> { new KeyValuePair<string, bool>(@"\\10.255.255.1\dead", true) }, 1000);
            Check(sw.ElapsedMilliseconds < 2500, "a place that does not answer does not hang the search (" + sw.ElapsedMilliseconds + " ms)");
            sw.Restart(); List<string> here = new SqlPackage().Find();
            Check(sw.ElapsedMilliseconds < 5000, "real search of this computer is quick (" + sw.ElapsedMilliseconds + " ms, " + here.Count + " found)");
            Check(SqlPackage.DownloadPath(app, user) == places[0], "download goes to «مكونات» next to the program");
            string file = Put(Path.Combine(t, "file"), good);
            Check(SqlPackage.DownloadPath(file, user) == places[2], "…or to the user's folder when that cannot be written");
            Check(SqlPackage.DownloadPath(@"\\server\share\program", user) == places[2], "…or when the program runs from the network");
            Check(!SqlPackage.IsLocal(@"\\server\share\x.exe") && SqlPackage.IsLocal(file), "network file told apart from a local file");
        }

        static void CheckTests(string root, byte[] good)
        {
            Console.WriteLine("\n--- check");
            SqlPackage p = Pkg(good, "");
            string t = NewDir(root, "check");
            string ok = Put(Path.Combine(t, "ok.exe"), good), cut = Put(Path.Combine(t, "cut.exe"), good.Take(good.Length / 2).ToArray()), longer = Put(Path.Combine(t, "long.exe"), good.Concat(new byte[] { 1 }).ToArray());
            byte[] m = (byte[])good.Clone(); m[good.Length / 2] ^= 1; string mod = Put(Path.Combine(t, "mod.exe"), m);
            long seen = 0;
            Check(p.Check(ok, delegate(long d, long n) { seen = d; }, null) == null && seen == good.Length, "the right file is accepted (progress to the end)");
            string r = p.Check(Path.Combine(t, "none.exe"), null, null); Check(Arabic(r) && r.Contains("غير موجود"), "missing: " + r);
            r = p.Check(cut, null, null); Check(Arabic(r) && r.Contains("ناقص"), "truncated: " + r.Replace("\n", " / "));
            r = p.Check(longer, null, null); Check(Arabic(r) && r.Contains("ليس الملف المطلوب"), "another file: " + r.Replace("\n", " / "));
            r = p.Check(mod, null, null); Check(Arabic(r) && r.Contains("تالف"), "one changed byte: " + r.Replace("\n", " / "));
            Check(p.Check(ok, null, delegate { return true; }) == SqlPackage.Cancelled, "check can be cancelled");
            using (new FileStream(ok, FileMode.Open, FileAccess.ReadWrite, FileShare.None)) r = p.Check(ok, null, null);
            Check(Arabic(r) && r.Contains("تعذرت قراءة"), "unreadable: " + r);
            string dest = Path.Combine(t, "copy", SqlPackage.FileName);
            Check(p.Copy(ok, dest, null, null) == null && HashFile(dest) == p.Sha256 && !File.Exists(dest + ".copy"), "copy from the network to this computer");
            Check(p.Copy(ok, Path.Combine(t, "c2", "x.exe"), null, delegate { return true; }) == SqlPackage.Cancelled && Directory.GetFiles(Path.Combine(t, "c2")).Length == 0, "cancelled copy leaves nothing");
            r = p.Copy(Path.Combine(t, "none.exe"), dest, null, null); Check(Arabic(r) && File.Exists(dest), "copy of a missing file: " + r.Split('\n')[0]);
        }

        static void DownloadTests(string root, byte[] good)
        {
            Console.WriteLine("\n--- download (local test server)");
            string t = NewDir(root, "تنزيل"), dest = Path.Combine(t, SqlPackage.FolderName, SqlPackage.FileName), part = SqlPackage.PartFile(dest);
            Action clean = delegate { if (File.Exists(dest)) File.Delete(dest); if (File.Exists(part)) File.Delete(part); };

            using (Server s = new Server(good))
            {
                long last = 0; bool back = false;
                string r = Pkg(good, s.Url).Download(dest, delegate(long d, double v, string e, int f) { if (d < last) back = true; last = d; }, null);
                Check(r == null && HashFile(dest) == Hash(good) && !File.Exists(part), "plain download: file complete, hash checked, part file gone" + (r == null ? "" : " — " + r));
                Check(last == good.Length && !back, "progress reaches the end and never goes back");
                string q = s.Requests[0];
                Check(Regex.IsMatch(q, "User-Agent: EngStores-Setup\r\n") && q.IndexOf(Environment.UserName, StringComparison.OrdinalIgnoreCase) < 0 && q.IndexOf(Environment.MachineName, StringComparison.OrdinalIgnoreCase) < 0 && !q.Contains("Cookie"), "request carries no personal data");
            }
            clean();
            using (Server s = new Server(good))
            {
                s.DropAfter = good.Length / 6; s.Drops = 5;
                bool told = false;
                string r = Pkg(good, s.Url).Download(dest, delegate(long d, double v, string e, int f) { if (e != null) told = true; }, null);
                Check(r == null && HashFile(dest) == Hash(good), "connection dropped 5 times -> continued, hash OK (" + s.Requests.Count + " connections)" + (r == null ? "" : " — " + r));
                Check(s.Requests.Count >= 6 && !s.Requests[0].Contains("Range:") && s.Requests.Skip(1).All(x => Regex.IsMatch(x, @"Range: bytes=[1-9]\d*-")), "every new connection continues (Range), never from the beginning");
                Check(told, "the window is told that the connection was lost");
            }
            clean();
            using (Server s = new Server(good))
            {
                s.DropAfter = 40000;   // always drops, but every connection brings something: far more drops than MaxFailures
                SqlPackage p = Pkg(good, s.Url); p.MaxFailures = 3; p.RetryDelay = 1;
                Check(p.Download(dest, null, null) == null && HashFile(dest) == Hash(good), "gives up only without progress: " + s.Requests.Count + " drops with progress (limit 3) are fine");
            }
            clean();
            using (Server s = new Server(good))
            {
                s.Ranges = false; s.DropAfter = good.Length / 2; s.Drops = 2;
                string r = Pkg(good, s.Url).Download(dest, null, null);
                Check(r == null && HashFile(dest) == Hash(good), "server without Range: restarts cleanly, hash OK" + (r == null ? "" : " — " + r));
            }
            clean();
            using (Server s = new Server(good))
            {
                s.Ranges = false; s.DropAfter = good.Length / 2;
                SqlPackage p = Pkg(good, s.Url); p.MaxFailures = 4;
                string r = p.Download(dest, null, null);
                Check(Arabic(r) && s.Requests.Count <= 6 && !File.Exists(dest), "server without Range that always drops: gives up (" + s.Requests.Count + " connections): " + (r ?? "").Replace("\n", " / "));
            }
            clean();
            using (Server s = new Server(good))
            {
                s.Delay = 15;
                bool cancel = false; long at = 0;
                Stopwatch sw = new Stopwatch();
                string r = Pkg(good, s.Url).Download(dest, delegate(long d, double v, string e, int f) { at = d; if (d > good.Length / 4 && !cancel) { cancel = true; sw.Start(); } }, delegate { return cancel; });
                long kept = SqlPackage.PartSize(dest);
                Check(r == SqlPackage.Cancelled && kept > 0 && kept < good.Length && !File.Exists(dest) && sw.ElapsedMilliseconds < 1500, "cancel: stops at once (" + sw.ElapsedMilliseconds + " ms) and keeps the part file (" + kept + " bytes)");
                s.Delay = 0;
                r = Pkg(good, s.Url).Download(dest, null, null);
                Check(r == null && HashFile(dest) == Hash(good) && s.Requests[1].Contains("Range: bytes=" + kept + "-"), "next download continues from there, hash OK");
            }
            clean();
            using (Server s = new Server(good))
            {
                s.StallAfter = 100000;
                bool cancel = false;
                Stopwatch sw = Stopwatch.StartNew();
                SqlPackage p = Pkg(good, s.Url); p.StallTimeout = 20000;
                string r = p.Download(dest, delegate(long d, double v, string e, int f) { if (d >= 100000) cancel = true; }, delegate { return cancel || sw.ElapsedMilliseconds > 2000; });
                Check(r == SqlPackage.Cancelled && sw.ElapsedMilliseconds < 5000, "cancel while the server is silent: stops at once (" + sw.ElapsedMilliseconds + " ms)");
            }
            clean();
            using (Server s = new Server(good))
            {
                s.StallAfter = 600000;
                SqlPackage p = Pkg(good, s.Url); p.StallTimeout = 400;
                string r = p.Download(dest, null, null);
                Check(r == null && HashFile(dest) == Hash(good), "a silent server is dropped and the download continues (" + s.Requests.Count + " connections)" + (r == null ? "" : " — " + r));
            }
            clean();
            byte[] other = (byte[])good.Clone(); other[123] ^= 1;
            using (Server s = new Server(other))
            {
                string r = Pkg(good, s.Url).Download(dest, null, null);
                Check(Arabic(r) && r.Contains("لا يطابق") && !File.Exists(dest) && !File.Exists(part), "modified file at the source: refused and deleted: " + r);
            }
            using (Server s = new Server(good.Take(1000).ToArray()))
            {
                string r = Pkg(good, s.Url).Download(dest, null, null);
                Check(Arabic(r) && s.Requests.Count == 1 && !File.Exists(dest), "another file (network sign-in page…): refused without retrying: " + r);
            }
            clean();
            using (Server s = new Server(good))
            {
                s.Status = "404 Not Found";
                string r = Pkg(good, s.Url).Download(dest, null, null);
                Check(Arabic(r) && r.Contains("404") && s.Requests.Count == 1, "404: refused without retrying: " + r);
            }
            using (Server s = new Server(good))
            {
                s.Status = "503 Service Unavailable"; s.StatusTimes = 2;
                Check(Pkg(good, s.Url).Download(dest, null, null) == null && s.Requests.Count == 3, "503 twice: tried again, then downloaded");
            }
            clean();
            {
                TcpListener l = new TcpListener(IPAddress.Loopback, 0); l.Start(); int port = ((IPEndPoint)l.LocalEndpoint).Port; l.Stop();
                SqlPackage p = Pkg(good, "http://127.0.0.1:" + port + "/x"); p.MaxFailures = 3; p.StallTimeout = 8000; int most = 0;
                string r = p.Download(dest, delegate(long d, double v, string e, int f) { most = Math.Max(most, f); }, null);
                Check(Arabic(r) && r.Contains("لا يوجد اتصال") && most == 2, "no connection: gives up after MaxFailures attempts: " + r.Replace("\n", " / "));
            }
            // a part file that is already complete / too long
            Put(part, good);
            using (Server s = new Server(good)) Check(Pkg(good, s.Url).Download(dest, null, null) == null && s.Requests.Count == 0 && File.Exists(dest), "complete part file: only checked, nothing downloaded");
            clean(); Put(part, good.Concat(good).ToArray());
            using (Server s = new Server(good)) Check(Pkg(good, s.Url).Download(dest, null, null) == null && HashFile(dest) == Hash(good), "part file longer than the package: started again");
            clean(); Put(part, new byte[500000]);
            using (Server s = new Server(good))
            {
                string r = Pkg(good, s.Url).Download(dest, null, null);
                Check(Arabic(r) && !File.Exists(part) && !File.Exists(dest), "damaged part file: noticed at the end and deleted");
            }
            File.WriteAllText(Path.Combine(t, "afile"), "x");
            using (Server s = new Server(good))
            {
                string r = Pkg(good, s.Url).Download(Path.Combine(t, "afile", "x.exe"), null, null);
                Check(Arabic(r) && r.Contains("تعذر حفظ") && s.Requests.Count == 0, "folder that cannot be made: clear reason at once");
            }
        }

        // ------------------------------------------------------------ setup-server.ps1
        /// <summary>Runs PowerShell code after the package functions of the embedded script (own $Work, Stop-Setup writes ERR|…).</summary>
        static string Ps(string dir, string work, string body, out int exit, params string[] env)
        {
            string script = Path.Combine(dir, "setup-server.ps1"), harness = Path.Combine(dir, "harness.ps1"), result = Path.Combine(dir, "result.txt");
            File.WriteAllText(script, ServerSetup.SetupScriptText(), new UTF8Encoding(true));
            if (File.Exists(result)) File.Delete(result);
            string h =
                "$ErrorActionPreference = 'Stop'\n$Work = '" + work + "'\n$ResultFile = '" + result + "'\n" +
                "function Say { }\nfunction Done([string]$r) { [IO.File]::WriteAllText($ResultFile, $r, (New-Object Text.UTF8Encoding $false)) }\n" +
                "function Stop-Setup([string]$m) { Done ('ERR|' + $m); exit 1 }\n" +
                "trap { Stop-Setup ('trap: ' + $_.Exception.Message + $_.InvocationInfo.PositionMessage) }\n" +
                "$tk = $null; $er = $null\n$ast = [Management.Automation.Language.Parser]::ParseFile('" + script + "', [ref]$tk, [ref]$er)\n" +
                "foreach ($f in $ast.FindAll({ param($n) $n -is [Management.Automation.Language.FunctionDefinitionAst] }, $false)) { if ('Test-MicrosoftSigned','Test-Package','Get-CheckedPackage','Expand-Package','Remove-InstallFiles','Get-InstallHelp' -contains $f.Name) { . ([ScriptBlock]::Create($f.Extent.Text)) } }\n" +
                "foreach ($a in $ast.FindAll({ param($n) $n -is [Management.Automation.Language.AssignmentStatementAst] }, $false)) { if ('$PkgName','$PkgSize','$PkgHash' -contains $a.Left.Extent.Text) { . ([ScriptBlock]::Create($a.Extent.Text)) } }\n" +
                body + "\n";
            File.WriteAllText(harness, h, new UTF8Encoding(true));
            ProcessStartInfo si = new ProcessStartInfo(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), Environment.Is64BitOperatingSystem && !Environment.Is64BitProcess ? "Sysnative" : "System32", @"WindowsPowerShell\v1.0\powershell.exe"),
                "-NoProfile -NonInteractive -ExecutionPolicy Bypass -File \"" + harness + "\"");
            si.UseShellExecute = false; si.CreateNoWindow = true; si.RedirectStandardOutput = true; si.RedirectStandardError = true;
            si.EnvironmentVariables["__COMPAT_LAYER"] = "RunAsInvoker";   // the package asks for administrator rights: never prompt here
            foreach (string e in env) { int i = e.IndexOf('='); si.EnvironmentVariables[e.Substring(0, i)] = e.Substring(i + 1); }
            using (Process p = Process.Start(si))
            {
                StringBuilder err = new StringBuilder(); p.ErrorDataReceived += delegate(object s, DataReceivedEventArgs e) { if (e.Data != null) err.AppendLine(e.Data); }; p.BeginErrorReadLine();
                string o = p.StandardOutput.ReadToEnd(); p.WaitForExit(); exit = p.ExitCode;
                if (o.Trim().Length + err.Length > 0) Console.WriteLine("   ps: " + (o + err).Trim());
            }
            return File.Exists(result) ? File.ReadAllText(result, Encoding.UTF8) : null;
        }

        /// <summary>A small program that behaves like Microsoft's self-extractor (/q /x:"folder"): writes setup.exe and a file;
        /// FAKE_EXIT = its exit code, FAKE_NOSETUP = writes no setup.exe.</summary>
        static byte[] FakePackage(string dir)
        {
            string exe = Path.Combine(dir, "fake.exe");
            CompilerParameters cp = new CompilerParameters(); cp.GenerateExecutable = true; cp.OutputAssembly = exe; cp.CompilerOptions = "/target:winexe";
            CompilerResults r = new Microsoft.CSharp.CSharpCodeProvider().CompileAssemblyFromSource(cp,
                "using System; using System.IO; class P { static int Main(string[] a) { string d = null; foreach (string x in a) if (x.StartsWith(\"/x:\")) d = x.Substring(3).Trim('\"'); " +
                "if (File.Exists(Path.Combine(d, \"stale.txt\"))) return 77; Directory.CreateDirectory(d); File.WriteAllText(Path.Combine(d, \"args.txt\"), string.Join(\" \", a)); " +
                "if (Environment.GetEnvironmentVariable(\"FAKE_NOSETUP\") == null) File.WriteAllText(Path.Combine(d, \"setup.exe\"), \"fake\"); " +
                "string e = Environment.GetEnvironmentVariable(\"FAKE_EXIT\"); return e == null ? 0 : int.Parse(e); } }");
            if (r.Errors.HasErrors) throw new Exception(r.Errors[0].ToString());
            return File.ReadAllBytes(exe);
        }

        static void ScriptTests(string root, string real)
        {
            Console.WriteLine("\n--- setup-server.ps1 (package part, nothing installed)");
            string script = ServerSetup.SetupScriptText();
            Check(Regex.Match(script, @"\$PkgHash\s*=\s*'([0-9A-F]{64})'").Groups[1].Value == SqlPackage.OfficialSha256, "the script pins the same SHA-256 as the program");
            Check(Regex.Match(script, @"\$PkgSize\s*=\s*(\d+)").Groups[1].Value == SqlPackage.OfficialSize.ToString() && Regex.Match(script, @"\$PkgName\s*=\s*'([^']+)'").Groups[1].Value == SqlPackage.FileName, "…the same size and file name");
            Check(!Regex.IsMatch(script, @"https?://|Invoke-WebRequest|Invoke-RestMethod|WebClient|BitsTransfer|SecurityProtocol|ACTION=Download|\$Ssei", RegexOptions.IgnoreCase), "no internet code in the script");
            Check(!Regex.IsMatch(script, @"/QS\b") && script.Contains("'/Q'") && !Regex.IsMatch(script, @"Start-Process(?![^\r\n]*-WindowStyle Hidden)"), "only fully silent, hidden processes");
            byte[] src = File.ReadAllBytes(Path.Combine(Repo(), @"src\Server\setup-server.ps1"));
            Check(src[0] == 0xEF && src[1] == 0xBB && src[2] == 0xBF, "setup-server.ps1 is UTF-8 with BOM");
            Check(SqlPackage.OfficialUrl.StartsWith("https://download.microsoft.com/"), "download address: Microsoft, https");

            string dir = NewDir(root, "سكربت"), work = NewDir(dir, "عمل"), media = Path.Combine(dir, SqlPackage.FolderName, SqlPackage.FileName), copy = Path.Combine(work, SqlPackage.FileName);
            int x; string r;
            string get = "$p = Get-CheckedPackage '" + media + "'; Done ('OK|' + $p)";

            // real constants: anything else is refused
            r = Ps(dir, work, get, out x); Check(x == 1 && r != null && r.StartsWith("ERR|") && Arabic(r) && r.Contains("غير موجود"), "no package -> " + r);
            r = Ps(dir, work, "$p = Get-CheckedPackage ''; Done ('OK|' + $p)", out x); Check(x == 1 && r.StartsWith("ERR|") && r.Contains("غير موجود"), "empty -MediaFile -> the same clear error");
            Put(media, new byte[200000]);
            r = Ps(dir, work, get, out x); Check(x == 1 && r.StartsWith("ERR|") && r.Contains("ناقص") && !File.Exists(copy), "incomplete package -> " + r);

            // a fake extractor with its own pinned size and hash
            byte[] fake = FakePackage(dir);
            string pin = "$PkgSize = " + fake.Length + "; $PkgHash = '" + Hash(fake) + "'\n", signed = "function Test-MicrosoftSigned { return $true }\n";
            byte[] mod = (byte[])fake.Clone(); mod[mod.Length - 1] ^= 1;
            Put(media, mod);
            r = Ps(dir, work, pin + signed + get, out x); Check(x == 1 && r.StartsWith("ERR|") && r.Contains("تالف") && !File.Exists(copy), "damaged package -> " + r);
            Put(media, fake.Concat(new byte[] { 0 }).ToArray());
            r = Ps(dir, work, pin + signed + get, out x); Check(x == 1 && r.Contains("ليس الملف المطلوب"), "another file -> " + r);
            Put(media, fake);
            r = Ps(dir, work, pin + get, out x); Check(x == 1 && r.Contains("غير موقّع") && !File.Exists(copy), "right hash but not signed by Microsoft -> " + r);
            r = Ps(dir, work, pin + signed + get, out x); Check(x == 0 && r == "OK|" + copy && HashFile(copy) == Hash(fake), "good package -> copied into the work folder");
            File.Delete(media);
            r = Ps(dir, work, pin + signed + get, out x); Check(x == 0 && r == "OK|" + copy, "a checked copy of an earlier attempt is used again (no package needed)");
            Put(copy, fake.Take(fake.Length / 2).ToArray());
            r = Ps(dir, work, pin + signed + get, out x); Check(x == 1 && r.StartsWith("ERR|") && !File.Exists(copy), "an incomplete copy of an earlier attempt is not used");
            Put(copy, mod); Put(media, fake);
            r = Ps(dir, work, pin + signed + get, out x); Check(x == 0 && HashFile(copy) == Hash(fake), "…and is replaced by the good package");
            using (new FileStream(media, FileMode.Open, FileAccess.Read, FileShare.None)) { File.Delete(copy); r = Ps(dir, work, pin + signed + get, out x); }
            Check(x == 1 && r.StartsWith("ERR|") && r.Contains("تعذرت قراءة") && !r.Contains("trap:"), "unreadable package -> " + r);

            // extraction
            string extract = Path.Combine(work, "extract"), exp = "$s = Expand-Package '" + copy + "'; Done ('OK|' + $s)";
            Put(copy, fake);
            r = Ps(dir, work, pin + signed + exp, out x);
            Check(x == 0 && r == "OK|" + Path.Combine(extract, "setup.exe") && File.ReadAllText(Path.Combine(extract, "args.txt")).StartsWith("/q /x:"), "extracts silently into the work folder");
            Put(Path.Combine(extract, "stale.txt"), new byte[] { 1 }); File.WriteAllText(Path.Combine(extract, "setup.exe"), "half");
            r = Ps(dir, work, pin + signed + exp, out x);
            Check(x == 0 && !File.Exists(Path.Combine(extract, "stale.txt")) && File.ReadAllText(Path.Combine(extract, "setup.exe")) == "fake", "an incomplete extract folder is removed and extracted again");
            r = Ps(dir, work, pin + signed + exp, out x, "FAKE_EXIT=1392"); Check(x == 1 && r.StartsWith("ERR|") && r.Contains("1392"), "extractor error -> " + r);
            r = Ps(dir, work, pin + signed + exp, out x, "FAKE_NOSETUP=1"); Check(x == 1 && r.StartsWith("ERR|") && r.Contains("لم يكتمل"), "extractor without setup.exe -> " + r);
            r = Ps(dir, work, pin + exp, out x); Check(x == 1 && r.Contains("غير موقّع"), "setup.exe not signed by Microsoft -> " + r);
            Directory.CreateDirectory(extract);
            using (new FileStream(Path.Combine(extract, "locked.txt"), FileMode.Create, FileAccess.Write, FileShare.None)) r = Ps(dir, work, pin + signed + exp, out x);
            Check(x == 1 && r.StartsWith("ERR|") && r.Contains("أعد تشغيل"), "files in use -> " + r);
            string running = Path.Combine(extract, "setup.exe"); Directory.CreateDirectory(extract);
            string keep = Put(Path.Combine(extract, "keep.txt"), new byte[] { 1 });
            File.Copy(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "PING.EXE"), running, true);
            ProcessStartInfo ri = new ProcessStartInfo(running, "-n 60 127.0.0.1"); ri.UseShellExecute = false; ri.CreateNoWindow = true; ri.EnvironmentVariables["__COMPAT_LAYER"] = "RunAsInvoker";
            Process still = null;
            try { still = Process.Start(ri); } catch (Exception ex) { Console.WriteLine("   (could not start the stand-in: " + ex.Message + ")"); }
            if (still != null)
            {
                r = Ps(dir, work, pin + signed + exp, out x);
                try { still.Kill(); still.WaitForExit(); } catch { }
                Check(x == 1 && r.StartsWith("ERR|") && r.Contains("ما زال يعمل") && File.Exists(keep), "a setup that is still running keeps its files -> " + r);
            }
            // clean-up after a successful install
            Directory.Delete(extract, true);
            Put(copy, fake); Put(Path.Combine(extract, "a", "b.txt"), new byte[1]); Put(Path.Combine(work, "media", "x.exe"), new byte[1]); Put(Path.Combine(work, "SQL2022-SSEI-Expr.exe"), new byte[1]);
            Put(Path.Combine(work, "backup.ps1"), new byte[1]); Put(Path.Combine(work, "setup-server.ps1"), new byte[1]);
            r = Ps(dir, work, "Remove-InstallFiles; Done 'OK|'", out x);
            string left = string.Join(", ", Directory.GetFileSystemEntries(work).Select(f => Path.GetFileName(f)).OrderBy(f => f));
            Check(x == 0 && left == "backup.ps1, setup-server.ps1", "after the install: extract, package and old downloads removed; kept: " + left);
            // exit codes
            r = Ps(dir, work, "Done (((-2067919934, 3017, -2061893606, 1618, 1602, 1603, 1633, -2147024784, -2147024891) | ForEach-Object { [int](Get-InstallHelp $_).Length }) -join ',') ", out x);
            Check(x == 0 && r.Split(',').Length == 9 && r.Split(',').All(n => int.Parse(n) > 20), "help text for every known exit code (" + r + ")");
            r = Ps(dir, work, "Done ('[' + (Get-InstallHelp 12345) + ']' + (Get-InstallHelp -2067919934))", out x);
            Check(r.StartsWith("[]") && r.Contains("أعد تشغيل"), "unknown code: no special text; pending restart: " + r);
            Check(Regex.IsMatch(script, @"-eq 3010") && script.Contains("'|reboot'"), "3010 = installed, the program is told to ask for a restart");

            if (real == null) { Console.WriteLine("(real package not given: its extraction is not tested)"); return; }
            Console.WriteLine("\n--- the real package");
            Check(new SqlPackage().Check(real, null, null) == null, "the program accepts the real package");
            string rw = NewDir(dir, "real");
            r = Ps(dir, rw, "$p = Get-CheckedPackage '" + real + "'; $s = Expand-Package $p; Done ('OK|' + $s + '|' + (Test-MicrosoftSigned $s))", out x);
            Check(x == 0 && r == "OK|" + Path.Combine(rw, "extract", "setup.exe") + "|True", "checked, extracted, setup.exe signed by Microsoft: " + r);
            r = Ps(dir, rw, "Remove-InstallFiles; Done 'OK|'", out x);
            Check(Directory.GetFileSystemEntries(rw).Length == 0, "removed again");
        }

        static string Repo() { return Path.GetFullPath(Path.Combine(Path.GetDirectoryName(Application.ExecutablePath), "..", "..")); }

        // ------------------------------------------------------------ scripts\make-offline.ps1
        static int MakeOffline(string outDir, string package, out string output)
        {
            string run = Path.Combine(Path.GetDirectoryName(outDir), "run.ps1");
            File.WriteAllText(run, "[Console]::OutputEncoding = [Text.Encoding]::UTF8\n& '" + Path.Combine(Repo(), @"scripts\make-offline.ps1") + "' -Out '" + outDir + "' -Package '" + package + "'\nexit $LASTEXITCODE\n", new UTF8Encoding(true));
            ProcessStartInfo si = new ProcessStartInfo("powershell.exe", "-NoProfile -NonInteractive -ExecutionPolicy Bypass -File \"" + run + "\"");
            si.UseShellExecute = false; si.CreateNoWindow = true; si.RedirectStandardOutput = true; si.RedirectStandardError = true; si.StandardOutputEncoding = Encoding.UTF8;
            using (Process p = Process.Start(si))
            {
                StringBuilder err = new StringBuilder(); p.ErrorDataReceived += delegate(object s, DataReceivedEventArgs e) { if (e.Data != null) err.AppendLine(e.Data); }; p.BeginErrorReadLine();
                output = p.StandardOutput.ReadToEnd().Trim(); p.WaitForExit(); output = (output + "\n" + err).Trim(); return p.ExitCode;
            }
        }

        static void OfflineFolderTests(string root, string real)
        {
            Console.WriteLine("\n--- scripts\\make-offline.ps1");
            string dir = NewDir(root, "مجلد [كامل]"), o; int x;
            string exe = Path.Combine(Repo(), "Inventory Management.exe");
            if (!File.Exists(exe)) { Console.WriteLine("(run build.bat first: make-offline.ps1 is not tested)"); return; }
            string bad = Put(Path.Combine(dir, "bad.exe"), new byte[1000]), target = Path.Combine(dir, "نسخة كاملة بدون إنترنت");
            x = MakeOffline(target, bad, out o); Check(x != 0 && !File.Exists(Path.Combine(target, "Inventory Management.exe")), "wrong package refused: " + o.Split('\n').Last());
            x = MakeOffline(target, Path.Combine(dir, "none.exe"), out o); Check(x != 0, "missing package refused: " + o.Split('\n').Last());
            if (real == null) { Console.WriteLine("(real package not given: building the folder is not tested)"); return; }
            x = MakeOffline(target, real, out o); Console.WriteLine("   " + o.Replace("\n", "\n   "));
            List<string> files = Directory.GetFiles(target, "*", SearchOption.AllDirectories).Select(f => f.Substring(target.Length + 1)).ToList();
            string[] must = { "Inventory Management.exe", "دليل التشغيل.txt", "إعداد الخادم المجاني.txt", @"docs\manual\index.html", SqlPackage.FolderName + "\\" + SqlPackage.FileName };
            Check(x == 0 && must.All(m => files.Contains(m)) && files.Any(f => f.StartsWith(@"docs\manual\img\")), "complete folder (" + files.Count + " files)");
            Check(files.All(f => must.Contains(f) || f.StartsWith(@"docs\manual\")) && !files.Any(f => Regex.IsMatch(f, @"settings\.ini|logo\.png|\.(amr|mdb|mde|log|bak|ldb)$", RegexOptions.IgnoreCase)), "no settings, data, logs, logo, backups");
            Check(new SqlPackage().Check(Path.Combine(target, SqlPackage.FolderName, SqlPackage.FileName), null, null) == null, "the copied package is correct");
            Check(new SqlPackage().Find(target, Path.Combine(dir, "u"), new List<KeyValuePair<string, bool>>(), 4000).Count == 1, "the program in that folder finds it");
            x = MakeOffline(target, real, out o); Check(x == 0, "can be refreshed");
            Put(Path.Combine(target, "settings.ini"), new byte[5]);
            x = MakeOffline(target, real, out o); Check(x != 0 && File.Exists(Path.Combine(target, "settings.ini")), "a folder that is in use (settings.ini) is refused: " + o.Split('\n').Last());
        }

        // ------------------------------------------------------------ the windows
        [DllImport("user32.dll")] static extern bool PrintWindow(IntPtr h, IntPtr hdc, uint flags);

        static void Shot(Form f, string path, bool close)
        {
            f.StartPosition = FormStartPosition.Manual; f.Location = new Point(-4000, -4000); f.ShowInTaskbar = false; f.TopMost = false;
            f.Show();
            DateTime end = DateTime.Now.AddMilliseconds(500); while (DateTime.Now < end) { Application.DoEvents(); Thread.Sleep(15); }
            using (Bitmap b = new Bitmap(f.Width, f.Height))
            {
                using (Graphics g = Graphics.FromImage(b)) { IntPtr hdc = g.GetHdc(); try { PrintWindow(f.Handle, hdc, 2); } finally { g.ReleaseHdc(hdc); } }
                b.Save(path, ImageFormat.Png);
            }
            Console.WriteLine(path);
            if (close) f.Dispose();
        }

        static void Shots(string dir, string tag)
        {
            Directory.CreateDirectory(dir);
            Func<string, string> P = delegate(string n) { return Path.Combine(dir, n + tag + ".png"); };
            long size = SqlPackage.OfficialSize;
            Shot(new SqlPackageDialog(null, 0, size), P("package_dialog"), true);
            Shot(new SqlPackageDialog("الملف ناقص: حجمه 120 ميغابايت والمطلوب 266 ميغابايت. النسخ أو التنزيل لم يكتمل، انسخه من جديد.\n" + SqlPackage.Ltr("E:\\مكونات\\SQLEXPR_x64_ENU.exe"), 125829120, size), P("package_dialog_note"), true);
            WaitForm w = DownloadWindow(); w.Download(size / 3, size, 2.4 * 1048576, null, 0, 10); Shot(w, P("download"), true);
            w = DownloadWindow(); w.Download(size / 3, size, 0, "انقطع الاتصال", 3, 10); Shot(w, P("download_retry"), true);
            w = new WaitForm("جاري فحص حزمة SQL Server", "التأكد أن الملف كامل وسليم قبل التنصيب (أقل من دقيقة عادة).", "إلغاء الفحص");
            w.Progress("فحص الملف:\n" + SqlPackage.Ltr("E:\\مكونات\\SQLEXPR_x64_ENU.exe", 60), 0.4, -1); Shot(w, P("check"), true);
            w = new WaitForm("جاري تنصيب السيرفر الرئيسي", "⚠ لا تغلق البرنامج ولا تطفئ الحاسوب حتى تظهر رسالة الانتهاء.\nالمدة المتوقعة: 4–10 دقائق حسب سرعة الحاسوب (مرة واحدة فقط).", null);
            w.Step("تنصيب SQL Server… (أطول خطوة: عادة 2–7 دقائق حسب سرعة الحاسوب)", 195, 80); Shot(w, P("install"), true);
        }

        static WaitForm DownloadWindow()
        {
            return new WaitForm("جاري تنزيل SQL Server من الإنترنت", "إذا انقطع الاتصال يُكمل التنزيل من حيث توقف تلقائيا.\nبعد التنزيل يبدأ التنصيب: 4–10 دقائق حسب سرعة الحاسوب.", "إلغاء التنزيل");
        }

        static void WindowTests()
        {
            Console.WriteLine("\n--- windows");
            foreach (bool en in new bool[] { false, true })
            {
                Lang.English = en;
                using (SqlPackageDialog d = new SqlPackageDialog("الملف تالف أو معدّل (لا يطابق ملف مايكروسوفت الأصلي). انسخه من جديد من مصدر سليم.", 1000000, SqlPackage.OfficialSize))
                {
                    d.StartPosition = FormStartPosition.Manual; d.Location = new Point(-4000, -4000); d.ShowInTaskbar = false; d.Show(); Application.DoEvents();
                    List<string> arabic = new List<string>(); bool clipped = false;
                    foreach (Control c in d.Controls)
                    {
                        Label l = c as Label; if (l == null) continue;
                        if (LangKey.HasArabic(l.Text.Replace(SqlPackage.FolderName, ""))) arabic.Add(l.Text);
                        if (TextRenderer.MeasureText(l.Text, l.Font, new Size(l.Width, 0), TextFormatFlags.WordBreak).Height > l.Height) clipped = true;
                    }
                    Check(!clipped, (en ? "English" : "Arabic") + " dialog: no clipped text");
                    if (en) Check(arabic.Count == 0, "English dialog: no Arabic left" + (arabic.Count == 0 ? "" : ": " + arabic[0]));
                    Button file = (Button)d.AcceptButton, down = null;
                    foreach (Control c in d.Controls) if (c is Button && c != file) down = (Button)c;
                    Check(file.Text.StartsWith("اختيار الملف") && down.Text == "تنزيل من الإنترنت (266 ميغابايت)", "the default button is the file, not the download");
                    Check(d.Result == SqlPackageDialog.Choice.Cancel, "closing the dialog = cancel (nothing is downloaded)");
                    down.PerformClick(); Check(d.Result == SqlPackageDialog.Choice.Download, "download only after its button");
                }
                using (WaitForm w = DownloadWindow())
                {
                    w.StartPosition = FormStartPosition.Manual; w.Location = new Point(-4000, -4000); w.ShowInTaskbar = false; w.TopMost = false;
                    using (Form owner = new Form())
                    {
                        owner.StartPosition = FormStartPosition.Manual; owner.Location = new Point(-4000, -4000); owner.ShowInTaskbar = false; owner.Show();
                        w.ShowOver(owner);
                        w.Download(SqlPackage.OfficialSize / 2, SqlPackage.OfficialSize, 3 * 1048576, null, 0, 10); Application.DoEvents();
                        string all = ""; Button cancel = null;
                        foreach (Control c in w.Controls) { all += c.Text + "\n"; if (c is Button) cancel = (Button)c; }
                        Check(all.Contains("50" + (en ? "%" : "٪")) && all.Contains("133") && all.Contains("3.0"), "download window: percent, MB, speed: " + all.Split('\n')[1] + " / " + all.Split('\n')[2]);
                        if (en) { string a = ""; foreach (Control c in w.Controls) if (!(c is Button) && LangKey.HasArabic(c.Text)) a += c.Text; Check(a == "", "English download window: no Arabic left " + a); }
                        Check(!IsWindowEnabled(owner.Handle), "the window behind does not take clicks meanwhile");
                        SendMessage(w.Handle, 0x112 /*WM_SYSCOMMAND*/, (IntPtr)0xF060 /*SC_CLOSE*/, IntPtr.Zero); Application.DoEvents();
                        Check(w.Visible, "Alt+F4 does not close it");
                        cancel.PerformClick(); Check(w.CancelPressed && !cancel.Enabled, "«إلغاء التنزيل»");
                        w.Close(); Check(IsWindowEnabled(owner.Handle), "the window behind works again");
                    }
                }
                using (WaitForm w = new WaitForm("x", "y", null)) { bool any = false; foreach (Control c in w.Controls) any |= c is Button; Check(!any, "no cancel button while installing"); }
            }
            Lang.English = false;
        }
        /// <summary>The program's own steps before the Windows permission prompt (ServerSetup.GetPackage), the dialog answered by a timer.</summary>
        static void FlowTests(string real)
        {
            Console.WriteLine("\n--- before the installation (find → check → dialog)");
            if (new SqlPackage().Find().Count > 0) { Console.WriteLine("(there is a package on this computer already: not tested)"); return; }
            System.Reflection.MethodInfo get = typeof(ServerSetup).GetMethod("GetPackage", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
            string dir = Path.Combine(Settings.AppDir, SqlPackage.FolderName), file = Path.Combine(dir, SqlPackage.FileName), seen = null, pick = null;
            System.Windows.Forms.Timer t = new System.Windows.Forms.Timer(); t.Interval = 100;
            t.Tick += delegate
            {
                foreach (Form f in Application.OpenForms)
                {
                    SqlPackageDialog d = f as SqlPackageDialog; if (d == null || !d.Visible) continue;
                    seen = ""; foreach (Control c in d.Controls) if (c is Label) { seen = c.Text; break; }
                    if (pick != null) { d.Picked = pick; d.Result = SqlPackageDialog.Choice.File; pick = null; }
                    d.Close(); return;
                }
            };
            t.Start();
            Func<string> run = delegate { seen = null; return (string)get.Invoke(null, new object[] { null }); };
            try
            {
                string r = run();
                Check(r == null && seen != null && seen.StartsWith("لتجهيز") && SqlPackage.PartSize(SqlPackage.DownloadPath()) == 0, "nothing found -> the dialog; cancelled -> nothing installed, nothing downloaded");
                Put(file, new byte[5000000]);
                r = run();
                Check(r == null && seen != null && seen.Contains("ناقص"), "incomplete copy next to the program -> the dialog says why");
                if (real == null) return;
                pick = real;
                r = run();
                Check(r == real, "picked file -> checked and used");
                File.Copy(real, file, true);
                r = run();
                Check(r == file && seen == null, "package next to the program -> checked and used without any question");
                using (FileStream f = new FileStream(file, FileMode.Open)) { f.Position = 1000000; int b = f.ReadByte(); f.Position--; f.WriteByte((byte)(b ^ 1)); }
                r = run();
                Check(r == null && seen != null && seen.Contains("تالف"), "damaged package -> the dialog says why, nothing is used");
            }
            finally { t.Dispose(); if (Directory.Exists(dir)) Directory.Delete(dir, true); }
        }

        [DllImport("user32.dll")] static extern bool IsWindowEnabled(IntPtr h);
        [DllImport("user32.dll")] static extern IntPtr SendMessage(IntPtr h, int msg, IntPtr w, IntPtr l);

        [STAThread]
        static int Main(string[] args)
        {
            Console.OutputEncoding = Encoding.UTF8;
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            if (args.Length < 1) { Console.WriteLine("usage: OfflineSetupTests.exe <outdir> [real package]   |   OfflineSetupTests.exe shots <outdir> [--en] [dark]"); return 2; }
            if (args[0] == "shots")
            {
                string tag = "";
                foreach (string a in args.Skip(2))
                {
                    if (a == "--en") { Lang.English = true; tag += "_en"; }
                    if (a == "dark") { Theme.Use(Palette.Make(true, "blue")); tag += "_dark"; }
                }
                Shots(args[1], tag);
                return 0;
            }
            string root = NewDir(Path.GetFullPath(args[0]), "offline-tests"), real = args.Length > 1 ? Path.GetFullPath(args[1]) : null;
            byte[] good = new byte[3 * 1024 * 1024 + 777]; new Random(5).NextBytes(good);
            FindTests(root, good);
            CheckTests(root, good);
            DownloadTests(root, good);
            ScriptTests(root, real);
            OfflineFolderTests(root, real);
            WindowTests();
            FlowTests(real);
            try { Directory.Delete(root, true); } catch (Exception ex) { Console.WriteLine("cleanup: " + ex.Message); }
            Console.WriteLine(fails == 0 ? "\nALL TESTS PASSED" : "\nFAILURES: " + fails);
            return fails;
        }
    }
}
