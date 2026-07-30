param(
    [string]$ServiceName = "NetworkHealthMonitorWorker",
    [int]$TimeoutSeconds = 30
)

$ErrorActionPreference = "Stop"
& (Join-Path $PSScriptRoot "Restart-Worker.ps1") -ServiceName $ServiceName -TimeoutSeconds $TimeoutSeconds
