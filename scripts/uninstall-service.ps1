param(
    [string]$ServiceName = "NetworkHealthMonitorWorker",
    [switch]$PurgeData
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
if ($null -ne $service) {
    if ($service.Status -ne "Stopped") {
        Stop-Service -Name $ServiceName -ErrorAction Stop
        (Get-Service -Name $ServiceName).WaitForStatus("Stopped", [TimeSpan]::FromSeconds(30))
    }
    & sc.exe delete $ServiceName | Out-Null
    Write-Host "Servis kaldirildi: $ServiceName"
}
else {
    Write-Host "Servis bulunamadi: $ServiceName"
}

if ($PurgeData) {
    $programData = Join-Path $env:ProgramData "NetworkHealthMonitor"
    if (Test-Path $programData) {
        Remove-Item -LiteralPath $programData -Recurse -Force
        Write-Host "ProgramData verisi silindi: $programData"
    }
}
