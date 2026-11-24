using System.Collections.Generic;
using System.Diagnostics;
using System.IO.Compression;
using System.IO;
using System.Linq;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Web.Script.Serialization;
using System.Windows.Forms;
using System;

namespace Stores
{
    /// <summary>Automatic updates from GitHub Releases (free).
    /// A release has a tag like "v1.2.0" and two files: the program zip and "&lt;zip&gt;.sha256" (its SHA-256).
    /// The update is downloaded, its SHA-256 is checked, and a small script replaces the program files after the
    /// program closes, then starts the new version (settings.ini and the data are never touched).
    /// The repository is fixed in the program itself (not a database setting, so nobody with database access can
    /// point the computers to another source). Empty = updates off.</summary>
    static class Updater
    {
        /// <summary>GitHub repository "owner/name" that publishes the releases. Set when the program is published.</summary>
        public const string Repo = "sarmadiq9090-coder/Warehouse-Management";

        public class Release
        {
            public string Tag, Title, Notes, ZipUrl, ZipName, ShaUrl, PageUrl;
            public Version Ver;
        }

        public static Version Current { get { return Parse(Program.Version); } }

        static Version Parse(string s)
        {
            s = (s ?? "").Trim().TrimStart('v', 'V');
            Version v;
            return Version.TryParse(s.Contains(".") ? s : s + ".0", out v) ? v : new Version(0, 0);
        }

        static HttpWebRequest Request(string url)
        {
            ServicePointManager.SecurityProtocol |= (SecurityProtocolType)3072;   // TLS 1.2 (GitHub requires it)
            HttpWebRequest r = (HttpWebRequest)WebRequest.Create(url);
            r.UserAgent = "EngStores-Updater";                                     // generic name, no personal data
            r.Accept = "application/vnd.github+json";
            r.Timeout = 20000; r.ReadWriteTimeout = 60000;
            return r;
        }

        /// <summary>Latest published release, or null. Throws on network errors.</summary>
        public static Release Latest()
        {
            string repo = Repo;
            if (repo.Length == 0 || !repo.Contains("/")) return null;
            string json;
            try
            {
                using (WebResponse resp = Request("https://api.github.com/repos/" + repo + "/releases/latest").GetResponse())
                using (StreamReader rd = new StreamReader(resp.GetResponseStream(), Encoding.UTF8)) json = rd.ReadToEnd();
            }
            catch (WebException ex)
            {
                HttpWebResponse hr = ex.Response as HttpWebResponse;
                if (hr != null && hr.StatusCode == HttpStatusCode.NotFound) return null;   // no release yet
                throw;
            }
            Dictionary<string, object> o = (Dictionary<string, object>)new JavaScriptSerializer().DeserializeObject(json);
            Release r = new Release();
            r.Tag = Convert.ToString(o["tag_name"]); r.Ver = Parse(r.Tag);
            r.Title = o.ContainsKey("name") ? Convert.ToString(o["name"]) : r.Tag;
            r.Notes = o.ContainsKey("body") ? Convert.ToString(o["body"]) : "";
            r.PageUrl = o.ContainsKey("html_url") ? Convert.ToString(o["html_url"]) : "";
            // only the two files the release workflow makes (scripts\package.ps1), and only from this repository's
            // own release downloads: an extra or renamed asset uploaded by hand is ignored
            string zipName = "EngStores-" + r.Tag.TrimStart('v', 'V') + ".zip";
            string prefix = "https://github.com/" + repo + "/releases/download/";
            foreach (object a in (object[])o["assets"])
            {
                Dictionary<string, object> asset = (Dictionary<string, object>)a;
                string name = Convert.ToString(asset["name"]), url = Convert.ToString(asset["browser_download_url"]);
                if (!url.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) continue;
                if (string.Equals(name, zipName, StringComparison.OrdinalIgnoreCase)) { r.ZipName = name; r.ZipUrl = url; }
                else if (string.Equals(name, zipName + ".sha256", StringComparison.OrdinalIgnoreCase)) r.ShaUrl = url;
            }
            return r;
        }

        // ------------------------------------------------------------ background check at start-up (once a day)
        /// <summary>Checks in the background; calls found(release) on the UI thread when a newer version exists.</summary>
        public static void CheckInBackground(Control ui, Action<Release> found)
        {
            if (Repo.Length == 0) return;
            string today = DateTime.Today.ToString("yyyy-MM-dd");
            string key = "update_checked_" + Environment.MachineName;
            try { if (Db.GetSetting(key, "") == today) return; Db.SetSetting(key, today); } catch { }
            Thread t = new Thread(delegate()
            {
                try
                {
                    Release r = Latest();
                    if (r != null && r.Ver > Current && r.ZipUrl != null && !ui.IsDisposed)
                        ui.BeginInvoke(new Action(delegate { found(r); }));
                }
                catch { }   // offline: try again tomorrow
            });
            t.IsBackground = true; t.Start();
        }

        // ------------------------------------------------------------ the «تحديث البرنامج» button
        public static void CheckNow(IWin32Window owner)
        {
            if (Repo.Length == 0)
            {
                UI.Info("التحديث التلقائي غير مفعّل في هذه النسخة.\nيُفعّل تلقائيا في النسخ المنشورة على GitHub.");
                return;
            }
            Release r;
            UI.WaitCursor(true);
            try { r = Latest(); }
            catch (Exception ex) { UI.WaitCursor(false); UI.Warn("تعذر الاتصال بـ GitHub للبحث عن تحديث.\nتأكد من الإنترنت ثم أعد المحاولة.\n\n" + ex.Message); return; }
            finally { UI.WaitCursor(false); }
            if (r == null || r.Ver <= Current) { UI.Info("البرنامج محدّث ✓\nالإصدار الحالي: " + Program.Version); return; }
            Offer(owner, r);
        }

        public static void Offer(IWin32Window owner, Release r)
        {
            string notes = (r.Notes ?? "").Trim();
            if (notes.Length > 700) notes = notes.Substring(0, 700) + "…";
            if (!UI.Confirm("يتوفر إصدار جديد: " + r.Ver + "   (الحالي: " + Program.Version + ")\n\n" + (notes.Length > 0 ? notes + "\n\n" : "") +
                            "هل تريد التحديث الآن؟\nسيُغلق البرنامج ويُفتح من جديد تلقائيا (البيانات والإعدادات لا تتغير).")) return;
            try { Install(r); }
            catch (Exception ex) { UI.Error(ex); }
        }

        /// <summary>Downloads, verifies and installs the release, then closes the program (the script restarts it).</summary>
        public static void Install(Release r)
        {
            if (r.ZipUrl == null) throw new UserError("ملف التحديث غير موجود في هذا الإصدار.");
            if (r.ShaUrl == null) throw new UserError("الإصدار لا يحتوي بصمة التحقق (sha256)، لذلك لن يُنصّب لحماية البرنامج.");
            string appDir = Settings.AppDir;
            // the program folder must be writable by this user
            try { string probe = Path.Combine(appDir, ".write_test"); File.WriteAllText(probe, "x"); File.Delete(probe); }
            catch { throw new UserError("لا توجد صلاحية كتابة في مجلد البرنامج:\n" + appDir + "\nشغّل البرنامج كمسؤول (Run as administrator) ثم أعد التحديث."); }

            string work = Path.Combine(Path.GetTempPath(), "EngStoresUpdate", r.Ver.ToString());
            if (Directory.Exists(work)) Directory.Delete(work, true);
            Directory.CreateDirectory(work);
            string zip = Path.Combine(work, "update.zip");
            UI.WaitCursor(true);
            try
            {
                using (WebResponse resp = Request(r.ZipUrl).GetResponse())
                using (Stream s = resp.GetResponseStream())
                using (FileStream f = File.Create(zip)) s.CopyTo(f);
                string expected;
                using (WebResponse resp = Request(r.ShaUrl).GetResponse())
                using (StreamReader rd = new StreamReader(resp.GetResponseStream())) expected = rd.ReadToEnd().Trim().Split(' ', '\t', '\r', '\n')[0].ToLowerInvariant();
                string actual;
                using (SHA256 sha = SHA256.Create()) using (FileStream f = File.OpenRead(zip))
                    actual = BitConverter.ToString(sha.ComputeHash(f)).Replace("-", "").ToLowerInvariant();
                if (actual != expected) throw new UserError("ملف التحديث المنزّل غير سليم (البصمة لا تطابق). لم يُنصّب شيء. أعد المحاولة لاحقا.");

                string stage = Path.Combine(work, "files");
                ZipFile.ExtractToDirectory(zip, stage);
                string exeName = Path.GetFileName(Application.ExecutablePath);
                string[] exes = Directory.GetFiles(stage, exeName, SearchOption.AllDirectories);
                if (exes.Length == 0) throw new UserError("ملف التحديث لا يحتوي البرنامج (" + exeName + ").");
                stage = Path.GetDirectoryName(exes[0]);
                // never overwrite local files that belong to this computer
                foreach (string keep in new[] { "settings.ini", "errors.log" })
                {
                    string p = Path.Combine(stage, keep); if (File.Exists(p)) File.Delete(p);
                }

                string script = Path.Combine(work, "apply-update.ps1");
                File.WriteAllText(script, ApplyScript, new UTF8Encoding(true));
                ProcessStartInfo si = new ProcessStartInfo("powershell.exe",
                    "-NoProfile -ExecutionPolicy Bypass -WindowStyle Hidden -File \"" + script + "\" -ProcessId " + Process.GetCurrentProcess().Id +
                    " -Source \"" + stage + "\" -Target \"" + appDir + "\" -Exe \"" + Application.ExecutablePath + "\" -Log \"" + Path.Combine(work, "update.log") + "\"");
                si.UseShellExecute = false; si.CreateNoWindow = true;
                Process.Start(si);
            }
            finally { UI.WaitCursor(false); }
            Db.Log(null, "تحديث البرنامج", Program.Version + " ← " + r.Ver);
            Db.SignOff();
            Environment.Exit(0);
        }

        static string ApplyScript
        {
            get
            {
                using (Stream s = typeof(Updater).Assembly.GetManifestResourceStream("apply-update.ps1"))
                using (StreamReader r = new StreamReader(s, Encoding.UTF8)) return r.ReadToEnd();
            }
        }
    }
}
