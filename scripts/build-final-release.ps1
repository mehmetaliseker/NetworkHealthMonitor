param(
    [string]$Version = (Get-Content -LiteralPath (Join-Path $PSScriptRoot "..\VERSION") -Raw).Trim(),
    [string]$Runtime = "win-x64",
    [switch]$CreateReleaseCandidate
)

$ErrorActionPreference = "Stop"
$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot "..")).Path
$artifacts = Join-Path $repoRoot "release\artifacts"
$staging = Join-Path $repoRoot "release\staging"
$packageRoot = Join-Path $repoRoot "release\package-root"
$suffix = if ($CreateReleaseCandidate) { "-rc" } else { "" }
$artifactBase = "NetworkHealthMonitor-Server-$Runtime-v$Version$suffix"
$zipPath = Join-Path $artifacts "$artifactBase.zip"
$shaPath = "$zipPath.sha256"

function Invoke-Checked {
    param([string]$Name, [scriptblock]$Command)
    Write-Host "==> $Name"
    & $Command
    if ($LASTEXITCODE -ne 0) {
        throw "$Name failed. ExitCode=$LASTEXITCODE"
    }
}

function Assert-CleanGit {
    $status = git status --short
    if (-not [string]::IsNullOrWhiteSpace($status)) {
        throw "Git calisma agaci temiz degil. Final/RC paket icin once degisiklikleri commit edin."
    }
}

function Assert-Branch {
    $branch = (git branch --show-current).Trim()
    if ($branch -ne "release/final-production-scheduler") {
        throw "Beklenen branch release/final-production-scheduler, mevcut branch $branch."
    }
}

function Assert-ExternalReports {
    $verification = Join-Path $repoRoot "release\verification"
    $reports = @(
        "windows-service-acceptance.json",
        "migration-acceptance.json",
        "notification-acceptance.json",
        "ui-acceptance.json",
        "soak-test.json",
        "upgrade-rollback-acceptance.json"
    )

    foreach ($report in $reports) {
        $path = Join-Path $verification $report
        if (-not (Test-Path $path)) {
            throw "Final release gate eksik: $path"
        }

        $content = Get-Content -LiteralPath $path -Raw
        if ($content -notmatch '"status"\s*:\s*"PASS"') {
            throw "Final release gate PASS degil: $path"
        }
    }
}

function Copy-IfExists([string]$Source, [string]$Destination) {
    if (Test-Path $Source) {
        New-Item -ItemType Directory -Force -Path (Split-Path -Parent $Destination) | Out-Null
        Copy-Item -LiteralPath $Source -Destination $Destination -Force
    }
}

function Copy-DirectoryContents([string]$Source, [string]$Destination) {
    if (-not (Test-Path $Source)) {
        throw "Kopyalanacak klasor bulunamadi: $Source"
    }

    New-Item -ItemType Directory -Force -Path $Destination | Out-Null
    $items = Get-ChildItem -LiteralPath $Source -Force
    if (-not $items) {
        throw "Kopyalanacak klasor bos: $Source"
    }

    foreach ($item in $items) {
        Copy-Item -LiteralPath $item.FullName -Destination $Destination -Recurse -Force
    }
}

function Assert-ReleasePackageLayout([string]$Root) {
    $requiredFiles = @(
        "ui\NetworkHealthMonitor.exe",
        "worker\NetworkHealthMonitor.Worker.exe",
        "scripts\install-service.ps1",
        "scripts\production-readiness-test.ps1",
        "README-SERVER.md",
        "VERSION"
    )

    foreach ($requiredFile in $requiredFiles) {
        $path = Join-Path $Root $requiredFile
        if (-not (Test-Path $path)) {
            throw "Release paketi eksik dosya iceriyor: $requiredFile"
        }
    }

    $fileCount = (Get-ChildItem -LiteralPath $Root -Recurse -File | Measure-Object).Count
    if ($fileCount -lt 50) {
        throw "Release paketi beklenenden az dosya iceriyor: $fileCount"
    }
}

function Test-ForbiddenPackageContent([string]$Root) {
    $forbidden = Get-ChildItem -LiteralPath $Root -Recurse -Force |
        Where-Object {
            $_.FullName -match '\\(\.git|bin|obj|TestResults)\\' -or
            $_.Name -match '\.(pdb|trx)$' -or
            $_.Name -match 'settings\.json|network_health_monitor\.db|\.log$'
        }
    if ($forbidden) {
        throw "ZIP icin yasakli dosya bulundu: $($forbidden[0].FullName)"
    }
}

function Test-SecretPatterns([string]$Root) {
    $patterns = @("smtp.*password\s*[:=]\s*[^`\r`\n]+", "ntfy.*token\s*[:=]\s*[^`\r`\n]+", "authorization:\s*bearer\s+")
    foreach ($file in Get-ChildItem -LiteralPath $Root -Recurse -File) {
        try {
            $text = Get-Content -LiteralPath $file.FullName -Raw -ErrorAction Stop
            foreach ($pattern in $patterns) {
                if ($text -match $pattern) {
                    throw "Secret benzeri icerik bulundu: $($file.FullName)"
                }
            }
        }
        catch [System.Management.Automation.ItemNotFoundException] {
            throw
        }
        catch {
            if ($_.Exception.Message -like "Secret benzeri*") { throw }
        }
    }
}

Assert-Branch
Assert-CleanGit

if (-not $CreateReleaseCandidate) {
    Assert-ExternalReports
}

if (Test-Path $staging) { Remove-Item -LiteralPath $staging -Recurse -Force }
if (Test-Path $packageRoot) { Remove-Item -LiteralPath $packageRoot -Recurse -Force }
New-Item -ItemType Directory -Force -Path $artifacts, $staging, $packageRoot | Out-Null

Invoke-Checked "dotnet restore" { dotnet restore (Join-Path $repoRoot "NetworkHealthMonitor.sln") }
Invoke-Checked "Debug build" { dotnet build (Join-Path $repoRoot "NetworkHealthMonitor.sln") -c Debug --no-restore }
Invoke-Checked "Release build" { dotnet build (Join-Path $repoRoot "NetworkHealthMonitor.sln") -c Release --no-restore }
Invoke-Checked "Tests" { dotnet test (Join-Path $repoRoot "NetworkHealthMonitor.sln") -c Release --no-build }

Invoke-Checked "UI publish" {
    dotnet publish (Join-Path $repoRoot "NetworkHealthMonitor.csproj") -c Release -r $Runtime --self-contained true -o (Join-Path $staging "ui") /p:DebugType=None /p:DebugSymbols=false
}
Invoke-Checked "Worker publish" {
    dotnet publish (Join-Path $repoRoot "NetworkHealthMonitor.Worker\NetworkHealthMonitor.Worker.csproj") -c Release -r $Runtime --self-contained true -o (Join-Path $staging "worker") /p:DebugType=None /p:DebugSymbols=false
}

Copy-DirectoryContents (Join-Path $staging "ui") (Join-Path $packageRoot "ui")
Copy-DirectoryContents (Join-Path $staging "worker") (Join-Path $packageRoot "worker")
Copy-DirectoryContents (Join-Path $repoRoot "scripts") (Join-Path $packageRoot "scripts")
Copy-DirectoryContents (Join-Path $repoRoot "docs") (Join-Path $packageRoot "docs")
Copy-IfExists (Join-Path $repoRoot "README-SERVER.md") (Join-Path $packageRoot "README-SERVER.md")
Copy-IfExists (Join-Path $repoRoot "README.md") (Join-Path $packageRoot "docs\README.md")
Copy-IfExists (Join-Path $repoRoot "INSTALLATION-GUIDE.md") (Join-Path $packageRoot "INSTALLATION-GUIDE.md")
Copy-IfExists (Join-Path $repoRoot "SMTP-CONFIGURATION.md") (Join-Path $packageRoot "SMTP-CONFIGURATION.md")
Copy-IfExists (Join-Path $repoRoot "NTFY-CONFIGURATION.md") (Join-Path $packageRoot "NTFY-CONFIGURATION.md")
Copy-IfExists (Join-Path $repoRoot "MIGRATION.md") (Join-Path $packageRoot "MIGRATION.md")
Copy-IfExists (Join-Path $repoRoot "BACKUP-RESTORE.md") (Join-Path $packageRoot "BACKUP-RESTORE.md")
Copy-IfExists (Join-Path $repoRoot "UPGRADE-GUIDE.md") (Join-Path $packageRoot "UPGRADE-GUIDE.md")
Copy-IfExists (Join-Path $repoRoot "TROUBLESHOOTING.md") (Join-Path $packageRoot "TROUBLESHOOTING.md")
Copy-IfExists (Join-Path $repoRoot "RELEASE-NOTES.md") (Join-Path $packageRoot "RELEASE-NOTES.md")
Copy-IfExists (Join-Path $repoRoot "KNOWN-LIMITATIONS.md") (Join-Path $packageRoot "KNOWN-LIMITATIONS.md")
Copy-IfExists (Join-Path $repoRoot "TEST-SUMMARY.md") (Join-Path $packageRoot "TEST-SUMMARY.md")
Copy-IfExists (Join-Path $repoRoot "VERSION") (Join-Path $packageRoot "VERSION")

Get-ChildItem -LiteralPath $packageRoot -Recurse -File |
    Where-Object { $_.Extension -in @(".pdb", ".trx") } |
    Remove-Item -Force

Assert-ReleasePackageLayout $packageRoot
Test-ForbiddenPackageContent $packageRoot
Test-SecretPatterns $packageRoot

if (Test-Path $zipPath) { Remove-Item -LiteralPath $zipPath -Force }
Compress-Archive -Path (Join-Path $packageRoot "*") -DestinationPath $zipPath -Force
$hash = (Get-FileHash -Algorithm SHA256 -LiteralPath $zipPath).Hash.ToLowerInvariant()
"$hash  $(Split-Path -Leaf $zipPath)" | Set-Content -LiteralPath $shaPath -Encoding ASCII

$extractRoot = Join-Path $artifacts "$artifactBase-extracted"
if (Test-Path $extractRoot) { Remove-Item -LiteralPath $extractRoot -Recurse -Force }
Expand-Archive -LiteralPath $zipPath -DestinationPath $extractRoot -Force
Assert-ReleasePackageLayout $extractRoot
Test-ForbiddenPackageContent $extractRoot
Test-SecretPatterns $extractRoot
$expectedHash = ((Get-Content -LiteralPath $shaPath -Raw) -split '\s+')[0].ToLowerInvariant()
if ($expectedHash -ne (Get-FileHash -Algorithm SHA256 -LiteralPath $zipPath).Hash.ToLowerInvariant()) {
    throw "SHA256 dosyasi ZIP ile eslesmedi."
}

$manifestPath = Join-Path $artifacts "release-manifest-v$Version$suffix.json"
$releaseNotesArtifact = Join-Path $artifacts "NetworkHealthMonitor-Release-Notes-v$Version$suffix.md"
Copy-IfExists (Join-Path $repoRoot "RELEASE-NOTES.md") $releaseNotesArtifact
[pscustomobject]@{
    version = $Version
    runtime = $Runtime
    releaseCandidate = [bool]$CreateReleaseCandidate
    zipPath = $zipPath
    sha256 = $hash
    createdAtUtc = [DateTime]::UtcNow.ToString("O")
    commit = (git rev-parse HEAD).Trim()
    releaseNotesPath = $releaseNotesArtifact
} | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath $manifestPath -Encoding UTF8

Write-Host "Artifact olusturuldu: $zipPath"
Write-Host "SHA256: $hash"
Write-Host "Manifest: $manifestPath"
Write-Host "Release notes: $releaseNotesArtifact"
