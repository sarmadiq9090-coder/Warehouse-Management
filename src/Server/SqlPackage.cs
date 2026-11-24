using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using System.Threading;
using System;

namespace Stores
{
    /// <summary>Microsoft's SQL Server Express package («مكونات\SQLEXPR_x64_ENU.exe», 266 MB): finding it on this computer,
    /// checking it and downloading it (only when the user asks). Never published with the program: the offline folder
    /// is made by scripts\make-offline.ps1. The same size and hash are pinned in setup-server.ps1, which checks its own
    /// protected copy again. The fields are settable for the tests.</summary>
    class SqlPackage
    {
        public const string FileName = "SQLEXPR_x64_ENU.exe";
        public const string FolderName = "مكونات";
        // SQL Server 2022 Express 16.0.1000.6, signed by Microsoft (scripts\make-offline.ps1 reads these two lines)
        public const long OfficialSize = 279293816;
        public const string OfficialSha256 = "2E61C8BBDE6021F9026C54AD9DB4BBB1227E68761D4C00A6A50A2C70FE7AFE05";
        public const string OfficialUrl = "https://download.microsoft.com/download/3/8/d/38de7036-2433-4207-8eae-06e247e17b25/SQLEXPR_x64_ENU.exe";

        /// <summary>Returned by Check / Copy / Download when the user stopped them.</summary>
        public const string Cancelled = "أُلغيت العملية";

        public string Url = OfficialUrl;
        public string Sha256 = OfficialSha256;
        public long Size = OfficialSize;
        /// <summary>Attempts in a row without one new byte before giving up; new bytes start the count again.</summary>
        public int MaxFailures = 10;
        /// <summary>Milliseconds: first wait between attempts (doubled each time), longest wait, no data from the server.</summary>
        public int RetryDelay = 1000, MaxRetryDelay = 30000, StallTimeout = 30000;

        /// <summary>Download progress: bytes so far, bytes per second (0 = not known yet); while waiting to reconnect:
        /// the reason and how many attempts in a row brought nothing.</summary>
        public delegate void DownloadProgress(long done, double speed, string error, int failures);

        public static string Mb(long bytes) { return (bytes / 1048576.0).ToString("0", CultureInfo.InvariantCulture); }

        /// <summary>A path inside Arabic text, written from the left; shortMax &gt; 0: a longer path as «X:\…\file».</summary>
        public static string Ltr(string path, int shortMax = 0)
        {
            if (shortMax > 0 && path.Length > shortMax)
                try { path = Path.GetPathRoot(path).TrimEnd('\\') + "\\…\\" + Path.GetFileName(path); } catch { }
            return "\u202A" + path + "\u202C";
        }

        // ------------------------------------------------------------ where the package is
        /// <summary>Where the program keeps the package when it cannot write next to itself.</summary>
        public static string UserDir { get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "EngStores"); } }

        /// <summary>The packages on this computer, best first: next to the program («مكونات», then beside the .exe), a
        /// previous download, then the ready fixed / removable drives. A file of the wrong size (incomplete copy) comes
        /// last. Never hangs: a place that does not answer in time is skipped.</summary>
        public List<string> Find()
        {
            List<KeyValuePair<string, bool>> drives = new List<KeyValuePair<string, bool>>();
            try
            {
                foreach (DriveInfo d in DriveInfo.GetDrives())
                    if (d.DriveType == DriveType.Fixed || d.DriveType == DriveType.Removable)
                        drives.Add(new KeyValuePair<string, bool>(d.Name, d.DriveType == DriveType.Removable));
            }
            catch { }
            return Find(Settings.AppDir, UserDir, drives, 4000);
        }

        /// <summary>drives: root folder, and whether to look one level deeper (a flash drive: X:\copied folder\مكونات).</summary>
        public List<string> Find(string appDir, string userDir, IList<KeyValuePair<string, bool>> drives, int timeoutMs)
        {
            List<Func<List<string>>> places = new List<Func<List<string>>>();
            places.Add(delegate { return new List<string> { Path.Combine(appDir, FolderName, FileName), Path.Combine(appDir, FileName) }; });
            places.Add(delegate { return new List<string> { Path.Combine(userDir, FileName) }; });
            foreach (KeyValuePair<string, bool> d in drives)
            {
                string root = d.Key; bool deeper = d.Value;
                places.Add(delegate
                {
                    List<string> l = new List<string> { Path.Combine(root, FolderName, FileName), Path.Combine(root, FileName) };
                    if (deeper && Directory.Exists(root))
                        foreach (string sub in Directory.EnumerateDirectories(root).Take(100)) l.Add(Path.Combine(sub, FolderName, FileName));
                    return l;
                });
            }
            List<KeyValuePair<string, long>>[] found = new List<KeyValuePair<string, long>>[places.Count];
            Thread[] threads = new Thread[places.Count];
            for (int i = 0; i < places.Count; i++)
            {
                int n = i;
                threads[n] = new Thread(delegate()
                {
                    List<KeyValuePair<string, long>> mine = new List<KeyValuePair<string, long>>();
                    try
                    {
                        foreach (string f in places[n]())
                        {
                            FileInfo fi = new FileInfo(f);
                            if (fi.Exists) mine.Add(new KeyValuePair<string, long>(fi.FullName, fi.Length));
                        }
                    }
                    catch { }   // drive without a disk, no permission…
                    found[n] = mine;
                });
                threads[n].IsBackground = true; threads[n].Start();
            }
            Stopwatch sw = Stopwatch.StartNew();
            foreach (Thread t in threads) t.Join((int)Math.Max(0, timeoutMs - sw.ElapsedMilliseconds));
            List<KeyValuePair<string, long>> all = new List<KeyValuePair<string, long>>();
            HashSet<string> seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (List<KeyValuePair<string, long>> l in found)
                if (l != null) foreach (KeyValuePair<string, long> f in l) if (seen.Add(f.Key)) all.Add(f);
            return all.OrderBy(f => f.Value == Size ? 0 : 1).Select(f => f.Key).ToList();   // (stable order)
        }

        /// <summary>True when the file is on a disk of this computer. Mapped network drives do not exist for the elevated
        /// setup, so a package on the network is copied to this computer first.</summary>
        public static bool IsLocal(string path)
        {
            try
            {
                string full = Path.GetFullPath(path);
                if (full.StartsWith(@"\\")) return false;
                return new DriveInfo(Path.GetPathRoot(full)).DriveType != DriveType.Network;
            }
            catch { return false; }
        }

        /// <summary>Where a download goes: «مكونات» next to the program when it can be written there (on this computer's
        /// own disk), otherwise the user's own folder.</summary>
        public static string DownloadPath() { return DownloadPath(Settings.AppDir, UserDir); }

        public static string DownloadPath(string appDir, string userDir)
        {
            if (IsLocal(appDir))
            {
                string dir = Path.Combine(appDir, FolderName);
                try
                {
                    Directory.CreateDirectory(dir);
                    string probe = Path.Combine(dir, ".write_test");
                    File.WriteAllText(probe, "x"); File.Delete(probe);
                    return Path.Combine(dir, FileName);
                }
                catch { }
            }
            return Path.Combine(userDir, FileName);
        }

        // ------------------------------------------------------------ check
        /// <summary>null when the file is exactly Microsoft's package, otherwise the reason (Arabic). Reads the whole file.</summary>
        public string Check(string path, Action<long, long> progress, Func<bool> cancelled)
        {
            try
            {
                FileInfo fi = new FileInfo(path);
                if (!fi.Exists) return "الملف غير موجود: " + path;
                if (fi.Length < Size)
                    return "الملف ناقص: حجمه " + Mb(fi.Length) + " ميغابايت والمطلوب " + Mb(Size) + " ميغابايت. النسخ أو التنزيل لم يكتمل، انسخه من جديد.\n" + Ltr(path);
                if (fi.Length > Size)
                    return "هذا ليس الملف المطلوب، حجمه مختلف. المطلوب: " + FileName + "\n" + Ltr(path);
                string hash;
                using (SHA256 sha = SHA256.Create())
                using (FileStream f = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 1 << 16, FileOptions.SequentialScan))
                {
                    byte[] buf = new byte[1 << 20];
                    long done = 0; int n;
                    while ((n = f.Read(buf, 0, buf.Length)) > 0)
                    {
                        sha.TransformBlock(buf, 0, n, null, 0);
                        done += n;
                        if (progress != null) progress(done, Size);
                        if (cancelled != null && cancelled()) return Cancelled;
                    }
                    sha.TransformFinalBlock(buf, 0, 0);
                    hash = BitConverter.ToString(sha.Hash).Replace("-", "");
                }
                if (!string.Equals(hash, Sha256, StringComparison.OrdinalIgnoreCase))
                    return "الملف تالف أو معدّل (لا يطابق ملف مايكروسوفت الأصلي). انسخه من جديد من مصدر سليم.\n" + Ltr(path);
                return null;
            }
            catch (Exception ex) { return "تعذرت قراءة الملف: " + ex.Message; }
        }

        /// <summary>Copies a package (from the network) to this computer. null = done.</summary>
        public string Copy(string source, string dest, Action<long, long> progress, Func<bool> cancelled)
        {
            string tmp = dest + ".copy";
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(dest));
                bool stop = false;
                using (FileStream s = new FileStream(source, FileMode.Open, FileAccess.Read, FileShare.Read, 1 << 16, FileOptions.SequentialScan))
                {
                    long total = s.Length;
                    string space = NoSpace(dest, total);
                    if (space != null) return space;
                    using (FileStream d = new FileStream(tmp, FileMode.Create, FileAccess.Write, FileShare.None))
                    {
                        byte[] buf = new byte[1 << 20];
                        long done = 0; int n;
                        while (!stop && (n = s.Read(buf, 0, buf.Length)) > 0)
                        {
                            d.Write(buf, 0, n);
                            done += n;
                            if (progress != null) progress(done, total);
                            stop = cancelled != null && cancelled();
                        }
                    }
                }
                if (stop) { File.Delete(tmp); return Cancelled; }
                if (File.Exists(dest)) File.Delete(dest);
                File.Move(tmp, dest);
                return null;
            }
            catch (Exception ex)
            {
                try { File.Delete(tmp); } catch { }
                return "تعذر نسخ الملف إلى هذا الحاسوب: " + ex.Message;
            }
        }

        static string NoSpace(string dest, long need)
        {
            try
            {
                need += 50L * 1024 * 1024;
                DriveInfo d = new DriveInfo(Path.GetPathRoot(Path.GetFullPath(dest)));
                if (d.AvailableFreeSpace < need)
                    return "المساحة الفارغة لا تكفي في القرص " + d.Name + " (المطلوب " + Mb(need) + " ميغابايت). فرّغ مساحة ثم أعد المحاولة.";
            }
            catch { }
            return null;
        }

        // ------------------------------------------------------------ download
        /// <summary>The unfinished download of this file (continued by the next download).</summary>
        public static string PartFile(string dest) { return dest + ".part"; }

        /// <summary>Bytes already downloaded by an interrupted or cancelled download.</summary>
        public static long PartSize(string dest)
        {
            try { FileInfo f = new FileInfo(PartFile(dest)); return f.Exists ? f.Length : 0; } catch { return 0; }
        }

        /// <summary>The answer is final: trying again does not help.</summary>
        class Refused : Exception { public Refused(string message) : base(message) { } }
        /// <summary>A network error while reading (told apart from a disk error while writing).</summary>
        class NetError : Exception { public NetError(string message) : base(message) { } }

        HttpWebRequest Request(long from)
        {
            ServicePointManager.SecurityProtocol |= (SecurityProtocolType)3072;   // TLS 1.2 (this process only)
            HttpWebRequest r = (HttpWebRequest)WebRequest.Create(Url);
            r.UserAgent = "EngStores-Setup";                                       // generic name, no personal data
            r.Timeout = StallTimeout; r.ReadWriteTimeout = StallTimeout;
            r.KeepAlive = false;
            if (from > 0) r.AddRange(from);
            return r;
        }

        /// <summary>Downloads the package to dest: continues an earlier unfinished download, reconnects by itself after a
        /// dropped connection, checks the finished file. null = done; Cancelled; otherwise the reason (Arabic).
        /// The unfinished file is kept, so the next call continues it. Runs on a background thread.</summary>
        public string Download(string dest, DownloadProgress progress, Func<bool> cancelled)
        {
            if (cancelled == null) cancelled = delegate { return false; };
            string part = PartFile(dest);
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(dest));
                long have = PartSize(dest);
                if (have > Size) { File.Delete(part); have = 0; }
                string space = NoSpace(dest, Size - have);
                if (space != null) return space;

                Meter meter = new Meter(progress);
                long best = have;   // the furthest point reached: only bytes beyond it are progress
                int failures = 0;
                while (have < Size)
                {
                    if (cancelled()) return Cancelled;
                    string error = "انقطع الاتصال";
                    meter.Report(have, null, failures);
                    try { Fetch(part, have, meter, cancelled); }
                    catch (WebException ex) { error = Reason(ex); }
                    catch (NetError ex) { error = ex.Message; }
                    if (cancelled()) return Cancelled;
                    have = PartSize(dest);
                    if (have >= Size) break;
                    if (have > best) { best = have; failures = 0; } else failures++;
                    if (failures >= MaxFailures)
                        return "تعذر إكمال التنزيل: " + error + ".\nنزل " + Mb(have) + " من أصل " + Mb(Size) + " ميغابايت وهي محفوظة، والتنزيل التالي يُكمل من حيث توقف.";
                    // wait before the next attempt: 1, 2, 4 … seconds
                    int wait = (int)Math.Min(MaxRetryDelay, RetryDelay * Math.Pow(2, Math.Max(0, failures - 1)));
                    for (Stopwatch sw = Stopwatch.StartNew(); sw.ElapsedMilliseconds < wait; )
                    {
                        if (cancelled()) return Cancelled;
                        meter.Report(have, error, failures);
                        Thread.Sleep(Math.Max(1, Math.Min(100, wait)));
                    }
                }
                meter.Report(Size, null, 0);

                string bad = Check(part, null, cancelled);
                if (bad == Cancelled) return Cancelled;
                if (bad != null)
                {
                    File.Delete(part);
                    return "الملف المنزّل لا يطابق ملف مايكروسوفت الأصلي فحُذف. أعد التنزيل، أو استعمل الملف من فلاش.";
                }
                if (File.Exists(dest)) File.Delete(dest);
                File.Move(part, dest);
                return null;
            }
            catch (Refused ex) { return ex.Message; }
            catch (Exception ex) { return "تعذر حفظ الملف المنزّل: " + ex.Message; }
        }

        /// <summary>One connection: continues the part file from «have» until the server ends or drops it.</summary>
        void Fetch(string part, long have, Meter meter, Func<bool> cancelled)
        {
            HttpWebRequest req = Request(have);
            // the user's «إلغاء التنزيل» ends a blocked read at once
            using (Timer watch = new Timer(delegate { if (cancelled()) try { req.Abort(); } catch { } }, null, 100, 100))
            using (HttpWebResponse resp = Response(req, part))
            {
                if (resp == null) return;   // the server cannot continue from there: the part file was dropped, start again
                try
                {
                    if (resp.StatusCode == HttpStatusCode.PartialContent)
                    {
                        long from, total;
                        if (!ContentRange(resp.Headers["Content-Range"], out from, out total) || (total >= 0 && total != Size)) throw new Refused(OtherFile());
                        if (from != have) { File.Delete(part); return; }
                    }
                    else
                    {
                        if (resp.StatusCode != HttpStatusCode.OK || (resp.ContentLength >= 0 && resp.ContentLength != Size)) throw new Refused(OtherFile());
                        have = 0;   // the server does not continue downloads: from the beginning
                    }
                    using (Stream s = resp.GetResponseStream())
                    using (FileStream f = new FileStream(part, FileMode.OpenOrCreate, FileAccess.Write, FileShare.Read))
                    {
                        f.SetLength(have); f.Position = have;
                        byte[] buf = new byte[1 << 16];
                        while (have < Size)
                        {
                            int n;
                            try { n = s.Read(buf, 0, buf.Length); }
                            catch (WebException) { throw; }
                            catch (Exception ex) { throw new NetError(ex is IOException || ex is ObjectDisposedException ? "انقطع الاتصال" : ex.Message); }
                            if (n <= 0) return;
                            if (have + n > Size) { f.SetLength(0); throw new Refused(OtherFile()); }
                            f.Write(buf, 0, n);
                            have += n;
                            meter.Tick(have);
                            if (cancelled()) return;
                        }
                    }
                }
                finally { try { req.Abort(); } catch { } }   // never wait for the rest of an answer that is not wanted
            }
        }

        HttpWebResponse Response(HttpWebRequest req, string part)
        {
            try { return (HttpWebResponse)req.GetResponse(); }
            catch (WebException ex)
            {
                HttpWebResponse r = ex.Response as HttpWebResponse;
                if (r == null) throw;
                int code = (int)r.StatusCode;
                r.Close();
                if (code == 416) { File.Delete(part); return null; }
                if (code == 407) throw new Refused("شبكة الشركة (البروكسي) تطلب كلمة سر للإنترنت. استعمل الملف من فلاش.");
                if (code >= 400 && code < 500 && code != 408 && code != 429) throw new Refused("موقع مايكروسوفت لا يعطي الملف (الرمز " + code + "). استعمل الملف من فلاش.");
                throw new NetError("الموقع مشغول (الرمز " + code + ")");   // try again
            }
        }

        static string OtherFile() { return "الموقع أعاد شيئا غير الملف المطلوب (قد تحتاج شبكتك تسجيل دخول في المتصفح، أو تغيّر الملف في الموقع). استعمل الملف من فلاش."; }

        /// <summary>«bytes from-to/total» (total -1 when the server does not tell it).</summary>
        static bool ContentRange(string h, out long from, out long total)
        {
            from = 0; total = -1;
            Match m = Regex.Match(h ?? "", @"^bytes\s+(\d+)-\d+/(\d+|\*)$");
            if (!m.Success || !long.TryParse(m.Groups[1].Value, out from)) return false;
            return m.Groups[2].Value == "*" || long.TryParse(m.Groups[2].Value, out total);
        }

        static string Reason(WebException ex)
        {
            switch (ex.Status)
            {
                case WebExceptionStatus.NameResolutionFailure:
                case WebExceptionStatus.ProxyNameResolutionFailure:
                case WebExceptionStatus.ConnectFailure: return "لا يوجد اتصال بالإنترنت";
                case WebExceptionStatus.Timeout: return "الموقع لا يستجيب";
                case WebExceptionStatus.SecureChannelFailure:
                case WebExceptionStatus.TrustFailure: return "تعذر الاتصال الآمن (تأكد من تاريخ الحاسوب ووقته)";
                default: return "انقطع الاتصال";
            }
        }

        /// <summary>Reports at most 4 times a second, with the speed of the last seconds.</summary>
        class Meter
        {
            readonly DownloadProgress to;
            readonly Stopwatch sw = Stopwatch.StartNew();
            readonly Queue<KeyValuePair<long, long>> marks = new Queue<KeyValuePair<long, long>>();   // time, bytes
            long lastTick = -1000, lastDone;
            double speed;

            public Meter(DownloadProgress to) { this.to = to; }

            /// <summary>New bytes arrived.</summary>
            public void Tick(long done) { if (sw.ElapsedMilliseconds - lastTick >= 250) Report(done, null, 0); }

            public void Report(long done, string error, int failures)
            {
                long t = lastTick = sw.ElapsedMilliseconds;
                if (error != null || done < lastDone) { marks.Clear(); speed = 0; }
                lastDone = done;
                if (error == null)
                {
                    marks.Enqueue(new KeyValuePair<long, long>(t, done));
                    while (marks.Count > 2 && t - marks.Peek().Key > 8000) marks.Dequeue();
                    KeyValuePair<long, long> first = marks.Peek();
                    if (t - first.Key >= 1000) speed = (done - first.Value) * 1000.0 / (t - first.Key);
                }
                if (to != null) to(done, speed, error, failures);
            }
        }
    }
}
