# ============================================================================
#  Main server setup for "مخازن الشركة الهندسية" (embedded in the program, run elevated by it)
#  - Installs Microsoft SQL Server 2022 Express (free) if missing
#  - Opens it to the local network: TCP 1433 + SQL Browser (so other PCs find it) + firewall rules
#  - Creates database EngStores and login storesapp (password derived by the program from the owner's
#    server password; least privilege: read/write data, create/alter its own tables, back up)
#  - Schedules a daily backup (last 14 days kept)
#  Started by the program through a small elevated command that first copies this script into
#  %ProgramFiles%\EngStores\setup (writable by Administrators/SYSTEM only) and checks its SHA-256.
#  Everything this script runs or schedules lives in that folder, and every Microsoft package is
#  checked to be signed by Microsoft Corporation before it is run.
#  Usage: setup-server.ps1 -PasswordFile <file> -ResultFile <file> [-MediaFile <SQLEXPR*.exe>] [-ProgressFile <file>]
#  Result file: "OK|<backup folder>"  or  "ERR|<message>"
# ============================================================================
param([string]$PasswordFile, [string]$ResultFile, [string]$MediaFile, [string]$ProgressFile)
$ErrorActionPreference = 'Stop'

$Instance = 'SQLEXPRESS'
$Database = 'EngStores'
$Login    = 'storesapp'
$ProgFiles = if ($env:ProgramW6432) { $env:ProgramW6432 } else { $env:ProgramFiles }
$Work     = Join-Path $ProgFiles 'EngStores\setup'      # Administrators + SYSTEM only (see Protect-Folder)
$OldWork  = Join-Path $env:ProgramData 'EngStoresSetup'   # used by older versions (writable by users) - removed
$SseiUrl  = 'https://download.microsoft.com/download/29654887-7cde-4397-bba3-d7f087970845/SQL2022-SSEI-Expr.exe'
$SseiHash = 'E2B8E8AAD30D8B1A1922C6C0976C806F0A2E08D04B2DE100A9D70B9F94750BB1'
$svcName  = 'MSSQL$' + $Instance

# Progress for the program's window: "<expected seconds of this step>|<expected seconds of everything after it>|<text>"
# (the "after" figures include ~30 s the program needs afterwards to connect and move the data)
function Say([string]$m, [int]$sec = 0, [int]$after = 0) { Write-Host ('[' + (Get-Date -Format 'HH:mm:ss') + '] ' + $m) -ForegroundColor Cyan; if ($ProgressFile) { try { [IO.File]::WriteAllText($ProgressFile, ([string]$sec + '|' + [string]$after + '|' + $m), (New-Object Text.UTF8Encoding $false)) } catch { } } }
function Done([string]$r) { [IO.File]::WriteAllText($ResultFile, $r, (New-Object Text.UTF8Encoding $false)) }
function Stop-Setup([string]$m) { Write-Host $m -ForegroundColor Red; Done ('ERR|' + $m); exit 1 }

trap { Stop-Setup ('خطأ غير متوقع: ' + $_.Exception.Message) }

# Only Administrators and SYSTEM may write (or even read) the work folder: nothing a normal user could plant is ever run.
function Protect-Folder([string]$dir) {
    New-Item -ItemType Directory -Force -Path $dir | Out-Null
    & icacls.exe $dir /setowner '*S-1-5-32-544' /T /C /Q | Out-Null
    & icacls.exe $dir /reset /T /C /Q | Out-Null
    & icacls.exe $dir /inheritance:r /grant:r '*S-1-5-32-544:(OI)(CI)F' '*S-1-5-18:(OI)(CI)F' /C /Q | Out-Null
    if ($LASTEXITCODE -ne 0) { Stop-Setup ('تعذر حماية مجلد الإعداد: ' + $dir) }
}

# A valid Authenticode signature is not enough: the signer and its root must be Microsoft Corporation.
function Test-MicrosoftSigned([string]$file) {
    if (-not $file -or -not (Test-Path -LiteralPath $file)) { return $false }
    $sig = Get-AuthenticodeSignature -LiteralPath $file
    if ($sig.Status -ne 'Valid' -or -not $sig.SignerCertificate) { return $false }
    if ($sig.SignerCertificate.Subject -notmatch '(^|,\s*)O=Microsoft Corporation(\s*,|$)') { return $false }
    $chain = New-Object System.Security.Cryptography.X509Certificates.X509Chain
    $chain.ChainPolicy.RevocationMode = 'NoCheck'
    [void]$chain.Build($sig.SignerCertificate)
    if ($chain.ChainElements.Count -lt 2) { return $false }
    $root = $chain.ChainElements[$chain.ChainElements.Count - 1].Certificate
    return ($root.Subject -match '(^|,\s*)O=Microsoft Corporation(\s*,|$)') -and ($root.Subject -match 'Microsoft Root')
}

if (-not [Environment]::Is64BitOperatingSystem) { Stop-Setup 'SQL Server يحتاج ويندوز 64 بت.' }
# 32-bit PowerShell sees the 32-bit registry only -> restart this script in 64-bit PowerShell
if (-not [Environment]::Is64BitProcess) {
    $ps64 = Join-Path $env:windir 'Sysnative\WindowsPowerShell\v1.0\powershell.exe'
    $p = Start-Process -FilePath $ps64 -Wait -PassThru -WindowStyle Hidden -ArgumentList @('-NoProfile','-ExecutionPolicy','Bypass','-File',('"' + $PSCommandPath + '"'),
        '-PasswordFile',('"' + $PasswordFile + '"'),'-ResultFile',('"' + $ResultFile + '"'),'-MediaFile',('"' + $MediaFile + '"'),'-ProgressFile',('"' + $ProgressFile + '"'))
    exit $p.ExitCode
}
$AppPassword = [IO.File]::ReadAllText($PasswordFile).Trim()
Remove-Item -LiteralPath $PasswordFile -Force -ErrorAction SilentlyContinue
if ($AppPassword.Length -lt 20) { Stop-Setup 'كلمة سر حساب البرنامج غير صالحة.' }
Protect-Folder $Work
# this script must run from the protected folder (the program's elevated start copies it there)
if (-not $PSCommandPath.StartsWith($Work + '\', [StringComparison]::OrdinalIgnoreCase)) { Stop-Setup 'يجب تشغيل الإعداد من البرنامج.' }

Write-Host '================================================================' -ForegroundColor Yellow
Write-Host '  Setting up the main server - please wait, do not close this window' -ForegroundColor Yellow
Write-Host '================================================================' -ForegroundColor Yellow

# ---------------------------------------------------------------- 1. install SQL Server Express if missing
if (-not (Get-Service -Name $svcName -ErrorAction SilentlyContinue)) {
    # 1a. the package bundled with the program (folder «مكونات») - no internet needed.
    #     A normal user can write next to the program, so it is copied into the protected folder first
    #     and only the checked copy is run.
    $pkg = $null
    if ($MediaFile -and (Test-Path -LiteralPath $MediaFile)) {
        Say 'فحص حزمة SQL Server المرفقة مع البرنامج…' 15 635
        $copy = Join-Path $Work 'SQLEXPR_x64_ENU.exe'
        Copy-Item -LiteralPath $MediaFile -Destination $copy -Force
        if (Test-MicrosoftSigned $copy) { $pkg = Get-Item -LiteralPath $copy } else { Remove-Item -LiteralPath $copy -Force -ErrorAction SilentlyContinue }
    }
    # 1b. otherwise download it from Microsoft
    if (-not $pkg) {
        [Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12
        $ssei = Join-Path $Work 'SQL2022-SSEI-Expr.exe'
        if (-not (Test-Path $ssei)) { Say 'تنزيل برنامج تنصيب SQL Server من مايكروسوفت…' 60 1235; Invoke-WebRequest -Uri $SseiUrl -OutFile $ssei -UseBasicParsing }
        if ((Get-FileHash $ssei -Algorithm SHA256).Hash -ne $SseiHash) { Remove-Item $ssei -Force; Stop-Setup 'ملف التنصيب المنزّل غير مطابق لملف مايكروسوفت الأصلي. أعد المحاولة.' }
        if (-not (Test-MicrosoftSigned $ssei)) { Stop-Setup 'ملف التنصيب غير موقّع من مايكروسوفت.' }
        $media = Join-Path $Work 'media'
        $pkg = Get-ChildItem $media -Filter 'SQLEXPR*.exe' -Recurse -ErrorAction SilentlyContinue | Select-Object -First 1
        if (-not $pkg) {
            Say 'تنزيل حزمة SQL Server من مايكروسوفت (نحو 280 ميغابايت — حسب سرعة الإنترنت)…' 600 635
            Start-Process -FilePath $ssei -ArgumentList '/ACTION=Download', ('/MEDIAPATH="' + $media + '"'), '/MEDIATYPE=Core', '/QUIET' -Wait -WindowStyle Hidden
            $pkg = Get-ChildItem $media -Filter 'SQLEXPR*.exe' -Recurse -ErrorAction SilentlyContinue | Select-Object -First 1
            if (-not $pkg) { Stop-Setup 'تعذر تنزيل SQL Server. تأكد من الإنترنت وأعد المحاولة.' }
        }
    }
    if (-not (Test-MicrosoftSigned $pkg.FullName)) { Stop-Setup 'حزمة SQL Server غير موقّعة من مايكروسوفت.' }

    $extract = Join-Path $Work 'extract'
    if (-not (Test-Path (Join-Path $extract 'setup.exe'))) {
        Say 'فك ضغط حزمة SQL Server…' 60 575
        Start-Process -FilePath $pkg.FullName -ArgumentList '/q', ('/x:"' + $extract + '"') -Wait -WindowStyle Hidden
    }
    if (-not (Test-MicrosoftSigned (Join-Path $extract 'setup.exe'))) { Stop-Setup 'برنامج تنصيب SQL Server غير موقّع من مايكروسوفت.' }
    # random strong password for "sa"; the account is disabled after setup
    $chars = 'ABCDEFGHJKLMNPQRSTUVWXYZabcdefghijkmnopqrstuvwxyz23456789!#%+-='
    $rnd = New-Object byte[] 24; [Security.Cryptography.RandomNumberGenerator]::Create().GetBytes($rnd)
    $sa = 'Aa1!' + (-join ($rnd | ForEach-Object { $chars[$_ % $chars.Length] }))
    Say 'تنصيب SQL Server… (أطول خطوة: عادة 5–15 دقيقة حسب سرعة الحاسوب)' 480 95
    $setupArgs = @('/Q', '/ACTION=Install', '/FEATURES=SQLEngine', ('/INSTANCENAME=' + $Instance),
                   '/SQLSYSADMINACCOUNTS="BUILTIN\Administrators"', '/SECURITYMODE=SQL', ('/SAPWD="' + $sa + '"'),
                   '/TCPENABLED=1', '/BROWSERSVCSTARTUPTYPE=Automatic', '/UPDATEENABLED=0', '/IACCEPTSQLSERVERLICENSETERMS')
    $p = Start-Process -FilePath (Join-Path $extract 'setup.exe') -ArgumentList $setupArgs -Wait -PassThru -WindowStyle Hidden
    if ($p.ExitCode -ne 0 -and $p.ExitCode -ne 3010) {
        Stop-Setup ('فشل تنصيب SQL Server (رمز ' + $p.ExitCode + '). التفاصيل في: C:\Program Files\Microsoft SQL Server\160\Setup Bootstrap\Log\Summary.txt')
    }
} else { Say 'SQL Server منصّب مسبقا — يجري إعداده…' 5 95 }

# ---------------------------------------------------------------- 2. network: password logins + port 1433 + SQL Browser + firewall
$instKey = (Get-ItemProperty 'HKLM:\SOFTWARE\Microsoft\Microsoft SQL Server\Instance Names\SQL').$Instance
if (-not $instKey) { Stop-Setup 'لم يتم العثور على SQL Server بعد التنصيب.' }
$base = "HKLM:\SOFTWARE\Microsoft\Microsoft SQL Server\$instKey\MSSQLServer"
Set-ItemProperty -Path $base -Name LoginMode -Value 2 -Type DWord
$tcp = "$base\SuperSocketNetLib\Tcp"
Set-ItemProperty -Path $tcp -Name Enabled -Value 1 -Type DWord
Set-ItemProperty -Path "$tcp\IPAll" -Name TcpDynamicPorts -Value ''
Set-ItemProperty -Path "$tcp\IPAll" -Name TcpPort -Value '1433'
Say 'فتح الاتصال للشبكة وإعادة تشغيل SQL Server…' 30 65
Set-Service -Name $svcName -StartupType Automatic
Restart-Service -Name $svcName -Force
if (Get-Service -Name 'SQLBrowser' -ErrorAction SilentlyContinue) { Set-Service -Name 'SQLBrowser' -StartupType Automatic; Start-Service -Name 'SQLBrowser' -ErrorAction SilentlyContinue }
if (-not (Get-NetFirewallRule -DisplayName 'EngStores SQL Server (TCP 1433)' -ErrorAction SilentlyContinue)) {
    New-NetFirewallRule -DisplayName 'EngStores SQL Server (TCP 1433)' -Direction Inbound -Protocol TCP -LocalPort 1433 -Action Allow -Profile Domain, Private | Out-Null
}
if (-not (Get-NetFirewallRule -DisplayName 'EngStores SQL Browser (UDP 1434)' -ErrorAction SilentlyContinue)) {
    New-NetFirewallRule -DisplayName 'EngStores SQL Browser (UDP 1434)' -Direction Inbound -Protocol UDP -LocalPort 1434 -Action Allow -Profile Domain, Private | Out-Null
}

# ---------------------------------------------------------------- 3. database + login
function Invoke-Sql([string]$sql, [string]$db = 'master') {
    $c = New-Object System.Data.SqlClient.SqlConnection ("Server=.\$Instance;Database=$db;Integrated Security=True;Encrypt=False;Connect Timeout=15")
    $c.Open()
    try { $cmd = $c.CreateCommand(); $cmd.CommandTimeout = 600; $cmd.CommandText = $sql; return $cmd.ExecuteScalar() }
    finally { $c.Close() }
}
Say 'انتظار جاهزية SQL Server…' 20 45
$ready = $false
for ($i = 0; $i -lt 30 -and -not $ready; $i++) { try { [void](Invoke-Sql 'SELECT 1'); $ready = $true } catch { Start-Sleep -Seconds 3 } }
if (-not $ready) { Stop-Setup 'SQL Server لا يستجيب. أعد تشغيل الحاسوب ثم اضغط «السيرفر الرئيسي» مرة ثانية.' }

Say 'إنشاء قاعدة البيانات وحساب البرنامج…' 10 35
$pw = $AppPassword.Replace("'", "''")
[void](Invoke-Sql "IF DB_ID(N'$Database') IS NULL CREATE DATABASE [$Database]")
[void](Invoke-Sql "IF SUSER_ID(N'$Login') IS NULL CREATE LOGIN [$Login] WITH PASSWORD = N'$pw', CHECK_POLICY = OFF, DEFAULT_DATABASE = [$Database] ELSE ALTER LOGIN [$Login] WITH PASSWORD = N'$pw', CHECK_POLICY = OFF")
# Least privilege for the program's login (not db_owner). Also run by tests\ServerLoginTests.cs - keep the here-string.
$RoleSql = @'
IF USER_ID(N'storesapp') IS NULL CREATE USER [storesapp] FOR LOGIN [storesapp];
ALTER USER [storesapp] WITH LOGIN = [storesapp];
IF IS_ROLEMEMBER(N'db_owner', N'storesapp') = 1 ALTER ROLE db_owner DROP MEMBER [storesapp];
ALTER ROLE db_datareader ADD MEMBER [storesapp];
ALTER ROLE db_datawriter ADD MEMBER [storesapp];
ALTER ROLE db_ddladmin ADD MEMBER [storesapp];
ALTER ROLE db_backupoperator ADD MEMBER [storesapp];
'@
[void](Invoke-Sql $RoleSql $Database)
[void](Invoke-Sql "IF SUSER_ID(N'NT AUTHORITY\SYSTEM') IS NULL CREATE LOGIN [NT AUTHORITY\SYSTEM] FROM WINDOWS")
[void](Invoke-Sql "IF USER_ID(N'NT AUTHORITY\SYSTEM') IS NULL CREATE USER [NT AUTHORITY\SYSTEM] FOR LOGIN [NT AUTHORITY\SYSTEM]; ALTER ROLE db_backupoperator ADD MEMBER [NT AUTHORITY\SYSTEM];" $Database)
[void](Invoke-Sql "ALTER LOGIN [sa] DISABLE")
$AppPassword = $null; $pw = $null

# ---------------------------------------------------------------- 4. daily backup (keeps 14 days)
Say 'إعداد النسخ الاحتياطي اليومي…' 5 30
$backupDir = [string](Invoke-Sql "SELECT CAST(SERVERPROPERTY('InstanceDefaultBackupPath') AS nvarchar(4000))")
if ([string]::IsNullOrWhiteSpace($backupDir)) {
    $backupDir = 'C:\EngStoresBackup'
    New-Item -ItemType Directory -Force -Path $backupDir | Out-Null
    & icacls.exe $backupDir /grant ('NT Service\' + $svcName + ':(OI)(CI)M') | Out-Null
}
$backupScript = Join-Path $Work 'backup.ps1'   # run daily as SYSTEM: lives in the protected folder only
@"
`$f = Join-Path '$backupDir' ('$Database' + '_' + (Get-Date -Format 'yyyy-MM-dd') + '.bak')
`$c = New-Object System.Data.SqlClient.SqlConnection 'Server=.\$Instance;Database=$Database;Integrated Security=True;Encrypt=False'
`$c.Open(); `$cmd = `$c.CreateCommand(); `$cmd.CommandTimeout = 3600
`$cmd.CommandText = "BACKUP DATABASE [$Database] TO DISK = N'" + `$f + "' WITH INIT, CHECKSUM"
[void]`$cmd.ExecuteNonQuery(); `$c.Close()
Get-ChildItem '$backupDir' -Filter '$Database*.bak' | Where-Object { `$_.LastWriteTime -lt (Get-Date).AddDays(-14) } | Remove-Item -Force
"@ | Set-Content -Path $backupScript -Encoding UTF8
$psExe    = Join-Path $env:windir 'System32\WindowsPowerShell\v1.0\powershell.exe'
$action   = New-ScheduledTaskAction -Execute $psExe -Argument ('-NoProfile -ExecutionPolicy Bypass -File "' + $backupScript + '"')
$trigger  = New-ScheduledTaskTrigger -Daily -At '14:00'
$settings = New-ScheduledTaskSettingsSet -StartWhenAvailable -ExecutionTimeLimit (New-TimeSpan -Hours 2)
Register-ScheduledTask -TaskName 'EngStores Daily Backup' -Action $action -Trigger $trigger -Settings $settings -User 'SYSTEM' -RunLevel Highest -Force | Out-Null
Protect-Folder $Work   # also covers backup.ps1 and the downloaded/extracted files
# older versions kept the setup files and the SYSTEM backup script in a folder users could write: remove it
if (Test-Path -LiteralPath $OldWork) { Remove-Item -LiteralPath $OldWork -Recurse -Force -ErrorAction SilentlyContinue }

Done ('OK|' + $backupDir)
exit 0
