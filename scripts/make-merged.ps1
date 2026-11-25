<#
  One single file that installs everything without internet: the setup program with Microsoft's SQL Server package
  appended to it (installer\SetupEngine.cs, PackageInside).

    scripts\make-merged.ps1 -Out "D:\...\Inventory Management Full Setup 1.2.2.exe" [-Package <SQLEXPR_x64_ENU.exe>]

  Run build.bat and installer\build-setup.bat first. The package must be exactly Microsoft's file (size and SHA-256 in
  src\Server\SqlPackage.cs) and stays unmodified; the setup asks the user to accept Microsoft's terms for it
  (installer\sql-express-terms.pdf, 2.b allows distributing it inside a setup program). -Download: fetches the package from
  Microsoft first (release workflow). -Asset: also writes <file>.sha256 and the GitHub step outputs.
#>
param([string]$Out = "", [string]$Package = "", [switch]$Download, [switch]$Asset)
$ErrorActionPreference = "Stop"
$root = Split-Path $PSScriptRoot -Parent
if ($Package -eq "") { $Package = Join-Path $root "مكونات\SQLEXPR_x64_ENU.exe" }
$setup = Join-Path $root "dist\Inventory Management Setup.exe"
if ($Download -and -not (Test-Path -LiteralPath $Package)) {
    $url = [regex]::Match([IO.File]::ReadAllText((Join-Path $root "src\Server\SqlPackage.cs")), 'https://download\.microsoft\.com[^"]+').Value
    [void](New-Item -ItemType Directory -Force (Split-Path $Package -Parent))
    & curl.exe --fail --silent --show-error --location --retry 5 --output $Package $url
    if ($LASTEXITCODE -ne 0) { Write-Host "download failed: $url"; exit 1 }
}
foreach ($f in $setup, $Package) { if (-not (Test-Path -LiteralPath $f -PathType Leaf)) { Write-Host "missing: $f"; exit 1 } }
if ((Get-Item -LiteralPath $setup).LastWriteTime -lt (Get-Item -LiteralPath (Join-Path $root "Inventory Management.exe")).LastWriteTime) { Write-Host "the setup is older than the program: run installer\build-setup.bat"; exit 1 }
$src = [IO.File]::ReadAllText((Join-Path $root "src\Server\SqlPackage.cs"))
$size = [long][regex]::Match($src, 'OfficialSize = (\d+)').Groups[1].Value
$hash = [regex]::Match($src, 'OfficialSha256 = "([0-9A-F]{64})"').Groups[1].Value
if ($Out -eq "") { $Out = Join-Path $root ("dist\Inventory-Management-Full-Setup-" + [regex]::Match([IO.File]::ReadAllText((Join-Path $root "src\App\Program.cs")), 'const\s+string\s+Version\s*=\s*"([^"]+)"').Groups[1].Value + ".exe") }
if ((Get-Item -LiteralPath $Package).Length -ne $size -or (Get-FileHash -LiteralPath $Package -Algorithm SHA256).Hash -ne $hash) { Write-Host "REFUSED: not Microsoft's package (size / SHA-256)"; exit 1 }

[void](New-Item -ItemType Directory -Force (Split-Path $Out -Parent))
$tmp = "$Out.part"
$o = [IO.File]::Create($tmp)
try {
    foreach ($f in $setup, $Package) { $i = [IO.File]::OpenRead($f); try { $i.CopyTo($o) } finally { $i.Close() } }
    $b = [BitConverter]::GetBytes($size); $o.Write($b, 0, 8)
    $m = [Text.Encoding]::ASCII.GetBytes("EngStoresPackage"); $o.Write($m, 0, 16)
} finally { $o.Close() }
if (Test-Path -LiteralPath $Out) { Remove-Item -LiteralPath $Out -Force }
Move-Item -LiteralPath $tmp -Destination $Out
Write-Host "merged  : $Out ($((Get-Item -LiteralPath $Out).Length) bytes)"
$sha = (Get-FileHash -LiteralPath $Out -Algorithm SHA256).Hash.ToLowerInvariant()
Write-Host "sha256  : $sha"
if ($Asset) {
    [IO.File]::WriteAllText("$Out.sha256", "$sha  $(Split-Path $Out -Leaf)`n", (New-Object Text.UTF8Encoding $false))
    if ($env:GITHUB_OUTPUT) { [IO.File]::AppendAllText($env:GITHUB_OUTPUT, "full=$Out`nfullsha=$Out.sha256`n", (New-Object Text.UTF8Encoding $false)) }
}
exit 0
