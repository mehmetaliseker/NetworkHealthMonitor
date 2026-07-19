param(
    [string]$ReportJsonPath = "release\verification\upgrade-rollback-acceptance.json",
    [string]$ReportTextPath = "release\verification\upgrade-rollback-acceptance.txt",
    [switch]$ConfirmPreUpgradeBackupCreated,
    [switch]$ConfirmServiceStoppedCleanly,
    [switch]$ConfirmBinariesReplaced,
    [switch]$ConfirmMigrationRan,
    [switch]$ConfirmServiceStarted,
    [switch]$ConfirmHeartbeatAfterUpgrade,
    [switch]$ConfirmDevicesPreserved,
    [switch]$ConfirmSettingsPreserved,
    [switch]$ConfirmIncidentsPreserved,
    [switch]$ConfirmOutboxPreserved,
    [switch]$ConfirmRollbackRestoredOldBinary,
    [switch]$ConfirmRollbackRestoredData
)

$ErrorActionPreference = "Stop"
$checks = New-Object System.Collections.Generic.List[object]

function Add-Check([string]$Status, [string]$Name, [string]$Detail = "") {
    $checks.Add([pscustomobject]@{ status = $Status; name = $Name; detail = $Detail; checkedAtUtc = [DateTime]::UtcNow.ToString("O") }) | Out-Null
    Write-Host "$Status $Name - $Detail"
}

Add-Check ($(if ($ConfirmPreUpgradeBackupCreated) { "PASS" } else { "NOT TESTED" })) "Yukseltme oncesi yedek" "Backup path kaniti gerekir."
Add-Check ($(if ($ConfirmServiceStoppedCleanly) { "PASS" } else { "NOT TESTED" })) "Service kontrollu durdu" "Script/service log kaniti gerekir."
Add-Check ($(if ($ConfirmBinariesReplaced) { "PASS" } else { "NOT TESTED" })) "Binary dosyalari degisti" "Version/hash kaniti gerekir."
Add-Check ($(if ($ConfirmMigrationRan) { "PASS" } else { "NOT TESTED" })) "Migration calisti" "migration-acceptance veya DB verify kaniti gerekir."
Add-Check ($(if ($ConfirmServiceStarted) { "PASS" } else { "NOT TESTED" })) "Service yeniden basladi" "Service status kaniti gerekir."
Add-Check ($(if ($ConfirmHeartbeatAfterUpgrade) { "PASS" } else { "NOT TESTED" })) "Yukseltme sonrasi heartbeat" "Worker heartbeat kaniti gerekir."
Add-Check ($(if ($ConfirmDevicesPreserved) { "PASS" } else { "NOT TESTED" })) "Cihazlar korundu" "Sayi karsilastirma kaniti gerekir."
Add-Check ($(if ($ConfirmSettingsPreserved) { "PASS" } else { "NOT TESTED" })) "Ayarlar korundu" "settings hash veya UI kaniti gerekir."
Add-Check ($(if ($ConfirmIncidentsPreserved) { "PASS" } else { "NOT TESTED" })) "Incident kayitlari korundu" "DB sayim kaniti gerekir."
Add-Check ($(if ($ConfirmOutboxPreserved) { "PASS" } else { "NOT TESTED" })) "Outbox kayitlari korundu" "DB sayim kaniti gerekir."
Add-Check ($(if ($ConfirmRollbackRestoredOldBinary) { "PASS" } else { "NOT TESTED" })) "Rollback eski binary'yi geri aldi" "Version/hash kaniti gerekir."
Add-Check ($(if ($ConfirmRollbackRestoredData) { "PASS" } else { "NOT TESTED" })) "Rollback veriyi geri aldi" "DB/settings kaniti gerekir."

$status = if (@($checks | Where-Object status -eq "FAIL").Count -gt 0) { "FAIL" } elseif (@($checks | Where-Object status -eq "NOT TESTED").Count -gt 0) { "NOT TESTED" } else { "PASS" }
$report = [pscustomobject]@{ status = $status; generatedAtUtc = [DateTime]::UtcNow.ToString("O"); checks = $checks }
$json = if ([System.IO.Path]::IsPathRooted($ReportJsonPath)) { $ReportJsonPath } else { Join-Path (Get-Location) $ReportJsonPath }
$txt = if ([System.IO.Path]::IsPathRooted($ReportTextPath)) { $ReportTextPath } else { Join-Path (Get-Location) $ReportTextPath }
New-Item -ItemType Directory -Force -Path (Split-Path -Parent $json), (Split-Path -Parent $txt) | Out-Null
$report | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $json -Encoding UTF8
$checks | ForEach-Object { "{0} {1} - {2}" -f $_.status, $_.name, $_.detail } | Set-Content -LiteralPath $txt -Encoding UTF8
Write-Host "Upgrade rollback acceptance status: $status"
if ($status -eq "PASS") { exit 0 }
if ($status -eq "NOT TESTED") { exit 2 }
exit 1
