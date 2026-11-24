<#
  Packs a release: dist\EngStores-<version>.zip (+ .zip.sha256) for GitHub Releases / the program's updater.

    scripts\package.ps1                 version from src\App\Program.cs (Program.Version)
    scripts\package.ps1 -Tag v1.2.0     also checks that the tag equals Program.Version (release workflow)

  Run build.bat first. The zip holds only: the program .exe, the two guide files and the illustrated user manual
  (docs\manual: index.html + img\*.png, opened by the program with F1 / «الإعدادات ← دليل الاستخدام»).
  Never: the «مكونات» folder (Microsoft's SQL Server package, see make-offline.ps1), logo.png, settings.ini, data files, backups.
  The updater (src\Update\Updater.cs) takes only EngStores-<tag version>.zip and its .zip.sha256 from this repository's release downloads,
  checks the SHA-256 and looks for «Inventory Management.exe» inside the zip.
#>
param(
    [string]$Tag = "",
    [string]$Out = ""
)
$ErrorActionPreference = "Stop"
$root = Split-Path $PSScriptRoot -Parent
if ($Out -eq "") { $Out = Join-Path $root "dist" }

$program = Get-Content -LiteralPath (Join-Path $root "src\App\Program.cs") -Raw -Encoding UTF8
$m = [regex]::Match($program, 'const\s+string\s+Version\s*=\s*"([^"]+)"')
if (-not $m.Success) { Write-Host "Program.Version not found in src\App\Program.cs"; exit 1 }
$version = $m.Groups[1].Value
if ($Tag -ne "") {
    $t = $Tag -replace '^refs/tags/', ''
    if ($t.TrimStart('v', 'V') -ne $version) {
        Write-Host "The tag '$t' does not match Program.Version '$version' (src\App\Program.cs). Bump the version or fix the tag."
        exit 1
    }
}

$files = @("Inventory Management.exe", "دليل التشغيل.txt", "إعداد الخادم المجاني.txt")
foreach ($f in $files) {
    if (-not (Test-Path -LiteralPath (Join-Path $root $f))) { Write-Host "missing: $f (run build.bat first)"; exit 1 }
}
# the user manual keeps its folder (docs/manual/...) next to the exe: the updater copies sub-folders too
$manual = Join-Path $root "docs\manual"
if (-not (Test-Path -LiteralPath (Join-Path $manual "index.html"))) { Write-Host "missing: docs\manual\index.html"; exit 1 }
$manualFiles = @(Get-ChildItem -LiteralPath $manual -Recurse -File | ForEach-Object { $_.FullName.Substring($root.Length + 1) })

New-Item -ItemType Directory -Force $Out | Out-Null
$name = "EngStores-$version.zip"
$zipPath = Join-Path (Resolve-Path $Out).Path $name
if (Test-Path -LiteralPath $zipPath) { Remove-Item -LiteralPath $zipPath -Force }

Add-Type -AssemblyName System.IO.Compression
Add-Type -AssemblyName System.IO.Compression.FileSystem
# entry names are written in UTF-8 (with the zip "UTF-8 names" flag), so the Arabic names survive extraction
$zip = [System.IO.Compression.ZipFile]::Open($zipPath, [System.IO.Compression.ZipArchiveMode]::Create, [System.Text.Encoding]::UTF8)
try {
    foreach ($f in $files) {
        [void][System.IO.Compression.ZipFileExtensions]::CreateEntryFromFile($zip, (Join-Path $root $f), $f, [System.IO.Compression.CompressionLevel]::Optimal)
    }
    foreach ($f in $manualFiles) {
        # zip entry names use '/' (docs/manual/img/x.png)
        [void][System.IO.Compression.ZipFileExtensions]::CreateEntryFromFile($zip, (Join-Path $root $f), $f.Replace('\', '/'), [System.IO.Compression.CompressionLevel]::Optimal)
    }
} finally { $zip.Dispose() }

$hash = (Get-FileHash -LiteralPath $zipPath -Algorithm SHA256).Hash.ToLowerInvariant()
$shaPath = "$zipPath.sha256"
[System.IO.File]::WriteAllText($shaPath, "$hash  $name`n", (New-Object System.Text.UTF8Encoding($false)))

Write-Host "version : $version"
Write-Host "manual  : $($manualFiles.Count) files"
Write-Host "zip     : $zipPath ($((Get-Item -LiteralPath $zipPath).Length) bytes)"
Write-Host "sha256  : $hash"
if ($env:GITHUB_OUTPUT) {
    # (no BOM: Add-Content -Encoding UTF8 would start the file with one and break the first key)
    [System.IO.File]::AppendAllText($env:GITHUB_OUTPUT, "version=$version`nzip=$zipPath`nsha=$shaPath`n", (New-Object System.Text.UTF8Encoding($false)))
}
exit 0
