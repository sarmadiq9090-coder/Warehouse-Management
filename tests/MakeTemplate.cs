using System;
using System.Collections.Generic;
using System.Data;
using System.Data.OleDb;
using System.IO;
using System.Text;

namespace Stores
{
    /// <summary>
    /// Builds tests/demo/empty-template.mdb: the exact structure of a real data file with NO data in it.
    ///   MakeTemplate.exe &lt;copy of a real data file&gt; &lt;output.mdb&gt; [schema-dump.txt]
    /// The copy gets the program's own tables (Db.EnsureAppTables). Then a NEW empty file is built from scratch
    /// (Jet DDL + ADOX): same tables, columns (types, sizes, required, defaults, AutoNumber, zero-length, validation),
    /// indexes, relationships and saved queries. Nothing else is copied - no rows, no document properties
    /// (author / company / title), no VBA project, no Access UI objects. (Deleting the rows and compacting the
    /// copy is NOT enough: the file keeps the original document properties and VBA project.)
    /// Checks: the schema dump of the copy and of the template are identical, every table is empty, and a byte
    /// search of the template for real values sampled from the copy (names, codes, notes - UTF-16LE, UTF-8,
    /// Windows-1256) finds nothing beyond the schema text itself. The values are kept in memory, never printed.
    /// Exit code 0 = clean template.
    /// </summary>
    static class MakeTemplateMain
    {
        static readonly string[][] Probe =
        {
            new[] { "person", "p_name" }, new[] { "person", "p_tel" }, new[] { "person", "p_addrs" }, new[] { "person", "p_note" },
            new[] { "item", "item_name" }, new[] { "item", "item_code" },
            new[] { "stor", "stor_name" }, new[] { "type", "type_name" }, new[] { "group", "group_name" },
            new[] { "pass", "user_name" }, new[] { "sale", "note" }, new[] { "shop", "note" }, new[] { "trans", "notes" },
            new[] { "excel1", "Describtion" }, new[] { "excel1", "Refrence No" }
        };

        // column / table properties copied to the template (the AutoNumber seed is not: it restarts at 1)
        static readonly string[] ColProps = { "Autoincrement", "Default", "Nullable", "Description",
            "Jet OLEDB:Allow Zero Length", "Jet OLEDB:Compressed UNICODE Strings", "Jet OLEDB:Column Validation Rule",
            "Jet OLEDB:Column Validation Text", "Jet OLEDB:Hyperlink" };
        static readonly string[] CreateProps = { "Autoincrement", "Default", "Jet OLEDB:Compressed UNICODE Strings" };
        static readonly string[] TableProps = { "Jet OLEDB:Table Validation Rule", "Jet OLEDB:Table Validation Text" };

        static string Jet(string path) { return "Provider=Microsoft.Jet.OLEDB.4.0;Data Source=" + path; }

        [STAThread]
        static int Main(string[] args)
        {
            Console.OutputEncoding = Encoding.UTF8;
            if (args.Length < 2) { Console.WriteLine("usage: MakeTemplate <copy.mdb> <output.mdb> [schema-dump.txt]"); return 2; }
            string copy = Path.GetFullPath(args[0]), output = Path.GetFullPath(args[1]);
            string raw = output + ".raw.mdb";
            foreach (string f in new[] { output, raw }) if (File.Exists(f)) File.Delete(f);

            string err = Db.InitAccess(copy);
            if (err != null) { Console.WriteLine("open failed: " + err); return 1; }

            // 1. real values to look for afterwards (memory only)
            List<string> values = new List<string>(), origin = new List<string>();
            foreach (string[] p in Probe)
            {
                if (!Db.TableExists(p[0]) || !Db.ColumnExists("[" + p[0] + "]", "[" + p[1] + "]")) continue;
                DataTable t = Db.Query("SELECT DISTINCT [" + p[1] + "] FROM [" + p[0] + "]");
                int step = Math.Max(1, t.Rows.Count / 60), n = 0;
                for (int i = 0; i < t.Rows.Count; i += step)
                {
                    string s = Db.S(t.Rows[i][0]).Trim();
                    if (s.Length >= 4 && s.Length <= 120) { values.Add(s); origin.Add(p[0] + "." + p[1]); n++; }
                }
                Console.WriteLine("sample " + p[0] + "." + p[1] + ": " + n + " values");
            }

            // 2. the program's own tables / columns
            Db.EnsureAppTables();
            Console.WriteLine("program tables: " + (Db.HasProj ? "ok" : "MISSING") + (Db.SchemaWarning != null ? " (" + Db.SchemaWarning + ")" : ""));
            Db.Close();
            OleDbConnection.ReleaseObjectPool();

            // 3. new file from scratch, structure only
            StringBuilder schemaText = new StringBuilder();
            Clone(copy, raw, schemaText);
            GC.Collect(); GC.WaitForPendingFinalizers();
            OleDbConnection.ReleaseObjectPool();
            Type jt = Type.GetTypeFromProgID("JRO.JetEngine");
            object je = Activator.CreateInstance(jt);
            jt.InvokeMember("CompactDatabase", System.Reflection.BindingFlags.InvokeMethod, null, je,
                new object[] { Jet(raw), Jet(output) + ";Jet OLEDB:Engine Type=5" });
            File.Delete(raw);
            Console.WriteLine("template: " + new FileInfo(output).Length.ToString("#,##0") + " bytes");

            // 4. same structure?
            string a = Dump(copy), b = Dump(output);
            if (args.Length > 2) File.WriteAllText(args[2], b, new UTF8Encoding(true));
            bool same = a == b;
            if (!same)
            {
                string[] la = a.Split('\n'), lb = b.Split('\n');
                HashSet<string> sb = new HashSet<string>(lb), sa = new HashSet<string>(la);
                foreach (string l in la) if (!sb.Contains(l)) Console.WriteLine("  only in copy:     " + l);
                foreach (string l in lb) if (!sa.Contains(l)) Console.WriteLine("  only in template: " + l);
            }
            Console.WriteLine("structure identical: " + same + " (" + a.Split('\n').Length + " schema lines)");

            // 5. every table empty
            int bad = 0, tables = 0;
            using (OleDbConnection c = new OleDbConnection(Jet(output) + ";Mode=Read;"))
            {
                c.Open();
                foreach (DataRow r in c.GetOleDbSchemaTable(OleDbSchemaGuid.Tables, new object[] { null, null, null, "TABLE" }).Rows)
                {
                    tables++;
                    string t = Convert.ToString(r["TABLE_NAME"]);
                    int n = Convert.ToInt32(new OleDbCommand("SELECT COUNT(*) FROM [" + t + "]", c).ExecuteScalar());
                    if (n != 0) { Console.WriteLine("NOT EMPTY: " + t + " = " + n); bad++; }
                }
            }
            OleDbConnection.ReleaseObjectPool();
            Console.WriteLine("tables: " + tables + ", all empty: " + (bad == 0));

            // 6. no real value anywhere in the file bytes (unless it is part of the schema text, e.g. a field description)
            byte[] file = File.ReadAllBytes(output);
            string schema = schemaText.ToString() + "\n" + b;
            Encoding[] encs = { Encoding.Unicode, Encoding.UTF8, Encoding.GetEncoding(1256) };
            int found = 0, searched = 0, inSchema = 0;
            HashSet<string> seen = new HashSet<string>();
            for (int k = 0; k < values.Count; k++)
            {
                string v = values[k];
                if (!seen.Add(v)) continue;
                foreach (Encoding e in encs)
                {
                    searched++;
                    int at = IndexOf(file, e.GetBytes(v));
                    if (at < 0) continue;
                    if (schema.Contains(v)) { inSchema++; continue; }
                    found++;
                    Console.WriteLine("  FOUND a value from " + origin[k] + " (" + e.WebName + ", " + v.Length + " chars) at offset 0x" + at.ToString("X"));
                }
            }
            Console.WriteLine("byte search: " + seen.Count + " real values x " + encs.Length + " encodings = " + searched + " searches, found " + found +
                              (inSchema > 0 ? " (" + inSchema + " generic words that are part of a field description were ignored)" : ""));
            foreach (string k in new[] { "Author", "Company", "Title", "VBA", "DPB=" })
                if (IndexOf(file, Encoding.Unicode.GetBytes(k)) >= 0 || IndexOf(file, Encoding.ASCII.GetBytes(k)) >= 0) { Console.WriteLine("  document property / VBA marker present: " + k); found++; }
            bool ok = same && bad == 0 && found == 0 && seen.Count > 0;
            Console.WriteLine(ok ? "TEMPLATE CLEAN" : "TEMPLATE NOT CLEAN");
            return ok ? 0 : 1;
        }

        // ------------------------------------------------------------ clone (structure only)
        static void Clone(string src, string dst, StringBuilder text)
        {
            dynamic sc = Activator.CreateInstance(Type.GetTypeFromProgID("ADOX.Catalog"));
            sc.ActiveConnection = Jet(src) + ";Mode=Read;";
            dynamic dc = Activator.CreateInstance(Type.GetTypeFromProgID("ADOX.Catalog"));
            dc.Create(Jet(dst) + ";Jet OLEDB:Engine Type=5");
            try { ((dynamic)dc.ActiveConnection).Close(); } catch { }
            Type tIdx = Type.GetTypeFromProgID("ADOX.Index"), tKey = Type.GetTypeFromProgID("ADOX.Key");
            List<dynamic> userTables = new List<dynamic>();
            foreach (dynamic t in sc.Tables) if ((string)t.Type == "TABLE") userTables.Add(t);

            // tables and columns: Jet DDL (types, sizes, required, defaults, AutoNumber, unicode compression)
            using (OleDbConnection s = new OleDbConnection(Jet(src) + ";Mode=Read;"))
            using (OleDbConnection d = new OleDbConnection(Jet(dst)))
            {
                s.Open(); d.Open();
                foreach (dynamic st in userTables)
                {
                    string name = st.Name;
                    text.Append(name).Append('\n');
                    DataView dv = new DataView(s.GetOleDbSchemaTable(OleDbSchemaGuid.Columns, new object[] { null, null, name, null }));
                    dv.Sort = "ORDINAL_POSITION";
                    List<string> cols = new List<string>();
                    foreach (DataRowView r in dv)
                    {
                        string cn = Convert.ToString(r["COLUMN_NAME"]);
                        dynamic scol = st.Columns[cn];
                        bool auto = Convert.ToBoolean(scol.Properties["Autoincrement"].Value);
                        bool comp = false; try { comp = Convert.ToBoolean(scol.Properties["Jet OLEDB:Compressed UNICODE Strings"].Value); } catch { }
                        int dt = Convert.ToInt32(r["DATA_TYPE"]); long flags = Convert.ToInt64(r["COLUMN_FLAGS"]);
                        string type;
                        switch (dt)
                        {
                            case 3: type = auto ? "COUNTER" : "LONG"; break;
                            case 2: type = "SHORT"; break;
                            case 17: type = "BYTE"; break;
                            case 4: type = "REAL"; break;
                            case 5: type = "DOUBLE"; break;
                            case 6: type = "CURRENCY"; break;
                            case 7: type = "DATETIME"; break;
                            case 11: type = "BIT"; break;
                            case 72: type = "GUID"; break;
                            case 128: type = (flags & 0x80) != 0 ? "LONGBINARY" : "BINARY(" + r["CHARACTER_MAXIMUM_LENGTH"] + ")"; break;
                            case 130: type = (flags & 0x80) != 0 ? "MEMO" : "TEXT(" + r["CHARACTER_MAXIMUM_LENGTH"] + ")"; break;
                            default: throw new Exception("unknown column type " + dt + " in " + name + "." + cn);
                        }
                        string def = Convert.ToString(r["COLUMN_DEFAULT"]);
                        cols.Add("[" + cn + "] " + type + (comp ? " WITH COMPRESSION" : "") +
                                 (!auto && dt != 11 && !Convert.ToBoolean(r["IS_NULLABLE"]) ? " NOT NULL" : "") +   // (Yes/No is not "required" in Access)
                                 (def.Length > 0 && !auto ? " DEFAULT " + def : ""));
                        text.Append(cn).Append('\n');
                    }
                    string ddl = "CREATE TABLE [" + name + "] (" + string.Join(", ", cols.ToArray()) + ")";
                    try { new OleDbCommand(ddl, d).ExecuteNonQuery(); }
                    catch (Exception ex) { throw new Exception(ddl + ": " + ex.Message, ex); }
                }
            }
            OleDbConnection.ReleaseObjectPool();
            dc = Activator.CreateInstance(Type.GetTypeFromProgID("ADOX.Catalog"));
            dc.ActiveConnection = Jet(dst);

            // the other properties (zero-length, validation, description) are set on the existing columns
            foreach (dynamic st in userTables)
            {
                dynamic nt = dc.Tables[(string)st.Name];
                foreach (string pn in TableProps) CopyProp(st, nt, pn, text);
                foreach (dynamic scol in st.Columns)
                    foreach (string pn in ColProps)
                        if (Array.IndexOf(CreateProps, pn) < 0) CopyProp(scol, nt.Columns[(string)scol.Name], pn, text);
            }

            // indexes (a relationship creates its own index with the relationship's name: those come with the keys)
            foreach (dynamic st in userTables)
            {
                HashSet<string> fkNames = new HashSet<string>();
                foreach (dynamic k in st.Keys) if ((int)k.Type == 2) fkNames.Add((string)k.Name);
                dynamic nt = dc.Tables[(string)st.Name];
                foreach (dynamic si in st.Indexes)
                {
                    if (fkNames.Contains((string)si.Name)) continue;
                    dynamic ni = Activator.CreateInstance(tIdx);
                    ni.Name = si.Name;
                    ni.PrimaryKey = si.PrimaryKey;
                    ni.Unique = si.Unique;
                    ni.IndexNulls = si.IndexNulls;
                    foreach (dynamic ic in si.Columns) { ni.Columns.Append((string)ic.Name); ni.Columns[(string)ic.Name].SortOrder = ic.SortOrder; }
                    nt.Indexes.Append(ni);
                }
            }

            // relationships
            foreach (dynamic st in userTables)
            {
                dynamic nt = dc.Tables[(string)st.Name];
                foreach (dynamic k in st.Keys)
                {
                    if ((int)k.Type != 2) continue;
                    dynamic nk = Activator.CreateInstance(tKey);
                    nk.Name = k.Name;
                    nk.Type = 2;
                    nk.RelatedTable = k.RelatedTable;
                    nk.UpdateRule = k.UpdateRule;
                    nk.DeleteRule = k.DeleteRule;
                    foreach (dynamic kc in k.Columns) { nk.Columns.Append((string)kc.Name); nk.Columns[(string)kc.Name].RelatedColumn = kc.RelatedColumn; }
                    nt.Keys.Append(nk);
                }
            }

            // saved queries (action queries are procedures, select queries views)
            foreach (dynamic p in sc.Procedures)
            {
                dynamic cmd = Activator.CreateInstance(Type.GetTypeFromProgID("ADODB.Command"));
                cmd.CommandText = p.Command.CommandText;
                dc.Procedures.Append((string)p.Name, cmd);
                text.Append((string)p.Command.CommandText).Append('\n');
            }
            foreach (dynamic v in sc.Views)
            {
                dynamic cmd = Activator.CreateInstance(Type.GetTypeFromProgID("ADODB.Command"));
                cmd.CommandText = v.Command.CommandText;
                dc.Views.Append((string)v.Name, cmd);
                text.Append((string)v.Command.CommandText).Append('\n');
            }
            object conn = dc.ActiveConnection;
            try { ((dynamic)conn).Close(); } catch { }
            try { ((dynamic)sc.ActiveConnection).Close(); } catch { }
            System.Runtime.InteropServices.Marshal.FinalReleaseComObject(dc);
            System.Runtime.InteropServices.Marshal.FinalReleaseComObject(sc);
        }

        static void CopyProp(dynamic from, dynamic to, string name, StringBuilder text)
        {
            object v;
            try { v = from.Properties[name].Value; } catch { return; }
            if (v == null || v is DBNull || (v is string && ((string)v).Length == 0)) return;
            if (name == "Nullable") return;   // given by Attributes
            if (v is bool && !(bool)v && name != "Jet OLEDB:Allow Zero Length") return;   // false is the default
            try { to.Properties[name].Value = v; } catch (Exception ex) { Console.WriteLine("  property " + name + ": " + ex.Message); }
            if (v is string) text.Append((string)v).Append('\n');
        }

        // ------------------------------------------------------------ schema dump used for the comparison
        static string Dump(string path)
        {
            List<string> lines = new List<string>();
            using (OleDbConnection c = new OleDbConnection(Jet(path) + ";Mode=Read;"))
            {
                c.Open();
                foreach (DataRow r in c.GetOleDbSchemaTable(OleDbSchemaGuid.Tables, new object[] { null, null, null, "TABLE" }).Rows)
                    lines.Add("T " + r["TABLE_NAME"]);
                foreach (DataRow r in c.GetOleDbSchemaTable(OleDbSchemaGuid.Columns, null).Rows)
                {
                    string t = Convert.ToString(r["TABLE_NAME"]);
                    if (t.StartsWith("MSys")) continue;
                    lines.Add("C " + t + "." + r["COLUMN_NAME"] + " #" + r["ORDINAL_POSITION"] + " type=" + r["DATA_TYPE"] + " len=" + r["CHARACTER_MAXIMUM_LENGTH"] +
                              " null=" + r["IS_NULLABLE"] + " default=" + r["COLUMN_DEFAULT"] + " flags=" + r["COLUMN_FLAGS"] + " desc=" + r["DESCRIPTION"]);
                }
                foreach (DataRow r in c.GetOleDbSchemaTable(OleDbSchemaGuid.Indexes, null).Rows)
                {
                    string t = Convert.ToString(r["TABLE_NAME"]);
                    if (t.StartsWith("MSys")) continue;
                    lines.Add("I " + t + " " + r["INDEX_NAME"] + " " + r["ORDINAL_POSITION"] + ":" + r["COLUMN_NAME"] + " pk=" + r["PRIMARY_KEY"] + " unique=" + r["UNIQUE"] +
                              " nulls=" + r["NULLS"] + " collation=" + r["COLLATION"]);
                }
                foreach (DataRow r in c.GetOleDbSchemaTable(OleDbSchemaGuid.Foreign_Keys, null).Rows)
                    lines.Add("F " + r["FK_NAME"] + " " + r["PK_TABLE_NAME"] + "." + r["PK_COLUMN_NAME"] + " <- " + r["FK_TABLE_NAME"] + "." + r["FK_COLUMN_NAME"] +
                              " update=" + r["UPDATE_RULE"] + " delete=" + r["DELETE_RULE"]);
                foreach (DataRow r in c.GetOleDbSchemaTable(OleDbSchemaGuid.Procedures, null).Rows)
                    lines.Add("P " + r["PROCEDURE_NAME"] + ": " + Convert.ToString(r["PROCEDURE_DEFINITION"]).Replace("\r", " ").Replace("\n", " "));
                foreach (DataRow r in c.GetOleDbSchemaTable(OleDbSchemaGuid.Views, null).Rows)
                    lines.Add("V " + r["TABLE_NAME"] + ": " + Convert.ToString(r["VIEW_DEFINITION"]).Replace("\r", " ").Replace("\n", " "));
            }
            // ADOX-only column properties (AutoNumber, zero-length, validation)
            dynamic cat = Activator.CreateInstance(Type.GetTypeFromProgID("ADOX.Catalog"));
            cat.ActiveConnection = Jet(path) + ";Mode=Read;";
            foreach (dynamic t in cat.Tables)
            {
                if ((string)t.Type != "TABLE") continue;
                foreach (dynamic col in t.Columns)
                {
                    StringBuilder sb = new StringBuilder("A " + t.Name + "." + col.Name);
                    foreach (string pn in ColProps)
                    {
                        object v; try { v = col.Properties[pn].Value; } catch { continue; }
                        sb.Append(" ").Append(pn).Append("=").Append(Convert.ToString(v));
                    }
                    lines.Add(sb.ToString());
                }
            }
            try { ((dynamic)cat.ActiveConnection).Close(); } catch { }
            System.Runtime.InteropServices.Marshal.FinalReleaseComObject(cat);
            OleDbConnection.ReleaseObjectPool();
            lines.Sort(StringComparer.Ordinal);
            return string.Join("\n", lines.ToArray());
        }

        static int IndexOf(byte[] hay, byte[] needle)
        {
            if (needle.Length == 0) return -1;
            for (int i = 0; i <= hay.Length - needle.Length; i++)
            {
                if (hay[i] != needle[0]) continue;
                int j = 1;
                while (j < needle.Length && hay[i + j] == needle[j]) j++;
                if (j == needle.Length) return i;
            }
            return -1;
        }
    }
}
