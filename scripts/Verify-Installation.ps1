param(
    [string]$InstallRoot = (Join-Path $env:ProgramFiles "NetworkHealthMonitor"),
    [string]$DataRoot = (Join-Path $env:ProgramData "NetworkHealthMonitor"),
    [string]$ServiceName = "NetworkHealthMonitorWorker",
    [switch]$RequireWorkerRunning
)

$ErrorActionPreference = "Stop"
$results = New-Object System.Collections.Generic.List[object]

function Add-Result {
    param(
        [string]$Status,
        [string]$Name,
        [string]$Detail
    )

    $results.Add([pscustomobject]@{
        status = $Status
        name = $Name
        detail = $Detail
        checkedAtUtc = [DateTime]::UtcNow.ToString("O")
    }) | Out-Null

    Write-Host "$Status $Name - $Detail"
}

function Test-PathResult {
    param(
        [string]$Name,
        [string]$Path
    )

    if (Test-Path -LiteralPath $Path) {
        Add-Result "PASS" $Name $Path
    }
    else {
        Add-Result "FAIL" $Name $Path
    }
}

$uiPath = Join-Path $InstallRoot "UI\NetworkHealthMonitor.exe"
$workerPath = Join-Path $InstallRoot "Worker\NetworkHealthMonitor.Worker.exe"
$trayPath = Join-Path $InstallRoot "Tray\NetworkHealthMonitor.Tray.exe"
$dbPath = Join-Path $DataRoot "data\NetworkHealthMonitor.db"

Test-PathResult "UI exe" $uiPath
Test-PathResult "Worker exe" $workerPath
Test-PathResult "Tray exe" $trayPath
Test-PathResult "Scripts directory" (Join-Path $InstallRoot "Scripts")

foreach ($directory in @($DataRoot, (Join-Path $DataRoot "data"), (Join-Path $DataRoot "config"), (Join-Path $DataRoot "logs"), (Join-Path $DataRoot "backups"))) {
    Test-PathResult "ProgramData directory" $directory
}

$service = Get-Service -Name $ServiceName -ErrorAction SilentlyContinue
if ($null -eq $service) {
    Add-Result "FAIL" "Worker service installed" $ServiceName
}
else {
    Add-Result "PASS" "Worker service installed" "$ServiceName Status=$($service.Status)"
}

$wmi = Get-CimInstance -ClassName Win32_Service -Filter "Name='$($ServiceName.Replace("'","''"))'" -ErrorAction SilentlyContinue
if ($wmi -and $wmi.StartMode -eq "Manual") {
    Add-Result "PASS" "Startup type Manual" $wmi.StartMode
}
else {
    $mode = "Missing"
    if ($wmi) { $mode = $wmi.StartMode }
    Add-Result "FAIL" "Startup type Manual" $mode
}

if ($wmi -and $wmi.StartMode -ne "Auto") {
    Add-Result "PASS" "Worker does not auto-start on boot" $wmi.StartMode
}
else {
    Add-Result "FAIL" "Worker does not auto-start on boot" "StartMode=$($wmi.StartMode)"
}

try {
    $delayed = (Get-ItemProperty -LiteralPath "HKLM:\SYSTEM\CurrentControlSet\Services\$ServiceName" -Name DelayedAutoStart -ErrorAction SilentlyContinue).DelayedAutoStart
    if ($null -eq $delayed -or $delayed -eq 0) {
        Add-Result "PASS" "DelayedAutoStart disabled" "DelayedAutoStart=$delayed"
    }
    else {
        Add-Result "FAIL" "DelayedAutoStart disabled" "DelayedAutoStart=$delayed"
    }
}
catch {
    Add-Result "PASS" "DelayedAutoStart disabled" "No DelayedAutoStart value"
}

if (Test-Path -LiteralPath $workerPath) {
    $verifyOutput = & $workerPath --verify-database --data-dir $DataRoot 2>&1
    if ($LASTEXITCODE -eq 0) {
        Add-Result "PASS" "Worker database verification" (($verifyOutput | Select-Object -First 5) -join " | ")
    }
    else {
        Add-Result "FAIL" "Worker database verification" (($verifyOutput | Select-Object -First 5) -join " | ")
    }
}

Test-PathResult "SQLite database" $dbPath

if ($RequireWorkerRunning) {
    if ($service -and $service.Status -eq "Running") {
        Add-Result "PASS" "Worker running" $service.Status
    }
    else {
        $status = "Missing"
        if ($service) { $status = $service.Status }
        Add-Result "FAIL" "Worker running" $status
    }
}

$blocking = @($results | Where-Object { $_.status -eq "FAIL" })
if ($blocking.Count -gt 0) {
    Write-Host "Installation verification failed: $($blocking.Count) failing checks."
    exit 1
}

Write-Host "Installation verification passed."
exit 0
