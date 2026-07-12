param(
    [string]$ServiceName = "NetworkHealthMonitorWorker"
)

$service = Get-Service -Name $ServiceName -ErrorAction SilentlyContinue
if ($null -eq $service) {
    Write-Host "Bulunamadi: $ServiceName"
    exit 2
}

$wmi = Get-CimInstance -ClassName Win32_Service -Filter "Name='$ServiceName'"
[pscustomobject]@{
    Name = $service.Name
    DisplayName = $service.DisplayName
    Status = $service.Status.ToString()
    StartType = $wmi.StartMode
    Account = $wmi.StartName
    BinaryPath = $wmi.PathName
} | Format-List
