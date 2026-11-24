<#
  Builds the setup program «Inventory Management Setup.exe» with Windows' own C# compiler.

    installer\build-setup.bat                 → dist\Inventory Management Setup.exe   (run build.bat first)
    installer\build-setup.bat -Release        → also dist\Inventory-Management-Setup-<version>.exe + .sha256 (release assets)

  The program files come from scripts\package.ps1 (the same zip as the release), embedded as one resource. The setup's
  version is Program.Version. It never contains data, settings or Microsoft SQL files.
#>
param([string]$Out = "", [string]$Tag = "", [switch]$Release)
$ErrorActionPreference = "Stop"
$root = Split-Path $PSScriptRoot -Parent
$obj = Join-Path $PSScriptRoot "obj"
$dist = Join-Path $root "dist"
if ($Out -eq "") { $Out = Join-Path $dist "Inventory Management Setup.exe" }
New-Item -ItemType Directory -Force $obj, (Split-Path $Out -Parent) | Out-Null

# 1. the program files: the release zip (its GITHUB_OUTPUT lines belong to the packaging step, not to this one)
$gh = $env:GITHUB_OUTPUT; $env:GITHUB_OUTPUT = ""
try { & (Join-Path $root "scripts\package.ps1") -Tag $Tag -Out $obj | Out-Null; $ok = $LASTEXITCODE -eq 0 } finally { $env:GITHUB_OUTPUT = $gh }
if (-not $ok) { Write-Host "packing the program files failed (run build.bat first)"; exit 1 }
$program = Get-Content -LiteralPath (Join-Path $root "src\App\Program.cs") -Raw -Encoding UTF8
$version = [regex]::Match($program, 'const\s+string\s+Version\s*=\s*"([^"]+)"').Groups[1].Value
$zip = Join-Path $obj "EngStores-$version.zip"

# no data, settings or SQL Server file may ever be inside
Add-Type -AssemblyName System.IO.Compression.FileSystem
$z = [System.IO.Compression.ZipFile]::OpenRead($zip)
try { $bad = @($z.Entries | Where-Object { $_.FullName -match '\.(amr|mdb|accdb|bak|ini|log)$' -or $_.FullName -match 'SQLEXPR' }) } finally { $z.Dispose() }
if ($bad.Count -gt 0) { Write-Host "the zip holds files that must not be published: $($bad.FullName -join ', ')"; exit 1 }

# 2. version information of the setup file
$four = ($version + ".0.0.0").Split(".")[0..3] -join "."
$info = @"
using System.Reflection;
[assembly: AssemblyTitle("Inventory Management Setup")]
[assembly: AssemblyProduct("Inventory Management Software")]
[assembly: AssemblyVersion("$four")]
[assembly: AssemblyFileVersion("$four")]
[assembly: AssemblyInformationalVersion("$version")]
"@
[System.IO.File]::WriteAllText((Join-Path $obj "SetupInfo.cs"), $info, (New-Object System.Text.UTF8Encoding($true)))

# 3. compile (x86 as the program, so its version can be read from its file)
$csc = Join-Path $env:WINDIR "Microsoft.NET\Framework\v4.0.30319\csc.exe"
$shared = "InstallEnv", "InstallFind", "InstallReplace", "ProgramExit", "ShellLink" | ForEach-Object { Join-Path $root "src\Update\$_.cs" }
$cscArgs = @("/nologo", "/target:winexe", "/platform:x86", "/optimize+", "/codepage:65001", "/nowarn:0649", "/out:$Out",
    "/win32icon:$(Join-Path $root 'src\app.ico')", "/win32manifest:$(Join-Path $PSScriptRoot 'setup.manifest')", "/resource:$zip,payload.zip", "/resource:$(Join-Path $root 'src\app.ico'),app.ico",
    "/r:System.dll", "/r:System.Core.dll", "/r:System.Drawing.dll", "/r:System.Windows.Forms.dll",
    "/r:System.IO.Compression.dll", "/r:System.IO.Compression.FileSystem.dll",
    (Join-Path $PSScriptRoot "Setup.cs"), (Join-Path $PSScriptRoot "SetupEngine.cs"), (Join-Path $PSScriptRoot "SetupForm.cs"), (Join-Path $obj "SetupInfo.cs")) + $shared
& $csc $cscArgs
if ($LASTEXITCODE -ne 0) { Write-Host "BUILD FAILED (setup)"; exit 1 }
Write-Host "version : $version"
Write-Host "setup   : $Out ($((Get-Item -LiteralPath $Out).Length) bytes)"

# 4. release assets (the in-app updater ignores them: it takes only EngStores-<version>.zip and its .sha256)
if ($Release) {
    $name = "Inventory-Management-Setup-$version.exe"
    $asset = Join-Path $dist $name
    Copy-Item -LiteralPath $Out -Destination $asset -Force
    $hash = (Get-FileHash -LiteralPath $asset -Algorithm SHA256).Hash.ToLowerInvariant()
    [System.IO.File]::WriteAllText("$asset.sha256", "$hash  $name`n", (New-Object System.Text.UTF8Encoding($false)))
    Write-Host "asset   : $asset"
    Write-Host "sha256  : $hash"
    if ($env:GITHUB_OUTPUT) { [System.IO.File]::AppendAllText($env:GITHUB_OUTPUT, "setup=$asset`nsetupsha=$asset.sha256`n", (New-Object System.Text.UTF8Encoding($false))) }
}
exit 0
