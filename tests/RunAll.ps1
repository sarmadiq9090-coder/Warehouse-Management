<#
  Runs the automatic tests (the ones CI runs) on a COPY of a data file.

    tests\RunAll.ps1                      demo data: copies tests\demo\empty-template.mdb, fills it with DemoData.exe
    tests\RunAll.ps1 -Data <file.AMR>     a copy of real data (the file itself is only read, never changed)
    tests\RunAll.ps1 -Build               build the program and the tests first (build.bat, tests\build-tests.bat)

  Every test gets its own fresh copy. Exit code 0 = all passed. Logs and reports go to -Work (default: %TEMP%).
  Not run here: Excel export (needs Excel), printing, the UI click test, SQL Server tests.
#>
param(
    [string]$Data = "",
    [string]$Work = "",
    [switch]$Build,
    [int]$TimeoutMinutes = 15
)
$ErrorActionPreference = "Stop"
$tests = $PSScriptRoot
$root = Split-Path $tests -Parent
$bin = Join-Path $tests "bin"
if ($Work -eq "") { $Work = Join-Path $env:TEMP ("engstores-tests-" + (Get-Date -Format "yyyyMMdd-HHmmss")) }
New-Item -ItemType Directory -Force $Work | Out-Null
$Work = (Resolve-Path $Work).Path

if ($Build) {
    & cmd /c "`"$root\build.bat`""
    if ($LASTEXITCODE -ne 0) { Write-Host "BUILD FAILED (program)"; exit 1 }
    & cmd /c "`"$tests\build-tests.bat`""
    if ($LASTEXITCODE -ne 0) { Write-Host "BUILD FAILED (tests)"; exit 1 }
}
foreach ($exe in "CoreTests", "YearCloseTests", "DemoData") {
    if (-not (Test-Path (Join-Path $bin "$exe.exe"))) { Write-Host "missing tests\bin\$exe.exe - run tests\build-tests.bat (or use -Build)"; exit 1 }
}

# runs one program with a time limit; prints its output; returns its exit code
function Invoke-Test([string]$name, [string[]]$arguments) {
    $log = Join-Path $Work "$name.log"
    $quoted = ($arguments | ForEach-Object { '"' + $_ + '"' }) -join " "
    $p = Start-Process -FilePath (Join-Path $bin "$name.exe") -ArgumentList $quoted -NoNewWindow -PassThru `
        -RedirectStandardOutput $log -RedirectStandardError "$log.err"
    $null = $p.Handle   # keeps the exit code readable after the process ends
    if (-not $p.WaitForExit($TimeoutMinutes * 60 * 1000)) {
        try { $p.Kill() } catch { }
        Write-Host "TIMEOUT: $name did not finish in $TimeoutMinutes minutes"
        $code = 99
    } else { $p.WaitForExit(); $code = $p.ExitCode }
    foreach ($f in $log, "$log.err") { if (Test-Path $f) { Get-Content $f -Encoding UTF8 | ForEach-Object { Write-Host $_ } } }
    return $code
}

# 1. the base data file for this run
$base = Join-Path $Work "base.mdb"
if ($Data -ne "") {
    if (-not (Test-Path -LiteralPath $Data)) { Write-Host "data file not found: $Data"; exit 1 }
    # read-only copy (the original may be open on other computers)
    $in = [System.IO.File]::Open((Resolve-Path -LiteralPath $Data).Path, 'Open', 'Read', 'ReadWrite,Delete')
    $out = [System.IO.File]::Create($base)
    try { $in.CopyTo($out) } finally { $out.Close(); $in.Close() }
    Write-Host "data: copy of $Data"
} else {
    $template = Join-Path $tests "demo\empty-template.mdb"
    $code = Invoke-Test "DemoData" @($template, $base)
    if ($code -ne 0) { Write-Host "DEMO DATA FAILED ($code)"; exit 1 }
}

# 2. the tests, each on a fresh copy
$results = [ordered]@{}
foreach ($t in "CoreTests", "YearCloseTests") {
    Write-Host ""
    Write-Host "==================== $t"
    $copy = Join-Path $Work "$t.mdb"
    $outDir = Join-Path $Work $t
    New-Item -ItemType Directory -Force $outDir | Out-Null
    Copy-Item -LiteralPath $base -Destination $copy -Force
    $results[$t] = Invoke-Test $t @($copy, $outDir)
}

Write-Host ""
Write-Host "==================== summary  (logs: $Work)"
$failed = 0
foreach ($k in $results.Keys) {
    $ok = $results[$k] -eq 0
    if (-not $ok) { $failed++ }
    Write-Host ("{0,-16} {1}" -f $k, $(if ($ok) { "PASSED" } else { "FAILED (exit code " + $results[$k] + ")" }))
}
if ($failed -gt 0) { exit 1 }
exit 0
