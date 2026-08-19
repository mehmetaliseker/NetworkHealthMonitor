param(
    [string]$BinaryPath = (Join-Path (Split-Path -Parent $PSScriptRoot) "Worker\NetworkHealthMonitor.Worker.exe"),
    [string]$ServiceName = "NetworkHealthMonitorWorker",
    [string]$DisplayName = "Network Health Monitor Worker",
    [string]$UiUser = "$env:USERDOMAIN\$env:USERNAME"
)

$ErrorActionPreference = "Stop"
& (Join-Path $PSScriptRoot "Install-WorkerService.ps1") `
    -WorkerPath $BinaryPath `
    -ServiceName $ServiceName `
    -DisplayName $DisplayName `
    -DataUser $UiUser
