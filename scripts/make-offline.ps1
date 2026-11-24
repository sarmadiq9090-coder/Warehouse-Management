<#
  Builds the complete offline folder «نسخة كاملة بدون إنترنت»: copy it as it is to a flash drive; on the new computer
  «السيرفر الرئيسي» then installs SQL Server without internet.

    scripts\make-offline.ps1                                   → dist\نسخة كاملة بدون إنترنت
    scripts\make-offline.ps1 -Out "E:\نسخة كاملة بدون إنترنت" -Package "D:\...\SQLEXPR_x64_ENU.exe"

  Run build.bat first. The folder holds only: the program .exe, the two guide files, docs\manual and
  مكونات\SQLEXPR_x64_ENU.exe (Microsoft's package, checked against the size and SHA-256 in src\Server\SqlPackage.cs;
  default -Package: مكونات\SQLEXPR_x64_ENU.exe in this repository's folder).
  Never: settings.ini, data files, logs, logo.png, backups. A folder that holds anything else (a program folder in use)
  is refused; nothing is ever deleted.
  The folder stays inside the company: the package is not published (scripts\package.ps1 never includes it).
#>
param(
    [string]$Out = "",
    [string]$Package = ""
)
$ErrorActionPreference = "Stop"
$root = Split-Path $PSScriptRoot -Parent
function Refuse([string]$m) { Write-Host "REFUSED: $m"; exit 1 }

# full paths (PowerShell and .NET resolve relative paths against different folders)
if ($Out -eq "") { $Out = Join-Path $root "dist\نسخة كاملة بدون إنترنت" } elseif (-not [IO.Path]::IsPathRooted($Out)) { $Out = Join-Path (Get-Location).ProviderPath $Out }
if ($Package -eq "") { $Package = Join-Path $root "مكونات\SQLEXPR_x64_ENU.exe" } elseif (-not [IO.Path]::IsPathRooted($Package)) { $Package = Join-Path (Get-Location).ProviderPath $Package }
$Out = [IO.Path]::GetFullPath($Out).TrimEnd('\')
$Package = [IO.Path]::GetFullPath($Package)

# 1. the package: exactly Microsoft's file
$src = [IO.File]::ReadAllText((Join-Path $root "src\Server\SqlPackage.cs"))
$name = [regex]::Match($src, 'const string FileName = "([^"]+)"').Groups[1].Value
$size = [long][regex]::Match($src, 'OfficialSize = (\d+)').Groups[1].Value
$hash = [regex]::Match($src, 'OfficialSha256 = "([0-9A-F]{64})"').Groups[1].Value
if (-not $name -or -not $size -or -not $hash) { Refuse "size / SHA-256 not found in src\Server\SqlPackage.cs" }
function Test-Pkg([string]$f) {
    if (-not (Test-Path -LiteralPath $f -PathType Leaf)) { return "the package does not exist: $f" }
    $len = (Get-Item -LiteralPath $f).Length
    if ($len -ne $size) { return "the package is incomplete or another file ($len bytes instead of $size): $f" }
    if ((Get-FileHash -LiteralPath $f -Algorithm SHA256).Hash -ne $hash) { return "the package is damaged (SHA-256 differs): $f" }
    return ""
}
$bad = Test-Pkg $Package
if ($bad) { Refuse $bad }

# 2. what goes into the folder (a fixed list: nothing else can slip in)
$list = New-Object System.Collections.Generic.List[string]
foreach ($f in "Inventory Management.exe", "دليل التشغيل.txt", "إعداد الخادم المجاني.txt", "docs\manual\index.html") {
    if (-not (Test-Path -LiteralPath (Join-Path $root $f) -PathType Leaf)) { Refuse "missing: $f (run build.bat first)" }
    $list.Add($f)
}
foreach ($f in Get-ChildItem -LiteralPath (Join-Path $root "docs\manual\img") -File) { if ($f.Extension -eq ".png") { $list.Add("docs\manual\img\" + $f.Name) } }
$pkgRel = "مكونات\$name"
# the setup program (installer\build-setup.bat), when it was built from this exe: it installs from the flash drive and
# replaces an older copy automatically
$setupName = "Inventory Management Setup.exe"
$setup = Join-Path $root "dist\$setupName"
$hasSetup = (Test-Path -LiteralPath $setup -PathType Leaf) -and ((Get-Item -LiteralPath $setup).LastWriteTime -ge (Get-Item -LiteralPath (Join-Path $root "Inventory Management.exe")).LastWriteTime)

# 3. the target: new, empty, or an earlier offline folder
if ($Out -eq $root -or $root.StartsWith($Out + "\", [StringComparison]::OrdinalIgnoreCase)) { Refuse "choose another folder: $Out" }
if (Test-Path -LiteralPath $Out) {
    if (-not (Test-Path -LiteralPath $Out -PathType Container)) { Refuse "not a folder: $Out" }
    $allowed = New-Object System.Collections.Generic.HashSet[string] ([StringComparer]::OrdinalIgnoreCase)
    foreach ($f in $list) { [void]$allowed.Add($f) }
    [void]$allowed.Add($pkgRel); [void]$allowed.Add($pkgRel + ".new"); [void]$allowed.Add($setupName)
    foreach ($f in Get-ChildItem -LiteralPath $Out -Recurse -File -Force) {
        $rel = $f.FullName.Substring($Out.Length).TrimStart('\')
        $oldManual = $rel -like "docs\manual\*" -and (".png", ".html" -contains $f.Extension) -and $f.Name -ne "logo.png"
        if (-not $allowed.Contains($rel) -and -not $oldManual) { Refuse "the folder already holds other files ($rel). Choose a new empty folder: $Out" }
    }
}

# 4. copy
$n = 0
foreach ($f in $list) {
    $to = Join-Path $Out $f
    [void][IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($to))
    [IO.File]::Copy((Join-Path $root $f), $to, $true)
    $n++
}
if ($hasSetup) { [IO.File]::Copy($setup, (Join-Path $Out $setupName), $true); $n++ }
elseif (Test-Path -LiteralPath (Join-Path $Out $setupName)) { Remove-Item -LiteralPath (Join-Path $Out $setupName) -Force }   # older than the program
$to = Join-Path $Out $pkgRel
[void][IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($to))
if ((Test-Pkg $to) -ne "") {
    # copied under another name first: an interrupted copy (flash drive pulled out) is never mistaken for the package
    $new = $to + ".new"
    [IO.File]::Copy($Package, $new, $true)
    $bad = Test-Pkg $new
    if ($bad) { Remove-Item -LiteralPath $new -Force; Refuse "the copy is not correct, check the disk / flash drive: $bad" }
    if (Test-Path -LiteralPath $to) { Remove-Item -LiteralPath $to -Force }
    [IO.File]::Move($new, $to)
}
$n++

# 5. last look: nothing private in the folder
$forbidden = @(Get-ChildItem -LiteralPath $Out -Recurse -File -Force | Where-Object {
    $_.Name -match '^(settings\.ini|logo\.png|errors\.log)$' -or $_.Extension -match '^\.(amr|mdb|mde|accdb|ldb|laccdb|bak|log|ini)$' })
if ($forbidden.Count -gt 0) { Refuse ("private files in the folder: " + (($forbidden | ForEach-Object { $_.Name }) -join ", ")) }

$total = (Get-ChildItem -LiteralPath $Out -Recurse -File | Measure-Object Length -Sum).Sum
Write-Host "folder  : $Out"
Write-Host "files   : $n ($([int]($total / 1MB)) MB)"
Write-Host "package : $pkgRel (SHA-256 OK)"
exit 0
