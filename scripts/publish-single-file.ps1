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

# 5. Clean symbols, copy assets, and generate checksums
Write-Host "[5/5] Generating single-file release assets and checksums..." -ForegroundColor Green
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

# Copy AppIcon.ico to publish folder
Copy-Item -Path "$repoRoot\src\SecureVault.App\Assets\AppIcon.ico" -Destination "$OutputDir\AppIcon.ico" -Force -ErrorAction SilentlyContinue

# Remove standalone pdb files from the release folder
Get-ChildItem -Path $OutputDir -Filter "*.pdb" | Remove-Item -Force

# Remove any legacy installer exe if present
$legacySetup = Join-Path $OutputDir "SecureVault-Setup.exe"
if (Test-Path $legacySetup) {
    Remove-Item -Path $legacySetup -Force -ErrorAction SilentlyContinue
}

# Calculate SHA-256 hash for the single executable
$hashApp = Get-FileHash -Path $exePath -Algorithm SHA256
$checksumLine = "$($hashApp.Hash.ToLower())  SecureVault.exe"
$hashFile = Join-Path $OutputDir "SecureVault-win-x64.sha256"
Set-Content -Path $hashFile -Value $checksumLine -Encoding utf8

# Create zip archive for release asset
$zipFile = Join-Path $OutputDir "SecureVault-v1.0.0-win-x64.zip"
Compress-Archive -Path $exePath, "$OutputDir\AppIcon.ico", "$repoRoot\LICENSE" -DestinationPath $zipFile -Force

$exeSizeMB = [math]::Round(((Get-Item $exePath).Length / 1MB), 2)
$zipSizeMB = [math]::Round(((Get-Item $zipFile).Length / 1MB), 2)

Write-Host ""
Write-Host "============================================================" -ForegroundColor Cyan
Write-Host "  Single-File Packaging Complete!" -ForegroundColor Green
Write-Host "============================================================" -ForegroundColor Cyan
Write-Host "  Application:  $exePath ($exeSizeMB MB)" -ForegroundColor White
Write-Host "  App SHA-256:  $($hashApp.Hash)" -ForegroundColor White
Write-Host "  Checksums:    $hashFile" -ForegroundColor White
Write-Host "  Zip Archive:  $zipFile ($zipSizeMB MB)" -ForegroundColor White
Write-Host "============================================================" -ForegroundColor Cyan

