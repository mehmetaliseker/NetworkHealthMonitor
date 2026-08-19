param(
    [string]$ServiceName = "NetworkHealthMonitorWorker",
    [switch]$RemoveData
)

$ErrorActionPreference = "Stop"

function Assert-Administrator {
    $identity = [Security.Principal.WindowsIdentity]::GetCurrent()
    $principal = New-Object Security.Principal.WindowsPrincipal($identity)
    if (-not $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
        throw "This script must be run from an elevated PowerShell session."
    }
}

Assert-Administrator

$service = Get-Service -Name $ServiceName -ErrorAction SilentlyContinue
if ($null -ne $service) {
    if ($service.Status -ne "Stopped") {
        Stop-Service -Name $ServiceName -ErrorAction Stop
        (Get-Service -Name $ServiceName).WaitForStatus("Stopped", [TimeSpan]::FromSeconds(30))
    }

    $output = & sc.exe delete $ServiceName 2>&1
    if ($LASTEXITCODE -ne 0) {
        throw "sc.exe delete failed. ExitCode=$LASTEXITCODE Output=$($output -join ' ')"
    }

    Write-Host "Service removed: $ServiceName"
}
else {
    Write-Host "Service is not installed: $ServiceName"
}

if ($RemoveData) {
    $programData = Join-Path $env:ProgramData "NetworkHealthMonitor"
    if (Test-Path -LiteralPath $programData) {
        Remove-Item -LiteralPath $programData -Recurse -Force
        Write-Host "ProgramData removed: $programData"
    }
}
