# Builds dist\Inventory-Management-Cleaner.exe (removes copies of the program, never data; see Cleaner.cs).
param([string]$Out = "")
$ErrorActionPreference = "Stop"
$root = Split-Path $PSScriptRoot -Parent
if ($Out -eq "") { $Out = Join-Path $root "dist\Inventory-Management-Cleaner.exe" }
[void](New-Item -ItemType Directory -Force (Split-Path $Out -Parent))
$csc = Join-Path $env:WINDIR "Microsoft.NET\Framework\v4.0.30319\csc.exe"
$shared = "InstallEnv", "InstallFind", "InstallReplace", "ProgramExit", "ShellLink" | ForEach-Object { Join-Path $root "src\Update\$_.cs" }
$a = @("/nologo", "/target:winexe", "/platform:x86", "/optimize+", "/codepage:65001", "/nowarn:0649,0414", "/main:Stores.Installer.CleanerMain", "/out:$Out",
    "/win32icon:$(Join-Path $root 'src\app.ico')", "/win32manifest:$(Join-Path $PSScriptRoot 'setup.manifest')",
    "/r:System.dll", "/r:System.Core.dll", "/r:System.Drawing.dll", "/r:System.Windows.Forms.dll", "/r:System.IO.Compression.dll", "/r:System.IO.Compression.FileSystem.dll",
    (Join-Path $PSScriptRoot "Cleaner.cs"), (Join-Path $PSScriptRoot "SetupEngine.cs")) + $shared
& $csc $a
if ($LASTEXITCODE -ne 0) { Write-Host "BUILD FAILED (cleaner)"; exit 1 }
Write-Host "cleaner : $Out ($((Get-Item -LiteralPath $Out).Length) bytes)"
exit 0
