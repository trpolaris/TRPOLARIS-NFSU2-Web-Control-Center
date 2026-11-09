#requires -Version 5.1
[CmdletBinding()]
param(
    [int]$Port = 0,
    [string]$GameDir = '',
    [string]$ControlRoot = ''
)

$ErrorActionPreference = 'Stop'
$ProgressPreference = 'SilentlyContinue'
$Utf8NoBom = New-Object System.Text.UTF8Encoding($false)
try {
    [Console]::OutputEncoding = $Utf8NoBom
    [Console]::InputEncoding = $Utf8NoBom
    $OutputEncoding = $Utf8NoBom
    $chcp = Join-Path $env:SystemRoot 'System32\chcp.com'
    if (Test-Path -LiteralPath $chcp) { & $chcp 65001 *> $null }
} catch { }

$WindowsDriveRoot = [IO.Path]::GetPathRoot($env:SystemRoot)
if ([string]::IsNullOrWhiteSpace($ControlRoot)) { $ControlRoot = Join-Path $WindowsDriveRoot 'nfsu2' }
$Root = [IO.Path]::GetFullPath($ControlRoot)
$RuntimeRoot = Join-Path $Root 'runtime'
$LogsRoot = Join-Path $Root 'logs'
$DriveLetter = $WindowsDriveRoot.TrimEnd('\', ':').ToLowerInvariant()
$RelativeRoot = $Root.Substring($WindowsDriveRoot.Length).Trim('\').Replace('\', '/')
$WslControlRoot = "/mnt/$DriveLetter/$RelativeRoot"
$WslRuntimeRoot = "$WslControlRoot/runtime"
$WslLogsRoot = "$WslControlRoot/logs"
$ConfigPath = Join-Path $Root 'nfsu2-config.json'
$DefaultPort = 9093
$RepoUrl = 'https://github.com/trpolaris/NFSU2-Web.git'
$SafeStopRequestPath = Join-Path $LogsRoot '.nfsu2-safe-stop.request'

function Test-InstallStopRequested {
    return (Test-Path -LiteralPath $SafeStopRequestPath -PathType Leaf)
}

function Assert-InstallNotStopped {
    if (Test-InstallStopRequested) {
        Write-Host '[STOPPED] Güvenli durdurma isteği alındı. Mevcut güvenli aşama tamamlandı; kurulum devam ettirilmeyecek.' -ForegroundColor Yellow
        exit 75
    }
}

function Write-InstallProgress([int]$Percent, [string]$Label, [string]$Quality = 'estimated') {
    $safeLabel = ($Label -replace '[\r\n|]', ' ').Trim()
    if ($Quality -notin @('estimated', 'measured', 'indeterminate')) { $Quality = 'estimated' }
    Write-Host ("[NFSU2-PROGRESS]|{0}|{1}|{2}" -f [Math]::Max(0, [Math]::Min(100, $Percent)), $Quality, $safeLabel)
}
Write-InstallProgress 2 'Ön kontroller ve ortam hazırlanıyor'


function Write-Section([string]$Message) {
    Assert-InstallNotStopped
    Write-Host "`n=== $Message ===" -ForegroundColor Cyan
}

function Test-IsAdministrator {
    $identity = [Security.Principal.WindowsIdentity]::GetCurrent()
    $principal = New-Object Security.Principal.WindowsPrincipal($identity)
    return $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
}

if (-not (Test-IsAdministrator)) {
    Write-Host 'Kurulum program, WSL ve Docker bileşenlerini hazırlayabilmek için yönetici izni istiyor.' -ForegroundColor Yellow
    $psExe = Join-Path $env:SystemRoot 'System32\WindowsPowerShell\v1.0\powershell.exe'
    $argsLine = '-NoProfile -ExecutionPolicy Bypass -File "{0}" -Port {1} -ControlRoot "{2}"' -f $PSCommandPath, $Port, ($Root -replace '"', '\"')
    if (-not [string]::IsNullOrWhiteSpace($GameDir)) { $argsLine += ' -GameDir "{0}"' -f ($GameDir -replace '"', '\"') }
    Start-Process -FilePath $psExe -Verb RunAs -ArgumentList $argsLine
    exit
}

New-Item -ItemType Directory -Force -Path $Root, (Join-Path $Root 'game'), (Join-Path $Root 'module'), $LogsRoot, $RuntimeRoot | Out-Null
$TranscriptPath = Join-Path $LogsRoot 'setup-powershell.log'
try { Start-Transcript -Path $TranscriptPath -Append | Out-Null } catch {}

try {
    Clear-Host
    Write-Host 'TRPOLARIS NFSU2 Web — Windows Tek Tık Kurulum' -ForegroundColor Cyan
    Write-Host 'Güncel GitHub kaynağı indirilir; oyun modülü üretilir, Docker imajı derlenir ve sunucu başlatılır.' -ForegroundColor Gray
    Write-Host 'Oyun arşivi Windows Control Center tarafından indirilir veya yerel ZIP arşivinden alınır. Bu betik oyun dosyası indirmez.' -ForegroundColor Yellow
    Write-Host "GitHub: $RepoUrl" -ForegroundColor Gray
    Write-Host "Kurulum alanı: $Root" -ForegroundColor Gray

    # GUI may provide a port/game path. When omitted, keep the original interactive flow.
    if ($Port -le 0) {
        do {
            $portText = Read-Host "Yerel oyun portu [varsayılan $DefaultPort] (Enter = varsayılan, farklı sayı = manuel)"
            if ([string]::IsNullOrWhiteSpace($portText)) { $Port = $DefaultPort }
            elseif ($portText -match '^\d+$') { $Port = [int]$portText }
            else { $Port = 0 }
            if ($Port -lt 1024 -or $Port -gt 65535 -or $Port -in @(80, 443)) {
                Write-Host 'Port 1024–65535 arasında olmalı; 80 ve 443 mevcut web sitelerini korumak için kullanılmıyor.' -ForegroundColor Yellow
                $Port = 0
            }
        } while ($Port -eq 0)
    } elseif ($Port -lt 1024 -or $Port -gt 65535 -or $Port -in @(80, 443)) {
        throw 'Port 1024–65535 arasında olmalı; 80 ve 443 kullanılmıyor.'
    }

    $DefaultGame = Join-Path $Root 'game'
    if ([string]::IsNullOrWhiteSpace($GameDir)) {
        $GameInput = Read-Host "Oyun klasörü (SPEED2.EXE bulunan klasör) [$DefaultGame] (Enter = varsayılan)"
        if ([string]::IsNullOrWhiteSpace($GameInput)) { $GameDir = $DefaultGame }
        else { $GameDir = [IO.Path]::GetFullPath($GameInput.Trim().Trim('"')) }
    } else {
        $GameDir = [IO.Path]::GetFullPath($GameDir.Trim().Trim('"'))
    }

    $GameExe = Join-Path $GameDir 'SPEED2.EXE'
    if (-not (Test-Path -LiteralPath $GameExe -PathType Leaf)) {
        throw "Oyun klasöründe SPEED2.EXE bulunamadı: $GameDir. WSL/Docker kurulumu başlatılmadı."
    }
    $GameExeInfo = Get-Item -LiteralPath $GameExe -ErrorAction Stop
    $ExpectedGameExeSize = 4800512
    $ExpectedGameExeSha256 = 'f9dd86c054878ce6276beb07c1fd61874f7a1e4bf1f241b084c65b73e24168a7'
    if ($GameExeInfo.Length -ne $ExpectedGameExeSize) {
        throw "Desteklenmeyen SPEED2.EXE boyutu: $($GameExeInfo.Length) bayt. Beklenen: $ExpectedGameExeSize. Kurulum başlatılmadı."
    }
    $ActualGameExeSha256 = (Get-FileHash -LiteralPath $GameExe -Algorithm SHA256 -ErrorAction Stop).Hash.ToLowerInvariant()
    if ($ActualGameExeSha256 -ne $ExpectedGameExeSha256) {
        throw 'SPEED2.EXE SHA-256 doğrulaması başarısız. Desteklenen oyun sürümü bulunamadı. Kurulum başlatılmadı.'
    }
    foreach ($RequiredGameFolder in @('CARS', 'FRONTEND', 'GLOBAL', 'TRACKS')) {
        $RequiredPath = Join-Path $GameDir $RequiredGameFolder
        if (-not (Test-Path -LiteralPath $RequiredPath -PathType Container)) {
            throw "Gerekli oyun klasörü bulunamadı: $RequiredPath. Kurulum başlatılmadı."
        }
        $AnyGameEntry = Get-ChildItem -LiteralPath $RequiredPath -Force -ErrorAction Stop | Select-Object -First 1
        if ($null -eq $AnyGameEntry) {
            throw "Gerekli oyun klasörü boş: $RequiredPath. Kurulum başlatılmadı."
        }
    }
    Write-Host "Oyun dosyaları doğrulandı: SPEED2.EXE SHA-256=$ActualGameExeSha256" -ForegroundColor Green
    $GameDir = (Resolve-Path -LiteralPath $GameDir).Path

    Write-Section 'Port ve klasör ayarları'
    Write-Host "Port: $Port"
    Write-Host "Oyun klasörü: $GameDir"
    Write-Host "Modül klasörü: $(Join-Path $Root 'module')"

    # Docker Desktop: install via winget if needed, then launch it.
    $DockerExeCandidates = @(
        (Join-Path $env:ProgramFiles 'Docker\Docker\Docker Desktop.exe'),
        (Join-Path $env:LOCALAPPDATA 'Programs\DockerDesktop\Docker Desktop.exe')
    )
    $DockerExe = $DockerExeCandidates | Where-Object { Test-Path $_ } | Select-Object -First 1
    if (-not $DockerExe) {
        $winget = Get-Command winget.exe -ErrorAction SilentlyContinue
        if (-not $winget) { throw 'Docker Desktop bulunamadı ve winget mevcut değil. Docker Desktop yükleyicisini https://www.docker.com/products/docker-desktop/ adresinden kurup scripti tekrar çalıştır.' }
        Write-Host 'Docker Desktop bulunamadı; winget üzerinden kuruluyor...' -ForegroundColor Yellow
        & winget.exe install --exact --id Docker.DockerDesktop --accept-source-agreements --accept-package-agreements
        if ($LASTEXITCODE -ne 0) { throw 'Docker Desktop kurulumu başarısız oldu. Kurulum çıktısını kontrol et ve scripti tekrar çalıştır.' }
        $DockerExe = $DockerExeCandidates | Where-Object { Test-Path $_ } | Select-Object -First 1
    }
    if (-not $DockerExe) { throw 'Docker Desktop kurulumdan sonra bulunamadı. Windows yeniden başlatması gerekiyorsa yeniden başlatıp scripti tekrar çalıştır.' }
    if (-not (Get-Process -Name 'Docker Desktop','com.docker.backend' -ErrorAction SilentlyContinue)) {
        Write-Host 'Docker Desktop başlatılıyor...' -ForegroundColor Cyan
        Start-Process -FilePath $DockerExe
    }

    # WSL/Ubuntu: enable/install if missing. Windows feature activation can require a reboot.
    function Read-CapturedNativeOutputLines {
        param([Parameter(Mandatory = $true)][string]$Path)
        try {
            if (-not (Test-Path -LiteralPath $Path)) { return @() }
            $bytes = [System.IO.File]::ReadAllBytes($Path)
            if ($bytes.Length -eq 0) { return @() }
            if ($bytes.Length -ge 2 -and $bytes[0] -eq 0xFF -and $bytes[1] -eq 0xFE) {
                $text = [System.Text.Encoding]::Unicode.GetString($bytes, 2, $bytes.Length - 2)
            } elseif ($bytes.Length -ge 2 -and $bytes[0] -eq 0xFE -and $bytes[1] -eq 0xFF) {
                $text = [System.Text.Encoding]::BigEndianUnicode.GetString($bytes, 2, $bytes.Length - 2)
            } elseif ($bytes.Length -ge 3 -and $bytes[0] -eq 0xEF -and $bytes[1] -eq 0xBB -and $bytes[2] -eq 0xBF) {
                $text = [System.Text.Encoding]::UTF8.GetString($bytes, 3, $bytes.Length - 3)
            } else {
                $sampleLength = [Math]::Min($bytes.Length, 1024)
                $nullBytes = 0
                for ($j = 0; $j -lt $sampleLength; $j++) { if ($bytes[$j] -eq 0) { $nullBytes++ } }
                if ($nullBytes -gt ($sampleLength / 8)) {
                    $text = [System.Text.Encoding]::Unicode.GetString($bytes)
                } else {
                    $text = [System.Text.Encoding]::UTF8.GetString($bytes)
                }
            }
            $text = $text.TrimStart([char]0xFEFF)
            return @($text -split "`r?`n")
        } catch { return @() }
    }

    function Get-RegisteredWslDistros {
        $registered = @()
        try {
            $listOutput = @(& wsl.exe --list --quiet 2>$null)
            $registered += @($listOutput | ForEach-Object {
                $name = ([string]$_) -replace "`0", ''
                $name = $name -replace '[\x00-\x1F]', ''
                $name.Trim()
            } | Where-Object { -not [string]::IsNullOrWhiteSpace($_) -and $_ -notmatch '^(Windows Subsystem for Linux|WSL_E_)' })
        } catch { }
        # Always merge the per-user registry list. Some WSL versions return partial/oddly encoded output.
        try {
            $lxss = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Lxss'
            if (Test-Path -LiteralPath $lxss) {
                foreach ($key in @(Get-ChildItem -LiteralPath $lxss -ErrorAction SilentlyContinue)) {
                    try {
                        $props = Get-ItemProperty -LiteralPath $key.PSPath -ErrorAction Stop
                        $name = [string]$props.DistributionName
                        if (-not [string]::IsNullOrWhiteSpace($name)) { $registered += $name.Trim() }
                    } catch { }
                }
            }
        } catch { }
        return @($registered | Select-Object -Unique)
    }

    function Get-WslDistroDefaultUid {
        param([Parameter(Mandatory = $true)][string]$Name)
        $registryPath = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Lxss'
        try {
            if (Test-Path -LiteralPath $registryPath) {
                foreach ($key in @(Get-ChildItem -LiteralPath $registryPath -ErrorAction SilentlyContinue)) {
                    try {
                        $properties = Get-ItemProperty -LiteralPath $key.PSPath -ErrorAction Stop
                        if ([string]$properties.DistributionName -eq $Name) {
                            if ($null -ne $properties.DefaultUid) { return [uint32]$properties.DefaultUid }
                            return $null
                        }
                    } catch { }
                }
            }
        } catch { }
        return $null
    }

    function Test-WslDistroLaunchable {
        param([Parameter(Mandatory = $true)][string]$Name)
        # An uninitialized Ubuntu has no DefaultUid yet. Never probe it with -u root:
        # that silently initializes root and suppresses the interactive first-run user wizard.
        $defaultUid = Get-WslDistroDefaultUid -Name $Name
        if ($null -eq $defaultUid -or $defaultUid -eq 0) { return $true }
        $lastMessage = ''
        for ($probeAttempt = 1; $probeAttempt -le 3; $probeAttempt++) {
            $probeOutput = @(& wsl.exe -d $Name -- /bin/true 2>&1)
            $probeCode = $LASTEXITCODE
            if ($probeCode -eq 0) { return $true }
            $lastMessage = ($probeOutput -join "`n").Trim()
            if ($lastMessage -match 'WSL_E_DISTRO_NOT_FOUND|There is no distribution with the supplied name') {
                Write-Host ("[WSL/UBUNTU] Eski/boş WSL kaydı doğrulanamadı: " + $Name + '. Bu kayıt kurulu dağıtım olarak kullanılmayacak.') -ForegroundColor Yellow
                return $false
            }
            if ($probeAttempt -lt 3) { Start-Sleep -Seconds 2 }
        }
        throw ("WSL dağıtımı kayıtlı görünüyor ancak varsayılan Linux kullanıcısı başlatılamadı: " + $Name + '. Çıkış kodu: ' + $probeCode + '. ' + $lastMessage)
    }

    if (-not (Get-Command wsl.exe -ErrorAction SilentlyContinue)) {
        Write-Host 'WSL bileşeni bulunamadı; Windows özellikleri etkinleştiriliyor...' -ForegroundColor Yellow
        Enable-WindowsOptionalFeature -Online -FeatureName Microsoft-Windows-Subsystem-Linux -All -NoRestart | Out-Null
        Enable-WindowsOptionalFeature -Online -FeatureName VirtualMachinePlatform -All -NoRestart | Out-Null
        throw 'WSL Windows özellikleri etkinleştirildi. Windows sistemini yeniden başlatıp scripti tekrar çalıştır; gerekirse wsl --install -d Ubuntu ilk kurulumunu tamamla.'
    }
    $Distros = @(Get-RegisteredWslDistros)
    $Distro = $Distros | Where-Object { $_ -match '^Ubuntu($|-|\s)' } | Select-Object -First 1
    if ($Distro -and -not (Test-WslDistroLaunchable -Name $Distro)) { $Distro = $null }
    if (-not $Distro) {
        Write-Host '[WSL/UBUNTU] Ubuntu dağıtımı bulunamadı; indirme ve WSL kayıt aşaması başlatılıyor.' -ForegroundColor Yellow
        Write-Host '[WSL/UBUNTU] Adım 1/4: wsl.exe --install --no-launch -d Ubuntu başlatılıyor.' -ForegroundColor Cyan
        Write-Host '[WSL/UBUNTU] Bu adım internet hızına göre birkaç dakika sürebilir. İlerleme arada bir canlı günlükte gösterilecek.' -ForegroundColor Cyan
        Write-Host '[WSL/UBUNTU] İlk açılışta Linux kullanıcı adı/parola kurulumu ayrıca istenebilir.' -ForegroundColor Cyan

        $wslOutFile = Join-Path $env:TEMP ('trpolaris-wsl-install-' + [guid]::NewGuid().ToString('N') + '.out')
        $wslErrFile = Join-Path $env:TEMP ('trpolaris-wsl-install-' + [guid]::NewGuid().ToString('N') + '.err')
        $wslInstallCode = $null
        try {
            $wslInstallProcess = Start-Process -FilePath (Join-Path $env:SystemRoot 'System32\wsl.exe') -ArgumentList @('--install', '--no-launch', '-d', 'Ubuntu') -PassThru -WindowStyle Hidden -RedirectStandardOutput $wslOutFile -RedirectStandardError $wslErrFile
            $stdoutSeen = 0
            $stderrSeen = 0
            $lastHeartbeat = Get-Date
            while ($true) {
                foreach ($streamPath in @($wslOutFile, $wslErrFile)) {
                    $streamName = if ([string]::Equals($streamPath, $wslOutFile, [StringComparison]::OrdinalIgnoreCase)) { 'OUT' } else { 'ERR' }
                    try { $streamLines = @(Read-CapturedNativeOutputLines -Path $streamPath) } catch { $streamLines = @() }
                    if ($streamName -eq 'OUT') { $seen = $stdoutSeen } else { $seen = $stderrSeen }
                    if ($streamLines.Count -gt $seen) {
                        for ($i = $seen; $i -lt $streamLines.Count; $i++) {
                            $entry = ([string]$streamLines[$i]) -replace "`0", ''
                            $entry = $entry -replace '[\x00-\x08\x0B\x0C\x0E-\x1F]', ''
                            if (-not [string]::IsNullOrWhiteSpace($entry)) { Write-Host ("[WSL/UBUNTU][$streamName] " + $entry.Trim()) }
                        }
                        if ($streamName -eq 'OUT') { $stdoutSeen = $streamLines.Count } else { $stderrSeen = $streamLines.Count }
                    }
                }
                $wslInstallProcess.Refresh()
                if ($wslInstallProcess.HasExited) { break }
                if (((Get-Date) - $lastHeartbeat).TotalSeconds -ge 12) {
                    $wslElapsed = [int]((Get-Date) - $wslInstallProcess.StartTime).TotalSeconds
                    Write-Host ("[WSL/UBUNTU] Ubuntu indirme/kayıt işlemi devam ediyor... geçen süre: " + $wslElapsed + ' sn. WSL kesin yüzde sağlamıyor.') -ForegroundColor Cyan
                    Write-InstallProgress 4 ("Ubuntu indiriliyor/kaydediliyor · " + $wslElapsed + " sn · kesin yüzde mevcut değil") 'indeterminate'
                    $lastHeartbeat = Get-Date
                }
                Start-Sleep -Seconds 2
            }
            try { $wslInstallProcess.WaitForExit() } catch { }
            $wslInstallProcess.Refresh()
            # Some WSL/MSIX install paths complete successfully but leave Process.ExitCode null.
            try {
                $rawExitCode = $wslInstallProcess.ExitCode
                if ($null -ne $rawExitCode -and -not [string]::IsNullOrWhiteSpace([string]$rawExitCode)) { $wslInstallCode = [int]$rawExitCode }
            } catch { $wslInstallCode = $null }
            foreach ($streamPath in @($wslOutFile, $wslErrFile)) {
                $streamName = if ([string]::Equals($streamPath, $wslOutFile, [StringComparison]::OrdinalIgnoreCase)) { 'OUT' } else { 'ERR' }
                try { $streamLines = @(Read-CapturedNativeOutputLines -Path $streamPath) } catch { $streamLines = @() }
                if ($streamName -eq 'OUT') { $seen = $stdoutSeen } else { $seen = $stderrSeen }
                for ($i = $seen; $i -lt $streamLines.Count; $i++) {
                    $entry = ([string]$streamLines[$i]) -replace "`0", ''
                    $entry = $entry -replace '[\x00-\x08\x0B\x0C\x0E-\x1F]', ''
                    if (-not [string]::IsNullOrWhiteSpace($entry)) { Write-Host ("[WSL/UBUNTU][$streamName] " + $entry.Trim()) }
                }
            }
        }
        finally {
            Remove-Item -LiteralPath $wslOutFile, $wslErrFile -Force -ErrorAction SilentlyContinue
        }

        Assert-InstallNotStopped
        $exitText = if ($null -eq $wslInstallCode) { 'alınamadı (WSL kurulum işlemi bazen boş çıkış kodu döndürür)' } else { [string]$wslInstallCode }
        Write-Host ("[WSL/UBUNTU] Adım 2/4: Ubuntu indirme/kurulum işlemi çıkış kodu: " + $exitText) -ForegroundColor Cyan
        Write-Host '[WSL/UBUNTU] Adım 3/4: Ubuntu kaydı wsl.exe ve kullanıcı kayıt defteri üzerinden doğrulanıyor.' -ForegroundColor Cyan
        $Distro = $null
        $verifyDeadline = (Get-Date).AddSeconds(45)
        do {
            $Distros = @(Get-RegisteredWslDistros)
            $Distro = $Distros | Where-Object { $_ -match '^Ubuntu($|-|\s)' } | Select-Object -First 1
            if ($Distro) { break }
            Start-Sleep -Seconds 3
        } while ((Get-Date) -lt $verifyDeadline)

        # Registration is the deciding signal: the install command may report an empty/non-zero exit
        # even though WSL has already installed Ubuntu. Never fail a completed installation just due to null ExitCode.
        if (-not $Distro) {
            if ($null -eq $wslInstallCode) {
                throw 'WSL kurulum komutu çıkış kodu döndürmedi ve Ubuntu dağıtımı WSL/kayıt defterinde doğrulanamadı. wsl --list --verbose çıktısını kontrol et; gerekirse Windows''u yeniden başlatıp tekrar dene.'
            }
            if ($wslInstallCode -ne 0) {
                throw "WSL/Ubuntu kurulum komutu $wslInstallCode koduyla tamamlandı ve Ubuntu kaydı doğrulanamadı. Yukarıdaki WSL/UBUNTU günlüklerini kontrol et; gerekirse Windows''u yeniden başlatıp tekrar dene."
            }
            throw 'Ubuntu kurulum komutu tamamlandı ancak dağıtım WSL listesinde görünmüyor. wsl --list --verbose çıktısını kontrol et; gerekirse Windows''u yeniden başlatıp tekrar dene.'
        }
        if ($null -eq $wslInstallCode) {
            Write-Host ("[WSL/UBUNTU] Uyarı: işlem çıkış kodu boş döndü; ancak Ubuntu dağıtımı kayıtlı olarak doğrulandı: " + $Distro + '. Kurulum devam ediyor.') -ForegroundColor Yellow
        } elseif ($wslInstallCode -ne 0) {
            Write-Host ("[WSL/UBUNTU] Uyarı: wsl.exe çıkış kodu " + $wslInstallCode + ' döndürdü ancak Ubuntu kayıtlı; kurulum doğrulandığı için devam ediliyor.') -ForegroundColor Yellow
        }
        Write-Host ('[WSL/UBUNTU] Adım 4/4: Ubuntu dağıtımı başarıyla kayıt edildi: ' + $Distro) -ForegroundColor Green
    }

    # DefaultUid is absent until Ubuntu's first-run user setup has completed.
    $DistroDefaultUid = Get-WslDistroDefaultUid -Name $Distro
    $existingRootDefault = ($DistroDefaultUid -eq 0)
    if ($null -eq $DistroDefaultUid -or $DistroDefaultUid -eq 0) {
        Write-Host ''
        if ($null -eq $DistroDefaultUid) {
            Write-Host 'Ubuntu ilk açılış ayarı gerekiyor.' -ForegroundColor Yellow
            Write-Host 'Yeni bir Ubuntu terminali açılacak. Linux kullanıcı adı ve parolanı bu terminalde oluştur.' -ForegroundColor Cyan
            Write-Host 'Kullanıcı oluşturma tamamlanıp kabuk açılınca terminale exit yazıp Enter tuşuna bas. Kontrol Merkezi bu pencereyi bekleyecek.' -ForegroundColor Cyan
            $firstRunArgs = @('-d', $Distro)
        } else {
            Write-Host 'Ubuntu kayıtlı ancak varsayılan kullanıcı root. Açılacak Ubuntu terminalinde standart bir kullanıcı oluşturman gerekiyor.' -ForegroundColor Yellow
            Write-Host 'Root kabuğu açılırsa şu komutları kendi kullanıcı adınla çalıştır:' -ForegroundColor Cyan
            Write-Host '  adduser KULLANICI_ADI' -ForegroundColor Yellow
            Write-Host '  usermod -aG sudo KULLANICI_ADI' -ForegroundColor Yellow
            Write-Host '  printf "[user]\ndefault=KULLANICI_ADI\n" > /etc/wsl.conf' -ForegroundColor Yellow
            Write-Host 'Sonra exit yaz. Varsayılan kullanıcı doğrulanmadan kurulum devam etmeyecek.' -ForegroundColor Cyan
        }
        # No -u root here: an uninitialized Ubuntu must be allowed to display its first-run username/password wizard.
        $firstRunArgs = @('-d', $Distro)
        $wslExe = Join-Path $env:SystemRoot 'System32\\wsl.exe'
        $firstRunProcess = Start-Process -FilePath $wslExe -ArgumentList $firstRunArgs -PassThru -WindowStyle Normal
        $firstRunDeadline = (Get-Date).AddMinutes(20)
        $lastFirstRunNotice = Get-Date
        $DistroDefaultUid = $null
        $validatedUser = $false
        while ((Get-Date) -lt $firstRunDeadline) {
            Assert-InstallNotStopped
            Start-Sleep -Seconds 2
            $uidNow = Get-WslDistroDefaultUid -Name $Distro
            if ($existingRootDefault -and ($null -eq $uidNow -or $uidNow -eq 0) -and $firstRunProcess -and $firstRunProcess.HasExited) {
                # Only an already-initialized root-default distro is probed here. Never re-launch an uninitialized distro
                # from this hidden PowerShell process after the interactive first-run window has closed.
                # /etc/wsl.conf can set the default user without updating the legacy registry DefaultUid value.
                $runtimeUidLines = @(& wsl.exe -d $Distro -- id -u 2>$null)
                $runtimeUidText = [string]($runtimeUidLines | Select-Object -First 1)
                $runtimeUidText = $runtimeUidText.Trim()
                $runtimeUid = 0
                if ([int]::TryParse($runtimeUidText, [ref]$runtimeUid) -and $runtimeUid -gt 0) { $uidNow = [uint32]$runtimeUid }
            }
            if ($null -ne $uidNow -and $uidNow -gt 0) {
                # Do not accept a registry value alone: confirm that the account and HOME exist inside Linux.
                $userInfoLines = @(& wsl.exe -d $Distro -u root -- getent passwd ([string]$uidNow) 2>$null)
                $userInfoLine = $userInfoLines | Where-Object { ([string]$_).Trim() -match '^[^:]+:[^:]*:[0-9]+:[0-9]+:.*:/[^:]*:' } | Select-Object -First 1
                if (-not [string]::IsNullOrWhiteSpace([string]$userInfoLine)) {
                    $userParts = ([string]$userInfoLine).Trim().Split(':')
                    $homePath = if ($userParts.Count -ge 6) { $userParts[5] } else { '' }
                    if (-not [string]::IsNullOrWhiteSpace($homePath) -and $homePath -ne '/root') {
                        $homeCheck = @(& wsl.exe -d $Distro -u root -- test -d $homePath 2>$null)
                        if ($LASTEXITCODE -eq 0) {
                            $DistroDefaultUid = $uidNow
                            $validatedUser = $true
                            break
                        }
                    }
                }
            }
            if (((Get-Date) - $lastFirstRunNotice).TotalSeconds -ge 15) {
                Write-Host 'Ubuntu Linux kullanıcı hesabının oluşturulması ve ev klasörünün doğrulanması bekleniyor...' -ForegroundColor Cyan
                $lastFirstRunNotice = Get-Date
            }
            try {
                if ($firstRunProcess -and $firstRunProcess.HasExited -and $null -eq (Get-WslDistroDefaultUid -Name $Distro)) {
                    # The launcher may exit before the user has completed first-run setup; continue polling.
                }
            } catch { }
        }
        if (-not $validatedUser -or $null -eq $DistroDefaultUid -or $DistroDefaultUid -eq 0) {
            throw 'Ubuntu Linux kullanıcısı doğrulanamadı. İlk açılış terminalinde kullanıcı oluşturmayı tamamla. Standart kullanıcı bulunmadan root ile kuruluma devam edilmedi.'
        }
        # If the interactive shell is still open, ask the user to exit normally. Never force-close it.
        try {
            if ($firstRunProcess -and -not $firstRunProcess.HasExited) {
                Write-Host 'Linux kullanıcı adı, UID ve HOME doğrulandı. Devam etmek için Ubuntu terminalinde exit yazıp Enter tuşuna bas.' -ForegroundColor Green
                $shellExitDeadline = (Get-Date).AddMinutes(3)
                while (-not $firstRunProcess.HasExited -and (Get-Date) -lt $shellExitDeadline) {
                    Assert-InstallNotStopped
                    Start-Sleep -Seconds 1
                    $firstRunProcess.Refresh()
                }
                if (-not $firstRunProcess.HasExited) {
                    Write-Host 'Ubuntu kabuğu açık kalıyor; kullanıcı doğrulandığı için kurulum devam ediyor. Terminali açık bırakabilirsin.' -ForegroundColor Yellow
                }
            }
        } catch { }
        Write-Host 'Ubuntu ilk açılış kurulumu ve normal kullanıcı doğrulaması tamamlandı. NFSU2 kurulumu otomatik devam ediyor...' -ForegroundColor Green
    }

    if ($null -eq $DistroDefaultUid -or $DistroDefaultUid -eq 0) {
        throw 'WSL_DEFAULT_USER_REQUIRED: Root hesabı yerine standart Ubuntu kullanıcısı gerekli.'
    }

    Write-Host '[DOCKER/WSL] Ubuntu dağıtımı Docker Desktop WSL Integration için hazırlanıyor...' -ForegroundColor Cyan
    $DistroWslVersion = 0
    $CurrentDefaultDistro = ''
    try {
        $wslVerbose = @(& wsl.exe --list --verbose 2>$null)
        foreach ($rawLine in $wslVerbose) {
            $line = ([string]$rawLine) -replace "`0", ''
            $line = $line -replace "`r|`n", ''
            if ($line -match '^\s*(?<Default>\*)?\s*(?<Name>.+?)\s{2,}\S+\s+(?<Version>[12])\s*$') {
                $rowName = $Matches.Name.Trim()
                if ($Matches.Default -eq '*') { $CurrentDefaultDistro = $rowName }
                if ([string]::Equals($rowName, $Distro, [StringComparison]::OrdinalIgnoreCase)) {
                    $DistroWslVersion = [int]$Matches.Version
                }
            }
        }
    } catch { }

    if ($DistroWslVersion -eq 0) {
        try {
            $lxssRoot = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Lxss'
            foreach ($lxssKey in @(Get-ChildItem -LiteralPath $lxssRoot -ErrorAction SilentlyContinue)) {
                $lxssProps = Get-ItemProperty -LiteralPath $lxssKey.PSPath -ErrorAction SilentlyContinue
                if ([string]::Equals([string]$lxssProps.DistributionName, $Distro, [StringComparison]::OrdinalIgnoreCase) -and $null -ne $lxssProps.Version) {
                    $DistroWslVersion = [int]$lxssProps.Version
                    break
                }
            }
        } catch { }
    }

    if ($DistroWslVersion -eq 1) {
        Write-Host "[DOCKER/WSL] $Distro WSL 1 üzerinde. WSL 2'ye dönüştürülüyor..." -ForegroundColor Yellow
        & wsl.exe --set-version $Distro 2
        if ($LASTEXITCODE -ne 0) { throw "Ubuntu dağıtımı WSL 2'ye dönüştürülemedi: $Distro. Docker Desktop WSL Integration etkinleştirilemez." }
        $DistroWslVersion = 2
    }
    if ($DistroWslVersion -eq 0) {
        Write-Host '[DOCKER/WSL] WSL dağıtım sürümü otomatik okunamadı; Ubuntu komut satırı erişimiyle doğrulama yapılacak.' -ForegroundColor Yellow
    }

    $DefaultDistroChanged = -not [string]::Equals($CurrentDefaultDistro, $Distro, [StringComparison]::OrdinalIgnoreCase)
    if ($DefaultDistroChanged) {
        & wsl.exe --set-default $Distro
        if ($LASTEXITCODE -ne 0) { throw "Ubuntu varsayılan WSL dağıtımı yapılamadı: $Distro." }
        Write-Host "[DOCKER/WSL] Varsayılan WSL dağıtımı Ubuntu olarak ayarlandı: $Distro" -ForegroundColor Green
    } else {
        Write-Host "[DOCKER/WSL] Ubuntu zaten varsayılan WSL dağıtımı: $Distro" -ForegroundColor Green
    }

    $DockerSettingsCandidates = @(
        (Join-Path $env:APPDATA 'Docker\settings-store.json'),
        (Join-Path $env:APPDATA 'Docker\settings.json')
    )
    $DockerSettingsPath = $DockerSettingsCandidates | Where-Object { Test-Path -LiteralPath $_ } | Select-Object -First 1
    $DockerSettingsChanged = $false
    $settingsOriginal = $null
    $settingsUpdated = $null
    if ($DockerSettingsPath) {
        try {
            $settingsOriginal = [System.IO.File]::ReadAllText($DockerSettingsPath, [System.Text.Encoding]::UTF8)
            $settingsObject = $settingsOriginal | ConvertFrom-Json -ErrorAction Stop
            if ($null -eq $settingsObject) { throw 'Docker Desktop settings JSON boş.' }

            $integratedDistros = @()
            $integrationProperty = $settingsObject.PSObject.Properties['integratedWslDistros']
            if ($null -ne $integrationProperty -and $null -ne $integrationProperty.Value) {
                $integratedDistros = @($integrationProperty.Value | ForEach-Object { [string]$_ } | Where-Object { -not [string]::IsNullOrWhiteSpace($_) })
            }
            $hasDistro = [bool]($integratedDistros | Where-Object { [string]::Equals($_, $Distro, [StringComparison]::OrdinalIgnoreCase) })
            $defaultIntegrationProperty = $settingsObject.PSObject.Properties['enableIntegrationWithDefaultWslDistro']
            $wslEngineProperty = $settingsObject.PSObject.Properties['wslEngineEnabled']
            $defaultIntegrationEnabled = $null -ne $defaultIntegrationProperty -and [bool]$defaultIntegrationProperty.Value
            $wslEngineEnabled = $null -ne $wslEngineProperty -and [bool]$wslEngineProperty.Value
            $DockerSettingsChanged = (-not $hasDistro) -or (-not $defaultIntegrationEnabled) -or (-not $wslEngineEnabled)

            if (-not $hasDistro) { $integratedDistros += $Distro }
            $integratedDistros = @($integratedDistros | Select-Object -Unique)
            $settingsObject | Add-Member -MemberType NoteProperty -Name 'integratedWslDistros' -Value $integratedDistros -Force
            $settingsObject | Add-Member -MemberType NoteProperty -Name 'enableIntegrationWithDefaultWslDistro' -Value $true -Force
            $settingsObject | Add-Member -MemberType NoteProperty -Name 'wslEngineEnabled' -Value $true -Force
            $settingsUpdated = ConvertTo-Json -InputObject $settingsObject -Depth 100
        } catch {
            throw "Docker Desktop WSL Integration ayarları okunamadı veya hazırlanamadı: $($_.Exception.Message)"
        }
    } else {
        throw 'Docker Desktop settings-store.json bulunamadı. Docker Desktop ilk açılışını tamamlayıp ayar dosyasını oluşturduktan sonra kurulumu yeniden çalıştır. WSL Integration seçimi doğrulanmadan kuruluma devam edilmeyecek.'
    }

    $NeedsDockerApplyRestart = $DefaultDistroChanged -or $DockerSettingsChanged
    $DockerCliCandidates = @(
        (Join-Path (Split-Path -Parent $DockerExe) 'DockerCli.exe'),
        (Join-Path (Split-Path -Parent $DockerExe) 'resources\bin\DockerCli.exe')
    )
    $DockerCliExe = $DockerCliCandidates | Where-Object { Test-Path -LiteralPath $_ } | Select-Object -First 1
    $DockerWasRunning = [bool](Get-Process -Name 'Docker Desktop','com.docker.backend' -ErrorAction SilentlyContinue)

    if ($NeedsDockerApplyRestart -and $DockerWasRunning) {
        Write-Host '[DOCKER/WSL] Değişiklikler Apply ediliyor: Docker Desktop güvenli biçimde kapatılıp yeniden başlatılacak...' -ForegroundColor Cyan
        if ($DockerCliExe) {
            & $DockerCliExe -Shutdown 2>&1 | ForEach-Object { if (-not [string]::IsNullOrWhiteSpace([string]$_)) { Write-Host ('[DOCKER/WSL] ' + [string]$_) } }
        } else {
            foreach ($desktopProcess in @(Get-Process -Name 'Docker Desktop' -ErrorAction SilentlyContinue)) {
                try { [void]$desktopProcess.CloseMainWindow() } catch { }
            }
        }
        $shutdownDeadline = (Get-Date).AddSeconds(90)
        do {
            Start-Sleep -Seconds 2
            $desktopStillRunning = [bool](Get-Process -Name 'Docker Desktop','com.docker.backend' -ErrorAction SilentlyContinue)
        } while ($desktopStillRunning -and (Get-Date) -lt $shutdownDeadline)
        if ($desktopStillRunning) {
            foreach ($desktopProcess in @(Get-Process -Name 'Docker Desktop' -ErrorAction SilentlyContinue)) {
                try { [void]$desktopProcess.CloseMainWindow() } catch { }
            }
            $closeDeadline = (Get-Date).AddSeconds(30)
            do {
                Start-Sleep -Seconds 2
                $desktopStillRunning = [bool](Get-Process -Name 'Docker Desktop','com.docker.backend' -ErrorAction SilentlyContinue)
            } while ($desktopStillRunning -and (Get-Date) -lt $closeDeadline)
        }
        if ($desktopStillRunning) {
            throw 'Docker Desktop değişiklikleri Apply etmek için kapatılamadı. Diğer containerlar etkilenmesin diye zorla sonlandırılmadı; Docker Desktop penceresini kapatıp kurulumu yeniden çalıştır.'
        }
    }

    # Apply the settings only after Docker Desktop has exited, so its in-memory state cannot overwrite them on shutdown.
    if ($DockerSettingsChanged -and $DockerSettingsPath) {
        try {
            $settingsBackup = $DockerSettingsPath + '.trpolaris-backup-' + (Get-Date -Format 'yyyyMMdd-HHmmss')
            Copy-Item -LiteralPath $DockerSettingsPath -Destination $settingsBackup -Force
            $settingsTemp = $DockerSettingsPath + '.trpolaris.tmp'
            [System.IO.File]::WriteAllText($settingsTemp, $settingsUpdated, [System.Text.UTF8Encoding]::new($false))
            Move-Item -LiteralPath $settingsTemp -Destination $DockerSettingsPath -Force
            Write-Host "[DOCKER/WSL] Docker settings güncellendi; orijinal dosya yedeklendi: $settingsBackup" -ForegroundColor Green
        } catch {
            throw "Docker Desktop WSL Integration ayarları yazılamadı: $($_.Exception.Message)"
        }
    } elseif ($DockerSettingsPath) {
        Write-Host '[DOCKER/WSL] Docker Desktop WSL Integration ayarları zaten doğru.' -ForegroundColor Green
    }

    if (-not (Get-Process -Name 'Docker Desktop','com.docker.backend' -ErrorAction SilentlyContinue)) {
        Write-Host '[DOCKER/WSL] Docker Desktop başlatılıyor...' -ForegroundColor Cyan
        Start-Process -FilePath $DockerExe
    }

    Write-Host "[DOCKER/WSL] Ubuntu entegrasyonu bekleniyor ve gerçek Docker Engine bağlantısı test ediliyor: $Distro" -ForegroundColor Cyan
    $DockerWslReady = $false
    $DockerOstype = ''
    for ($dockerAttempt = 1; $dockerAttempt -le 90; $dockerAttempt++) {
        Assert-InstallNotStopped
        $dockerProbe = @(& wsl.exe -d $Distro -u root -- docker info --format '{{.OSType}}' 2>$null)
        $dockerProbeCode = $LASTEXITCODE
        $DockerOstype = ([string]($dockerProbe | Select-Object -First 1)).Trim()
        if ($dockerProbeCode -eq 0 -and $DockerOstype -eq 'linux') {
            $DockerWslReady = $true
            break
        }
        if ($dockerProbeCode -eq 0 -and $DockerOstype -eq 'windows' -and $DockerCliExe) {
            Write-Host '[DOCKER/WSL] Docker Windows container modunda. Linux engine moduna geçiş isteniyor...' -ForegroundColor Yellow
            & $DockerCliExe -SwitchLinuxEngine 2>&1 | ForEach-Object { if (-not [string]::IsNullOrWhiteSpace([string]$_)) { Write-Host ('[DOCKER/WSL] ' + [string]$_) } }
            Start-Sleep -Seconds 5
        }
        if (($dockerAttempt % 10) -eq 0) {
            Write-Host "[DOCKER/WSL] Ubuntu Docker entegrasyonu bekleniyor... ($($dockerAttempt * 2)/180 sn)" -ForegroundColor Cyan
        }
        Start-Sleep -Seconds 2
    }
    if (-not $DockerWslReady) {
        throw "Docker Desktop Ubuntu WSL Integration doğrulanamadı. Ayar dosyası ve varsayılan dağıtım güncellendi; Docker Engine yanıtı: '$DockerOstype'. Docker Desktop > Settings > Resources > WSL Integration bölümünde '$Distro' seçili olmalı."
    }
    Write-Host "[DOCKER/WSL] BAŞARILI: $Distro için WSL Integration etkin, Apply/Restart tamam ve Linux Docker Engine erişilebilir." -ForegroundColor Green

    Write-InstallProgress 18 'WSL, Ubuntu ve Docker WSL Integration hazır'

    # Make the WSL-side helper. The selected game path is passed base64 encoded so spaces and Turkish chars survive.
    $BashScript = @'
#!/usr/bin/env bash
set -Eeuo pipefail
export LANG=C.UTF-8 LC_ALL=C.UTF-8

PORT="${1:?port missing}"
GAME_WIN_B64="${2:?game path missing}"
GAME_WIN="$(printf '%s' "$GAME_WIN_B64" | base64 -d)"
INSTALL_USER="${3:-${NFSU2_INSTALL_USER:-}}"
INSTALL_USER_HOME="${NFSU2_INSTALL_HOME:-}"
if [[ -z "$INSTALL_USER_HOME" && -n "$INSTALL_USER" ]]; then
  INSTALL_USER_HOME="$(getent passwd "$INSTALL_USER" | cut -d: -f6 || true)"
fi
if [[ -z "$INSTALL_USER_HOME" ]]; then INSTALL_USER_HOME="${HOME:-/root}"; fi
# This process runs as root for unattended apt/Docker operations. Keep HOME aligned
# with euid so Rust/Cargo and pip do not write into the regular user's cache directory.
if [[ "$(id -u)" == 0 ]]; then
  export HOME=/root
  export CARGO_HOME=/root/.cargo
  export RUSTUP_HOME=/root/.rustup
  export PATH="$CARGO_HOME/bin:$PATH"
fi
REPO_URL='https://github.com/trpolaris/NFSU2-Web.git'
WIN_ROOT='C:\nfsu2'
ROOT='/mnt/c/nfsu2'
MODULE="$ROOT/module"
CONFIG_FILE="${NFSU2_CONFIG_FILE:-$ROOT/nfsu2-config.json}"
LOG_DIR="$ROOT/logs"
mkdir -p "$LOG_DIR"
LOG="$LOG_DIR/nfsu2-one-click-wsl-$(date +%Y%m%d-%H%M%S).log"
STOP_REQUEST_FILE="${NFSU2_STOP_REQUEST_FILE:-$LOG_DIR/.nfsu2-safe-stop.request}"

check_safe_stop() {
  if [[ -f "$STOP_REQUEST_FILE" ]]; then
    echo "[STOPPED] Güvenli durdurma isteği alındı. Tamamlanan adımlar ve kalıcı veriler korunuyor." >&2
    return 75
  fi
}

# WSL inherits the Windows caller's current directory. When launched by the WPF
# app that directory can be a transient/unmounted path. apt/dpkg child shells then
# fail with "getcwd() failed" and Node/npm terminates with ENOENT uv_cwd. Use the
# Linux filesystem root for all bootstrap/package-manager work; enter the checkout
# explicitly only after it has been validated below.
unset CDPATH
if ! cd /; then
  printf '[ERROR] Could not switch to stable WSL working directory /\n' >&2
  exit 70
fi
printf '[INFO] Stable WSL working directory: %s\n' "$PWD"

mkdir -p "$ROOT" "$MODULE" "$ROOT/logs"
exec > >(tee -a "$LOG") 2>&1
trap 'rc=$?; echo; if [[ "$rc" == 75 ]]; then echo "[STOPPED] Setup safely stopped at a checkpoint (exit 75). Log: $LOG"; else echo "[ERROR] Setup stopped with exit code $rc. Log: $LOG"; fi; printf "[DIAGNOSTICS] Working directory: "; pwd -P 2>&1 || true; exit "$rc"' ERR

say() {
  check_safe_stop || exit 75
  printf '\n\033[1;36m==> %s\033[0m\n' "$*"
}
progress() { local quality="${3:-estimated}"; printf '[NFSU2-PROGRESS]|%s|%s|%s\n' "$1" "$quality" "$2"; }
die() { echo "[ERROR] $*" >&2; exit 1; }
export DEBIAN_FRONTEND=noninteractive
export APT_LISTCHANGES_FRONTEND=none
export NEEDRESTART_MODE=a
export DPKG_COLORS=never
export UCF_FORCE_CONFFOLD=1
export DEBIAN_PRIORITY=critical
root_run() {
  local -a docker_env=()
  if [[ -n "${NFSU2_DOCKER_CONFIG:-}" ]]; then docker_env+=("DOCKER_CONFIG=$NFSU2_DOCKER_CONFIG"); fi
  if [[ "$(id -u)" == 0 ]]; then
    env DEBIAN_FRONTEND=noninteractive APT_LISTCHANGES_FRONTEND=none NEEDRESTART_MODE=a "${docker_env[@]}" "$@"
  else
    sudo env DEBIAN_FRONTEND=noninteractive APT_LISTCHANGES_FRONTEND=none NEEDRESTART_MODE=a "${docker_env[@]}" "$@"
  fi
}
configure_nfsu2_docker_config() {
  local source_config="${DOCKER_CONFIG:-/root/.docker}"
  local safe_config="/root/.config/nfsu2-control-center-docker"
  [[ -d "$source_config" ]] || source_config="/root/.docker"
  mkdir -p -- /root/.config/nfsu2-control-center-docker
  python3 - "$source_config/config.json" "$safe_config/config.json" <<'PYDOCKERCFG'
import json, pathlib, sys
source, target = map(pathlib.Path, sys.argv[1:3])
data = {}
try:
    if source.is_file():
        data = json.loads(source.read_text(encoding="utf-8"))
except Exception:
    data = {}
# This isolated public-image build config deliberately avoids Windows-only
# Docker Desktop credential helper executables inside Linux/WSL.
data.pop("credsStore", None)
data.pop("credHelpers", None)
target.write_text(json.dumps(data, ensure_ascii=False) + "\n", encoding="utf-8")
PYDOCKERCFG
  if [[ -d "$source_config/contexts" && ! -d "$safe_config/contexts" ]]; then cp -a "$source_config/contexts" "$safe_config/contexts"; fi
  NFSU2_DOCKER_CONFIG="$safe_config"
  export NFSU2_DOCKER_CONFIG DOCKER_CONFIG="$safe_config"
  echo "[INFO] WSL Docker için uygulamaya özel kimlik doğrulama yapılandırması hazırlandı; kullanıcının orijinal Docker ayarları değiştirilmedi."
}
apt_run() {
  root_run apt-get \
    -o Dpkg::Use-Pty=0 \
    -o Dpkg::Progress-Fancy=0 \
    -o DPkg::Lock::Timeout=120 \
    -o Dpkg::Options::=--force-confdef \
    -o Dpkg::Options::=--force-confold \
    "$@"
}
run_with_heartbeat() {
  local label="$1"; shift
  local progress_start=-1 progress_end=-1
  if [[ "${1:-}" == "--progress" ]]; then progress_start="$2"; progress_end="$3"; shift 3; fi
  local started=$SECONDS result=0 command_pid stop_seen=0 last_heartbeat=$SECONDS
  check_safe_stop || exit 75
  if [[ "$progress_start" =~ ^[0-9]+$ ]]; then progress "$progress_start" "$label başlatıldı · kesin yüzde bu aşamada ölçülemiyor" "indeterminate"; fi
  "$@" &
  command_pid=$!
  while kill -0 "$command_pid" 2>/dev/null; do
    sleep 2
    if [[ -f "$STOP_REQUEST_FILE" ]]; then stop_seen=1; fi
    if ! kill -0 "$command_pid" 2>/dev/null; then break; fi
    if (( SECONDS - last_heartbeat < 12 )); then continue; fi
    last_heartbeat=$SECONDS
    local elapsed=$((SECONDS - started))
    echo "[INFO] Still running: $label (${elapsed}s elapsed)."
    if [[ "$progress_start" =~ ^[0-9]+$ ]]; then
      progress "$progress_start" "$label çalışıyor · işlem canlı, aşama tamamlanma yüzdesi henüz bilinmiyor" "indeterminate"
    fi
    if [[ "$label" == *"dependency installation"* || "$label" == *"dpkg package configuration"* || "$label" == *"dependency repair"* || "$label" == *"package installation"* ]]; then
      echo "[DIAGNOSTICS] Process tree / wait state (PID PPID STAT WCHAN ELAPSED CPU MEM COMMAND):"
      ps -eo pid,ppid,stat,wchan:26,etime,%cpu,%mem,comm,args --sort=-%cpu 2>/dev/null | awk '/apt-get|(^|[ /])apt |dpkg|mandb|man-db|debconf|needrestart|dpkg-deb|sudo|ucf/ && !/awk/ {print}' | head -n 20 || true
      echo "[DIAGNOSTICS] Latest dpkg log entries:"
      tail -n 12 /var/log/dpkg.log 2>/dev/null || true
      echo "[DIAGNOSTICS] Latest apt terminal log entries:"
      tail -n 12 /var/log/apt/term.log 2>/dev/null || true
      echo "[DIAGNOSTICS] dpkg audit (non-zero output means pending/inconsistent packages):"
      dpkg --audit 2>&1 || true
    fi
  done
  wait "$command_pid" || result=$?
  local elapsed=$((SECONDS - started))
  if [[ "$result" == 0 ]]; then
    if (( stop_seen == 1 )) || [[ -f "$STOP_REQUEST_FILE" ]]; then
      echo "[STOPPED] $label güvenli aşaması tamamlandı; güvenli durdurma isteği nedeniyle sonraki adımlar başlatılmadı." >&2
      return 75
    fi
    echo "[INFO] Completed: $label (${elapsed}s)."
    if [[ "$progress_end" =~ ^[0-9]+$ ]]; then progress "$progress_end" "$label aşaması tamamlandı" "estimated"; fi
  else
    echo "[ERROR] Failed: $label (exit $result, ${elapsed}s)."
  fi
  return "$result"
}

# For large direct downloads, read Content-Length when the server provides it and
# translate bytes received into the same weighted overall progress range.
download_with_progress() {
  local label="$1" progress_start="$2" progress_end="$3" url="$4" destination="$5"
  local temp_file="${destination}.download" total_bytes="" received=0 mapped=0 last_mapped=-1 pid rc=0
  total_bytes="$(curl -fsSI -L --max-time 25 "$url" 2>/dev/null | awk 'tolower($1)=="content-length:" {gsub("\\r","",$2); size=$2} END {print size}' || true)"
  [[ "$total_bytes" =~ ^[0-9]+$ ]] || total_bytes=0
  rm -f "$temp_file"
  check_safe_stop || return 75
  progress "$progress_start" "$label başlatıldı" "indeterminate"
  curl -fL --retry 3 --retry-delay 2 "$url" -o "$temp_file" &
  pid=$!
  local started=$SECONDS
  while kill -0 "$pid" 2>/dev/null; do
    received="$(stat -c '%s' "$temp_file" 2>/dev/null || echo 0)"
    elapsed=$((SECONDS - started))
    if (( total_bytes > 0 )); then
      local transfer_percent=$((received * 100 / total_bytes))
      (( transfer_percent > 100 )) && transfer_percent=100
      mapped=$((progress_start + transfer_percent * (progress_end - progress_start) / 100))
      (( mapped >= progress_end )) && mapped=$((progress_end - 1))
      if (( mapped > last_mapped )); then
        progress "$mapped" "$label · indirilen veri: $transfer_percent% ($(numfmt --to=iec "$received" 2>/dev/null || echo "$received B") / $(numfmt --to=iec "$total_bytes" 2>/dev/null || echo "$total_bytes B"))" "measured"
        last_mapped=$mapped
      fi
    else
      if (( last_mapped < 0 )); then
        progress "$progress_start" "$label indiriliyor · sunucu boyut bildirmedi, gerçek yüzde ölçülemiyor" "indeterminate"
        last_mapped=0
      fi
    fi
    sleep 1
  done
  wait "$pid" || rc=$?
  if (( rc != 0 )); then
    rm -f "$temp_file"
    echo "[ERROR] Download failed: $label (exit $rc)." >&2
    return "$rc"
  fi
  mv "$temp_file" "$destination"
  if [[ -f "$STOP_REQUEST_FILE" ]]; then
    echo "[STOPPED] $label indirmesi tamamlandı; güvenli durdurma isteği nedeniyle sonraki adımlar başlatılmadı." >&2
    return 75
  fi
  progress "$progress_end" "$label indirme adımı tamamlandı" "estimated"
  echo "[INFO] Download complete: $label ($(stat -c '%s' "$destination" 2>/dev/null || echo 0) bytes)."
}



# Git writes progress with carriage returns. Convert each update to a line and map
# its real Receiving/Resolving percentages into the GitHub stage of the total bar.
git_clone_with_progress() {
  local rc=0
  set +e
  git clone --progress --depth 1 "$REPO_URL" "$PROJECT" 2>&1 | tr '\r' '\n' | while IFS= read -r line; do
    [[ -n "$line" ]] || continue
    printf '%s\n' "$line"
    if [[ "$line" =~ Receiving[[:space:]]objects:[[:space:]]*([0-9]+)% ]]; then
      local pct="${BASH_REMATCH[1]}"
      local mapped=$((59 + pct * 3 / 100))
      (( mapped > 62 )) && mapped=62
      progress "$mapped" "GitHub dosyaları alınıyor · gerçek aktarım: $pct%" "measured"
    elif [[ "$line" =~ Resolving[[:space:]]deltas:[[:space:]]*([0-9]+)% ]]; then
      local pct="${BASH_REMATCH[1]}"
      local mapped=$((62 + pct * 2 / 100))
      (( mapped > 64 )) && mapped=64
      progress "$mapped" "GitHub delta çözümlemesi · gerçek işlem: $pct%" "measured"
    fi
  done
  local -a pipeline_status=( "${PIPESTATUS[@]}" )
  rc="${pipeline_status[0]:-1}"
  set -e
  if (( rc == 0 )) && [[ -f "$STOP_REQUEST_FILE" ]]; then
    echo "[STOPPED] GitHub klonlama güvenli noktada tamamlandı; güvenli durdurma isteği nedeniyle sonraki adımlar başlatılmadı." >&2
    return 75
  fi
  return "$rc"
}

if [[ ! "$PORT" =~ ^[0-9]+$ ]] || (( PORT < 1024 || PORT > 65535 )); then
  die "Port must be between 1024 and 65535. Received: $PORT"
fi

progress 20 "Oyun klasörü doğrulanıyor"
say "Convert and validate the selected Windows game folder"
GAME="$(wslpath -a "$GAME_WIN")"
[[ -d "$GAME" ]] || die "Game directory does not exist in WSL: $GAME_WIN"
[[ -f "$GAME/SPEED2.EXE" ]] || die "SPEED2.EXE not found in $GAME_WIN"
EXPECTED_SHA256='f9dd86c054878ce6276beb07c1fd61874f7a1e4bf1f241b084c65b73e24168a7'
EXE_SIZE="$(stat -c '%s' "$GAME/SPEED2.EXE")"
EXE_SHA="$(sha256sum "$GAME/SPEED2.EXE" | awk '{print $1}')"
echo "Game folder (Windows): $GAME_WIN"
echo "Game folder (WSL): $GAME"
echo "SPEED2.EXE size: $EXE_SIZE bytes"
echo "SPEED2.EXE SHA-256: $EXE_SHA"
if [[ "$EXE_SIZE" != '4800512' || "$EXE_SHA" != "$EXPECTED_SHA256" ]]; then
  die "Unsupported SPEED2.EXE. This installer currently supports the North America v1.2 executable (4,800,512 bytes and expected SHA-256). No copyrighted game files will be downloaded or replaced."
fi
for dir in CARS FRONTEND GLOBAL TRACKS; do
  [[ -d "$GAME/$dir" ]] || die "Required game data folder not found: $GAME/$dir"
  [[ -n "$(find "$GAME/$dir" -mindepth 1 -maxdepth 1 -print -quit)" ]] || die "Required game data folder is empty: $GAME/$dir"
done

progress 25 "Ubuntu bağımlılıkları hazırlanıyor"
say "Prepare package manager and install WSL build dependencies"
missing=()
for bin in git curl tar xz sha256sum python3; do command -v "$bin" >/dev/null 2>&1 || missing+=("$bin"); done
if ((${#missing[@]})); then
  echo "Missing commands: ${missing[*]}"
fi
# If an earlier attempt was interrupted, finish pending package configuration before installing again.
if [[ "$(id -u)" != 0 ]]; then sudo -v; fi
say "Repair any interrupted dpkg configuration"
run_with_heartbeat "dpkg package configuration" --progress 25 27 root_run dpkg --configure -a
run_with_heartbeat "apt dependency repair" --progress 27 29 apt_run -f install -y
say "Update Ubuntu package indexes"
run_with_heartbeat "apt-get update" --progress 29 34 apt_run update -o Acquire::Retries=3
say "Install required build dependencies (non-interactive)"
run_with_heartbeat "build dependency installation" --progress 34 39 apt_run install -y --no-install-recommends git curl ca-certificates tar xz-utils python3 python3-venv python3-pip python3-dev build-essential pkg-config libssl-dev perl
# Validate the package manager has returned to an idle/consistent state before proceeding.
if pgrep -x apt-get >/dev/null 2>&1 || pgrep -x dpkg >/dev/null 2>&1; then
  echo "[INFO] APT/DPKG still has a process after the install command returned; waiting briefly."
  ps -eo pid,ppid,stat,etime,%cpu,%mem,comm,args --sort=-%cpu 2>/dev/null | awk '/apt-get|dpkg|mandb|debconf|needrestart/ && !/awk/ {print}' | head -n 12 || true
  for _ in $(seq 1 30); do
    pgrep -x apt-get >/dev/null 2>&1 || pgrep -x dpkg >/dev/null 2>&1 || break
    sleep 2
  done
  if pgrep -x apt-get >/dev/null 2>&1 || pgrep -x dpkg >/dev/null 2>&1; then
    tail -n 20 /var/log/dpkg.log 2>/dev/null || true
    die "APT/DPKG remains active. Package manager state and active processes are written above; do not start a second installation."
  fi
fi

progress 38 "Sistem paketleri tamamlandı; Docker bağlantısı denetleniyor"
say "Prepare an isolated WSL Docker CLI config"
configure_nfsu2_docker_config
say "Check Docker Desktop integration early"
command -v docker >/dev/null 2>&1 || die "Docker CLI is not available in this Ubuntu WSL distro. Open Docker Desktop > Settings > Resources > WSL Integration and enable this Ubuntu distribution, then rerun the installer."
DOCKER_READY=0
for _ in $(seq 1 60); do
  if root_run docker version >/tmp/nfsu2-docker-version.txt 2>&1 && grep -q '^Server:' /tmp/nfsu2-docker-version.txt; then DOCKER_READY=1; break; fi
  sleep 2
done
if [[ "$DOCKER_READY" != 1 ]]; then
  cat /tmp/nfsu2-docker-version.txt 2>/dev/null || true
  die "Docker Desktop engine did not become ready. Confirm Docker Desktop is running and this Ubuntu distro is enabled under Settings > Resources > WSL Integration."
fi
sed -n '1,24p' /tmp/nfsu2-docker-version.txt

NODE_MAJOR=0
if command -v node >/dev/null 2>&1; then NODE_MAJOR="$(node -p 'Number(process.versions.node.split(".")[0])')"; fi
if [[ "$NODE_MAJOR" != 24 ]] || ! command -v npm >/dev/null 2>&1; then
  say "Install the Node.js 24 web build toolchain"
  NODE_SETUP="$(mktemp)"
  download_with_progress "NodeSource Node.js 24 kurulum betiği" 41 43 https://deb.nodesource.com/setup_24.x "$NODE_SETUP"
  run_with_heartbeat "NodeSource repository setup" --progress 41 43 root_run bash "$NODE_SETUP"
  rm -f "$NODE_SETUP"
  run_with_heartbeat "Node.js 24 and npm package installation" --progress 43 47 apt_run install -y --no-install-recommends nodejs
fi
# A final guard before npm starts: it calls process.cwd() even for --version.
cd / || die "Could not restore a valid working directory before Node/npm validation."
node --version
npm --version
progress 48 "Node.js, npm ve temel araçlar hazır"

if ! command -v rustup >/dev/null 2>&1 || ! command -v rustc >/dev/null 2>&1; then
  say "Install Rust stable toolchain"
  RUSTUP_SCRIPT="$(mktemp)"
  download_with_progress "Rustup kurulum betiği" 48 49 https://sh.rustup.rs "$RUSTUP_SCRIPT"
  run_with_heartbeat "Rust stable toolchain installation" --progress 49 53 sh "$RUSTUP_SCRIPT" -y --profile minimal
  rm -f "$RUSTUP_SCRIPT"
fi
# shellcheck disable=SC1090
[[ -f "$HOME/.cargo/env" ]] && source "$HOME/.cargo/env"
run_with_heartbeat "Rust stable toolchain update" --progress 53 55 rustup update stable
rustc --version
cargo --version
progress 55 "Rust derleme araçları hazır"

progress 59 "GitHub kaynak kodu alınıyor"
say "Download the TRPOLARIS GitHub project"
PROJECT="$INSTALL_USER_HOME/nfsu2-web-trpolaris"
if [[ -d "$PROJECT" ]]; then
  if [[ ! -d "$PROJECT/.git" ]]; then
    die "Project folder already exists but is not a git checkout: $PROJECT. Nothing was deleted. Rename it or move it, then run setup again."
  fi
  ORIGIN="$(git -C "$PROJECT" remote get-url origin 2>/dev/null || true)"
  if [[ "$ORIGIN" != "$REPO_URL" ]]; then
    die "Existing $PROJECT uses another Git remote ($ORIGIN). Nothing was deleted. Move it or change its remote before retrying."
  fi
  echo "Using existing checkout: $PROJECT"
else
  run_with_heartbeat "GitHub repository download" --progress 59 64 git_clone_with_progress
fi
[[ -f "$PROJECT/web/server-rs/Dockerfile" ]] || die "Downloaded repository does not look like the expected TRPOLARIS NFSU2 Web project."
[[ -f "$PROJECT/web/gen.sh" && -f "$PROJECT/web/build.sh" ]] || die "Downloaded source is missing the current web/gen.sh or web/build.sh workflow."
[[ -f "$PROJECT/web/package.json" && -f "$PROJECT/web/package-lock.json" && -f "$PROJECT/requirements.txt" ]] || die "Downloaded source is missing expected web/Python dependency manifests or package lockfile."
[[ -f "$PROJECT/web/server-rs/src/main.rs" && -f "$PROJECT/web/server-rs/Cargo.toml" && -f "$PROJECT/web/server-rs/Cargo.lock" ]] || die "Downloaded source is missing the current Rust server sources or locked Cargo dependencies."
[[ -f "$PROJECT/web/tools/i18n-keys.mjs" ]] || die "The current web build imports web/tools/i18n-keys.mjs, but that source file is missing from the downloaded repository."

say "Validate and repair the minimal Docker web-build source allowlist"
# web/build.mjs imports only web/tools/i18n-keys.mjs. Do not expose the whole
# tools directory to Docker: it contains unrelated development/deployment utilities.
# Keep Dockerfile COPY, Dockerfile.dockerignore and the regression test in exact sync.
DOCKERFILE="$PROJECT/web/server-rs/Dockerfile"
DOCKERIGNORE="$PROJECT/web/server-rs/Dockerfile.dockerignore"
DOCKERTEST="$PROJECT/web/test/docker.test.mjs"
python3 - "$DOCKERFILE" "$DOCKERIGNORE" "$DOCKERTEST" <<'PYPATCH'
from pathlib import Path
import re, sys

dockerfile, ignore, test = (Path(x) for x in sys.argv[1:])
for p in (dockerfile, ignore, test):
    if not p.is_file():
        raise SystemExit(f"Required current-repository build file is missing: {p}")

# 1. COPY only the helper actually imported by web/build.mjs. Remove the stale
# broad-directory rule left by older control-center releases.
s = dockerfile.read_text(encoding="utf-8")
lines = [line for line in s.splitlines() if line.strip() != "COPY web/tools tools"]
required_copy = "COPY web/tools/i18n-keys.mjs tools/i18n-keys.mjs"
if required_copy not in [line.strip() for line in lines]:
    marker = "COPY web/src src"
    if marker not in [line.strip() for line in lines]:
        raise SystemExit("Dockerfile source-copy section changed; cannot safely add i18n helper COPY")
    idx = next(i for i, line in enumerate(lines) if line.strip() == marker)
    lines.insert(idx + 1, required_copy)
s = "\n".join(lines) + "\n"
if required_copy not in s.splitlines() or "COPY web/tools tools" in s.splitlines():
    raise SystemExit("Dockerfile does not have the exact minimal i18n helper COPY rule")
dockerfile.write_text(s, encoding="utf-8")

# 2. Docker's ignore engine needs the parent directory unignored before the
# one allowed file. The ALLOWED source list below still contains only that file.
ignore_lines = ignore.read_text(encoding="utf-8").splitlines()
for required_ignore in ("!web/tools/", "!web/tools/i18n-keys.mjs"):
    if required_ignore not in ignore_lines:
        marker = "!web/src/"
        if marker not in ignore_lines:
            raise SystemExit("Dockerfile.dockerignore allowlist changed; cannot safely add i18n helper")
        idx = ignore_lines.index(marker) + 1
        # Keep parent exception before its single-file exception.
        if required_ignore == "!web/tools/i18n-keys.mjs" and "!web/tools/" in ignore_lines:
            idx = ignore_lines.index("!web/tools/") + 1
        ignore_lines.insert(idx, required_ignore)
ignore.write_text("\n".join(ignore_lines) + "\n", encoding="utf-8")

# 3. Normalize the regression test. The old installer added web/tools/ to
# ALLOWED, directly contradicting the new test that forbids the whole folder.
t = test.read_text(encoding="utf-8")
m = re.search(r"const\s+ALLOWED\s*=\s*\[(.*?)\]\s*;", t, re.S)
if not m:
    raise SystemExit("docker.test.mjs ALLOWED list changed; cannot safely normalize it")
body = m.group(1)
body = re.sub(r"(['\"])web/tools/\1", "'web/tools/i18n-keys.mjs'", body)
if not re.search(r"(['\"])web/tools/i18n-keys\.mjs\1", body):
    if "'web/src/'" in body:
        body = body.replace("'web/src/'", "'web/src/', 'web/tools/i18n-keys.mjs'", 1)
    elif '"web/src/"' in body:
        body = body.replace('"web/src/"', '"web/src/", "web/tools/i18n-keys.mjs"', 1)
    else:
        raise SystemExit("docker.test.mjs ALLOWED list has no web/src entry to anchor the exact helper")
t = t[:m.start(1)] + body + t[m.end(1):]

# A directory exception is valid in .dockerignore but must never be part of ALLOWED.
mi = re.search(r"const\s+ALLOWED_IGNORE_DIRS\s*=\s*\[(.*?)\]\s*;", t, re.S)
if not mi:
    ma = re.search(r"const\s+ALLOWED\s*=\s*\[.*?\]\s*;", t, re.S)
    if not ma:
        raise SystemExit("Could not locate ALLOWED declaration end")
    t = t[:ma.end()] + "\nconst ALLOWED_IGNORE_DIRS = ['web/tools/'];" + t[ma.end():]
else:
    ibody = mi.group(1)
    if not re.search(r"(['\"])web/tools/\1", ibody):
        ibody = ibody.rstrip() + (", " if ibody.strip() else "") + "'web/tools/'"
        t = t[:mi.start(1)] + ibody + t[mi.end(1):]

# Ensure the ignore-list assertion recognizes the parent-directory exception.
if "ALLOWED_IGNORE_DIRS.includes(l.slice(1))" not in t:
    old = "if (l.startsWith('!')) assert.ok(ALLOWED.includes(l.slice(1)),"
    new = "if (l.startsWith('!')) assert.ok(ALLOWED.includes(l.slice(1)) || ALLOWED_IGNORE_DIRS.includes(l.slice(1)),"
    if old not in t:
        raise SystemExit("docker.test.mjs ignore assertion changed; cannot safely normalize it")
    t = t.replace(old, new, 1)

# Ensure the context walker visits the exact helper file, not the whole tools directory.
mc = re.search(r"const\s+CONTEXT_ROOTS\s*=\s*\[(.*?)\]\s*;", t, re.S)
if mc:
    cbody = re.sub(r"(['\"])web/tools/\1", "'web/tools/i18n-keys.mjs'", mc.group(1))
    if not re.search(r"(['\"])web/tools/i18n-keys\.mjs\1", cbody):
        if "'web/src/'" in cbody:
            cbody = cbody.replace("'web/src/'", "'web/src/', 'web/tools/i18n-keys.mjs'", 1)
        elif '"web/src/"' in cbody:
            cbody = cbody.replace('"web/src/"', '"web/src/", "web/tools/i18n-keys.mjs"', 1)
        else:
            raise SystemExit("CONTEXT_ROOTS has no web/src entry for the exact helper")
    t = t[:mc.start(1)] + cbody + t[mc.end(1):]

if not re.search(r"['\"]web/tools/i18n-keys\.mjs['\"]", re.search(r"const\s+ALLOWED\s*=\s*\[(.*?)\]\s*;", t, re.S).group(1)):
    raise SystemExit("ALLOWED still does not contain the exact i18n helper")
al = re.search(r"const\s+ALLOWED\s*=\s*\[(.*?)\]\s*;", t, re.S).group(1)
if re.search(r"['\"]web/tools/['\"]", al):
    raise SystemExit("ALLOWED must not include the entire web/tools directory")
if "!web/tools/" not in ignore.read_text(encoding="utf-8").splitlines() or "!web/tools/i18n-keys.mjs" not in ignore.read_text(encoding="utf-8").splitlines():
    raise SystemExit("Dockerfile.dockerignore is missing the minimal i18n helper exception")
if "only the imported i18n-keys helper" not in t:
    raise SystemExit("Expected i18n helper regression test is missing")
test.write_text(t, encoding="utf-8")
print("Docker build repaired: only web/tools/i18n-keys.mjs is copied and allowlisted; the full web/tools directory remains excluded.")
PYPATCH

say "Prepare the current repository for one-click Docker setup"
cd "$PROJECT"
find . -type f -name '*.sh' -exec chmod u+x {} +
# Compose is generated locally instead of applying a patch made for an older repository.
# The game and module directories are mounted read-only; persistent state remains in a named volume.
cat > compose.local.yml <<'YAML'
name: nfsu2-web-local
services:
  server:
    build:
      context: .
      dockerfile: web/server-rs/Dockerfile
    image: nfsu2-web-rs:local
    restart: unless-stopped
    working_dir: /app
    labels:
      com.trpolaris.nfsu2.project-path: "${NFSU2_PROJECT_DIR:?NFSU2_PROJECT_DIR missing}"
      com.trpolaris.nfsu2.repository: "https://github.com/trpolaris/NFSU2-Web.git"
    network_mode: host
    command: ["--data", "/game", "--http-port", "${NFSU2_HTTP_PORT:-9093}"]
    environment:
      NFSU2_HTTP_PORT: "${NFSU2_HTTP_PORT:-9093}"
      NFSU2_DATA: /game
      NFSU2_WASM: /module/nfsu2.wasm
      NFSU2_REPO: /app
      NFSU2_STATE: /state
    volumes:
      - game-data:/game:ro
      - module-data:/module:ro
      - state:/state
    read_only: true
    cap_drop: [ALL]
    security_opt:
      - no-new-privileges:true
    mem_limit: 4g
    memswap_limit: 4g
    ulimits:
      nofile: 65536
    logging:
      driver: local
volumes:
  game-data:
    name: nfsu2-web-local_game-data
  module-data:
    name: nfsu2-web-local_module-data
  state:
    name: nfsu2-web-local_state
YAML

progress 65 "Kaynak deposu hazır; Python bağımlılıkları kuruluyor"
say "Install Python build requirements"
if [[ ! -x .venv/bin/python ]]; then python3 -m venv .venv; fi
run_with_heartbeat "Python pip bootstrap" --progress 65 67 .venv/bin/python -m pip install --disable-pip-version-check --upgrade pip
run_with_heartbeat "Python dependency installation" --progress 67 70 .venv/bin/python -m pip install --disable-pip-version-check -r requirements.txt

progress 72 "WASI SDK indiriliyor ve hazırlanıyor"
say "Download and prepare WASI SDK 34"
WASI="$PROJECT/deps/wasi-sdk-34.0-x86_64-linux"
if [[ ! -x "$WASI/bin/clang" ]]; then
  mkdir -p "$PROJECT/deps"
  TMP_DIR="$(mktemp -d)"
  download_with_progress "WASI SDK indirme" 72 75 \
    'https://github.com/WebAssembly/wasi-sdk/releases/download/wasi-sdk-34/wasi-sdk-34.0-x86_64-linux.tar.gz' \
    "$TMP_DIR/wasi-sdk.tar.gz"
  tar -xzf "$TMP_DIR/wasi-sdk.tar.gz" -C "$PROJECT/deps"
  rm -rf "$TMP_DIR"
fi
[[ -x "$WASI/bin/clang" ]] || die "WASI SDK clang not found at $WASI/bin/clang"
"$WASI/bin/clang" --version | head -n 1

progress 78 "Kaynak testleri çalıştırılıyor"
say "Run focused source regression tests"
run_with_heartbeat "Docker source regression tests" --progress 78 82 node --test web/test/docker.test.mjs

progress 82 "Oyun kodu seçilen SPEED2.EXE üzerinden üretiliyor"
say "Generate the game-specific C code (this needs your own SPEED2.EXE)"
# The upstream web build rejects shared-worktree links and stale generated data. Always regenerate against this selected local EXE.
# Refuse symlinked generation output rather than deleting a shared path outside this checkout.
if [[ -L gen || -L gen/all || -L gen/server ]]; then
  die "This checkout contains shared generated-code symlinks. Refusing to modify shared worktree data; clone the repository into a fresh folder and retry."
fi
rm -rf gen/all gen/server
mkdir -p gen/all gen/server
run_with_heartbeat "Generate game runtime source" --progress 82 87 bash web/gen.sh "$GAME/SPEED2.EXE"

progress 87 "WebAssembly oyun modülü derleniyor"
say "Compile the WebAssembly game module"
run_with_heartbeat "WebAssembly module compilation" --progress 87 93 env WASI="$WASI" J=2 bash web/build.sh
[[ -s out/web/nfsu2.wasm && -s out/web/nfsu2.json ]] || die 'WASM build finished without both expected output files.'
cp out/web/nfsu2.wasm out/web/nfsu2.json "$MODULE/"
ls -lh "$MODULE/nfsu2.wasm" "$MODULE/nfsu2.json"

say "Write local-only Compose environment"
mkdir -p "$MODULE"
dotenv_quote() { local v="$1"; v="${v//\\/\\\\}"; v="${v//\"/\\\"}"; printf '"%s"' "$v"; }
printf 'NFSU2_DATA=%s\nNFSU2_MODULE=%s\nNFSU2_HTTP_PORT=%s\nNFSU2_PROJECT_DIR=%s\n' \
  "$(dotenv_quote "$GAME")" \
  "$(dotenv_quote "$MODULE")" \
  "$PORT" \
  "$(dotenv_quote "$PROJECT")" > .env.local
[[ -f compose.local.yml ]] || die 'The local Compose profile was not generated.'

# Docker Desktop does not consistently resolve /mnt/c bind sources from every WSL context.
# Stream game/module files into dedicated Docker volumes so the runtime always sees verified data.
seed_runtime_volume() {
  local volume="$1" source="$2" sentinel="$3" signature current
  [[ -s "$source/$sentinel" ]] || die "Required runtime file is missing: $source/$sentinel"
  signature="$(find "$source" -type f -printf '%P\0%s\0%T@\0' | LC_ALL=C sort -z | sha256sum | awk '{print $1}')"
  root_run docker volume create "$volume" >/dev/null
  current="$(root_run docker run --rm --network none -v "$volume:/data:ro" alpine:3.22 sh -c 'cat /data/.nfsu2-source-signature 2>/dev/null || true' 2>/dev/null || true)"
  if [[ "$current" == "$signature" ]]; then
    echo "[INFO] Docker runtime volume already has current data: $volume"
    return 0
  fi
  echo "[INFO] Streaming $(basename "$source") into Docker volume $volume"
  tar -C "$source" -cpf - . | root_run docker run --rm --network none -i -v "$volume:/data" alpine:3.22 sh -ec 'find /data -mindepth 1 -maxdepth 1 -exec rm -rf -- {} +; tar -xpf - -C /data; chmod -R a+rX /data'
  printf '%s\n' "$signature" | root_run docker run --rm --network none -i -v "$volume:/data" alpine:3.22 sh -ec 'cat > /data/.nfsu2-source-signature.tmp; mv /data/.nfsu2-source-signature.tmp /data/.nfsu2-source-signature'
  root_run docker run --rm --network none -v "$volume:/data:ro" alpine:3.22 sh -ec "test -s '/data/$sentinel'" || die "Docker runtime volume does not contain $sentinel: $volume"
}
seed_runtime_volume nfsu2-web-local_game-data "$GAME" SPEED2.EXE
seed_runtime_volume nfsu2-web-local_module-data "$MODULE" nfsu2.wasm

say "Validate the loopback-only local Compose profile"
python3 - "$PROJECT/compose.local.yml" <<'PYCOMPOSE'
from pathlib import Path
import sys
p = Path(sys.argv[1])
s = p.read_text(encoding="utf-8")
if "    network_mode: host\n" not in s:
    raise SystemExit("Local server must use Docker Desktop host networking because its HTTP listener is intentionally bound to 127.0.0.1")
if 'command: ["--data", "/game", "--http-port", "${NFSU2_HTTP_PORT:-9093}"]' not in s:
    raise SystemExit("Local HTTP command line is not the expected loopback-only command")
if '"--bind", "0.0.0.0"' in s:
    raise SystemExit("Refusing to pass --bind without TLS: this option is for the HTTPS listener only")
if "game-data:/game:ro" not in s or "module-data:/module:ro" not in s:
    raise SystemExit("Local profile must use verified Docker-managed game/module volumes")
if "type: bind" in s:
    raise SystemExit("Local profile must not rely on Windows/WSL bind-mounted game or module directories")
if "    ports:" in s:
    raise SystemExit("Local host-network mode must not also publish a container port")
for required in ("NFSU2_DATA: /game", "NFSU2_WASM: /module/nfsu2.wasm", "NFSU2_REPO: /app", "NFSU2_STATE: /state"):
    if required not in s:
        raise SystemExit(f"Local profile is missing required current-server path: {required}")
print("Local profile verified: host networking, current server paths, loopback-only HTTP and no invalid --bind argument.")
PYCOMPOSE

say "Validate Compose and check that the selected port is free"
root_run docker compose --env-file .env.local -f compose.local.yml config >/tmp/nfsu2-compose-config.yml
existing_nfsu2_container="$(root_run docker compose --env-file .env.local -f compose.local.yml ps -q server 2>/dev/null | tail -n 1)"
if ss -ltnH "sport = :$PORT" 2>/dev/null | grep -q .; then
  if [[ -n "$existing_nfsu2_container" ]]; then
    echo "[INFO] Port $PORT is currently held by the existing NFSU2 server. It will remain online while the replacement image is built."
  else
    die "Port $PORT is already occupied by another process. Rerun the Windows script and choose another port."
  fi
fi

progress 93 "Docker imajı derleniyor"
say "Build the Docker image from this exact downloaded/modified checkout"
run_with_heartbeat "Docker image build" --progress 93 97 root_run docker compose --env-file .env.local -f compose.local.yml build

progress 97 "Oyun sunucusu başlatılıyor"
say "Start the game server container"
if ! run_with_heartbeat "Start NFSU2 server container" --progress 97 99 root_run docker compose --env-file .env.local -f compose.local.yml up -d --force-recreate; then
  root_run docker compose --env-file .env.local -f compose.local.yml ps -a || true
  root_run docker compose --env-file .env.local -f compose.local.yml logs --tail=160 server || true
  die "The local container could not start. If Docker reports that host networking is unsupported, enable Docker Desktop > Settings > Resources > Network > Enable host networking, then restart Docker Desktop and rerun setup."
fi

say "Wait for the server container health check"
ready=0
container_id="$(root_run docker compose --env-file .env.local -f compose.local.yml ps -q server 2>/dev/null | tail -n 1)"
if [[ -z "$container_id" ]]; then
  root_run docker compose --env-file .env.local -f compose.local.yml ps -a || true
  root_run docker compose --env-file .env.local -f compose.local.yml logs --tail=160 server || true
  die "Docker Compose did not return a server container ID; startup cannot be verified."
fi
last_health_note=0
for attempt in $(seq 1 90); do
  if [[ -f "$STOP_REQUEST_FILE" ]]; then
    echo "[STOPPED] Yeni konteyner başlatıldı; sağlık kontrolü güvenli noktada bırakıldı. Mevcut kayıt hacmi korunuyor." >&2
    exit 75
  fi
  inspect="$(root_run docker inspect --format '{{.State.Status}}|{{if .State.Health}}{{.State.Health.Status}}{{else}}no-healthcheck{{end}}|{{.RestartCount}}|{{.State.ExitCode}}|{{.State.Error}}' "$container_id" 2>/dev/null || true)"
  IFS='|' read -r container_state health_status restart_count exit_code state_error <<< "$inspect"
  container_state="${container_state:-unknown}"
  health_status="${health_status:-unknown}"
  restart_count="${restart_count:-0}"
  if [[ "$health_status" == "healthy" ]]; then ready=1; break; fi
  # Fallback only for images that do not define a Docker HEALTHCHECK.
  if [[ "$health_status" == "no-healthcheck" ]] && curl --noproxy '*' -fsS --max-time 2 "http://127.0.0.1:$PORT/game.json" -o /dev/null 2>/dev/null; then
    ready=1
    break
  fi
  elapsed=$((attempt * 2))
  if (( elapsed - last_health_note >= 10 )); then
    echo "[INFO] Server readiness still pending (${elapsed}s): state=$container_state health=$health_status restarts=$restart_count exit=${exit_code:-unknown}."
    last_health_note=$elapsed
  fi
  if [[ "$health_status" == "unhealthy" || "$container_state" == "dead" ]]; then
    echo "[DIAGNOSTICS] Container is not healthy; collecting its state and logs immediately."
    break
  fi
  if [[ "$container_state" == "exited" || "$container_state" == "restarting" ]] \
     && [[ "$restart_count" =~ ^[0-9]+$ ]] && (( restart_count >= 3 )); then
    echo "[DIAGNOSTICS] Container exited/restarted repeatedly; stopping readiness wait to show the failure."
    break
  fi
  sleep 2
done
if [[ "$ready" != 1 ]]; then
  echo "[DIAGNOSTICS] Final container inspect: ${inspect:-unavailable}"
  root_run docker compose --env-file .env.local -f compose.local.yml ps -a || true
  root_run docker compose --env-file .env.local -f compose.local.yml logs --tail=200 server || true
  die "The NFSU2 server container did not become healthy. Module/game mount errors or process crashes are shown above; files were preserved."
fi
say "Docker reports the NFSU2 server healthy"

MODULE_REV="$(grep -o '"rev":"[^"]*"' "$MODULE/nfsu2.json" | head -n 1 | cut -d '"' -f4)"
COMMIT="$(git rev-parse --short HEAD)"
INSTALL_UID="$(id -u "$INSTALL_USER" 2>/dev/null || printf '0')"
python3 - "$CONFIG_FILE" "${NFSU2_WSL_DISTRO:-Ubuntu}" "$INSTALL_USER" "$INSTALL_UID" "$INSTALL_USER_HOME" "$PROJECT" "$COMMIT" "$MODULE_REV" "$GAME_WIN" "$GAME" "$PORT" "$LOG" "$WIN_ROOT" <<'PYINSTALLCONFIG'
import datetime, json, os, pathlib, sys
(path, distro, user, uid, home, project, commit, module_rev,
 game_win, game_wsl, port, setup_log, windows_root) = sys.argv[1:]
config_path = pathlib.Path(path)
data = {}
try:
    if config_path.is_file():
        data = json.loads(config_path.read_text(encoding="utf-8"))
except Exception:
    data = {}
install = data.setdefault("install", {})
install.update({
    "wslDistribution": distro,
    "linuxUser": user,
    "linuxUid": int(uid) if uid.isdigit() else 0,
    "linuxHome": home,
    "projectDirectory": project,
    "github": "https://github.com/trpolaris/NFSU2-Web.git",
    "sourceCommit": commit,
    "wasmModuleRevision": module_rev,
    "windowsGameDirectory": game_win,
    "wslGameDirectory": game_wsl,
    "serverPort": int(port),
    "moduleDirectory": windows_root.rstrip("\\/") + "\\module",
    "composeFile": project + "/compose.local.yml",
    "composeEnvironmentFile": project + "/.env.local",
    "containerImage": "nfsu2-web-rs:local",
    "gameVolume": "nfsu2-web-local_game-data",
    "stateVolume": "nfsu2-web-local_state",
    "setupLog": setup_log,
    "installedAt": datetime.datetime.now(datetime.timezone.utc).isoformat()
})
access = data.setdefault("access", {})
access.setdefault("mode", "local")
access.setdefault("url", "http://localhost:" + port + "/")
access.setdefault("lanPort", 8443)
data.setdefault("update", {})
data.setdefault("schemaVersion", 1)
config_path.parent.mkdir(parents=True, exist_ok=True)
temporary = config_path.with_suffix(config_path.suffix + ".tmp")
temporary.write_text(json.dumps(data, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")
os.replace(temporary, config_path)
PYINSTALLCONFIG
rm -f "$ROOT/NFSU2-PROJECT-PATH.txt" "$ROOT/NFSU2-SETUP-REPORT.txt"
echo "Install config: $CONFIG_FILE"
echo "Local URL: http://localhost:$PORT/"

# The GUI runs the build as root to avoid invisible sudo prompts; restore ownership after the build.
if [[ -n "$INSTALL_USER" ]] && id "$INSTALL_USER" >/dev/null 2>&1 && [[ -d "$PROJECT" ]]; then
  INSTALL_GROUP="$(id -gn "$INSTALL_USER")"
  chown -R "$INSTALL_USER":"$INSTALL_GROUP" "$PROJECT" || echo "[WARNING] Could not change project ownership back to $INSTALL_USER."
fi

# Install/project metadata is now stored in nfsu2-config.json.
if [[ -d "$PROJECT" ]]; then
  cat > "$PROJECT/.nfsu2-control-center-project" <<PROJECT_MARKER_EOF
format=1
github=$REPO_URL
distribution=${NFSU2_WSL_DISTRO:-Ubuntu}
linux_user=${INSTALL_USER:-unknown}
linux_home=${INSTALL_USER_HOME:-unknown}
project=$PROJECT
PROJECT_MARKER_EOF
  if [[ -n "$INSTALL_USER" ]] && id "$INSTALL_USER" >/dev/null 2>&1; then
    chown "$INSTALL_USER":"$(id -gn "$INSTALL_USER")" "$PROJECT/.nfsu2-control-center-project" 2>/dev/null || true
  fi
fi

progress 95 "Sunucu hazır; Windows ve erişim doğrulaması bekleniyor"
say "Installation succeeded"
root_run docker compose --env-file .env.local -f compose.local.yml ps
echo "URL: http://localhost:$PORT/"
echo "Install config: $CONFIG_FILE"
echo "Setup log: $LOG"
'@
    $BashScript = $BashScript.Replace('C:\nfsu2', $Root)
    $BashScript = $BashScript.Replace("ROOT='/mnt/c/nfsu2'", "ROOT='$WslControlRoot'")
    $BashScript = $BashScript.Replace('/mnt/c/nfsu2', $WslControlRoot)
    $BashPath = Join-Path $RuntimeRoot 'nfsu2-one-click-wsl.sh'
    $BashPathWsl = "$WslRuntimeRoot/nfsu2-one-click-wsl.sh"
    [IO.File]::WriteAllText($BashPath, ($BashScript -replace "`r`n", "`n"), $Utf8NoBom)
    $GamePathBase64 = [Convert]::ToBase64String([Text.Encoding]::UTF8.GetBytes($GameDir))

    Write-Section 'WSL kurulum betiği sözdizimi kontrolü'
    & wsl.exe -d $Distro -- bash -n $BashPathWsl
    if ($LASTEXITCODE -ne 0) { throw "WSL kurulum betiğinde Bash sözdizimi hatası (kod $LASTEXITCODE). Kurulum başlatılmadı; betik: $BashPath" }

    Write-Section 'Otomatik kurulum başlatılıyor'
    Write-Host 'İlk çalıştırmada WSL içinde sudo parolası sorulabilir. WSL içinde Docker komutu bulunamazsa Docker Desktop > Settings > Resources > WSL Integration altında seçili Ubuntu dağıtımını etkinleştir.' -ForegroundColor Yellow
    $userInfoLines = @(& wsl.exe -d $Distro -u root -- getent passwd ([string]$DistroDefaultUid) 2>$null)
    $userInfoLine = ($userInfoLines | Where-Object { $_ -match ':' } | Select-Object -First 1)
    if ([string]::IsNullOrWhiteSpace($userInfoLine)) { throw "WSL kullanıcı bilgisi alınamadı (UID $DistroDefaultUid). Ubuntu hesabını doğrula ve tekrar dene." }
    $userInfoParts = $userInfoLine.Trim().Split(':')
    if ($userInfoParts.Count -lt 6 -or [string]::IsNullOrWhiteSpace($userInfoParts[5])) { throw 'WSL kullanıcı ev klasörü çözümlenemedi.' }
    $DistroUserName = $userInfoParts[0]
    $DistroHome = $userInfoParts[5]
    if ($DistroUserName -eq 'root') {
        throw 'WSL_DEFAULT_USER_REQUIRED: Ubuntu varsayılan kullanıcısı root olarak kaldı. Standart Linux hesabı oluşturup /etc/wsl.conf içindeki [user] default ayarını tanımla; kurulum root sahipliğinde devam ettirilmedi.'
    }
    Write-Host "WSL build kullanıcı modu: root (dosya sahibi işlem sonunda $DistroUserName olarak geri verilecek)." -ForegroundColor Cyan
    & wsl.exe -d $Distro -u root -- env "HOME=/root" "NFSU2_CONTROL_ROOT=$WslControlRoot" "NFSU2_RUNTIME_DIR=$WslRuntimeRoot" "NFSU2_LOG_DIR=$WslLogsRoot" "NFSU2_CONFIG_FILE=$WslControlRoot/nfsu2-config.json" "NFSU2_STOP_REQUEST_FILE=$WslLogsRoot/.nfsu2-safe-stop.request" "NFSU2_INSTALL_HOME=$DistroHome" "NFSU2_INSTALL_USER=$DistroUserName" "NFSU2_WSL_DISTRO=$Distro" bash $BashPathWsl "$Port" "$GamePathBase64" "$DistroUserName"
    $wslSetupExitCode = $LASTEXITCODE
    if ($wslSetupExitCode -eq 75) {
        Write-Host '[STOPPED] Kurulum güvenli bir aşamada durduruldu. Tamamlanan adımlar ve mevcut dosyalar korundu.' -ForegroundColor Yellow
        exit 75
    }
    if ($wslSetupExitCode -ne 0) {
        throw "Kurulum WSL adımında hata ile durdu (kod $wslSetupExitCode). Günlük klasörü: $LogsRoot. Hata sonrası tekrar çalıştırabilirsin; proje ve modül silinmez."
    }

    Write-Section 'Kurulum tamamlandı'
    Write-Host "Yerel profil için tarayıcı adresi: http://localhost:$Port/" -ForegroundColor Yellow
    Write-Host "Birleşik yapılandırma: $ConfigPath" -ForegroundColor Green
    if (Test-Path -LiteralPath $ConfigPath) { Get-Content -LiteralPath $ConfigPath -Raw | Write-Host }
    Write-Host "PowerShell günlüğü: $TranscriptPath" -ForegroundColor Gray
}
catch {
    Write-Host "`nKURULUM HATASI: $($_.Exception.Message)" -ForegroundColor Red
    Write-Host "Günlük dosyaları: $LogsRoot" -ForegroundColor Yellow
    Write-Host 'Kaynak kodu veya oyun klasörü otomatik olarak silinmedi.' -ForegroundColor Gray
    exit 1
}
finally {
    try { if ($BashPath -and (Test-Path -LiteralPath $BashPath)) { Remove-Item -LiteralPath $BashPath -Force -ErrorAction SilentlyContinue } } catch {}
    try { Stop-Transcript | Out-Null } catch {}
}

