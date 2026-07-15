param(
    [string]$ServiceName = "NetworkHealthMonitorWorker",
    [string]$WorkerPath = (Join-Path $PSScriptRoot "..\worker\NetworkHealthMonitor.Worker.exe"),
    [string]$UiPath = (Join-Path $PSScriptRoot "..\ui\NetworkHealthMonitor.exe"),
    [string]$DataRoot = (Join-Path $env:ProgramData "NetworkHealthMonitor"),
    [int]$HeartbeatMaxAgeSeconds = 120,
    [int64]$MinimumFreeBytes = 1073741824,
    [switch]$SkipBackupCheck,
    [switch]$SkipScheduledPingCheck
)

$ErrorActionPreference = "Stop"
$failures = New-Object System.Collections.Generic.List[string]

function Write-Result {
    param(
        [string]$Name,
        [bool]$Passed,
        [string]$Detail = "",
        [bool]$Mandatory = $true
    )

    $prefix = if ($Passed) { "PASS" } else { "FAIL" }
    $line = if ([string]::IsNullOrWhiteSpace($Detail)) { "$prefix $Name" } else { "$prefix $Name - $Detail" }
    if ($Passed) {
        Write-Host $line -ForegroundColor Green
    }
    else {
        Write-Host $line -ForegroundColor Red
        if ($Mandatory) { $script:failures.Add($Name) | Out-Null }
    }
}

function Resolve-ExistingPath([string]$Path) {
    try { return (Resolve-Path $Path -ErrorAction Stop).Path } catch { return $Path }
}

function Get-ExeVersion([string]$Path) {
    if (-not (Test-Path $Path)) { return "" }
    $version = (Get-Item $Path).VersionInfo.ProductVersion
    if ([string]::IsNullOrWhiteSpace($version)) { $version = (Get-Item $Path).VersionInfo.FileVersion }
    return ($version -replace '\+.*$', '').Trim()
}

$worker = Resolve-ExistingPath $WorkerPath
$ui = Resolve-ExistingPath $UiPath
$db = Join-Path $DataRoot "data\network_health_monitor.db"

Write-Result "Worker exe var" (Test-Path $worker) $worker
Write-Result "UI exe var" (Test-Path $ui) $ui

$service = Get-Service -Name $ServiceName -ErrorAction SilentlyContinue
Write-Result "Service kurulu" ($null -ne $service) $ServiceName
$serviceStatusText = if ($service) { $service.Status.ToString() } else { "Missing" }
Write-Result "Service Running" ($service -and $service.Status -eq "Running") $serviceStatusText

$wmi = Get-CimInstance -ClassName Win32_Service -Filter "Name='$($ServiceName.Replace("'","''"))'" -ErrorAction SilentlyContinue
$startupOk = $wmi -and ($wmi.StartMode -eq "Auto")
$startupText = if ($wmi) { $wmi.StartMode } else { "Missing" }
Write-Result "Startup type Automatic" $startupOk $startupText

$recoveryOutput = & sc.exe qfailure $ServiceName 2>&1
$recoveryOk = $LASTEXITCODE -eq 0 -and ($recoveryOutput -join "`n").Contains("RESTART")
Write-Result "Recovery actions mevcut" $recoveryOk (($recoveryOutput | Select-Object -First 3) -join " ")

try {
    New-Item -ItemType Directory -Force -Path $DataRoot | Out-Null
    $testFile = Join-Path $DataRoot ".readiness-write-test"
    "ok" | Set-Content -LiteralPath $testFile -Encoding ASCII
    Remove-Item -LiteralPath $testFile -Force
    Write-Result "ProgramData yazilabilir" $true $DataRoot
}
catch {
    Write-Result "ProgramData yazilabilir" $false $_.Exception.Message
}

Write-Result "SQLite DB var" (Test-Path $db) $db
if ((Test-Path $worker) -and (Test-Path $db)) {
    $health = & $worker --health-check --data-dir $DataRoot --heartbeat-max-age-seconds $HeartbeatMaxAgeSeconds 2>&1
    $healthOk = $LASTEXITCODE -eq 0
    Write-Result "SQLite acilabilir / outbox tabloları acilabilir / heartbeat guncel" $healthOk (($health | Select-Object -First 6) -join " | ")
}
else {
    Write-Result "SQLite acilabilir / outbox tabloları acilabilir / heartbeat guncel" $false "Worker veya DB yok"
}

if (-not $SkipScheduledPingCheck -and (Test-Path $worker)) {
    $runOnce = & $worker --run-once --data-dir $DataRoot 2>&1
    Write-Result "Scheduled test ping calistirilabilir" ($LASTEXITCODE -eq 0) (($runOnce | Select-Object -First 4) -join " ")
}

if (-not $SkipBackupCheck -and (Test-Path $db)) {
    $backupRoot = Join-Path $env:TEMP ("nhm-readiness-backup-" + [guid]::NewGuid().ToString("N"))
    try {
        $backupOutput = & (Join-Path $PSScriptRoot "backup-data.ps1") -ServiceName $ServiceName -DataRoot $DataRoot -DestinationRoot $backupRoot -NoServiceStop 2>&1
        Write-Result "Backup alinabiliyor" ($LASTEXITCODE -eq 0) (($backupOutput | Select-Object -Last 1) -join "")
    }
    finally {
        if (Test-Path $backupRoot) { Remove-Item -LiteralPath $backupRoot -Recurse -Force }
    }
}

try {
    $root = [System.IO.Path]::GetPathRoot($DataRoot)
    $drive = [System.IO.DriveInfo]::new($root)
    Write-Result "Yeterli disk alani" ($drive.AvailableFreeSpace -ge $MinimumFreeBytes) ("FreeBytes=$($drive.AvailableFreeSpace)")
}
catch {
    Write-Result "Yeterli disk alani" $false $_.Exception.Message
}

$uiVersion = Get-ExeVersion $ui
$workerVersion = Get-ExeVersion $worker
$versionsMatch = -not [string]::IsNullOrWhiteSpace($uiVersion) -and -not [string]::IsNullOrWhiteSpace($workerVersion) -and ($uiVersion.Split('+')[0] -eq $workerVersion.Split('+')[0])
Write-Result "UI ve Worker versiyonlari eslesiyor" $versionsMatch "UI=$uiVersion Worker=$workerVersion"

if ($failures.Count -gt 0) {
    Write-Host "Production readiness failed: $($failures -join ', ')" -ForegroundColor Red
    exit 1
}

Write-Host "Production readiness passed." -ForegroundColor Green
exit 0
