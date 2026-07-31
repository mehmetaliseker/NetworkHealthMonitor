param(
    [string]$Runtime = "win-x64",
    [string]$Configuration = "Release"
)

$ErrorActionPreference = "Stop"
$repoRoot = (Resolve-Path -LiteralPath (Join-Path $PSScriptRoot "..")).Path
$artifactsRoot = Join-Path $repoRoot "artifacts"
$layoutRoot = Join-Path $artifactsRoot $Runtime
$zipPath = Join-Path $artifactsRoot "NetworkHealthMonitor-Server-$Runtime.zip"
$shaPath = "$zipPath.sha256"

function Invoke-Checked {
    param(
        [string]$Name,
        [scriptblock]$Command
    )

    Write-Host "==> $Name"
    & $Command
    if ($LASTEXITCODE -ne 0) {
        throw "$Name failed. ExitCode=$LASTEXITCODE"
    }
}

function Assert-UnderDirectory {
    param(
        [string]$Child,
        [string]$Parent
    )

    $childFull = [System.IO.Path]::GetFullPath($Child)
    $parentFull = [System.IO.Path]::GetFullPath($Parent).TrimEnd('\') + '\'
    if (-not $childFull.StartsWith($parentFull, [System.StringComparison]::OrdinalIgnoreCase)) {
        throw "Refusing to modify path outside artifacts root: $childFull"
    }
}

function Copy-DirectoryContents {
    param(
        [string]$Source,
        [string]$Destination
    )

    if (-not (Test-Path -LiteralPath $Source)) {
        throw "Directory was not found: $Source"
    }

    New-Item -ItemType Directory -Force -Path $Destination | Out-Null
    Get-ChildItem -LiteralPath $Source -Force | ForEach-Object {
        Copy-Item -LiteralPath $_.FullName -Destination $Destination -Recurse -Force
    }
}

New-Item -ItemType Directory -Force -Path $artifactsRoot | Out-Null
Assert-UnderDirectory -Child $layoutRoot -Parent $artifactsRoot
if (Test-Path -LiteralPath $layoutRoot) {
    Remove-Item -LiteralPath $layoutRoot -Recurse -Force
}

if (Test-Path -LiteralPath $zipPath) {
    Remove-Item -LiteralPath $zipPath -Force
}

if (Test-Path -LiteralPath $shaPath) {
    Remove-Item -LiteralPath $shaPath -Force
}

New-Item -ItemType Directory -Force -Path $layoutRoot | Out-Null

Invoke-Checked "dotnet restore" { dotnet restore (Join-Path $repoRoot "NetworkHealthMonitor.sln") }
Invoke-Checked "dotnet build" { dotnet build (Join-Path $repoRoot "NetworkHealthMonitor.sln") -c $Configuration --no-restore }
Invoke-Checked "dotnet test" { dotnet test (Join-Path $repoRoot "NetworkHealthMonitor.sln") -c $Configuration --no-build }

Invoke-Checked "UI publish" {
    dotnet publish (Join-Path $repoRoot "NetworkHealthMonitor.csproj") -c $Configuration -r $Runtime --self-contained true -o (Join-Path $layoutRoot "UI") /p:DebugType=None /p:DebugSymbols=false
}

Invoke-Checked "Worker publish" {
    dotnet publish (Join-Path $repoRoot "NetworkHealthMonitor.Worker\NetworkHealthMonitor.Worker.csproj") -c $Configuration -r $Runtime --self-contained true -o (Join-Path $layoutRoot "Worker") /p:DebugType=None /p:DebugSymbols=false
}

Invoke-Checked "Tray publish" {
    dotnet publish (Join-Path $repoRoot "NetworkHealthMonitor.Tray\NetworkHealthMonitor.Tray.csproj") -c $Configuration -r $Runtime --self-contained true -o (Join-Path $layoutRoot "Tray") /p:DebugType=None /p:DebugSymbols=false
}

Copy-DirectoryContents -Source (Join-Path $repoRoot "scripts") -Destination (Join-Path $layoutRoot "Scripts")
if (Test-Path -LiteralPath (Join-Path $repoRoot "docs")) {
    Copy-DirectoryContents -Source (Join-Path $repoRoot "docs") -Destination (Join-Path $layoutRoot "Docs")
}

$optionalFiles = @(
    "README-SERVER.md",
    "README.md",
    "INSTALLATION-GUIDE.md",
    "TROUBLESHOOTING.md",
    "VERSION"
)

foreach ($fileName in $optionalFiles) {
    $source = Join-Path $repoRoot $fileName
    if (Test-Path -LiteralPath $source) {
        Copy-Item -LiteralPath $source -Destination (Join-Path $layoutRoot $fileName) -Force
    }
}

$requiredFiles = @(
    "UI\NetworkHealthMonitor.exe",
    "Worker\NetworkHealthMonitor.Worker.exe",
    "Tray\NetworkHealthMonitor.Tray.exe",
    "Scripts\Install-NetworkHealthMonitor.ps1",
    "Scripts\Install-WorkerService.ps1",
    "Scripts\Verify-Installation.ps1"
)

foreach ($required in $requiredFiles) {
    $path = Join-Path $layoutRoot $required
    if (-not (Test-Path -LiteralPath $path)) {
        throw "Package layout is missing required file: $required"
    }
}

Compress-Archive -Path (Join-Path $layoutRoot "*") -DestinationPath $zipPath -Force
$hash = (Get-FileHash -Algorithm SHA256 -LiteralPath $zipPath).Hash.ToLowerInvariant()
"$hash  $(Split-Path -Leaf $zipPath)" | Set-Content -LiteralPath $shaPath -Encoding ASCII

Write-Host "Publish layout: $layoutRoot"
Write-Host "Package: $zipPath"
Write-Host "SHA256: $hash"
