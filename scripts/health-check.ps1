param(
    [string]$ServiceName = "NetworkHealthMonitorWorker",
    [string]$WorkerPath = (Join-Path $PSScriptRoot "..\worker\NetworkHealthMonitor.Worker.exe"),
    [string]$DataRoot = (Join-Path $env:ProgramData "NetworkHealthMonitor"),
    [int]$HeartbeatMaxAgeSeconds = 120,
    [switch]$SkipServiceCheck,
    [switch]$SkipHeartbeatCheck,
    [switch]$Quiet
)

$ErrorActionPreference = "Stop"
$errors = New-Object System.Collections.Generic.List[string]
$programData = $DataRoot
$db = Join-Path $programData "data\network_health_monitor.db"
$worker = $WorkerPath
try { $worker = (Resolve-Path $WorkerPath).Path } catch {}

function Add-CheckError([string]$message) {
    $script:errors.Add($message) | Out-Null
    if (-not $Quiet) { Write-Host "FAIL $message" -ForegroundColor Red }
}

function Write-CheckInfo([string]$message) {
    if (-not $Quiet) { Write-Host "OK   $message" -ForegroundColor Green }
}

if (-not $SkipServiceCheck) {
    $service = Get-Service -Name $ServiceName -ErrorAction SilentlyContinue
    if ($null -eq $service) { Add-CheckError "Service mevcut degil: $ServiceName" } else { Write-CheckInfo "Service mevcut: $($service.Status)" }
    if ($service -and $service.Status -ne "Running") { Add-CheckError "Service Running degil: $($service.Status)" }
}
if (-not (Test-Path $worker)) { Add-CheckError "Worker exe yok: $worker" } else { Write-CheckInfo "Worker exe mevcut" }
if (-not (Test-Path $programData)) { Add-CheckError "ProgramData yok: $programData" } else { Write-CheckInfo "ProgramData erisilebilir" }
if (-not (Test-Path $db)) { Add-CheckError "SQLite DB yok: $db" }

if ((Test-Path $db) -and (Test-Path $worker)) {
    $workerArgs = @("--health-check", "--data-dir", $programData, "--heartbeat-max-age-seconds", $HeartbeatMaxAgeSeconds)
    if ($SkipHeartbeatCheck -or $SkipServiceCheck) { $workerArgs += "--skip-heartbeat-check" }
    $output = & $worker @workerArgs 2>&1
    $exitCode = $LASTEXITCODE
    if (-not $Quiet -and $output) { $output | ForEach-Object { Write-Host $_ } }
    if ($exitCode -ne 0) { Add-CheckError "Worker health-check basarisiz. ExitCode=$exitCode" } else { Write-CheckInfo "Worker health-check basarili" }
}

if ($errors.Count -gt 0) {
    if (-not $Quiet) { $errors | ForEach-Object { Write-Host $_ -ForegroundColor Red } }
    exit 1
}

exit 0
