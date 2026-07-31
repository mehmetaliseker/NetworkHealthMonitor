param(
    [string]$SourceRoot = (Split-Path -Parent $PSScriptRoot),
    [string]$InstallRoot = (Join-Path $env:ProgramFiles "NetworkHealthMonitor"),
    [string]$ServiceName = "NetworkHealthMonitorWorker",
    [switch]$StartWorker,
    [switch]$StartTray,
    [switch]$CreateTrayShortcut,
    [switch]$EnableWorkerAutoStartOnBoot
)

$ErrorActionPreference = "Stop"

function Assert-Administrator {
    $identity = [Security.Principal.WindowsIdentity]::GetCurrent()
    $principal = New-Object Security.Principal.WindowsPrincipal($identity)
    if (-not $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
        throw "This script must be run from an elevated PowerShell session."
    }
}

function Copy-DirectoryContents {
    param(
        [string]$Source,
        [string]$Destination
    )

    if (-not (Test-Path -LiteralPath $Source)) {
        throw "Source directory was not found: $Source"
    }

    New-Item -ItemType Directory -Force -Path $Destination | Out-Null
    Get-ChildItem -LiteralPath $Source -Force | ForEach-Object {
        Copy-Item -LiteralPath $_.FullName -Destination $Destination -Recurse -Force
    }
}

function New-Shortcut {
    param(
        [string]$ShortcutPath,
        [string]$TargetPath,
        [string]$WorkingDirectory,
        [string]$Description
    )

    $shortcutDirectory = Split-Path -Parent $ShortcutPath
    New-Item -ItemType Directory -Force -Path $shortcutDirectory | Out-Null
    $shell = New-Object -ComObject WScript.Shell
    $shortcut = $shell.CreateShortcut($ShortcutPath)
    $shortcut.TargetPath = $TargetPath
    $shortcut.WorkingDirectory = $WorkingDirectory
    $shortcut.Description = $Description
    $shortcut.Save()
}

Assert-Administrator

$resolvedSourceRoot = (Resolve-Path -LiteralPath $SourceRoot -ErrorAction Stop).Path
$resolvedInstallRoot = $InstallRoot
New-Item -ItemType Directory -Force -Path $resolvedInstallRoot | Out-Null
$resolvedInstallRoot = (Resolve-Path -LiteralPath $resolvedInstallRoot).Path

$folders = @("UI", "Worker", "Tray", "Scripts")
foreach ($folder in $folders) {
    Copy-DirectoryContents -Source (Join-Path $resolvedSourceRoot $folder) -Destination (Join-Path $resolvedInstallRoot $folder)
}

$workerPath = Join-Path $resolvedInstallRoot "Worker\NetworkHealthMonitor.Worker.exe"
$installWorkerScript = Join-Path $resolvedInstallRoot "Scripts\Install-WorkerService.ps1"
& $installWorkerScript -WorkerPath $workerPath -ServiceName $ServiceName

if ($EnableWorkerAutoStartOnBoot) {
    $output = & sc.exe config $ServiceName start= auto 2>&1
    if ($LASTEXITCODE -ne 0) {
        throw "Unable to enable worker auto-start. ExitCode=$LASTEXITCODE Output=$($output -join ' ')"
    }
    Set-Service -Name $ServiceName -StartupType Automatic
}

$startMenuFolder = Join-Path $env:ProgramData "Microsoft\Windows\Start Menu\Programs\Network Health Monitor"
$uiPath = Join-Path $resolvedInstallRoot "UI\NetworkHealthMonitor.exe"
$trayPath = Join-Path $resolvedInstallRoot "Tray\NetworkHealthMonitor.Tray.exe"
if (Test-Path -LiteralPath $uiPath) {
    New-Shortcut `
        -ShortcutPath (Join-Path $startMenuFolder "Network Health Monitor.lnk") `
        -TargetPath $uiPath `
        -WorkingDirectory (Split-Path -Parent $uiPath) `
        -Description "Network Health Monitor"
}

if ($CreateTrayShortcut -and (Test-Path -LiteralPath $trayPath)) {
    New-Shortcut `
        -ShortcutPath (Join-Path $startMenuFolder "Network Health Monitor Tray.lnk") `
        -TargetPath $trayPath `
        -WorkingDirectory (Split-Path -Parent $trayPath) `
        -Description "Network Health Monitor tray"
}

if ($StartWorker) {
    & (Join-Path $resolvedInstallRoot "Scripts\Start-Worker.ps1") -ServiceName $ServiceName
}

if ($StartTray) {
    $trayPath = Join-Path $resolvedInstallRoot "Tray\NetworkHealthMonitor.Tray.exe"
    if (Test-Path -LiteralPath $trayPath) {
        Start-Process -FilePath $trayPath -WorkingDirectory (Split-Path -Parent $trayPath) -WindowStyle Hidden
    }
}

Write-Host "Network Health Monitor installed: $resolvedInstallRoot"
if ($EnableWorkerAutoStartOnBoot) {
    Write-Host "Worker startup type: Automatic"
    Write-Host "Worker auto-start on boot: enabled by explicit parameter"
}
else {
    Write-Host "Worker startup type: Manual"
    Write-Host "Worker auto-start on boot: disabled"
}
