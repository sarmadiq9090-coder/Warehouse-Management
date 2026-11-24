# ============================================================================
#  Main server setup for "مخازن الشركة الهندسية" (embedded in the program, run elevated by it)
#  - Installs Microsoft SQL Server 2022 Express (free) if missing, from the package given in -MediaFile.
#    No internet: this script never downloads anything (the program finds, checks or downloads the package first)
#  - Opens it to the local network: TCP 1433 + SQL Browser (so other PCs find it) + firewall rules
#  - Creates database EngStores and login storesapp (password derived by the program from the owner's
#    server password; least privilege: read/write data, create/alter its own tables, back up)
#  - Schedules a daily backup (the last 10 are kept)
#  Started by the program through a small elevated command that first copies this script into
#  %ProgramFiles%\EngStores\setup (writable by Administrators/SYSTEM only) and checks its SHA-256.
#  Everything this script runs or schedules lives in that folder. The package is copied into it, and that copy must
#  have the pinned SHA-256 and be signed by Microsoft Corporation before it is run.
#  Usage: setup-server.ps1 -PasswordFile <file> -ResultFile <file> [-MediaFile <SQLEXPR_x64_ENU.exe>] [-ProgressFile <file>]
#         (-MediaFile is needed unless SQL Server is installed already or a checked copy is left from an earlier attempt)
#  Result file: "OK|<backup folder>"  or  "OK|<backup folder>|reboot"  or  "ERR|<message>"
# ============================================================================
param([string]$PasswordFile, [string]$ResultFile, [string]$MediaFile, [string]$ProgressFile)
$ErrorActionPreference = 'Stop'

$Instance = 'SQLEXPRESS'
$Database = 'EngStores'
$Login    = 'storesapp'
$ProgFiles = if ($env:ProgramW6432) { $env:ProgramW6432 } else { $env:ProgramFiles }
$Work     = Join-Path $ProgFiles 'EngStores\setup'      # Administrators + SYSTEM only (see Protect-Folder)
$OldWork  = Join-Path $env:ProgramData 'EngStoresSetup'   # used by older versions (writable by users) - removed
# the package (the same three values as src\Server\SqlPackage.cs)
$PkgName  = 'SQLEXPR_x64_ENU.exe'
$PkgSize  = 279293816
$PkgHash  = '2E61C8BBDE6021F9026C54AD9DB4BBB1227E68761D4C00A6A50A2C70FE7AFE05'
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

# The certificates inside a signed .exe. Without internet Windows cannot download the intermediate certificate («Microsoft
# Code Signing PCA»), and a clean Windows does not have it: the chain is built with the file's own copy.
function Get-SignatureCertificates([string]$file) {
    $all = New-Object System.Security.Cryptography.X509Certificates.X509Certificate2Collection
    try {
        Add-Type -AssemblyName System.Security
        $fs = [IO.File]::OpenRead($file)
        try {
            $r = New-Object IO.BinaryReader $fs
            $fs.Position = 0x3C; $pe = $r.ReadInt32()
            $fs.Position = $pe + 24; $magic = $r.ReadUInt16()               # 0x10b = 32-bit, 0x20b = 64-bit
            $dir = $pe + 24 + $(if ($magic -eq 0x20b) { 112 } else { 96 }) + 4 * 8   # data directory 4 = certificates
            $fs.Position = $dir; $off = [long]$r.ReadUInt32(); $len = [long]$r.ReadUInt32()
            if ($off -gt 0 -and $len -gt 8 -and $off + $len -le $fs.Length) {
                $fs.Position = $off; $size = $r.ReadUInt32(); [void]$r.ReadUInt32()   # WIN_CERTIFICATE header
                $cms = New-Object System.Security.Cryptography.Pkcs.SignedCms
                $cms.Decode($r.ReadBytes([int]$size - 8))
                $all.AddRange($cms.Certificates)
            }
        } finally { $fs.Close() }
    } catch { }
    return , $all
}

# A valid Authenticode signature is not enough: the signer and its root must be Microsoft Corporation.
function Test-MicrosoftSigned([string]$file) {
    if (-not $file -or -not (Test-Path -LiteralPath $file)) { return $false }
    $sig = Get-AuthenticodeSignature -LiteralPath $file
    if ($sig.Status -ne 'Valid' -or -not $sig.SignerCertificate) { return $false }
    if ($sig.SignerCertificate.Subject -notmatch '(^|,\s*)O=Microsoft Corporation(\s*,|$)') { return $false }
    $chain = New-Object System.Security.Cryptography.X509Certificates.X509Chain
    $chain.ChainPolicy.RevocationMode = 'NoCheck'
    [void]$chain.ChainPolicy.ExtraStore.AddRange((Get-SignatureCertificates $file))
    [void]$chain.Build($sig.SignerCertificate)
    if ($chain.ChainElements.Count -lt 2) { return $false }
    $root = $chain.ChainElements[$chain.ChainElements.Count - 1].Certificate
    return ($root.Subject -match '(^|,\s*)O=Microsoft Corporation(\s*,|$)') -and ($root.Subject -match 'Microsoft Root')
}

# ---- the SQL Server package (tests\OfflineSetupTests.cs runs these functions alone, with its own $Work)
# '' = exactly Microsoft's package, otherwise why not
function Test-Package([string]$file) {
    if (-not $file -or -not (Test-Path -LiteralPath $file -PathType Leaf)) { return 'الملف غير موجود' }
    $len = (Get-Item -LiteralPath $file).Length
    if ($len -lt $PkgSize) { return 'الملف ناقص، النسخ لم يكتمل (' + [int]($len / 1MB) + ' من أصل ' + [int]($PkgSize / 1MB) + ' ميغابايت)' }
    if ($len -ne $PkgSize) { return 'ليس الملف المطلوب، حجمه مختلف' }
    if ((Get-FileHash -LiteralPath $file -Algorithm SHA256).Hash -ne $PkgHash) { return 'الملف تالف أو معدّل، لا يطابق ملف مايكروسوفت الأصلي' }
    if (-not (Test-MicrosoftSigned $file)) { return 'الملف غير موقّع من مايكروسوفت' }
    return ''
}

# A normal user can write next to the program, so the package is copied into the protected folder first and only that copy,
# once checked, is run. A checked copy left by an earlier attempt is used again.
function Get-CheckedPackage([string]$media) {
    $copy = Join-Path $Work $PkgName
    if (Test-Path -LiteralPath $copy) {
        if ((Test-Package $copy) -eq '') { return $copy }
        Remove-Item -LiteralPath $copy -Force -ErrorAction SilentlyContinue
    }
    $again = ' انسخ مجلد «مكونات» من «النسخة الكاملة بدون إنترنت» إلى جانب البرنامج، ثم اضغط «السيرفر الرئيسي» مرة ثانية.'
    if (-not $media -or -not (Test-Path -LiteralPath $media -PathType Leaf)) { Stop-Setup ('ملف حزمة SQL Server غير موجود.' + $again) }
    try { Copy-Item -LiteralPath $media -Destination $copy -Force }
    catch {
        Remove-Item -LiteralPath $copy -Force -ErrorAction SilentlyContinue
        Stop-Setup ('تعذرت قراءة حزمة SQL Server (' + $_.Exception.Message + '). تأكد أن الفلاش موصول، ثم اضغط «السيرفر الرئيسي» مرة ثانية.')
    }
    $bad = Test-Package $copy
    if ($bad -ne '') {
        Remove-Item -LiteralPath $copy -Force -ErrorAction SilentlyContinue
        Stop-Setup ('حزمة SQL Server غير صالحة: ' + $bad + '.' + $again)
    }
    return $copy
}

# Always into an empty folder: what an interrupted attempt left there may be incomplete. Returns the path of setup.exe.
function Expand-Package([string]$pkg) {
    $extract = Join-Path $Work 'extract'
    $restart = ' أعد تشغيل الحاسوب ثم اضغط «السيرفر الرئيسي» مرة ثانية.'
    if (Test-Path -LiteralPath $extract) {
        # never pull the files away from a setup that is still running
        if (Get-Process -ErrorAction SilentlyContinue | Where-Object { $_.Path -and $_.Path.StartsWith($extract + '\', [StringComparison]::OrdinalIgnoreCase) }) {
            Stop-Setup 'تنصيب SQL Server من محاولة سابقة ما زال يعمل. انتظر 10 دقائق ثم اضغط «السيرفر الرئيسي» مرة ثانية، أو أعد تشغيل الحاسوب.'
        }
        Remove-Item -LiteralPath $extract -Recurse -Force -ErrorAction SilentlyContinue
        if (Test-Path -LiteralPath $extract) { Stop-Setup ('تعذر حذف ملفات محاولة سابقة: ' + $extract + '.' + $restart) }
    }
    $p = Start-Process -FilePath $pkg -ArgumentList '/q', ('/x:"' + $extract + '"') -Wait -PassThru -WindowStyle Hidden
    if ($p.ExitCode -ne 0) { Stop-Setup ('تعذر فك ضغط حزمة SQL Server (رمز ' + $p.ExitCode + '). تأكد من وجود 6 غيغابايت فارغة في القرص C.' + $restart) }
    $setup = Join-Path $extract 'setup.exe'
    if (-not (Test-Path -LiteralPath $setup)) { Stop-Setup ('فك ضغط حزمة SQL Server لم يكتمل (قد يكون برنامج مكافحة الفيروسات منعه).' + $restart) }
    if (-not (Test-MicrosoftSigned $setup)) { Stop-Setup 'برنامج تنصيب SQL Server غير موقّع من مايكروسوفت.' }
    return $setup
}

# Not needed once SQL Server is installed (about 0.6 GB); backup.ps1 and this script stay.
# The last two names are what older versions downloaded.
function Remove-InstallFiles {
    foreach ($n in 'extract', $PkgName, 'media', 'SQL2022-SSEI-Expr.exe') {
        $f = Join-Path $Work $n
        if (Test-Path -LiteralPath $f) { Remove-Item -LiteralPath $f -Recurse -Force -ErrorAction SilentlyContinue }
    }
}

# What to do about an exit code of SQL Server's setup ('' = nothing special known)
function Get-InstallHelp([int]$code) {
    switch ($code) {
        -2067919934 { return 'ويندوز ينتظر إعادة تشغيل (تحديث أو برنامج لم يكتمل تنصيبه). أعد تشغيل الحاسوب ثم اضغط «السيرفر الرئيسي» مرة ثانية.' }       # 0x84BE0BC2
        3017        { return 'ويندوز ينتظر إعادة تشغيل. أعد تشغيل الحاسوب ثم اضغط «السيرفر الرئيسي» مرة ثانية.' }
        -2061893606 { return 'تم التنصيب لكن خدمة SQL Server لم تبدأ. أعد تشغيل الحاسوب ثم اضغط «السيرفر الرئيسي» مرة ثانية. إذا تكرر فقد يكون قرص هذا الحاسوب غير متوافق: اختر حاسوبا آخر ليكون السيرفر الرئيسي.' }   # 0x851A001A
        1618        { return 'ويندوز ينصّب برنامجا آخر الآن. انتظر حتى ينتهي ثم اضغط «السيرفر الرئيسي» مرة ثانية.' }
        1602        { return 'أُلغي التنصيب. اضغط «السيرفر الرئيسي» مرة ثانية.' }
        1603        { return 'خطأ في منصّب ويندوز. أعد تشغيل الحاسوب، وأوقف برنامج مكافحة الفيروسات مؤقتا، ثم اضغط «السيرفر الرئيسي» مرة ثانية.' }
        1633        { return 'هذا الويندوز لا يدعم SQL Server (يحتاج ويندوز 10 أو 11 أو Server، نسخة 64 بت).' }
        -2147024784 { return 'المساحة الفارغة في القرص لا تكفي. فرّغ 6 غيغابايت ثم اضغط «السيرفر الرئيسي» مرة ثانية.' }   # 0x80070070
        -2147024891 { return 'ويندوز منع التنصيب (صلاحيات أو برنامج حماية). شغّل البرنامج بحساب مدير الحاسوب، وأوقف برنامج مكافحة الفيروسات مؤقتا، ثم أعد المحاولة.' }   # 0x80070005
    }
    return ''
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
$reboot = $false
if (-not (Get-Service -Name $svcName -ErrorAction SilentlyContinue)) {
    Say 'فحص حزمة SQL Server…' 20 300
    $pkg = Get-CheckedPackage $MediaFile
    Say 'فك ضغط حزمة SQL Server…' 25 275
    $setupExe = Expand-Package $pkg
    # random strong password for "sa"; the account is disabled after setup
    $chars = 'ABCDEFGHJKLMNPQRSTUVWXYZabcdefghijkmnopqrstuvwxyz23456789!#%+-='
    $rnd = New-Object byte[] 24; [Security.Cryptography.RandomNumberGenerator]::Create().GetBytes($rnd)
    $sa = 'Aa1!' + (-join ($rnd | ForEach-Object { $chars[$_ % $chars.Length] }))
    Say 'تنصيب SQL Server… (أطول خطوة: عادة 2–7 دقائق حسب سرعة الحاسوب)' 195 80
    $setupArgs = @('/Q', '/ACTION=Install', '/FEATURES=SQLEngine', ('/INSTANCENAME=' + $Instance),
                   '/SQLSYSADMINACCOUNTS="BUILTIN\Administrators"', '/SECURITYMODE=SQL', ('/SAPWD="' + $sa + '"'),
                   '/TCPENABLED=1', '/BROWSERSVCSTARTUPTYPE=Automatic', '/UPDATEENABLED=0', '/IACCEPTSQLSERVERLICENSETERMS')
    # /Q = fully silent: no window ever appears
    $p = Start-Process -FilePath $setupExe -ArgumentList $setupArgs -Wait -PassThru -WindowStyle Hidden
    $sa = $null
    $code = $p.ExitCode
    if ($code -eq 3010 -or $code -eq 1641) { $reboot = $true }   # installed, but Windows wants a restart
    elseif ($code -ne 0) {
        $logDir = Join-Path $ProgFiles 'Microsoft SQL Server\160\Setup Bootstrap\Log'
        $why = Select-String -LiteralPath (Join-Path $logDir 'Summary.txt') -Pattern '^\s*Exit message:\s*(.+)$' -ErrorAction SilentlyContinue | Select-Object -First 1
        $m = 'فشل تنصيب SQL Server (رمز ' + $code + '). '
        $help = Get-InstallHelp $code
        if ($help) { $m += $help } else { $m += 'أعد تشغيل الحاسوب ثم اضغط «السيرفر الرئيسي» مرة ثانية.' }
        if ($why) { $m += "`n" + $why.Matches[0].Groups[1].Value.Trim() }
        Stop-Setup ($m + "`n" + 'التفاصيل في: ' + (Join-Path $logDir 'Summary.txt'))
    }
} else { Say 'SQL Server منصّب مسبقا، يجري إعداده…' 5 80 }
Remove-InstallFiles

# ---------------------------------------------------------------- 2. network: password logins + port 1433 + SQL Browser + firewall
$instKey = (Get-ItemProperty 'HKLM:\SOFTWARE\Microsoft\Microsoft SQL Server\Instance Names\SQL').$Instance
if (-not $instKey) { Stop-Setup 'لم يتم العثور على SQL Server بعد التنصيب. أعد تشغيل الحاسوب ثم اضغط «السيرفر الرئيسي» مرة ثانية.' }
$base = "HKLM:\SOFTWARE\Microsoft\Microsoft SQL Server\$instKey\MSSQLServer"
Set-ItemProperty -Path $base -Name LoginMode -Value 2 -Type DWord
$tcp = "$base\SuperSocketNetLib\Tcp"
Set-ItemProperty -Path $tcp -Name Enabled -Value 1 -Type DWord
Set-ItemProperty -Path "$tcp\IPAll" -Name TcpDynamicPorts -Value ''
Set-ItemProperty -Path "$tcp\IPAll" -Name TcpPort -Value '1433'
Say 'فتح الاتصال للشبكة وإعادة تشغيل SQL Server…' 20 60
Set-Service -Name $svcName -StartupType Automatic
try { Restart-Service -Name $svcName -Force }
catch { Stop-Setup ('خدمة SQL Server لا تبدأ على هذا الحاسوب. أعد تشغيل الحاسوب ثم اضغط «السيرفر الرئيسي» مرة ثانية. إذا تكرر فاختر حاسوبا آخر ليكون السيرفر الرئيسي.' + "`n" + $_.Exception.Message) }
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
Say 'انتظار جاهزية SQL Server…' 15 45
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

# ---------------------------------------------------------------- 4. daily backup (keeps the last 10)
Say 'إعداد النسخ الاحتياطي اليومي…' 5 30
$backupDir = [string](Invoke-Sql "SELECT CAST(SERVERPROPERTY('InstanceDefaultBackupPath') AS nvarchar(4000))")
if ([string]::IsNullOrWhiteSpace($backupDir)) {
    $backupDir = 'C:\EngStoresBackup'
    New-Item -ItemType Directory -Force -Path $backupDir | Out-Null
    & icacls.exe $backupDir /grant ('NT Service\' + $svcName + ':(OI)(CI)M') | Out-Null
}
$backupScript = Join-Path $Work 'backup.ps1'   # run daily as SYSTEM: lives in the protected folder only
@"
`$ErrorActionPreference = 'Stop'
`$f = Join-Path '$backupDir' ('$Database' + '_' + (Get-Date -Format 'yyyy-MM-dd') + '.bak')
`$c = New-Object System.Data.SqlClient.SqlConnection 'Server=.\$Instance;Database=$Database;Integrated Security=True;Encrypt=False'
`$c.Open(); `$cmd = `$c.CreateCommand(); `$cmd.CommandTimeout = 3600
`$cmd.CommandText = "BACKUP DATABASE [$Database] TO DISK = N'" + `$f + "' WITH INIT, CHECKSUM"
[void]`$cmd.ExecuteNonQuery(); `$c.Close()
# the last 10 daily backups stay; older ones are replaced (only after today's was made)
Get-ChildItem '$backupDir' -Filter '${Database}_????-??-??.bak' | Sort-Object Name -Descending | Select-Object -Skip 10 | Remove-Item -Force
"@ | Set-Content -Path $backupScript -Encoding UTF8
$psExe    = Join-Path $env:windir 'System32\WindowsPowerShell\v1.0\powershell.exe'
$action   = New-ScheduledTaskAction -Execute $psExe -Argument ('-NoProfile -ExecutionPolicy Bypass -File "' + $backupScript + '"')
$trigger  = New-ScheduledTaskTrigger -Daily -At '14:00'
# also on battery (a laptop as the main server): Windows' default would leave the task waiting
$settings = New-ScheduledTaskSettingsSet -StartWhenAvailable -ExecutionTimeLimit (New-TimeSpan -Hours 2) -AllowStartIfOnBatteries -DontStopIfGoingOnBatteries
Register-ScheduledTask -TaskName 'EngStores Daily Backup' -Action $action -Trigger $trigger -Settings $settings -User 'SYSTEM' -RunLevel Highest -Force | Out-Null
Protect-Folder $Work   # also covers backup.ps1
# older versions kept the setup files and the SYSTEM backup script in a folder users could write: remove it
if (Test-Path -LiteralPath $OldWork) { Remove-Item -LiteralPath $OldWork -Recurse -Force -ErrorAction SilentlyContinue }

if ($reboot) { Done ('OK|' + $backupDir + '|reboot') } else { Done ('OK|' + $backupDir) }
exit 0
