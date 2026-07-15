param(
    [Parameter(Mandatory=$true)][string]$BackupPath,
    [string]$ServiceName = "NetworkHealthMonitorWorker",
    [string]$DataRoot = (Join-Path $env:ProgramData "NetworkHealthMonitor"),
    [switch]$SkipServiceCheck
)

$ErrorActionPreference = "Stop"
$programData = $DataRoot
$dataDir = Join-Path $programData "data"
$configDir = Join-Path $programData "config"
$db = Join-Path $dataDir "network_health_monitor.db"
$backupDb = Join-Path $BackupPath "network_health_monitor.db"
if (-not (Test-Path $backupDb)) { throw "Yedek veritabani bulunamadi: $backupDb" }

$service = Get-Service -Name $ServiceName -ErrorAction SilentlyContinue
$wasRunning = $service -and $service.Status -eq "Running"
if ($wasRunning) {
    Stop-Service -Name $ServiceName
    (Get-Service -Name $ServiceName).WaitForStatus("Stopped", [TimeSpan]::FromSeconds(30))
}

try {
    New-Item -ItemType Directory -Force -Path $dataDir, $configDir | Out-Null
    if (Test-Path $db) {
        try {
            & (Join-Path $PSScriptRoot "backup-data.ps1") -ServiceName $ServiceName -DataRoot $DataRoot -NoServiceStop | Out-Null
        }
        catch {
            throw "Restore oncesi backup basarisiz. $($_.Exception.Message)"
        }
    }
    Copy-Item -LiteralPath $backupDb -Destination $db -Force
    $backupConfig = Join-Path $BackupPath "config"
    if (Test-Path $backupConfig) { Copy-Item -LiteralPath (Join-Path $backupConfig "*") -Destination $configDir -Recurse -Force }
}
finally {
    if ($wasRunning) { Start-Service -Name $ServiceName }
}

& (Join-Path $PSScriptRoot "health-check.ps1") -ServiceName $ServiceName -DataRoot $DataRoot -SkipServiceCheck:$SkipServiceCheck
if ($LASTEXITCODE -ne 0) { throw "Restore sonrasi health-check basarisiz. ExitCode=$LASTEXITCODE" }
