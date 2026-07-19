# Build Installer Script
$ErrorActionPreference = "Stop"

$projectRoot = Resolve-Path "$PSScriptRoot\.."
$installerRoot = $PSScriptRoot
$publishDir = Join-Path $installerRoot "Publish"
$outputDir = Join-Path $installerRoot "Output"
$msiPath = Join-Path $outputDir "PBIPortWrapper.msi"

# Ensure WiX tool is installed/available
Write-Host "Checking for WiX tool..."
if (-not (Get-Command wix -ErrorAction SilentlyContinue)) {
    Write-Host "WiX tool not found. Installing..."
    dotnet tool install --global wix
}

# Clean Output
if (Test-Path $outputDir) { Remove-Item $outputDir -Recurse -Force }
New-Item -ItemType Directory -Path $outputDir | Out-Null

# Clean and Re-Publish App
if (Test-Path $publishDir) { Remove-Item $publishDir -Recurse -Force }
New-Item -ItemType Directory -Path $publishDir | Out-Null

Write-Host "Publishing Application to $publishDir..."
dotnet publish "$projectRoot\PBIPortWrapper.csproj" -c Release -r win-x64 --self-contained false -o $publishDir

Write-Host "Ensuring WiX Extensions are available..."
wix extension add WixToolset.UI.wixext --global
wix extension add WixToolset.Util.wixext --global

Write-Host "Building MSI with WiX..."
# Note: We use -b $installerRoot so that Source="Publish\..." in Package.wxs resolves to InstallerRoot\Publish
wix build "$installerRoot\Package.wxs" -o $msiPath -b $installerRoot -ext WixToolset.UI.wixext -ext WixToolset.Util.wixext

if (Test-Path $msiPath) {
    Write-Host "Build Complete. Installer is at: $msiPath"
}
else {
    Write-Error "Build failed. MSI not found at $msiPath"
}
