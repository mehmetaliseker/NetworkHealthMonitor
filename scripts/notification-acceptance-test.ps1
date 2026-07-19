param(
    [string]$ReportJsonPath = "release\verification\notification-acceptance.json",
    [string]$ReportTextPath = "release\verification\notification-acceptance.txt",
    [switch]$ConfirmRealSmtpConnectionTestPassed,
    [switch]$ConfirmRealSmtpEmailReceived,
    [switch]$ConfirmRealNtfyReceived,
    [switch]$ConfirmInitialOfflineNotificationReceivedOnce,
    [switch]$ConfirmEscalationNotificationReceivedOnce,
    [switch]$ConfirmRecoveryNotificationReceivedOnce,
    [switch]$ConfirmNoDuplicateNotifications,
    [switch]$ConfirmNoSecretsLogged
)

$ErrorActionPreference = "Stop"
$checks = New-Object System.Collections.Generic.List[object]

function Add-Check([string]$Status, [string]$Name, [string]$Detail = "") {
    $checks.Add([pscustomobject]@{ status = $Status; name = $Name; detail = $Detail; checkedAtUtc = [DateTime]::UtcNow.ToString("O") }) | Out-Null
    Write-Host "$Status $Name - $Detail"
}

Add-Check ($(if ($ConfirmRealSmtpConnectionTestPassed) { "PASS" } else { "NOT TESTED" })) "Gercek SMTP baglanti testi" "Gercek SMTP credential ve UI sonucu gerekir."
Add-Check ($(if ($ConfirmRealSmtpEmailReceived) { "PASS" } else { "NOT TESTED" })) "Gercek SMTP test e-postasi alindi" "Gelen kutusu kaniti gerekir."
Add-Check ($(if ($ConfirmRealNtfyReceived) { "PASS" } else { "NOT TESTED" })) "Gercek ntfy bildirimi alindi" "Gercek subscriber/telefon kaniti gerekir."
Add-Check ($(if ($ConfirmInitialOfflineNotificationReceivedOnce) { "PASS" } else { "NOT TESTED" })) "Ilk kesinti bildirimi tek kez alindi" "Outbox ve alici kaniti gerekir."
Add-Check ($(if ($ConfirmEscalationNotificationReceivedOnce) { "PASS" } else { "NOT TESTED" })) "Escalation bildirimi tek kez alindi" "Escalation alici listesi kaniti gerekir."
Add-Check ($(if ($ConfirmRecoveryNotificationReceivedOnce) { "PASS" } else { "NOT TESTED" })) "Recovery bildirimi tek kez alindi" "Recovery ayari aciksa kanit gerekir."
Add-Check ($(if ($ConfirmNoDuplicateNotifications) { "PASS" } else { "NOT TESTED" })) "Duplicate bildirim yok" "Incident/outbox ve alici bazli kanit gerekir."
Add-Check ($(if ($ConfirmNoSecretsLogged) { "PASS" } else { "NOT TESTED" })) "Secret loglanmadi" "Log tarama kaniti gerekir."

$status = if (@($checks | Where-Object status -eq "FAIL").Count -gt 0) { "FAIL" } elseif (@($checks | Where-Object status -eq "NOT TESTED").Count -gt 0) { "NOT TESTED" } else { "PASS" }
$report = [pscustomobject]@{ status = $status; generatedAtUtc = [DateTime]::UtcNow.ToString("O"); checks = $checks }
$json = if ([System.IO.Path]::IsPathRooted($ReportJsonPath)) { $ReportJsonPath } else { Join-Path (Get-Location) $ReportJsonPath }
$txt = if ([System.IO.Path]::IsPathRooted($ReportTextPath)) { $ReportTextPath } else { Join-Path (Get-Location) $ReportTextPath }
New-Item -ItemType Directory -Force -Path (Split-Path -Parent $json), (Split-Path -Parent $txt) | Out-Null
$report | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $json -Encoding UTF8
$checks | ForEach-Object { "{0} {1} - {2}" -f $_.status, $_.name, $_.detail } | Set-Content -LiteralPath $txt -Encoding UTF8
Write-Host "Notification acceptance status: $status"
if ($status -eq "PASS") { exit 0 }
if ($status -eq "NOT TESTED") { exit 2 }
exit 1
