# ==============================================================================
# SecureVault Single-File Executable Packaging Script
# ==============================================================================
# Packages the entire application (WinUI 3, LibVLC, SkiaSharp, PDFium, and .NET 8)
# into a self-contained, standalone single executable.
# ==============================================================================

[CmdletBinding()]
param (
    [string]$Configuration = "Release",
    [string]$Runtime = "win-x64",
    [string]$OutputDir = "./publish",
    [switch]$SkipTests
)

$ErrorActionPreference = "Stop"

$scriptRoot = Split-Path -Parent $MyInvocation.MyCommand.Definition
$repoRoot = Resolve-Path "$scriptRoot\.."

Write-Host "============================================================" -ForegroundColor Cyan
Write-Host "  SecureVault Standalone Packaging Pipeline" -ForegroundColor Cyan
Write-Host "============================================================" -ForegroundColor Cyan

# 1. Activate .NET isolated environment if present
$dotnetCmd = "dotnet"
if ($env:DOTNET_ROOT -and (Test-Path "$env:DOTNET_ROOT\dotnet.exe")) {
    $dotnetCmd = "$env:DOTNET_ROOT\dotnet.exe"
} elseif (Test-Path "$env:USERPROFILE\.dotnet\dotnet.exe") {
    $env:DOTNET_ROOT = "$env:USERPROFILE\.dotnet"
    $env:PATH = "$env:DOTNET_ROOT;$env:PATH"
    $dotnetCmd = "$env:DOTNET_ROOT\dotnet.exe"
}

Write-Host "[1/5] Using .NET SDK: $(& $dotnetCmd --version)" -ForegroundColor Green

# 2. Run test suite verification
if (-not $SkipTests) {
    Write-Host "[2/5] Running test suite pre-flight check..." -ForegroundColor Green
    & $dotnetCmd test "$repoRoot\tests\SecureVault.Core.Tests\SecureVault.Core.Tests.csproj" -c $Configuration --verbosity minimal
    if ($LASTEXITCODE -ne 0) {
        Write-Error "Pre-flight test suite failed. Aborting packaging."
        exit $LASTEXITCODE
    }
} else {
    Write-Host "[2/5] Skipping test suite per -SkipTests flag." -ForegroundColor Yellow
}

# 3. Clean and prepare output directory
Write-Host "[3/5] Cleaning output directory: $OutputDir..." -ForegroundColor Green
if (Test-Path $OutputDir) {
    Remove-Item -Path $OutputDir -Recurse -Force
}
New-Item -ItemType Directory -Path $OutputDir -Force | Out-Null

# 4. Execute single-file publish for SecureVault.App
Write-Host "[4/6] Publishing single-file application ($Configuration | $Runtime)..." -ForegroundColor Green
& $dotnetCmd publish "$repoRoot\src\SecureVault.App\SecureVault.App.csproj" `
    -c $Configuration `
    -r $Runtime `
    --self-contained true `
    -p:PublishSingleFile=true `
    -p:EnableMsixTooling=true `
    -o $OutputDir

if ($LASTEXITCODE -ne 0) {
    Write-Error "Publish command for SecureVault.App failed with exit code $LASTEXITCODE."
    exit $LASTEXITCODE
}

# 5. Execute single-file publish for SecureVault.Installer
Write-Host "[5/6] Publishing installation wizard ($Configuration | $Runtime)..." -ForegroundColor Green
$installerTempDir = "$OutputDir\_installer_temp"
& $dotnetCmd publish "$repoRoot\src\SecureVault.Installer\SecureVault.Installer.csproj" `
    -c $Configuration `
    -r $Runtime `
    --self-contained true `
    -p:PublishSingleFile=true `
    -o $installerTempDir

if ($LASTEXITCODE -ne 0) {
    Write-Error "Publish command for SecureVault.Installer failed with exit code $LASTEXITCODE."
    exit $LASTEXITCODE
}

# Copy setup executable to publish folder
$setupExeSource = Join-Path $installerTempDir "SecureVault-Setup.exe"
$setupExeDest = Join-Path $OutputDir "SecureVault-Setup.exe"
if (Test-Path $setupExeSource) {
    Copy-Item -Path $setupExeSource -Destination $setupExeDest -Force
}
Remove-Item -Path $installerTempDir -Recurse -Force -ErrorAction SilentlyContinue

# Copy AppIcon.ico to publish folder
Copy-Item -Path "$repoRoot\src\SecureVault.App\Assets\AppIcon.ico" -Destination "$OutputDir\AppIcon.ico" -Force -ErrorAction SilentlyContinue

# 6. Clean symbols and generate checksums
Write-Host "[6/6] Generating release assets and checksums..." -ForegroundColor Green
$exePath = Join-Path $OutputDir "SecureVault.exe"
if (-not (Test-Path $exePath)) {
    # If output was named SecureVault.App.exe, rename to SecureVault.exe
    $fallbackPath = Join-Path $OutputDir "SecureVault.App.exe"
    if (Test-Path $fallbackPath) {
        Rename-Item -Path $fallbackPath -NewName "SecureVault.exe"
    } else {
        Write-Error "Expected executable not found in $OutputDir."
        exit 1
    }
}

# Remove standalone pdb files from the release folder
Get-ChildItem -Path $OutputDir -Filter "*.pdb" | Remove-Item -Force

# Calculate SHA-256 hashes
$hashApp = Get-FileHash -Path $exePath -Algorithm SHA256
$hashSetup = Get-FileHash -Path $setupExeDest -Algorithm SHA256

$checksumLines = @(
    "$($hashApp.Hash.ToLower())  SecureVault.exe",
    "$($hashSetup.Hash.ToLower())  SecureVault-Setup.exe"
)
$hashFile = Join-Path $OutputDir "SecureVault-win-x64.sha256"
Set-Content -Path $hashFile -Value ($checksumLines -join "`r`n") -Encoding utf8

# Create zip archive for release asset
$zipFile = Join-Path $OutputDir "SecureVault-v1.0.0-win-x64.zip"
Compress-Archive -Path $exePath, $setupExeDest, "$OutputDir\AppIcon.ico", "$repoRoot\LICENSE" -DestinationPath $zipFile -Force

$exeSizeMB = [math]::Round(((Get-Item $exePath).Length / 1MB), 2)
$setupSizeMB = [math]::Round(((Get-Item $setupExeDest).Length / 1MB), 2)
$zipSizeMB = [math]::Round(((Get-Item $zipFile).Length / 1MB), 2)

Write-Host ""
Write-Host "============================================================" -ForegroundColor Cyan
Write-Host "  Packaging Complete!" -ForegroundColor Green
Write-Host "============================================================" -ForegroundColor Cyan
Write-Host "  Application:  $exePath ($exeSizeMB MB)" -ForegroundColor White
Write-Host "  Installer:    $setupExeDest ($setupSizeMB MB)" -ForegroundColor White
Write-Host "  App SHA-256:  $($hashApp.Hash)" -ForegroundColor White
Write-Host "  Setup SHA:    $($hashSetup.Hash)" -ForegroundColor White
Write-Host "  Checksums:    $hashFile" -ForegroundColor White
Write-Host "  Zip Archive:  $zipFile ($zipSizeMB MB)" -ForegroundColor White
Write-Host "============================================================" -ForegroundColor Cyan
