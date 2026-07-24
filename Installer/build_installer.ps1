<#
    Builds the PBI Port Wrapper MSI.

    1. Publishes the app as a single-file, self-contained win-x64 build (the same
       convention the release zip uses) into .\Publish.
    2. Builds the WiX package (WixToolset.Sdk + UI extension are restored from NuGet).
    3. Copies the resulting MSI to .\Output\PBIPortWrapper.msi.

    Run from anywhere:  powershell -File Installer\build_installer.ps1
#>
$ErrorActionPreference = "Stop"

# The machine/user PATH is not always inherited by the calling shell on this box.
$env:PATH = [Environment]::GetEnvironmentVariable("Path","Machine") + ";" +
            [Environment]::GetEnvironmentVariable("Path","User")

$installerRoot = $PSScriptRoot
$projectRoot   = (Resolve-Path (Join-Path $installerRoot "..")).Path
$publishDir    = Join-Path $installerRoot "Publish"
$outputDir     = Join-Path $installerRoot "Output"
$wixproj       = Join-Path $installerRoot "PBIPortWrapper.Installer.wixproj"

# 1. Publish the application (single-file, self-contained, win-x64).
if (Test-Path $publishDir) { Remove-Item $publishDir -Recurse -Force }
Write-Host "==> Publishing application to $publishDir" -ForegroundColor Cyan
dotnet publish (Join-Path $projectRoot "PBIPortWrapper.csproj") `
    -c Release -r win-x64 --self-contained true `
    -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true `
    -p:DebugType=None -p:DebugSymbols=false `
    -o $publishDir
if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed ($LASTEXITCODE)" }

# 2. Build the MSI.
Write-Host "==> Building MSI" -ForegroundColor Cyan
dotnet build $wixproj -c Release
if ($LASTEXITCODE -ne 0) { throw "wix build failed ($LASTEXITCODE)" }

# 3. Stage the MSI to Output\.
$msi = Get-ChildItem (Join-Path $installerRoot "bin") -Recurse -Filter "PBIPortWrapper.msi" `
        -ErrorAction SilentlyContinue | Select-Object -First 1
if (-not $msi) { throw "MSI not found under $installerRoot\bin after build" }

if (-not (Test-Path $outputDir)) { New-Item -ItemType Directory -Path $outputDir | Out-Null }
$dest = Join-Path $outputDir "PBIPortWrapper.msi"
Copy-Item $msi.FullName $dest -Force
Write-Host "==> Installer built: $dest" -ForegroundColor Green
