param(
    [string]$ServiceName = "NetworkHealthMonitorWorker",
    [string]$DataRoot = (Join-Path $env:ProgramData "NetworkHealthMonitor"),
    [string]$DestinationRoot = (Join-Path $DataRoot "backups"),
    [switch]$NoServiceStop
)

$ErrorActionPreference = "Stop"
$programData = $DataRoot
$dataDir = Join-Path $programData "data"
$configDir = Join-Path $programData "config"
$db = Join-Path $dataDir "network_health_monitor.db"
if (-not (Test-Path $db)) { throw "Veritabani bulunamadi: $db" }

$service = Get-Service -Name $ServiceName -ErrorAction SilentlyContinue
$wasRunning = $service -and $service.Status -eq "Running"
if ($wasRunning -and -not $NoServiceStop) {
    Stop-Service -Name $ServiceName
    (Get-Service -Name $ServiceName).WaitForStatus("Stopped", [TimeSpan]::FromSeconds(30))
}

try {
    $backupDir = Join-Path $DestinationRoot (Get-Date -Format "yyyyMMdd-HHmmss")
    New-Item -ItemType Directory -Force -Path $backupDir | Out-Null
    Copy-Item -LiteralPath $db -Destination $backupDir -Force
    foreach ($suffix in @("-wal", "-shm")) {
        $sidecar = "$db$suffix"
        if (Test-Path $sidecar) { Copy-Item -LiteralPath $sidecar -Destination $backupDir -Force }
    }
    if (Test-Path $configDir) { Copy-Item -LiteralPath $configDir -Destination (Join-Path $backupDir "config") -Recurse -Force }
    "Secrets in settings.json are DPAPI LocalMachine protected when saved by the application." | Set-Content -Path (Join-Path $backupDir "README.txt") -Encoding UTF8
    Write-Output $backupDir
}
finally {
    if ($wasRunning -and -not $NoServiceStop) {
        Start-Service -Name $ServiceName
    }
}
