param(
    [string]$ServiceName = "NetworkHealthMonitorWorker"
)

$ErrorActionPreference = "Stop"

function Assert-Administrator {
    $identity = [Security.Principal.WindowsIdentity]::GetCurrent()
    $principal = [Security.Principal.WindowsPrincipal]::new($identity)
    if (-not $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
        throw "Bu script yonetici PowerShell oturumunda calistirilmalidir."
    }
}

Assert-Administrator

$service = Get-Service -Name $ServiceName -ErrorAction Stop
if ($service.Status -eq "Stopped") {
    Write-Host "Servis zaten durdurulmus: $ServiceName"
    return
}

Stop-Service -Name $ServiceName
$service.WaitForStatus("Stopped", [TimeSpan]::FromSeconds(30))
Write-Host "Servis durduruldu: $ServiceName"
