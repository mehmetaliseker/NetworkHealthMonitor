param(
    [string]$Runtime = "win-x64",
    [string]$Configuration = "Release"
)

$ErrorActionPreference = "Stop"
& (Join-Path $PSScriptRoot "Publish-NetworkHealthMonitor.ps1") -Runtime $Runtime -Configuration $Configuration
