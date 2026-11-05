[CmdletBinding()]
param(
    [string]$RuntimePackVersion = '10.0.12'
)

$ErrorActionPreference = 'Stop'
$root = $PSScriptRoot
$project = Join-Path $root 'src\TRPOLARIS.NFSU2.WebControlCenter\TRPOLARIS.NFSU2.WebControlCenter.csproj'

if (-not (Test-Path $project)) {
    throw "Project file not found: $project"
}
if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) {
    throw '.NET SDK was not found. Install the .NET 10 SDK first.'
}

Write-Host '[1/5] Detecting NuGet global package cache...' -ForegroundColor Cyan
$globalPackages = $env:NUGET_PACKAGES
if ([string]::IsNullOrWhiteSpace($globalPackages)) {
    $localOutput = (& dotnet nuget locals global-packages --list 2>&1 | Out-String)
    if ($localOutput -match '(?im)global-packages:\s*(.+)') {
        $globalPackages = $Matches[1].Trim()
    }
}
if ([string]::IsNullOrWhiteSpace($globalPackages)) {
    $globalPackages = Join-Path $HOME '.nuget\packages'
}
$globalPackages = [Environment]::ExpandEnvironmentVariables($globalPackages.Trim('"'))
if (-not (Test-Path $globalPackages)) {
    throw "NuGet global package cache not found: $globalPackages"
}
Write-Host "NuGet cache: $globalPackages"

Write-Host '[2/5] Removing only the damaged .NET 10.0.12 Windows runtime packs...' -ForegroundColor Cyan
$runtimePackages = @(
    'microsoft.netcore.app.runtime.win-x64',
    'microsoft.windowsdesktop.app.runtime.win-x64'
)
foreach ($package in $runtimePackages) {
    $packageVersionPath = Join-Path (Join-Path $globalPackages $package) $RuntimePackVersion
    if (Test-Path $packageVersionPath) {
        Remove-Item -LiteralPath $packageVersionPath -Recurse -Force
        Write-Host "Removed: $packageVersionPath" -ForegroundColor Yellow
    } else {
        Write-Host "Not present (will be restored): $packageVersionPath" -ForegroundColor DarkGray
    }
}

Write-Host '[3/5] Clearing NuGet HTTP cache and project build outputs...' -ForegroundColor Cyan
& dotnet nuget locals http-cache --clear
if ($LASTEXITCODE -ne 0) { throw "NuGet HTTP cache clear failed: $LASTEXITCODE" }
$projectDir = Split-Path -Parent $project
foreach ($folder in @('obj', 'bin')) {
    $path = Join-Path $projectDir $folder
    if (Test-Path $path) {
        Remove-Item -LiteralPath $path -Recurse -Force
    }
}

Write-Host '[4/5] Downloading the win-x64 runtime packs again...' -ForegroundColor Cyan
& dotnet restore $project -r win-x64 --force --no-cache
if ($LASTEXITCODE -ne 0) { throw "dotnet restore failed: $LASTEXITCODE" }

Write-Host '[5/5] Building the application...' -ForegroundColor Cyan
& dotnet build $project -c Release -r win-x64 --no-restore
if ($LASTEXITCODE -ne 0) { throw "dotnet build failed: $LASTEXITCODE" }

Write-Host ''
Write-Host 'Runtime-pack repair and Release build completed.' -ForegroundColor Green
Write-Host 'Next step: run .\Publish-SingleFile.ps1 to produce the single-file EXE.' -ForegroundColor Green
