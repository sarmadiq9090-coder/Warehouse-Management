<#
  Real installation test of the main server, from scratch and without internet, in Windows Sandbox (a disposable Windows:
  nothing is installed on this computer). Needs the Windows feature «Windows Sandbox».

    tests\sandbox\make-sandbox.ps1 -Offline <folder of the complete offline copy> [-Work <folder>] [-NoStart]

  Maps the offline folder read-only (like a flash drive), switches the network off, copies the folder inside the sandbox,
  and runs tests\bin\ServerInstallCheck.exe there. Result: <Work>\out\result.txt (and result.txt.done at the end).
#>
param([Parameter(Mandatory = $true)][string]$Offline, [string]$Work = "", [string]$Merged = "", [switch]$Auto, [switch]$NoStart)
$ErrorActionPreference = "Stop"
$root = Split-Path (Split-Path $PSScriptRoot -Parent) -Parent
if ($Work -eq "") { $Work = Join-Path $env:TEMP "engstores-sandbox" }
$Offline = (Resolve-Path -LiteralPath $Offline).Path
foreach ($f in "Inventory Management.exe", "مكونات\SQLEXPR_x64_ENU.exe") { if (-not (Test-Path -LiteralPath (Join-Path $Offline $f))) { Write-Host "missing in the offline folder: $f"; exit 1 } }
$bin = Join-Path $root "tests\bin"
foreach ($f in "ServerInstallCheck.exe", "DemoData.exe") { if (-not (Test-Path -LiteralPath (Join-Path $bin $f))) { Write-Host "missing tests\bin\$f - run tests\build-tests.bat"; exit 1 } }

if (Test-Path -LiteralPath $Work) { Remove-Item -LiteralPath $Work -Recurse -Force }
$in = Join-Path $Work "in"; $out = Join-Path $Work "out"
[void](New-Item -ItemType Directory -Force $in); [void](New-Item -ItemType Directory -Force $out)
Copy-Item -LiteralPath (Join-Path $bin "ServerInstallCheck.exe") $in
$p = Start-Process (Join-Path $bin "DemoData.exe") -ArgumentList ('"' + (Join-Path $root "tests\demo\empty-template.mdb") + '"'), ('"' + (Join-Path $in "demo.mdb") + '"') -NoNewWindow -Wait -PassThru
if (-not (Test-Path -LiteralPath (Join-Path $in "demo.mdb"))) { Write-Host "no demo data"; exit 1 }

if ($Merged -ne "") { Copy-Item -LiteralPath $Merged (Join-Path $in "full.exe") }      # the one-file setup (scripts\make-merged.ps1)
# inside the sandbox: the flash drive's folder copied to the disk, then the check next to «مكونات»
[IO.File]::WriteAllText((Join-Path $in "run.ps1"), @'
$ErrorActionPreference = 'Stop'
# Windows Sandbox only: its Smart App Control waits about 2 minutes for the internet at every MSI (a normal computer does not)
'started' | Set-Content 'C:\out\started.txt'
try { & reg.exe add HKLM\SYSTEM\CurrentControlSet\Control\CI\Policy /v VerifiedAndReputablePolicyState /t REG_DWORD /d 0 /f > C:\out\sac.txt 2>&1 } catch { }
try { & cmd.exe /c 'CiTool.exe --refresh --json < nul >> C:\out\sac.txt 2>&1' } catch { }
try {
    if (Test-Path 'C:\in\full.exe') {
        Copy-Item 'C:\in\full.exe' 'C:\Users\Public\full.exe'
        $q = Start-Process 'C:\Users\Public\full.exe' -ArgumentList '/S','/NODESKTOP','/ACCEPTSQLTERMS','/D=C:\Stores' -Wait -PassThru; "merged setup: exit $($q.ExitCode)" | Set-Content 'C:\out\merged.txt'
        Get-ChildItem C:\Stores -Recurse -File | Where-Object { $_.Length -gt 1MB } | Select-Object FullName, Length | Out-String | Add-Content 'C:\out\merged.txt'
    } else { Copy-Item -LiteralPath 'C:\flash' -Destination 'C:\Stores' -Recurse }
    Copy-Item -LiteralPath 'C:\in\ServerInstallCheck.exe' 'C:\Stores'
    Copy-Item -LiteralPath 'C:\in\demo.mdb' 'C:\Stores\demo.mdb'
    & 'C:\Stores\ServerInstallCheck.exe' 'C:\Stores\demo.mdb' 'C:\out\result.txt' *> 'C:\out\console.txt'
} catch { $_ | Out-String | Set-Content 'C:\out\error.txt' -Encoding UTF8 }
Copy-Item 'C:\Program Files\Microsoft SQL Server\160\Setup Bootstrap\Log\Summary.txt' 'C:\out\' -ErrorAction SilentlyContinue
Copy-Item 'C:\Program Files\Microsoft SQL Server\MSSQL16.SQLEXPRESS\MSSQL\Log\ERRORLOG' 'C:\out\' -ErrorAction SilentlyContinue
& { netstat -ano; ipconfig /all; Get-Content C:\Windows\System32\drivers\etc\hosts; [Net.Dns]::GetHostAddresses('localhost') | Out-String; Get-Service 'MSSQL*', SQLBrowser | Out-String } *> 'C:\out\network.txt'
'finished' | Set-Content 'C:\out\finished.txt'
'@, (New-Object Text.UTF8Encoding $true))

if ($Auto) {    # a user runs the one file and does nothing else
    Copy-Item -LiteralPath (Join-Path $PSScriptRoot "auto.ps1") $in
    $run = Join-Path $in "run.ps1"; $lines = [IO.File]::ReadAllLines($run); $keep = $lines | Where-Object { $_ -match "reg\.exe add|CiTool|started" }
    [IO.File]::WriteAllText($run, (($keep -join "`r`n") + "`r`n& 'C:\in\auto.ps1'`r`n"), (New-Object Text.UTF8Encoding $true))
}
$x = { param($s) [Security.SecurityElement]::Escape($s) }
$wsb = Join-Path $Work "offline-server.wsb"
[IO.File]::WriteAllText($wsb, @"
<Configuration>
  <Networking>Disable</Networking>
  <VGpu>Disable</VGpu>
  <MemoryInMB>6144</MemoryInMB>
  <MappedFolders>
    <MappedFolder><HostFolder>$(& $x $Offline)</HostFolder><SandboxFolder>C:\flash</SandboxFolder><ReadOnly>true</ReadOnly></MappedFolder>
    <MappedFolder><HostFolder>$(& $x $in)</HostFolder><SandboxFolder>C:\in</SandboxFolder><ReadOnly>true</ReadOnly></MappedFolder>
    <MappedFolder><HostFolder>$(& $x $out)</HostFolder><SandboxFolder>C:\out</SandboxFolder><ReadOnly>false</ReadOnly></MappedFolder>
  </MappedFolders>
  <LogonCommand><Command>powershell.exe -NoProfile -ExecutionPolicy Bypass -File C:\in\run.ps1</Command></LogonCommand>
</Configuration>
"@, (New-Object Text.UTF8Encoding $false))
Write-Host "sandbox: $wsb"
Write-Host "result : $out\result.txt"
if ($NoStart) { exit 0 }
$ws = Join-Path $env:windir "System32\WindowsSandbox.exe"
if (-not (Test-Path $ws)) { Write-Host "Windows Sandbox is not enabled on this computer."; exit 4 }
Start-Process $ws -ArgumentList ('"' + $wsb + '"')
