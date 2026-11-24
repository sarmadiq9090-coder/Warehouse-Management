using System.Collections.Generic;
using System.Data;
using System.Diagnostics;
using System.Drawing;
using System.Globalization;
using System.IO.Compression;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Windows.Forms;
using System;

namespace Stores
{
    // ================================================================== HTML rendering
    static class Html
    {
        static readonly CultureInfo Inv = CultureInfo.InvariantCulture;
        static string E(string s) { return WebUtility.HtmlEncode(s ?? ""); }

        /// <summary>Extra class for a number cell: zero = grey, negative = red.</summary>
        static string NumClass(object v, ColKind k)
        {
            if (k == ColKind.Date || v == null || v is DBNull || v is string) return "";
            double d; try { d = Convert.ToDouble(v); } catch { return ""; }
            return Math.Abs(d) < 1e-9 ? " z" : d < 0 ? " ng" : "";
        }

        static string Sep(string s) { return (s ?? "").Replace(" — ", "  •  "); }

        static string Cell(object v, ColKind k)
        {
            if (v == null || v is DBNull) return "";
            switch (k)
            {
                case ColKind.Num: return v is string ? E((string)v) : UI.Num(Convert.ToDouble(v));
                case ColKind.Int: return v is string ? E((string)v) : Convert.ToDouble(v).ToString("#,##0", Inv);
                case ColKind.Date: return v is DateTime ? UI.Date((DateTime)v) : E(Convert.ToString(v));
                default: return E(Convert.ToString(v));
            }
        }

        // Clear, calm layout: soft header row, KPI cards, zeros in grey, only problem cells in red.
        const string Css = @"
@page { margin: 12mm 10mm; }
body { font-family: 'Segoe UI', Tahoma, Arial; font-size: 13.5px; line-height: 1.45; color: #1b1b1b; margin: 20px 26px; direction: rtl; background: #fff; }
.hdr { width: 100%; border-bottom: 3px solid #01A552; margin-bottom: 14px; border-collapse: collapse; }
.hdr td { vertical-align: middle; padding: 4px 0 8px 0; }
.hdr img { height: 44px; }
.co { color: #2F56A3; font-size: 17px; font-weight: 600; }
.app { color: #5f6b7a; font-size: 12px; }
.meta { color: #6b7785; font-size: 11.5px; text-align: left; }
h1 { color: #1f3f7a; font-size: 23px; font-weight: 600; margin: 6px 0 2px 0; }
.sub { color: #5f6b7a; margin-bottom: 14px; font-size: 13.5px; }
.chips { margin: 4px 0 16px 0; }
.chip { display: inline-block; min-width: 130px; border: 1px solid #e3e6eb; border-radius: 8px; padding: 8px 16px 9px 16px; margin: 0 0 8px 10px; background: #fafbfc; vertical-align: top; }
.chip .k { display: block; color: #5f6b7a; font-size: 12px; }
.chip b { display: block; color: #1f3f7a; font-size: 21px; font-weight: 600; }
table.t { width: 100%; border-collapse: collapse; }
table.t th { background: #eef3fb; color: #1f3f7a; padding: 10px 8px; font-weight: 600; border-bottom: 2px solid #2F56A3; text-align: center; }
table.t td { padding: 8px 8px; border-bottom: 1px solid #eceef2; }
table.t tr.alt td { background: #fafbfd; }
table.t td.n { text-align: center; white-space: nowrap; direction: ltr; font-variant-numeric: tabular-nums; }
table.t td.z { color: #b0b6be; }
table.t td.ng { color: #c42b1c; font-weight: 600; }
table.t tfoot td { font-weight: 700; font-size: 14px; background: #eef3fb; border-top: 2px solid #2F56A3; border-bottom: 2px solid #2F56A3; padding: 10px 8px; }
table.t tbody tr:hover td { background: #e8f1ff; }
thead { display: table-header-group; }
tfoot { display: table-row-group; }
table.t td { white-space: nowrap; }
table.t td.w { white-space: normal; min-width: 150px; }
tr { page-break-inside: avoid; }
table.t th.s { cursor: pointer; }
table.t th.s:hover { background: #dde8f8; }
table.t th .ar { font-size: 10px; color: #7d93bd; }
.tools { margin: 0 0 12px 0; padding: 10px 12px; background: #fafbfc; border: 1px solid #e3e6eb; border-radius: 8px; }
.tools input { font-family: 'Segoe UI', Tahoma; font-size: 14px; width: 320px; padding: 6px 10px; border: 1px solid #c4ccd8; border-radius: 6px; }
.tools span { color: #6b7785; margin-right: 12px; font-size: 12.5px; }
.info { width: 100%; border-collapse: collapse; margin: 8px 0 14px 0; }
.info td { border: 1px solid #dde2ea; padding: 9px 12px; width: 33%; }
.info td b { color: #1f3f7a; margin-left: 6px; }
.docno { float: left; border: 2px solid #2F56A3; color: #2F56A3; font-size: 16px; font-weight: bold; padding: 4px 14px; margin-top: 6px; }
tr[data-l] { cursor: pointer; }
tr.neg td { background: #fff6f5; }
body.wide { font-size: 12.5px; }
body.wide table.t td, body.wide table.t th { padding: 6px 5px; }
body.wide table.t td.w { min-width: 110px; }
tr.in td { color: #0f7b45; }
tr.open td { font-weight: 600; background: #eef3fb; }
tr.muted td { color: #9aa1aa; }
.foot { margin-top: 14px; color: #444; padding: 10px 12px; background: #fafbfc; border-right: 3px solid #2F56A3; }
.sig { width: 100%; margin-top: 50px; }
.sig td { text-align: center; width: 33%; padding-top: 30px; }
.sig span { border-top: 1px solid #333; padding: 4px 40px 0 40px; }
.chart { margin: 8px 0 18px 0; border-collapse: collapse; }
.chart td { padding: 3px 6px; font-size: 12px; white-space: nowrap; }
.bar { height: 0; border-top-style: solid; border-top-width: 12px; margin: 1px 0; }
.leg { display: inline-block; width: 12px; height: 0; border-top: 12px solid; margin: 0 6px 0 2px; vertical-align: middle; }
@media print {
  body { margin: 0; font-size: 11.5px; }
  .chip { border-radius: 0; padding: 4px 10px; min-width: 0; }
  .chip b { font-size: 15px; }
  table.t th { color: #000; background: #fff; border: 1px solid #555; border-bottom: 2px solid #2F56A3; padding: 5px 4px; }
  table.t td { border: 1px solid #bbb; padding: 4px 5px; }
  table.t td.z { color: #888; }
  table.t tbody tr:hover td { background: transparent; }
  tr.neg td { background: transparent; }
  table.t th .ar { display: none; }
  body.wide { font-size: 10px; }
  body.wide table.t td, body.wide table.t th { padding: 3px 3px; }
  .noprint { display: none; }
}";
        // Search inside the report, click a column header to sort, totals follow the visible rows.
        // Plain ES5 (the report window uses the Internet Explorer 11 engine).
        const string Js = @"
function nrm(s){return (s||'').toLowerCase().replace(/[أإآ]/g,'ا').replace(/ة/g,'ه').replace(/ى/g,'ي');}
function num(s){var v=parseFloat((s||'').replace(/,/g,''));return isNaN(v)?null:v;}
function fmt(v){var r=Math.round(v*1000)/1000;var p=r.toFixed(3).replace(/\.?0+$/,'').split('.');p[0]=p[0].replace(/\B(?=(\d{3})+(?!\d))/g,',');return p.join('.');}
var R=null,TH=null;
function init(){if(R)return;R=[];TH=document.getElementById('hd').cells;var tb=document.getElementById('tb'),i,j;
  for(i=0;i<tb.rows.length;i++){var r=tb.rows[i],c=[];for(j=0;j<r.cells.length;j++)c.push(r.cells[j].textContent);
    R.push({r:r,n:i,c:c,t:null,o:r.className.indexOf('open')>=0,h:false,b:r.className.replace(/\balt\b/,'').replace(/^\s+|\s+$/g,'')});}}
function refresh(){init();var vis=0,a=0,i,c,sums=[];for(c=0;c<TH.length;c++)sums.push(0);
  for(i=0;i<R.length;i++){var x=R[i];if(x.h||x.o)continue;vis++;var cls=x.b+(a++%2==1?(x.b?' ':'')+'alt':'');if(x.r.className!=cls)x.r.className=cls;
    for(c=0;c<TH.length;c++)if(TH[c].getAttribute('data-t')=='1'){var v=num(x.c[c]);if(v!==null)sums[c]+=v;}}
  var cnt=document.getElementById('cnt');if(cnt)cnt.innerText='المعروض: '+fmt(vis)+' من '+fmt(R.length);
  var ft=document.getElementById('ft');if(ft)for(c=0;c<TH.length;c++)if(TH[c].getAttribute('data-t')=='1')ft.cells[c].innerText=fmt(sums[c]);}
function filt(){init();var q=nrm(document.getElementById('q').value).split(' '),i,j;
  for(i=0;i<R.length;i++){var x=R[i];if(x.o)continue;if(x.t===null)x.t=nrm(x.c.join(' '));var ok=true;
    for(j=0;j<q.length;j++)if(q[j]&&x.t.indexOf(q[j])<0){ok=false;break;}
    if(x.h==ok){x.h=!ok;x.r.style.display=ok?'':'none';}}
  refresh();}
function vis(){if(!R)return '';var a=[],ch=false,i;for(i=0;i<R.length;i++){if(R[i].h){ch=true;continue;}if(R[i].n!=i)ch=true;a.push(R[i].n);}return ch?(a.length?a.join(','):'-'):'';}
var sc=-1,sd=1;
function srt(c){init();var tb=document.getElementById('tb'),k=TH[c].getAttribute('data-k'),a=[],pin=[],i;
  sd=(sc==c)?-sd:1;sc=c;
  for(i=0;i<R.length;i++){var x=R[i];if(x.o){pin.push(x);continue;}var s=x.c[c]||'';x.k=k=='n'?num(s):s;if(k=='n'&&x.k===null)x.k=-1e300;a.push(x);}
  a.sort(k=='n'?function(x,y){return (x.k-y.k)*sd;}:function(x,y){return x.k<y.k?-sd:x.k>y.k?sd:0;});
  R=pin.concat(a);var f=document.createDocumentFragment();for(i=0;i<R.length;i++)f.appendChild(R[i].r);tb.appendChild(f);
  for(i=0;i<TH.length;i++){var s=TH[i].getElementsByTagName('span');if(s.length)s[0].innerText=(i==c?(sd>0?' ▲':' ▼'):' ⇅');}
  refresh();}
";
        // Double click on a row: the program opens what the row is about (RowLinks, caught in WebBrowser.Navigating).
        const string JsLink = @"
function dbl(ev){ev=ev||window.event;var e=ev.target||ev.srcElement;while(e&&e.tagName!='TR')e=e.parentNode;var l=e?e.getAttribute('data-l'):null;
  if(l){try{document.selection?document.selection.empty():window.getSelection().removeAllRanges();}catch(x){}location.href='app:'+l;}}
";
        /// <summary>Rows above which search / sort are not offered (the old browser engine gets slow).</summary>
        const int InteractiveMax = 15000;

        static readonly string[] SeriesColors = { "#2F56A3", "#01A552", "#117A65", "#D68910" };

        /// <summary>English: the same page left-to-right (Css mirrors its right / left rules).</summary>
        const string CssLtr = "body{direction:ltr} .meta{text-align:right} .tools span{margin-right:0;margin-left:12px} .info td b{margin-left:0;margin-right:6px} .docno{float:right} .foot{border-right:0;border-left:3px solid #2F56A3}";

        /// <summary>The script with its two texts in English («المعروض: x من y»).</summary>
        static string JsEn()
        {
            return Js.Replace("'المعروض: '", "'" + Lang.T("المعروض: ") + "'").Replace("' من '", "' of '");
        }

        public static string Render(ReportData r)
        {
            r = Lang.Report(r);   // English: translated copy (titles, headers, known cell texts)
            StringBuilder sb = new StringBuilder();
            sb.Append("<!DOCTYPE html><html dir='" + Lang.Dir + "' lang='" + Lang.Code + "'><head><meta charset='utf-8'><meta http-equiv='X-UA-Compatible' content='IE=edge'>");
            bool interactive = r.Info == null && r.Rows.Count > 1 && r.Rows.Count <= InteractiveMax;
            sb.Append("<title>").Append(E(r.Title)).Append("</title><style>").Append(Css).Append(Lang.English ? CssLtr : "").Append("</style>");
            bool links = r.RowLink.Any(l => l != null);
            if (interactive) sb.Append("<script>").Append(Lang.English ? JsEn() : Js).Append("</script>");
            if (links) sb.Append("<script>").Append(JsLink).Append("</script>");
            sb.Append("</head><body").Append(r.Cols.Count >= 9 ? " class='wide'" : "").Append(">");

            // header
            string logo = Theme.LogoDataUri;
            sb.Append("<table class='hdr'><tr><td>");
            if (logo != null) sb.Append("<img src='").Append(logo).Append("'>");
            else if (Program.Company.Length > 0) sb.Append("<div class='co'>").Append(E(Program.Company)).Append("</div>");
            sb.Append("<div class='app'>").Append(E(Program.AppName)).Append("</div></td>");
            sb.Append("<td class='meta'>" + Lang.T("تاريخ الطباعة: ")).Append(DateTime.Now.ToString("yyyy/MM/dd  HH:mm", Inv))
              .Append("<br>" + Lang.T("المستخدم: ")).Append(E(Session.UserName)).Append("</td></tr></table>");

            if (r.Info != null)
            {
                // a document (voucher): title + boxed header fields
                sb.Append("<h1>").Append(E(Sep(r.Title))).Append("</h1>");
                sb.Append("<table class='info'><tr>");
                for (int i = 0; i < r.Info.Count; i++)
                {
                    if (i > 0 && i % 3 == 0) sb.Append("</tr><tr>");
                    sb.Append("<td><b>").Append(E(r.Info[i].Key)).Append(":</b>").Append(E(r.Info[i].Value)).Append("</td>");
                }
                for (int i = r.Info.Count % 3; i > 0 && i < 3; i++) sb.Append("<td></td>");
                sb.Append("</tr></table>");
            }
            else
            {
                sb.Append("<h1>").Append(E(Sep(r.Title))).Append("</h1>");
                if (!string.IsNullOrEmpty(r.Subtitle)) sb.Append("<div class='sub'>").Append(E(Sep(r.Subtitle))).Append("</div>");
            }

            if (r.Summary.Count > 0 || (r.Info == null && r.Rows.Count > 0))
            {
                sb.Append("<div class='chips'>");
                foreach (KeyValuePair<string, string> kv in r.Summary)
                    sb.Append("<span class='chip'><span class='k'>").Append(E(Lang.T(kv.Key))).Append("</span><b>").Append(E(kv.Value)).Append("</b></span>");
                if (r.Info == null && !r.Summary.Any(kv => kv.Key.StartsWith("عدد") || kv.Value == r.Rows.Count.ToString("#,##0", Inv) || kv.Value == r.Rows.Count.ToString()))
                    sb.Append("<span class='chip'><span class='k'>" + Lang.T("عدد السطور") + "</span><b>").Append(r.Rows.Count.ToString("#,##0", Inv)).Append("</b></span>");
                sb.Append("</div>");
            }

            if (r.Chart != null && r.Chart.Count > 0) RenderChart(sb, r);

            if (interactive)
                sb.Append("<div class='tools noprint'>" + Lang.T("بحث داخل التقرير: ") + "<input id='q' onkeyup='filt()' placeholder='" + Lang.T("اكتب رمزا أو اسما أو رقما…") + "'>")
                  .Append("<span id='cnt'>" + Lang.T("المعروض: ")).Append(r.Rows.Count.ToString("#,##0", Inv)).Append(Lang.English ? " of " : " من ").Append(r.Rows.Count.ToString("#,##0", Inv))
                  .Append("</span><span>" + Lang.T("— اضغط على عنوان أي عمود للترتيب") + "</span>" + (links ? "<span>" + Lang.T("— انقر مرتين على أي سطر لعرض المستند") + "</span>" : "") + "</div>");

            sb.Append("<table class='t'><thead><tr id='hd'>");
            for (int c = 0; c < r.Cols.Count; c++)
            {
                RCol col = r.Cols[c];
                string k = col.Kind == ColKind.Text ? "t" : col.Kind == ColKind.Date ? "d" : "n";
                sb.Append("<th data-k='").Append(k).Append("'").Append(col.Total ? " data-t='1'" : "");
                if (interactive) sb.Append(" class='s' onclick='srt(").Append(c).Append(")' title='" + Lang.T("اضغط للترتيب") + "'");
                sb.Append(">").Append(E(col.Header));
                if (interactive) sb.Append("<span class='ar'> ⇅</span>");
                sb.Append("</th>");
            }
            sb.Append("</tr></thead><tbody id='tb'").Append(links ? " ondblclick='dbl(event)'" : "").Append(">");
            // long text columns (names, descriptions) may wrap; all other columns stay on one line
            bool[] wrap = new bool[r.Cols.Count];
            for (int c = 0; c < r.Cols.Count; c++)
            {
                if (r.Cols[c].Kind != ColKind.Text) continue;
                int max = 0;
                foreach (object[] row in r.Rows.Take(3000)) if (c < row.Length && row[c] is string) max = Math.Max(max, ((string)row[c]).Length);
                wrap[c] = max > (r.Cols.Count >= 9 ? 18 : 28);
            }
            double[] totals = new double[r.Cols.Count];
            for (int i = 0; i < r.Rows.Count; i++)
            {
                object[] row = r.Rows[i];
                string cls = r.RowClass[i];
                if (i % 2 == 1) cls = (cls == null ? "" : cls + " ") + "alt";
                string link = i < r.RowLink.Count ? r.RowLink[i] : null;
                sb.Append(cls == null ? "<tr" : "<tr class='" + cls + "'").Append(link == null ? ">" : " data-l='" + E(link) + "'>");
                for (int c = 0; c < r.Cols.Count; c++)
                {
                    object v = c < row.Length ? row[c] : null;
                    ColKind k = r.Cols[c].Kind;
                    bool num = k == ColKind.Num || k == ColKind.Int || k == ColKind.Date;
                    sb.Append(num ? "<td class='n" + NumClass(v, k) + "'>" : wrap[c] ? "<td class='w'>" : "<td>").Append(Cell(v, k)).Append("</td>");
                    if (r.Cols[c].Total && v != null && !(v is DBNull) && !(v is string) && r.RowClass[i] != "open") totals[c] += Convert.ToDouble(v);
                }
                sb.Append("</tr>");
            }
            if (r.Rows.Count == 0) sb.Append("<tr><td colspan='").Append(r.Cols.Count).Append("' style='text-align:center;color:#888;padding:20px'>" + Lang.T("لا توجد بيانات") + "</td></tr>");
            sb.Append("</tbody>");
            if (r.Cols.Any(c => c.Total) && r.Rows.Count > 0)
            {
                sb.Append("<tfoot><tr id='ft'>");
                bool labelDone = false;
                for (int c = 0; c < r.Cols.Count; c++)
                {
                    if (r.Cols[c].Total) sb.Append("<td class='n'>").Append(UI.Num(totals[c])).Append("</td>");
                    else if (!labelDone) { sb.Append("<td>" + Lang.T("المجموع") + "</td>"); labelDone = true; }
                    else sb.Append("<td></td>");
                }
                sb.Append("</tr></tfoot>");
            }
            sb.Append("</table>");
            if (!string.IsNullOrEmpty(r.Footer)) sb.Append("<div class='foot'>").Append(E(r.Footer)).Append("</div>");
            if (r.Signatures)
            {
                string[] sig = r.SigLabels ?? new[] { "المستلم", "أمين المخزن", "المدير" };
                sb.Append("<table class='sig'><tr>");
                foreach (string s in sig) sb.Append("<td><span>").Append(E(Lang.T(s))).Append("</span></td>");
                sb.Append("</tr></table>");
            }
            sb.Append("</body></html>");
            return sb.ToString();
        }

        static void RenderChart(StringBuilder sb, ReportData r)
        {
            double max = 1;
            foreach (KeyValuePair<string, double[]> kv in r.Chart) foreach (double v in kv.Value) max = Math.Max(max, v);
            if (r.ChartSeries != null && r.ChartSeries.Length > 1)
            {
                sb.Append("<div class='chips'>");
                for (int s = 0; s < r.ChartSeries.Length; s++)
                    sb.Append("<span style='margin-left:14px'><span class='leg' style='border-color:").Append(SeriesColors[s % 4]).Append("'></span>").Append(E(r.ChartSeries[s])).Append("</span>");
                sb.Append("</div>");
            }
            sb.Append("<table class='chart' style='width:100%'>");
            foreach (KeyValuePair<string, double[]> kv in r.Chart)
            {
                sb.Append("<tr><td style='width:180px;overflow:hidden'>").Append(E(kv.Key.Length > 40 ? kv.Key.Substring(0, 40) + "…" : kv.Key)).Append("</td><td>");
                for (int s = 0; s < kv.Value.Length; s++)
                {
                    double pct = Math.Max(0.3, kv.Value[s] / max * 85);
                    sb.Append("<div class='bar' style='width:").Append(pct.ToString("0.0", Inv)).Append("%;border-top-color:").Append(SeriesColors[s % 4])
                      .Append("'></div>");
                }
                sb.Append("</td><td class='n' style='width:120px'>");
                sb.Append(string.Join(" / ", kv.Value.Select(v => UI.Num(v)).ToArray()));
                sb.Append("</td></tr>");
            }
            sb.Append("</table>");
        }
    }
}
