param(
    [string]$BinaryPath = (Join-Path $PSScriptRoot "..\worker\NetworkHealthMonitor.Worker.exe"),
    [string]$ServiceName = "NetworkHealthMonitorWorker",
    [string]$DisplayName = "Network Health Monitor Worker"
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

$resolvedBinary = (Resolve-Path $BinaryPath).Path
if (-not (Test-Path $resolvedBinary)) {
    throw "Worker exe bulunamadi: $resolvedBinary"
}

$programData = Join-Path $env:ProgramData "NetworkHealthMonitor"
$logs = Join-Path $programData "logs"
$backups = Join-Path $programData "backups"
New-Item -ItemType Directory -Force -Path $programData, $logs, $backups | Out-Null

$testFile = Join-Path $programData ".access-test"
"ok" | Set-Content -Path $testFile -Encoding ASCII
Remove-Item -Path $testFile -Force

$quotedBinary = '"' + $resolvedBinary + '"'
$service = Get-Service -Name $ServiceName -ErrorAction SilentlyContinue
if ($null -eq $service) {
    New-Service -Name $ServiceName -BinaryPathName $quotedBinary -DisplayName $DisplayName -StartupType Automatic | Out-Null
}
else {
    & sc.exe config $ServiceName binPath= $quotedBinary DisplayName= $DisplayName | Out-Null
}

& sc.exe config $ServiceName start= delayed-auto | Out-Null
& sc.exe failure $ServiceName reset= 86400 actions= restart/60000/restart/60000/restart/300000 | Out-Null
& sc.exe failureflag $ServiceName 1 | Out-Null

Write-Host "Servis kuruldu veya guncellendi: $ServiceName"
Write-Host "Binary: $resolvedBinary"
Write-Host "Veri dizini: $programData"
