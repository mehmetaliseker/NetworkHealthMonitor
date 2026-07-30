param(
    [string]$ServiceName = "NetworkHealthMonitorWorker",
    [Alias("PurgeData")]
    [switch]$RemoveData
)

$ErrorActionPreference = "Stop"
& (Join-Path $PSScriptRoot "Uninstall-WorkerService.ps1") -ServiceName $ServiceName -RemoveData:$RemoveData
