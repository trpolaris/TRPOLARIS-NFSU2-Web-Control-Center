$ErrorActionPreference = 'Stop'
$project = Join-Path $PSScriptRoot 'src\TRPOLARIS.NFSU2.WebControlCenter\TRPOLARIS.NFSU2.WebControlCenter.csproj'
$output = Join-Path $PSScriptRoot 'publish'

if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) {
    throw 'dotnet SDK not found. Install the .NET 10 SDK and WPF desktop development workload in Visual Studio 2026.'
}

if (Test-Path $output) { Remove-Item $output -Recurse -Force }

dotnet publish $project -c Release -r win-x64 --self-contained true `
    -p:PublishSingleFile=true `
    -p:IncludeNativeLibrariesForSelfExtract=true `
    -p:EnableCompressionInSingleFile=true `
    -p:DebugType=None `
    -p:DebugSymbols=false `
    -o $output

if ($LASTEXITCODE -ne 0) { throw "Publish failed: exit code $LASTEXITCODE" }
Write-Host "Published EXE: $output\TRPOLARIS.NFSU2.WebControlCenter.exe" -ForegroundColor Green
