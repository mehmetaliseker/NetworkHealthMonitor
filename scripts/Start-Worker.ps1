param(
    [string]$ServiceName = "NetworkHealthMonitorWorker",
    [int]$TimeoutSeconds = 30
)

$ErrorActionPreference = "Stop"

$service = Get-Service -Name $ServiceName -ErrorAction SilentlyContinue
if ($null -eq $service) {
    Write-Host "Worker service is not installed: $ServiceName"
    exit 1
}

if ($service.Status -eq "Running") {
    Write-Host "Service is already running: $ServiceName"
    return
}

Start-Service -Name $ServiceName -ErrorAction Stop
(Get-Service -Name $ServiceName).WaitForStatus("Running", [TimeSpan]::FromSeconds($TimeoutSeconds))
Write-Host "Service started: $ServiceName"
