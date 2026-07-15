param(
    [string]$UiPath = (Join-Path $PSScriptRoot "..\ui\NetworkHealthMonitor.exe")
)

$ErrorActionPreference = "Stop"
$startup = Join-Path $env:AppData "Microsoft\Windows\Start Menu\Programs\Startup"
$shortcutPath = Join-Path $startup "NetworkHealthMonitor.lnk"
$resolvedUi = (Resolve-Path $UiPath).Path
if (-not (Test-Path $resolvedUi)) { throw "UI exe bulunamadi: $resolvedUi" }

New-Item -ItemType Directory -Force -Path $startup | Out-Null
if (Test-Path $shortcutPath) { Remove-Item -LiteralPath $shortcutPath -Force }

$shell = New-Object -ComObject WScript.Shell
$shortcut = $shell.CreateShortcut($shortcutPath)
$shortcut.TargetPath = $resolvedUi
$shortcut.WorkingDirectory = Split-Path -Parent $resolvedUi
$shortcut.Description = "Network Health Monitor management UI"
$shortcut.Save()

Write-Host "UI autostart etkin: $shortcutPath"
