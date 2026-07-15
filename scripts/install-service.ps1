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
if (-not (Test-Path $resolvedBinary)) { throw "Worker exe bulunamadi: $resolvedBinary" }

$programData = Join-Path $env:ProgramData "NetworkHealthMonitor"
$dirs = @(
    $programData,
    (Join-Path $programData "data"),
    (Join-Path $programData "config"),
    (Join-Path $programData "logs"),
    (Join-Path $programData "backups")
)
New-Item -ItemType Directory -Force -Path $dirs | Out-Null

$acl = Get-Acl $programData
$rule = New-Object System.Security.AccessControl.FileSystemAccessRule("NT AUTHORITY\SYSTEM", "FullControl", "ContainerInherit,ObjectInherit", "None", "Allow")
$acl.SetAccessRule($rule)
$usersRule = New-Object System.Security.AccessControl.FileSystemAccessRule("BUILTIN\Users", "Modify", "ContainerInherit,ObjectInherit", "None", "Allow")
$acl.SetAccessRule($usersRule)
Set-Acl -Path $programData -AclObject $acl

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

Start-Service -Name $ServiceName
(Get-Service -Name $ServiceName).WaitForStatus("Running", [TimeSpan]::FromSeconds(30))

$deadline = (Get-Date).AddSeconds(45)
do {
    Start-Sleep -Seconds 2
    $health = & (Join-Path $PSScriptRoot "health-check.ps1") -ServiceName $ServiceName -WorkerPath $resolvedBinary -Quiet
    if ($LASTEXITCODE -eq 0) {
        Write-Host "Servis kuruldu ve saglik kontrolu basarili: $ServiceName"
        exit 0
    }
} while ((Get-Date) -lt $deadline)

throw "Servis baslatildi ancak heartbeat saglik kontrolu zamaninda basarili olmadi."
