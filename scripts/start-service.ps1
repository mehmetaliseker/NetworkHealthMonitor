param(
    [string]$ServiceName = "NetworkHealthMonitorWorker",
    [int]$TimeoutSeconds = 30
)

$ErrorActionPreference = "Stop"
& (Join-Path $PSScriptRoot "Start-Worker.ps1") -ServiceName $ServiceName -TimeoutSeconds $TimeoutSeconds
