param(
    [string]$ScreenshotDirectory = "release\verification\ui-acceptance",
    [string]$ReportJsonPath = "release\verification\ui-acceptance.json",
    [string]$ReportTextPath = "release\verification\ui-acceptance.txt",
    [switch]$Confirm1366x768Scale100,
    [switch]$Confirm1366x768Scale125,
    [switch]$Confirm1920x1080Scale100,
    [switch]$Confirm1920x1080Scale150,
    [switch]$ConfirmLongDeviceNamesVisible,
    [switch]$ConfirmIpAddressesVisible,
    [switch]$ConfirmScheduleFormUsable,
    [switch]$ConfirmKeyboardNavigation,
    [switch]$ConfirmWorkerStatusVisible,
    [switch]$ConfirmUiDoesNotBlockWorker
)

$ErrorActionPreference = "Stop"
$checks = New-Object System.Collections.Generic.List[object]

function Add-Check([string]$Status, [string]$Name, [string]$Detail = "") {
    $checks.Add([pscustomobject]@{ status = $Status; name = $Name; detail = $Detail; checkedAtUtc = [DateTime]::UtcNow.ToString("O") }) | Out-Null
    Write-Host "$Status $Name - $Detail"
}

$screenshotPath = if ([System.IO.Path]::IsPathRooted($ScreenshotDirectory)) { $ScreenshotDirectory } else { Join-Path (Get-Location) $ScreenshotDirectory }
$screenshotCount = if (Test-Path $screenshotPath) { (Get-ChildItem -LiteralPath $screenshotPath -Recurse -File | Measure-Object).Count } else { 0 }
Add-Check ($(if ($screenshotCount -gt 0) { "PASS" } else { "NOT TESTED" })) "Screenshot kanitlari" "Path=$screenshotPath Count=$screenshotCount"
Add-Check ($(if ($Confirm1366x768Scale100) { "PASS" } else { "NOT TESTED" })) "1366x768 %100" "Manuel gorsel kanit gerekir."
Add-Check ($(if ($Confirm1366x768Scale125) { "PASS" } else { "NOT TESTED" })) "1366x768 %125" "Manuel gorsel kanit gerekir."
Add-Check ($(if ($Confirm1920x1080Scale100) { "PASS" } else { "NOT TESTED" })) "1920x1080 %100" "Manuel gorsel kanit gerekir."
Add-Check ($(if ($Confirm1920x1080Scale150) { "PASS" } else { "NOT TESTED" })) "1920x1080 %150" "Manuel gorsel kanit gerekir."
Add-Check ($(if ($ConfirmLongDeviceNamesVisible) { "PASS" } else { "NOT TESTED" })) "Uzun cihaz adlari gorunur" "Manuel UI kaniti gerekir."
Add-Check ($(if ($ConfirmIpAddressesVisible) { "PASS" } else { "NOT TESTED" })) "IP adresleri gorunur" "Manuel UI kaniti gerekir."
Add-Check ($(if ($ConfirmScheduleFormUsable) { "PASS" } else { "NOT TESTED" })) "Zamanlama formu kullanilabilir" "Manuel UI kaniti gerekir."
Add-Check ($(if ($ConfirmKeyboardNavigation) { "PASS" } else { "NOT TESTED" })) "Klavye navigasyonu" "Manuel UI kaniti gerekir."
Add-Check ($(if ($ConfirmWorkerStatusVisible) { "PASS" } else { "NOT TESTED" })) "Worker durumu gorunur" "Manuel UI kaniti gerekir."
Add-Check ($(if ($ConfirmUiDoesNotBlockWorker) { "PASS" } else { "NOT TESTED" })) "UI Worker'i etkilemiyor" "UI kapatildiktan sonra heartbeat/log kaniti gerekir."

$status = if (@($checks | Where-Object status -eq "FAIL").Count -gt 0) { "FAIL" } elseif (@($checks | Where-Object status -eq "NOT TESTED").Count -gt 0) { "NOT TESTED" } else { "PASS" }
$report = [pscustomobject]@{ status = $status; generatedAtUtc = [DateTime]::UtcNow.ToString("O"); screenshotDirectory = $screenshotPath; checks = $checks }
$json = if ([System.IO.Path]::IsPathRooted($ReportJsonPath)) { $ReportJsonPath } else { Join-Path (Get-Location) $ReportJsonPath }
$txt = if ([System.IO.Path]::IsPathRooted($ReportTextPath)) { $ReportTextPath } else { Join-Path (Get-Location) $ReportTextPath }
New-Item -ItemType Directory -Force -Path (Split-Path -Parent $json), (Split-Path -Parent $txt) | Out-Null
$report | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $json -Encoding UTF8
$checks | ForEach-Object { "{0} {1} - {2}" -f $_.status, $_.name, $_.detail } | Set-Content -LiteralPath $txt -Encoding UTF8
Write-Host "UI acceptance status: $status"
if ($status -eq "PASS") { exit 0 }
if ($status -eq "NOT TESTED") { exit 2 }
exit 1
