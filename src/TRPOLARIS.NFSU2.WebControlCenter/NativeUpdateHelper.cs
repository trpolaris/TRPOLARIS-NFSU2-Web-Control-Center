using System.Diagnostics;
using System.Windows;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace TRPOLARIS.NFSU2.WebControlCenter;

internal static class NativeUpdateHelper
{
    private const string HelperSwitch = "--native-update-helper";
    private const string HealthSwitch = "--update-health";
    private const string CleanupSwitch = "--cleanup-helper";
    private static readonly TimeSpan ParentExitTimeout = TimeSpan.FromMinutes(2);
    private static readonly TimeSpan HealthTimeout = TimeSpan.FromSeconds(45);

    private sealed record HelperRequest(
        string SourcePath,
        string TargetPath,
        string LegacyPath,
        string ExpectedSha256,
        long ExpectedSize,
        int ParentProcessId,
        string Version,
        string FromVersion,
        string Channel,
        bool DeleteSourceAfterApply,
        string HelperExecutablePath,
        string HealthMarkerPath);

    public static bool IsHelperInvocation(string[] args) =>
        args.Length >= 2 && args[0].Equals(HelperSwitch, StringComparison.Ordinal);

    public static string? GetArgumentValue(string[] args, string name)
    {
        for (int i = 0; i < args.Length - 1; i++)
            if (args[i].Equals(name, StringComparison.OrdinalIgnoreCase))
                return args[i + 1];
        return null;
    }

    public static void ScheduleHelperCleanup(string? path)
    {
        if (string.IsNullOrWhiteSpace(path)) return;
        _ = Task.Run(async () =>
        {
            await Task.Delay(TimeSpan.FromSeconds(2));
            for (int i = 0; i < 12; i++)
            {
                try
                {
                    if (!File.Exists(path)) return;
                    File.Delete(path);
                    return;
                }
                catch { await Task.Delay(750); }
            }
        });
    }

    public static void WriteHealthMarker(string? markerPath)
    {
        if (string.IsNullOrWhiteSpace(markerPath)) return;
        try
        {
            var directory = Path.GetDirectoryName(markerPath);
            if (!string.IsNullOrWhiteSpace(directory)) Directory.CreateDirectory(directory);
            File.WriteAllText(markerPath, AppVersion.Current, new UTF8Encoding(false));
        }
        catch { }
    }

    public static bool NeedsExecutableNameMigration(string? executablePath)
    {
        if (string.IsNullOrWhiteSpace(executablePath) || !File.Exists(executablePath)) return false;
        return Path.GetFileName(executablePath).Equals(UpdateEngine.LegacyExecutableName, StringComparison.OrdinalIgnoreCase);
    }

    public static void StartLegacyNameMigration(string executablePath)
    {
        string fullPath = Path.GetFullPath(executablePath);
        string targetPath = Path.Combine(Path.GetDirectoryName(fullPath)!, UpdateEngine.ExecutableName);
        string hash = HashFile(fullPath);
        StartHelper(new HelperRequest(
            fullPath, targetPath, fullPath, hash, new FileInfo(fullPath).Length,
            Environment.ProcessId, AppVersion.Current, AppVersion.Current, "migration",
            false, string.Empty, string.Empty));
    }

    public static void StartUpdate(
        string currentExecutable,
        string downloadedExecutable,
        string expectedSha256,
        long expectedSize,
        string version,
        string channel)
    {
        string currentPath = Path.GetFullPath(currentExecutable);
        string directory = Path.GetDirectoryName(currentPath)
            ?? throw new InvalidOperationException("Control Center klasörü belirlenemedi.");
        string targetPath = Path.Combine(directory, UpdateEngine.ExecutableName);
        string markerPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "TRPOLARIS", "ControlCenter", "updates",
            "health-" + Guid.NewGuid().ToString("N") + ".marker");

        StartHelper(new HelperRequest(
            Path.GetFullPath(downloadedExecutable),
            targetPath,
            currentPath,
            expectedSha256.ToLowerInvariant(),
            expectedSize,
            Environment.ProcessId,
            version,
            AppVersion.Current,
            channel,
            true,
            string.Empty,
            markerPath));
    }

    private static void StartHelper(HelperRequest request)
    {
        string currentExe = Environment.ProcessPath
            ?? Process.GetCurrentProcess().MainModule?.FileName
            ?? throw new InvalidOperationException("Çalışan Control Center EXE yolu bulunamadı.");
        string helperDirectory = Path.Combine(Path.GetTempPath(), "TRPOLARIS-ControlCenter", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(helperDirectory);
        string helperExe = Path.Combine(helperDirectory, "TRPOLARIS.UpdateHost.exe");
        File.Copy(currentExe, helperExe, overwrite: false);

        request = request with { HelperExecutablePath = helperExe };
        string encoded = Convert.ToBase64String(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(request)));
        var start = new ProcessStartInfo
        {
            FileName = helperExe,
            WorkingDirectory = helperDirectory,
            UseShellExecute = false,
            CreateNoWindow = true,
            WindowStyle = ProcessWindowStyle.Hidden
        };
        start.ArgumentList.Add(HelperSwitch);
        start.ArgumentList.Add(encoded);
        _ = Process.Start(start) ?? throw new InvalidOperationException("Yerel güncelleme yardımcısı başlatılamadı.");
        Application.Current.Shutdown();
    }

    public static async Task<int> RunHelperAsync(string[] args)
    {
        string logPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "TRPOLARIS", "ControlCenter", "logs", "native-updater.log");
        HelperRequest? request = null;
        try
        {
            byte[] json = Convert.FromBase64String(args[1]);
            request = JsonSerializer.Deserialize<HelperRequest>(Encoding.UTF8.GetString(json))
                ?? throw new InvalidDataException("Güncelleme yardımcısı isteği okunamadı.");
            WriteLog(logPath, $"Güncelleme yardımcısı başladı. Hedef={request.TargetPath}; Sürüm={request.Version}.");

            var deadline = DateTimeOffset.UtcNow + ParentExitTimeout;
            while (IsProcessRunning(request.ParentProcessId) && DateTimeOffset.UtcNow < deadline)
                await Task.Delay(300);
            if (IsProcessRunning(request.ParentProcessId))
                throw new TimeoutException("Ana uygulama kapanmadı; dosya değiştirilmedi.");

            if (!File.Exists(request.SourcePath))
                throw new FileNotFoundException("Doğrulanmış güncelleme kaynağı bulunamadı.", request.SourcePath);

            var sourceInfo = new FileInfo(request.SourcePath);
            if (request.ExpectedSize > 0 && sourceInfo.Length != request.ExpectedSize)
                throw new InvalidDataException("Güncelleme dosyasının boyutu beklenen değerle eşleşmiyor.");

            string sourceHash = HashFile(request.SourcePath);
            if (!sourceHash.Equals(request.ExpectedSha256, StringComparison.OrdinalIgnoreCase))
                throw new CryptographicException("Güncelleme yardımcısı SHA-256 doğrulamasını geçemedi.");

            string targetDirectory = Path.GetDirectoryName(request.TargetPath)
                ?? throw new InvalidOperationException("Hedef klasör belirlenemedi.");
            Directory.CreateDirectory(targetDirectory);

            // Stage on the target volume so the final rename stays within the same filesystem.
            string staged = Path.Combine(targetDirectory, "." + UpdateEngine.ExecutableName + "." + Guid.NewGuid().ToString("N") + ".staged");
            File.Copy(request.SourcePath, staged, overwrite: false);
            string stagedHash = HashFile(staged);
            if (!stagedHash.Equals(request.ExpectedSha256, StringComparison.OrdinalIgnoreCase))
            {
                TryDelete(staged);
                throw new CryptographicException("Hedef klasördeki hazırlık dosyasının SHA-256 değeri eşleşmiyor.");
            }

            // No .previous/.bak copy is created. The verified staged file atomically replaces the target.
            File.Move(staged, request.TargetPath, overwrite: true);
            if (!string.IsNullOrWhiteSpace(request.LegacyPath) &&
                !Path.GetFullPath(request.LegacyPath).Equals(Path.GetFullPath(request.TargetPath), StringComparison.OrdinalIgnoreCase))
                TryDelete(request.LegacyPath);

            if (request.DeleteSourceAfterApply &&
                !Path.GetFullPath(request.SourcePath).Equals(Path.GetFullPath(request.TargetPath), StringComparison.OrdinalIgnoreCase))
                TryDelete(request.SourcePath);

            WriteLog(logPath, $"Doğrulanan dosya yerleştirildi. SHA256={sourceHash}; hedef={request.TargetPath}.");
            var launch = new ProcessStartInfo
            {
                FileName = request.TargetPath,
                WorkingDirectory = targetDirectory,
                UseShellExecute = false
            };
            launch.ArgumentList.Add(HealthSwitch);
            launch.ArgumentList.Add(request.HealthMarkerPath);
            launch.ArgumentList.Add(CleanupSwitch);
            launch.ArgumentList.Add(request.HelperExecutablePath);
            _ = Process.Start(launch) ?? throw new InvalidOperationException("Yeni Control Center süreci başlatılamadı.");

            if (!string.IsNullOrWhiteSpace(request.HealthMarkerPath))
            {
                var healthDeadline = DateTimeOffset.UtcNow + HealthTimeout;
                while (DateTimeOffset.UtcNow < healthDeadline && !File.Exists(request.HealthMarkerPath))
                    await Task.Delay(300);

                if (File.Exists(request.HealthMarkerPath))
                {
                    string reportedVersion = File.ReadAllText(request.HealthMarkerPath).Trim();
                    if (reportedVersion.Equals(request.Version, StringComparison.OrdinalIgnoreCase))
                    {
                        UpdateEngine.RecordHistory(request.FromVersion, request.Version, request.Channel, "success", sourceHash);
                        WriteLog(logPath, $"Yeni sürüm açılış doğrulamasını tamamladı: v{reportedVersion}.");
                        TryDelete(request.HealthMarkerPath);
                        return 0;
                    }
                }

                UpdateEngine.RecordHistory(request.FromVersion, request.Version, request.Channel,
                    "installed-health-check-timeout", sourceHash);
                WriteLog(logPath, "Yeni EXE yerleştirildi ancak açılış doğrulaması zaman aşımına uğradı. Kullanıcı yedek oluşturulmasını istemediğinden otomatik geri yükleme yapılmadı.");
                return 2;
            }

            UpdateEngine.RecordHistory(request.FromVersion, request.Version, request.Channel, "migration", sourceHash);
            WriteLog(logPath, "Eski EXE adı yeni ürün adıyla değiştirildi.");
            return 0;
        }
        catch (Exception ex)
        {
            WriteLog(logPath, "GÜNCELLEME HATASI: " + ex);
            if (request is not null)
                UpdateEngine.RecordHistory(request.FromVersion, request.Version, request.Channel, "failed-before-replace", request.ExpectedSha256);
            return 1;
        }
    }

    private static bool IsProcessRunning(int processId)
    {
        try
        {
            using var process = Process.GetProcessById(processId);
            return !process.HasExited;
        }
        catch (ArgumentException) { return false; }
        catch (InvalidOperationException) { return false; }
    }

    private static string HashFile(string path)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        return Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
    }

    private static void WriteLog(string path, string message)
    {
        try
        {
            var directory = Path.GetDirectoryName(path);
            if (!string.IsNullOrWhiteSpace(directory)) Directory.CreateDirectory(directory);
            File.AppendAllText(path, $"[{DateTimeOffset.UtcNow:O}] {message}{Environment.NewLine}", new UTF8Encoding(false));
        }
        catch { }
    }

    private static void TryDelete(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); } catch { }
    }
}
