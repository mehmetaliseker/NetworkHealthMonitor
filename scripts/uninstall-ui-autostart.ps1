$ErrorActionPreference = "Stop"
$shortcutPath = Join-Path $env:AppData "Microsoft\Windows\Start Menu\Programs\Startup\NetworkHealthMonitor.lnk"

if (Test-Path $shortcutPath) {
    Remove-Item -LiteralPath $shortcutPath -Force
    Write-Host "UI autostart kaldirildi: $shortcutPath"
}
else {
    Write-Host "UI autostart zaten kurulu degil."
}
