[CmdletBinding()]
param(
    [string]$RuntimePackVersion = '10.0.12'
)

$ErrorActionPreference = 'Stop'
$root = $PSScriptRoot
$project = Join-Path $root 'src\TRPOLARIS.NFSU2.WebControlCenter\TRPOLARIS.NFSU2.WebControlCenter.csproj'

if (-not (Test-Path $project)) { throw "Project file not found: $project" }
if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) { throw '.NET SDK was not found. Install the .NET 10 SDK first.' }

Write-Host '[1/4] Restoring win-x64 packages...' -ForegroundColor Cyan
& dotnet restore $project -r win-x64 --force
if ($LASTEXITCODE -ne 0) { throw "dotnet restore failed: $LASTEXITCODE" }

Write-Host '[2/4] Building Release...' -ForegroundColor Cyan
& dotnet build $project -c Release -r win-x64 --no-restore
if ($LASTEXITCODE -ne 0) { throw "dotnet build failed: $LASTEXITCODE" }

Write-Host '[3/4] Publishing the self-contained single-file EXE...' -ForegroundColor Cyan
& (Join-Path $root 'Publish-SingleFile.ps1')
if ($LASTEXITCODE -ne 0) { throw "Publish failed: $LASTEXITCODE" }

Write-Host '[4/4] Done.' -ForegroundColor Green
Write-Host "EXE: $(Join-Path $root 'publish\TRPOLARIS.NFSU2.WebControlCenter.exe')" -ForegroundColor Green
