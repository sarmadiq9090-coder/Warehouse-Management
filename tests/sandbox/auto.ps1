# Inside Windows Sandbox: a user runs the one-file setup and does nothing else. Started by make-sandbox.ps1 -Merged … -Auto.
$ErrorActionPreference = 'Continue'
$log = 'C:\out\result.txt'
function L($m) { "$(Get-Date -Format 'HH:mm:ss')  $m" | Add-Content $log -Encoding UTF8 }
Add-Type -AssemblyName UIAutomationClient, UIAutomationTypes, System.Windows.Forms, System.Drawing
function Windows() {
    $r = @()
    foreach ($w in [Windows.Automation.AutomationElement]::RootElement.FindAll('Children', [Windows.Automation.Condition]::TrueCondition)) {
        try { $p = Get-Process -Id $w.Current.ProcessId -ErrorAction Stop; if ($p.ProcessName -match 'full|Inventory|EngStores') { $r += $w } } catch { }
    }
    return $r
}
function Shot($n) {
    try {
        $b = [Windows.Forms.SystemInformation]::VirtualScreen; $i = New-Object Drawing.Bitmap $b.Width, $b.Height; $g = [Drawing.Graphics]::FromImage($i)
        $g.CopyFromScreen($b.Location, [Drawing.Point]::Empty, $b.Size); $i.Save("C:\out\$n.png"); $g.Dispose(); $i.Dispose()
    } catch { L "no picture: $_" }
}
Copy-Item 'C:\in\full.exe' 'C:\Users\Public\full.exe'
L 'the setup is started (nothing else is clicked)'
Start-Process 'C:\Users\Public\full.exe' -ArgumentList '/lang=ar'
# the only click: «أوافق، ابدأ التنصيب»
for ($k = 0; $k -lt 20; $k++) {
    Start-Sleep 2; $hit = $false
    foreach ($w in Windows) {
        $b = $w.FindFirst('Descendants', (New-Object Windows.Automation.PropertyCondition([Windows.Automation.AutomationElement]::NameProperty, 'أوافق، ابدأ التنصيب')))
        if ($b) { Shot 'agree'; $b.GetCurrentPattern([Windows.Automation.InvokePattern]::Pattern).Invoke(); L 'agreed'; $hit = $true }
    }
    if ($hit) { break }
}
$seen = ''; $end = (Get-Date).AddMinutes(25); $done = $null
while ((Get-Date) -lt $end -and -not $done) {
    Start-Sleep 3
    foreach ($w in Windows) {
        $n = $w.Current.Name
        if ($n -ne $seen) { $seen = $n; L "window: $n"; Start-Sleep 2; Shot ('w' + (Get-Date -Format 'HHmmss')) }
        if ($n -eq 'تم التنصيب') { $done = $w }
    }
}
if (-not $done) { L 'FAIL  the final window did not appear'; Shot 'fail' }
else {
    Shot 'done'
    foreach ($e in $done.FindAll('Descendants', [Windows.Automation.Condition]::TrueCondition)) { if ($e.Current.Name) { L ("   " + $e.Current.Name) } }
    $box = $done.FindFirst('Descendants', (New-Object Windows.Automation.PropertyCondition([Windows.Automation.AutomationElement]::ControlTypeProperty, [Windows.Automation.ControlType]::Edit)))
    $pw = ''; try { $pw = $box.GetCurrentPattern([Windows.Automation.ValuePattern]::Pattern).Current.Value } catch { }
    L ('PASS  final window, server password of ' + $pw.Length + ' characters')
    $c = New-Object System.Data.SqlClient.SqlConnection 'Server=.\SQLEXPRESS;Database=EngStores;Integrated Security=True;Encrypt=False'; $c.Open()
    $m = $c.CreateCommand(); $m.CommandText = "SELECT CAST((SELECT COUNT(*) FROM sys.tables) AS varchar) + ' tables, stores: ' + CAST((SELECT COUNT(*) FROM stor) AS varchar) + ', users: ' + CAST((SELECT COUNT(*) FROM pass) AS varchar)"
    L ('PASS  database on the server: ' + $m.ExecuteScalar()); $c.Close()
    [Windows.Forms.SendKeys]::SendWait('{ESC}'); Start-Sleep 2
    try { $done.GetCurrentPattern([Windows.Automation.WindowPattern]::Pattern).Close() } catch { }
    $end = (Get-Date).AddSeconds(60); $login = $false
    while (-not $login -and (Get-Date) -lt $end) { Start-Sleep 2; foreach ($w in Windows) { if ($w.Current.Name -match 'تسجيل الدخول') { $login = $true } } }
    Shot 'login'
    if ($login) { L 'PASS  the program is open at sign-in, on the server' } else { L ('FAIL  no sign-in window: ' + ((Windows | ForEach-Object { $_.Current.Name }) -join ' | ')) }
    $ini = Get-ChildItem C:\Users -Recurse -Force -Filter settings.ini -ErrorAction SilentlyContinue | Where-Object { $_.FullName -match 'Inventory Management' } | Select-Object -First 1
    $t = Get-Content $ini.FullName -Encoding UTF8; L ($ini.FullName + ': ' + (($t | Where-Object { $_ -match '^(Mode|SqlServer|SqlDatabase|SqlUser)=' }) -join ', '))
    if ($t -match '^Mode=sql') { L 'PASS  saved: this computer uses the server' } else { L 'FAIL  settings' }
}
'finished' | Set-Content 'C:\out\finished.txt'
