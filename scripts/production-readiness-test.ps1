param(
    [string]$ServiceName = "NetworkHealthMonitorWorker",
    [string]$WorkerPath = (Join-Path $PSScriptRoot "..\worker\NetworkHealthMonitor.Worker.exe"),
    [string]$UiPath = (Join-Path $PSScriptRoot "..\ui\NetworkHealthMonitor.exe"),
    [string]$DataRoot = (Join-Path $env:ProgramData "NetworkHealthMonitor"),
    [string]$ZipPath = "",
    [string]$Sha256Path = "",
    [int]$HeartbeatMaxAgeSeconds = 120,
    [int64]$MinimumFreeBytes = 1073741824,
    [string]$ReportJsonPath = "release-readiness-report.json",
    [string]$ReportTextPath = "release-readiness-report.txt",
    [switch]$RequireExternalAcceptanceReports
)

$ErrorActionPreference = "Stop"
$results = New-Object System.Collections.Generic.List[object]

function Add-Result {
    param(
        [ValidateSet("PASS","FAIL","WARNING","NOT TESTED")]
        [string]$Status,
        [string]$Name,
        [string]$Detail = "",
        [bool]$Mandatory = $true
    )

    $results.Add([pscustomobject]@{
        status = $Status
        name = $Name
        detail = $Detail
        mandatory = $Mandatory
        checkedAtUtc = [DateTime]::UtcNow.ToString("O")
    }) | Out-Null

    $color = switch ($Status) {
        "PASS" { "Green" }
        "WARNING" { "Yellow" }
        "NOT TESTED" { "Yellow" }
        default { "Red" }
    }
    $line = if ([string]::IsNullOrWhiteSpace($Detail)) { "$Status $Name" } else { "$Status $Name - $Detail" }
    Write-Host $line -ForegroundColor $color
}

function Resolve-ExistingPath([string]$Path) {
    try { return (Resolve-Path $Path -ErrorAction Stop).Path } catch { return $Path }
}

function Get-ExeVersion([string]$Path) {
    if (-not (Test-Path $Path)) { return "" }
    $info = (Get-Item $Path).VersionInfo
    $version = $info.ProductVersion
    if ([string]::IsNullOrWhiteSpace($version)) { $version = $info.FileVersion }
    return ($version -replace '\+.*$', '').Trim()
}

function Test-Administrator {
    $identity = [Security.Principal.WindowsIdentity]::GetCurrent()
    $principal = [Security.Principal.WindowsPrincipal]::new($identity)
    return $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
}

function Test-FileContainsSecretPattern([string]$Root) {
    if (-not (Test-Path $Root)) { return @() }
    $findings = New-Object System.Collections.Generic.List[string]
    $textExtensions = @(".cs", ".xaml", ".ps1", ".json", ".config", ".md", ".txt", ".xml", ".csproj", ".props", ".targets", ".yml", ".yaml")
    Get-ChildItem -LiteralPath $Root -Recurse -File -ErrorAction SilentlyContinue |
        Where-Object {
            $textExtensions -contains $_.Extension.ToLowerInvariant() -and
            $_.FullName -notmatch '\\(bin|obj|TestResults)\\' -and
            $_.FullName -notmatch '\\release\\(archive|artifacts|staging|package-root|verification)\\'
        } |
        ForEach-Object {
            $path = $_.FullName
            try {
                foreach ($line in (Get-Content -LiteralPath $path -ErrorAction Stop)) {
                    $quotedSecret =
                        $line -match '(?i)(smtp(password|_password)|smtppassword|ntfy(token|_token)|accesstoken|access_token)\s*[:=]\s*["''](?<secret>[^"'']{12,})["'']'
                    $bearerSecret =
                        $line -match '(?i)authorization:\s*bearer\s+[A-Za-z0-9._~+/=-]{20,}'

                    if ($quotedSecret -or $bearerSecret) {
                        $findings.Add($path) | Out-Null
                        break
                    }
                }
            }
            catch {}
        }
    return $findings
}

$worker = Resolve-ExistingPath $WorkerPath
$ui = Resolve-ExistingPath $UiPath
$db = Join-Path $DataRoot "data\network_health_monitor.db"
$jsonPath = if ([System.IO.Path]::IsPathRooted($ReportJsonPath)) { $ReportJsonPath } else { Join-Path (Get-Location) $ReportJsonPath }
$textPath = if ([System.IO.Path]::IsPathRooted($ReportTextPath)) { $ReportTextPath } else { Join-Path (Get-Location) $ReportTextPath }

Add-Result -Status ($(if (Test-Administrator) { "PASS" } else { "WARNING" })) -Name "Yonetici durumu" -Detail "Kurulum/servis kontrolleri icin yonetici oturumu gerekir." -Mandatory:$false
Add-Result -Status ($(if (Test-Path $worker) { "PASS" } else { "FAIL" })) -Name "Worker exe var" -Detail $worker
Add-Result -Status ($(if (Test-Path $ui) { "PASS" } else { "FAIL" })) -Name "UI exe var" -Detail $ui

if (-not [string]::IsNullOrWhiteSpace($ZipPath)) {
    $zipExists = Test-Path $ZipPath
    Add-Result -Status ($(if ($zipExists) { "PASS" } else { "FAIL" })) -Name "ZIP var" -Detail $ZipPath
    if ($zipExists -and -not [string]::IsNullOrWhiteSpace($Sha256Path) -and (Test-Path $Sha256Path)) {
        $actual = (Get-FileHash -Algorithm SHA256 -LiteralPath $ZipPath).Hash.ToLowerInvariant()
        $expected = ((Get-Content -LiteralPath $Sha256Path -Raw) -split '\s+')[0].ToLowerInvariant()
        Add-Result -Status ($(if ($actual -eq $expected) { "PASS" } else { "FAIL" })) -Name "ZIP SHA256 eslesiyor" -Detail $actual
    }
    else {
        Add-Result -Status "NOT TESTED" -Name "ZIP SHA256 eslesiyor" -Detail "ZIP veya SHA256 dosyasi verilmedi."
    }
}
else {
    Add-Result -Status "NOT TESTED" -Name "ZIP hash" -Detail "ZipPath parametresi verilmedi."
}

$service = Get-Service -Name $ServiceName -ErrorAction SilentlyContinue
Add-Result -Status ($(if ($null -ne $service) { "PASS" } else { "FAIL" })) -Name "Service kurulu" -Detail $ServiceName
Add-Result -Status ($(if ($service -and $service.Status -eq "Running") { "PASS" } else { "FAIL" })) -Name "Service Running" -Detail ($(if ($service) { $service.Status } else { "Missing" }))

$wmi = Get-CimInstance -ClassName Win32_Service -Filter "Name='$($ServiceName.Replace("'","''"))'" -ErrorAction SilentlyContinue
Add-Result -Status ($(if ($wmi -and $wmi.StartMode -eq "Auto") { "PASS" } else { "FAIL" })) -Name "Service Automatic" -Detail ($(if ($wmi) { $wmi.StartMode } else { "Missing" }))

$delayedStatus = "FAIL"
$delayedDetail = "Service registry kaydi bulunamadi."
try {
    $serviceRegPath = "HKLM:\SYSTEM\CurrentControlSet\Services\$ServiceName"
    $delayed = (Get-ItemProperty -LiteralPath $serviceRegPath -Name DelayedAutoStart -ErrorAction Stop).DelayedAutoStart
    $delayedStatus = if ($delayed -eq 1) { "PASS" } else { "FAIL" }
    $delayedDetail = "DelayedAutoStart=$delayed"
}
catch {}
Add-Result -Status $delayedStatus -Name "DelayedAutoStart aktif" -Detail $delayedDetail

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
$recoveryOk = $recoveryExitCode -eq 0 -and $recoveryText -match "60000" -and $recoveryText -match "300000" -and $recoveryText -match "900000"
Add-Result -Status ($(if ($recoveryOk) { "PASS" } else { "FAIL" })) -Name "Recovery policy 1/5/15 dakika" -Detail (($recoveryOutput | Select-Object -First 5) -join " ")

try {
    New-Item -ItemType Directory -Force -Path $DataRoot | Out-Null
    $testFile = Join-Path $DataRoot ".readiness-write-test"
    "ok" | Set-Content -LiteralPath $testFile -Encoding ASCII
    Remove-Item -LiteralPath $testFile -Force
    Add-Result -Status "PASS" -Name "ProgramData yazilabilir" -Detail $DataRoot
}
catch {
    Add-Result -Status "FAIL" -Name "ProgramData yazilabilir" -Detail $_.Exception.Message
}

Add-Result -Status ($(if (Test-Path $db) { "PASS" } else { "FAIL" })) -Name "SQLite DB var" -Detail $db
if (Test-Path $worker) {
    $databaseVerifyJson = Join-Path (Split-Path -Parent $jsonPath) "database-verify-readiness.json"
    $databaseVerifyText = Join-Path (Split-Path -Parent $textPath) "database-verify-readiness.txt"
    $databaseVerify = @()
    $databaseVerifyExitCode = -1
    try {
        $previousErrorActionPreference = $ErrorActionPreference
        $ErrorActionPreference = "Continue"
        $databaseVerify = & $worker --verify-database --data-dir $DataRoot --database-report-json $databaseVerifyJson --database-report-text $databaseVerifyText 2>&1
        $databaseVerifyExitCode = $LASTEXITCODE
    }
    catch {
        $databaseVerify = @($_.Exception.Message)
    }
    finally {
        $ErrorActionPreference = $previousErrorActionPreference
    }

    Add-Result -Status ($(if ($databaseVerifyExitCode -eq 0) { "PASS" } else { "FAIL" })) -Name "Self-contained database verify" -Detail (($databaseVerify | Select-Object -First 8) -join " | ")
}
else {
    Add-Result -Status "NOT TESTED" -Name "Self-contained database verify" -Detail "Worker exe yok."
}

if ((Test-Path $worker) -and (Test-Path $db)) {
    $health = @()
    $healthExitCode = -1
    try {
        $previousErrorActionPreference = $ErrorActionPreference
        $ErrorActionPreference = "Continue"
        $health = & $worker --health-check --data-dir $DataRoot --heartbeat-max-age-seconds $HeartbeatMaxAgeSeconds 2>&1
        $healthExitCode = $LASTEXITCODE
    }
    catch {
        $health = @($_.Exception.Message)
    }
    finally {
        $ErrorActionPreference = $previousErrorActionPreference
    }

    Add-Result -Status ($(if ($healthExitCode -eq 0) { "PASS" } else { "FAIL" })) -Name "Worker health-check" -Detail (($health | Select-Object -First 8) -join " | ")
}
else {
    Add-Result -Status "NOT TESTED" -Name "Worker health-check" -Detail "Worker veya DB yok."
}

$uiVersion = Get-ExeVersion $ui
$workerVersion = Get-ExeVersion $worker
$versionsMatch = -not [string]::IsNullOrWhiteSpace($uiVersion) -and -not [string]::IsNullOrWhiteSpace($workerVersion) -and ($uiVersion -eq $workerVersion)
Add-Result -Status ($(if ($versionsMatch) { "PASS" } else { "FAIL" })) -Name "UI/Worker surumleri eslesiyor" -Detail "UI=$uiVersion Worker=$workerVersion"

try {
    $root = [System.IO.Path]::GetPathRoot((Resolve-Path $DataRoot -ErrorAction SilentlyContinue).Path)
    if ([string]::IsNullOrWhiteSpace($root)) { $root = [System.IO.Path]::GetPathRoot($DataRoot) }
    $drive = [System.IO.DriveInfo]::new($root)
    Add-Result -Status ($(if ($drive.AvailableFreeSpace -ge $MinimumFreeBytes) { "PASS" } else { "FAIL" })) -Name "Disk alani" -Detail "FreeBytes=$($drive.AvailableFreeSpace)"
}
catch {
    Add-Result -Status "FAIL" -Name "Disk alani" -Detail $_.Exception.Message
}

$secretFindings = Test-FileContainsSecretPattern -Root (Resolve-Path (Join-Path $PSScriptRoot "..")).Path
Add-Result -Status ($(if ($secretFindings.Count -eq 0) { "PASS" } else { "FAIL" })) -Name "Secret pattern taramasi" -Detail (($secretFindings | Select-Object -First 5) -join "; ")

$externalRoot = Join-Path (Resolve-Path (Join-Path $PSScriptRoot "..")).Path "release\verification"
$externalReports = @(
    "windows-service-acceptance.json",
    "migration-acceptance.json",
    "notification-acceptance.json",
    "ui-acceptance.json",
    "soak-test.json",
    "upgrade-rollback-acceptance.json"
)
foreach ($report in $externalReports) {
    $path = Join-Path $externalRoot $report
    if (Test-Path $path) {
        $content = Get-Content -LiteralPath $path -Raw
        $passed = $content -match '"status"\s*:\s*"PASS"'
        Add-Result -Status ($(if ($passed) { "PASS" } else { "FAIL" })) -Name "External acceptance: $report" -Detail $path -Mandatory:$RequireExternalAcceptanceReports
    }
    else {
        Add-Result -Status "NOT TESTED" -Name "External acceptance: $report" -Detail $path -Mandatory:$RequireExternalAcceptanceReports
    }
}

$report = [pscustomobject]@{
    generatedAtUtc = [DateTime]::UtcNow.ToString("O")
    serviceName = $ServiceName
    dataRoot = $DataRoot
    results = $results
}

New-Item -ItemType Directory -Force -Path (Split-Path -Parent $jsonPath) | Out-Null
New-Item -ItemType Directory -Force -Path (Split-Path -Parent $textPath) | Out-Null
$report | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $jsonPath -Encoding UTF8
$results | ForEach-Object { "{0} {1} - {2}" -f $_.status, $_.name, $_.detail } | Set-Content -LiteralPath $textPath -Encoding UTF8

$blocking = @($results | Where-Object { $_.mandatory -and $_.status -ne "PASS" })
if ($blocking.Count -gt 0) {
    Write-Host "Production readiness failed: $($blocking.Count) blocking checks are not PASS." -ForegroundColor Red
    exit 1
}

Write-Host "Production readiness passed." -ForegroundColor Green
exit 0
