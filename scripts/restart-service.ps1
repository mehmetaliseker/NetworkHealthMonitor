param(
    [string]$ServiceName = "NetworkHealthMonitorWorker"
)

$ErrorActionPreference = "Stop"
& (Join-Path $PSScriptRoot "stop-service.ps1") -ServiceName $ServiceName
& (Join-Path $PSScriptRoot "start-service.ps1") -ServiceName $ServiceName
