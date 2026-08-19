param(
    [string]$ServiceName = "NetworkHealthMonitorWorker",
    [string]$WorkerPath = "",
    [string]$ReportJsonPath = "release\verification\windows-service-acceptance.json",
    [string]$ReportTextPath = "release\verification\windows-service-acceptance.txt",
    [switch]$RequireWorkerRunning,
    [switch]$ConfirmWorkerSurvivedUiClosed,
    [switch]$ConfirmRebootCompleted,
    [switch]$ConfirmWorkerStoppedAfterReboot
)

$ErrorActionPreference = "Stop"
$checks = New-Object System.Collections.Generic.List[object]

function Add-Check([string]$Status, [string]$Name, [string]$Detail = "") {
    $checks.Add([pscustomobject]@{ status = $Status; name = $Name; detail = $Detail; checkedAtUtc = [DateTime]::UtcNow.ToString("O") }) | Out-Null
    Write-Host "$Status $Name - $Detail"
}

function Resolve-PathOrDefault([string]$Path) {
    if ([string]::IsNullOrWhiteSpace($Path)) {
        $Path = Join-Path $PSScriptRoot "..\Worker\NetworkHealthMonitor.Worker.exe"
    }
    if (Test-Path $Path) { return (Resolve-Path $Path).Path }
    return $Path
}

$worker = Resolve-PathOrDefault $WorkerPath
$service = Get-Service -Name $ServiceName -ErrorAction SilentlyContinue
Add-Check ($(if ($service) { "PASS" } else { "FAIL" })) "Service kurulu" $ServiceName
Add-Check ($(if (-not $RequireWorkerRunning -or ($service -and $service.Status -eq "Running")) { "PASS" } else { "FAIL" })) "Service Running" ($(if ($service) { $service.Status } else { "Missing" }))

$wmi = Get-CimInstance -ClassName Win32_Service -Filter "Name='$($ServiceName.Replace("'","''"))'" -ErrorAction SilentlyContinue
Add-Check ($(if ($wmi -and $wmi.StartMode -eq "Manual") { "PASS" } else { "FAIL" })) "StartMode Manual" ($(if ($wmi) { $wmi.StartMode } else { "Missing" }))

try {
    $delayed = (Get-ItemProperty -LiteralPath "HKLM:\SYSTEM\CurrentControlSet\Services\$ServiceName" -Name DelayedAutoStart -ErrorAction SilentlyContinue).DelayedAutoStart
    Add-Check ($(if ($null -eq $delayed -or $delayed -eq 0) { "PASS" } else { "FAIL" })) "DelayedAutoStart disabled" "DelayedAutoStart=$delayed"
}
catch {
    Add-Check "PASS" "DelayedAutoStart disabled" "No DelayedAutoStart value"
}

$recoveryOutput = @()
$recoveryExitCode = -1
try {
    $previousErrorActionPreference = $ErrorActionPreference
    $ErrorActionPreference = "Continue"
    $recoveryOutput = & sc.exe qfailure $ServiceName 2>&1
    $recoveryExitCode = $LASTEXITCODE
}
catch {
    $recoveryOutput = @($_.Exception.Message)
}
finally {
    $ErrorActionPreference = $previousErrorActionPreference
}
$recoveryText = $recoveryOutput -join "`n"
Add-Check ($(if ($recoveryExitCode -ne 0 -or $recoveryText -notmatch "RESTART") { "PASS" } else { "WARNING" })) "Recovery auto restart yok" (($recoveryOutput | Select-Object -First 5) -join " ")

if ((Test-Path $worker) -and $RequireWorkerRunning) {
    $health = @()
    $healthExitCode = -1
    try {
        $previousErrorActionPreference = $ErrorActionPreference
        $ErrorActionPreference = "Continue"
        $health = & $worker --health-check 2>&1
        $healthExitCode = $LASTEXITCODE
    }
    catch {
        $health = @($_.Exception.Message)
    }
    finally {
        $ErrorActionPreference = $previousErrorActionPreference
    }

    Add-Check ($(if ($healthExitCode -eq 0) { "PASS" } else { "FAIL" })) "Worker heartbeat health-check" (($health | Select-Object -First 8) -join " | ")
}
else {
    Add-Check ($(if (Test-Path $worker) { "PASS" } else { "FAIL" })) "Worker exe bulundu" $worker
}

Add-Check ($(if ($ConfirmWorkerSurvivedUiClosed) { "PASS" } else { "NOT TESTED" })) "UI kapaliyken Worker calisti" "Manuel kanit gerekir."
Add-Check ($(if ($ConfirmRebootCompleted) { "PASS" } else { "NOT TESTED" })) "Gercek reboot testi" "Bilgisayar yeniden baslatilmadan PASS sayilmaz."
Add-Check ($(if ($ConfirmWorkerStoppedAfterReboot) { "PASS" } else { "NOT TESTED" })) "Reboot sonrasi Worker durdu" "Manual startup icin reboot sonrasi Stopped kaniti gerekir."

$status = if (@($checks | Where-Object status -eq "FAIL").Count -gt 0) { "FAIL" } elseif (@($checks | Where-Object status -eq "NOT TESTED").Count -gt 0) { "NOT TESTED" } else { "PASS" }
$report = [pscustomobject]@{ status = $status; generatedAtUtc = [DateTime]::UtcNow.ToString("O"); serviceName = $ServiceName; workerPath = $worker; checks = $checks }
$json = if ([System.IO.Path]::IsPathRooted($ReportJsonPath)) { $ReportJsonPath } else { Join-Path (Get-Location) $ReportJsonPath }
$txt = if ([System.IO.Path]::IsPathRooted($ReportTextPath)) { $ReportTextPath } else { Join-Path (Get-Location) $ReportTextPath }
New-Item -ItemType Directory -Force -Path (Split-Path -Parent $json), (Split-Path -Parent $txt) | Out-Null
$report | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $json -Encoding UTF8
$checks | ForEach-Object { "{0} {1} - {2}" -f $_.status, $_.name, $_.detail } | Set-Content -LiteralPath $txt -Encoding UTF8
Write-Host "Windows service acceptance status: $status"
if ($status -eq "PASS") { exit 0 }
if ($status -eq "NOT TESTED") { exit 2 }
exit 1
