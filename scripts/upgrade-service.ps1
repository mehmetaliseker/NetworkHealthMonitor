param(
    [Parameter(Mandatory=$true)][string]$NewWorkerPath,
    [string]$ServiceName = "NetworkHealthMonitorWorker"
)

$ErrorActionPreference = "Stop"

function Resolve-ServiceExecutablePath {
    param([Parameter(Mandatory=$true)][string]$PathName)

    $trimmed = $PathName.Trim()
    if ($trimmed.StartsWith('"')) {
        $closingQuote = $trimmed.IndexOf('"', 1)
        if ($closingQuote -gt 1) {
            return $trimmed.Substring(1, $closingQuote - 1)
        }
    }

    $exeIndex = $trimmed.IndexOf(".exe", [StringComparison]::OrdinalIgnoreCase)
    if ($exeIndex -ge 0) {
        return $trimmed.Substring(0, $exeIndex + 4).Trim('"')
    }

    return $trimmed
}

$escapedServiceName = $ServiceName.Replace("'", "''")
$service = Get-CimInstance -ClassName Win32_Service -Filter "Name='$escapedServiceName'" -ErrorAction Stop
if ($null -eq $service) { throw "Servis bulunamadi: $ServiceName" }
if ([string]::IsNullOrWhiteSpace($service.PathName)) { throw "Servis binary yolu bos: $ServiceName" }
$currentBinary = Resolve-ServiceExecutablePath -PathName $service.PathName
if (-not (Test-Path $NewWorkerPath)) { throw "Yeni worker bulunamadi: $NewWorkerPath" }
if (-not (Test-Path $currentBinary)) { throw "Mevcut servis binary bulunamadi: $currentBinary" }

try {
    & (Join-Path $PSScriptRoot "backup-data.ps1") -ServiceName $ServiceName | Out-Null
}
catch {
    throw "Upgrade oncesi backup basarisiz: $($_.Exception.Message)"
}

if ((Get-Service -Name $ServiceName).Status -ne "Stopped") {
    Stop-Service -Name $ServiceName
    (Get-Service -Name $ServiceName).WaitForStatus("Stopped", [TimeSpan]::FromSeconds(30))
}

$currentDir = Split-Path -Parent $currentBinary
if ((Get-Item $NewWorkerPath).PSIsContainer) {
    Copy-Item -LiteralPath (Join-Path $NewWorkerPath "*") -Destination $currentDir -Recurse -Force
}
else {
    Copy-Item -LiteralPath $NewWorkerPath -Destination $currentBinary -Force
}

& $currentBinary --run-once
if ($LASTEXITCODE -ne 0) { throw "Worker run-once dogrulamasi basarisiz. ExitCode=$LASTEXITCODE" }

Start-Service -Name $ServiceName
(Get-Service -Name $ServiceName).WaitForStatus("Running", [TimeSpan]::FromSeconds(30))

& (Join-Path $PSScriptRoot "health-check.ps1") -ServiceName $ServiceName -WorkerPath $currentBinary
if ($LASTEXITCODE -ne 0) { throw "Upgrade sonrasi health-check basarisiz. ExitCode=$LASTEXITCODE" }
