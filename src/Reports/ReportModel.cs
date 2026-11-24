using System.Collections.Generic;
using System.Data;
using System.Globalization;
using System.Linq;
using System.Text;
using System;

namespace Stores
{
    enum ColKind { Text, Num, Date, Int }

    class RCol
    {
        public string Header; public ColKind Kind; public bool Total;
        public RCol(string h, ColKind k, bool total) { Header = h; Kind = k; Total = total; }
    }

    class ReportData
    {
        public string Title, Subtitle;
        public List<RCol> Cols = new List<RCol>();
        public List<object[]> Rows = new List<object[]>();
        public List<string> RowClass = new List<string>();
        public List<string> RowLink = new List<string>();        // what a double click on the row opens (see RowLinks), null = nothing
        public List<KeyValuePair<string, string>> Summary = new List<KeyValuePair<string, string>>();
        public List<KeyValuePair<string, double[]>> Chart;     // label -> values (bar chart)
        public string[] ChartSeries;
        public string Footer;
        public bool Signatures;
        public string[] SigLabels;                               // null = المستلم / أمين المخزن / المدير
        public List<KeyValuePair<string, string>> Info;          // document header (vouchers): shown as a boxed grid
        public object Tag;                                       // the rows behind the report (e.g. List<Reports.CriticalRow>), not shown

        public void Col(string h, ColKind k) { Cols.Add(new RCol(h, k, false)); }
        public void ColT(string h) { Cols.Add(new RCol(h, ColKind.Num, true)); }
        public void Row(string cls, params object[] v) { Rows.Add(v); RowClass.Add(cls); RowLink.Add(null); }

        /// <summary>A double click on the row just added opens this (RowLinks.Doc / Item / …).</summary>
        public void Link(string link) { RowLink[RowLink.Count - 1] = link; }

        /// <summary>A copy with only these rows, in this order (rows found / sorted inside the report).</summary>
        public ReportData Subset(List<int> rows)
        {
            ReportData t = (ReportData)MemberwiseClone();
            t.Rows = new List<object[]>(); t.RowClass = new List<string>(); t.RowLink = new List<string>();
            foreach (int i in rows)
                if (i >= 0 && i < Rows.Count) { t.Rows.Add(Rows[i]); t.RowClass.Add(RowClass[i]); t.RowLink.Add(RowLink[i]); }
            if (t.Rows.Count < Rows.Count)
            {
                // the boxes and the chart belong to the whole report
                t.Summary = new List<KeyValuePair<string, string>>(); t.Chart = null;
                t.Subtitle = (string.IsNullOrEmpty(Subtitle) ? "" : Subtitle + " — ") + "المعروض: " + t.Rows.Count + " من " + Rows.Count;
            }
            return t;
        }
        public void Sum(string k, string v) { Summary.Add(new KeyValuePair<string, string>(k, v)); }
    }

    class ReportParams
    {
        public DateTime From = new DateTime(DateTime.Today.Year, 1, 1), To = DateTime.Today;
        public int Store, ItemNo, PersonNo, TypeNo, RType, ProjNo, Top = 50, Months = 6;
        public bool Flag;
        public string Text = "";
        public DateTime ToEx { get { return To.Date.AddDays(1); } }
    }

    [Flags]
    enum RP { None = 0, Dates = 1, AsOf = 2, Store = 4, Item = 8, Person = 16, Type = 32, RType = 64, Top = 128, Text = 256, Flag = 512, ItemReq = 1024, PersonReq = 2048, Since = 4096, Project = 8192, ProjectReq = 16384, Months = 32768 }

    class ReportDef
    {
        public string Key, Title, Group, FlagText, Desc;
        public RP P;
        public Func<ReportParams, ReportData> Run;
        public ReportDef(string key, string group, string title, RP p, string flagText, string desc, Func<ReportParams, ReportData> run)
        { Key = key; Group = group; Title = title; P = p; FlagText = flagText; Desc = desc; Run = run; }
    }
}
