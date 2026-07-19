param(
    [string]$ServiceName = "NetworkHealthMonitorWorker",
    [string]$WorkerPath = (Join-Path $PSScriptRoot "..\worker\NetworkHealthMonitor.Worker.exe"),
    [int]$HeartbeatMaxAgeSeconds = 120,
    [switch]$Quiet
)

$ErrorActionPreference = "Stop"

& (Join-Path $PSScriptRoot "health-check.ps1") `
    -ServiceName $ServiceName `
    -WorkerPath $WorkerPath `
    -HeartbeatMaxAgeSeconds $HeartbeatMaxAgeSeconds `
    -Quiet:$Quiet

exit $LASTEXITCODE
