using System.Collections.Generic;
using System.IO;
using System.Text;
using System;

namespace Stores
{
    /// <summary>Lists every Arabic string literal of src/**/*.cs and *.ps1 (comments skipped) and compares it with the
    /// English dictionary src/App/lang-en.txt («arabic TAB english» per line).
    ///   build : csc /out:tools\ExtractStrings.exe tools\ExtractStrings.cs src\App\LangKey.cs
    ///   run   : tools\ExtractStrings.exe src src\App\lang-en.txt [missing.txt [unused.txt]]
    /// unused.txt lists dictionary keys no literal uses any more (remove them, except the day names).
    /// missing.txt gets one «arabic TAB» line per literal that has no translation yet: translate the
    /// right side and append the lines to lang-en.txt. Also used by tests\LangTests.cs.</summary>
    static class StringExtractor
    {
        /// <summary>All Arabic literals (decoded), as dictionary keys (LangKey.Core), with the first file that uses each.</summary>
        public static SortedDictionary<string, string> Extract(string srcDir)
        {
            SortedDictionary<string, string> found = new SortedDictionary<string, string>(StringComparer.Ordinal);
            List<string> files = new List<string>(Directory.GetFiles(srcDir, "*.cs", SearchOption.AllDirectories));
            files.AddRange(Directory.GetFiles(srcDir, "*.ps1", SearchOption.AllDirectories));   // progress and error texts shown by the program
            files.Sort(StringComparer.OrdinalIgnoreCase);
            foreach (string f in files)
                foreach (string lit in f.EndsWith(".ps1", StringComparison.OrdinalIgnoreCase) ? PsLiterals(File.ReadAllText(f, Encoding.UTF8)) : Literals(File.ReadAllText(f, Encoding.UTF8)))
                {
                    if (!LangKey.HasArabic(lit)) continue;
                    // HTML / JavaScript / «ERR|…» protocol pieces: each text between markup characters is its own key
                    foreach (string piece in lit.Split(LangKey.Markup))
                    {
                        string k = LangKey.Core(piece);
                        if (k.Length == 0 || !LangKey.HasArabic(k)) continue;
                        if (!found.ContainsKey(k)) found[k] = Path.GetFileName(f);
                    }
                }
            return found;
        }

        /// <summary>The 'single quoted' literals of a PowerShell script (whole-line and end-of-line comments skipped).</summary>
        public static List<string> PsLiterals(string s)
        {
            List<string> res = new List<string>();
            foreach (string raw in s.Split('\n'))
            {
                string line = raw.Trim();
                if (line.StartsWith("#")) continue;
                foreach (System.Text.RegularExpressions.Match m in System.Text.RegularExpressions.Regex.Matches(line, "'([^']*)'|\\s#\\s.*$"))
                    if (m.Groups[1].Success) res.Add(m.Groups[1].Value);
            }
            return res;
        }

        /// <summary>The string literals of a C# 5 source file (regular, verbatim and char literals; comments skipped).</summary>
        public static List<string> Literals(string s)
        {
            List<string> res = new List<string>();
            int i = 0, n = s.Length;
            while (i < n)
            {
                char c = s[i];
                if (c == '/' && i + 1 < n && s[i + 1] == '/') { while (i < n && s[i] != '\n') i++; continue; }
                if (c == '/' && i + 1 < n && s[i + 1] == '*') { int e = s.IndexOf("*/", i + 2); i = e < 0 ? n : e + 2; continue; }
                if (c == '\'')
                {
                    i++; while (i < n && s[i] != '\'') { if (s[i] == '\\') i++; i++; } i++; continue;
                }
                if (c == '@' && i + 1 < n && s[i + 1] == '"')
                {
                    StringBuilder sb = new StringBuilder(); i += 2;
                    while (i < n)
                    {
                        if (s[i] == '"') { if (i + 1 < n && s[i + 1] == '"') { sb.Append('"'); i += 2; continue; } i++; break; }
                        sb.Append(s[i]); i++;
                    }
                    res.Add(sb.ToString()); continue;
                }
                if (c == '"')
                {
                    StringBuilder sb = new StringBuilder(); i++;
                    while (i < n && s[i] != '"' && s[i] != '\n')
                    {
                        if (s[i] == '\\' && i + 1 < n)
                        {
                            char e = s[i + 1]; i += 2;
                            switch (e)
                            {
                                case 'n': sb.Append('\n'); break;
                                case 'r': sb.Append('\r'); break;
                                case 't': sb.Append('\t'); break;
                                case '0': sb.Append('\0'); break;
                                case 'u':
                                    if (i + 4 <= n) { sb.Append((char)Convert.ToInt32(s.Substring(i, 4), 16)); i += 4; }
                                    break;
                                default: sb.Append(e); break;
                            }
                            continue;
                        }
                        sb.Append(s[i]); i++;
                    }
                    i++;
                    res.Add(sb.ToString()); continue;
                }
                i++;
            }
            return res;
        }

        /// <summary>The dictionary file: key → English (keys stored escaped, see LangKey.Unescape).</summary>
        public static Dictionary<string, string> ReadDict(string path)
        {
            Dictionary<string, string> d = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (string line in File.ReadAllLines(path, Encoding.UTF8))
            {
                int t = line.IndexOf('\t');
                if (line.Length == 0 || line[0] == '#' || t <= 0) continue;
                d[LangKey.Unescape(line.Substring(0, t))] = LangKey.Unescape(line.Substring(t + 1));
            }
            return d;
        }

        static int Main(string[] args)
        {
            if (args.Length < 2) { Console.WriteLine("ExtractStrings <srcDir> <lang-en.txt> [missing.txt [unused.txt]]"); return 2; }
            SortedDictionary<string, string> all = Extract(args[0]);
            Dictionary<string, string> dict = File.Exists(args[1]) ? ReadDict(args[1]) : new Dictionary<string, string>();
            List<string> missing = new List<string>();
            foreach (KeyValuePair<string, string> kv in all)
            {
                string en;
                if (!dict.TryGetValue(kv.Key, out en) || en.Trim().Length == 0) missing.Add(LangKey.Escape(kv.Key) + "\t");
            }
            int unused = 0; List<string> unusedKeys = new List<string>();
            foreach (string k in dict.Keys) if (!all.ContainsKey(k)) { unused++; unusedKeys.Add(LangKey.Escape(k)); }
            if (args.Length > 3) File.WriteAllLines(args[3], unusedKeys.ToArray(), new UTF8Encoding(false));
            Console.WriteLine("Arabic literals: " + all.Count + ", translated: " + (all.Count - missing.Count) + ", missing: " + missing.Count +
                ", dictionary entries not used by src: " + unused);
            if (args.Length > 2) File.WriteAllLines(args[2], missing.ToArray(), new UTF8Encoding(false));
            return 0;
        }
    }
}
