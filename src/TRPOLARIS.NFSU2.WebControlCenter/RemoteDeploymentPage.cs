using Microsoft.Win32;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Windows;
using System.Windows.Controls;

namespace TRPOLARIS.NFSU2.WebControlCenter;

public partial class MainWindow
{
    private static readonly string[] RemoteGameDirectories =
    {
        "CARS", "CREDITS", "FRONTEND", "GLOBAL", "LANGUAGES", "memcard", "MOVIES",
        "NIS", "pfdata", "SOUND", "speech", "SUBTITLES", "TRACKS"
    };
    private static readonly string[] RemoteGameRootFiles = { "SPEED2.EXE", "server.dll", "server.cfg" };

    private void BrowseRemoteGameFolder(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFolderDialog { Title = "Kendi NFSU2 oyun klasörünü seç" };
        if (dialog.ShowDialog(this) == true && _remoteGameFolderBox is not null)
            _remoteGameFolderBox.Text = dialog.FolderName;
    }

    private void BrowseRemoteWasm(object sender, RoutedEventArgs e) =>
        BrowseRemoteFile(_remoteWasmPathBox, "WASM module|nfsu2.wasm|All files|*.*");

    private void BrowseRemoteJson(object sender, RoutedEventArgs e) =>
        BrowseRemoteFile(_remoteJsonPathBox, "JSON files|*.json|All files|*.*");

    private void BrowseRemoteFile(TextBox? target, string filter)
    {
        var dialog = new OpenFileDialog { Title = "NFSU2 derleme çıktısını seç", CheckFileExists = true, Multiselect = false, Filter = filter };
        if (dialog.ShowDialog(this) == true && target is not null) target.Text = dialog.FileName;
    }

    private async Task InstallRemoteServerAsync()
    {
        if (_remoteServerSession?.IsConnected != true) { ShowRemoteError("Önce SSH bağlantısı kur."); return; }
        try
        {
            var profile = ReadRemoteProfile();
            string gameRoot = Path.GetFullPath(_remoteGameFolderBox?.Text.Trim() ?? "");
            string wasmPath = Path.GetFullPath(_remoteWasmPathBox?.Text.Trim() ?? "");
            string jsonPath = Path.GetFullPath(_remoteJsonPathBox?.Text.Trim() ?? "");
            string domain = (_remoteDomainBox?.Text ?? "").Trim().ToLowerInvariant();
            string installRoot = ValidateRemoteInstallRoot(_remoteInstallRootBox?.Text ?? "");
            int port = ParseRemoteHttpPort();
            bool useCaddy = _remoteProxyModeBox?.SelectedIndex == 0;
            string imageReference = GetRemoteImageReference();

            ValidateRemoteDomain(domain);
            ValidateLocalGameFolder(gameRoot);
            ValidateModuleFiles(wasmPath, jsonPath);
            var uploadItems = BuildRemoteUploadPlan(gameRoot, wasmPath, jsonPath, installRoot);
            long totalBytes = uploadItems.Sum(x => new FileInfo(x.LocalPath).Length);
            string accessMode = useCaddy ? "Caddy otomatik HTTPS (80/443 boş olmalı)" : "Mevcut reverse proxy";
            string prompt = $"Sunucu: {profile.Host}:{profile.Port}\nDomain: {domain}\nKurulum: {installRoot}\nErişim: {accessMode}\nSunucu imajı: {imageReference}\n\nYaklaşık {FormatRemoteBytes(totalBytes)} veri SSH/SFTP ile aktarılacak. Docker eksikse Ubuntu/Debian paketleri kurulacak.\nYalnızca seçilmiş oyun veri klasörleri ve WASM modülü gönderilecek.\n\nBaşlatılsın mı?";
            if (MessageBox.Show(prompt, "Uzak NFSU2 kurulumu", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return;

            await RunRemoteDeploymentOperationAsync("Uzak sunucu kurulumu", async progress =>
            {
                progress.Report((2, "Linux ve Docker ön koşulları denetleniyor"));
                string prepared = await _remoteServerSession.ExecuteDeploymentCommandAsync(BuildRemotePrepareCommand(installRoot, useCaddy, port, GetRemoteComposeProjectName(installRoot)), TimeSpan.FromMinutes(8));
                AppendRemoteOutput("Uzak Docker hazırlığı", prepared);

                progress.Report((8, "Oyun dosyaları ve modül SFTP ile aktarılıyor"));
                await _remoteServerSession.UploadFilesAsync(uploadItems, new Progress<RemoteUploadProgress>(p =>
                {
                    int pct = p.TotalBytes <= 0 ? 8 : 8 + (int)Math.Min(74, p.BytesTransferred * 74L / p.TotalBytes);
                    progress.Report((pct, $"Dosyalar aktarılıyor · {p.CompletedFiles}/{p.TotalFiles} · {FormatRemoteBytes(p.BytesTransferred)} / {FormatRemoteBytes(p.TotalBytes)}"));
                }));

                progress.Report((83, "Docker Compose yapılandırması yükleniyor"));
                await UploadRemoteComposeFilesAsync(profile.Id, installRoot, domain, port, useCaddy, imageReference);
                progress.Report((87, "Docker imajı indiriliyor"));
                string output = await _remoteServerSession.ExecuteDeploymentCommandAsync(
                    BuildRemoteComposeCommand(installRoot, "pull") + BuildRemoteComposeCommand(installRoot, "up -d --remove-orphans"),
                    TimeSpan.FromMinutes(20));
                AppendRemoteOutput("Uzak Compose kurulumu", output);

                progress.Report((94, "NFSU2 sağlık kontrolü"));
                string health = await _remoteServerSession.ExecuteDeploymentCommandAsync(BuildRemoteHealthCommand(installRoot, domain, port, useCaddy), TimeSpan.FromMinutes(4));
                AppendRemoteOutput("Uzak sağlık kontrolü", health);
                progress.Report((100, "Uzak NFSU2 kurulumu tamamlandı"));
            });

            if (_remoteDiagnosticsBox is not null) _remoteDiagnosticsBox.Text = await _remoteServerSession.GetServerDiagnosticsAsync();
        }
        catch (Exception ex)
        {
            SetRemoteDeploymentState("Kurulum başarısız: " + ex.Message, false);
            ShowRemoteError(ex.Message);
            AppendLog("Uzak NFSU2 kurulumu başarısız: " + ex.Message, "HATA");
        }
    }

    private async Task UpdateRemoteServerAsync()
    {
        if (_remoteServerSession?.IsConnected != true) { ShowRemoteError("Önce SSH bağlantısı kur."); return; }
        try
        {
            string root = ValidateRemoteInstallRoot(_remoteInstallRootBox?.Text ?? "");
            string wasm = Path.GetFullPath(_remoteWasmPathBox?.Text.Trim() ?? "");
            string json = Path.GetFullPath(_remoteJsonPathBox?.Text.Trim() ?? "");
            string domain = (_remoteDomainBox?.Text ?? "").Trim().ToLowerInvariant();
            int port = ParseRemoteHttpPort();
            bool useCaddy = _remoteProxyModeBox?.SelectedIndex == 0;
            string imageReference = GetRemoteImageReference();
            ValidateModuleFiles(wasm, json);
            ValidateRemoteDomain(domain);

            if (MessageBox.Show($"Seçilen nfsu2.wasm ve nfsu2.json dosyaları aktarılacak. {imageReference} imajı çekilecek ve NFSU2 yeniden başlatılacak. Oyun klasörü ve kalıcı kayıt volume'ü korunur. Devam edilsin mi?",
                "Uzak altyapıyı güncelle", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return;

            await RunRemoteDeploymentOperationAsync("Uzak altyapı güncellemesi", async progress =>
            {
                progress.Report((5, "WASM modülü aktarılıyor"));
                var items = new[]
                {
                    new RemoteUploadItem(wasm, root + "/module/nfsu2.wasm", "nfsu2.wasm"),
                    new RemoteUploadItem(json, root + "/module/nfsu2.json", "nfsu2.json")
                };
                await _remoteServerSession.UploadFilesAsync(items, new Progress<RemoteUploadProgress>(p =>
                {
                    int pct = p.TotalBytes <= 0 ? 5 : 5 + (int)(p.BytesTransferred * 30L / p.TotalBytes);
                    progress.Report((pct, $"Modül aktarılıyor · {p.CompletedFiles}/{p.TotalFiles}"));
                }));
                progress.Report((36, "Docker Compose sürüm eşleşmesi güncelleniyor"));
                string profileId = (_remoteProfileBox?.SelectedItem as RemoteServerProfile)?.Id ?? GetRemoteComposeProjectName(root);
                await UploadRemoteComposeFilesAsync(profileId, root, domain, port, useCaddy, imageReference);
                progress.Report((40, "Docker imajı güncelleniyor"));
                string output = await _remoteServerSession.ExecuteDeploymentCommandAsync(
                    BuildRemoteComposeCommand(root, "pull") + BuildRemoteComposeCommand(root, "up -d --remove-orphans"), TimeSpan.FromMinutes(20));
                AppendRemoteOutput("Uzak altyapı güncellemesi", output);
                progress.Report((90, "Sunucu sağlık kontrolü"));
                string health = await _remoteServerSession.ExecuteDeploymentCommandAsync(BuildRemoteHealthCommand(root, domain, port, useCaddy), TimeSpan.FromMinutes(4));
                AppendRemoteOutput("Uzak sağlık kontrolü", health);
                progress.Report((100, "Uzak altyapı güncellendi"));
            });
            if (_remoteDiagnosticsBox is not null) _remoteDiagnosticsBox.Text = await _remoteServerSession.GetServerDiagnosticsAsync();
        }
        catch (Exception ex)
        {
            SetRemoteDeploymentState("Güncelleme başarısız: " + ex.Message, false);
            ShowRemoteError(ex.Message);
            AppendLog("Uzak altyapı güncellemesi başarısız: " + ex.Message, "HATA");
        }
    }

    private async Task RunRemoteServiceActionAsync(string action)
    {
        if (_remoteServerSession?.IsConnected != true) { ShowRemoteError("Önce SSH bağlantısı kur."); return; }
        if (action is not ("start" or "stop" or "restart")) throw new ArgumentOutOfRangeException(nameof(action));
        try
        {
            string root = ValidateRemoteInstallRoot(_remoteInstallRootBox?.Text ?? "");
            string output = await _remoteServerSession.ExecuteDeploymentCommandAsync(BuildRemoteComposeCommand(root, action), TimeSpan.FromMinutes(2));
            AppendRemoteOutput("Uzak servis " + action, output);
            SetRemoteDeploymentState("Uzak servis işlemi tamamlandı: " + action, true);
        }
        catch (Exception ex) { SetRemoteDeploymentState("Servis işlemi başarısız: " + ex.Message, false); ShowRemoteError(ex.Message); }
    }

    private async Task ReadRemoteLogsAsync()
    {
        if (_remoteServerSession?.IsConnected != true) { ShowRemoteError("Önce SSH bağlantısı kur."); return; }
        try
        {
            string root = ValidateRemoteInstallRoot(_remoteInstallRootBox?.Text ?? "");
            string output = await _remoteServerSession.ExecuteDeploymentCommandAsync(BuildRemoteComposeCommand(root, "logs --tail=200 --no-color"), TimeSpan.FromSeconds(40));
            if (_remoteDiagnosticsBox is not null) _remoteDiagnosticsBox.Text = output;
            SetRemoteDeploymentState("Uzak servis günlükleri alındı.", true);
            AppendLog("Uzak NFSU2 günlükleri alındı.", "OK");
        }
        catch (Exception ex) { SetRemoteDeploymentState("Günlükler alınamadı: " + ex.Message, false); ShowRemoteError(ex.Message); }
    }

    private async Task UninstallRemoteServerAsync()
    {
        if (_remoteServerSession?.IsConnected != true) { ShowRemoteError("Önce SSH bağlantısı kur."); return; }
        string root = ValidateRemoteInstallRoot(_remoteInstallRootBox?.Text ?? "");
        string prompt = $"'{root}' dizinindeki NFSU2 konteynerleri kaldırılacak. Oyun dosyaları, modül ve kalıcı Docker kayıt volume'ü korunacak. Başka Docker projelerine dokunulmayacak.\n\nDevam edilsin mi?";
        if (MessageBox.Show(prompt, "Uzak NFSU2 kurulumunu kaldır", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return;
        try
        {
            string output = await _remoteServerSession.ExecuteDeploymentCommandAsync(BuildRemoteComposeCommand(root, "down --remove-orphans"), TimeSpan.FromMinutes(3));
            AppendRemoteOutput("Uzak NFSU2 kaldırma", output);
            await _remoteServerSession.ExecuteDeploymentCommandAsync($"rm -f {ShellQuote(root + "/compose.yml")} {ShellQuote(root + "/Caddyfile")}", TimeSpan.FromSeconds(30));
            SetRemoteDeploymentState("Konteynerler kaldırıldı. Oyun/modül dosyaları ve kayıt volume'ü korundu.", true);
        }
        catch (Exception ex) { SetRemoteDeploymentState("Kaldırma başarısız: " + ex.Message, false); ShowRemoteError(ex.Message); }
    }

    private async Task PurgeRemoteServerAsync()
    {
        if (_remoteServerSession?.IsConnected != true)
        {
            ShowRemoteError("Önce SSH bağlantısı kur.");
            return;
        }

        string root = ValidateRemoteInstallRoot(_remoteInstallRootBox?.Text ?? "");
        string project = GetRemoteComposeProjectName(root);
        string prompt = $"DİKKAT: '{root}' altındaki tüm NFSU2 oyun dosyaları, modül, yapılandırma ve günlükler kalıcı olarak silinecek.\n\n" +
            $"Ayrıca yalnızca '{project}' Compose projesine ait konteynerler ve kalıcı Docker volume'leri silinecek. Bu işlem geri alınamaz.\n\nDevam etmek istiyor musun?";
        if (MessageBox.Show(prompt, "TÜM UZAK NFSU2 VERİLERİNİ SİL", MessageBoxButton.YesNo, MessageBoxImage.Stop) != MessageBoxResult.Yes)
            return;
        if (MessageBox.Show("Son onay: oyun verileri ve kayıt volume'ü de dahil olmak üzere bu kurulum tamamen silinsin mi?",
            "Kalıcı silme için son onay", MessageBoxButton.YesNo, MessageBoxImage.Stop) != MessageBoxResult.Yes)
            return;

        try
        {
            string rootQuoted = ShellQuote(root);
            string marker = ShellQuote(root + "/.nfsu2-control-center-managed");
            string inner = $"set -eu\n" +
                $"if [ -L {rootQuoted} ] || ! grep -Fxq {ShellQuote(project)} {marker}; then echo '[ERROR] Ownership marker does not match this NFSU2 install. Refusing to purge.' >&2; exit 50; fi\n" +
                $"if [ -f {ShellQuote(root + "/compose.yml")} ]; then cd {rootQuoted} && docker compose --project-name {project} -f compose.yml down -v --remove-orphans; fi\n" +
                $"docker ps -aq --filter label=com.docker.compose.project={project} | xargs -r docker rm -f\n" +
                $"docker volume ls -q --filter label=com.docker.compose.project={project} | xargs -r docker volume rm\n" +
                $"rm -rf -- {rootQuoted}\n";
            string command = "set -eu\nif [ \"$(id -u)\" -eq 0 ]; then sh -c " + ShellQuote(inner) +
                "; else sudo -n sh -c " + ShellQuote(inner) + "; fi\n";
            string output = await _remoteServerSession.ExecuteDeploymentCommandAsync(command, TimeSpan.FromMinutes(5));
            AppendRemoteOutput("Uzak NFSU2 kalıcı silme", output);
            SetRemoteDeploymentState("Uzak NFSU2 kurulumu ve verileri tamamen silindi.", true);
            if (_remoteDiagnosticsBox is not null) _remoteDiagnosticsBox.Text = "Uzak NFSU2 verileri silindi.";
            if (_remoteFileList is not null) _remoteFileList.ItemsSource = null;
        }
        catch (Exception ex)
        {
            SetRemoteDeploymentState("Kalıcı silme başarısız: " + ex.Message, false);
            ShowRemoteError(ex.Message);
        }
    }

    private async Task RunRemoteDeploymentOperationAsync(string name, Func<IProgress<(int Percent, string Status)>, Task> operation)
    {
        var reporter = new Progress<(int Percent, string Status)>(p =>
        {
            if (_remoteDeploymentProgress is not null) _remoteDeploymentProgress.Value = Math.Clamp(p.Percent, 0, 100);
            SetRemoteDeploymentState(p.Status, p.Percent >= 100);
        });
        if (_remoteDeploymentProgress is not null) _remoteDeploymentProgress.Value = 0;
        SetRemoteDeploymentState(name + " başladı.", true);
        try
        {
            await operation(reporter);
            if (_remoteDeploymentProgress is not null) _remoteDeploymentProgress.Value = 100;
            SetRemoteDeploymentState(name + " tamamlandı.", true);
        }
        catch { SetRemoteDeploymentState(name + " başarısız oldu.", false); throw; }
    }

    private void SetRemoteDeploymentState(string message, bool success)
    {
        if (_remoteDeploymentState is null) return;
        _remoteDeploymentState.Text = message;
        _remoteDeploymentState.Foreground = BrushHex(success ? "#65D6B0" : "#E5B65B");
    }

    private void AppendRemoteOutput(string title, string output)
    {
        AppendLog(title + Environment.NewLine + output, "INFO");
        if (_remoteDiagnosticsBox is not null && _currentPage == "Remote") _remoteDiagnosticsBox.Text = title + Environment.NewLine + output;
    }

    private static string ValidateRemoteInstallRoot(string value)
    {
        string root = value.Trim().TrimEnd('/');
        if (!root.StartsWith("/", StringComparison.Ordinal) || root.Length < 5 || root.Contains("..", StringComparison.Ordinal) ||
            root.Any(c => !(char.IsAsciiLetterOrDigit(c) || c is '/' or '-' or '_' or '.')))
            throw new InvalidOperationException("Uzak kurulum dizini mutlak Linux yolu olmalı; yalnızca harf, rakam, '/', '-', '_' ve '.' kullan.");
        if (root is "/" or "/srv" or "/opt" or "/home" or "/root" or "/etc" or "/var")
            throw new InvalidOperationException("Sistem dizininin kendisi kurulum dizini olamaz.");
        return root;
    }

    private int ParseRemoteHttpPort()
    {
        if (!int.TryParse(_remoteHttpPortBox?.Text.Trim(), out int port) || port is < 1024 or > 65535)
            throw new InvalidOperationException("İç HTTP portu 1024 ile 65535 arasında olmalı.");
        return port;
    }

    private static void ValidateRemoteDomain(string domain)
    {
        if (domain.Length is < 4 or > 253 || domain.Contains('/') || domain.Contains(':') ||
            Uri.CheckHostName(domain) != UriHostNameType.Dns || !domain.Contains('.', StringComparison.Ordinal) ||
            domain.Split('.').Any(label => label.Length is < 1 or > 63 || label.StartsWith("-") || label.EndsWith("-")))
            throw new InvalidOperationException("Geçerli bir DNS domaini gir. Örnek: game.example.com");
    }

    private static void ValidateLocalGameFolder(string root)
    {
        if (!Directory.Exists(root)) throw new DirectoryNotFoundException("Yerel oyun klasörü bulunamadı: " + root);
        string exe = Path.Combine(root, "SPEED2.EXE");
        if (!File.Exists(exe)) throw new InvalidDataException("Seçilen klasörde SPEED2.EXE bulunamadı.");
        var info = new FileInfo(exe);
        if (info.Length != SupportedGameExeSize) throw new InvalidDataException("Bu SPEED2.EXE sürümü desteklenmiyor. North America v1.2 dosyasının boyutu eşleşmiyor.");
        using var stream = File.OpenRead(exe);
        string hash = Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
        if (!string.Equals(hash, SupportedGameExeSha256, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("SPEED2.EXE SHA-256 doğrulaması başarısız. Yanlış oyun sürümü gönderilmeyecek.");
        foreach (string name in RemoteGameDirectories)
        {
            string path = Path.Combine(root, name);
            if (!Directory.Exists(path) || !Directory.EnumerateFileSystemEntries(path).Any())
                throw new InvalidDataException("Gerekli oyun veri klasörü eksik veya boş: " + name);
        }
    }

    private static void ValidateModuleFiles(string wasm, string json)
    {
        if (!File.Exists(wasm) || new FileInfo(wasm).Length < 8) throw new FileNotFoundException("Derlenmiş nfsu2.wasm bulunamadı. Önce yerel kurulum/derleme işlemini tamamla.", wasm);
        if (!File.Exists(json) || new FileInfo(json).Length == 0) throw new FileNotFoundException("Derlenmiş nfsu2.json bulunamadı.", json);
    }

    private static IReadOnlyList<RemoteUploadItem> BuildRemoteUploadPlan(string gameRoot, string wasm, string json, string installRoot)
    {
        var items = new List<RemoteUploadItem>();
        string fullRoot = Path.GetFullPath(gameRoot);
        foreach (string name in RemoteGameRootFiles)
        {
            string path = Path.Combine(fullRoot, name);
            if (!File.Exists(path))
            {
                if (name == "SPEED2.EXE") throw new FileNotFoundException("SPEED2.EXE bulunamadı.", path);
                continue;
            }
            EnsureNoReparsePoint(path);
            items.Add(new RemoteUploadItem(path, installRoot + "/game/" + name, "game/" + name));
        }
        foreach (string name in RemoteGameDirectories)
        {
            string directory = Path.Combine(fullRoot, name);
            EnsureNoReparsePoint(directory);
            foreach (string path in EnumerateGameFilesSafely(directory))
            {
                EnsureNoReparsePoint(path);
                string relative = Path.GetRelativePath(fullRoot, path).Replace('\\', '/');
                items.Add(new RemoteUploadItem(path, installRoot + "/game/" + relative, "game/" + relative));
            }
        }
        EnsureNoReparsePoint(wasm);
        EnsureNoReparsePoint(json);
        items.Add(new RemoteUploadItem(wasm, installRoot + "/module/nfsu2.wasm", "module/nfsu2.wasm"));
        items.Add(new RemoteUploadItem(json, installRoot + "/module/nfsu2.json", "module/nfsu2.json"));
        return items;
    }

    private static IEnumerable<string> EnumerateGameFilesSafely(string directory)
    {
        foreach (string entry in Directory.EnumerateFileSystemEntries(directory))
        {
            EnsureNoReparsePoint(entry);
            if (Directory.Exists(entry))
            {
                foreach (string nested in EnumerateGameFilesSafely(entry)) yield return nested;
            }
            else if (File.Exists(entry)) yield return entry;
        }
    }

    private static void EnsureNoReparsePoint(string path)
    {
        FileSystemInfo info = Directory.Exists(path) ? new DirectoryInfo(path) : new FileInfo(path);
        if (info.Attributes.HasFlag(FileAttributes.ReparsePoint))
            throw new InvalidDataException("Sembolik bağlantı/junction içeren dosya gönderilmiyor: " + path);
    }

    private async Task UploadRemoteComposeFilesAsync(string profileId, string root, string domain, int port, bool useCaddy, string imageReference)
    {
        string temp = Path.Combine(RuntimeDir, "remote-deploy", profileId);
        Directory.CreateDirectory(temp);
        string composePath = Path.Combine(temp, "compose.yml");
        string caddyPath = Path.Combine(temp, "Caddyfile");
        File.WriteAllText(composePath, BuildRemoteComposeYaml(domain, port, useCaddy, imageReference), new UTF8Encoding(false));
        var files = new List<RemoteUploadItem> { new(composePath, root + "/compose.yml", "compose.yml") };
        if (useCaddy)
        {
            File.WriteAllText(caddyPath, BuildRemoteCaddyfile(domain, port), new UTF8Encoding(false));
            files.Add(new RemoteUploadItem(caddyPath, root + "/Caddyfile", "Caddyfile"));
        }
        try { await _remoteServerSession!.UploadFilesAsync(files); }
        finally { try { Directory.Delete(temp, true); } catch { } }
    }

    private static string BuildRemoteComposeYaml(string domain, int port, bool useCaddy, string imageReference)
    {
        string caddy = useCaddy ? """
          caddy:
            image: caddy:2.11-alpine
            restart: unless-stopped
            network_mode: host
            volumes:
              - ./Caddyfile:/etc/caddy/Caddyfile:ro
              - caddy-data:/data
              - caddy-config:/config
            logging:
              driver: local
        """ : "";
        string volumes = useCaddy ? "  caddy-data:\n  caddy-config:\n" : "";
        return $"""
        name: nfsu2-web
        services:
        {caddy}
          server:
            image: {imageReference}
            restart: unless-stopped
            network_mode: host
            command: ["--behind-proxy"]
            environment:
              NFSU2_HOST: {domain}
              NFSU2_HTTP_PORT: "{port}"
              NFSU2_DATA: /game
              NFSU2_WASM: /module/nfsu2.wasm
              NFSU2_REPO: /app
              NFSU2_STATE: /state
            volumes:
              - ./game:/game:ro
              - ./module:/module:ro
              - state:/state
            read_only: true
            cap_drop: [ALL]
            security_opt:
              - no-new-privileges:true
            logging:
              driver: local
        volumes:
          state:
        {volumes}
        """;
    }

    private static string GetRemoteImageReference()
    {
        const string image = "ghcr.io/trpolaris/need-for-speed-underground-2-web-rs";
        string commit = (Nfsu2ConfigStore.Read(RootDir).Install.SourceCommit ?? "").Trim().ToLowerInvariant();
        if (commit.Length >= 7 && commit.All(Uri.IsHexDigit))
            return image + ":sha-" + commit[..7];
        return image + ":latest";
    }

    private static string BuildRemoteCaddyfile(string domain, int port) =>
        "{\n    admin off\n}\n" +
        "http://" + domain + " {\n    header -Server\n    redir https://{host}{uri} 308\n}\n" +
        ":80 {\n    abort\n}\n" +
        ":443 {\n    abort\n}\n" +
        domain + " {\n    reverse_proxy 127.0.0.1:" + port.ToString(System.Globalization.CultureInfo.InvariantCulture) +
        " {\n        header_up CF-Connecting-IP {client_ip}\n    }\n    header -Server\n    header -Via\n" +
        "    handle_errors {\n        header -Server\n    }\n}\n";

    private static string BuildRemotePrepareCommand(string root, bool useCaddy, int port, string project)
    {
        string checkPorts = useCaddy ? """
        if ss -ltnH '( sport = :80 or sport = :443 )' 2>/dev/null | grep -q .; then
            echo '[ERROR] Port 80 or 443 is in use. Choose existing reverse proxy mode or free the ports.' >&2
            exit 31
        fi
        """ : "";
        string quotedRoot = ShellQuote(root);
        string markerPath = ShellQuote(root + "/.nfsu2-control-center-managed");
        string projectValue = ShellQuote(project);
        string internalPortCheck = $"""
        if ss -ltnH 'sport = :{port}' 2>/dev/null | grep -q .; then
            echo '[ERROR] The selected internal HTTP port is already in use. Choose another port.' >&2
            exit 32
        fi
        """;
        return $"""
        set -eu
        if [ "$(id -u)" -ne 0 ]; then
            command -v sudo >/dev/null 2>&1 || exit 20
            if ! sudo -n true; then
                echo '[ERROR] Root or passwordless sudo is required.' >&2
                exit 21
            fi
            SUDO='sudo -n'
        else
            SUDO=''
        fi
        . /etc/os-release
        case "$ID" in ubuntu|debian) ;; *) echo "[ERROR] Ubuntu/Debian required; found $PRETTY_NAME"; exit 22;; esac
        {checkPorts}
        {internalPortCheck}
        if ! command -v docker >/dev/null 2>&1; then
            $SUDO apt-get update
            $SUDO env DEBIAN_FRONTEND=noninteractive apt-get install -y docker.io docker-compose-v2
        elif ! docker compose version >/dev/null 2>&1; then
            $SUDO apt-get update
            $SUDO env DEBIAN_FRONTEND=noninteractive apt-get install -y docker-compose-v2
        fi
        if ! command -v curl >/dev/null 2>&1 || ! command -v ss >/dev/null 2>&1; then
            $SUDO apt-get update
            $SUDO env DEBIAN_FRONTEND=noninteractive apt-get install -y curl iproute2
        fi
        $SUDO systemctl enable --now docker
        $SUDO docker compose version
        invalid_root=0
        if [ -L {quotedRoot} ]; then invalid_root=1; fi
        if [ -e {quotedRoot} ] && [ ! -d {quotedRoot} ]; then invalid_root=1; fi
        if [ "$invalid_root" -eq 1 ]; then
            echo '[ERROR] The selected install path is a symlink or not a directory. Choose a different path.' >&2
            exit 34
        fi
        $SUDO mkdir -p {quotedRoot}
        if [ -L {quotedRoot}/game ] || [ -L {quotedRoot}/module ] || [ -L {markerPath} ]; then
            echo '[ERROR] The game, module or ownership marker path is a symbolic link. Deployment was refused.' >&2
            exit 35
        fi
        if [ -f {markerPath} ]; then
            if ! grep -Fxq {projectValue} {markerPath}; then
                echo '[ERROR] The install path belongs to a different Control Center project. Choose a new empty path.' >&2
                exit 36
            fi
        else
            existing_entry="$(find {quotedRoot} -mindepth 1 -maxdepth 1 -print -quit)"
            if [ -n "$existing_entry" ]; then
                echo '[ERROR] The install path is not empty and has no Control Center ownership marker. Choose another path; existing files were not touched.' >&2
                exit 37
            fi
            printf '%s\n' {projectValue} | $SUDO tee {markerPath} >/dev/null
        fi
        $SUDO mkdir -p {quotedRoot}/game {quotedRoot}/module
        $SUDO chown -R "$(id -u):$(id -g)" {quotedRoot}
        chmod 755 {quotedRoot} {quotedRoot}/game {quotedRoot}/module
        available_kb="$(df -Pk {quotedRoot} | tail -n 1 | sed -E 's/ +/ /g' | cut -d ' ' -f4)"
        if [ "$available_kb" -lt 5000000 ]; then
            echo '[ERROR] Less than 5 GB of free disk space is available on the target filesystem.' >&2
            exit 33
        fi
        echo '[OK] Docker Engine, Compose, target folders, HTTP port and free disk space are ready.'
        """;
    }

    private static string BuildRemoteComposeCommand(string root, string operation)
    {
        string project = GetRemoteComposeProjectName(root);
        string marker = ShellQuote(root + "/.nfsu2-control-center-managed");
        string compose = $"cd {ShellQuote(root)} && grep -Fxq {ShellQuote(project)} {marker} && docker compose --project-name {project} -f compose.yml {operation}";
        return "set -eu\nif [ \"$(id -u)\" -eq 0 ]; then sh -c " + ShellQuote(compose) +
            "; else sudo -n sh -c " + ShellQuote(compose) + "; fi\n";
    }

    private static string GetRemoteComposeProjectName(string root)
    {
        string digest = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(root.ToLowerInvariant())))
            .ToLowerInvariant();
        return "nfsu2-" + digest[..10];
    }

    private static string BuildRemoteHealthCommand(string root, string domain, int port, bool useCaddy)
    {
        string url = useCaddy ? "https://" + domain + "/game.json" : "http://127.0.0.1:" + port + "/game.json";
        string tls = useCaddy ? "-k --resolve " + domain + ":443:127.0.0.1" : "";
        string hostHeader = useCaddy ? "" : "-H " + ShellQuote("Host: " + domain);
        string status = BuildRemoteComposeCommand(root, "ps").TrimEnd();
        string publicUrl = useCaddy ? "https://" + domain + "/" : "http://127.0.0.1:" + port + "/ (reverse proxy hedefi)";
        return $"""
        set -eu
        {status}
        for attempt in $(seq 1 90); do
            if curl --noproxy '*' {tls} {hostHeader} -fsS --max-time 4 {ShellQuote(url)} -o /dev/null; then
                echo '[OK] NFSU2 server responds to /game.json.'
                echo 'URL: {publicUrl}'
                exit 0
            fi
            sleep 2
        done
        echo '[ERROR] NFSU2 did not become reachable in 180 seconds.' >&2
        {status}
        exit 40
        """;
    }

    private static string ShellQuote(string value) => "'" + value.Replace("'", "'\\''", StringComparison.Ordinal) + "'";

    private static string FormatRemoteBytes(long bytes)
    {
        string[] units = { "B", "KB", "MB", "GB", "TB" };
        double value = Math.Max(0, bytes);
        int unit = 0;
        while (value >= 1024 && unit < units.Length - 1) { value /= 1024; unit++; }
        return value.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture) + " " + units[unit];
    }
}
