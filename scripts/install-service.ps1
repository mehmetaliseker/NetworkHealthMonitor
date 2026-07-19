param(
    [string]$BinaryPath = (Join-Path $PSScriptRoot "..\worker\NetworkHealthMonitor.Worker.exe"),
    [string]$ServiceName = "NetworkHealthMonitorWorker",
    [string]$DisplayName = "Network Health Monitor Worker",
    [string]$UiUser = "$env:USERDOMAIN\$env:USERNAME"
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

function Set-RestrictedDataAcl([string]$Path) {
    $item = Get-Item -LiteralPath $Path
    $inheritanceFlags = if ($item.PSIsContainer) { "ContainerInherit,ObjectInherit" } else { "None" }
    $acl = Get-Acl $Path
    $acl.SetAccessRuleProtection($true, $false)

    $rules = @(
        New-Object System.Security.AccessControl.FileSystemAccessRule("NT AUTHORITY\SYSTEM", "FullControl", $inheritanceFlags, "None", "Allow"),
        New-Object System.Security.AccessControl.FileSystemAccessRule("BUILTIN\Administrators", "FullControl", $inheritanceFlags, "None", "Allow")
    )

    if (-not [string]::IsNullOrWhiteSpace($UiUser)) {
        $rules += New-Object System.Security.AccessControl.FileSystemAccessRule($UiUser, "Modify", $inheritanceFlags, "None", "Allow")
    }

    foreach ($rule in $rules) {
        $acl.SetAccessRule($rule)
    }

    Set-Acl -Path $Path -AclObject $acl
}

Set-RestrictedDataAcl $programData
Get-ChildItem -LiteralPath $programData -Recurse -Force | ForEach-Object {
    Set-RestrictedDataAcl $_.FullName
}

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
& sc.exe failure $ServiceName reset= 86400 actions= restart/60000/restart/300000/restart/900000 | Out-Null
& sc.exe failureflag $ServiceName 1 | Out-Null

$escapedServiceName = $ServiceName.Replace("'", "''")
$serviceConfig = Get-CimInstance -ClassName Win32_Service -Filter "Name='$escapedServiceName'" -ErrorAction Stop
if ($serviceConfig.StartMode -ne "Auto") {
    throw "Servis baslangic modu Automatic olmadi. Mevcut deger: $($serviceConfig.StartMode)"
}

$serviceRegPath = "HKLM:\SYSTEM\CurrentControlSet\Services\$ServiceName"
$delayedAutoStart = (Get-ItemProperty -LiteralPath $serviceRegPath -Name DelayedAutoStart -ErrorAction Stop).DelayedAutoStart
if ($delayedAutoStart -ne 1) {
    throw "Servis gecikmeli otomatik baslangic olarak ayarlanmadi."
}

$failureConfig = (& sc.exe qfailure $ServiceName 2>&1) -join "`n"
if ($failureConfig -notmatch "60000" -or $failureConfig -notmatch "300000" -or $failureConfig -notmatch "900000") {
    throw "Servis recovery ayarlari beklenen 1/5/15 dakika politikasina uymuyor."
}

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
