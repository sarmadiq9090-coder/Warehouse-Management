using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing;
using System.IO;
using System.Windows.Forms;
using System;

namespace Stores
{
    /// <summary>Company name and logo, stored in the database (shared by all computers) and set from
    /// «الإعدادات ← الإعدادات العامة». Both are optional: without a logo the program shows the company name.</summary>
    static class Branding
    {
        public const string DefaultCompany = "";   // no company until the owner types one in «الإعدادات العامة»

        static string company; static byte[] logo; static bool loaded;

        /// <summary>Reads name and logo after the database is connected. A legacy "logo.png" next to the program is
        /// moved into the database once, so existing installations keep their logo.</summary>
        public static void Load()
        {
            loaded = true;
            company = Db.GetSetting("company_name", DefaultCompany);
            logo = GetBlob("logo");
            if (logo == null)
            {
                string legacy = Path.Combine(Settings.AppDir, "logo.png");
                if (File.Exists(legacy)) { try { SetLogo(File.ReadAllBytes(legacy)); } catch { } }
            }
            Theme.ResetLogo();
        }

        public static string Company { get { return loaded && company != null ? company : DefaultCompany; } }
        public static byte[] LogoBytes { get { return logo; } }

        public static void SetCompany(string name)
        {
            name = (name ?? "").Trim();
            Db.SetSetting("company_name", name.Length == 0 ? DefaultCompany : name);
            company = name.Length == 0 ? DefaultCompany : name;
        }

        /// <summary>Stores a logo (any common image format; it is resized to at most 1200×300 and saved as PNG). null removes it.</summary>
        public static void SetLogo(byte[] image)
        {
            if (image == null) { SetBlob("logo", null); logo = null; Theme.ResetLogo(); return; }
            byte[] png;
            using (MemoryStream ms = new MemoryStream(image))
            using (Image src = Image.FromStream(ms))
            {
                double k = Math.Min(1.0, Math.Min(1200.0 / src.Width, 300.0 / src.Height));
                int w = Math.Max(1, (int)(src.Width * k)), h = Math.Max(1, (int)(src.Height * k));
                using (Bitmap b = new Bitmap(w, h, PixelFormat.Format32bppArgb))
                {
                    using (Graphics g = Graphics.FromImage(b)) { g.InterpolationMode = InterpolationMode.HighQualityBicubic; g.DrawImage(src, 0, 0, w, h); }
                    using (MemoryStream o = new MemoryStream()) { b.Save(o, ImageFormat.Png); png = o.ToArray(); }
                }
            }
            SetBlob("logo", png);
            logo = png;
            Theme.ResetLogo();
        }

        // ---------------------------------------------------------------- small binary values (base64 text)
        static byte[] GetBlob(string key)
        {
            try
            {
                object o = Db.Scalar("SELECT v FROM app_blob WHERE k = ?", key);
                string s = o == null || o is DBNull ? "" : Convert.ToString(o);
                return s.Length == 0 ? null : Convert.FromBase64String(s);
            }
            catch { return null; }
        }

        static void SetBlob(string key, byte[] data)
        {
            Db.Exec("DELETE FROM app_blob WHERE k = ?", key);
            if (data != null) Db.Exec("INSERT INTO app_blob (k, v) VALUES (?, ?)", key, Convert.ToBase64String(data));
        }
    }
}
