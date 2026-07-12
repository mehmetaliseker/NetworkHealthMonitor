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

$service = Get-Service -Name $ServiceName -ErrorAction SilentlyContinue
if ($null -eq $service) {
    Write-Host "Servis bulunamadi: $ServiceName"
    return
}

if ($service.Status -ne "Stopped") {
    Stop-Service -Name $ServiceName -ErrorAction Stop
    $service.WaitForStatus("Stopped", [TimeSpan]::FromSeconds(30))
}

& sc.exe delete $ServiceName | Out-Null
Write-Host "Servis kaldirildi: $ServiceName"
