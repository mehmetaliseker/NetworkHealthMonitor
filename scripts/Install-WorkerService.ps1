param(
    [string]$WorkerPath = (Join-Path (Split-Path -Parent $PSScriptRoot) "Worker\NetworkHealthMonitor.Worker.exe"),
    [string]$ServiceName = "NetworkHealthMonitorWorker",
    [string]$DisplayName = "Network Health Monitor Worker",
    [string]$DataRoot = (Join-Path $env:ProgramData "NetworkHealthMonitor"),
    [string]$DataUser = "$env:USERDOMAIN\$env:USERNAME"
)

$ErrorActionPreference = "Stop"

function Assert-Administrator {
    $identity = [Security.Principal.WindowsIdentity]::GetCurrent()
    $principal = New-Object Security.Principal.WindowsPrincipal($identity)
    if (-not $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
        throw "This script must be run from an elevated PowerShell session."
    }
}

function Set-RestrictedDataAcl {
    param([string]$Path)

    $item = Get-Item -LiteralPath $Path
    $inheritanceFlags = "None"
    if ($item.PSIsContainer) {
        $inheritanceFlags = "ContainerInherit,ObjectInherit"
    }

    $acl = Get-Acl -LiteralPath $Path
    $acl.SetAccessRuleProtection($true, $false)
    $rules = @(
        [System.Security.AccessControl.FileSystemAccessRule]::new("NT AUTHORITY\SYSTEM", "FullControl", $inheritanceFlags, "None", "Allow"),
        [System.Security.AccessControl.FileSystemAccessRule]::new("BUILTIN\Administrators", "FullControl", $inheritanceFlags, "None", "Allow")
    )

    if (-not [string]::IsNullOrWhiteSpace($DataUser)) {
        $rules += [System.Security.AccessControl.FileSystemAccessRule]::new($DataUser, "Modify", $inheritanceFlags, "None", "Allow")
    }

    foreach ($rule in $rules) {
        $acl.SetAccessRule($rule)
    }

    Set-Acl -LiteralPath $Path -AclObject $acl
}

function Invoke-Sc {
    param([string[]]$Arguments)

    $output = & sc.exe @Arguments 2>&1
    if ($LASTEXITCODE -ne 0) {
        throw "sc.exe $($Arguments -join ' ') failed. ExitCode=$LASTEXITCODE Output=$($output -join ' ')"
    }
}

Assert-Administrator

$resolvedWorkerPath = (Resolve-Path -LiteralPath $WorkerPath -ErrorAction Stop).Path
if (-not (Test-Path -LiteralPath $resolvedWorkerPath -PathType Leaf)) {
    throw "Worker executable was not found: $resolvedWorkerPath"
}

$dataDirectories = @(
    $DataRoot,
    (Join-Path $DataRoot "data"),
    (Join-Path $DataRoot "config"),
    (Join-Path $DataRoot "logs"),
    (Join-Path $DataRoot "backups")
)
New-Item -ItemType Directory -Force -Path $dataDirectories | Out-Null
foreach ($directory in $dataDirectories) {
    Set-RestrictedDataAcl -Path $directory
}

$quotedWorkerPath = '"' + $resolvedWorkerPath + '"'
$service = Get-Service -Name $ServiceName -ErrorAction SilentlyContinue
if ($null -eq $service) {
    Invoke-Sc -Arguments @("create", $ServiceName, "binPath=", $quotedWorkerPath, "start=", "demand", "DisplayName=", $DisplayName)
}
else {
    Invoke-Sc -Arguments @("config", $ServiceName, "binPath=", $quotedWorkerPath, "start=", "demand", "DisplayName=", $DisplayName)
}

Set-Service -Name $ServiceName -StartupType Manual

$serviceRegPath = "HKLM:\SYSTEM\CurrentControlSet\Services\$ServiceName"
if (Test-Path -LiteralPath $serviceRegPath) {
    New-ItemProperty -LiteralPath $serviceRegPath -Name DelayedAutoStart -Value 0 -PropertyType DWord -Force | Out-Null
}

try {
    Invoke-Sc -Arguments @("failure", $ServiceName, "reset=", "0", "actions=", "")
}
catch {
    Write-Warning "Service recovery actions could not be cleared: $($_.Exception.Message)"
}

$escapedServiceName = $ServiceName.Replace("'", "''")
$serviceConfig = Get-CimInstance -ClassName Win32_Service -Filter "Name='$escapedServiceName'" -ErrorAction Stop
if ($serviceConfig.StartMode -ne "Manual") {
    Set-Service -Name $ServiceName -StartupType Manual
    $serviceConfig = Get-CimInstance -ClassName Win32_Service -Filter "Name='$escapedServiceName'" -ErrorAction Stop
}

if ($serviceConfig.StartMode -ne "Manual") {
    throw "Service startup mode is not Manual. Current value: $($serviceConfig.StartMode)"
}

$service = Get-Service -Name $ServiceName -ErrorAction Stop
Write-Host "Worker Windows Service installed."
Write-Host "ServiceName=$ServiceName"
Write-Host "DisplayName=$DisplayName"
Write-Host "BinaryPath=$resolvedWorkerPath"
Write-Host "StartupType=Manual"
Write-Host "Status=$($service.Status)"
Write-Host "The service was not started by this installer."
