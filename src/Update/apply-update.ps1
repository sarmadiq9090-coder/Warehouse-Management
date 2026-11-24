# Replaces the program files once the program has closed, keeps a copy of the old files, and rolls back on error.
# Embedded in the program (build: /resource) and run by Updater.Install.
param([int]$ProcessId, [string]$Source, [string]$Target, [string]$Exe, [string]$Log)
$ErrorActionPreference = 'Stop'
function L($m) { Add-Content -Path $Log -Value ((Get-Date -Format 'HH:mm:ss') + ' ' + $m) -Encoding UTF8 }
try { Wait-Process -Id $ProcessId -Timeout 60 -ErrorAction SilentlyContinue } catch { }
Start-Sleep -Milliseconds 500
$backup = Join-Path $Target ('.update-backup\' + (Get-Date -Format 'yyyyMMdd-HHmmss'))
$copied = New-Object System.Collections.Generic.List[string]
try {
    Get-ChildItem -Path $Source -Recurse -File | ForEach-Object {
        $rel = $_.FullName.Substring($Source.TrimEnd('\').Length + 1)
        $dst = Join-Path $Target $rel
        if (Test-Path -LiteralPath $dst) {
            $b = Join-Path $backup $rel
            New-Item -ItemType Directory -Force -Path (Split-Path $b) | Out-Null
            Copy-Item -LiteralPath $dst -Destination $b -Force
        }
        New-Item -ItemType Directory -Force -Path (Split-Path $dst) | Out-Null
        $copied.Add($rel)        # before the copy: a file that fails half-way is restored too
        Copy-Item -LiteralPath $_.FullName -Destination $dst -Force
        L ('updated ' + $rel)
    }
    L 'update finished'
} catch {
    L ('ERROR ' + $_.Exception.Message + ' - restoring the previous version')
    foreach ($rel in $copied) {
        $b = Join-Path $backup $rel
        if (Test-Path -LiteralPath $b) { Copy-Item -LiteralPath $b -Destination (Join-Path $Target $rel) -Force }
    }
}
Start-Process -FilePath $Exe
