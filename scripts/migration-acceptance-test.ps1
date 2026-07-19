param(
    [Parameter(Mandatory = $true)]
    [string]$OldDatabasePath,
    [string]$OldSettingsPath = "",
    [string]$WorkerPath = "",
    [string]$WorkRoot = "release\verification\migration-acceptance-work",
    [string]$ReportJsonPath = "release\verification\migration-acceptance.json",
    [string]$ReportTextPath = "release\verification\migration-acceptance.txt"
)

$ErrorActionPreference = "Stop"
$checks = New-Object System.Collections.Generic.List[object]

function Add-Check {
    param(
        [ValidateSet("PASS","FAIL","NOT TESTED")]
        [string]$Status,
        [string]$Name,
        [string]$Detail = ""
    )

    $checks.Add([pscustomobject]@{
        status = $Status
        name = $Name
        detail = $Detail
        checkedAtUtc = [DateTime]::UtcNow.ToString("O")
    }) | Out-Null
    Write-Host "$Status $Name - $Detail"
}

function Resolve-WorkerPath {
    param([string]$ExplicitPath)

    $candidates = @()
    if (-not [string]::IsNullOrWhiteSpace($ExplicitPath)) {
        $candidates += $ExplicitPath
    }

    $candidates += @(
        (Join-Path $PSScriptRoot "..\worker\NetworkHealthMonitor.Worker.exe"),
        (Join-Path $PSScriptRoot "..\release\staging\worker\NetworkHealthMonitor.Worker.exe"),
        (Join-Path $PSScriptRoot "..\release\artifacts\NetworkHealthMonitor-Server-win-x64-v1.1.0-rc-extracted\worker\NetworkHealthMonitor.Worker.exe")
    )

    foreach ($candidate in $candidates) {
        if (Test-Path $candidate) {
            return (Resolve-Path $candidate).Path
        }
    }

    return $ExplicitPath
}

function Invoke-WorkerDatabaseCommand {
    param(
        [string]$Worker,
        [string[]]$Arguments,
        [string]$Name
    )

    $output = @()
    $exitCode = -1
    try {
        $previousErrorActionPreference = $ErrorActionPreference
        $ErrorActionPreference = "Continue"
        $output = & $Worker @Arguments 2>&1
        $exitCode = $LASTEXITCODE
    }
    catch {
        $output = @($_.Exception.Message)
    }
    finally {
        $ErrorActionPreference = $previousErrorActionPreference
    }

    if ($exitCode -ne 0) {
        Add-Check -Status "FAIL" -Name $Name -Detail (($output | Select-Object -First 8) -join " | ")
    }
    else {
        Add-Check -Status "PASS" -Name $Name -Detail (($output | Select-Object -First 3) -join " | ")
    }

    return $exitCode
}

function Read-Json {
    param([string]$Path)
    return Get-Content -LiteralPath $Path -Raw | ConvertFrom-Json
}

function Get-TableCount {
    param(
        [object]$Summary,
        [string]$TableName
    )

    $property = $Summary.tables.PSObject.Properties[$TableName]
    if ($null -eq $property -or $null -eq $property.Value -or -not $property.Value.exists) {
        return $null
    }

    return [long]$property.Value.count
}

function Get-MigrationSignature {
    param([object]$Summary)

    if ($null -eq $Summary.migrations) {
        return ""
    }

    return (($Summary.migrations | ForEach-Object { "$($_.version)|$($_.appliedAtUtc)" }) -join ";")
}

$worker = Resolve-WorkerPath $WorkerPath
$reportJsonFullPath = if ([System.IO.Path]::IsPathRooted($ReportJsonPath)) { $ReportJsonPath } else { Join-Path (Get-Location) $ReportJsonPath }
$reportTextFullPath = if ([System.IO.Path]::IsPathRooted($ReportTextPath)) { $ReportTextPath } else { Join-Path (Get-Location) $ReportTextPath }
New-Item -ItemType Directory -Force -Path (Split-Path -Parent $reportJsonFullPath), (Split-Path -Parent $reportTextFullPath) | Out-Null

$status = "PASS"
$runRoot = $null
$preSummaryPath = $null
$postSummaryPath = $null
$secondSummaryPath = $null
$databaseBackupPath = $null
$settingsBackupPath = $null

try {
    if (-not (Test-Path $worker)) {
        Add-Check -Status "NOT TESTED" -Name "Worker exe bulundu" -Detail $worker
        $status = "NOT TESTED"
        throw "Worker exe bulunamadi."
    }
    Add-Check -Status "PASS" -Name "Worker exe bulundu" -Detail $worker

    if (-not (Test-Path $OldDatabasePath)) {
        Add-Check -Status "NOT TESTED" -Name "Eski DB bulundu" -Detail $OldDatabasePath
        $status = "NOT TESTED"
        throw "Eski DB bulunamadi."
    }
    Add-Check -Status "PASS" -Name "Eski DB bulundu" -Detail $OldDatabasePath

    $runId = Get-Date -Format "yyyyMMdd-HHmmss"
    $workRootFullPath = if ([System.IO.Path]::IsPathRooted($WorkRoot)) { $WorkRoot } else { Join-Path (Get-Location) $WorkRoot }
    $runRoot = Join-Path $workRootFullPath $runId
    $dataRoot = Join-Path $runRoot "data-root"
    $dataDir = Join-Path $dataRoot "data"
    $configDir = Join-Path $dataRoot "config"
    $backupDir = Join-Path $runRoot "input-backup"
    New-Item -ItemType Directory -Force -Path $dataDir, $configDir, $backupDir | Out-Null

    $targetDb = Join-Path $dataDir "network_health_monitor.db"
    Copy-Item -LiteralPath $OldDatabasePath -Destination $targetDb -Force
    $databaseBackupPath = Join-Path $backupDir ("network_health_monitor-input-{0}.db" -f $runId)
    Copy-Item -LiteralPath $OldDatabasePath -Destination $databaseBackupPath -Force
    Add-Check -Status "PASS" -Name "Eski DB yedegi alindi" -Detail $databaseBackupPath

    if (-not [string]::IsNullOrWhiteSpace($OldSettingsPath) -and (Test-Path $OldSettingsPath)) {
        $targetSettings = Join-Path $configDir "settings.json"
        Copy-Item -LiteralPath $OldSettingsPath -Destination $targetSettings -Force
        $settingsBackupPath = Join-Path $backupDir ("settings-input-{0}.json" -f $runId)
        Copy-Item -LiteralPath $OldSettingsPath -Destination $settingsBackupPath -Force
        Add-Check -Status "PASS" -Name "Settings yedegi alindi" -Detail $settingsBackupPath
    }
    elseif ([string]::IsNullOrWhiteSpace($OldSettingsPath)) {
        Add-Check -Status "NOT TESTED" -Name "Settings dosyasi karsilastirma" -Detail "OldSettingsPath verilmedi."
    }
    else {
        Add-Check -Status "NOT TESTED" -Name "Settings dosyasi karsilastirma" -Detail "Dosya bulunamadi: $OldSettingsPath"
    }

    $preSummaryPath = Join-Path $runRoot "pre-summary.json"
    $preTextPath = Join-Path $runRoot "pre-summary.txt"
    $preExit = Invoke-WorkerDatabaseCommand -Worker $worker -Name "Migration oncesi database-summary" -Arguments @(
        "--database-summary",
        "--data-dir", $dataRoot,
        "--database-report-json", $preSummaryPath,
        "--database-report-text", $preTextPath)

    $postSummaryPath = Join-Path $runRoot "post-verify.json"
    $postTextPath = Join-Path $runRoot "post-verify.txt"
    $postExit = Invoke-WorkerDatabaseCommand -Worker $worker -Name "Migration sonrasi verify-database" -Arguments @(
        "--verify-database",
        "--data-dir", $dataRoot,
        "--database-report-json", $postSummaryPath,
        "--database-report-text", $postTextPath)

    $secondSummaryPath = Join-Path $runRoot "second-verify.json"
    $secondTextPath = Join-Path $runRoot "second-verify.txt"
    $secondExit = Invoke-WorkerDatabaseCommand -Worker $worker -Name "Idempotent ikinci verify-database" -Arguments @(
        "--verify-database",
        "--data-dir", $dataRoot,
        "--database-report-json", $secondSummaryPath,
        "--database-report-text", $secondTextPath)

    if ($preExit -ne 0 -or $postExit -ne 0 -or $secondExit -ne 0) {
        $status = "FAIL"
    }

    $pre = Read-Json $preSummaryPath
    $post = Read-Json $postSummaryPath
    $second = Read-Json $secondSummaryPath

    foreach ($tableName in @("Devices", "DeviceGroups", "SchedulePlans", "PingLogs")) {
        $before = Get-TableCount -Summary $pre -TableName $tableName
        $after = Get-TableCount -Summary $post -TableName $tableName
        if ($null -eq $before -or $null -eq $after) {
            Add-Check -Status "FAIL" -Name "$tableName tablo sayimi" -Detail "Migration oncesi veya sonrasi tablo yok."
            $status = "FAIL"
            continue
        }

        if ($after -lt $before) {
            Add-Check -Status "FAIL" -Name "$tableName veri koruma" -Detail "Once=$before Sonra=$after"
            $status = "FAIL"
        }
        else {
            Add-Check -Status "PASS" -Name "$tableName veri koruma" -Detail "Once=$before Sonra=$after"
        }
    }

    foreach ($tableName in @("DeviceIncidents", "NotificationOutbox", "AppSettings")) {
        $before = Get-TableCount -Summary $pre -TableName $tableName
        $after = Get-TableCount -Summary $post -TableName $tableName
        if ($null -ne $before -and $null -ne $after -and $after -lt $before) {
            Add-Check -Status "FAIL" -Name "$tableName veri koruma" -Detail "Once=$before Sonra=$after"
            $status = "FAIL"
        }
        elseif ($null -ne $after) {
            Add-Check -Status "PASS" -Name "$tableName veri koruma" -Detail "Once=$before Sonra=$after"
        }
    }

    $migrationSignatureAfter = Get-MigrationSignature $post
    $migrationSignatureSecond = Get-MigrationSignature $second
    if ($migrationSignatureAfter -eq $migrationSignatureSecond) {
        Add-Check -Status "PASS" -Name "Migration ikinci calistirmada tekrarlanmadi" -Detail "Migration imzasi degismedi."
    }
    else {
        Add-Check -Status "FAIL" -Name "Migration ikinci calistirmada tekrarlanmadi" -Detail "Migration imzasi degisti."
        $status = "FAIL"
    }

    if ($post.status -eq "PASS" -and $second.status -eq "PASS") {
        Add-Check -Status "PASS" -Name "SQLite integrity ve schema verify" -Detail "verify-database PASS"
    }
    else {
        Add-Check -Status "FAIL" -Name "SQLite integrity ve schema verify" -Detail "Post=$($post.status) Second=$($second.status)"
        $status = "FAIL"
    }

    if ($settingsBackupPath) {
        $migratedSettings = Join-Path $configDir "settings.json"
        if ((Get-FileHash -Algorithm SHA256 -LiteralPath $settingsBackupPath).Hash -eq (Get-FileHash -Algorithm SHA256 -LiteralPath $migratedSettings).Hash) {
            Add-Check -Status "PASS" -Name "Settings dosyasi korundu" -Detail $migratedSettings
        }
        else {
            Add-Check -Status "FAIL" -Name "Settings dosyasi korundu" -Detail "SHA256 degisti."
            $status = "FAIL"
        }
    }
}
catch {
    if ($status -eq "PASS") {
        $status = "FAIL"
    }
    Add-Check -Status $status -Name "Migration acceptance tamamlanamadi" -Detail $_.Exception.Message
}

if (@($checks | Where-Object { $_.status -eq "FAIL" }).Count -gt 0) {
    $status = "FAIL"
}
elseif (@($checks | Where-Object { $_.status -eq "NOT TESTED" }).Count -gt 0) {
    $status = "NOT TESTED"
}

$report = [pscustomobject]@{
    status = $status
    generatedAtUtc = [DateTime]::UtcNow.ToString("O")
    workerPath = $worker
    oldDatabasePath = $OldDatabasePath
    oldSettingsPath = $OldSettingsPath
    runRoot = $runRoot
    databaseBackupPath = $databaseBackupPath
    settingsBackupPath = $settingsBackupPath
    preSummaryPath = $preSummaryPath
    postSummaryPath = $postSummaryPath
    secondSummaryPath = $secondSummaryPath
    checks = $checks
}

$report | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $reportJsonFullPath -Encoding UTF8
$checks | ForEach-Object { "{0} {1} - {2}" -f $_.status, $_.name, $_.detail } | Set-Content -LiteralPath $reportTextFullPath -Encoding UTF8

Write-Host "Migration acceptance status: $status"
Write-Host "JSON: $reportJsonFullPath"
Write-Host "TXT: $reportTextFullPath"

if ($status -eq "PASS") { exit 0 }
if ($status -eq "NOT TESTED") { exit 2 }
exit 1
