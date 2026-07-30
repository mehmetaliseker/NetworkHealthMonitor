param(
    [string]$ServiceName = "NetworkHealthMonitorWorker",
    [int]$TimeoutSeconds = 30
)

$ErrorActionPreference = "Stop"
& (Join-Path $PSScriptRoot "Stop-Worker.ps1") -ServiceName $ServiceName -TimeoutSeconds $TimeoutSeconds
