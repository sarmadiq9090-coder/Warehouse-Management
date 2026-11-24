param([string]$Dir)
$ErrorActionPreference = 'Stop'
$inv = [Globalization.CultureInfo]::InvariantCulture
$xl = New-Object -ComObject Excel.Application
$xl.Visible = $false; $xl.DisplayAlerts = $false
$total = 0; $bad = 0
try {
  foreach ($line in [IO.File]::ReadAllLines((Join-Path $Dir 'index.tsv'), [Text.Encoding]::UTF8)) {
    $key = $line.Split("`t")[0]
    $file = Join-Path $Dir ($key + '.xlsx')
    $exp = [IO.File]::ReadAllLines((Join-Path $Dir ($key + '.expected.tsv')), [Text.Encoding]::UTF8)
    $totFile = Join-Path $Dir ($key + '.totals.tsv')
    $tot = if (Test-Path $totFile) { [IO.File]::ReadAllText($totFile, [Text.Encoding]::UTF8) } else { '' }
    $wb = $null; $errs = New-Object System.Collections.Generic.List[string]; $info = ''
    try {
      $wb = $xl.Workbooks.Open($file, 0, $true)
      $ws = $wb.Worksheets.Item(1)
      $used = $ws.UsedRange
      $nRows = $used.Row + $used.Rows.Count - 1
      $nCols = $exp[0].Split("`t").Count
      $lastRow = 4 + $exp.Count - 1
      $expRows = $lastRow; if ($tot) { $expRows++ }
      if ($nRows -ne $expRows) { $errs.Add("row count: excel last row $nRows, expected $expRows") }
      $vals = $ws.Range($ws.Cells.Item(4, 1), $ws.Cells.Item([Math]::Max($lastRow, 4), $nCols)).Value2
      for ($r = 0; $r -lt $exp.Count -and $errs.Count -lt 8; $r++) {
        $cells = $exp[$r].Split("`t")
        for ($c = 0; $c -lt $nCols; $c++) {
          $e = $cells[$c]; $v = $vals[($r + 1), ($c + 1)]
          $k = $e.Substring(0, 2); $ev = $e.Substring(2)
          $ok = $true
          switch ($k) {
            'e:' { $ok = ($v -eq $null -or [string]$v -eq '') }
            'n:' { $ok = ($v -is [double]) -and [Math]::Abs($v - [double]::Parse($ev, $inv)) -le 1e-9 * [Math]::Max(1, [Math]::Abs($v)) }
            'd:' { $ok = ($v -is [double]) -and [Math]::Abs($v - [double]::Parse($ev, $inv)) -lt 1e-6 }
            't:' { $t = $ev.Replace('\n', "`n").Replace('\r', "`r").Replace('\t', "`t").Replace('\\', '\'); $ok = ([string]$v -eq $t) }
          }
          if (-not $ok) { $errs.Add("row $($r + 4) col $($c + 1): expected [$e] got [$v]") }
        }
      }
      $totInfo = ''
      if ($tot) {
        $tc = $tot.Split("`t")
        for ($c = 0; $c -lt $tc.Count; $c++) {
          if ($tc[$c] -eq '') { continue }
          $cell = $ws.Cells.Item($lastRow + 1, $c + 1); $v = $cell.Value2; $e2 = [double]::Parse($tc[$c], $inv)
          if (-not ($v -is [double]) -or [Math]::Abs($v - $e2) -gt 1e-6 * [Math]::Max(1, [Math]::Abs($e2))) { $errs.Add("total col $($c + 1): expected $e2 got $v") }
          elseif (-not $totInfo) { $totInfo = " total[$($c + 1)]=$v via $($cell.Formula)" }
        }
      }
      $ps = $ws.PageSetup
      $info = "filter=$($ws.AutoFilterMode) orient=$(if ($ps.Orientation -eq 2) {'landscape'} else {'portrait'}) fitWide=$($ps.FitToPagesWide) repeat=$($ps.PrintTitleRows) footer='$($ps.LeftFooter)'$totInfo"
    } catch { $errs.Add("OPEN FAILED: " + $_.Exception.Message) }
    finally { if ($wb) { $wb.Close($false) | Out-Null; [void][Runtime.InteropServices.Marshal]::ReleaseComObject($wb) } }
    $total++
    if ($errs.Count -eq 0) { "PASS  $key ($($exp.Count - 1) rows)  $info" } else { $bad++; "FAIL  $key"; $errs | % { "      $_" } }
  }
} finally { $xl.Quit(); [void][Runtime.InteropServices.Marshal]::ReleaseComObject($xl) }
"checked $total files, $bad with problems"
