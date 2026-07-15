param(
    [string]$ServiceName = "NetworkHealthMonitorWorker",
    [string]$WorkerPath = (Join-Path $PSScriptRoot "..\worker\NetworkHealthMonitor.Worker.exe")
)

$service = Get-Service -Name $ServiceName -ErrorAction SilentlyContinue
if ($null -eq $service) {
    Write-Host "Bulunamadi: $ServiceName"
    exit 2
}

$wmi = Get-CimInstance -ClassName Win32_Service -Filter "Name='$ServiceName'"
$health = & (Join-Path $PSScriptRoot "health-check.ps1") -ServiceName $ServiceName -WorkerPath $WorkerPath -Quiet

[pscustomobject]@{
    Name = $service.Name
    DisplayName = $service.DisplayName
    Status = $service.Status.ToString()
    StartType = $wmi.StartMode
    Account = $wmi.StartName
    BinaryPath = $wmi.PathName
    HealthExitCode = $LASTEXITCODE
} | Format-List
