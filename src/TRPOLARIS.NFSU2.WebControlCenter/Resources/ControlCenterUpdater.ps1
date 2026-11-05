[CmdletBinding()]
param(
    [Parameter(Mandatory=$true)][string]$TargetExe,
    [Parameter(Mandatory=$true)][string]$DownloadedExe,
    [Parameter(Mandatory=$true)][string]$ExpectedSha256,
    [Parameter(Mandatory=$true)][int]$ParentProcessId,
    [string]$BackupExe = ''
)
$ErrorActionPreference = 'Stop'
$log = Join-Path $env:LOCALAPPDATA 'TRPOLARIS\NFSU2-Web-Control-Center\logs\updater.log'
function Write-UpdateLog([string]$Message) {
    $dir = Split-Path -Parent $log
    if (-not (Test-Path $dir)) { New-Item -ItemType Directory -Path $dir -Force | Out-Null }
    Add-Content -LiteralPath $log -Value ("[{0}] {1}" -f (Get-Date -Format o), $Message) -Encoding UTF8
}
try {
    Write-UpdateLog 'Updater started.'
    $target = [IO.Path]::GetFullPath($TargetExe)
    $download = [IO.Path]::GetFullPath($DownloadedExe)
    if (-not (Test-Path -LiteralPath $download -PathType Leaf)) { throw 'Downloaded executable is missing.' }
    $actual = (Get-FileHash -LiteralPath $download -Algorithm SHA256).Hash.ToLowerInvariant()
    if ($actual -ne $ExpectedSha256.ToLowerInvariant()) { throw "SHA-256 mismatch. Expected $ExpectedSha256, received $actual." }

    try { Wait-Process -Id $ParentProcessId -Timeout 120 -ErrorAction SilentlyContinue } catch { }
    $deadline = (Get-Date).AddSeconds(120)
    while ((Get-Process -Id $ParentProcessId -ErrorAction SilentlyContinue) -and (Get-Date) -lt $deadline) { Start-Sleep -Milliseconds 500 }
    if (Get-Process -Id $ParentProcessId -ErrorAction SilentlyContinue) { throw 'Control Center did not exit in time; update was not applied.' }

    $dir = Split-Path -Parent $target
    if (-not (Test-Path -LiteralPath $dir)) { New-Item -ItemType Directory -Path $dir -Force | Out-Null }
    if ([string]::IsNullOrWhiteSpace($BackupExe)) { $BackupExe = "$target.previous" }
    if (Test-Path -LiteralPath $target) { Copy-Item -LiteralPath $target -Destination $BackupExe -Force }

    $staged = "$target.update-staged"
    Copy-Item -LiteralPath $download -Destination $staged -Force
    $stagedHash = (Get-FileHash -LiteralPath $staged -Algorithm SHA256).Hash.ToLowerInvariant()
    if ($stagedHash -ne $ExpectedSha256.ToLowerInvariant()) { throw 'Staged executable verification failed.' }
    Move-Item -LiteralPath $staged -Destination $target -Force
    Write-UpdateLog "Updated $target successfully. SHA256=$actual"
    Start-Process -FilePath $target -WorkingDirectory $dir
    exit 0
}
catch {
    Write-UpdateLog ("UPDATE FAILED: " + $_.Exception.Message)
    try {
        if ((Test-Path -LiteralPath $BackupExe) -and (Test-Path -LiteralPath $TargetExe)) {
            Copy-Item -LiteralPath $BackupExe -Destination $TargetExe -Force
            Write-UpdateLog 'Previous executable restored.'
        }
        if (Test-Path -LiteralPath $TargetExe) { Start-Process -FilePath $TargetExe -WorkingDirectory (Split-Path -Parent $TargetExe) }
    } catch { Write-UpdateLog ("Rollback failed: " + $_.Exception.Message) }
    exit 1
}
