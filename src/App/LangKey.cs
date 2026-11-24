using System.Text;
using System;

namespace Stores
{
    /// <summary>How Arabic texts become dictionary keys (shared by Lang and tools\ExtractStrings.cs):
    /// the key is the text without its leading / trailing spaces, punctuation and symbols («  ● المتصلون الآن: »
    /// → «المتصلون الآن»), so one translation serves every decorated variant.</summary>
    static class LangKey
    {
        /// <summary>Characters that separate translatable pieces inside one literal (HTML, JavaScript, «ERR|text»).</summary>
        public static readonly char[] Markup = { '<', '>', '\'', '|' };

        public static bool IsArabic(char c) { return (c >= '\u0600' && c <= '\u06FF') || (c >= '\u0750' && c <= '\u077F') || (c >= '\uFB50' && c <= '\uFEFF'); }

        public static bool HasArabic(string s)
        {
            if (s == null) return false;
            foreach (char c in s) if (IsArabic(c) && char.IsLetter(c)) return true;
            return false;
        }

        /// <summary>A letter or digit of any script (word characters for matching).</summary>
        public static bool IsWord(char c) { return char.IsLetterOrDigit(c) || (c >= '\u064B' && c <= '\u0652'); }

        static bool Edge(char c) { return !IsWord(c); }

        public static int CoreStart(string s) { int i = 0; while (i < s.Length && Edge(s[i])) i++; return i; }
        public static int CoreEnd(string s) { int j = s.Length; while (j > 0 && Edge(s[j - 1])) j--; return j; }

        public static string Core(string s)
        {
            if (s == null) return "";
            int i = CoreStart(s), j = CoreEnd(s);
            return j > i ? s.Substring(i, j - i) : "";
        }

        public static string Escape(string s)
        {
            return s.Replace("\\", "\\\\").Replace("\t", "\\t").Replace("\r", "\\r").Replace("\n", "\\n");
        }

        public static string Unescape(string s)
        {
            if (s.IndexOf('\\') < 0) return s;
            StringBuilder sb = new StringBuilder(s.Length);
            for (int i = 0; i < s.Length; i++)
            {
                if (s[i] != '\\' || i + 1 >= s.Length) { sb.Append(s[i]); continue; }
                char e = s[++i];
                sb.Append(e == 'n' ? '\n' : e == 't' ? '\t' : e == 'r' ? '\r' : e);
            }
            return sb.ToString();
        }
    }
}
