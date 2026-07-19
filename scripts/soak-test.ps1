param(
    [string]$ReportJsonPath = "release\verification\soak-test.json",
    [string]$ReportTextPath = "release\verification\soak-test.txt",
    [int]$MinimumHours = 8,
    [int]$ObservedHours = 0,
    [switch]$Confirm300Devices,
    [switch]$Confirm1000DeviceStress,
    [switch]$ConfirmNoUnhandledExceptions,
    [switch]$ConfirmNoDuplicatePings,
    [switch]$ConfirmNoDuplicateIncidents,
    [switch]$ConfirmNoDuplicateNotifications,
    [switch]$ConfirmNoUnboundedMemoryGrowth,
    [switch]$ConfirmNoPersistentSqliteLocks,
    [switch]$ConfirmOutboxDidNotGrowUnbounded
)

$ErrorActionPreference = "Stop"
$checks = New-Object System.Collections.Generic.List[object]

function Add-Check([string]$Status, [string]$Name, [string]$Detail = "") {
    $checks.Add([pscustomobject]@{ status = $Status; name = $Name; detail = $Detail; checkedAtUtc = [DateTime]::UtcNow.ToString("O") }) | Out-Null
    Write-Host "$Status $Name - $Detail"
}

Add-Check ($(if ($ObservedHours -ge $MinimumHours) { "PASS" } else { "NOT TESTED" })) "Soak test suresi" "ObservedHours=$ObservedHours MinimumHours=$MinimumHours"
Add-Check ($(if ($Confirm300Devices) { "PASS" } else { "NOT TESTED" })) "300 cihaz veri seti" "Manuel/otomatik kanit gerekir."
Add-Check ($(if ($Confirm1000DeviceStress) { "PASS" } else { "NOT TESTED" })) "1000 cihaz stres testi" "Manuel/otomatik kanit gerekir."
Add-Check ($(if ($ConfirmNoUnhandledExceptions) { "PASS" } else { "NOT TESTED" })) "Unhandled exception yok" "Log kaniti gerekir."
Add-Check ($(if ($ConfirmNoDuplicatePings) { "PASS" } else { "NOT TESTED" })) "Duplicate ping yok" "Scheduler/log kaniti gerekir."
Add-Check ($(if ($ConfirmNoDuplicateIncidents) { "PASS" } else { "NOT TESTED" })) "Duplicate incident yok" "DB kaniti gerekir."
Add-Check ($(if ($ConfirmNoDuplicateNotifications) { "PASS" } else { "NOT TESTED" })) "Duplicate bildirim yok" "Outbox/alici kaniti gerekir."
Add-Check ($(if ($ConfirmNoUnboundedMemoryGrowth) { "PASS" } else { "NOT TESTED" })) "Kontrolsuz memory artisi yok" "Perf counter/process kaniti gerekir."
Add-Check ($(if ($ConfirmNoPersistentSqliteLocks) { "PASS" } else { "NOT TESTED" })) "Kalici SQLite lock yok" "Log kaniti gerekir."
Add-Check ($(if ($ConfirmOutboxDidNotGrowUnbounded) { "PASS" } else { "NOT TESTED" })) "Outbox kontrolsuz buyumedi" "DB/outbox kaniti gerekir."

$status = if (@($checks | Where-Object status -eq "FAIL").Count -gt 0) { "FAIL" } elseif (@($checks | Where-Object status -eq "NOT TESTED").Count -gt 0) { "NOT TESTED" } else { "PASS" }
$report = [pscustomobject]@{ status = $status; generatedAtUtc = [DateTime]::UtcNow.ToString("O"); observedHours = $ObservedHours; minimumHours = $MinimumHours; checks = $checks }
$json = if ([System.IO.Path]::IsPathRooted($ReportJsonPath)) { $ReportJsonPath } else { Join-Path (Get-Location) $ReportJsonPath }
$txt = if ([System.IO.Path]::IsPathRooted($ReportTextPath)) { $ReportTextPath } else { Join-Path (Get-Location) $ReportTextPath }
New-Item -ItemType Directory -Force -Path (Split-Path -Parent $json), (Split-Path -Parent $txt) | Out-Null
$report | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $json -Encoding UTF8
$checks | ForEach-Object { "{0} {1} - {2}" -f $_.status, $_.name, $_.detail } | Set-Content -LiteralPath $txt -Encoding UTF8
Write-Host "Soak test status: $status"
if ($status -eq "PASS") { exit 0 }
if ($status -eq "NOT TESTED") { exit 2 }
exit 1
