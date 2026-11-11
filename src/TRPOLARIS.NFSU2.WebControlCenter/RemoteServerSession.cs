using System.IO;
using System.Security.Cryptography;
using System.Text;
using Renci.SshNet;
using Renci.SshNet.Common;
using Renci.SshNet.Sftp;

namespace TRPOLARIS.NFSU2.WebControlCenter;

internal sealed class RemoteHostKeyTrustRequiredException : Exception
{
    public string Fingerprint { get; }
    public RemoteHostKeyTrustRequiredException(string fingerprint, Exception innerException)
        : base("SSH sunucusunun anahtarı henüz güvenilir olarak kaydedilmemiş.", innerException)
        => Fingerprint = fingerprint;
}

internal sealed class RemoteHostKeyMismatchException : Exception
{
    public string ExpectedFingerprint { get; }
    public string ReceivedFingerprint { get; }
    public RemoteHostKeyMismatchException(string expected, string received, Exception innerException)
        : base("Uzak sunucunun SSH host key parmak izi kayıtlı değerle eşleşmiyor. Güvenlik nedeniyle bağlantı reddedildi.", innerException)
    {
        ExpectedFingerprint = expected;
        ReceivedFingerprint = received;
    }
}

/// <summary>
/// SSH transport for the remote Linux mode. This first milestone exposes only
/// fixed application-owned diagnostics; it does not execute arbitrary shell input.
/// </summary>
internal sealed class RemoteServerSession : IDisposable
{
    private readonly RemoteServerProfile _profile;
    private SshClient? _client;
    private PrivateKeyFile? _privateKeyFile;
    private string _receivedFingerprint = "";
    private string _password = "";
    private string _privateKeyPassphrase = "";

    public RemoteServerSession(RemoteServerProfile profile) =>
        _profile = profile ?? throw new ArgumentNullException(nameof(profile));

    public bool IsConnected => _client?.IsConnected == true;

    public Task ConnectAsync(string password, string privateKeyPassphrase, CancellationToken cancellationToken = default) =>
        Task.Run(() => ConnectCore(password, privateKeyPassphrase), cancellationToken);

    private void ConnectCore(string password, string privateKeyPassphrase)
    {
        Disconnect();
        _receivedFingerprint = "";
        _password = password ?? "";
        _privateKeyPassphrase = privateKeyPassphrase ?? "";

        AuthenticationMethod authentication;
        if (string.Equals(_profile.AuthenticationMode, "private-key", StringComparison.OrdinalIgnoreCase))
        {
            if (string.IsNullOrWhiteSpace(_profile.PrivateKeyPath) || !File.Exists(_profile.PrivateKeyPath))
                throw new FileNotFoundException("Seçilen SSH özel anahtar dosyası bulunamadı.", _profile.PrivateKeyPath);

            _privateKeyFile = string.IsNullOrEmpty(privateKeyPassphrase)
                ? new PrivateKeyFile(_profile.PrivateKeyPath)
                : new PrivateKeyFile(_profile.PrivateKeyPath, privateKeyPassphrase);
            authentication = new PrivateKeyAuthenticationMethod(_profile.Username, _privateKeyFile);
        }
        else
        {
            if (string.IsNullOrEmpty(password)) throw new InvalidOperationException("SSH parolası girilmedi.");
            authentication = new PasswordAuthenticationMethod(_profile.Username, password);
        }

        var connectionInfo = new ConnectionInfo(_profile.Host, _profile.Port, _profile.Username, authentication);
        var client = new SshClient(connectionInfo);
        client.HostKeyReceived += HandleHostKeyReceived;
        _client = client;

        try
        {
            client.Connect();
        }
        catch (Exception ex)
        {
            string expected = NormalizeFingerprint(_profile.HostKeyFingerprint);
            string received = NormalizeFingerprint(_receivedFingerprint);
            client.Dispose();
            _client = null;
            _privateKeyFile?.Dispose();
            _privateKeyFile = null;

            if (received.Length > 0 && expected.Length == 0)
                throw new RemoteHostKeyTrustRequiredException("SHA256:" + received, ex);
            if (received.Length > 0 && expected.Length > 0 && !FingerprintsMatch(expected, received))
                throw new RemoteHostKeyMismatchException("SHA256:" + expected, "SHA256:" + received, ex);
            throw;
        }
    }

    private void HandleHostKeyReceived(object? sender, HostKeyEventArgs e)
    {
        string fingerprint = NormalizeFingerprint(e.FingerPrintSHA256);
        _receivedFingerprint = fingerprint;
        string expected = NormalizeFingerprint(_profile.HostKeyFingerprint);
        // Fail closed on first use. UI asks the user to verify and explicitly trust the key.
        e.CanTrust = expected.Length > 0 && FingerprintsMatch(expected, fingerprint);
    }

    public async Task<string> GetServerDiagnosticsAsync(CancellationToken cancellationToken = default)
    {
        const string command = """
            printf 'hostname='; hostname
            printf 'os='; grep '^PRETTY_NAME=' /etc/os-release 2>/dev/null | cut -d= -f2- | tr -d '"'; printf '\n'
            printf 'kernel='; uname -r
            printf 'uptime='; uptime -p 2>/dev/null || uptime
            printf 'cpu_cores='; getconf _NPROCESSORS_ONLN 2>/dev/null || nproc 2>/dev/null || printf 'unknown\n'
            printf 'memory='; free -h 2>/dev/null | awk 'NR==2 {print $2 " total, " $3 " used, " $4 " free"}' || printf 'unavailable\n'
            printf 'root_disk='; df -h / 2>/dev/null | awk 'NR==2 {print $2 " total, " $3 " used, " $5 " used"}' || printf 'unavailable\n'
            printf 'docker_cli='; if command -v docker >/dev/null 2>&1; then docker --version 2>/dev/null || printf 'installed\n'; else printf 'missing\n'; fi
            printf 'docker_engine='; if command -v docker >/dev/null 2>&1; then docker info --format '{{.ServerVersion}}' 2>/dev/null || printf 'unavailable_or_permission_denied\n'; else printf 'missing\n'; fi
            printf 'docker_compose='; if command -v docker >/dev/null 2>&1; then docker compose version --short 2>/dev/null || printf 'missing\n'; else printf 'missing\n'; fi
            """;

        return await ExecuteAsync(command, TimeSpan.FromSeconds(25), cancellationToken).ConfigureAwait(false);
    }

    private async Task<string> ExecuteAsync(string commandText, TimeSpan timeout, CancellationToken cancellationToken)
    {
        var client = _client;
        if (client?.IsConnected != true) throw new InvalidOperationException("Uzak sunucuya bağlı değil.");

        return await Task.Run(() =>
        {
            using var command = client.CreateCommand(commandText);
            command.CommandTimeout = timeout;
            string output = command.Execute();
            string error = command.Error;
            if (command.ExitStatus != 0)
                throw new InvalidOperationException($"Uzak komut başarısız (çıkış kodu {command.ExitStatus}). {error}".Trim());
            return string.IsNullOrWhiteSpace(error) ? output : output + Environment.NewLine + error;
        }, cancellationToken).ConfigureAwait(false);
    }

    public async Task UploadFilesAsync(
        IReadOnlyList<RemoteUploadItem> items,
        IProgress<RemoteUploadProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(items);
        if (items.Count == 0) throw new ArgumentException("Yüklenecek dosya bulunamadı.", nameof(items));
        if (!IsConnected) throw new InvalidOperationException("Önce uzak sunucuya bağlan.");

        await Task.Run(() =>
        {
            using var sftp = new SftpClient(BuildConnectionInfo());
            sftp.HostKeyReceived += HandleHostKeyReceived;
            sftp.Connect();
            if (!sftp.IsConnected) throw new InvalidOperationException("SFTP bağlantısı kurulamadı.");

            try
            {
                long totalBytes = items.Sum(x => new FileInfo(x.LocalPath).Length);
                long completedBytes = 0;
                int completedFiles = 0;

                foreach (var item in items)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var file = new FileInfo(item.LocalPath);
                    if (!file.Exists) throw new FileNotFoundException("Yüklenecek yerel dosya bulunamadı.", item.LocalPath);
                    EnsureRemoteDirectory(sftp, GetRemoteDirectory(item.RemotePath));

                    string temporaryRemotePath = item.RemotePath + ".uploading";
                    if (sftp.Exists(temporaryRemotePath)) sftp.DeleteFile(temporaryRemotePath);
                    using var input = new FileStream(item.LocalPath, FileMode.Open, FileAccess.Read, FileShare.Read);
                    ulong lastReported = 0;
                    sftp.UploadFile(input, temporaryRemotePath, true, uploaded =>
                    {
                        if (uploaded < lastReported) return;
                        lastReported = uploaded;
                        long aggregate = completedBytes + (long)Math.Min(uploaded, (ulong)long.MaxValue);
                        progress?.Report(new RemoteUploadProgress(item.RelativePath, aggregate, totalBytes, completedFiles, items.Count));
                    });
                    cancellationToken.ThrowIfCancellationRequested();
                    if (sftp.Exists(item.RemotePath)) sftp.DeleteFile(item.RemotePath);
                    sftp.RenameFile(temporaryRemotePath, item.RemotePath);
                    completedBytes += file.Length;
                    completedFiles++;
                    progress?.Report(new RemoteUploadProgress(item.RelativePath, completedBytes, totalBytes, completedFiles, items.Count));
                }
            }
            finally
            {
                try { if (sftp.IsConnected) sftp.Disconnect(); } catch { }
            }
        }, cancellationToken).ConfigureAwait(false);
    }

    public Task<IReadOnlyList<RemoteFileEntry>> ListRemoteDirectoryAsync(
        string remotePath, CancellationToken cancellationToken = default)
    {
        return Task.Run<IReadOnlyList<RemoteFileEntry>>(() =>
        {
            using var sftp = new SftpClient(BuildConnectionInfo());
            sftp.HostKeyReceived += HandleHostKeyReceived;
            sftp.Connect();
            try
            {
                return sftp.ListDirectory(remotePath)
                    .Where(x => x.Name is not ("." or ".." or ".nfsu2-control-center-managed"))
                    .Where(x => !x.Attributes.IsSymbolicLink && (x.IsDirectory || x.IsRegularFile))
                    .OrderByDescending(x => x.IsDirectory)
                    .ThenBy(x => x.Name, StringComparer.OrdinalIgnoreCase)
                    .Select(x => new RemoteFileEntry(
                        x.Name, x.FullName, x.IsDirectory, x.Length, x.LastWriteTimeUtc))
                    .ToArray();
            }
            finally { try { if (sftp.IsConnected) sftp.Disconnect(); } catch { } }
        }, cancellationToken);
    }

    public Task DownloadRemoteFileAsync(
        string remotePath, string localPath, CancellationToken cancellationToken = default)
    {
        return Task.Run(() =>
        {
            using var sftp = new SftpClient(BuildConnectionInfo());
            sftp.HostKeyReceived += HandleHostKeyReceived;
            sftp.Connect();
            try
            {
                var attributes = sftp.GetAttributes(remotePath);
                if (attributes.IsSymbolicLink || attributes.IsDirectory || !attributes.IsRegularFile)
                    throw new InvalidOperationException("Yalnızca normal dosyalar indirilebilir.");
                string? parent = Path.GetDirectoryName(localPath);
                if (!string.IsNullOrWhiteSpace(parent)) Directory.CreateDirectory(parent);
                using var output = new FileStream(localPath, FileMode.Create, FileAccess.Write, FileShare.None);
                sftp.DownloadFile(remotePath, output);
                output.Flush();
            }
            finally { try { if (sftp.IsConnected) sftp.Disconnect(); } catch { } }
        }, cancellationToken);
    }

    public Task DeleteRemoteFileOrEmptyDirectoryAsync(
        string remotePath, CancellationToken cancellationToken = default)
    {
        return Task.Run(() =>
        {
            using var sftp = new SftpClient(BuildConnectionInfo());
            sftp.HostKeyReceived += HandleHostKeyReceived;
            sftp.Connect();
            try
            {
                var attributes = sftp.GetAttributes(remotePath);
                if (attributes.IsSymbolicLink)
                    throw new InvalidOperationException("Sembolik bağlantılar dosya yöneticisinden silinmez.");
                if (attributes.IsDirectory) sftp.DeleteDirectory(remotePath);
                else if (attributes.IsRegularFile) sftp.DeleteFile(remotePath);
                else throw new InvalidOperationException("Bu dosya türü silinemez.");
            }
            finally { try { if (sftp.IsConnected) sftp.Disconnect(); } catch { } }
        }, cancellationToken);
    }

    public Task CreateRemoteDirectoryAsync(
        string remotePath, CancellationToken cancellationToken = default)
    {
        return Task.Run(() =>
        {
            using var sftp = new SftpClient(BuildConnectionInfo());
            sftp.HostKeyReceived += HandleHostKeyReceived;
            sftp.Connect();
            try
            {
                if (sftp.Exists(remotePath))
                    throw new IOException("Bu adla bir dosya veya klasör zaten bulunuyor.");
                sftp.CreateDirectory(remotePath);
            }
            finally { try { if (sftp.IsConnected) sftp.Disconnect(); } catch { } }
        }, cancellationToken);
    }

    public Task<string> ExecuteDeploymentCommandAsync(
        string commandText,
        TimeSpan timeout,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(commandText) || commandText.Length > 16000)
            throw new ArgumentException("Uzak işlem komutu geçersiz.", nameof(commandText));
        return ExecuteAsync(commandText, timeout, cancellationToken);
    }

    private ConnectionInfo BuildConnectionInfo()
    {
        AuthenticationMethod authentication;
        if (string.Equals(_profile.AuthenticationMode, "private-key", StringComparison.OrdinalIgnoreCase))
        {
            _privateKeyFile ??= string.IsNullOrEmpty(_privateKeyPassphrase)
                ? new PrivateKeyFile(_profile.PrivateKeyPath)
                : new PrivateKeyFile(_profile.PrivateKeyPath, _privateKeyPassphrase);
            authentication = new PrivateKeyAuthenticationMethod(_profile.Username, _privateKeyFile);
        }
        else
        {
            authentication = new PasswordAuthenticationMethod(_profile.Username, _password);
        }
        return new ConnectionInfo(_profile.Host, _profile.Port, _profile.Username, authentication);
    }

    private static void EnsureRemoteDirectory(SftpClient sftp, string path)
    {
        string current = path.StartsWith("/", StringComparison.Ordinal) ? "/" : "";
        foreach (string part in path.Split('/', StringSplitOptions.RemoveEmptyEntries))
        {
            current = current == "/" ? current + part : current.Length == 0 ? part : current + "/" + part;
            if (!sftp.Exists(current)) sftp.CreateDirectory(current);
        }
    }

    private static string GetRemoteDirectory(string path)
    {
        int separator = path.LastIndexOf('/');
        return separator <= 0 ? "/" : path[..separator];
    }

    public void Disconnect()
    {
        var client = _client;
        _client = null;
        if (client is not null)
        {
            try { if (client.IsConnected) client.Disconnect(); } catch { }
            client.Dispose();
        }
        _privateKeyFile?.Dispose();
        _privateKeyFile = null;
        _password = "";
        _privateKeyPassphrase = "";
    }

    public void Dispose() => Disconnect();

    private static string NormalizeFingerprint(string? fingerprint)
    {
        string value = (fingerprint ?? "").Trim();
        if (value.StartsWith("SHA256:", StringComparison.OrdinalIgnoreCase))
            value = value["SHA256:".Length..];
        return value.TrimEnd('=').Trim();
    }

    private static bool FingerprintsMatch(string left, string right)
    {
        byte[] a = Encoding.ASCII.GetBytes(NormalizeFingerprint(left));
        byte[] b = Encoding.ASCII.GetBytes(NormalizeFingerprint(right));
        return a.Length == b.Length && CryptographicOperations.FixedTimeEquals(a, b);
    }
}
