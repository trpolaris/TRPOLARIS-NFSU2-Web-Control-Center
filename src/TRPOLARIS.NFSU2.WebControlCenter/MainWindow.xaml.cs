using Microsoft.Win32;
using System.Collections.Concurrent;
using System.IO.Compression;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using Ellipse = System.Windows.Shapes.Ellipse;
using System.Windows.Threading;

namespace TRPOLARIS.NFSU2.WebControlCenter;

public partial class MainWindow : Window
{
    private const string ProductName = "TRPOLARIS NFSU2 Web Server Control Center";
    private const string InstallerResource = "TRPOLARIS.NFSU2.WebControlCenter.Resources.NFSU2-Windows-OneClick-Installer.ps1";
    private const string AccessResource = "TRPOLARIS.NFSU2.WebControlCenter.Resources.nfsu2-access.sh";
    private const string ProjectLookupResource = "TRPOLARIS.NFSU2.WebControlCenter.Resources.nfsu2-project-lookup.sh";
    private const string GitUpdateResource = "TRPOLARIS.NFSU2.WebControlCenter.Resources.nfsu2-git-update.sh";
    private const string GameArchiveDownloadUrl = "https://download1350.mediafire.com/01nkc48zrwhgwWqRkkGLes5UeMMf_Tml-ktVRBFnSowkLqRYN8iNcxYHjDVmfWnUAUPZ6Q9rMSeiw8_xnBhkz60Lxxi4HYLvN8EeDI-qxJOuR0tJACJimZVLuTLSJYTwH5hNd_Fsj5BXgQko1SK7CMkeA4AhmJIyU8QI1yJnFZ8/8is8o4zs7yzcreg/Need+For+Speed+Underground+2.zip";
    private const long MaxGameArchiveDownloadBytes = 20L * 1024 * 1024 * 1024;
    private const long SupportedGameExeSize = 4_800_512;
    private const string SupportedGameExeSha256 = "f9dd86c054878ce6276beb07c1fd61874f7a1e4bf1f241b084c65b73e24168a7";
    private const long MaxGameArchiveExpandedBytes = 20L * 1024 * 1024 * 1024;
    private const int MaxGameArchiveEntries = 150_000;
    private const string AppLogFolder = "TRPOLARIS\\NFSU2-Web-Control-Center";
    private static readonly string RootDir = @"C:\nfsu2";
    private static readonly string SafeStopRequestPath = Path.Combine(RootDir, "logs", ".nfsu2-safe-stop.request");
    private static readonly string LogsDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), AppLogFolder, "logs");
    private static readonly string RuntimeDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), AppLogFolder, "runtime");
    private static readonly string MainLog = Path.Combine(LogsDir, "manager.log");
    private static readonly string AppLog = Path.Combine(LogsDir, "app.log");
    private static readonly string WslLog = Path.Combine(LogsDir, "wsl.log");
    private static readonly string AppErrorsLog = Path.Combine(LogsDir, "app-errors.log");
    private static readonly string WslErrorsLog = Path.Combine(LogsDir, "wsl-errors.log");
    private static readonly object LogFileGate = new();
    private static readonly Regex LogErrorPattern = new(
        @"\b(error|failed|failure|denied|exception|fatal|not found|could not|cannot|timed out|unavailable)\b|başarısız|erişim engellendi|zaman aşım|bulunamadı|erişilemiyor",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(8) };
    private static readonly HttpClient GameDownloadHttp = new(new HttpClientHandler { AllowAutoRedirect = true, MaxAutomaticRedirections = 10 }) { Timeout = System.Threading.Timeout.InfiniteTimeSpan };
    private static readonly HttpClient LocalHttp = new(new HttpClientHandler { UseProxy = false }) { Timeout = TimeSpan.FromSeconds(8) };
    // LAN profile uses a self-signed cert. This client is used only for the local 127.0.0.1 port probe,
    // never for opening the URL in the user's browser or for public URLs.
    private static readonly HttpClient LanProbeHttp = new(new HttpClientHandler
    {
        UseProxy = false,
        ServerCertificateCustomValidationCallback = HttpClientHandler.DangerousAcceptAnyServerCertificateValidator
    });
    private readonly DispatcherTimer _statusTimer = new() { Interval = TimeSpan.FromSeconds(8) };
    private readonly DispatcherTimer _accessLogTimer = new() { Interval = TimeSpan.FromMilliseconds(650) };
    private readonly DispatcherTimer _logUiTimer = new() { Interval = TimeSpan.FromMilliseconds(100) };
    private readonly ConcurrentQueue<LogEntry> _pendingDiskLogs = new();
    private readonly ConcurrentQueue<LogEntry> _pendingUiLogs = new();
    private readonly object _diskLogFlushGate = new();
    private readonly object _logQueueGate = new();
    private System.Threading.Timer? _diskLogTimer;
    private volatile bool _logsVisible;
    private int _pendingUiLogCount;
    private long _droppedUiLogEntries;
    private int _liveLogCharCount;
    private const int LiveLogMaxChars = 500_000;
    private const int LiveLogBatchMaxChars = 64_000;
    private const int LiveLogBatchMaxEntries = 350;
    private const int LiveLogMaxPendingEntries = 20_000;
    private const int LiveLogMaxEntryChars = 16_000;
    private const int LiveLogMaxLineChars = 2_500;
    private readonly SemaphoreSlim _statusLock = new(1, 1);
    private readonly List<(TextBlock State, TextBlock Detail, Border Dot, string Kind)> _statusLabels = new();
    private TextBox? _liveLogBox;
    private TextBlock? _logsCurrentFileLabel;
    private string _logView = "ALL";
    private readonly List<(Button Button, string Key)> _logFilterButtons = new();
    private string _currentPage = "Dashboard";
    private bool _operationBusy;
    private bool _gameArchiveBusy;
    private CancellationTokenSource? _installCancellation;
    private Process? _activeInstallProcess;
    private Button? _safeStopOperationButton;
    private Button? _cancelOperationButton;
    private ProgressBar? _logsOperationProgressBar;
    private TextBlock? _logsOperationStatus;
    private TextBlock? _logsOperationStage;
    private TextBlock? _logsOperationPercent;
    private bool _safeStopRequested;
    private bool _forceCancelRequested;
    private string _accessLogPath = Path.Combine(RootDir, "logs", "nfsu2-access-mode.log");
    private long _accessLogOffset;
    private Process? _accessProcess;
    private TextBox? _portBox;
    private TextBox? _gameDirBox;
    private TextBlock? _gameValidationStatus;
    private ComboBox? _installAccessCombo;
    private TextBox? _lanIpBox;
    private TextBox? _lanPortBox;
    private TextBlock? _accessStateLabel;
    private TextBlock? _dashboardUrl;
    private TextBlock? _dashboardLan;
    private TextBlock? _dashboardPublic;
    private Button? _installButton;
    private TextBlock? _installState;
    private TextBlock? _serverProjectLabel;
    private TextBlock? _serverStateLabel;
    private ListBox? _projectFileList;
    private TextBox? _projectPathBox;
    private TextBlock? _projectFileExplorerStatus;
    private CheckBox? _autoApplyProjectChangesCheckBox;
    private bool _autoApplyProjectChanges = true;
    private string _projectCurrentRelativePath = string.Empty;
    private string? _projectUncRoot;
    private string? _projectWatcherRoot;
    private FileSystemWatcher? _projectWatcher;
    private DispatcherTimer? _projectChangeDebounceTimer;
    private readonly DispatcherTimer _projectSnapshotTimer = new() { Interval = TimeSpan.FromSeconds(6) };
    private bool _projectSnapshotPollBusy;
    private bool _projectSnapshotInitialized;
    private Dictionary<string, string> _projectSnapshot = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _pendingProjectChanges = new(StringComparer.OrdinalIgnoreCase);
    private string? _resolvedProjectDirectory;
    private string? _resolvedWslDistro;
    private string? _resolvedLinuxHome;
    private string? _resolvedLinuxUser;
    private int _operationProgressValue;
    private string _operationProgressLabel = "İşlem bekleniyor";
    private string _operationProgressState = "idle";
    private string _operationProgressQuality = "estimated";
    private string? _operationProgressErrorCode;
    private string? _lastOperationErrorCode;

    private sealed class OperationFailureException : InvalidOperationException
    {
        public string FailureCode { get; }
        public OperationFailureException(string message, string failureCode) : base(message) => FailureCode = failureCode;
    }

    private sealed record WslProjectLocation(string Distro, string ProjectDirectory, string LinuxHome, string LinuxUser);
    private sealed record GameFolderValidation(bool IsValid, string Message, string? GameDirectory);
    private sealed record GameImportResult(string TargetDirectory, string? BackupDirectory, long ExpandedBytes, int ExtractedFiles);
    private sealed record ProjectFileEntry(string Name, string RelativePath, bool IsDirectory, long Length, DateTime LastWriteUtc)
    {
        public override string ToString() => IsDirectory
            ? "[KLASÖR] " + Name
            : "[DOSYA]  " + Name + "    · " + FormatProjectFileSize(Length);
    }

    private sealed record LogEntry(string FileBlock, string UiBlock, string Category, string Level, bool IsError);
    private record ExecResult(int ExitCode, string Output);
    private sealed record WslDistroListResult(bool Succeeded, string[] Names, string Error);
    private record MachineStatus(bool WslPresent, bool WslListAvailable, bool UbuntuPresent, string WslDiagnostic, bool DockerReady, bool DockerAppOpen,
        string DockerVersion, string DockerClientVersion, string DockerDesktopVersion, string DockerContext, string ServerState, bool HttpReachable,
        string ActiveUrl, string AccessMode, string ProbeUrl, string LocalIp);

    public MainWindow()
    {
        InitializeComponent();
        try { Process.GetCurrentProcess().PriorityClass = ProcessPriorityClass.AboveNormal; } catch { }
        Directory.CreateDirectory(LogsDir);
        Directory.CreateDirectory(RuntimeDir);
        Loaded += MainWindow_Loaded;
        Closed += MainWindow_Closed;
        StateChanged += (_, _) => UpdateMaximizeButtonGlyph();
        UpdateMaximizeButtonGlyph();
        _statusTimer.Tick += async (_, _) => await RefreshStatusAsync(false);
        _accessLogTimer.Tick += (_, _) => ReadAccessLogTail();
        _logUiTimer.Tick += (_, _) => FlushVisibleLogBatch();
        _projectSnapshotTimer.Tick += async (_, _) => await PollProjectSnapshotAsync();
        _diskLogTimer = new System.Threading.Timer(_ => FlushPendingDiskLogs(), null, 150, 150);
        _logUiTimer.Start();
        ShowPage("Dashboard");
        AppendLog("Kontrol merkezi başlatıldı (v1.6.3). WSL, Docker ve NFSU2 sunucu durumu denetleniyor.", "OK");
    }

    private async void MainWindow_Loaded(object sender, RoutedEventArgs e)
    {
        await RefreshStatusAsync(true);
        _statusTimer.Start();
    }

    private void MainWindow_Closed(object? sender, EventArgs e)
    {
        StopProjectChangeMonitor();
        _logsVisible = false;
        _statusTimer.Stop();
        _projectSnapshotTimer.Stop();
        _accessLogTimer.Stop();
        _logUiTimer.Stop();
        // Stop the periodic writer, then drain any queued entries before closing.
        try
        {
            _diskLogTimer?.Change(System.Threading.Timeout.Infinite, System.Threading.Timeout.Infinite);
            FlushPendingDiskLogs();
            _diskLogTimer?.Dispose();
        }
        catch (Exception ex) { Trace.WriteLine("Log shutdown flush failed: " + ex); }
        // Do not kill a live install automatically: the user may close the UI while a long build is running.
    }

    private void BeginOperationProgress(string label)
    {
        _operationProgressValue = 0;
        _operationProgressLabel = label;
        _operationProgressState = "running";
        _operationProgressQuality = "estimated";
        _operationProgressErrorCode = null;
        _lastOperationErrorCode = null;
        _safeStopRequested = false;
        _forceCancelRequested = false;
        try { if (File.Exists(SafeStopRequestPath)) File.Delete(SafeStopRequestPath); } catch { }
        SetOperationProgress(0, label, true, "indeterminate");
        RefreshOperationControlButtons();
    }

    private void SetOperationProgress(int percent, string label, bool visible = true, string quality = "estimated")
    {
        if (_operationProgressState is not ("running" or "stop-requested")) return;
        percent = Math.Clamp(percent, 0, 100);
        _operationProgressValue = Math.Max(_operationProgressValue, percent);
        _operationProgressLabel = label;
        _operationProgressQuality = quality is "measured" or "indeterminate" ? quality : "estimated";
        void Apply()
        {
            if (_operationProgressState is not ("running" or "stop-requested")) return;
            bool stopPending = _operationProgressState == "stop-requested";
            var accent = BrushHex(stopPending ? "#F4C15D" : "#42C9C0");
            OperationProgressPanel.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
            OperationProgressPanel.Background = BrushHex(stopPending ? "#282114" : "#0D1A22");
            OperationProgressPanel.BorderBrush = BrushHex(stopPending ? "#755B2B" : "#25434A");
            OperationProgressBar.Foreground = accent;
            OperationProgressBar.IsIndeterminate = !stopPending && _operationProgressQuality == "indeterminate";
            OperationProgressBar.Value = _operationProgressValue;
            OperationProgressLabel.Text = label;
            OperationProgressStatus.Text = stopPending
                ? "DURDURMA İSTENDİ · GÜVENLİ NOKTA BEKLENİYOR"
                : _operationProgressQuality == "indeterminate" ? "ÇALIŞIYOR · AŞAMA YÜZDESİ BELİRSİZ"
                : _operationProgressQuality == "measured" ? "ÇALIŞIYOR · İNDİRME ÖLÇÜLÜYOR"
                : "ÇALIŞIYOR · AŞAMA İLERLEMESİ";
            OperationProgressStatus.Foreground = accent;
            OperationProgressPercent.Text = "~" + _operationProgressValue.ToString(CultureInfo.InvariantCulture) + "%";
            OperationProgressPercent.Foreground = accent;
            OperationProgressErrorCode.Text = stopPending ? "KOD: STOP_REQUESTED" : string.Empty;
            OperationProgressErrorCode.Visibility = stopPending ? Visibility.Visible : Visibility.Collapsed;
            OperationProgressErrorCode.Foreground = accent;
            OperationProgressHint.Text = stopPending
                ? "Güvenli durdurma isteği kaydedildi. Çalışan kritik komut tamamlanınca sonraki adımlar başlatılmayacak. Acil sonlandırma için İptal et kullanılabilir."
                : _operationProgressQuality switch
                {
                    "measured" => "İndirme yüzdesi alınan/toplam bayttan ölçülür. Üstteki toplam yüzde aşama ağırlıklı tahmindir.",
                    "indeterminate" => "Bu aşama sayısal yüzde sağlamıyor. Animasyon işlem gerçekten çalışırken sürer; sahte zaman yüzdesi kullanılmaz.",
                    _ => "Toplam yüzde tamamlanan işlem aşamalarına göre ilerler; belirsiz alt işlemler sahte yüzdeler üretmez."
                };
            UpdateLogsProgressUi(label, OperationProgressStatus.Text, _operationProgressValue, stopPending ? "#F4C15D" : "#42C9C0", !stopPending && _operationProgressQuality == "indeterminate");
        }
        if (Dispatcher.CheckAccess()) Apply(); else Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(Apply));
    }

    private void CompleteOperationProgress(bool success, string label, string? errorCode = null)
    {
        _operationProgressState = success ? "success" : "failed";
        _operationProgressLabel = label;
        if (success) _operationProgressQuality = "estimated";
        _operationProgressErrorCode = success ? null : (string.IsNullOrWhiteSpace(errorCode) ? "APP_ERROR" : errorCode);
        if (success) _operationProgressValue = 100;
        ApplyTerminalOperationProgress(label);
        RefreshOperationControlButtons();
    }

    private void CompleteOperationWarning(string label, string? warningCode = null)
    {
        _operationProgressState = "warning";
        _operationProgressLabel = label;
        _operationProgressErrorCode = warningCode;
        ApplyTerminalOperationProgress(label);
        RefreshOperationControlButtons();
    }

    private void StopOperationProgress(string label, string cancellationCode = "75")
    {
        _operationProgressState = "stopped";
        _operationProgressLabel = label;
        _operationProgressErrorCode = "SAFE_STOP / EXIT " + cancellationCode;
        ApplyTerminalOperationProgress(label);
        RefreshOperationControlButtons();
    }

    private void ApplyTerminalOperationProgress(string label)
    {
        string terminalState = _operationProgressState;
        _operationProgressLabel = label;
        void Apply()
        {
            if (_operationProgressState != terminalState) return;
            var (status, color, background, border) = terminalState switch
            {
                "success" => ("TAMAMLANDI", "#38D996", "#10231D", "#28654E"),
                "stopped" => ("DURDURULDU · UYARI", "#F4C15D", "#282114", "#755B2B"),
                "warning" => ("BEKLEMEDE · UYARI", "#F4C15D", "#282114", "#755B2B"),
                _ => ("İPTAL EDİLDİ / BAŞARISIZ · HATA", "#FF687D", "#2B151B", "#783340")
            };
            var brush = BrushHex(color);
            OperationProgressPanel.Visibility = Visibility.Visible;
            OperationProgressPanel.Background = BrushHex(background);
            OperationProgressPanel.BorderBrush = BrushHex(border);
            OperationProgressBar.IsIndeterminate = false;
            OperationProgressBar.Foreground = brush;
            OperationProgressBar.Value = _operationProgressValue;
            OperationProgressStatus.Text = status;
            OperationProgressStatus.Foreground = brush;
            OperationProgressLabel.Text = label;
            OperationProgressPercent.Text = terminalState == "success"
                ? "100%" : "~" + _operationProgressValue.ToString(CultureInfo.InvariantCulture) + "%";
            OperationProgressPercent.Foreground = brush;
            OperationProgressHint.Text = terminalState switch
            {
                "success" => "İşlem başarıyla tamamlandı.",
                "stopped" => "Güvenli durdurma isteği mevcut kritik adım tamamlandıktan sonra işlendi. Dosyalar, tamamlanan adımlar ve sunucu kayıtları korundu.",
                "warning" => "İşlem uyarıyla beklemede. Günlükte belirtilen eksik adım tamamlandığında devam edilebilir.",
                _ => "İşlem iptal edildi veya başarısız oldu. KOD alanındaki çıkış/HRESULT bilgisini ve canlı günlüğün son hata satırlarını incele."
            };
            bool hasCode = !string.IsNullOrWhiteSpace(_operationProgressErrorCode);
            OperationProgressErrorCode.Text = hasCode ? "KOD: " + _operationProgressErrorCode : string.Empty;
            OperationProgressErrorCode.Foreground = brush;
            OperationProgressErrorCode.Visibility = hasCode ? Visibility.Visible : Visibility.Collapsed;
            UpdateLogsProgressUi(label, status, _operationProgressValue, color, false);
        }
        if (Dispatcher.CheckAccess()) Apply(); else Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(Apply));
    }

    private void UpdateLogsProgressUi(string label, string status, int percent, string color, bool indeterminate)
    {
        if (_logsOperationStatus is not null)
        {
            _logsOperationStatus.Text = status;
            _logsOperationStatus.Foreground = BrushHex(color);
        }
        if (_logsOperationStage is not null) _logsOperationStage.Text = label;
        if (_logsOperationPercent is not null)
        {
            _logsOperationPercent.Text = _operationProgressState == "success" ? "100%" : "~" + percent.ToString(CultureInfo.InvariantCulture) + "%";
            _logsOperationPercent.Foreground = BrushHex(color);
        }
        if (_logsOperationProgressBar is not null)
        {
            _logsOperationProgressBar.IsIndeterminate = indeterminate;
            _logsOperationProgressBar.Foreground = BrushHex(color);
            _logsOperationProgressBar.Value = percent;
        }
    }

    private void RefreshOperationControlButtons()
    {
        bool progressIsActive = _operationProgressState is "running" or "stop-requested";
        bool installActive = progressIsActive && _installCancellation is not null && !_installCancellation.IsCancellationRequested;
        if (_safeStopOperationButton is not null)
        {
            _safeStopOperationButton.IsEnabled = installActive && !_safeStopRequested && !_forceCancelRequested;
            _safeStopOperationButton.Content = _safeStopRequested ? "Durdurma isteği gönderildi" : "Ⅱ  Güvenli durdur";
        }
        if (_cancelOperationButton is not null)
        {
            _cancelOperationButton.IsEnabled = installActive && !_forceCancelRequested;
            _cancelOperationButton.Content = _forceCancelRequested ? "İptal ediliyor…" : "×  Hemen iptal et";
        }
    }

    private void RequestSafeStopCurrentOperation()
    {
        if (_installCancellation is null || _installCancellation.IsCancellationRequested)
        {
            AppendLog("Güvenli durdurma için devam eden bir kurulum işlemi bulunmuyor.", "UYARI", "APP");
            RefreshOperationControlButtons();
            return;
        }
        if (_safeStopRequested) return;
        var answer = MessageBox.Show(this,
            "Kurulum hemen öldürülmeyecek. Çalışan paket kurulum/derleme/indirme adımı güvenli bir noktaya ulaştığında duracak. Bu sırada işlem günlüğü açık kalacak. Devam edilsin mi?",
            "Güvenli durdurma", MessageBoxButton.YesNo, MessageBoxImage.Warning);
        if (answer != MessageBoxResult.Yes) return;
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(SafeStopRequestPath)!);
            File.WriteAllText(SafeStopRequestPath, DateTimeOffset.Now.ToString("O", CultureInfo.InvariantCulture), new UTF8Encoding(false));
            _safeStopRequested = true;
            _operationProgressState = "stop-requested";
            _operationProgressErrorCode = "STOP_REQUESTED / WAITING_FOR_CHECKPOINT";
            SetOperationProgress(_operationProgressValue, "Güvenli durdurma istendi; çalışan kritik adımın tamamlanması bekleniyor", true, _operationProgressQuality);
            AppendLog("Kullanıcı güvenli durdurma istedi. Çalışan kritik komut tamamlanacak; sonraki kurulum aşamasına geçilmeyecek.", "UYARI", "WSL");
            RefreshOperationControlButtons();
        }
        catch (Exception ex)
        {
            AppendLog("Güvenli durdurma isteği kaydedilemedi: " + ex.Message, "HATA", "APP");
            CompleteOperationProgress(false, "Güvenli durdurma isteği kaydedilemedi", "STOP_REQUEST_WRITE_FAILED");
        }
    }

    private void CancelCurrentOperationImmediately()
    {
        var cancellation = _installCancellation;
        if (cancellation is null || cancellation.IsCancellationRequested)
        {
            AppendLog("İptal edilebilecek devam eden bir kurulum işlemi bulunmuyor.", "UYARI", "APP");
            RefreshOperationControlButtons();
            return;
        }
        var answer = MessageBox.Show(this,
            "Çalışan PowerShell/WSL işlemi ve alt süreçleri hemen sonlandırılacak. Paket kurulumu yarım kalabilir; yeniden başlatıldığında dpkg onarımı gerekebilir. Mevcut oyun dosyaları ve Docker kayıt hacmi otomatik silinmeyecek. Hemen iptal edilsin mi?",
            "Kurulumu hemen iptal et", MessageBoxButton.YesNo, MessageBoxImage.Error);
        if (answer != MessageBoxResult.Yes) return;
        _forceCancelRequested = true;
        _operationProgressState = "failed";
        _operationProgressLabel = "Kurulum kullanıcı tarafından hemen iptal edildi";
        _operationProgressErrorCode = "CANCELLED_BY_USER / EXIT 130";
        AppendLog("Kullanıcı kurulumu hemen iptal etti. Aktif işlem ağacı sonlandırılıyor. Kod: CANCELLED_BY_USER / EXIT 130.", "HATA", "WSL");
        try { cancellation.Cancel(); } catch (ObjectDisposedException) { }
        try
        {
            if (_activeInstallProcess is { HasExited: false } process) process.Kill(entireProcessTree: true);
        }
        catch (Exception ex) { AppendLog("Kurulum işlem ağacı sonlandırılamadı: " + ex.Message, "UYARI", "WSL"); }
        try
        {
            if (_accessProcess is { HasExited: false } accessProcess) accessProcess.Kill(entireProcessTree: true);
        }
        catch (Exception ex) { AppendLog("Erişim profili işlem ağacı sonlandırılamadı: " + ex.Message, "UYARI", "WSL"); }
        ApplyTerminalOperationProgress(_operationProgressLabel);
        RefreshOperationControlButtons();
    }

    private static string GetOperationFailureCode(Exception ex) => ex switch
    {
        OperationFailureException failure => failure.FailureCode,
        _ => "HRESULT 0x" + ex.HResult.ToString("X8", CultureInfo.InvariantCulture)
    };

    private void ParseProgressMarker(string line)
    {
        const string prefix = "[NFSU2-PROGRESS]|";
        if (!line.StartsWith(prefix, StringComparison.Ordinal)) return;
        var parts = line[prefix.Length..].Split('|', 3);
        if (parts.Length < 2 || !int.TryParse(parts[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out var percent)) return;
        string quality = parts.Length >= 3 ? parts[1] : "estimated";
        string label = parts.Length >= 3 ? parts[2] : parts[1];
        SetOperationProgress(percent, label, true, quality);
    }

    private void WindowTitleBar_MouseLeftButtonDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (e.ChangedButton != System.Windows.Input.MouseButton.Left) return;
        if (FindAncestorButton(e.OriginalSource as DependencyObject) is not null) return;
        if (e.ClickCount == 2) { ToggleWindowState(); return; }
        try { DragMove(); } catch (InvalidOperationException) { }
    }

    private static Button? FindAncestorButton(DependencyObject? node)
    {
        while (node is not null)
        {
            if (node is Button button) return button;
            if (node is Visual || node is System.Windows.Media.Media3D.Visual3D) node = VisualTreeHelper.GetParent(node);
            else node = LogicalTreeHelper.GetParent(node);
        }
        return null;
    }

    private void MinimizeWindow_Click(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;
    private void MaximizeWindow_Click(object sender, RoutedEventArgs e) => ToggleWindowState();
    private void CloseWindow_Click(object sender, RoutedEventArgs e) => Close();

    private void ToggleWindowState()
    {
        WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
        UpdateMaximizeButtonGlyph();
    }

    private void UpdateMaximizeButtonGlyph()
    {
        if (MaximizeWindowGlyph is not null)
        {
            // Use vector geometry so caption icons do not depend on optional symbol fonts.
            var geometry = WindowState == WindowState.Maximized
                ? "M4,1 H11 V8 M1,4 H8 V11 H1 Z"
                : "M2,2 H10 V10 H2 Z";
            MaximizeWindowGlyph.Data = System.Windows.Media.Geometry.Parse(geometry);
            MaximizeWindowButton.ToolTip = WindowState == WindowState.Maximized ? "Geri yükle" : "Büyüt";
        }
    }

    private void Navigation_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button button && button.Tag is string page) ShowPage(page);
    }

    private void ShowPage(string page)
    {
        _logsVisible = page == "Logs";
        if (page != "Logs")
        {
            _liveLogBox = null;
            _logsCurrentFileLabel = null;
        }
        _currentPage = page;
        PageHost.Margin = page == "Logs" ? new Thickness(0) : new Thickness(20, 18, 20, 16);
        _statusLabels.Clear();
        _portBox = null;
        _gameDirBox = null;
        _installAccessCombo = null;
        _lanIpBox = null;
        _lanPortBox = null;
        _accessStateLabel = null;
        _installButton = null;
        _installState = null;
        _safeStopOperationButton = null;
        _cancelOperationButton = null;
        _logsOperationProgressBar = null;
        _logsOperationStatus = null;
        _logsOperationStage = null;
        _logsOperationPercent = null;
        _serverProjectLabel = null;
        _serverStateLabel = null;
        _projectFileList = null;
        _projectPathBox = null;
        _projectFileExplorerStatus = null;
        _autoApplyProjectChangesCheckBox = null;
        switch (page)
        {
            case "Dashboard":
                TopEyebrow.Text = "SUNUCU YÖNETİMİ / GENEL BAKIŞ"; TopTitle.Text = "Kontrol Merkezi"; PageHost.Content = BuildDashboardPage(); break;
            case "Server":
                TopEyebrow.Text = "SUNUCU / YAŞAM DÖNGÜSÜ"; TopTitle.Text = "Sunucu Yönetimi"; PageHost.Content = BuildServerPage(); break;
            case "Files":
                TopEyebrow.Text = "SUNUCU / YAPILANDIRMA DOSYALARI"; TopTitle.Text = "Dosya Yöneticisi"; PageHost.Content = BuildServerFilesPage(); break;
            case "Install":
                TopEyebrow.Text = "DAĞITIM / YAPILANDIRMA"; TopTitle.Text = "Tek Tık Kurulum"; PageHost.Content = BuildInstallPage(); break;
            case "Access":
                TopEyebrow.Text = "AĞ / ERİŞİM PROFİLLERİ"; TopTitle.Text = "Erişim ve Paylaşım"; PageHost.Content = BuildAccessPage(); break;
            case "Diagnostics":
                TopEyebrow.Text = "SİSTEM / BAKIM"; TopTitle.Text = "Tanılama ve Bakım"; PageHost.Content = BuildDiagnosticsPage(); break;
            case "Uninstall":
                TopEyebrow.Text = "KALDIRMA / TEMİZLİK"; TopTitle.Text = "Kaldırma Merkezi"; PageHost.Content = BuildUninstallPage(); break;
            case "Logs":
                TopEyebrow.Text = "CANLI İŞLEM AKIŞI"; TopTitle.Text = "İşlem Günlüğü"; PageHost.Content = BuildLogsPage(); break;
            default:
                ShowPage("Dashboard"); return;
        }
        SetNavState(page);
        if (page == "Dashboard") UpdateAccessLabels();
        if (page == "Logs") ScrollLiveLogToEnd();
    }

    private void SetNavState(string selected)
    {
        var nav = new[] { NavDashboard, NavServer, NavFiles, NavInstall, NavAccess, NavDiagnostics, NavUninstall, NavLogs };
        foreach (var b in nav)
        {
            bool active = Equals(b.Tag, selected);
            b.Background = active ? BrushHex("#1A2B33") : Brushes.Transparent;
            b.BorderBrush = active ? BrushHex("#36535A") : Brushes.Transparent;
            b.Foreground = active ? BrushHex("#70D9D0") : BrushHex("#A7B5C1");
        }
    }

    private UIElement BuildDashboardPage()
    {
        var root = NewStack(12);
        root.Children.Add(Heading("Sunucu durumu", "Tarayıcıdan oynanan NFSU2 sunucusunun anlık durumu ve erişim bilgileri."));
        var hero = new Border
        {
            CornerRadius = new CornerRadius(10), Padding = new Thickness(17),
            BorderThickness = new Thickness(1), BorderBrush = BrushHex("#344550"),
            Background = new LinearGradientBrush(BrushHex("#1A2834").Color, BrushHex("#151F2A").Color, 0)
        };
        var heroGrid = new Grid();
        heroGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        heroGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var heroText = NewStack(6);
        heroText.Children.Add(MutedLabel("TRPOLARIS  /  NFSU2 WEB SERVER"));
        heroText.Children.Add(new TextBlock { Text = "Sunucunu tek merkezden yönet.", FontSize = 18, FontWeight = FontWeights.SemiBold, Foreground = BrushHex("#F1F3F4") });
        heroText.Children.Add(new TextBlock { Text = "Kurulum, erişim profilleri, yapılandırma dosyaları ve sunucu yaşam döngüsü aynı kontrol panelinde.", FontSize = 10, Foreground = BrushHex("#A2AFBA"), TextWrapping = TextWrapping.Wrap, MaxWidth = 620 });
        heroGrid.Children.Add(heroText);
        var heroActions = new StackPanel { Orientation = Orientation.Vertical, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(20, 0, 0, 0) };
        heroActions.Children.Add(MakeButton("Sunucu yönetimi", "ModernButton", (_, _) => ShowPage("Server")));
        heroActions.Children.Add(MakeButton("Yeni kurulum", "SecondaryButton", (_, _) => ShowPage("Install"), new Thickness(0, 6, 0, 0)));
        Grid.SetColumn(heroActions, 1); heroGrid.Children.Add(heroActions); hero.Child = heroGrid;
        root.Children.Add(hero);
        var statusGrid = new UniformGrid { Columns = 4, Rows = 1, Margin = new Thickness(0) };
        statusGrid.Children.Add(BuildStatusCard("WSL 2", "WSL", "Windows Linux altyapısı"));
        statusGrid.Children.Add(BuildStatusCard("Ubuntu", "Ubuntu", "Derleme dağıtımı"));
        statusGrid.Children.Add(BuildStatusCard("Docker Engine", "Docker", "Konteyner motoru"));
        statusGrid.Children.Add(BuildStatusCard("NFSU2 sunucusu", "Server", "Web sunucusu / oyun modülü"));
        root.Children.Add(statusGrid);

        var accessCard = MakeCard("ERİŞİM BİLGİLERİ", "Sunucu adresini aç, kopyala veya paylaşım profilini yönet.");
        var accessGrid = new Grid();
        accessGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.1, GridUnitType.Star) });
        accessGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(0.9, GridUnitType.Star) });
        var left = NewStack(8);
        left.Children.Add(MutedLabel("AKTİF SUNUCU ADRESİ"));
        _dashboardUrl = new TextBlock { Text = "http://localhost:9093/", FontSize = 17, FontWeight = FontWeights.SemiBold, Foreground = BrushHex("#4DD1C7"), TextWrapping = TextWrapping.Wrap };
        left.Children.Add(_dashboardUrl);
        var quick = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 7, 0, 0) };
        quick.Children.Add(MakeButton("Tarayıcıda aç", "ModernButton", OpenBrowser_Click));
        quick.Children.Add(MakeButton("Adresi kopyala", "SecondaryButton", (_, _) => { try { var url = GetActiveServerUrl(); Clipboard.SetText(url); AppendLog("Aktif sunucu adresi panoya kopyalandı: " + url, "OK"); } catch (Exception ex) { AppendLog("Adres kopyalanamadı: " + ex.Message, "HATA"); } }, new Thickness(10, 0, 0, 0)));
        quick.Children.Add(MakeButton("Erişim ayarları", "SecondaryButton", (_, _) => ShowPage("Access"), new Thickness(10, 0, 0, 0)));
        left.Children.Add(quick);
        Grid.SetColumn(left, 0); accessGrid.Children.Add(left);
        var right = NewStack(8); Grid.SetColumn(right, 1);
        right.Children.Add(MutedLabel("AKTİF LAN ADRESİ"));
        _dashboardLan = new TextBlock { Text = "Denetleniyor...", FontSize = 13, FontWeight = FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap };
        right.Children.Add(_dashboardLan);
        right.Children.Add(MutedLabel("PUBLIC TUNNEL / HTTPS"));
        _dashboardPublic = new TextBlock { Text = "Aktif değil", FontSize = 13, Foreground = (Brush)FindResource("MutedBrush"), TextWrapping = TextWrapping.Wrap };
        right.Children.Add(_dashboardPublic);
        right.Children.Add(MakeButton("Bağlantıları yenile", "SecondaryButton", async (_, _) => await RefreshStatusAsync(true), new Thickness(0, 6, 0, 0)));
        accessGrid.Children.Add(right); AddCardChild(accessCard, accessGrid); root.Children.Add(accessCard);

        var quickCard = MakeCard("HIZLI İŞLEMLER", "Sık kullanılan işlemler. Uzun görevler arka planda çalışır; aşamalar ilerledikçe toplam ilerleme göstergesi güncellenir.");
        var actions = new UniformGrid { Columns = 5 };
        actions.Children.Add(QuickAction("Sunucuyu yönet", "Sunucuyu başlat, durdur, yeniden başlat; günlüklerini izle ve GitHub sürümünü uygula.", "Sunucu Yönetimi", () => ShowPage("Server")));
        actions.Children.Add(QuickAction("Kurulumu başlat", "Kod deposunu hazırlar, bağımlılıkları kurar, oyun modülünü derler ve sunucuyu başlatır.", "Tek Tık Kurulum", () => ShowPage("Install")));
        actions.Children.Add(QuickAction("Sistem denetimi", "WSL, Ubuntu, Docker Engine, port ve oyun dosyalarını kontrol eder.", "Tanılama ve Bakım", () => ShowPage("Diagnostics")));
        actions.Children.Add(QuickAction("Dosyaları düzenle", "Compose, ortam, Docker ve Rust sunucu dosyalarını yedekleyerek düzenle.", "Dosya Yöneticisi", () => ShowPage("Files")));
        actions.Children.Add(QuickAction("Canlı günlük", "PowerShell ve WSL çıktılarını tek akışta izle.", "İşlem Günlüğü", () => ShowPage("Logs")));
        AddCardChild(quickCard, actions); root.Children.Add(quickCard);
        return WrapScrollable(root);
    }

    private Border BuildStatusCard(string title, string kind, string detail)
    {
        var card = new Border { Style = (Style)FindResource("CardBorder"), Margin = new Thickness(0, 0, 8, 0), Padding = new Thickness(12) };
        var stack = NewStack(6);
        stack.Children.Add(new TextBlock { Text = title, FontSize = 11, FontWeight = FontWeights.SemiBold, Foreground = (Brush)FindResource("MutedBrush") });
        var row = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        var dot = new Border { Width = 8, Height = 8, CornerRadius = new CornerRadius(4), Background = (Brush)FindResource("WarningBrush"), Margin = new Thickness(0, 0, 9, 0), VerticalAlignment = VerticalAlignment.Center };
        var state = new TextBlock { Text = "DENETLENİYOR", FontSize = 12, FontWeight = FontWeights.Bold, Foreground = (Brush)FindResource("WarningBrush") };
        row.Children.Add(dot); row.Children.Add(state); stack.Children.Add(row);
        var sub = new TextBlock { Text = detail, FontSize = 9, Foreground = (Brush)FindResource("MutedBrush"), TextWrapping = TextWrapping.Wrap };
        stack.Children.Add(sub); card.Child = stack;
        AddCardHover(card, "#141F2A", "#1B2B36");
        _statusLabels.Add((state, sub, dot, kind));
        return card;
    }

    private UIElement BuildInstallPage()
    {
        var root = NewStack(16);
        root.Children.Add(Heading("NFSU2 Web kurulumu", "Sunucu bileşenleri tek tıkla hazırlanır. Oyun dosyaları indirme bağlantısından veya bilgisayarındaki ZIP arşivinden alınır; doğrulama geçmeden sunucu kurulumu başlamaz."));
        var card = MakeCard("KURULUM AYARLARI", "Kurulum başlamadan önce oyun sürümü, SPEED2.EXE SHA-256 değeri ve temel oyun klasörleri hem Windows hem WSL tarafında doğrulanır. Doğrulama başarısızsa WSL/Docker adımları çalıştırılmaz.");
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.2, GridUnitType.Star) });
        AddGridRow(grid, 0, "HTTP portu", out var portCell);
        _portBox = new TextBox { Text = "9093", Width = 130, HorizontalAlignment = HorizontalAlignment.Left }; _portBox.SetResourceReference(FrameworkElement.StyleProperty, "DarkTextBox");
        portCell.Children.Add(_portBox);
        AddGridRow(grid, 1, "Oyun klasörü", out var gameCell);
        var gamePanel = NewStack(8);
        _gameDirBox = new TextBox { Text = Path.Combine(RootDir, "game"), MinWidth = 320, ToolTip = "SPEED2.EXE ve temel oyun klasörlerini içeren NFS Underground 2 kök klasörü" };
        _gameDirBox.SetResourceReference(FrameworkElement.StyleProperty, "DarkTextBox");
        _gameDirBox.LostFocus += (_, _) => RefreshGameFolderValidation();
        gamePanel.Children.Add(_gameDirBox);
        var gameActions = new WrapPanel();
        var downloadGameButton = MakeButton("Oyunu sunucudan indir ve otomatik yerleştir", "ModernButton", DownloadGameArchive_Click);
        downloadGameButton.ToolTip = GameArchiveDownloadUrl;
        gameActions.Children.Add(downloadGameButton);
        gameActions.Children.Add(MakeButton("Bilgisayardaki ZIP arşivini içe aktar", "SecondaryButton", ImportGameArchive_Click, new Thickness(8, 0, 0, 0)));
        gameActions.Children.Add(MakeButton("Klasörü doğrula", "SecondaryButton", VerifyGameFolder_Click, new Thickness(8, 0, 0, 0)));
        gamePanel.Children.Add(gameActions);
        _gameValidationStatus = new TextBlock { Text = "Oyun klasörü henüz doğrulanmadı.", Foreground = (Brush)FindResource("MutedBrush"), FontSize = 10, TextWrapping = TextWrapping.Wrap };
        gamePanel.Children.Add(_gameValidationStatus);
        gamePanel.Children.Add(new TextBlock { Text = "MediaFire indirme kaynağı:", Foreground = (Brush)FindResource("MutedBrush"), FontSize = 10, TextWrapping = TextWrapping.Wrap, MaxWidth = 680 });
        var sourceLinkText = new TextBlock { FontSize = 10, Margin = new Thickness(0, -6, 0, 0) };
        var sourceLink = new System.Windows.Documents.Hyperlink(new System.Windows.Documents.Run("NFS Underground 2 ZIP bağlantısını tarayıcıda aç"))
        {
            NavigateUri = new Uri(GameArchiveDownloadUrl),
            Foreground = BrushHex("#86C8FF")
        };
        sourceLink.RequestNavigate += (_, args) =>
        {
            try { Process.Start(new ProcessStartInfo(args.Uri.AbsoluteUri) { UseShellExecute = true }); }
            catch (Exception ex) { AppendLog("MediaFire bağlantısı açılamadı: " + ex.Message, "HATA"); }
            args.Handled = true;
        };
        sourceLinkText.Inlines.Add(sourceLink);
        gamePanel.Children.Add(sourceLinkText);
        gamePanel.Children.Add(new TextBlock
        {
            Text = "Arşivde desteklenen Kuzey Amerika 1.2 oyun sürümü aranır. ZIP önce geçici klasöre açılır; SPEED2.EXE boyutu / SHA-256 değeri ve CARS / FRONTEND / GLOBAL / TRACKS klasörleri doğrulanır. Yalnızca kontrol başarılıysa oyun C:\\nfsu2\\game konumuna taşınır; mevcut klasör gerekirse yedeklenir.",
            Foreground = (Brush)FindResource("MutedBrush"), FontSize = 10, TextWrapping = TextWrapping.Wrap, MaxWidth = 680
        });
        gameCell.Children.Add(gamePanel);
        AddGridRow(grid, 2, "Kurulum sonrası erişim", out var accessCell);
        _installAccessCombo = new ComboBox { Width = 340, HorizontalAlignment = HorizontalAlignment.Left, SelectedIndex = 0 };
        _installAccessCombo.SetResourceReference(FrameworkElement.StyleProperty, "DarkComboBox");
        _installAccessCombo.Items.Add(new ComboBoxItem { Content = "Yalnızca bu bilgisayar (Local)", Tag = "local" });
        _installAccessCombo.Items.Add(new ComboBoxItem { Content = "Yerel ağ (LAN / HTTPS)", Tag = "lan" });
        _installAccessCombo.Items.Add(new ComboBoxItem { Content = "Herkese açık geçici tünel", Tag = "public" });
        accessCell.Children.Add(_installAccessCombo);
        AddGridRow(grid, 3, "Bilgi", out var infoCell);
        infoCell.Children.Add(new TextBlock { Text = "WSL/Ubuntu, Docker Desktop ve .NET kurulumu ayrı bileşenlerdir. Uygulama bunları kaldırmadan kurulum yapar. Ubuntu ilk kurulumunda Linux hesabı oluşturman istenebilir.", TextWrapping = TextWrapping.Wrap, Foreground = (Brush)FindResource("MutedBrush"), FontSize = 11, MaxWidth = 650 });
        AddCardChild(card, grid);
        var actions = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 18, 0, 0) };
        _installButton = MakeButton("Kurulumu başlat", "ModernButton", Install_Click); actions.Children.Add(_installButton);
        actions.Children.Add(MakeButton("Sistem denetimi", "SecondaryButton", async (_, _) => await RunDiagnosticsAsync(), new Thickness(10, 0, 0, 0)));
        actions.Children.Add(MakeButton("Canlı günlüğü aç", "SecondaryButton", (_, _) => ShowPage("Logs"), new Thickness(10, 0, 0, 0)));
        AddCardChild(card, actions);
        _installState = new TextBlock { Text = "Hazır. Kurulum başlatılmadı.", Foreground = (Brush)FindResource("MutedBrush"), Margin = new Thickness(0, 14, 0, 0), TextWrapping = TextWrapping.Wrap };
        AddCardChild(card, _installState);
        root.Children.Add(card);
        return WrapScrollable(root);
    }

    private UIElement BuildServerPage()
    {
        var root = NewStack(16);
        root.Children.Add(Heading("NFSU2 sunucu yönetimi", "Kurulumdan sonra konteyneri, günlükleri ve güncellemeleri tek yerden yönet. Sunucu işlemleri diğer Docker projelerine dokunmaz."));

        var status = MakeCard("SUNUCU DURUMU", "Yalnızca doğrulanmış TRPOLARIS NFSU2 Compose projesi yönetilir.");
        _serverStateLabel = new TextBlock { Text = "Durum denetleniyor…", FontSize = 15, FontWeight = FontWeights.SemiBold, Foreground = (Brush)FindResource("MutedBrush"), TextWrapping = TextWrapping.Wrap };
        _serverProjectLabel = new TextBlock { Text = "WSL proje yolu henüz çözülmedi.", FontSize = 11, Foreground = (Brush)FindResource("MutedBrush"), TextWrapping = TextWrapping.Wrap };
        AddCardChild(status, _serverStateLabel); AddCardChild(status, _serverProjectLabel);
        var buttons = new WrapPanel { Margin = new Thickness(0, 8, 0, 0) };
        buttons.Children.Add(MakeButton("▶  Başlat", "ModernButton", async (_, _) => await ManageServerAsync("up -d")));
        buttons.Children.Add(MakeButton("■  Durdur", "DangerButton", async (_, _) => await ManageServerAsync("stop"), new Thickness(8, 0, 0, 0)));
        buttons.Children.Add(MakeButton("↻  Yeniden başlat", "SecondaryButton", async (_, _) => await ManageServerAsync("restart"), new Thickness(8, 0, 0, 0)));
        buttons.Children.Add(MakeButton("Durumu yenile", "SecondaryButton", async (_, _) => await RefreshServerPageAsync(), new Thickness(8, 0, 0, 0)));
        buttons.Children.Add(MakeButton("Tarayıcıda aç", "SecondaryButton", OpenBrowser_Click, new Thickness(8, 0, 0, 0)));
        AddCardChild(status, buttons);
        var logs = new WrapPanel { Margin = new Thickness(0, 8, 0, 0) };
        logs.Children.Add(MakeButton("Son sunucu günlüklerini getir", "SecondaryButton", async (_, _) => await ReadServerLogsAsync()));
        logs.Children.Add(MakeButton("Dosyaları düzenle", "SecondaryButton", (_, _) => ShowPage("Files"), new Thickness(8, 0, 0, 0)));
        AddCardChild(status, logs); root.Children.Add(status);

        var update = MakeCard("GITHUB GÜNCELLEMESİ", "main dalını indirir; Python/WASI araçlarını ve WASM oyun modülünü yeniler, Docker içinde React (npm ci) + Rust (Cargo.lock) sunucusunu derler ve sağlıklıysa containerı yeniden oluşturur. Oyun ve /state volume'leri korunur.");
        AddCardChild(update, new TextBlock { Text = "Güncelleme öncesi mevcut Docker image ve WASM modülü için geri dönüş kopyası alınır. Yerel Git değişiklikleri korunur; fast-forward mümkün değilse işlem durur.", FontSize = 11, Foreground = BrushHex("#E0B86C"), TextWrapping = TextWrapping.Wrap });
        AddCardChild(update, MakeButton("GitHub'dan güncelle ve yeniden derle", "ModernButton", async (_, _) => await UpdateServerAsync(), new Thickness(0, 8, 0, 0)));
        root.Children.Add(update);

        var paths = MakeCard("KURULUM BİLGİLERİ", "Son kurulum raporunda kaydedilen kaynak, port ve modül yolları.");
        var report = ReadTextFile(Path.Combine(RootDir, "NFSU2-SETUP-REPORT.txt"), "Kurulum raporu bulunamadı. Önce tek tık kurulumu tamamla.");
        AddCardChild(paths, new TextBlock { Text = report, FontFamily = new FontFamily("Cascadia Mono"), FontSize = 10, Foreground = BrushHex("#B8C5D2"), TextWrapping = TextWrapping.Wrap });
        root.Children.Add(paths);

        _ = RefreshServerPageAsync();
        return WrapScrollable(root);
    }

    private UIElement BuildServerFilesPage()
    {
        var root = NewStack(14);
        root.Children.Add(Heading("GitHub proje yöneticisi", "Kurulan NFSU2-Web deposunun tamamını FTP benzeri biçimde görüntüle. Frontend ve backend dosyalarını VS Code / Not Defteri ile aç; kaydedilen web kaynakları otomatik derlenip sunucuya uygulanabilir."));

        var location = MakeCard("SUNUCU PROJE KLASÖRÜ", "Bu klasör doğrudan WSL içindeki GitHub çalışma kopyasıdır. Windows üzerinden açılan dosyalar aynı depoya yazılır; ayrıca kopyalama gerekmez.");
        var pathRow = new DockPanel { LastChildFill = true };
        _projectPathBox = new TextBox { Text = "/", MinHeight = 34, Padding = new Thickness(9, 6, 9, 6), Background = BrushHex("#0F1923"), Foreground = BrushHex("#DDE5EC"), BorderBrush = BrushHex("#344550"), BorderThickness = new Thickness(1), VerticalContentAlignment = VerticalAlignment.Center };
        _projectPathBox.KeyDown += async (_, e) =>
        {
            if (e.Key != System.Windows.Input.Key.Enter) return;
            e.Handled = true;
            await RefreshProjectDirectoryAsync(_projectPathBox.Text);
        };
        var pathRefresh = MakeButton("GitHub yolunu yenile", "SecondaryButton", async (_, _) => await RefreshProjectDirectoryAsync(_projectCurrentRelativePath), new Thickness(8, 0, 0, 0));
        DockPanel.SetDock(pathRefresh, Dock.Right); pathRow.Children.Add(pathRefresh);
        pathRow.Children.Add(_projectPathBox);
        AddCardChild(location, pathRow);

        var toolbar = new WrapPanel { Orientation = Orientation.Horizontal };
        toolbar.Children.Add(MakeButton("↑ Üst klasör", "SecondaryButton", async (_, _) => await NavigateProjectParentAsync()));
        toolbar.Children.Add(MakeButton("Yenile", "SecondaryButton", async (_, _) => await RefreshProjectDirectoryAsync(_projectCurrentRelativePath), new Thickness(6, 0, 0, 0)));
        toolbar.Children.Add(MakeButton("VS Code ile projeyi aç", "ModernButton", async (_, _) => await OpenProjectInVsCodeAsync(), new Thickness(6, 0, 0, 0)));
        toolbar.Children.Add(MakeButton("Explorer'da aç", "SecondaryButton", async (_, _) => await OpenProjectInExplorerAsync(), new Thickness(6, 0, 0, 0)));
        AddCardChild(location, toolbar);

        _projectFileList = new ListBox
        {
            MinHeight = 310,
            MaxHeight = 470,
            Background = BrushHex("#0F1923"),
            Foreground = BrushHex("#DDE5EC"),
            BorderBrush = BrushHex("#344550"),
            BorderThickness = new Thickness(1),
            Padding = new Thickness(7),
            FontFamily = new FontFamily("Cascadia Code"),
            FontSize = 11,
            HorizontalContentAlignment = HorizontalAlignment.Stretch
        };
        _projectFileList.MouseDoubleClick += async (_, _) => await OpenSelectedProjectEntryAsync();
        AddCardChild(location, _projectFileList);

        var actions = new WrapPanel { Orientation = Orientation.Horizontal };
        actions.Children.Add(MakeButton("Dosya VS Code", "ModernButton", async (_, _) => await OpenSelectedProjectFileAsync("code")));
        actions.Children.Add(MakeButton("Dosya Not Defteri", "SecondaryButton", async (_, _) => await OpenSelectedProjectFileAsync("notepad"), new Thickness(6, 0, 0, 0)));
        actions.Children.Add(MakeButton("Dosyayı indir", "SecondaryButton", async (_, _) => await DownloadSelectedProjectFileAsync(), new Thickness(6, 0, 0, 0)));
        actions.Children.Add(MakeButton("Dosya yükle", "SecondaryButton", async (_, _) => await UploadProjectFilesAsync(), new Thickness(6, 0, 0, 0)));
        actions.Children.Add(MakeButton("Yeni klasör", "SecondaryButton", async (_, _) => await CreateProjectFolderAsync(), new Thickness(6, 0, 0, 0)));
        actions.Children.Add(MakeButton("Yeniden adlandır", "SecondaryButton", async (_, _) => await RenameSelectedProjectEntryAsync(), new Thickness(6, 0, 0, 0)));
        actions.Children.Add(MakeButton("Sil", "DangerButton", async (_, _) => await DeleteSelectedProjectEntryAsync(), new Thickness(6, 0, 0, 0)));
        AddCardChild(location, actions);

        _projectFileExplorerStatus = new TextBlock { Text = "GitHub proje yolu aranıyor…", FontSize = 11, Foreground = (Brush)FindResource("MutedBrush"), TextWrapping = TextWrapping.Wrap };
        AddCardChild(location, _projectFileExplorerStatus);
        root.Children.Add(location);

        var automation = MakeCard("KAYDET → OTOMATİK UYGULA", "VS Code veya Not Defteri ile düzenleyip kaydettiğinde web/ altındaki frontend ve backend kaynakları izlenir. Değişiklikler kısa bir beklemeden sonra Docker imajına alınır ve sunucu yeniden oluşturulur. /state kayıt hacmi korunur.");
        _autoApplyProjectChangesCheckBox = new CheckBox { Content = "Kaydedilen frontend/backend değişikliklerini otomatik derle ve sunucuya uygula", IsChecked = _autoApplyProjectChanges, Foreground = BrushHex("#DDE5EC"), FontSize = 11, VerticalContentAlignment = VerticalAlignment.Center };
        _autoApplyProjectChangesCheckBox.Checked += (_, _) => _autoApplyProjectChanges = true;
        _autoApplyProjectChangesCheckBox.Unchecked += (_, _) => _autoApplyProjectChanges = false;
        AddCardChild(automation, _autoApplyProjectChangesCheckBox);
        var applyRow = new WrapPanel { Orientation = Orientation.Horizontal };
        applyRow.Children.Add(MakeButton("Bekleyen değişiklikleri şimdi uygula", "ModernButton", async (_, _) => await ApplyPendingProjectChangesAsync(true)));
        applyRow.Children.Add(MakeButton("Değişiklik izleyicisini yeniden bağla", "SecondaryButton", async (_, _) => await RefreshProjectDirectoryAsync(_projectCurrentRelativePath), new Thickness(8, 0, 0, 0)));
        AddCardChild(automation, applyRow);
        root.Children.Add(automation);

        var safety = MakeCard("GÜVENLİK / ÇALIŞMA ŞEKLİ", "Dosyalar doğrudan WSL içindeki GitHub reposunda düzenlenir. Kaynak değişikliği sırasında eski konteyner derleme bitene kadar çalışmaya devam eder; Docker imajı başarıyla oluşmadan konteyner kaldırılmaz.");
        AddCardChild(safety, new TextBlock { Text = "Uyarı: .env.local bağlantı/yerel yol bilgileri içerebilir. .git klasörü dosya yöneticisinde gizlidir. node_modules, deps, gen, out, target ve önbellek klasörleri otomatik derleme izleyicisinden hariç tutulur. GitHub güncellemesi yerel değişiklikleri stash ile korumaya çalışır; çakışmalarda dosyalar elle uzlaştırılmalıdır.", FontSize = 11, Foreground = (Brush)FindResource("MutedBrush"), TextWrapping = TextWrapping.Wrap });
        root.Children.Add(safety);

        _projectChangeDebounceTimer ??= new DispatcherTimer { Interval = TimeSpan.FromSeconds(2.2) };
        _projectChangeDebounceTimer.Tick -= ProjectChangeDebounceTimer_Tick;
        _projectChangeDebounceTimer.Tick += ProjectChangeDebounceTimer_Tick;
        _ = RefreshProjectDirectoryAsync(_projectCurrentRelativePath);
        return WrapScrollable(root);
    }

    private static string FormatProjectFileSize(long bytes)
    {
        string[] units = { "B", "KB", "MB", "GB" };
        double value = Math.Max(0, bytes);
        int unit = 0;
        while (value >= 1024 && unit < units.Length - 1) { value /= 1024; unit++; }
        return value.ToString(unit == 0 ? "0" : "0.0", CultureInfo.InvariantCulture) + " " + units[unit];
    }

    // Shared size formatter used by the game archive download and extraction pipeline.
    // Keeps progress and validation messages readable for large archives.
    private static string FormatBytes(long bytes)
    {
        string[] units = { "B", "KB", "MB", "GB", "TB" };
        double value = Math.Max(0L, bytes);
        int unit = 0;
        while (value >= 1024d && unit < units.Length - 1)
        {
            value /= 1024d;
            unit++;
        }

        string format = unit == 0 ? "N0" : "N1";
        return value.ToString(format, CultureInfo.CurrentCulture) + " " + units[unit];
    }

    private static string NormalizeProjectRelativePath(string? relative)
    {
        string value = (relative ?? string.Empty).Trim().Replace('\\', '/').Trim('/');
        if (string.IsNullOrWhiteSpace(value)) return string.Empty;
        var parts = value.Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Any(x => x == "." || x == ".." || x.Contains(':') || x.Contains('\0') || x.Equals(".git", StringComparison.OrdinalIgnoreCase)))
            throw new InvalidOperationException("Proje dışına çıkan veya geçersiz bir dosya yolu kullanılamaz.");
        return string.Join("/", parts);
    }

    private static string GetProjectUncRoot(WslProjectLocation location, bool legacy = false)
    {
        string share = legacy ? @"\\wsl$\" : @"\\wsl.localhost\";
        return share + location.Distro + location.ProjectDirectory.Replace('/', '\\');
    }

    private string GetProjectUncPath(string relative)
    {
        if (string.IsNullOrWhiteSpace(_projectUncRoot)) throw new InvalidOperationException("GitHub proje klasörü henüz eşlenmedi.");
        relative = NormalizeProjectRelativePath(relative);
        string path = string.IsNullOrEmpty(relative) ? _projectUncRoot : Path.Combine(_projectUncRoot, relative.Replace('/', Path.DirectorySeparatorChar));
        string root = Path.GetFullPath(_projectUncRoot).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        string full = Path.GetFullPath(path);
        if (!full.Equals(root, StringComparison.OrdinalIgnoreCase) && !full.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Dosya yolu GitHub proje klasörünün dışına çıkamaz.");
        return full;
    }

    private async Task RefreshProjectDirectoryAsync(string? relativePath)
    {
        if (_projectFileList is null) return;
        try
        {
            var location = await ResolveWslProjectAsync(true);
            string root = GetProjectUncRoot(location);
            if (!Directory.Exists(root)) root = GetProjectUncRoot(location, true);
            if (!Directory.Exists(root))
                throw new DirectoryNotFoundException("WSL Git deposu bulundu ancak Windows dosya erişimi açılamadı. VS Code için Remote - WSL uzantısını kullan veya WSL UNC yolunu kontrol et: " + GetProjectUncRoot(location));
            if (!string.Equals(_projectUncRoot, root, StringComparison.OrdinalIgnoreCase))
            {
                _projectSnapshotInitialized = false;
                _projectSnapshot.Clear();
            }
            _projectUncRoot = root;
            EnsureProjectWatcher(root);
            await PollProjectSnapshotAsync(); // Establish the baseline before the user can open/edit listed files.
            string relative = NormalizeProjectRelativePath(relativePath);
            string directory = GetProjectUncPath(relative);
            if (!Directory.Exists(directory)) throw new DirectoryNotFoundException("Klasör bulunamadı: " + relative);
            _projectCurrentRelativePath = relative;
            if (_projectPathBox is not null) _projectPathBox.Text = "/" + relative;
            _projectFileList.Items.Clear();
            var entries = Directory.EnumerateFileSystemEntries(directory)
                .Where(path => !Path.GetFileName(path).Equals(".git", StringComparison.OrdinalIgnoreCase))
                .Where(path => !Path.GetFileName(path).Equals(".nfsu2-manager-backups", StringComparison.OrdinalIgnoreCase))
                .Select(path =>
                {
                    var attributes = File.GetAttributes(path);
                    bool isDirectory = attributes.HasFlag(FileAttributes.Directory);
                    long length = 0;
                    DateTime modified = DateTime.MinValue;
                    try { if (!isDirectory) length = new FileInfo(path).Length; modified = isDirectory ? Directory.GetLastWriteTimeUtc(path) : File.GetLastWriteTimeUtc(path); } catch { }
                    string name = Path.GetFileName(path);
                    string rel = string.IsNullOrEmpty(relative) ? name : relative + "/" + name;
                    return new ProjectFileEntry(name, rel, isDirectory, length, modified);
                })
                .OrderByDescending(x => x.IsDirectory)
                .ThenBy(x => x.Name, StringComparer.OrdinalIgnoreCase)
                .ToArray();
            foreach (var entry in entries) _projectFileList.Items.Add(entry);
            if (_projectFileExplorerStatus is not null)
                _projectFileExplorerStatus.Text = $"{location.Distro}:{location.ProjectDirectory}/{relative} · {entries.Length} öğe · Dosyalar doğrudan WSL deposunda";
            AppendLog($"GitHub proje klasörü açıldı: /{relative} ({entries.Length} öğe).", "OK", "WSL");
        }
        catch (Exception ex)
        {
            if (_projectFileExplorerStatus is not null) _projectFileExplorerStatus.Text = ex.Message;
            AppendLog("GitHub dosya yöneticisi klasörü açamadı: " + ex.Message, "HATA", "WSL");
        }
    }

    private async Task NavigateProjectParentAsync()
    {
        var parts = NormalizeProjectRelativePath(_projectCurrentRelativePath).Split('/', StringSplitOptions.RemoveEmptyEntries).ToList();
        if (parts.Count > 0) parts.RemoveAt(parts.Count - 1);
        await RefreshProjectDirectoryAsync(string.Join("/", parts));
    }

    private ProjectFileEntry? GetSelectedProjectEntry() => _projectFileList?.SelectedItem as ProjectFileEntry;

    private async Task OpenSelectedProjectEntryAsync()
    {
        var entry = GetSelectedProjectEntry();
        if (entry is null) return;
        if (entry.IsDirectory) { await RefreshProjectDirectoryAsync(entry.RelativePath); return; }
        await OpenSelectedProjectFileAsync("code");
    }

    private async Task OpenSelectedProjectFileAsync(string editor)
    {
        var entry = GetSelectedProjectEntry();
        if (entry is null || entry.IsDirectory) { if (_projectFileExplorerStatus is not null) _projectFileExplorerStatus.Text = "Önce bir dosya seç."; return; }
        try
        {
            var location = await ResolveWslProjectAsync(false);
            string unc = GetProjectUncPath(entry.RelativePath);
            if (!File.Exists(unc)) throw new FileNotFoundException("Seçilen dosya artık bulunmuyor.", unc);
            if (editor == "notepad")
            {
                Process.Start(new ProcessStartInfo { FileName = "notepad.exe", ArgumentList = { unc }, UseShellExecute = false });
            }
            else
            {
                string code = FindVsCodeExecutable() ?? throw new InvalidOperationException("VS Code bulunamadı. VS Code kurulumunu veya PATH içindeki code.exe yolunu kontrol et. Not Defteri seçeneğini de kullanabilirsin.");
                var psi = new ProcessStartInfo { FileName = code, UseShellExecute = false };
                psi.ArgumentList.Add("--remote");
                psi.ArgumentList.Add("wsl+" + location.Distro);
                psi.ArgumentList.Add(location.ProjectDirectory + "/" + entry.RelativePath);
                Process.Start(psi);
            }
            if (_projectFileExplorerStatus is not null) _projectFileExplorerStatus.Text = "Düzenleme açıldı: " + entry.RelativePath + " · Kaydettiğinde otomatik izleme devreye girer.";
            AppendLog("Proje dosyası dış editörde açıldı: " + entry.RelativePath, "OK", "WSL");
        }
        catch (Exception ex) { if (_projectFileExplorerStatus is not null) _projectFileExplorerStatus.Text = ex.Message; AppendLog("Dış editör açılamadı: " + ex.Message, "HATA", "APP"); }
    }

    private async Task OpenProjectInVsCodeAsync()
    {
        try
        {
            var location = await ResolveWslProjectAsync(false);
            string code = FindVsCodeExecutable() ?? throw new InvalidOperationException("VS Code bulunamadı. VS Code'u kurup code.exe yolunu PATH'e ekle.");
            var psi = new ProcessStartInfo { FileName = code, UseShellExecute = false };
            psi.ArgumentList.Add("--remote");
            psi.ArgumentList.Add("wsl+" + location.Distro);
            psi.ArgumentList.Add(location.ProjectDirectory);
            Process.Start(psi);
            AppendLog("GitHub deposu VS Code Remote - WSL ile açıldı: " + location.ProjectDirectory, "OK", "APP");
        }
        catch (Exception ex) { AppendLog("VS Code proje klasörü açılamadı: " + ex.Message, "HATA", "APP"); MessageBox.Show(this, ex.Message, "VS Code", MessageBoxButton.OK, MessageBoxImage.Warning); }
    }

    private async Task OpenProjectInExplorerAsync()
    {
        try
        {
            var location = await ResolveWslProjectAsync(false);
            string root = GetProjectUncRoot(location);
            if (!Directory.Exists(root)) root = GetProjectUncRoot(location, true);
            if (!Directory.Exists(root)) throw new DirectoryNotFoundException("WSL proje klasörü Windows tarafından açılamadı: " + root);
            Process.Start(new ProcessStartInfo { FileName = "explorer.exe", ArgumentList = { root }, UseShellExecute = false });
        }
        catch (Exception ex) { AppendLog("Windows dosya gezgini açılamadı: " + ex.Message, "HATA", "APP"); }
    }

    private static string? FindVsCodeExecutable()
    {
        var candidates = new List<string>
        {
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", "Microsoft VS Code", "Code.exe"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Microsoft VS Code", "Code.exe"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "Microsoft VS Code", "Code.exe")
        };
        string pathEnv = Environment.GetEnvironmentVariable("PATH") ?? string.Empty;
        foreach (var directory in pathEnv.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            candidates.Add(Path.Combine(directory.Trim('"'), "Code.exe"));
        return candidates.FirstOrDefault(File.Exists);
    }

    private async Task UploadProjectFilesAsync()
    {
        try
        {
            var dialog = new OpenFileDialog { Title = "GitHub projesine yüklenecek dosyaları seç", Multiselect = true, CheckFileExists = true };
            if (dialog.ShowDialog(this) != true) return;
            string targetDirectory = GetProjectUncPath(_projectCurrentRelativePath);
            foreach (string source in dialog.FileNames)
            {
                string target = Path.Combine(targetDirectory, Path.GetFileName(source));
                if (File.Exists(target) && MessageBox.Show(this, Path.GetFileName(source) + " zaten var. Üzerine yazılsın mı?", "Dosya yükleme", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) continue;
                File.Copy(source, target, true);
                AppendLog("Dosya GitHub çalışma kopyasına yüklendi: " + _projectCurrentRelativePath + "/" + Path.GetFileName(source), "OK", "APP");
            }
            await RefreshProjectDirectoryAsync(_projectCurrentRelativePath);
        }
        catch (Exception ex) { if (_projectFileExplorerStatus is not null) _projectFileExplorerStatus.Text = ex.Message; AppendLog("Dosya yüklenemedi: " + ex.Message, "HATA", "APP"); }
    }

    private async Task DownloadSelectedProjectFileAsync()
    {
        var entry = GetSelectedProjectEntry();
        if (entry is null || entry.IsDirectory) { if (_projectFileExplorerStatus is not null) _projectFileExplorerStatus.Text = "İndirmek için bir dosya seç."; return; }
        try
        {
            string source = GetProjectUncPath(entry.RelativePath);
            var dialog = new SaveFileDialog { FileName = entry.Name, Title = "Proje dosyasını bilgisayara indir", OverwritePrompt = true };
            if (dialog.ShowDialog(this) == true) { File.Copy(source, dialog.FileName, true); if (_projectFileExplorerStatus is not null) _projectFileExplorerStatus.Text = "İndirildi: " + dialog.FileName; }
        }
        catch (Exception ex) { AppendLog("Proje dosyası indirilemedi: " + ex.Message, "HATA", "APP"); }
        await Task.CompletedTask;
    }

    private async Task CreateProjectFolderAsync()
    {
        string? name = PromptForProjectName("Yeni klasör", "Klasör adı:", "yeni-klasor");
        if (string.IsNullOrWhiteSpace(name)) return;
        try
        {
            ValidateProjectLeafName(name);
            Directory.CreateDirectory(Path.Combine(GetProjectUncPath(_projectCurrentRelativePath), name));
            await RefreshProjectDirectoryAsync(_projectCurrentRelativePath);
        }
        catch (Exception ex) { AppendLog("Proje klasörü oluşturulamadı: " + ex.Message, "HATA", "APP"); }
    }

    private async Task RenameSelectedProjectEntryAsync()
    {
        var entry = GetSelectedProjectEntry();
        if (entry is null) return;
        string? name = PromptForProjectName("Yeniden adlandır", "Yeni ad:", entry.Name);
        if (string.IsNullOrWhiteSpace(name) || name == entry.Name) return;
        try
        {
            ValidateProjectLeafName(name);
            string source = GetProjectUncPath(entry.RelativePath);
            string target = Path.Combine(Path.GetDirectoryName(source)!, name);
            if (File.Exists(target) || Directory.Exists(target)) throw new IOException("Bu adla başka bir dosya veya klasör zaten var.");
            if (entry.IsDirectory) Directory.Move(source, target); else File.Move(source, target);
            await RefreshProjectDirectoryAsync(_projectCurrentRelativePath);
        }
        catch (Exception ex) { AppendLog("Proje öğesi yeniden adlandırılamadı: " + ex.Message, "HATA", "APP"); }
    }

    private async Task DeleteSelectedProjectEntryAsync()
    {
        var entry = GetSelectedProjectEntry();
        if (entry is null) return;
        if (entry.RelativePath.Equals(".git", StringComparison.OrdinalIgnoreCase) || entry.RelativePath.StartsWith(".git/", StringComparison.OrdinalIgnoreCase))
        { if (_projectFileExplorerStatus is not null) _projectFileExplorerStatus.Text = "Git metadata dosyaları silinemez."; return; }
        var answer = MessageBox.Show(this, "Aşağıdaki öğe GitHub çalışma kopyasından kalıcı olarak silinecek:\n\n" + entry.RelativePath + "\n\nDevam edilsin mi?", "Proje dosyasını sil", MessageBoxButton.YesNo, MessageBoxImage.Warning);
        if (answer != MessageBoxResult.Yes) return;
        try
        {
            string path = GetProjectUncPath(entry.RelativePath);
            if (entry.IsDirectory) Directory.Delete(path, true); else File.Delete(path);
            AppendLog("Proje öğesi silindi: " + entry.RelativePath, "UYARI", "APP");
            await RefreshProjectDirectoryAsync(_projectCurrentRelativePath);
        }
        catch (Exception ex) { AppendLog("Proje öğesi silinemedi: " + ex.Message, "HATA", "APP"); }
    }

    private static void ValidateProjectLeafName(string name)
    {
        if (string.IsNullOrWhiteSpace(name) || name is "." or ".." || name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 || name.Contains('/') || name.Contains('\\'))
            throw new InvalidOperationException("Geçersiz dosya veya klasör adı.");
        if (name.Equals(".git", StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException(".git klasörü bu dosya yöneticisinden oluşturulamaz veya değiştirilemez.");
    }

    private string? PromptForProjectName(string title, string prompt, string initial)
    {
        var dialog = new Window { Title = title, Owner = this, Width = 430, Height = 170, WindowStartupLocation = WindowStartupLocation.CenterOwner, ResizeMode = ResizeMode.NoResize, Background = BrushHex("#111B25"), Foreground = BrushHex("#E8EEF5") };
        var panel = new StackPanel { Margin = new Thickness(16) };
        panel.Children.Add(new TextBlock { Text = prompt, Margin = new Thickness(0, 0, 0, 8), Foreground = BrushHex("#DDE5EC") });
        var input = new TextBox { Text = initial, MinHeight = 32, Padding = new Thickness(8), Background = BrushHex("#0F1923"), Foreground = BrushHex("#DDE5EC") };
        panel.Children.Add(input);
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 12, 0, 0) };
        var ok = new Button { Content = "Tamam", Width = 85, Height = 30, IsDefault = true, Margin = new Thickness(0, 0, 8, 0) };
        var cancel = new Button { Content = "İptal", Width = 85, Height = 30, IsCancel = true };
        ok.Click += (_, _) => dialog.DialogResult = true;
        buttons.Children.Add(ok); buttons.Children.Add(cancel); panel.Children.Add(buttons); dialog.Content = panel;
        return dialog.ShowDialog() == true ? input.Text.Trim() : null;
    }

    private void EnsureProjectWatcher(string root)
    {
        if (!string.Equals(_projectWatcherRoot, root, StringComparison.OrdinalIgnoreCase) || _projectWatcher is null)
        {
            DisposeProjectWatcher();
            try
            {
                _projectWatcher = new FileSystemWatcher(root)
                {
                    IncludeSubdirectories = true,
                    Filter = "*",
                    NotifyFilter = NotifyFilters.FileName | NotifyFilters.DirectoryName | NotifyFilters.LastWrite | NotifyFilters.Size,
                    InternalBufferSize = 64 * 1024
                };
                _projectWatcher.Changed += ProjectWatcher_Changed;
                _projectWatcher.Created += ProjectWatcher_Changed;
                _projectWatcher.Deleted += ProjectWatcher_Changed;
                _projectWatcher.Renamed += ProjectWatcher_Renamed;
                _projectWatcher.Error += (_, e) => AppendLog("Proje değişiklik izleyicisi uyarısı: " + e.GetException().Message + ". WSL zaman damgası taraması yedek izleme olarak devam ediyor.", "UYARI", "APP");
                _projectWatcherRoot = root;
                _projectWatcher.EnableRaisingEvents = true;
                if (_projectFileExplorerStatus is not null) _projectFileExplorerStatus.Text += " · Dosya sistemi izleyicisi açık";
            }
            catch (Exception ex)
            {
                _projectWatcher = null; _projectWatcherRoot = null;
                AppendLog("WSL UNC dosya sistemi izleyicisi başlatılamadı: " + ex.Message + ". WSL zaman damgası taraması yedek izleme olarak kullanılacak.", "UYARI", "APP");
            }
        }
        if (!_projectSnapshotTimer.IsEnabled)
        {
            _projectSnapshotInitialized = false;
            _projectSnapshot.Clear();
            _projectSnapshotTimer.Start();
            if (_projectFileExplorerStatus is not null) _projectFileExplorerStatus.Text += " · WSL kaynak taraması 6 sn aralıkla etkin";
        }
    }

    private void ProjectWatcher_Changed(object sender, FileSystemEventArgs e) => QueueProjectChange(e.FullPath);
    private void ProjectWatcher_Renamed(object sender, RenamedEventArgs e) { QueueProjectChange(e.OldFullPath); QueueProjectChange(e.FullPath); }

    private void QueueProjectChange(string fullPath)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(_projectWatcherRoot)) return;
            string relative = Path.GetRelativePath(_projectWatcherRoot, fullPath).Replace('\\', '/');
            if (relative.StartsWith("../", StringComparison.Ordinal)) return;
            QueueProjectRelativeChange(relative);
        }
        catch (Exception ex) { Trace.WriteLine("Project watcher event failed: " + ex.Message); }
    }

    private void QueueProjectRelativeChange(string relative)
    {
        if (IsIgnoredProjectPath(relative) || !IsBuildRelevantProjectPath(relative)) return;
        Dispatcher.BeginInvoke(new Action(() =>
        {
            _pendingProjectChanges.Add(relative);
            if (_projectFileExplorerStatus is not null) _projectFileExplorerStatus.Text = "Kaydedilen değişiklik algılandı: " + relative + " · uygulama kuyruğunda";
            if (_projectChangeDebounceTimer is not null)
            {
                _projectChangeDebounceTimer.Stop();
                _projectChangeDebounceTimer.Start();
            }
        }));
    }

    private async Task PollProjectSnapshotAsync()
    {
        if (_projectSnapshotPollBusy || string.IsNullOrWhiteSpace(_resolvedProjectDirectory) || string.IsNullOrWhiteSpace(_resolvedWslDistro)) return;
        _projectSnapshotPollBusy = true;
        try
        {
            var location = new WslProjectLocation(_resolvedWslDistro!, _resolvedProjectDirectory!, _resolvedLinuxHome ?? "/root", _resolvedLinuxUser ?? "root");
            var wsl = FindWslExe();
            if (wsl is null) return;
            string scan = """
find web -type d \( -name .git -o -name .nfsu2-manager-backups -o -name node_modules -o -name .venv -o -name deps -o -name gen -o -name out -o -name target -o -name .cache -o -name dist -o -name build -o -name vendor -o -name __pycache__ -o -name .pytest_cache \) -prune -o -type f -printf '%p\t%T@\t%s\n' 2>/dev/null
for f in compose.local.yml .env.local requirements.txt pyproject.toml package.json package-lock.json Dockerfile .dockerignore; do
  if [[ -f "$f" ]]; then
    printf '%s\t%s\t%s\n' "$f" "$(stat -c '%y' "$f" 2>/dev/null || true)" "$(stat -c '%s' "$f" 2>/dev/null || true)"
  fi
done
""";
            string command = "cd " + QuoteBash(location.ProjectDirectory) + " && " + scan;
            var result = await RunCommandAsync(wsl, new[] { "-d", location.Distro, "-u", "root", "--", "env", "HOME=/root", "LC_ALL=C.UTF-8", "bash", "-lc", command }, TimeSpan.FromSeconds(25), RootDir, logOutput: false, logCategory: "WSL");
            if (result.ExitCode != 0) return;
            var current = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (string line in result.Output.Replace("\0", "").Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries))
            {
                string[] fields = line.Split(new[] { '\t' }, 3, StringSplitOptions.None);
                if (fields.Length != 3) continue;
                string relative = fields[0].Trim();
                if (relative.Length == 0 || relative.StartsWith("/", StringComparison.Ordinal) || IsIgnoredProjectPath(relative)) continue;
                current[relative] = fields[1] + "|" + fields[2];
            }
            if (!_projectSnapshotInitialized)
            {
                _projectSnapshot = current;
                _projectSnapshotInitialized = true;
                return;
            }
            foreach (var item in current)
            {
                if (!_projectSnapshot.TryGetValue(item.Key, out string? previous) || !string.Equals(previous, item.Value, StringComparison.Ordinal))
                    QueueProjectRelativeChange(item.Key);
            }
            foreach (string removed in _projectSnapshot.Keys.Except(current.Keys, StringComparer.OrdinalIgnoreCase))
                QueueProjectRelativeChange(removed);
            _projectSnapshot = current;
        }
        catch (Exception ex) { Trace.WriteLine("Project polling snapshot failed: " + ex.Message); }
        finally { _projectSnapshotPollBusy = false; }
    }

    private static bool IsIgnoredProjectPath(string relative)
    {
        var ignored = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        { ".git", ".nfsu2-manager-backups", "node_modules", ".venv", "deps", "gen", "out", "target", ".cache", "dist", "build", "vendor", "__pycache__", ".pytest_cache" };
        return relative.Split('/', StringSplitOptions.RemoveEmptyEntries).Any(ignored.Contains);
    }

    private static bool IsBuildRelevantProjectPath(string relative)
    {
        string normalized = relative.Replace('\\', '/');
        if (normalized.StartsWith("web/", StringComparison.OrdinalIgnoreCase)) return true;
        string name = Path.GetFileName(normalized);
        return name.Equals("compose.local.yml", StringComparison.OrdinalIgnoreCase)
            || name.Equals(".env.local", StringComparison.OrdinalIgnoreCase)
            || name.Equals("requirements.txt", StringComparison.OrdinalIgnoreCase)
            || name.Equals("pyproject.toml", StringComparison.OrdinalIgnoreCase)
            || name.Equals("package.json", StringComparison.OrdinalIgnoreCase)
            || name.Equals("package-lock.json", StringComparison.OrdinalIgnoreCase)
            || name.Equals("pnpm-lock.yaml", StringComparison.OrdinalIgnoreCase)
            || name.Equals("bun.lock", StringComparison.OrdinalIgnoreCase)
            || name.Equals("bun.lockb", StringComparison.OrdinalIgnoreCase)
            || name.Equals("Cargo.toml", StringComparison.OrdinalIgnoreCase)
            || name.Equals("Cargo.lock", StringComparison.OrdinalIgnoreCase)
            || name.Equals("Dockerfile", StringComparison.OrdinalIgnoreCase)
            || name.Equals(".dockerignore", StringComparison.OrdinalIgnoreCase);
    }

    private async void ProjectChangeDebounceTimer_Tick(object? sender, EventArgs e)
    {
        _projectChangeDebounceTimer?.Stop();
        await ApplyPendingProjectChangesAsync(false);
    }

    private async Task ApplyPendingProjectChangesAsync(bool force)
    {
        if (_pendingProjectChanges.Count == 0) { if (_projectFileExplorerStatus is not null) _projectFileExplorerStatus.Text = "Uygulanacak bekleyen kaynak değişikliği yok."; return; }
        if (!force && !_autoApplyProjectChanges)
        {
            if (_projectFileExplorerStatus is not null) _projectFileExplorerStatus.Text = "Kaynak değişiklikleri bekliyor. Otomatik uygulama kapalı; şimdi uygula düğmesini kullanabilirsin.";
            return;
        }
        if (_operationBusy)
        {
            _projectChangeDebounceTimer?.Stop();
            _projectChangeDebounceTimer?.Start();
            return;
        }
        var changes = _pendingProjectChanges.ToArray();
        _pendingProjectChanges.Clear();
        bool composeDefinitionChanged = changes.Any(x => x.Equals("compose.local.yml", StringComparison.OrdinalIgnoreCase));
        bool environmentChanged = changes.Any(x => x.Equals(".env.local", StringComparison.OrdinalIgnoreCase));
        bool composeChanged = composeDefinitionChanged || environmentChanged;
        bool needsBuild = composeDefinitionChanged || changes.Any(x => IsBuildRelevantProjectPath(x)
            && !x.Equals("compose.local.yml", StringComparison.OrdinalIgnoreCase)
            && !x.Equals(".env.local", StringComparison.OrdinalIgnoreCase));
        _operationBusy = true;
        BeginOperationProgress("Harici editördeki kaynak değişiklikleri uygulanıyor");
        try
        {
            var location = await ResolveWslProjectAsync(true);
            string projectRoot = _projectUncRoot ?? GetProjectUncRoot(location);
            if (!File.Exists(Path.Combine(projectRoot, "compose.local.yml")) || !File.Exists(Path.Combine(projectRoot, ".env.local")))
            {
                foreach (string changed in changes) _pendingProjectChanges.Add(changed);
                AppendLog("Dosya GitHub deposuna kaydedildi ancak kurulumun Compose ortamı henüz hazır değil. Değişiklikler kuyrukta korundu; kurulum tamamlanınca yeniden uygulanabilir.", "UYARI", "WSL");
                CompleteOperationWarning("Dosya kaydedildi; Ubuntu/kurulum hazır olduğunda sunucuya uygulanacak", "PENDING_SETUP");
                return;
            }
            string compose = "docker compose --env-file .env.local -f compose.local.yml";
            if (composeChanged)
            {
                SetOperationProgress(24, "Docker Compose ayarları doğrulanıyor");
                var validate = await RunWslProjectCommandAsync(compose + " config >/dev/null", TimeSpan.FromMinutes(2));
                if (validate.ExitCode != 0) throw new OperationFailureException("Compose yapılandırması hatalı. Kaynak dosyalar Git deposunda kayıtlı, ancak sunucuya uygulanmadı.", "DOCKER_COMPOSE_CONFIG_EXIT_" + validate.ExitCode.ToString(CultureInfo.InvariantCulture));
            }
            if (needsBuild)
            {
                SetOperationProgress(48, "Frontend/backend kaynaklarından Compose servis imajları derleniyor");
                var build = await RunWslProjectCommandAsync(compose + " build", TimeSpan.FromMinutes(60));
                if (build.ExitCode != 0) throw new OperationFailureException("Docker Compose imajları derlenemedi. Önceki çalışan konteyner korunuyor; son WSL günlüklerini kontrol et.", "DOCKER_BUILD_EXIT_" + build.ExitCode.ToString(CultureInfo.InvariantCulture));
            }
            SetOperationProgress(88, "Yeni sürüm sunucuya uygulanıyor; kayıt hacmi korunuyor");
            var up = await RunWslProjectCommandAsync(compose + " up -d --force-recreate", TimeSpan.FromMinutes(5));
            if (up.ExitCode != 0) throw new OperationFailureException("Yeni konteyner başlatılamadı. Docker Compose çıktısını kontrol et; /state hacmi silinmedi.", "DOCKER_UP_EXIT_" + up.ExitCode.ToString(CultureInfo.InvariantCulture));
            await RefreshStatusAsync(true);
            CompleteOperationProgress(true, "Kaydedilen kaynak değişiklikleri sunucuya uygulandı");
            AppendLog("Dış editörde kaydedilen değişiklikler uygulandı: " + string.Join(", ", changes.Take(12)) + (changes.Length > 12 ? $" ve {changes.Length - 12} dosya daha" : ""), "OK", "WSL");
            if (_projectFileExplorerStatus is not null) _projectFileExplorerStatus.Text = $"{changes.Length} değişiklik algılandı; Docker doğrulandı, imaj güncellendi ve sunucu yeniden oluşturuldu.";
        }
        catch (Exception ex)
        {
            foreach (string changed in changes) _pendingProjectChanges.Add(changed);
            CompleteOperationProgress(false, "Değişiklikler kaydedildi ancak sunucuya uygulanamadı; kuyrukta korundu", GetOperationFailureCode(ex));
            AppendLog("Otomatik kaynak güncellemesi başarısız: " + ex.Message, "HATA", "WSL");
            if (_projectFileExplorerStatus is not null) _projectFileExplorerStatus.Text = ex.Message;
            MessageBox.Show(this, ex.Message, "Otomatik sunucu güncellemesi", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        finally { _operationBusy = false; }
    }

    private void DisposeProjectWatcher()
    {
        try { if (_projectWatcher is not null) { _projectWatcher.EnableRaisingEvents = false; _projectWatcher.Dispose(); } } catch { }
        _projectWatcher = null; _projectWatcherRoot = null;
    }

    private void StopProjectChangeMonitor()
    {
        _projectChangeDebounceTimer?.Stop();
        _projectSnapshotTimer.Stop();
        DisposeProjectWatcher();
    }


    private static string[] ReadRegisteredWslDistrosFromRegistry()
    {
        var registered = new List<string>();
        try
        {
            using var root = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Lxss", writable: false);
            if (root is null) return Array.Empty<string>();
            foreach (string keyName in root.GetSubKeyNames())
            {
                try
                {
                    using var distroKey = root.OpenSubKey(keyName, writable: false);
                    var value = distroKey?.GetValue("DistributionName") as string;
                    if (!string.IsNullOrWhiteSpace(value)) registered.Add(value.Trim());
                }
                catch { /* An inaccessible WSL registry entry must not hide other registered distros. */ }
            }
        }
        catch (Exception ex)
        {
            Trace.WriteLine("Could not read WSL distro registry: " + ex.Message);
        }
        return registered.Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
    }

    private static string[] ParseWslDistroNames(string output)
    {
        return (output ?? string.Empty)
            .Replace("\0", string.Empty, StringComparison.Ordinal)
            .Replace("\uFEFF", string.Empty, StringComparison.Ordinal)
            .Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(x => x.Trim().TrimStart('*').Trim())
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Where(x => !x.Contains("Windows Subsystem for Linux", StringComparison.OrdinalIgnoreCase))
            .Where(x => !x.Contains("no installed distributions", StringComparison.OrdinalIgnoreCase))
            .Where(x => !x.Contains("yüklü dağıtım", StringComparison.OrdinalIgnoreCase))
            .Where(x => !x.StartsWith("Use 'wsl", StringComparison.OrdinalIgnoreCase))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private async Task<WslDistroListResult> ReadWslDistributionsAsync(string wslExe, bool log = false)
    {
        string lastError = "WSL dağıtım listesi alınamadı.";
        string[] registryNames = ReadRegisteredWslDistrosFromRegistry();
        for (int attempt = 1; attempt <= 2; attempt++)
        {
            var result = await RunCommandAsync(wslExe, new[] { "--list", "--quiet" },
                TimeSpan.FromSeconds(8), logOutput: false, logCategory: "WSL");
            var cliNames = ParseWslDistroNames(result.Output);
            var mergedNames = cliNames.Concat(registryNames)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();
            if (result.ExitCode == 0)
            {
                if (mergedNames.Length == 0 && log)
                    AppendLog("WSL komutu çalıştı ve kayıt defterinde de dağıtım yok. Ubuntu bu Windows kullanıcı hesabında kaldırılmış veya henüz kurulmamış.", "UYARI", "WSL");
                else if (registryNames.Length > 0 && !registryNames.All(x => cliNames.Contains(x, StringComparer.OrdinalIgnoreCase)) && log)
                    AppendLog("WSL listesi kayıt defteriyle tamamlandı; dağıtım adları Windows kayıtlarından da doğrulandı: " + string.Join(", ", mergedNames), "INFO", "WSL");
                return new WslDistroListResult(true, mergedNames, string.Empty);
            }

            // The CLI may explicitly report the same missing-distro code that WSL
            // returns for `wsl -d Ubuntu`. With no HKCU registrations this is a
            // confirmed empty distro set, not a transient service-list failure.
            if (registryNames.Length == 0 && result.Output.Contains("WSL_E_DISTRO_NOT_FOUND", StringComparison.OrdinalIgnoreCase))
            {
                if (log) AppendLog("WSL açık ancak istenen/kayıtlı Linux dağıtımı yok (WSL_E_DISTRO_NOT_FOUND). Ubuntu kaldırılmış veya henüz kurulmamış.", "UYARI", "WSL");
                return new WslDistroListResult(true, Array.Empty<string>(), string.Empty);
            }

            // A live WSL registration in HKCU is not proof that the WSL service is
            // currently responding. Keep this as a list/service error, not as "no distro".
            lastError = string.IsNullOrWhiteSpace(result.Output)
                ? $"wsl.exe --list --quiet çıkış kodu {result.ExitCode} döndürdü."
                : TailText(result.Output, 1800).Trim();
            if (attempt == 1) await Task.Delay(550);
        }

        if (log) AppendLog("WSL dağıtım listesi okunamadı; Ubuntu kaldırılmış olarak işaretlenmedi. Ayrıntı: " + lastError, "UYARI", "WSL");
        return new WslDistroListResult(false, Array.Empty<string>(), lastError);
    }

    private (string? Distro, string? ProjectDirectory) ReadInstallReportMetadata()
    {
        // Prefer the compact metadata file, but never let an incomplete/stale file
        // hide the full installer report. The report is written by the installer
        // before the first access-profile resolution.
        string? bestDistro = null;
        string? bestProject = null;
        foreach (string report in new[]
        {
            Path.Combine(RootDir, "NFSU2-PROJECT-PATH.txt"),
            Path.Combine(RootDir, "NFSU2-SETUP-REPORT.txt")
        })
        {
            if (!File.Exists(report)) continue;
            try
            {
                string[] lines = File.ReadAllLines(report, Encoding.UTF8);
                string? distroLine = lines.FirstOrDefault(x => x.TrimStart('\uFEFF').StartsWith("WSL distribution:", StringComparison.OrdinalIgnoreCase));
                string? projectLine = lines.FirstOrDefault(x => x.TrimStart('\uFEFF').StartsWith("Source folder (WSL):", StringComparison.OrdinalIgnoreCase));
                string? distro = distroLine is null ? null : distroLine.TrimStart('\uFEFF')["WSL distribution:".Length..].Trim();
                string? project = projectLine is null ? null : projectLine.TrimStart('\uFEFF')["Source folder (WSL):".Length..].Trim();
                if (!string.IsNullOrWhiteSpace(project))
                    return (string.IsNullOrWhiteSpace(distro) ? bestDistro : distro, project);
                if (bestDistro is null && !string.IsNullOrWhiteSpace(distro)) bestDistro = distro;
            }
            catch (Exception ex)
            {
                AppendLog("Kurulum yolu dosyası okunamadı; sonraki rapor kaynağı denenecek: " + ex.Message, "UYARI", "APP");
            }
        }
        return (bestDistro, bestProject);
    }

    private void PersistResolvedProjectMetadata(WslProjectLocation location)
    {
        try
        {
            Directory.CreateDirectory(RootDir);
            string metadata = string.Join(Environment.NewLine, new[]
            {
                "TRPOLARIS NFSU2 Web Control Center project metadata",
                "WSL distribution: " + location.Distro,
                "Linux user: " + location.LinuxUser,
                "Linux home: " + location.LinuxHome,
                "Source folder (WSL): " + location.ProjectDirectory,
                "GitHub: https://github.com/trpolaris/NFSU2-Web.git"
            }) + Environment.NewLine;
            File.WriteAllText(Path.Combine(RootDir, "NFSU2-PROJECT-PATH.txt"), metadata, new UTF8Encoding(false));
        }
        catch (Exception ex)
        {
            AppendLog("Doğrulanmış proje yolu kalıcı kaydedilemedi: " + ex.Message, "UYARI", "APP");
        }
    }

    private async Task<WslProjectLocation> ResolveWslProjectAsync(bool log)
    {
        var wsl = FindWslExe() ?? throw new OperationFailureException("Windows WSL bileşeni bulunamadı. Önce WSL özelliğini etkinleştir ve bilgisayarı yeniden başlat.", "WSL_NOT_INSTALLED");
        var list = await ReadWslDistributionsAsync(wsl, log);
        if (!list.Succeeded)
            throw new OperationFailureException("WSL dağıtım listesi okunamadı. Ubuntu eksik kabul edilmedi; WSL servisinin yanıt vermesini kontrol et. Ayrıntı: " + list.Error, "WSL_LIST_UNAVAILABLE");
        if (list.Names.Length == 0)
            throw new OperationFailureException("Bu Windows kullanıcı hesabında kayıtlı bir WSL dağıtımı yok. Ubuntu kaldırılmış veya henüz kurulmamış. Bu bir GitHub deposu hatası değil; Tek Tık Kurulum ile Ubuntu'yu yeniden kurmalısın.", "WSL_E_DISTRO_NOT_FOUND");

        var metadata = ReadInstallReportMetadata();
        string? preferredDistro = _resolvedWslDistro ?? metadata.Distro;
        var distro = !string.IsNullOrWhiteSpace(preferredDistro)
            ? list.Names.FirstOrDefault(x => x.Equals(preferredDistro, StringComparison.OrdinalIgnoreCase))
            : null;
        distro ??= list.Names.FirstOrDefault(x => x.StartsWith("Ubuntu", StringComparison.OrdinalIgnoreCase));
        if (string.IsNullOrWhiteSpace(distro))
            throw new OperationFailureException("WSL çalışıyor ancak bu kullanıcı hesabında Ubuntu dağıtımı kayıtlı değil. Kayıtlı dağıtımlar: " + string.Join(", ", list.Names) + ". NFSU2 derleme ortamı için Ubuntu'yu Tek Tık Kurulum üzerinden kur.", "WSL_E_DISTRO_NOT_FOUND");

        // Verify the selected distro itself before interpreting any Git/path result.
        // Registry entries can outlive an unregistered WSL distro, so the Windows
        // registry/list is discovery only; a real WSL launch is the final authority.
        // Probe the actual distro default user for identity and HOME. The subsequent
        // filesystem/Compose-label discovery runs read-only as root to inspect all owners.
        var distroProbe = await RunCommandAsync(wsl,
            new[] { "-d", distro, "--", "sh", "-lc", "printf 'UID=%s\\nHOME=%s\\n' \"$(id -u)\" \"$HOME\"" },
            TimeSpan.FromSeconds(20), logOutput: false, logCategory: "WSL");
        if (distroProbe.ExitCode != 0)
        {
            string probeDetail = TailText(distroProbe.Output, 1600).Trim();
            bool missingDistro = probeDetail.Contains("WSL_E_DISTRO_NOT_FOUND", StringComparison.OrdinalIgnoreCase)
                || probeDetail.Contains("there is no distribution with the supplied name", StringComparison.OrdinalIgnoreCase)
                || probeDetail.Contains("sağlanan ada sahip dağıtım yok", StringComparison.OrdinalIgnoreCase);
            string code = missingDistro
                ? "WSL_E_DISTRO_NOT_FOUND"
                : "WSL_DISTRO_START_EXIT_" + distroProbe.ExitCode.ToString(CultureInfo.InvariantCulture);
            string message = missingDistro
                ? $"'{distro}' dağıtımı kayıt listesinde görünmesine rağmen WSL tarafından başlatılamadı. Dağıtım kaldırılmış veya eski bir kayıt kalmış. Bu GitHub deposu hatası değildir; Ubuntu'yu Tek Tık Kurulum ile yeniden kur."
                : $"'{distro}' WSL dağıtımı kayıtlı ancak varsayılan Linux kullanıcısıyla başlatılamadı (kod {distroProbe.ExitCode}). GitHub proje klasörü henüz kontrol edilmedi. WSL günlüklerini incele. {probeDetail}";
            throw new OperationFailureException(message, code);
        }
        var probeLines = distroProbe.Output.Replace("\0", "", StringComparison.Ordinal)
            .Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        string? uidLine = probeLines.FirstOrDefault(x => x.StartsWith("UID=", StringComparison.Ordinal));
        string? homeLine = probeLines.FirstOrDefault(x => x.StartsWith("HOME=", StringComparison.Ordinal));
        if (uidLine is not null && uidLine.Equals("UID=0", StringComparison.Ordinal))
            throw new OperationFailureException("Ubuntu'nun varsayılan kullanıcısı hâlâ root. Önce normal Linux kullanıcısını varsayılan yap; dosya izinleri ve proje yolu bu kullanıcıya göre yönetiliyor.", "WSL_DEFAULT_USER_REQUIRED");

        string? expectedProject = _resolvedProjectDirectory ?? metadata.ProjectDirectory;
        // Discovery runs read-only as root so it can inspect both the normal user's HOME
        // and legacy root-owned checkouts. It also uses Docker Compose labels as a source
        // of truth when the already-running local server knows its working directory.
        // Keep the resolver in a real WSL-visible file. Passing a multi-line Bash program
        // as a Windows process argument caused truncated/mis-parsed helper definitions on
        // some WSL/PowerShell combinations (read_origin/normalize_remote: command not found).
        Directory.CreateDirectory(RootDir);
        var lookupScriptWinPath = Path.Combine(RootDir, "nfsu2-project-lookup.sh");
        ExtractResource(ProjectLookupResource, "nfsu2-project-lookup.sh", lookupScriptWinPath);
        var args = new List<string>
        {
            "-d", distro, "-u", "root", "--", "env",
            "HOME=/root", "LC_ALL=C.UTF-8",
            "NFSU2_DEFAULT_HOME=" + (homeLine is null ? "" : homeLine["HOME=".Length..]),
            "NFSU2_PROJECT_METADATA_FILE=/mnt/c/nfsu2/NFSU2-PROJECT-PATH.txt",
            "NFSU2_SETUP_REPORT_FILE=/mnt/c/nfsu2/NFSU2-SETUP-REPORT.txt",
            "DOCKER_CONFIG=/root/.config/nfsu2-control-center-docker"
        };
        if (!string.IsNullOrWhiteSpace(expectedProject))
            args.Add("NFSU2_EXPECTED_PROJECT_DIR=" + expectedProject);
        args.AddRange(new[] { "/bin/bash", "--noprofile", "--norc", "/mnt/c/nfsu2/nfsu2-project-lookup.sh" });
        AppendLog("WSL proje doğrulayıcısı dosya üzerinden çalıştırılıyor; kurulum raporu ve varsayılan HOME doğrudan kontrol edilecek.", "INFO", "WSL");
        var result = await RunCommandAsync(wsl, args, TimeSpan.FromSeconds(90), logOutput: true, logCategory: "WSL");
        if (result.Output.Contains("WSL_E_DISTRO_NOT_FOUND", StringComparison.OrdinalIgnoreCase))
            throw new OperationFailureException("Windows WSL kayıtlarında dağıtım adı görünmesine rağmen WSL bu dağıtımı açamadı. Ubuntu kaldırılmış veya kayıt defterinde eski bir kayıt kalmış. Tek Tık Kurulum ile Ubuntu'yu yeniden kur; bu GitHub deposu hatası değildir.", "WSL_E_DISTRO_NOT_FOUND");
        var location = ParseWslProjectLookup(result.Output);
        if (result.ExitCode != 0 || location is null)
        {
            string lookupOutput = result.Output;
            string detail = string.IsNullOrWhiteSpace(lookupOutput)
                ? $" WSL proje kontrolü çıkış kodu {result.ExitCode} ile tamamlandı ancak çıktı alınamadı. /bin/bash çağrısı, dağıtım başlangıcı ve C:\\nfsu2\\NFSU2-PROJECT-PATH.txt kontrol edilmeli."
                : " Ayrıntılı tanılama: " + TailText(lookupOutput, 7000).Trim();
            string userContext = string.Join("; ", new[] { uidLine, homeLine }.Where(x => !string.IsNullOrWhiteSpace(x)));
            if (!string.IsNullOrWhiteSpace(userContext)) detail = " Varsayılan kullanıcı: " + userContext + "." + detail;
            throw new OperationFailureException("Ubuntu ve varsayılan Linux kullanıcısı çalışıyor ancak NFSU2 proje çalışma kopyası doğrulanamadı. Önce kurulum manifestindeki tam yol ve HOME dizini doğrudan kontrol edildi; ardından Compose proje yolu etiketi ve sınırlı Linux kaynak dizinleri denetlendi. GitHub deposu bozuk kabul edilmedi. Eksik dosya, remote veya gerçek WSL komut hatası aşağıdaki tanılamada gösterilir." + detail,
                result.ExitCode is 0 ? "NFSU2_PROJECT_NOT_FOUND" : "WSL_PROJECT_LOOKUP_EXIT_" + result.ExitCode.ToString(CultureInfo.InvariantCulture));
        }
        _resolvedProjectDirectory = location.Value.ProjectDirectory;
        _resolvedWslDistro = distro;
        _resolvedLinuxHome = location.Value.LinuxHome;
        _resolvedLinuxUser = location.Value.LinuxUser;
        PersistResolvedProjectMetadata(new WslProjectLocation(distro, location.Value.ProjectDirectory, location.Value.LinuxHome, location.Value.LinuxUser));
        if (log) AppendLog($"GitHub remote adresi doğrulandı. Dağıtım={distro}; proje={location.Value.ProjectDirectory}; sahibi={location.Value.LinuxUser}", "OK", "WSL");
        if (_serverProjectLabel is not null) _serverProjectLabel.Text = "Ubuntu: " + distro + " · Proje: " + location.Value.ProjectDirectory;
        return new WslProjectLocation(distro, location.Value.ProjectDirectory, location.Value.LinuxHome, location.Value.LinuxUser);
    }

    private static (string ProjectDirectory, string LinuxHome, string LinuxUser)? ParseWslProjectLookup(string output)
    {
        const string marker = "NFSU2_PROJECT_B64=";
        var line = output.Replace("\0", "").Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .LastOrDefault(x => x.StartsWith(marker, StringComparison.Ordinal));
        if (line is null) return null;
        try
        {
            // Each field is encoded separately: Bash command substitution drops NUL bytes,
            // so a single NUL-delimited payload is not safe to capture through stdout.
            var encodedFields = line[marker.Length..].Split('.');
            if (encodedFields.Length != 3) return null;
            var fields = encodedFields.Select(x => Encoding.UTF8.GetString(Convert.FromBase64String(x))).ToArray();
            if (fields.Any(string.IsNullOrWhiteSpace)) return null;
            return (fields[0], fields[1], fields[2]);
        }
        catch (FormatException) { return null; }
    }

    private static string QuoteBash(string value) => "'" + value.Replace("'", "'\\''", StringComparison.Ordinal) + "'";

    private async Task<ExecResult> RunWslProjectCommandAsync(string command, TimeSpan? timeout = null, bool log = true)
    {
        var location = await ResolveWslProjectAsync(log);
        var wsl = FindWslExe() ?? throw new InvalidOperationException("wsl.exe bulunamadı.");
        string shell = """
source_config="${DOCKER_CONFIG:-/root/.docker}"
safe_config="/root/.config/nfsu2-control-center-docker"
mkdir -p -- /root/.config/nfsu2-control-center-docker
python3 - "$source_config/config.json" /root/.config/nfsu2-control-center-docker/config.json <<'PYDOCKERCFG'
import json, pathlib, sys
source, target = map(pathlib.Path, sys.argv[1:3])
data = {}
try:
    if source.is_file():
        data = json.loads(source.read_text(encoding="utf-8"))
except Exception:
    data = {}
data.pop("credsStore", None)
data.pop("credHelpers", None)
target.write_text(json.dumps(data, ensure_ascii=False) + "\n", encoding="utf-8")
PYDOCKERCFG
if [[ -d "$source_config/contexts" && ! -d "$safe_config/contexts" ]]; then cp -a "$source_config/contexts" "$safe_config/contexts"; fi
export DOCKER_CONFIG="$safe_config"
""" + "\ncd " + QuoteBash(location.ProjectDirectory) + " && " + command;
        return await RunCommandAsync(wsl, new[] { "-d", location.Distro, "-u", "root", "--", "env", "HOME=/root", "LC_ALL=C.UTF-8", "bash", "--noprofile", "--norc", "-c", shell }, timeout ?? TimeSpan.FromMinutes(3), RootDir, logOutput: log, logCategory: "WSL");
    }

    private async Task ManageServerAsync(string action)
    {
        if (_operationBusy) { AppendLog("Sunucu işlemi başlatılmadı: başka bir görev sürüyor.", "UYARI"); return; }
        var supported = new HashSet<string>(StringComparer.Ordinal) { "up -d", "stop", "restart" };
        if (!supported.Contains(action)) throw new InvalidOperationException("Desteklenmeyen sunucu işlemi.");
        _operationBusy = true; BeginOperationProgress("NFSU2 sunucusu: " + action);
        try
        {
            SetOperationProgress(28, "WSL içindeki doğrulanmış NFSU2 Compose projesi açılıyor");
            var result = await RunWslProjectCommandAsync("docker compose --env-file .env.local -f compose.local.yml " + action, TimeSpan.FromMinutes(3));
            if (result.ExitCode != 0) throw new OperationFailureException("Docker Compose işlemi başarısız oldu (kod " + result.ExitCode + ").", "DOCKER_COMPOSE_EXIT_" + result.ExitCode.ToString(CultureInfo.InvariantCulture));
            SetOperationProgress(82, "NFSU2 konteyner işlemi tamamlandı; sağlık durumu yenileniyor");
            AppendLog("Sunucu komutu tamamlandı: " + action, "OK", "WSL");
            await RefreshStatusAsync(true);
            if (_currentPage == "Server") await RefreshServerPageAsync();
            CompleteOperationProgress(true, "Sunucu işlemi ve durum kontrolü tamamlandı");
        }
        catch (Exception ex) { CompleteOperationProgress(false, "Sunucu işlemi hata ile durdu", GetOperationFailureCode(ex)); AppendLog("Sunucu işlemi başarısız: " + ex.Message, "HATA", "WSL"); MessageBox.Show(this, ex.Message, "Sunucu yönetimi", MessageBoxButton.OK, MessageBoxImage.Error); }
        finally { _operationBusy = false; }
    }

    private async Task ReadServerLogsAsync()
    {
        try
        {
            ShowPage("Logs"); AppendLog("=== NFSU2 SUNUCU GÜNLÜKLERİ ===", "INFO", "WSL");
            var result = await RunWslProjectCommandAsync("docker compose --env-file .env.local -f compose.local.yml logs --tail=250 server", TimeSpan.FromMinutes(2));
            if (result.ExitCode != 0) AppendLog("Sunucu günlükleri alınamadı (kod " + result.ExitCode + ").", "HATA", "WSL");
        }
        catch (Exception ex) { AppendLog("Sunucu günlükleri okunamadı: " + ex.Message, "HATA", "WSL"); }
    }

    private async Task RefreshServerPageAsync()
    {
        try
        {
            var location = await ResolveWslProjectAsync(false);
            if (_serverProjectLabel is not null) _serverProjectLabel.Text = "Ubuntu: " + location.Distro + " · Proje: " + location.ProjectDirectory;
            var result = await RunWslProjectCommandAsync("docker compose --env-file .env.local -f compose.local.yml ps --format json", TimeSpan.FromSeconds(30), false);
            string status = result.ExitCode == 0 && !string.IsNullOrWhiteSpace(result.Output)
                ? FormatComposeStatus(result.Output)
                : "Sunucu durumu alınamadı (kod " + result.ExitCode.ToString(CultureInfo.InvariantCulture) + "): " + TailText(result.Output, 1800).Trim();
            if (_serverStateLabel is not null) _serverStateLabel.Text = status;
        }
        catch (Exception ex) { if (_serverStateLabel is not null) _serverStateLabel.Text = "Sunucu/proje durumu bulunamadı: " + ex.Message; }
    }

    private static string FormatComposeStatus(string output)
    {
        var rows = new List<string>();
        var warnings = new List<string>();

        void AddContainer(JsonElement root)
        {
            if (root.ValueKind != JsonValueKind.Object) return;
            string Read(string key)
            {
                foreach (var property in root.EnumerateObject())
                {
                    if (!property.Name.Equals(key, StringComparison.OrdinalIgnoreCase)) continue;
                    var value = property.Value;
                    return value.ValueKind switch
                    {
                        JsonValueKind.String => value.GetString() ?? string.Empty,
                        JsonValueKind.Number => value.ToString(),
                        JsonValueKind.True => "true",
                        JsonValueKind.False => "false",
                        _ => string.Empty
                    };
                }
                return string.Empty;
            }

            string name = Read("Name");
            string service = Read("Service");
            string state = Read("State");
            string health = Read("Health");
            string exit = Read("ExitCode");
            string ports = Read("Ports");
            if (string.IsNullOrWhiteSpace(ports))
            {
                foreach (var property in root.EnumerateObject())
                {
                    if (!property.Name.Equals("Publishers", StringComparison.OrdinalIgnoreCase) || property.Value.ValueKind != JsonValueKind.Array) continue;
                    var mapped = new List<string>();
                    foreach (var publisher in property.Value.EnumerateArray())
                    {
                        string ReadPublisher(string key)
                        {
                            foreach (var p in publisher.EnumerateObject())
                                if (p.Name.Equals(key, StringComparison.OrdinalIgnoreCase)) return p.Value.ToString();
                            return string.Empty;
                        }
                        string target = ReadPublisher("TargetPort");
                        string published = ReadPublisher("PublishedPort");
                        string protocol = ReadPublisher("Protocol");
                        if (!string.IsNullOrWhiteSpace(published) && !string.IsNullOrWhiteSpace(target))
                            mapped.Add($"{published}->{target}/{(string.IsNullOrWhiteSpace(protocol) ? "tcp" : protocol)}");
                    }
                    ports = string.Join(", ", mapped);
                    break;
                }
            }

            rows.Add(string.Join("\n", new[]
            {
                string.IsNullOrWhiteSpace(name) ? "Konteyner" : "Konteyner: " + name,
                string.IsNullOrWhiteSpace(service) ? null : "Servis: " + service,
                string.IsNullOrWhiteSpace(state) ? null : "Durum: " + state,
                string.IsNullOrWhiteSpace(health) ? null : "Sağlık: " + health,
                string.IsNullOrWhiteSpace(exit) ? null : "Çıkış kodu: " + exit,
                string.IsNullOrWhiteSpace(ports) ? "Yayımlanan port: yok" : "Portlar: " + ports
            }.Where(x => x is not null)!));
        }

        var trimmed = output.Trim();
        if (string.IsNullOrWhiteSpace(trimmed)) return "NFSU2 Compose çıktısı boş.";
        try
        {
            using var document = JsonDocument.Parse(trimmed);
            if (document.RootElement.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in document.RootElement.EnumerateArray()) AddContainer(item);
            }
            else AddContainer(document.RootElement);
        }
        catch (JsonException)
        {
            foreach (var line in output.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                if (line.StartsWith("{", StringComparison.Ordinal))
                {
                    try
                    {
                        using var document = JsonDocument.Parse(line);
                        AddContainer(document.RootElement);
                        continue;
                    }
                    catch (JsonException) { warnings.Add("Compose JSON satırı okunamadı: " + TailText(line, 400)); continue; }
                }
                if (line.Contains("error", StringComparison.OrdinalIgnoreCase)
                    || line.Contains("cannot", StringComparison.OrdinalIgnoreCase)
                    || line.Contains("permission denied", StringComparison.OrdinalIgnoreCase))
                    warnings.Add(line);
            }
        }

        if (rows.Count == 0) return warnings.Count > 0
            ? string.Join("\n", warnings.Select(x => "WSL/Docker uyarısı: " + x))
            : TailText(output, 2500).Trim();
        if (warnings.Count > 0) rows.Add("WSL/Docker uyarısı: " + string.Join(" | ", warnings));
        return string.Join("\n\n", rows);
    }

    private async Task UpdateServerAsync()
    {
        if (_operationBusy) { AppendLog("Güncelleme başlatılmadı: başka bir işlem sürüyor.", "UYARI"); return; }
        var answer = MessageBox.Show(this, "GitHub main dalı indirilecek. WSL içindeki Python/WASI araçları ve bağımlılıkları güncellenecek; SPEED2.EXE mevcut oyun volume'ünden okunarak WASM modülü yeniden üretilecek. Docker içinde React npm paketleri ve Rust Cargo bağımlılıkları derlenecek. Yeni image ve modül sağlıklı olmazsa önceki sürüme geri dönülecek. Oyun dosyaları ile /state kayıt volume'ü korunur. Devam edilsin mi?", "NFSU2 güncellemesi", MessageBoxButton.YesNo, MessageBoxImage.Warning);
        if (answer != MessageBoxResult.Yes) return;
        _operationBusy = true; BeginOperationProgress("GitHub güncellemesi hazırlanıyor"); ShowPage("Logs");
        try
        {
            Directory.CreateDirectory(RootDir);
            Directory.CreateDirectory(Path.Combine(RootDir, "logs"));
            ExtractResource(GitUpdateResource, "nfsu2-git-update.sh", Path.Combine(RootDir, "nfsu2-git-update.sh"));
            SetOperationProgress(8, "GitHub main, WSL paketleri ve Docker bağlantısı doğrulanıyor");
            var pull = await RunWslProjectCommandAsync("bash /mnt/c/nfsu2/nfsu2-git-update.sh", TimeSpan.FromMinutes(90));
            if (pull.ExitCode != 0)
            {
                string detail = TailText(pull.Output, 4500).Trim();
                string code = "GITHUB_UPDATE_EXIT_" + pull.ExitCode.ToString(CultureInfo.InvariantCulture);
                throw new OperationFailureException("GitHub/WSL/WASM/React/Rust/Docker güncelleme zinciri tamamlanamadı (kod " + pull.ExitCode + "). Ayrıntı: " + detail + ". Yerel Git değişiklikleri stash içinde korunur; container durdurulduktan sonraki bir hata oluştuysa updater önceki image ve WASM modülünü geri yüklemeyi dener.", code);
            }
            if (!pull.Output.Contains("UPDATE_STATUS=SUCCESS", StringComparison.Ordinal))
                throw new OperationFailureException("Güncelleme betiği başarı onayı vermedi. WSL günlüğünü kontrol et; sunucu sağlık durumu doğrulanmadan işlem tamamlandı sayılmadı.", "UPDATE_SUCCESS_MARKER_MISSING");
            await RefreshStatusAsync(true);
            CompleteOperationProgress(true, "GitHub, Python/WASI, React, Rust, WASM ve Docker güncellemesi tamamlandı");
            AppendLog("GitHub main indirildi; Python/WASI bağımlılıkları ve WASM modülü yenilendi; Docker içinde npm ci + Rust Cargo derlemesi yapıldı. Yeni container healthy oldu. Oyun ve /state volume'leri korundu; geri dönüş image etiketi oluşturuldu.", "OK", "WSL");
            ShowPage("Server");
        }
        catch (Exception ex) { CompleteOperationProgress(false, "Güncelleme başarısız; mevcut veriler korundu", GetOperationFailureCode(ex)); AppendLog("NFSU2 güncellemesi başarısız: " + ex.Message, "HATA", "WSL"); MessageBox.Show(this, ex.Message, "Güncelleme hatası", MessageBoxButton.OK, MessageBoxImage.Error); }
        finally { _operationBusy = false; }
    }

    private UIElement BuildAccessPage()
    {
        var root = NewStack(16);
        root.Children.Add(Heading("Erişim ve paylaşım", "Yerel, yerel ağ ve public tünel profilleri. Public bağlantıyı bilen herkes erişebilir; kimlik doğrulaması otomatik eklenmez."));
        var card = MakeCard("ERİŞİM PROFİLİ", "Bir profil seç ve uygula. Public tünel geçicidir; uygulama ve sunucu çalışırken geçerlidir.");
        var warning = new Border { Background = BrushHex("#2B2021"), BorderBrush = BrushHex("#744447"), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(8), Padding = new Thickness(12), Margin = new Thickness(0, 0, 0, 16) };
        warning.Child = new TextBlock { Text = "Güvenlik: Public profil, internete geçici HTTPS adresi açar. Gizli oyun verilerini paylaşma; kalıcı üretim için erişim kontrolü ve kendi alan adına sahip bir proxy tercih et.", Foreground = BrushHex("#FFD7D7"), TextWrapping = TextWrapping.Wrap, FontSize = 11 };
        AddCardChild(card, warning);
        var grid = new Grid(); grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(170) }); grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        AddGridRow(grid, 0, "Sunucu HTTP portu", out var portCell); var port = new TextBox { Text = "9093", Width = 130, HorizontalAlignment = HorizontalAlignment.Left }; port.SetResourceReference(FrameworkElement.StyleProperty, "DarkTextBox"); portCell.Children.Add(port);
        AddGridRow(grid, 1, "LAN IPv4 adresi", out var ipCell); _lanIpBox = new TextBox { Text = GetPrimaryLanIPv4(), Width = 220, HorizontalAlignment = HorizontalAlignment.Left }; _lanIpBox.SetResourceReference(FrameworkElement.StyleProperty, "DarkTextBox"); ipCell.Children.Add(_lanIpBox);
        AddGridRow(grid, 2, "LAN HTTPS portu", out var lanPortCell); _lanPortBox = new TextBox { Text = FindAvailableLanPort().ToString(), Width = 130, HorizontalAlignment = HorizontalAlignment.Left }; _lanPortBox.SetResourceReference(FrameworkElement.StyleProperty, "DarkTextBox"); lanPortCell.Children.Add(_lanPortBox);
        AddCardChild(card, grid);
        var profiles = new UniformGrid { Columns = 3, Margin = new Thickness(0, 16, 0, 14) };
        profiles.Children.Add(ProfileCard("LOCAL", "Bu bilgisayardan erişim", "http://localhost:PORT/", "local"));
        profiles.Children.Add(ProfileCard("LAN / HTTPS", "Aynı ağdaki cihazlar", "https://IP:LAN_PORT/", "lan"));
        profiles.Children.Add(ProfileCard("PUBLIC", "Geçici Cloudflare URL", "https://…trycloudflare.com", "public"));
        AddCardChild(card, profiles);
        var row = new StackPanel { Orientation = Orientation.Horizontal };
        row.Children.Add(MakeButton("Local uygula", "SecondaryButton", async (_, _) => { await ApplyAccessProfileAsync("local", port.Text); }));
        row.Children.Add(MakeButton("LAN uygula", "ModernButton", async (_, _) => { await ApplyAccessProfileAsync("lan", port.Text); }, new Thickness(10, 0, 0, 0)));
        row.Children.Add(MakeButton("Public tünel başlat", "DangerButton", async (_, _) => { await ApplyAccessProfileAsync("public", port.Text); }, new Thickness(10, 0, 0, 0)));
        AddCardChild(card, row);
        _accessStateLabel = new TextBlock { Text = "Profil durumu kontrol ediliyor...", Margin = new Thickness(0, 16, 0, 0), Foreground = (Brush)FindResource("MutedBrush"), TextWrapping = TextWrapping.Wrap };
        AddCardChild(card, _accessStateLabel); root.Children.Add(card);
        var current = ReadTextFile(Path.Combine(RootDir, "nfsu2-access-mode.txt"), "Bilinmiyor").Trim();
        var currentUrl = ReadTextFile(Path.Combine(RootDir, "nfsu2-access-url.txt"), "").Trim();
        var active = MakeCard("AKTİF PROFİL", "Son kaydedilen erişim ayarı");
        AddCardChild(active, new TextBlock { Text = "Profil: " + current, FontSize = 16, FontWeight = FontWeights.SemiBold, Foreground = BrushHex("#4DD1C7") });
        AddCardChild(active, new TextBlock { Text = string.IsNullOrWhiteSpace(currentUrl) ? "URL kaydı bulunamadı." : currentUrl, Margin = new Thickness(0, 6, 0, 0), TextWrapping = TextWrapping.Wrap, Foreground = (Brush)FindResource("MutedBrush") });
        root.Children.Add(active); return WrapScrollable(root);
    }

    private UIElement ProfileCard(string title, string subtitle, string example, string mode)
    {
        var border = new Border { Style = (Style)FindResource("CardBorder"), Margin = new Thickness(0, 0, 8, 0), Padding = new Thickness(12) };
        var stack = NewStack(6); stack.Children.Add(new TextBlock { Text = title, FontSize = 12, FontWeight = FontWeights.Bold, Foreground = mode == "public" ? BrushHex("#FF8798") : BrushHex("#4DD1C7") });
        stack.Children.Add(new TextBlock { Text = subtitle, FontSize = 10, Foreground = (Brush)FindResource("MutedBrush"), TextWrapping = TextWrapping.Wrap });
        stack.Children.Add(new TextBlock { Text = example, FontSize = 9, Foreground = BrushHex("#B6C8E2"), TextWrapping = TextWrapping.Wrap });
        border.Child = stack; AddCardHover(border, "#111E32", "#182C46"); return border;
    }

    private UIElement BuildDiagnosticsPage()
    {
        var root = NewStack(16);
        root.Children.Add(Heading("Tanılama ve güvenli bakım", "İşlem çalışırken UI yanıt vermeye devam eder. Onarım başlamadan önce apt/dpkg etkinliği kontrol edilir."));
        var card = MakeCard("SİSTEM DENETİMİ", "WSL, Ubuntu, Docker Engine, port, oyun dosyası ve sunucu konteynerini kontrol et.");
        var buttons = new WrapPanel();
        buttons.Children.Add(MakeButton("Tam sistem denetimi", "ModernButton", async (_, _) => await RunDiagnosticsAsync()));
        buttons.Children.Add(MakeButton("WSL / Docker durumunu yenile", "SecondaryButton", async (_, _) => await RefreshStatusAsync(true), new Thickness(8, 0, 0, 0)));
        buttons.Children.Add(MakeButton("WSL bağlantısını kurtar", "SecondaryButton", async (_, _) => await RestartWslAsync(), new Thickness(8, 0, 0, 0)));
        buttons.Children.Add(MakeButton("Yerel sunucuyu aç", "SecondaryButton", OpenBrowser_Click, new Thickness(8, 0, 0, 0)));
        buttons.Children.Add(MakeButton("Günlüğü görüntüle", "SecondaryButton", (_, _) => ShowPage("Logs"), new Thickness(8, 0, 0, 0)));
        AddCardChild(card, buttons); root.Children.Add(card);
        var repair = MakeCard("GÜVENLİ ONARIM", "Sistem denetimi paket yöneticisi kilitli değilse yarım kalmış dpkg yapılandırmasını tamamlar ve apt-get check yapar. Canlı apt/dpkg işlemi varsa onarım durdurulur.");
        AddCardChild(repair, MakeButton("Paket durumunu güvenli onar", "SecondaryButton", async (_, _) => await SafeRepairAsync()));
        root.Children.Add(repair);
        var perf = MakeCard("PERFORMANS / KARARLILIK", "Uygulama I/O, WSL ve Docker kontrollerini arka planda yapar; uzun işlemlerde UI thread bloklanmaz. Sistem önceliği yükseltmek tek başına derlemeyi hızlandırmaz.");
        AddCardChild(perf, new TextBlock { Text = "Not: apt veya dpkg canlı çalışıyorsa yeni kurulum/onarım başlatma. Bu uygulama paket yöneticisi kilidini zorla silmez ve WSL/Docker dağıtımlarını otomatik sıfırlamaz.", Foreground = (Brush)FindResource("MutedBrush"), FontSize = 11, TextWrapping = TextWrapping.Wrap });
        root.Children.Add(perf); return WrapScrollable(root);
    }

    private UIElement BuildUninstallPage()
    {
        var root = NewStack(16); root.Children.Add(Heading("Kaldırma merkezi", "Yalnızca NFSU2'ye ait kaynaklar hedeflenir. Docker Desktop, Ubuntu/WSL ve başka projeler varsayılan olarak korunur."));
        var card = MakeCard("KALDIRILACAK KAYNAKLAR", "Kayıt hacmi oyun kayıtlarını barındırabilir. Hacmi silmeden önce ayrıca onay isteyeceğiz.");
        var removeContainers = new CheckBox { Content = "NFSU2 konteynerlerini durdur ve kaldır", IsChecked = true }; removeContainers.SetResourceReference(FrameworkElement.StyleProperty, "DarkCheckBox");
        var removeImages = new CheckBox { Content = "NFSU2 sunucu imajlarını kaldır", IsChecked = true }; removeImages.SetResourceReference(FrameworkElement.StyleProperty, "DarkCheckBox");
        var removeTunnel = new CheckBox { Content = "NFSU2 Local / Public tünel süreçlerini kapat ve erişim kaydını temizle", IsChecked = true }; removeTunnel.SetResourceReference(FrameworkElement.StyleProperty, "DarkCheckBox");
        var removeVolume = new CheckBox { Content = "NFSU2 kayıt hacmini sil (oyun kayıtları kaybolur)", IsChecked = false }; removeVolume.SetResourceReference(FrameworkElement.StyleProperty, "DarkCheckBox");
        var removeProject = new CheckBox { Content = "C:\\nfsu2 içindeki NFSU2 kaynak kodu, modül ve ayarları sil", IsChecked = false }; removeProject.SetResourceReference(FrameworkElement.StyleProperty, "DarkCheckBox");
        var removeGame = new CheckBox { Content = "C:\\nfsu2\\game içindeki oyun dosyalarını da sil", IsChecked = false }; removeGame.SetResourceReference(FrameworkElement.StyleProperty, "DarkCheckBox");
        var removeUbuntu = new CheckBox { Content = "Ubuntu WSL dağıtımını kaldır (Linux dosyaları kalıcı silinir)", IsChecked = false }; removeUbuntu.SetResourceReference(FrameworkElement.StyleProperty, "DarkCheckBox");
        var removeDockerDesktop = new CheckBox { Content = "Docker Desktop uygulamasını kaldır (diğer Docker projeleri etkilenir)", IsChecked = false }; removeDockerDesktop.SetResourceReference(FrameworkElement.StyleProperty, "DarkCheckBox");
        AddCardChild(card, removeContainers); AddCardChild(card, removeImages); AddCardChild(card, removeTunnel); AddCardChild(card, removeVolume); AddCardChild(card, removeProject); AddCardChild(card, removeGame); AddCardChild(card, removeUbuntu); AddCardChild(card, removeDockerDesktop);
        var warning = new TextBlock { Text = "Docker Desktop ve Ubuntu kaldırma seçenekleri varsayılan olarak kapalıdır. Bunları açarsan ek bir onay daha istenir; başka Docker projeleri hedeflenmez.", Foreground = BrushHex("#F8C45C"), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 8, 0, 12), FontSize = 11 };
        AddCardChild(card, warning);
        AddCardChild(card, MakeButton("Seçilen bileşenleri kaldır", "DangerButton", async (_, _) => await UninstallAsync(removeContainers.IsChecked == true, removeImages.IsChecked == true, removeTunnel.IsChecked == true, removeVolume.IsChecked == true, removeProject.IsChecked == true, removeGame.IsChecked == true, removeUbuntu.IsChecked == true, removeDockerDesktop.IsChecked == true)));
        root.Children.Add(card); return WrapScrollable(root);
    }

    private UIElement BuildLogsPage()
    {
        // The page heading already lives in the application shell. Keep this view focused
        // on operation state, log controls, and the live terminal rather than repeating it.
        var root = new Grid { Margin = new Thickness(16, 10, 16, 10) };
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

        // Compact operation strip: status, measured progress, and only the two process controls.
        var operationCard = new Border
        {
            Background = BrushHex("#131F2A"),
            BorderBrush = BrushHex("#2D414F"),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(13),
            Margin = new Thickness(0, 0, 0, 10)
        };
        var operationGrid = new Grid();
        operationGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.25, GridUnitType.Star) });
        operationGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.35, GridUnitType.Star) });
        operationGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var statusPanel = new StackPanel { Orientation = Orientation.Vertical, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 16, 0) };
        statusPanel.Children.Add(new TextBlock
        {
            Text = "CANLI İŞLEM MERKEZİ", FontSize = 9, FontWeight = FontWeights.Bold,
            Foreground = BrushHex("#63CEC5"), Margin = new Thickness(0, 0, 0, 5)
        });
        _logsOperationStatus = new TextBlock
        {
            Text = GetProgressStatusText(), FontSize = 11, FontWeight = FontWeights.SemiBold,
            Foreground = BrushHex(GetProgressColor()), TextWrapping = TextWrapping.Wrap
        };
        statusPanel.Children.Add(_logsOperationStatus);
        _logsOperationStage = new TextBlock
        {
            Text = _operationProgressLabel, FontSize = 10, Foreground = BrushHex("#9EAFBC"),
            TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 4, 0, 0)
        };
        statusPanel.Children.Add(_logsOperationStage);
        operationGrid.Children.Add(statusPanel);

        var progressPanel = new StackPanel { Orientation = Orientation.Vertical, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(10, 0, 18, 0) };
        var progressHeader = new Grid { Margin = new Thickness(0, 0, 0, 7) };
        progressHeader.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        progressHeader.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        progressHeader.Children.Add(new TextBlock
        {
            Text = "İŞLEM İLERLEMESİ", FontSize = 9, FontWeight = FontWeights.Bold,
            Foreground = BrushHex("#91A3B1"), VerticalAlignment = VerticalAlignment.Center
        });
        _logsOperationPercent = new TextBlock
        {
            Text = GetProgressPercentText(), FontSize = 10, FontWeight = FontWeights.Bold,
            Foreground = BrushHex(GetProgressColor()), VerticalAlignment = VerticalAlignment.Center
        };
        Grid.SetColumn(_logsOperationPercent, 1);
        progressHeader.Children.Add(_logsOperationPercent);
        progressPanel.Children.Add(progressHeader);
        _logsOperationProgressBar = new ProgressBar
        {
            Minimum = 0, Maximum = 100, Value = _operationProgressValue, Height = 7,
            BorderThickness = new Thickness(0), Background = BrushHex("#263746"),
            Foreground = BrushHex(GetProgressColor()),
            IsIndeterminate = (_operationProgressState == "running" || _operationProgressState == "stop-requested") && _operationProgressQuality == "indeterminate"
        };
        progressPanel.Children.Add(_logsOperationProgressBar);
        Grid.SetColumn(progressPanel, 1);
        operationGrid.Children.Add(progressPanel);

        var processControls = new WrapPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        _safeStopOperationButton = MakeButton("Ⅱ  Güvenli durdur", "SecondaryButton", (_, _) => RequestSafeStopCurrentOperation());
        _safeStopOperationButton.Foreground = BrushHex("#E7BD67");
        _safeStopOperationButton.MinWidth = 116;
        _safeStopOperationButton.MinHeight = 32;
        _safeStopOperationButton.ToolTip = "Devam eden kritik komutun tamamlanmasını bekleyerek kurulumu güvenli biçimde durdurur.";
        _cancelOperationButton = MakeButton("×  Hemen iptal et", "DangerButton", (_, _) => CancelCurrentOperationImmediately(), new Thickness(7, 0, 0, 0));
        _cancelOperationButton.MinWidth = 112;
        _cancelOperationButton.MinHeight = 32;
        _cancelOperationButton.ToolTip = "PowerShell / WSL işlem ağacını hemen sonlandırır. Devam eden işlemde veri kaybına yol açabilir.";
        processControls.Children.Add(_safeStopOperationButton);
        processControls.Children.Add(_cancelOperationButton);
        Grid.SetColumn(processControls, 2);
        operationGrid.Children.Add(processControls);
        operationCard.Child = operationGrid;
        Grid.SetRow(operationCard, 0);
        root.Children.Add(operationCard);

        // One adaptive toolbar: filters first, file actions second. It wraps cleanly on
        // narrower windows instead of forcing a horizontal page scroll.
        var toolbar = new Border
        {
            Background = BrushHex("#111C26"), BorderBrush = BrushHex("#293B49"),
            BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(7),
            Padding = new Thickness(7), Margin = new Thickness(0, 0, 0, 10)
        };
        var toolbarItems = new WrapPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        _logFilterButtons.Clear();
        AddLogFilterButton(toolbarItems, "Tüm kayıtlar", "ALL");
        AddLogFilterButton(toolbarItems, "WSL / kurulum", "WSL");
        AddLogFilterButton(toolbarItems, "Uygulama", "APP");
        AddLogFilterButton(toolbarItems, "WSL hataları", "WSL_ERRORS");
        AddLogFilterButton(toolbarItems, "Uygulama hataları", "APP_ERRORS");
        toolbarItems.Children.Add(new Border
        {
            Width = 1, Height = 23, Background = BrushHex("#354957"),
            Margin = new Thickness(2, 4, 9, 4), VerticalAlignment = VerticalAlignment.Center
        });

        var openFolderButton = MakeButton("Günlük klasörü", "SecondaryButton", (_, _) => OpenLogFolder(), new Thickness(0, 0, 6, 0));
        var openFileButton = MakeButton("Seçili dosyayı aç", "SecondaryButton", (_, _) => OpenSelectedLogFile(), new Thickness(0, 0, 6, 0));
        var exportButton = MakeButton("Dışa aktar", "SecondaryButton", (_, _) => ExportSelectedLog(), new Thickness(0, 0, 6, 0));
        var refreshButton = MakeButton("Yenile", "SecondaryButton", (_, _) => RefreshVisibleLog(), new Thickness(0, 0, 6, 0));
        var clearButton = MakeButton("Temizle", "SecondaryButton", (_, _) => ClearLogs());
        foreach (var button in new[] { openFolderButton, openFileButton, exportButton, refreshButton, clearButton })
            button.MinHeight = 31;
        toolbarItems.Children.Add(openFolderButton);
        toolbarItems.Children.Add(openFileButton);
        toolbarItems.Children.Add(exportButton);
        toolbarItems.Children.Add(refreshButton);
        toolbarItems.Children.Add(clearButton);
        toolbar.Child = toolbarItems;
        Grid.SetRow(toolbar, 1);
        root.Children.Add(toolbar);

        // Terminal occupies all remaining height. The internal TextBox scroll viewer is
        // kept vertical-only so the bottom-follow behavior stays predictable.
        var logCard = new Border
        {
            Background = BrushHex("#0A1118"), BorderBrush = BrushHex("#2B3E4C"),
            BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(8),
            ClipToBounds = true
        };
        var terminalGrid = new Grid();
        terminalGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(36) });
        terminalGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        var terminalHeader = new Border
        {
            Background = BrushHex("#15222D"), BorderBrush = BrushHex("#2B3E4C"),
            BorderThickness = new Thickness(0, 0, 0, 1), Padding = new Thickness(12, 0, 12, 0)
        };
        var terminalHeaderGrid = new Grid();
        terminalHeaderGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        terminalHeaderGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var terminalFileInfo = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        terminalFileInfo.Children.Add(new Ellipse { Width = 7, Height = 7, Fill = BrushHex("#4BD6AC"), Margin = new Thickness(0, 0, 8, 0), VerticalAlignment = VerticalAlignment.Center });
        terminalFileInfo.Children.Add(new TextBlock { Text = "LIVE OUTPUT", FontSize = 9, FontWeight = FontWeights.Bold, Foreground = BrushHex("#B9C8D2"), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 10, 0) });
        _logsCurrentFileLabel = new TextBlock
        {
            Text = Path.GetFileName(GetSelectedLogPath()), FontSize = 9,
            Foreground = BrushHex("#7F97A8"), VerticalAlignment = VerticalAlignment.Center,
            ToolTip = GetSelectedLogPath()
        };
        terminalFileInfo.Children.Add(_logsCurrentFileLabel);
        terminalHeaderGrid.Children.Add(terminalFileInfo);
        var followPill = new Border
        {
            Background = BrushHex("#19332F"), BorderBrush = BrushHex("#2D6558"),
            BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(10),
            Padding = new Thickness(9, 3, 9, 3), VerticalAlignment = VerticalAlignment.Center
        };
        followPill.Child = new TextBlock { Text = "●  OTOMATİK TAKİP", FontSize = 8, FontWeight = FontWeights.Bold, Foreground = BrushHex("#65D8B5") };
        Grid.SetColumn(followPill, 1);
        terminalHeaderGrid.Children.Add(followPill);
        terminalHeader.Child = terminalHeaderGrid;
        Grid.SetRow(terminalHeader, 0);
        terminalGrid.Children.Add(terminalHeader);

        _liveLogBox = new TextBox
        {
            IsReadOnly = true, IsUndoEnabled = false, AcceptsReturn = true, AcceptsTab = true,
            TextWrapping = TextWrapping.Wrap,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            Background = BrushHex("#0A1118"), Foreground = BrushHex("#D2DDE5"),
            BorderThickness = new Thickness(0), FontFamily = new FontFamily("Cascadia Mono"),
            FontSize = 10.5, Padding = new Thickness(13, 11, 13, 12), Margin = new Thickness(0),
            Text = ReadSelectedLogText(),
            SelectionBrush = BrushHex("#31586A"), CaretBrush = BrushHex("#62DCCB")
        };
        _liveLogCharCount = _liveLogBox.Text.Length;
        Grid.SetRow(_liveLogBox, 1);
        terminalGrid.Children.Add(_liveLogBox);
        logCard.Child = terminalGrid;
        Grid.SetRow(logCard, 2);
        root.Children.Add(logCard);

        UpdateLogFilterButtonStyles();
        RefreshOperationControlButtons();
        ScrollLiveLogToEnd();
        return root;
    }

    private string GetProgressStatusText() => _operationProgressState switch
    {
        "running" => _operationProgressQuality == "indeterminate" ? "ÇALIŞIYOR · AŞAMA YÜZDESİ BELİRSİZ" : _operationProgressQuality == "measured" ? "ÇALIŞIYOR · İNDİRME ÖLÇÜLÜYOR" : "ÇALIŞIYOR",
        "stop-requested" => "DURDURMA İSTENDİ · GÜVENLİ NOKTA BEKLENİYOR",
        "stopped" => "DURDURULDU · UYARI",
        "warning" => "BEKLEMEDE · UYARI",
        "success" => "TAMAMLANDI",
        "failed" => "İPTAL EDİLDİ / BAŞARISIZ · HATA",
        _ => "HAZIR · İŞLEM BEKLENİYOR"
    };

    private string GetProgressColor() => _operationProgressState switch
    {
        "success" => "#38D996",
        "stopped" or "warning" or "stop-requested" => "#F4C15D",
        "failed" => "#FF687D",
        _ => "#42C9C0"
    };

    private string GetProgressPercentText() => _operationProgressState == "success" ? "100%" : "~" + _operationProgressValue.ToString(CultureInfo.InvariantCulture) + "%";

    private void AddLogFilterButton(Panel parent, string caption, string key)
    {
        var button = MakeButton(caption, "SecondaryButton", (_, _) => SetLogView(key), new Thickness(0, 0, 8, 0));
        button.MinHeight = 30;
        button.Padding = new Thickness(11, 4, 11, 4);
        parent.Children.Add(button);
        _logFilterButtons.Add((button, key));
    }

    private void SetLogView(string key)
    {
        _logView = key;
        RefreshVisibleLog();
    }

    private void RefreshVisibleLog()
    {
        if (_liveLogBox is not null)
        {
            var text = ReadSelectedLogText();
            _liveLogBox.Text = text;
            _liveLogCharCount = text.Length;
            if (_logsCurrentFileLabel is not null)
            {
                _logsCurrentFileLabel.Text = Path.GetFileName(GetSelectedLogPath());
                _logsCurrentFileLabel.ToolTip = GetSelectedLogPath();
            }
            ScrollLiveLogToEnd();
        }
        UpdateLogFilterButtonStyles();
    }

    private void UpdateLogFilterButtonStyles()
    {
        foreach (var (button, key) in _logFilterButtons)
        {
            bool selected = string.Equals(key, _logView, StringComparison.OrdinalIgnoreCase);
            button.Background = BrushHex(selected ? "#1B3037" : "#17232D");
            button.BorderBrush = BrushHex(selected ? "#42C9C0" : "#344550");
            button.Foreground = BrushHex(selected ? "#4DD1C7" : "#D8E1E8");
        }
    }

    private string GetSelectedLogPath() => _logView switch
    {
        "WSL" => WslLog,
        "APP" => AppLog,
        "WSL_ERRORS" => WslErrorsLog,
        "APP_ERRORS" => AppErrorsLog,
        _ => MainLog
    };

    private string ReadSelectedLogText()
    {
        var path = GetSelectedLogPath();
        return File.Exists(path) ? TailText(path, LiveLogMaxChars) : $"Henüz kayıt yok.\n\nGünlük dosyası: {path}";
    }

    private void OpenSelectedLogFile()
    {
        try
        {
            var path = GetSelectedLogPath();
            Directory.CreateDirectory(LogsDir);
            if (!File.Exists(path)) File.WriteAllText(path, string.Empty, new UTF8Encoding(false));
            Process.Start(new ProcessStartInfo { FileName = "notepad.exe", ArgumentList = { path }, UseShellExecute = true });
            AppendLog("Günlük dosyası açıldı: " + path, "OK");
        }
        catch (Exception ex) { AppendLog("Günlük dosyası açılamadı: " + ex.Message, "HATA"); }
    }

    private void OpenLogFolder()
    {
        try
        {
            Directory.CreateDirectory(LogsDir);
            Process.Start(new ProcessStartInfo { FileName = "explorer.exe", ArgumentList = { LogsDir }, UseShellExecute = true });
            AppendLog("Günlük klasörü açıldı: " + LogsDir, "OK");
        }
        catch (Exception ex) { AppendLog("Günlük klasörü açılamadı: " + ex.Message, "HATA"); }
    }

    private async void ExportSelectedLog()
    {
        try
        {
            var source = GetSelectedLogPath();
            var dialog = new SaveFileDialog
            {
                Title = "İşlem günlüğünü dışa aktar",
                FileName = Path.GetFileName(source),
                Filter = "Günlük dosyası (*.log)|*.log|Metin dosyası (*.txt)|*.txt|Tüm dosyalar (*.*)|*.*",
                AddExtension = true,
                OverwritePrompt = true
            };
            if (dialog.ShowDialog(this) != true) return;
            // Do not read a potentially huge build log into one UI-thread string.
            await Task.Run(() =>
            {
                FlushPendingDiskLogs();
                lock (LogFileGate)
                {
                    if (File.Exists(source)) File.Copy(source, dialog.FileName, true);
                    else File.WriteAllText(dialog.FileName, string.Empty, new UTF8Encoding(false));
                }
            });
            AppendLog("Günlük dışa aktarıldı: " + dialog.FileName, "OK");
        }
        catch (Exception ex) { AppendLog("Günlük dışa aktarılamadı: " + ex.Message, "HATA"); }
    }

    private void ClearLogs()
    {
        var answer = MessageBox.Show(this,
            "Kontrol merkezi günlüklerindeki mevcut kayıtların tamamı silinecek. Çalışan kurulum veya sunucu durdurulmaz. Devam edilsin mi?",
            "Günlükleri temizle", MessageBoxButton.YesNo, MessageBoxImage.Warning);
        if (answer != MessageBoxResult.Yes) return;

        try
        {
            // Serialize clear against the background disk writer so queued old records cannot
            // reappear in the freshly cleared files. New output produced after this point remains.
            lock (_diskLogFlushGate)
            {
                FlushPendingDiskLogs(); // Monitor locks are reentrant; flush the existing queue first.
                lock (_logQueueGate)
                {
                    while (_pendingDiskLogs.TryDequeue(out _)) { }
                    while (_pendingUiLogs.TryDequeue(out _)) { }
                    Interlocked.Exchange(ref _pendingUiLogCount, 0);
                    Interlocked.Exchange(ref _droppedUiLogEntries, 0);
                    lock (LogFileGate)
                    {
                        Directory.CreateDirectory(LogsDir);
                        foreach (var path in new[] { MainLog, AppLog, WslLog, AppErrorsLog, WslErrorsLog })
                            File.WriteAllText(path, string.Empty, new UTF8Encoding(false));
                    }
                }
            }

            _logView = "ALL";
            if (_liveLogBox is not null)
            {
                _liveLogBox.Clear();
                _liveLogCharCount = 0;
            }
            UpdateLogFilterButtonStyles();
            AppendLog("Tüm kontrol merkezi günlükleri temizlendi. Çalışan işlem varsa devam ediyor.", "OK");
            ScrollLiveLogToEnd();
        }
        catch (Exception ex)
        {
            AppendLog("Günlükler temizlenemedi: " + ex.Message, "HATA");
        }
    }

    private static bool IsErrorLogLine(string message, string level)
    {
        // Ubuntu packages can emit these when systemd's system bus is unavailable in a
        // WSL session. In this installer flow apt continues and exits successfully; keep
        // the text in the full log, but do not misclassify it as a blocking WSL failure.
        if (message.Contains("Failed to get properties: Transport endpoint is not connected", StringComparison.OrdinalIgnoreCase)
            || message.Contains("Failed to connect to system scope bus via local transport: Connection refused", StringComparison.OrdinalIgnoreCase))
            return false;
        if (string.Equals(level, "HATA", StringComparison.OrdinalIgnoreCase)) return true;
        return LogErrorPattern.IsMatch(message);
    }

    private bool LogViewIncludes(LogEntry entry) => _logView switch
    {
        "WSL" => entry.Category == "WSL",
        "APP" => entry.Category != "WSL",
        "WSL_ERRORS" => entry.Category == "WSL" && entry.IsError,
        "APP_ERRORS" => entry.Category != "WSL" && entry.IsError,
        _ => true
    };

    private void AddGridRow(Grid grid, int rowIndex, string label, out StackPanel cell)
    {
        while (grid.RowDefinitions.Count <= rowIndex) grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        var labelBlock = MutedLabel(label); labelBlock.Margin = new Thickness(0, 7, 16, 7); labelBlock.VerticalAlignment = VerticalAlignment.Top;
        Grid.SetRow(labelBlock, rowIndex); Grid.SetColumn(labelBlock, 0); grid.Children.Add(labelBlock);
        cell = NewStack(4); cell.Margin = new Thickness(0, 3, 0, 8); Grid.SetRow(cell, rowIndex); Grid.SetColumn(cell, 1); grid.Children.Add(cell);
    }

    private void AddCardHover(Border card, string normalBackground, string hoverBackground)
    {
        // Local brushes keep hover effects independent from shared XAML styles.
        var normalBrush = BrushHex(normalBackground);
        var hoverBrush = BrushHex(hoverBackground);
        var originalBorderBrush = card.BorderBrush;
        var hoverBorderBrush = BrushHex("#42C9C0");
        card.Background = normalBrush;
        card.Cursor = System.Windows.Input.Cursors.Hand;
        card.MouseEnter += (_, _) =>
        {
            card.Background = hoverBrush;
            card.BorderBrush = hoverBorderBrush;
        };
        card.MouseLeave += (_, _) =>
        {
            card.Background = normalBrush;
            card.BorderBrush = originalBorderBrush;
        };
    }

    private Border MakeCard(string title, string subtitle)
    {
        var card = new Border { Style = (Style)FindResource("CardBorder"), Margin = new Thickness(0), Padding = new Thickness(14) };
        var stack = NewStack(8);
        stack.Children.Add(new TextBlock { Text = title, FontSize = 10, FontWeight = FontWeights.Bold, Foreground = BrushHex("#6BCFC7") });
        stack.Children.Add(new TextBlock { Text = subtitle, FontSize = 10, Foreground = (Brush)FindResource("MutedBrush"), TextWrapping = TextWrapping.Wrap });
        card.Child = stack;
        // Expose the content stack so callers can append rows while keeping title/subtitle at the top.
        card.Tag = stack;
        AddCardHover(card, "#141F2A", "#1B2B36");
        return card;
    }

    private void AddCardChild(Border card, UIElement child)
    {
        if (card.Tag is StackPanel stack) stack.Children.Add(child);
    }

    private StackPanel NewStack(double spacing) => new SpacingStackPanel { Orientation = Orientation.Vertical, Margin = new Thickness(0), Spacing = spacing };

    private sealed class SpacingStackPanel : StackPanel
    {
        private readonly Dictionary<FrameworkElement, Thickness> _originalMargins = new();
        public double Spacing { get; init; }

        protected override void OnVisualChildrenChanged(DependencyObject visualAdded, DependencyObject visualRemoved)
        {
            if (visualRemoved is FrameworkElement removed && _originalMargins.Remove(removed, out var original))
                removed.Margin = original;
            base.OnVisualChildrenChanged(visualAdded, visualRemoved);
            if (visualAdded is FrameworkElement added)
            {
                var margin = added.Margin;
                _originalMargins[added] = margin;
                if (Orientation == Orientation.Vertical && InternalChildren.IndexOf(added) > 0 && Spacing > 0)
                    added.Margin = new Thickness(margin.Left, margin.Top + Spacing, margin.Right, margin.Bottom);
            }
        }
    }

    private StackPanel Heading(string title, string subtitle)
    {
        var s = NewStack(5); s.Children.Add(new TextBlock { Text = title, FontSize = 22, FontWeight = FontWeights.SemiBold, Foreground = (Brush)FindResource("TextBrush") });
        s.Children.Add(new TextBlock { Text = subtitle, FontSize = 11, Foreground = (Brush)FindResource("MutedBrush"), TextWrapping = TextWrapping.Wrap }); return s;
    }

    private TextBlock MutedLabel(string text) => new() { Text = text, FontSize = 10, FontWeight = FontWeights.SemiBold, Foreground = (Brush)FindResource("MutedBrush"), TextWrapping = TextWrapping.Wrap };

    private Button MakeButton(string text, string style, RoutedEventHandler handler, Thickness? margin = null)
    {
        var b = new Button { Content = text, Margin = margin ?? new Thickness(0), MinHeight = 34 };
        b.SetResourceReference(FrameworkElement.StyleProperty, style); b.Click += handler; return b;
    }

    private Border QuickAction(string title, string description, string buttonText, Action action)
    {
        var border = new Border { Background = BrushHex("#141F2A"), BorderBrush = BrushHex("#2B3A47"), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(8), Padding = new Thickness(11), Margin = new Thickness(0, 0, 8, 0) };
        var s = NewStack(8); s.Children.Add(new TextBlock { Text = title, FontSize = 12, FontWeight = FontWeights.SemiBold });
        s.Children.Add(new TextBlock { Text = description, FontSize = 10, Foreground = (Brush)FindResource("MutedBrush"), TextWrapping = TextWrapping.Wrap, MinHeight = 40 });
        s.Children.Add(MakeButton(buttonText, "SecondaryButton", (_, _) => action())); border.Child = s; AddCardHover(border, "#141F2A", "#1B2B36"); return border;
    }

    private UIElement WrapScrollable(UIElement element) => new ScrollViewer { Content = element, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };

    private static SolidColorBrush BrushHex(string value) => new((Color)ColorConverter.ConvertFromString(value));

    private void AppendLog(string message, string level = "INFO", string category = "APP")
    {
        category = string.Equals(category, "WSL", StringComparison.OrdinalIgnoreCase) ? "WSL" : "APP";
        string cleanMessage = NormalizeLogText(message ?? string.Empty);
        var rawLines = cleanMessage.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
        var visibleLines = new List<string>(rawLines.Length);
        foreach (var line in rawLines)
        {
            if (line.StartsWith("[NFSU2-PROGRESS]|", StringComparison.Ordinal)) ParseProgressMarker(line);
            else visibleLines.Add(line);
        }
        string[] messageLines = visibleLines.ToArray();
        if (messageLines.Length == 0 || (messageLines.Length == 1 && messageLines[0].Length == 0)) return;
        string timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
        string block = string.Join(Environment.NewLine, messageLines.Select(m => $"[{timestamp}] [{level}] {m}")) + Environment.NewLine;
        bool isError = messageLines.Any(m => IsErrorLogLine(m, level));

        // Full text goes to disk. Build a UI copy only when the log page is actually visible;
        // while compiling large modules this avoids formatting every line twice.
        bool collectForUi = _logsVisible;
        string uiBlock = string.Empty;
        if (collectForUi)
        {
            var uiLines = messageLines.Select(m => m.Length <= LiveLogMaxLineChars
                ? m
                : m[..LiveLogMaxLineChars] + $" … [satır {m.Length:N0} karakter; tam içerik günlük dosyasında]");
            uiBlock = string.Join(Environment.NewLine, uiLines.Select(m => $"[{timestamp}] [{level}] {m}")) + Environment.NewLine;
            if (uiBlock.Length > LiveLogMaxEntryChars)
                uiBlock = uiBlock[..LiveLogMaxEntryChars] + Environment.NewLine + $"[… tek günlük girdisi canlı görünüm için kısaltıldı; tam içerik dosyada, {block.Length:N0} karakter …]{Environment.NewLine}";
        }

        var entry = new LogEntry(block, uiBlock, category, level, isError);
        lock (_logQueueGate)
        {
            _pendingDiskLogs.Enqueue(entry);

            // While logs are not on screen, avoid UI work for every process line.
            if (!collectForUi) return;
            int pending = Interlocked.Increment(ref _pendingUiLogCount);
            if (pending <= LiveLogMaxPendingEntries)
            {
                _pendingUiLogs.Enqueue(entry);
            }
            else
            {
                Interlocked.Decrement(ref _pendingUiLogCount);
                Interlocked.Increment(ref _droppedUiLogEntries);
            }
        }
    }

    private void FlushPendingDiskLogs()
    {
        if (_pendingDiskLogs.IsEmpty) return;
        // Only one writer may drain the queue at a time; the timer runs off the UI thread.
        lock (_diskLogFlushGate)
        {
            var main = new StringBuilder();
            var app = new StringBuilder();
            var wsl = new StringBuilder();
            var appErrors = new StringBuilder();
            var wslErrors = new StringBuilder();
            int count = 0;
            int chars = 0;
            lock (_logQueueGate)
            {
                while (count < 2000 && chars < 2_000_000 && _pendingDiskLogs.TryDequeue(out var entry))
                {
                    main.Append(entry.FileBlock);
                    (entry.Category == "WSL" ? wsl : app).Append(entry.FileBlock);
                    if (entry.IsError) (entry.Category == "WSL" ? wslErrors : appErrors).Append(entry.FileBlock);
                    count++;
                    chars += entry.FileBlock.Length;
                }
            }
            if (main.Length == 0) return;
            try
            {
                lock (LogFileGate)
                {
                    var utf8 = new UTF8Encoding(false);
                    File.AppendAllText(MainLog, main.ToString(), utf8);
                    if (app.Length > 0) File.AppendAllText(AppLog, app.ToString(), utf8);
                    if (wsl.Length > 0) File.AppendAllText(WslLog, wsl.ToString(), utf8);
                    if (appErrors.Length > 0) File.AppendAllText(AppErrorsLog, appErrors.ToString(), utf8);
                    if (wslErrors.Length > 0) File.AppendAllText(WslErrorsLog, wslErrors.ToString(), utf8);
                }
            }
            catch (Exception ex)
            {
                // Avoid recursively logging a logger failure; keep it available to debugger diagnostics.
                Trace.WriteLine("Could not flush application log batch: " + ex);
            }
        }
    }

    private void ScrollLiveLogToEnd()
    {
        var logBox = _liveLogBox;
        if (logBox is null) return;

        // TextBox.ScrollToEnd can run before WPF has recalculated the scroll extent after
        // an AppendText/Text assignment. Defer until the layout pass has completed.
        Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() =>
        {
            if (!ReferenceEquals(_liveLogBox, logBox)) return;
            logBox.UpdateLayout();
            logBox.CaretIndex = logBox.Text.Length;
            logBox.ScrollToEnd();
        }));
    }

    private void FlushVisibleLogBatch()
    {
        if (!_logsVisible || _currentPage != "Logs" || _liveLogBox is null)
        {
            // Clear any queue entries that were added just as the user navigated away.
            int discarded = 0;
            while (discarded < 2000 && _pendingUiLogs.TryDequeue(out _))
            {
                Interlocked.Decrement(ref _pendingUiLogCount);
                discarded++;
            }
            Interlocked.Exchange(ref _droppedUiLogEntries, 0);
            return;
        }

        var batch = new StringBuilder();
        int entries = 0;
        while (entries < LiveLogBatchMaxEntries && batch.Length < LiveLogBatchMaxChars && _pendingUiLogs.TryDequeue(out var entry))
        {
            Interlocked.Decrement(ref _pendingUiLogCount);
            entries++;
            if (LogViewIncludes(entry)) batch.Append(entry.UiBlock);
        }

        long dropped = Interlocked.Exchange(ref _droppedUiLogEntries, 0);
        if (dropped > 0)
            batch.Append($"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] [PERFORMANS] Canlı görünüm akıcılığı için {dropped:N0} kayıt atlandı. Tam kayıtlar manager.log / wsl.log dosyalarında korunuyor.{Environment.NewLine}");
        if (batch.Length == 0) return;

        string visibleBatch = batch.ToString();
        _liveLogBox.AppendText(visibleBatch);
        _liveLogCharCount += visibleBatch.Length;

        if (_liveLogCharCount > LiveLogMaxChars)
        {
            string current = _liveLogBox.Text; // One text read per batch, never once per incoming line.
            int excess = current.Length - LiveLogMaxChars;
            int newline = excess > 0 ? current.IndexOf('\n', excess) : -1;
            int cut = newline >= 0 ? newline + 1 : Math.Max(0, excess);
            const string trimmedNotice = "[CANLI GÖRÜNÜM] Eski satırlar performans için kaldırıldı; eksiksiz günlük dosyada korunuyor.\r\n";
            string tail = current[cut..];
            if (tail.Length + trimmedNotice.Length > LiveLogMaxChars)
            {
                int keep = Math.Max(0, LiveLogMaxChars - trimmedNotice.Length);
                tail = tail.Length > keep ? tail[(tail.Length - keep)..] : tail;
            }
            _liveLogBox.Text = trimmedNotice + tail;
            _liveLogCharCount = trimmedNotice.Length + tail.Length;
        }

        // Always follow the newest output, including after the visible buffer is trimmed.
        ScrollLiveLogToEnd();
    }

    private static T? FindVisualChild<T>(DependencyObject parent) where T : DependencyObject
    {
        int childCount = VisualTreeHelper.GetChildrenCount(parent);
        for (int i = 0; i < childCount; i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);
            if (child is T result) return result;
            var nested = FindVisualChild<T>(child);
            if (nested is not null) return nested;
        }
        return null;
    }

    private static string RepairMojibake(string text)
    {
        // Some Windows PowerShell 5.1 / native WSL capture combinations decode UTF-8
        // output through an ANSI code page before forwarding it to this process.
        // Repair the common Turkish sequences without touching already-correct UTF-8.
        (string Broken, string Correct)[] fixes =
        {
            ("Ã¼", "ü"), ("Ãœ", "Ü"), ("Ã¶", "ö"), ("Ã–", "Ö"),
            ("Ã§", "ç"), ("Ã‡", "Ç"), ("ÄŸ", "ğ"), ("Äž", "Ğ"),
            ("ÅŸ", "ş"), ("Åž", "Ş"), ("Ä±", "ı"), ("Ä°", "İ"),
            ("Ã¢", "â"), ("Ã¤", "ä"), ("Ã©", "é"), ("Ã¡", "á"),
            ("Ã±", "ñ"), ("Â ", " "), ("â€”", "—"), ("â€“", "–"),
            ("â€™", "’"), ("â€˜", "‘"), ("â€œ", "“"), ("â€", "”")
        };
        foreach (var fix in fixes) text = text.Replace(fix.Broken, fix.Correct, StringComparison.Ordinal);
        return text;
    }

    private static string NormalizeLogText(string text)
    {
        var stripped = StripAnsi(RepairMojibake(text ?? string.Empty));
        return new string(stripped.Where(c => c == '\r' || c == '\n' || c == '\t' || !char.IsControl(c)).ToArray());
    }

    private static string StripAnsi(string text) => Regex.Replace(text.Replace("\0", ""), @"\x1B\[[0-?]*[ -/]*[@-~]", "");

    private async Task<ExecResult> RunCommandAsync(string exe, IEnumerable<string> args, TimeSpan? timeout = null, string? workingDirectory = null, bool logOutput = true, string logCategory = "APP", CancellationToken cancellationToken = default)
    {
        var psi = new ProcessStartInfo { FileName = exe, UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true, RedirectStandardInput = true, WorkingDirectory = workingDirectory ?? Environment.SystemDirectory };
        psi.StandardOutputEncoding = new UTF8Encoding(false); psi.StandardErrorEncoding = new UTF8Encoding(false);
        foreach (var arg in args) psi.ArgumentList.Add(arg);
        using var process = new Process { StartInfo = psi, EnableRaisingEvents = true };
        var buffer = new StringBuilder();
        var gate = new object();
        DataReceivedEventHandler outHandler = (_, e) => { if (e.Data is null) return; lock (gate) buffer.AppendLine(e.Data); if (logOutput) AppendLog(e.Data, "PS/WSL", logCategory); };
        DataReceivedEventHandler errHandler = (_, e) => { if (e.Data is null) return; lock (gate) buffer.AppendLine(e.Data); if (logOutput) AppendLog(e.Data, "UYARI", logCategory); };
        process.OutputDataReceived += outHandler; process.ErrorDataReceived += errHandler;
        if (cancellationToken.CanBeCanceled) _activeInstallProcess = process;
        try
        {
            if (!process.Start()) return new ExecResult(-1, "Could not start process.");
            try { process.StandardInput.Close(); } catch { }
            process.BeginOutputReadLine(); process.BeginErrorReadLine();
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            cts.CancelAfter(timeout ?? TimeSpan.FromHours(3));
            try { await process.WaitForExitAsync(cts.Token); }
            catch (OperationCanceledException)
            {
                bool userCancelled = cancellationToken.IsCancellationRequested;
                try { if (!process.HasExited) process.Kill(entireProcessTree: true); } catch { }
                try { await process.WaitForExitAsync(CancellationToken.None); } catch { }
                try { process.WaitForExit(); } catch { }
                if (userCancelled)
                    AppendLog($"İşlem kullanıcı tarafından durduruldu: {exe}", "UYARI", logCategory);
                else
                    AppendLog($"İşlem zaman aşımına uğradı: {exe}", "HATA", logCategory);
                lock (gate) return new ExecResult(userCancelled ? 130 : 124, buffer.ToString());
            }
            process.WaitForExit();
            lock (gate) return new ExecResult(process.ExitCode, buffer.ToString());
        }
        catch (Exception ex) { AppendLog($"Komut başlatılamadı ({exe}): {ex.Message}", "HATA", logCategory); return new ExecResult(-1, ex.ToString()); }
        finally { if (ReferenceEquals(_activeInstallProcess, process)) _activeInstallProcess = null; }
    }

    private async Task RefreshStatusAsync(bool writeLog)
    {
        if (!await _statusLock.WaitAsync(0)) return;
        try
        {
            var wslExe = FindWslExe();
            bool wslPresent = wslExe is not null;
            bool wslListAvailable = false;
            bool ubuntuPresent = false;
            string wslDiagnostic = string.Empty;
            if (wslPresent)
            {
                var wslList = await ReadWslDistributionsAsync(wslExe!, false);
                wslListAvailable = wslList.Succeeded;
                ubuntuPresent = wslList.Succeeded && wslList.Names.Any(x => x.StartsWith("Ubuntu", StringComparison.OrdinalIgnoreCase));
                wslDiagnostic = wslList.Error;
            }

            var dockerExe = FindDockerExe();
            bool dockerAppOpen = Process.GetProcessesByName("Docker Desktop").Length > 0;
            string dockerVersion = string.Empty;
            string dockerClientVersion = string.Empty;
            string dockerDesktopVersion = GetDockerDesktopVersion();
            string dockerContext = string.Empty;
            bool dockerReady = false;

            if (!string.IsNullOrWhiteSpace(dockerExe))
            {
                // CLI and Engine are queried independently. "docker --version" is the
                // locally installed CLI; ".Server.Version" is the active context Engine.
                var cliInfo = await RunCommandAsync(dockerExe, new[] { "--version" }, TimeSpan.FromSeconds(5), logOutput: false);
                if (cliInfo.ExitCode == 0)
                {
                    var cliMatch = Regex.Match(cliInfo.Output, @"Docker version\s+([^,\s]+)", RegexOptions.IgnoreCase);
                    dockerClientVersion = cliMatch.Success ? cliMatch.Groups[1].Value.Trim() : cliInfo.Output.Trim();
                }

                var contextInfo = await RunCommandAsync(dockerExe, new[] { "context", "show" }, TimeSpan.FromSeconds(4), logOutput: false);
                if (contextInfo.ExitCode == 0) dockerContext = contextInfo.Output.Trim();

                var engineInfo = await RunCommandAsync(dockerExe, new[] { "version", "--format", "{{.Server.Version}}" }, TimeSpan.FromSeconds(7), logOutput: false);
                if (engineInfo.ExitCode == 0 && !string.IsNullOrWhiteSpace(engineInfo.Output))
                {
                    dockerVersion = engineInfo.Output.Trim();
                    dockerReady = true;
                }
            }

            string serverState = dockerReady ? "NFSU2 konteyneri bulunamadı" : "Docker Engine hazır değil";
            bool httpReachable = false;
            bool serverContainerRunning = false;
            if (dockerReady && !string.IsNullOrWhiteSpace(dockerExe))
            {
                var ps = await RunCommandAsync(dockerExe, new[] { "ps", "-a", "--format", "{{.Names}}|{{.Image}}|{{.Status}}" }, TimeSpan.FromSeconds(7), logOutput: false);
                var found = ps.Output.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
                    .FirstOrDefault(line => line.Contains("nfsu2", StringComparison.OrdinalIgnoreCase)
                        && (line.Contains("server", StringComparison.OrdinalIgnoreCase) || line.Contains("nfsu2-web-rs", StringComparison.OrdinalIgnoreCase)));
                if (found is not null)
                {
                    var columns = found.Split('|', 3);
                    serverState = columns.Length >= 3 ? columns[2].Trim() : found.Trim();
                    serverContainerRunning = serverState.StartsWith("Up ", StringComparison.OrdinalIgnoreCase);
                }
            }

            string accessMode = GetActiveAccessMode();
            string activeUrl = GetActiveServerUrl();
            string probeUrl = BuildServerProbeUrl(accessMode, activeUrl);

            // Only probe a verified running NFSU2 container. A stopped Docker Engine
            // or an absent container must not be reported as a canceled HTTP check.
            if (dockerReady && serverContainerRunning)
            {
                Exception? lastProbeError = null;
                for (int attempt = 0; attempt < 2 && !httpReachable; attempt++)
                {
                    try
                    {
                        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(4));
                        HttpClient probeClient = accessMode == "lan" ? LanProbeHttp : accessMode == "public" ? Http : LocalHttp;
                        using var response = await probeClient.GetAsync(probeUrl, HttpCompletionOption.ResponseHeadersRead, cts.Token);
                        httpReachable = response.IsSuccessStatusCode;
                        if (!httpReachable) lastProbeError = new HttpRequestException("HTTP " + (int)response.StatusCode);
                    }
                    catch (Exception ex)
                    {
                        lastProbeError = ex;
                        if (attempt == 0) await Task.Delay(400);
                    }
                }
                if (writeLog && !httpReachable)
                    AppendLog($"NFSU2 konteyneri çalışıyor ancak HTTP sağlık kontrolü yanıt vermedi. Profil={accessMode}; kontrol={probeUrl}; aktif adres={activeUrl}; neden={lastProbeError?.GetType().Name}: {lastProbeError?.Message}", "UYARI");
            }

            if (wslListAvailable && !ubuntuPresent)
            {
                _resolvedWslDistro = null;
                _resolvedProjectDirectory = null;
                _resolvedLinuxHome = null;
                _resolvedLinuxUser = null;
                StopProjectChangeMonitor();
            }

            var status = new MachineStatus(wslPresent, wslListAvailable, ubuntuPresent, wslDiagnostic,
                dockerReady, dockerAppOpen, dockerVersion, dockerClientVersion, dockerDesktopVersion, dockerContext,
                serverState, httpReachable, activeUrl, accessMode, probeUrl, GetPrimaryLanIPv4());
            UpdateStatusUI(status);

            if (writeLog)
            {
                string wslState = !wslPresent ? "eksik" : !wslListAvailable ? "yanıt vermiyor" : "OK";
                string ubuntuState = !wslListAvailable ? "kontrol edilemedi" : ubuntuPresent ? "OK" : "eksik";
                string dockerState = dockerReady
                    ? $"Desktop {DisplayVersion(dockerDesktopVersion)}, CLI {DisplayVersion(dockerClientVersion)}, Engine {DisplayVersion(dockerVersion)}, Context {DisplayVersion(dockerContext)}"
                    : $"Desktop {DisplayVersion(dockerDesktopVersion)}, CLI {DisplayVersion(dockerClientVersion)}, Engine hazır değil";
                string suffix = !wslListAvailable && !string.IsNullOrWhiteSpace(wslDiagnostic) ? " WSL ayrıntısı=" + wslDiagnostic : "";
                AppendLog($"Durum denetimi: WSL={wslState}, Ubuntu={ubuntuState}, Docker={dockerState}, Sunucu={(httpReachable ? "erişilebilir" : "erişilemiyor")}, Profil={accessMode}, Kontrol={probeUrl}, Adres={activeUrl}.{suffix}",
                    httpReachable ? "OK" : dockerReady ? "UYARI" : "HATA");
            }
        }
        catch (Exception ex) { if (writeLog) AppendLog("Durum denetimi hatası: " + ex.Message, "HATA"); }
        finally { _statusLock.Release(); }
    }

    private void UpdateStatusUI(MachineStatus s)
    {
        string wslState = !s.WslPresent ? "KURULU DEĞİL" : s.WslListAvailable ? "ÇALIŞIYOR" : "YANIT VERMİYOR";
        string wslDetail = !s.WslPresent ? "Windows WSL bileşeni bulunamadı"
            : s.WslListAvailable ? "WSL dağıtımları başarıyla okundu"
            : "wsl.exe mevcut ancak dağıtım listesi alınamadı: " + s.WslDiagnostic;
        string wslLevel = !s.WslPresent ? "danger" : s.WslListAvailable ? "success" : "warning";
        SetStatusTexts("WSL", wslState, wslDetail, wslLevel);

        string ubuntuState = !s.WslPresent ? "WSL GEREKLİ" : !s.WslListAvailable ? "KONTROL EDİLEMEDİ" : s.UbuntuPresent ? "KURULU" : "DAĞITIM YOK";
        string ubuntuDetail = !s.WslPresent ? "Önce Windows WSL bileşenini etkinleştir"
            : !s.WslListAvailable ? "WSL yanıt vermedi; Ubuntu eksik varsayılmadı"
            : s.UbuntuPresent ? "Ubuntu Linux dağıtımı kayıtlı" : "Ubuntu WSL dağıtımı bulunamadı";
        string ubuntuLevel = !s.WslPresent ? "danger" : !s.WslListAvailable ? "warning" : s.UbuntuPresent ? "success" : "danger";
        SetStatusTexts("Ubuntu", ubuntuState, ubuntuDetail, ubuntuLevel);

        string dockerDetail = s.DockerReady
            ? $"Docker Desktop {DisplayVersion(s.DockerDesktopVersion)} · CLI {DisplayVersion(s.DockerClientVersion)} · Engine {DisplayVersion(s.DockerVersion)} · Context {DisplayVersion(s.DockerContext)}"
            : $"Desktop {DisplayVersion(s.DockerDesktopVersion)} · CLI {DisplayVersion(s.DockerClientVersion)} · Engine yanıt vermiyor";
        SetStatusTexts("Docker", s.DockerReady ? "ÇALIŞIYOR" : s.DockerAppOpen ? "ENGINE BEKLENİYOR" : "ERİŞİLEMİYOR",
            dockerDetail, s.DockerReady ? "success" : s.DockerAppOpen ? "warning" : "danger");

        string serverLevel = s.HttpReachable ? "success" : s.ServerState.Contains("Up", StringComparison.OrdinalIgnoreCase) ? "warning" : "danger";
        string serverLabel = s.HttpReachable ? "ERİŞİLEBİLİR" : s.ServerState.Contains("Up", StringComparison.OrdinalIgnoreCase) ? "KONTEYNER AÇIK" : "DURMUŞ / YOK";
        string serverDetail = s.HttpReachable
            ? $"{s.ProbeUrl} yanıt veriyor. Aktif adres: {s.ActiveUrl}"
            : $"{s.ServerState}. Kontrol edilen: {s.ProbeUrl}. Açılacak adres: {s.ActiveUrl}";
        SetStatusTexts("Server", serverLabel, serverDetail, serverLevel);

        Brush wslBrush = BrushHex(!s.WslPresent ? "#FF687D" : s.WslListAvailable ? "#38D996" : "#F8C45C");
        WslIndicator.Fill = wslBrush;
        WslSidebarText.Text = !s.WslPresent ? "WSL KURULU DEĞİL" : s.WslListAvailable ? "WSL 2 HAZIR" : "WSL YANIT VERMİYOR";
        WslSidebarText.Foreground = wslBrush;
        WslTopDot.Fill = wslBrush;
        WslTopText.Text = !s.WslPresent ? "WSL KURULU DEĞİL" : s.WslListAvailable ? "WSL 2 HAZIR" : "WSL YANIT VERMİYOR";
        WslTopText.Foreground = wslBrush;

        string dockerSummary = $"Docker Desktop {DisplayVersion(s.DockerDesktopVersion)} · CLI {DisplayVersion(s.DockerClientVersion)} · Engine {DisplayVersion(s.DockerVersion)} · Context {DisplayVersion(s.DockerContext)}";
        FooterStatus.Text = s.DockerReady
            ? $"{dockerSummary} · {(s.HttpReachable ? "Sunucu çevrimiçi" : "Sunucu kontrol bekliyor")}"
            : $"{dockerSummary} · Engine bağlantısı yok";
        FooterStatus.ToolTip = dockerSummary;
        if (_dashboardUrl is not null) _dashboardUrl.Text = s.ActiveUrl;
        if (_dashboardLan is not null)
        {
            _dashboardLan.Text = s.AccessMode == "lan"
                ? s.ActiveUrl
                : s.AccessMode == "local"
                    ? "LAN profili etkin değil. Diğer cihazlar için Erişim ayarları → LAN / HTTPS profilini uygula."
                    : string.IsNullOrWhiteSpace(s.LocalIp) ? "Aktif IPv4 bulunamadı" : $"LAN profili etkin değil ({s.LocalIp}).";
        }
        if (_dashboardPublic is not null) _dashboardPublic.Text = s.AccessMode == "public" ? s.ActiveUrl : "Aktif değil";
        if (_accessStateLabel is not null)
            _accessStateLabel.Text = $"Aktif profil: {s.AccessMode.ToUpperInvariant()} · {(s.HttpReachable ? "Sağlık kontrolü başarılı" : "Sağlık kontrolü başarısız")} · Kontrol: {s.ProbeUrl} · Adres: {s.ActiveUrl}";
    }

    private void SetStatusTexts(string kind, string state, string detail, string level)
    {
        foreach (var item in _statusLabels.Where(x => x.Kind == kind))
        {
            item.State.Text = state; item.Detail.Text = detail;
            var color = level == "success" ? "#38D996" : level == "warning" ? "#F8C45C" : "#FF687D";
            item.State.Foreground = BrushHex(color); item.Dot.Background = BrushHex(color);
        }
    }

    private void UpdateAccessLabels()
    {
        if (_dashboardUrl is not null) _dashboardUrl.Text = GetActiveServerUrl();
    }

    private async void RefreshStatus_Click(object sender, RoutedEventArgs e) => await RefreshStatusAsync(true);

    private async void OpenBrowser_Click(object sender, RoutedEventArgs e)
    {
        var url = GetActiveServerUrl();
        try { Process.Start(new ProcessStartInfo(url) { UseShellExecute = true }); AppendLog("Aktif sunucu adresi tarayıcıda açıldı: " + url, "OK"); }
        catch (Exception ex) { AppendLog("Tarayıcı açılamadı: " + ex.Message, "HATA"); }
    }

    private void BrowseGameFolder_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFolderDialog { Title = "NFSU2 oyun klasörünü seç", InitialDirectory = Directory.Exists(RootDir) ? RootDir : Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles) };
        if (dialog.ShowDialog(this) == true && _gameDirBox is not null)
        {
            _gameDirBox.Text = dialog.FolderName;
            RefreshGameFolderValidation();
        }
    }

    private void VerifyGameFolder_Click(object sender, RoutedEventArgs e) => RefreshGameFolderValidation(showDialog: true);

    private void RefreshGameFolderValidation(bool showDialog = false)
    {
        string path = _gameDirBox?.Text.Trim() ?? Path.Combine(RootDir, "game");
        var validation = ValidateGameFolder(path);
        if (_gameValidationStatus is not null)
        {
            _gameValidationStatus.Text = validation.IsValid ? "DOĞRULANDI · " + validation.Message : "DOĞRULANAMADI · " + validation.Message;
            _gameValidationStatus.Foreground = (Brush)FindResource(validation.IsValid ? "SuccessBrush" : "WarningBrush");
        }
        if (showDialog)
            MessageBox.Show(this, validation.Message, validation.IsValid ? "Oyun dosyaları doğrulandı" : "Oyun klasörü doğrulanamadı", MessageBoxButton.OK, validation.IsValid ? MessageBoxImage.Information : MessageBoxImage.Warning);
    }

    private static GameFolderValidation ValidateGameFolder(string gameDirectory)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(gameDirectory)) return new(false, "Oyun klasörü yolu boş.", null);
            string fullPath = Path.GetFullPath(gameDirectory.Trim().Trim('"'));
            if (!Directory.Exists(fullPath)) return new(false, "Seçilen oyun klasörü bulunamadı: " + fullPath, null);
            string exePath = Path.Combine(fullPath, "SPEED2.EXE");
            if (!File.Exists(exePath)) return new(false, "Bu klasörde SPEED2.EXE bulunamadı. Seçilen klasör, oyunun kurulu olduğu gerçek kök klasör olmalı.", fullPath);

            var exeInfo = new FileInfo(exePath);
            if (exeInfo.Length != SupportedGameExeSize)
                return new(false, $"SPEED2.EXE boyutu desteklenen sürümle eşleşmiyor. Bulunan: {exeInfo.Length:N0} bayt; beklenen: {SupportedGameExeSize:N0} bayt. Bu genellikle farklı bölge/sürüm, değiştirilmiş EXE veya kurulum arşivi seçildiğini gösterir.", fullPath);

            string actualHash;
            using (var stream = new FileStream(exePath, FileMode.Open, FileAccess.Read, FileShare.Read))
                actualHash = Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
            if (!string.Equals(actualHash, SupportedGameExeSha256, StringComparison.OrdinalIgnoreCase))
                return new(false, $"SPEED2.EXE SHA-256 değeri desteklenen Kuzey Amerika 1.2 dosyasıyla eşleşmiyor.{Environment.NewLine}Bulunan: {actualHash}{Environment.NewLine}Beklenen: {SupportedGameExeSha256}{Environment.NewLine}Dosya değiştirilmedi.", fullPath);

            string[] requiredFolders = ["CARS", "FRONTEND", "GLOBAL", "TRACKS"];
            var missingFolders = requiredFolders.Where(folder =>
            {
                string folderPath = Path.Combine(fullPath, folder);
                return !Directory.Exists(folderPath) || !Directory.EnumerateFileSystemEntries(folderPath).Any();
            }).ToArray();
            if (missingFolders.Length > 0)
                return new(false, "SPEED2.EXE sürümü doğru, ancak aynı oyun kökünde şu klasörler eksik veya boş: " + string.Join(", ", missingFolders), fullPath);

            return new(true, "Kuzey Amerika 1.2 yürütülebilir dosyası ve CARS / FRONTEND / GLOBAL / TRACKS klasörleri mevcut.", fullPath);
        }
        catch (Exception ex)
        {
            return new(false, "Oyun klasörü okunamadı: " + ex.Message, null);
        }
    }

    private async void ImportGameArchive_Click(object sender, RoutedEventArgs e)
    {
        if (_operationBusy || _gameArchiveBusy)
        {
            MessageBox.Show(this, "Kurulum veya oyun arşivi işlemi devam ederken başka bir oyun arşivi başlatılamaz.", "İşlem sürüyor", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        await ImportGameArchiveAsync();
    }

    private async void DownloadGameArchive_Click(object sender, RoutedEventArgs e)
    {
        if (_operationBusy || _gameArchiveBusy)
        {
            MessageBox.Show(this, "Kurulum veya oyun arşivi işlemi devam ederken yeni bir indirme başlatılamaz.", "İşlem sürüyor", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        await DownloadAndInstallGameArchiveAsync();
    }

    private async Task<bool> ImportGameArchiveAsync()
    {
        if (_gameArchiveBusy) return false;
        var dialog = new OpenFileDialog
        {
            Title = "Sahip olduğunuz NFS Underground 2 ZIP arşivini seçin",
            Filter = "ZIP arşivi (*.zip)|*.zip",
            CheckFileExists = true,
            Multiselect = false
        };
        if (dialog.ShowDialog(this) != true) return false;

        _gameArchiveBusy = true;
        try
        {
            return await ImportGameArchiveAsync(dialog.FileName, "Kullanıcının seçtiği yerel ZIP arşivi");
        }
        finally
        {
            _gameArchiveBusy = false;
        }
    }

    private async Task<bool> DownloadAndInstallGameArchiveAsync()
    {
        if (_operationBusy || _gameArchiveBusy) return false;
        _gameArchiveBusy = true;
        string archivePath = Path.Combine(RuntimeDir, "nfsu2-game-download-" + Guid.NewGuid().ToString("N") + ".zip");
        string partialPath = archivePath + ".partial";
        try
        {
            Directory.CreateDirectory(RuntimeDir);
            if (_gameValidationStatus is not null) _gameValidationStatus.Text = "MediaFire bağlantısı hazırlanıyor…";
            AppendLog("Kullanıcının verdiği MediaFire bağlantısından NFSU2 oyun arşivi indiriliyor.", "INFO");

            using var timeout = new CancellationTokenSource(TimeSpan.FromHours(3));
            using var request = new HttpRequestMessage(HttpMethod.Get, GameArchiveDownloadUrl);
            request.Headers.UserAgent.ParseAdd("TRPOLARIS-NFSU2-ControlCenter/1.6.3");
            using var response = await GameDownloadHttp.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeout.Token);
            if (!response.IsSuccessStatusCode)
                throw new HttpRequestException($"MediaFire indirmesi başarısız: HTTP {(int)response.StatusCode} ({response.ReasonPhrase}). Bağlantı süresi dolmuş veya sunucu indirmeyi reddetmiş olabilir.");

            Uri? finalUri = response.RequestMessage?.RequestUri;
            if (finalUri is null || !string.Equals(finalUri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("İndirme adresi güvenli HTTPS bağlantısına yönlenmedi. İndirme durduruldu.");

            long? contentLength = response.Content.Headers.ContentLength;
            if (contentLength is > MaxGameArchiveDownloadBytes)
                throw new InvalidDataException($"İndirilecek arşiv izin verilen indirme sınırını ({FormatBytes(MaxGameArchiveDownloadBytes)}) aşıyor.");

            var downloadDrive = new DriveInfo(Path.GetPathRoot(RuntimeDir)!);
            long minimumFreeSpace = contentLength.HasValue
                ? checked(contentLength.Value + 512L * 1024 * 1024)
                : 512L * 1024 * 1024;
            if (downloadDrive.AvailableFreeSpace < minimumFreeSpace)
                throw new IOException($"Arşivi indirmek için yeterli boş alan yok. Gereken en az: {FormatBytes(minimumFreeSpace)}; boş alan: {FormatBytes(downloadDrive.AvailableFreeSpace)}.");

            await using (var input = await response.Content.ReadAsStreamAsync(timeout.Token))
            await using (var output = new FileStream(partialPath, FileMode.CreateNew, FileAccess.Write, FileShare.None, 1024 * 1024, FileOptions.Asynchronous | FileOptions.SequentialScan))
            {
                var buffer = new byte[1024 * 1024];
                long downloaded = 0;
                long lastLogged = 0;
                while (true)
                {
                    int read = await input.ReadAsync(buffer.AsMemory(0, buffer.Length), timeout.Token);
                    if (read == 0) break;
                    downloaded = checked(downloaded + read);
                    if (downloaded > MaxGameArchiveDownloadBytes)
                        throw new InvalidDataException("İndirilen arşiv 20 GB indirme sınırını aştı. Dosya güvenlik nedeniyle silinecek.");
                    await output.WriteAsync(buffer.AsMemory(0, read), timeout.Token);

                    if (contentLength is > 0)
                    {
                        double percent = Math.Min(100, downloaded * 100d / contentLength.Value);
                        if (_gameValidationStatus is not null)
                            _gameValidationStatus.Text = $"Oyun arşivi indiriliyor… {percent:0.0}% · {FormatBytes(downloaded)} / {FormatBytes(contentLength.Value)}";
                    }
                    else if (_gameValidationStatus is not null)
                    {
                        _gameValidationStatus.Text = "Oyun arşivi indiriliyor… " + FormatBytes(downloaded);
                    }

                    if (downloaded - lastLogged >= 64L * 1024 * 1024)
                    {
                        AppendLog("Oyun arşivi indirildi: " + FormatBytes(downloaded) + (contentLength.HasValue ? " / " + FormatBytes(contentLength.Value) : string.Empty), "INFO");
                        lastLogged = downloaded;
                    }
                }
                await output.FlushAsync(timeout.Token);
                if (contentLength.HasValue && downloaded != contentLength.Value)
                    throw new EndOfStreamException($"İndirme tamamlanmadı. Beklenen: {FormatBytes(contentLength.Value)}; alınan: {FormatBytes(downloaded)}.");
                if (downloaded == 0)
                    throw new InvalidDataException("MediaFire boş bir dosya döndürdü.");
            }

            File.Move(partialPath, archivePath);
            AppendLog("Oyun arşivi indirildi (" + new FileInfo(archivePath).Length.ToString("N0", CultureInfo.InvariantCulture) + " bayt). ZIP içeriği doğrulanıyor.", "OK");
            if (_gameValidationStatus is not null) _gameValidationStatus.Text = "İndirme tamamlandı. ZIP açılıyor ve SPEED2.EXE / oyun klasörleri doğrulanıyor…";
            return await ImportGameArchiveAsync(archivePath, "Kullanıcının MediaFire bağlantısından indirilen ZIP arşivi");
        }
        catch (OperationCanceledException)
        {
            const string message = "Oyun arşivi indirmesi zaman aşımına uğradı veya iptal edildi. Kurulum başlatılmadı.";
            if (_gameValidationStatus is not null) _gameValidationStatus.Text = message;
            AppendLog(message, "UYARI");
            MessageBox.Show(this, message, "İndirme tamamlanamadı", MessageBoxButton.OK, MessageBoxImage.Warning);
            return false;
        }
        catch (Exception ex)
        {
            if (_gameValidationStatus is not null) _gameValidationStatus.Text = "Oyun indirilemedi: " + ex.Message;
            AppendLog("MediaFire oyun arşivi indirilemedi: " + ex, "HATA");
            MessageBox.Show(this, "Oyun arşivi indirilemedi veya doğrulama aşamasına geçilemedi. Kurulum başlatılmadı.\n\n" + ex.Message + "\n\nBağlantı geçici olarak çalışmıyorsa \"Bilgisayardaki ZIP arşivini içe aktar\" seçeneğini kullanabilirsin.", "Oyun indirilemedi", MessageBoxButton.OK, MessageBoxImage.Error);
            return false;
        }
        finally
        {
            try { if (File.Exists(partialPath)) File.Delete(partialPath); } catch { }
            try { if (File.Exists(archivePath)) File.Delete(archivePath); } catch { }
            _gameArchiveBusy = false;
        }
    }

    private async Task<bool> ImportGameArchiveAsync(string archivePath, string sourceDescription)
    {
        try
        {
            Directory.CreateDirectory(RootDir);
            Directory.CreateDirectory(Path.Combine(RootDir, "logs"));
            if (_gameValidationStatus is not null) _gameValidationStatus.Text = "ZIP arşivi güvenli biçimde açılıyor ve oyun dosyaları doğrulanıyor…";
            AppendLog(sourceDescription + " içe aktarılıyor.", "INFO");
            var progress = new Progress<string>(message =>
            {
                if (_gameValidationStatus is not null) _gameValidationStatus.Text = message;
                AppendLog(message, "INFO");
            });
            GameImportResult importResult = await Task.Run(() => ImportGameArchiveToRoot(archivePath, RootDir, progress));
            if (_gameDirBox is not null) _gameDirBox.Text = importResult.TargetDirectory;
            RefreshGameFolderValidation();
            string backupText = importResult.BackupDirectory is null
                ? string.Empty
                : "\nÖnceki oyun klasörü yedek olarak korundu: " + importResult.BackupDirectory;
            string messageText = $"Oyun arşivi içe aktarıldı.\nÇıkarılan dosya: {importResult.ExtractedFiles:N0}\nAçılan veri: {FormatBytes(importResult.ExpandedBytes)}\nHedef: {importResult.TargetDirectory}{backupText}";
            AppendLog(messageText.Replace("\n", " · "), "OK");
            MessageBox.Show(this, messageText, "Oyun dosyaları hazır", MessageBoxButton.OK, MessageBoxImage.Information);
            return ValidateGameFolder(importResult.TargetDirectory).IsValid;
        }
        catch (Exception ex)
        {
            if (_gameValidationStatus is not null) _gameValidationStatus.Text = "İçe aktarma başarısız: " + ex.Message;
            AppendLog("Oyun ZIP içe aktarımı başarısız: " + ex, "HATA");
            MessageBox.Show(this, ex.Message, "Oyun arşivi içe aktarılamadı", MessageBoxButton.OK, MessageBoxImage.Error);
            return false;
        }
    }

    private static GameImportResult ImportGameArchiveToRoot(string archivePath, string rootDirectory, IProgress<string>? progress)
    {
        const int bufferSize = 1024 * 1024;
        string root = Path.GetFullPath(rootDirectory);
        string stagingRoot = Path.Combine(root, ".game-import-" + Guid.NewGuid().ToString("N"));
        string extractRoot = Path.Combine(stagingRoot, "extracted");
        string stagedGame = Path.Combine(stagingRoot, "validated-game");
        string target = Path.Combine(root, "game");
        Directory.CreateDirectory(extractRoot);
        string? backup = null;
        long expandedBytes = 0;
        int extractedFiles = 0;

        try
        {
            using (var archiveFile = new FileStream(archivePath, FileMode.Open, FileAccess.Read, FileShare.Read))
            using (var archive = new ZipArchive(archiveFile, ZipArchiveMode.Read, leaveOpen: false))
            {
                if (archive.Entries.Count == 0) throw new InvalidDataException("ZIP arşivi boş.");
                if (archive.Entries.Count > MaxGameArchiveEntries) throw new InvalidDataException($"ZIP arşivinde izin verilen dosya sayısı aşıldı ({MaxGameArchiveEntries:N0}).");
                long declaredTotal = 0;
                foreach (var entry in archive.Entries)
                {
                    if (entry.Length < 0) throw new InvalidDataException("ZIP arşivinde geçersiz dosya boyutu bulundu.");
                    declaredTotal = checked(declaredTotal + entry.Length);
                    if (declaredTotal > MaxGameArchiveExpandedBytes) throw new InvalidDataException("ZIP arşivinin açılmış boyutu güvenlik sınırını (20 GB) aşıyor.");
                    int unixType = (entry.ExternalAttributes >> 16) & 0xF000;
                    if (unixType == 0xA000) throw new InvalidDataException("ZIP arşivinde güvenlik nedeniyle sembolik bağlantı kabul edilmiyor.");
                }
                var drive = new DriveInfo(Path.GetPathRoot(root)!);
                long requiredSpace = Math.Min(MaxGameArchiveExpandedBytes, declaredTotal + Math.Max(256L * 1024 * 1024, declaredTotal / 20));
                if (drive.AvailableFreeSpace < requiredSpace) throw new IOException($"Oyun arşivini açmak için yeterli boş alan yok. Gereken yaklaşık: {FormatBytes(requiredSpace)}; boş alan: {FormatBytes(drive.AvailableFreeSpace)}.");

                string safeExtractRoot = Path.GetFullPath(extractRoot).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
                var buffer = new byte[bufferSize];
                int entryNumber = 0;
                foreach (var entry in archive.Entries)
                {
                    entryNumber++;
                    string normalizedName = entry.FullName.Replace('\\', '/');
                    if (string.IsNullOrWhiteSpace(normalizedName) || normalizedName.StartsWith('/') || Path.IsPathRooted(normalizedName))
                        throw new InvalidDataException("ZIP arşivinde kök dizine veya mutlak yola yazmaya çalışan bir kayıt bulundu.");
                    string[] segments = normalizedName.Split('/', StringSplitOptions.RemoveEmptyEntries);
                    if (segments.Length == 0 || segments.Any(segment => segment is "." or ".." || segment.Contains(':') || segment.Contains('\0')))
                        throw new InvalidDataException("ZIP arşivinde güvenli olmayan dosya yolu bulundu: " + entry.FullName);
                    string relativeName = Path.Combine(segments);
                    string destination = Path.GetFullPath(Path.Combine(extractRoot, relativeName));
                    if (!destination.StartsWith(safeExtractRoot, StringComparison.OrdinalIgnoreCase))
                        throw new InvalidDataException("ZIP arşivi çıkarma alanının dışına yazmaya çalıştı: " + entry.FullName);

                    bool isDirectory = normalizedName.EndsWith('/');
                    if (isDirectory)
                    {
                        Directory.CreateDirectory(destination);
                        continue;
                    }

                    string? parent = Path.GetDirectoryName(destination);
                    if (parent is not null) Directory.CreateDirectory(parent);
                    using var input = entry.Open();
                    using var output = new FileStream(destination, FileMode.CreateNew, FileAccess.Write, FileShare.None, bufferSize, FileOptions.SequentialScan);
                    while (true)
                    {
                        int read = input.Read(buffer, 0, buffer.Length);
                        if (read <= 0) break;
                        expandedBytes = checked(expandedBytes + read);
                        if (expandedBytes > MaxGameArchiveExpandedBytes) throw new InvalidDataException("ZIP açılırken gerçek veri boyutu 20 GB güvenlik sınırını aştı.");
                        output.Write(buffer, 0, read);
                    }
                    extractedFiles++;
                    if (entryNumber % 250 == 0) progress?.Report($"ZIP açılıyor… {entryNumber:N0}/{archive.Entries.Count:N0} kayıt işlendi.");
                }
            }

            progress?.Report("SPEED2.EXE dosyaları ve oyun klasörleri taranıyor…");
            var candidateDirectories = Directory.EnumerateFiles(extractRoot, "SPEED2.EXE", SearchOption.AllDirectories)
                .Select(Path.GetDirectoryName)
                .Where(path => !string.IsNullOrWhiteSpace(path))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Cast<string>()
                .ToList();

            if (candidateDirectories.Count == 0)
                throw new InvalidDataException("ZIP arşivinin içinde SPEED2.EXE bulunamadı. Seçilen ZIP, kurulu oyun klasörünü değil kurulum programını veya farklı bir paketi içeriyor olabilir. ZIP içeriğini kontrol edin. Mevcut oyun dosyalarınız değiştirilmedi.");
            if (candidateDirectories.Count > 256)
                throw new InvalidDataException($"ZIP içinde {candidateDirectories.Count:N0} farklı SPEED2.EXE bulundu. Güvenlik ve performans nedeniyle en fazla 256 aday kontrol ediliyor; tek oyun kurulumunu içeren arşivi seçin.");

            var candidateResults = candidateDirectories
                .Select(path => (Path: path, Validation: ValidateGameFolder(path)))
                .ToList();
            var validCandidates = candidateResults
                .Where(result => result.Validation.IsValid)
                .Select(result => result.Path)
                .ToList();

            if (validCandidates.Count == 0)
            {
                string diagnostics = string.Join(Environment.NewLine + Environment.NewLine, candidateResults.Take(5).Select(result =>
                {
                    string relative = Path.GetRelativePath(extractRoot, result.Path);
                    return $"Aday: {relative}{Environment.NewLine}Neden reddedildi: {result.Validation.Message}";
                }));
                string more = candidateResults.Count > 5 ? $"{Environment.NewLine}{Environment.NewLine}… ve {candidateResults.Count - 5:N0} aday daha." : string.Empty;
                throw new InvalidDataException("ZIP içindeki oyun dosyaları desteklenen sürüm ve klasör kontrollerini geçemedi." + Environment.NewLine + Environment.NewLine + diagnostics + more + Environment.NewLine + Environment.NewLine + "Mevcut oyun dosyalarınız değiştirilmedi.");
            }
            if (validCandidates.Count > 1)
                throw new InvalidDataException("Arşivde birden fazla geçerli NFSU2 oyun kökü bulundu. Yalnızca tek bir oyun kurulumunun bulunduğu ZIP arşivini seçin.");

            string candidate = validCandidates[0];
            Directory.Move(candidate, stagedGame);
            progress?.Report("Oyun doğrulandı. C:\\nfsu2\\game klasörüne güvenli biçimde yerleştiriliyor…");

            if (Directory.Exists(target))
            {
                backup = Path.Combine(root, "game.backup-" + DateTime.Now.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture));
                if (Directory.Exists(backup)) backup += "-" + Guid.NewGuid().ToString("N")[..6];
                Directory.Move(target, backup);
            }
            try
            {
                Directory.Move(stagedGame, target);
            }
            catch
            {
                if (backup is not null && Directory.Exists(backup) && !Directory.Exists(target)) Directory.Move(backup, target);
                throw;
            }

            var finalValidation = ValidateGameFolder(target);
            if (!finalValidation.IsValid)
            {
                string failedTarget = Path.Combine(root, "game.failed-" + DateTime.Now.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture));
                Directory.Move(target, failedTarget);
                if (backup is not null && Directory.Exists(backup)) Directory.Move(backup, target);
                throw new InvalidDataException("Yerleştirme sonrası oyun doğrulaması başarısız: " + finalValidation.Message);
            }

            progress?.Report("Oyun dosyaları doğrulandı ve C:\\nfsu2\\game konumuna yerleştirildi.");
            return new GameImportResult(target, backup, expandedBytes, extractedFiles);
        }
        finally
        {
            try { if (Directory.Exists(stagingRoot)) Directory.Delete(stagingRoot, recursive: true); } catch { }
        }
    }

    private async void Install_Click(object sender, RoutedEventArgs e)
    {
        if (_operationBusy || _gameArchiveBusy) { MessageBox.Show(this, "Bir işlem zaten çalışıyor.", "İşlem sürüyor", MessageBoxButton.OK, MessageBoxImage.Information); return; }
        int port = GetInstallPort(); string gameDir = _gameDirBox?.Text.Trim() ?? Path.Combine(RootDir, "game");
        if (port < 1024 || port > 65535 || port is 80 or 443) { MessageBox.Show(this, "Port 1024-65535 arasında olmalı; 80 ve 443 kullanılamaz.", "Port geçersiz", MessageBoxButton.OK, MessageBoxImage.Warning); return; }
        var gameValidation = ValidateGameFolder(gameDir);
        if (!gameValidation.IsValid)
        {
            AppendLog("Kurulum öncesi oyun doğrulaması başarısız: " + gameValidation.Message, "UYARI");
            var answer = MessageBox.Show(this,
                "Oyun dosyaları bulunamadı veya doğrulanamadı.\n\n" + gameValidation.Message + "\n\n" +
                "Sana ait MediaFire bağlantısından oyun arşivi indirilsin mi?\n\n" +
                "Evet: Arşivi indir, güvenli biçimde aç, sürümünü ve dosyalarını doğrula ve C:\\nfsu2\\game klasörüne yerleştir.\n" +
                "Hayır: Kurulumu iptal et. WSL ve Docker adımları başlatılmaz.",
                "Oyun dosyaları gerekli", MessageBoxButton.YesNo, MessageBoxImage.Warning);
            if (answer != MessageBoxResult.Yes)
            {
                if (_installState is not null) _installState.Text = "Kurulum iptal edildi. Geçerli oyun klasörü olmadan hiçbir kurulum adımı başlatılmadı.";
                AppendLog("Kullanıcı oyun arşivini indirmeyi kabul etmedi; kurulum başlatılmadan iptal edildi.", "UYARI");
                return;
            }
            if (!await DownloadAndInstallGameArchiveAsync())
            {
                if (_installState is not null) _installState.Text = "Oyun indirilemedi veya doğrulanamadı. Kurulum başlatılmadı.";
                AppendLog("Oyun indirme / doğrulama işlemi tamamlanmadı; WSL ve Docker kurulumu başlatılmadı.", "UYARI");
                return;
            }
            gameDir = _gameDirBox?.Text.Trim() ?? Path.Combine(RootDir, "game");
            gameValidation = ValidateGameFolder(gameDir);
            if (!gameValidation.IsValid)
            {
                MessageBox.Show(this, "İndirme ve yerleştirme sonrasında oyun dosyaları tekrar doğrulanamadı. Kurulum iptal edildi.\n\n" + gameValidation.Message, "Oyun doğrulaması başarısız", MessageBoxButton.OK, MessageBoxImage.Error);
                if (_installState is not null) _installState.Text = "Oyun doğrulaması başarısız. Kurulum başlatılmadı.";
                return;
            }
        }
        gameDir = gameValidation.GameDirectory ?? Path.GetFullPath(gameDir);
        if (_gameValidationStatus is not null) _gameValidationStatus.Text = "DOĞRULANDI · " + gameValidation.Message;
        var choice = (_installAccessCombo?.SelectedItem as ComboBoxItem)?.Tag as string ?? "local";
        if (choice == "public" && MessageBox.Show(this, "Public geçici HTTPS bağlantısı oluşturulacak. URL'yi bilen herkes sunucuya erişebilir ve bağlantı tünel açıkken çalışır. Devam edilsin mi?", "Public erişim uyarısı", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return;
        using var installCancellation = new CancellationTokenSource();
        _installCancellation = installCancellation;
        _operationBusy = true;
        try { if (File.Exists(SafeStopRequestPath)) File.Delete(SafeStopRequestPath); } catch { }
        BeginOperationProgress("Kurulum hazırlığı başlatılıyor"); if (_installButton is not null) _installButton.IsEnabled = false; if (_installState is not null) _installState.Text = "Kurulum çalışıyor... Canlı işlem yüzdesi altta gösteriliyor.";
        ShowPage("Logs"); AppendLog($"Kurulum başlatılıyor. Port={port}; GameDir={gameDir}; Erişim={choice}", "INFO");
        try
        {
            Directory.CreateDirectory(RootDir); Directory.CreateDirectory(Path.Combine(RootDir, "logs"));
            var installer = ExtractResource(InstallerResource, "NFSU2-Windows-OneClick-Installer.ps1");
            ExecResult result;
            try
            {
                result = await RunCommandAsync(GetPowerShellExe(), new[] { "-NoLogo", "-NoProfile", "-ExecutionPolicy", "Bypass", "-File", installer, "-Port", port.ToString(), "-GameDir", gameDir }, TimeSpan.FromHours(4), RootDir, logCategory: "WSL", cancellationToken: installCancellation.Token);
            }
            finally
            {
                try { if (File.Exists(installer)) File.Delete(installer); } catch { }
            }
            if (installCancellation.IsCancellationRequested)
            {
                if (_forceCancelRequested)
                {
                    AppendLog("Kurulum hemen iptal edildi. Alt süreçler sonlandırıldı; yarım kalan paket adımları sonraki çalıştırmada onarılabilir.", "HATA", "WSL");
                    CompleteOperationProgress(false, "Kurulum kullanıcı tarafından hemen iptal edildi", "CANCELLED_BY_USER / EXIT 130");
                    if (_installState is not null) _installState.Text = "Kurulum iptal edildi. Yeniden denemeden önce İşlem Günlüğü ve WSL paket durumunu kontrol et.";
                }
                else
                {
                    AppendLog("Kurulum durduruldu. Tamamlanmış adımlar geri alınmadı; kaynak ve oyun dosyaları korunuyor.", "UYARI", "WSL");
                    if (_installState is not null) _installState.Text = "Kurulum durduruldu. Tekrar başlatmadan önce İşlem Günlüğü ve sistem durumunu kontrol et.";
                    StopOperationProgress("Kurulum güvenli noktada durduruldu; tamamlanan adımlar korundu", "75");
                }
                return;
            }
            if (result.ExitCode == 75)
            {
                AppendLog("Kurulum güvenli bir aşamada durduruldu. Aktif kritik komut tamamlandı; sonraki adımlar başlatılmadı.", "UYARI", "WSL");
                if (_installState is not null) _installState.Text = "Kurulum güvenli biçimde durduruldu. Tamamlanan adımlar ve mevcut veriler korundu.";
                StopOperationProgress("Kurulum güvenli noktada durduruldu", "75");
                return;
            }
            if (result.ExitCode != 0)
            {
                AppendLog($"Kurulum başarısız (kod {result.ExitCode}). C:/nfsu2 içindeki WSL günlüklerini denetle.", "HATA");
                string installerCode = ExtractInstallerFailureCode(result.Output, result.ExitCode);
                CompleteOperationProgress(false, "Kurulum hata ile durdu — günlükleri kontrol et", installerCode);
                await AppendDockerDiagnosticsAsync();
                MessageBox.Show(this, $"Kurulum başarısız (kod {result.ExitCode}). Canlı günlüğün son satırlarına bak.\n\nWSL logları: {RootDir}", "Kurulum hatası", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }
            // The WSL installer checks Docker's internal health; verify the published Windows endpoint too.
            if (!await WaitForWindowsHttpAsync(port, TimeSpan.FromSeconds(45), installCancellation.Token))
            {
                if (installCancellation.IsCancellationRequested)
                {
                    AppendLog("Kurulum kullanıcı tarafından durduruldu. Çalışan kontrol işlemi sonlandırıldı; tamamlanmış kurulum adımları korunuyor.", "UYARI", "WSL");
                    if (_installState is not null) _installState.Text = "Kurulum durduruldu. Tamamlanmış adımlar geri alınmadı.";
                    return;
                }
                if (_safeStopRequested)
                {
                    AppendLog("Güvenli durdurma isteği HTTP kontrolü sırasında alındı. Erişim profili başlatılmadı; mevcut sunucu dosyaları korundu.", "UYARI", "WSL");
                    StopOperationProgress("Kurulum HTTP kontrolünden sonra güvenli biçimde durduruldu", "75");
                    if (_installState is not null) _installState.Text = "Kurulum güvenli biçimde durduruldu. HTTP doğrulaması tamamlanamadı; mevcut dosyalar korundu.";
                    return;
                }
                AppendLog("Docker iç sağlık kontrolü tamamlandı ancak Windows localhost üzerinden HTTP doğrulanamadı. Kurulum dosyaları korunuyor; konteyner günlükleri yukarıya eklendi.", "HATA");
                CompleteOperationProgress(false, "Konteyner açıldı ancak Windows HTTP kontrolü başarısız", "HTTP_PROBE_TIMEOUT");
                if (_installState is not null) _installState.Text = "Konteyner oluşturuldu ancak Windows üzerinden erişim doğrulanamadı. Günlüklerdeki Docker/HTTP tanılamasını incele.";
                ShowPage("Logs");
                return;
            }
            AppendLog("Temel kurulum ve Windows HTTP doğrulaması tamamlandı.", "OK");
            if (_safeStopRequested)
            {
                AppendLog("Güvenli durdurma istendi; temel sunucu hazır durumda bırakıldı ve erişim profili aşamasına geçilmedi.", "UYARI", "WSL");
                StopOperationProgress("Temel sunucu hazır; kurulum erişim profili öncesinde durduruldu", "75");
                return;
            }
            SetOperationProgress(97, choice == "local" ? "Yerel sunucu son kez doğrulanıyor" : "Seçilen erişim profili uygulanıyor");
            if (choice != "local")
            {
                bool accessSucceeded = await ApplyAccessProfileAsync(choice, port.ToString(), fromInstall: true, cancellationToken: installCancellation.Token);
                if (installCancellation.IsCancellationRequested)
                {
                    AppendLog("Kurulum kullanıcı tarafından durduruldu. Erişim profili ve alt süreçler sonlandırıldı.", "UYARI", "WSL");
                    if (_installState is not null) _installState.Text = "Kurulum durduruldu. Tamamlanmış adımlar geri alınmadı.";
                    return;
                }
                if (_safeStopRequested)
                {
                    AppendLog("Güvenli durdurma isteği erişim profili aşamasında alındı. Profil değişikliği ve sağlık kontrolü güvenli biçimde tamamlandı; sonraki kurulum adımları başlatılmadı.", "UYARI", "WSL");
                    StopOperationProgress("Erişim profili güvenli noktada tamamlandıktan sonra kurulum durduruldu", "75");
                    if (_installState is not null) _installState.Text = "Erişim profili güvenli biçimde tamamlandı; kurulum durduruldu.";
                    return;
                }
                if (!accessSucceeded)
                {
                    AppendLog("Sunucu temel kurulumu tamamlandı fakat seçilen erişim profili uygulanamadı. Sunucu çalışır durumda olabilir; Erişim ve Paylaşım sayfasından profili tekrar uygulayabilirsin.", "HATA");
                    CompleteOperationProgress(false, "Sunucu kuruldu; erişim profili tamamlanamadı", _lastOperationErrorCode ?? "ACCESS_PROFILE_FAILED");
                    if (_installState is not null) _installState.Text = "Sunucu kuruldu ancak erişim profili uygulanamadı. WSL hataları günlüğünü açıp port / sertifika sorununu düzelt.";
                    ShowPage("Logs");
                    return;
                }
                SetOperationProgress(99, "Erişim profili doğrulandı");
            }
            else SetOperationProgress(99, "Yerel sunucu doğrulandı");
            if (_safeStopRequested)
            {
                AppendLog("Güvenli durdurma isteği son doğrulama sırasında alındı. Kritik işlemler tamamlandı; kurulum başarı mesajı yerine durduruldu olarak işaretlendi.", "UYARI", "WSL");
                StopOperationProgress("Kurulum son doğrulama noktasında güvenli biçimde durduruldu", "75");
                return;
            }
            AppendLog("Kurulum iş akışı başarıyla tamamlandı.", "OK"); CompleteOperationProgress(true, "Kurulum ve erişim kontrolü tamamlandı"); if (_installState is not null) _installState.Text = "Kurulum tamamlandı. Kontrol Merkezi'nden durum ve adresleri yenileyebilirsin.";
            await RefreshStatusAsync(true); ShowPage("Dashboard");
        }
        catch (OperationCanceledException) when (installCancellation.IsCancellationRequested)
        {
            if (_forceCancelRequested)
            {
                AppendLog("Kurulum kullanıcı tarafından hemen iptal edildi. Kod: CANCELLED_BY_USER / EXIT 130.", "HATA", "WSL");
                CompleteOperationProgress(false, "Kurulum hemen iptal edildi", "CANCELLED_BY_USER / EXIT 130");
                if (_installState is not null) _installState.Text = "Kurulum iptal edildi. Yarım kalan paket işlemleri için güvenli onarım gerekebilir.";
            }
            else
            {
                AppendLog("Kurulum güvenli durdurma isteğiyle sonlandırıldı; tamamlanmış adımlar korundu.", "UYARI", "WSL");
                StopOperationProgress("Kurulum güvenli noktada durduruldu", "75");
                if (_installState is not null) _installState.Text = "Kurulum güvenli biçimde durduruldu.";
            }
        }
        catch (Exception ex) { CompleteOperationProgress(false, "Kurulum hata ile durdu — ayrıntılar günlükte", GetOperationFailureCode(ex)); AppendLog("Kurulum hatası: " + ex, "HATA"); MessageBox.Show(this, ex.Message, "Kurulum hatası", MessageBoxButton.OK, MessageBoxImage.Error); }
        finally
        {
            if (ReferenceEquals(_installCancellation, installCancellation)) _installCancellation = null;
            _activeInstallProcess = null;
            _operationBusy = false;
            try { if (File.Exists(SafeStopRequestPath)) File.Delete(SafeStopRequestPath); } catch { }
            _safeStopRequested = false;
            if (_installButton is not null) _installButton.IsEnabled = true;
            RefreshOperationControlButtons();
        }
    }

    private static string ExtractInstallerFailureCode(string output, int processExitCode)
    {
        if (processExitCode == 130) return "CANCELLED / EXIT 130";
        if (processExitCode == 124) return "TIMEOUT / EXIT 124";
        if (processExitCode == -1) return "PROCESS_START_FAILED / EXIT -1";
        var wslCode = Regex.Match(output ?? string.Empty, @"\b(WSL_[A-Z0-9_]+|0x[0-9a-fA-F]{8})\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        if (wslCode.Success) return wslCode.Groups[1].Value.ToUpperInvariant() + " · EXIT " + processExitCode.ToString(CultureInfo.InvariantCulture);
        var matches = Regex.Matches(output ?? string.Empty, @"(?:exit|kod)\s*[:=]?\s*(-?\d+)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        if (matches.Count > 0 && int.TryParse(matches[^1].Groups[1].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int nestedCode))
            return "WSL_EXIT " + nestedCode.ToString(CultureInfo.InvariantCulture) + " · POWERSHELL " + processExitCode.ToString(CultureInfo.InvariantCulture);
        return "EXIT " + processExitCode.ToString(CultureInfo.InvariantCulture);
    }

    private async Task<bool> WaitForWindowsHttpAsync(int port, TimeSpan timeout, CancellationToken cancellationToken = default)
    {
        var url = new Uri($"http://127.0.0.1:{port}/game.json");
        var timer = Stopwatch.StartNew();
        int attempt = 0;
        while (timer.Elapsed < timeout)
        {
            cancellationToken.ThrowIfCancellationRequested();
            attempt++;
            try
            {
                using var requestTimeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                requestTimeout.CancelAfter(TimeSpan.FromSeconds(2));
                using var response = await LocalHttp.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, requestTimeout.Token);
                if (response.IsSuccessStatusCode)
                {
                    AppendLog($"Windows HTTP sağlık kontrolü başarılı: {url} (HTTP {(int)response.StatusCode}).", "OK");
                    return true;
                }
                if (attempt % 5 == 0)
                    AppendLog($"Windows HTTP sağlık kontrolü bekleniyor: {url} şu an HTTP {(int)response.StatusCode} döndürdü ({(int)timer.Elapsed.TotalSeconds} sn).", "INFO");
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                if (attempt % 5 == 0)
                    AppendLog($"Windows HTTP sağlık kontrolü henüz yanıt alamadı ({(int)timer.Elapsed.TotalSeconds} sn): {ex.GetType().Name}: {ex.Message}", "INFO");
            }
            await Task.Delay(TimeSpan.FromSeconds(1), cancellationToken);
        }
        AppendLog($"Windows HTTP sağlık kontrolü {timeout.TotalSeconds:0} saniyede tamamlanamadı: {url}.", "HATA");
        await AppendDockerDiagnosticsAsync();
        return false;
    }

    private async Task OfferLanFirewallRuleAsync(int port)
    {
        var answer = MessageBox.Show(this,
            $"LAN HTTPS adresi hazır: {GetActiveServerUrl()}\n\nAynı ağdaki diğer cihazlar Windows Güvenlik Duvarı tarafından engellenebilir. Yalnızca Özel ağlarda TCP {port} portuna izin veren bir kural oluşturalım mı? Yönetici onayı (UAC) istenir.",
            "LAN bağlantısı / Güvenlik Duvarı", MessageBoxButton.YesNo, MessageBoxImage.Question);
        if (answer != MessageBoxResult.Yes)
        {
            AppendLog($"Windows Güvenlik Duvarı kuralı atlandı. Diğer LAN cihazlarından erişim yoksa Özel ağlarda TCP {port} girişine izin ver.", "UYARI");
            return;
        }

        try
        {
            const string displayName = "TRPOLARIS NFSU2 Web Control Center LAN";
            string script = "$displayName = '" + displayName + "'; " +
                "Get-NetFirewallRule -DisplayName $displayName -ErrorAction SilentlyContinue | Remove-NetFirewallRule; " +
                "New-NetFirewallRule -DisplayName $displayName -Direction Inbound -Action Allow -Protocol TCP -LocalPort " + port.ToString(CultureInfo.InvariantCulture) + " -Profile Private | Out-Null";
            string encoded = Convert.ToBase64String(Encoding.Unicode.GetBytes(script));
            var result = await RunCommandAsync(GetPowerShellExe(),
                new[] { "-NoProfile", "-NonInteractive", "-ExecutionPolicy", "Bypass", "-EncodedCommand", encoded },
                TimeSpan.FromSeconds(20), logOutput: false);
            if (result.ExitCode == 0)
                AppendLog($"Windows Güvenlik Duvarı kuralı hazır: Özel ağlarda TCP {port} (yalnızca NFSU2 LAN HTTPS).", "OK");
            else
                AppendLog($"Windows Güvenlik Duvarı kuralı oluşturulamadı (PowerShell kodu {result.ExitCode}). {result.Output} Bu bilgisayarda URL çalışıp diğer cihazlarda çalışmıyorsa TCP {port} için Özel ağ izinlerini kontrol et.", "UYARI");
        }
        catch (Exception ex)
        {
            AppendLog("Windows Güvenlik Duvarı kuralı oluşturulamadı: " + ex.Message, "UYARI");
        }
    }

    private async Task AppendDockerDiagnosticsAsync()
    {
        var docker = FindDockerExe();
        if (docker is null)
        {
            AppendLog("Docker tanılaması yapılamadı: docker.exe bulunamadı.", "UYARI");
            return;
        }
        AppendLog("=== NFSU2 Docker tanılaması ===", "INFO");
        var list = await RunCommandAsync(docker,
            new[] { "ps", "-a", "--filter", "name=nfsu2", "--format", "{{.Names}}|{{.Image}}|{{.Status}}|{{.Ports}}" },
            TimeSpan.FromSeconds(12), logCategory: "WSL");
        var names = list.Output.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
            .Select(line => line.Split('|').FirstOrDefault()?.Trim())
            .Where(name => !string.IsNullOrWhiteSpace(name) && name!.StartsWith("nfsu2", StringComparison.OrdinalIgnoreCase))
            .Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        if (list.ExitCode != 0 || names.Length == 0)
        {
            AppendLog(list.ExitCode == 0 ? "Docker listesinde NFSU2 konteyneri bulunamadı." : $"Docker konteyner sorgusu başarısız (kod {list.ExitCode}).", "UYARI");
            return;
        }
        foreach (var name in names)
        {
            AppendLog($"NFSU2 konteyner günlükleri: {name}", "INFO");
            await RunCommandAsync(docker, new[] { "logs", "--tail", "120", name! }, TimeSpan.FromSeconds(15), logCategory: "WSL");
        }
    }

    private async Task<bool> ApplyAccessProfileAsync(string mode, string portText, bool fromInstall = false, int? lanPortOverride = null, int lanRetryDepth = 0, CancellationToken cancellationToken = default)
    {
        if (_operationBusy && !fromInstall) { MessageBox.Show(this, "Başka bir işlem devam ediyor.", "İşlem sürüyor", MessageBoxButton.OK, MessageBoxImage.Information); return false; }
        if (!int.TryParse(portText, out var port) || port < 1024 || port > 65535 || port is 80 or 443) { MessageBox.Show(this, "Port 1024-65535 arasında olmalı; 80 ve 443 kullanılamaz.", "Port geçersiz", MessageBoxButton.OK, MessageBoxImage.Warning); return false; }
        if (mode == "public" && !fromInstall && MessageBox.Show(this, "Geçici Cloudflare HTTPS tüneli oluşturulacak. URL'yi bilen herkes kimlik doğrulaması olmadan erişebilir. Devam edilsin mi?", "Public erişim", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return false;
        if (!_operationBusy)
        {
            _operationBusy = true;
            BeginOperationProgress("Erişim profili uygulanıyor: " + mode);
        }
        try
        {
            var location = await ResolveWslProjectAsync(true);
            var wslExe = FindWslExe() ?? throw new OperationFailureException("wsl.exe bulunamadı.", "WSL_NOT_INSTALLED");
            var distro = location.Distro;
            var projectDirectory = location.ProjectDirectory;
            var userHome = location.LinuxHome;
            var installUser = location.LinuxUser;
            AppendLog($"Erişim profili için kurulum raporundaki dağıtım/yol kullanılıyor: {distro}:{projectDirectory}", "INFO", "WSL");
            string ip = _lanIpBox?.Text.Trim() ?? GetPrimaryLanIPv4();
            int lanPort = lanPortOverride ?? 8443;
            if (lanPortOverride is null && _lanPortBox is not null && !string.IsNullOrWhiteSpace(_lanPortBox.Text)
                && (!int.TryParse(_lanPortBox.Text.Trim(), out lanPort) || lanPort < 1024 || lanPort > 65535))
                throw new InvalidOperationException("LAN HTTPS portu 1024-65535 arasında bir sayı olmalı.");
            if (mode == "lan" && (string.IsNullOrWhiteSpace(ip) || !IPAddress.TryParse(ip, out var parsedIp) || parsedIp.AddressFamily != AddressFamily.InterNetwork || !IsPrivateIpv4(parsedIp)))
                throw new InvalidOperationException("LAN için özel IPv4 adresi gir.");
            if (mode == "lan" && !IsTcpPortAvailable(lanPort))
            {
                if (fromInstall)
                {
                    int alternativePort = FindAvailableLanPort(lanPort + 1, 8500);
                    if (alternativePort < 0) throw new InvalidOperationException("8443-8500 aralığında boş LAN HTTPS portu bulunamadı. Erişim ayarlarından başka bir port seç.");
                    AppendLog($"LAN HTTPS portu {lanPort} kullanımda; kurulum için boş port {alternativePort} otomatik seçildi.", "UYARI");
                    lanPort = alternativePort;
                }
                else throw new InvalidOperationException($"LAN HTTPS portu {lanPort} başka bir işlem tarafından kullanılıyor. Boş bir port seçip tekrar dene.");
            }
            Directory.CreateDirectory(RootDir); Directory.CreateDirectory(Path.Combine(RootDir, "logs")); ExtractResource(AccessResource, "nfsu2-access.sh", Path.Combine(RootDir, "nfsu2-access.sh"));
            _accessLogPath = Path.Combine(RootDir, "logs", "nfsu2-access-mode.log"); _accessLogOffset = File.Exists(_accessLogPath) ? new FileInfo(_accessLogPath).Length : 0;
            AppendLog($"Erişim profili uygulanıyor: {mode}. WSL terminalinde sudo parolası istenirse terminalde gir.", "INFO");
            if (_accessStateLabel is not null) _accessStateLabel.Text = "Profil uygulanıyor: " + mode;
            _accessProcess?.Dispose();
            var psi = new ProcessStartInfo { FileName = wslExe, UseShellExecute = false, CreateNoWindow = false, WindowStyle = ProcessWindowStyle.Normal, WorkingDirectory = RootDir };
            foreach (var arg in new[] { "-d", distro, "-u", "root", "--", "env", "HOME=/root", "NFSU2_INSTALL_HOME=" + userHome, "NFSU2_PROJECT_DIR=" + projectDirectory, "SUDO_USER=" + (installUser ?? ""), "NFSU2_STOP_REQUEST_FILE=/mnt/c/nfsu2/logs/.nfsu2-safe-stop.request", "bash", "/mnt/c/nfsu2/nfsu2-access.sh", mode, port.ToString(), ip, lanPort.ToString() }) psi.ArgumentList.Add(arg);
            _accessProcess = Process.Start(psi) ?? throw new InvalidOperationException("WSL erişim komutu başlatılamadı.");
            _accessLogTimer.Start();
            await _accessProcess.WaitForExitAsync(cancellationToken);
            ReadAccessLogTail(); _accessLogTimer.Stop();
            int profileExitCode = _accessProcess.ExitCode;
            ReadAccessLogTail();
            try { if (File.Exists(Path.Combine(RootDir, "nfsu2-access.sh"))) File.Delete(Path.Combine(RootDir, "nfsu2-access.sh")); } catch { }
            if (profileExitCode == 75)
            {
                _lastOperationErrorCode = "SAFE_STOP / EXIT 75";
                AppendLog("Erişim profili güvenli durdurma isteğiyle kontrol noktasında durduruldu. Çıkış kodu 75.", "UYARI", "WSL");
                if (!fromInstall) CompleteOperationWarning("Erişim profili güvenli biçimde durduruldu", "SAFE_STOP / EXIT 75");
                return false;
            }
            if (profileExitCode != 0)
            {
                AppendLog($"Erişim profili başarısız: çıkış kodu {profileExitCode}.", "HATA", "WSL");
                string recentAccessLog = string.Empty;
                try { recentAccessLog = File.Exists(_accessLogPath) ? TailText(_accessLogPath, 60000) : string.Empty; } catch { }
                bool dockerPortBindError = recentAccessLog.Contains("ports are not available", StringComparison.OrdinalIgnoreCase)
                    || recentAccessLog.Contains("forwards/expose", StringComparison.OrdinalIgnoreCase)
                    || recentAccessLog.Contains("port is already allocated", StringComparison.OrdinalIgnoreCase);
                if (mode == "lan" && fromInstall && dockerPortBindError && lanRetryDepth < 4)
                {
                    int nextPort = FindAvailableLanPort(lanPort + 1, 8500);
                    if (nextPort > 0)
                    {
                        AppendLog($"Docker Desktop {lanPort} portunu yayımlayamadı. LAN profili otomatik olarak {nextPort} portuyla yeniden deneniyor ({lanRetryDepth + 1}/4).", "UYARI", "WSL");
                        return await ApplyAccessProfileAsync(mode, portText, fromInstall: true, lanPortOverride: nextPort, lanRetryDepth: lanRetryDepth + 1, cancellationToken: cancellationToken);
                    }
                }
                if (_accessStateLabel is not null) _accessStateLabel.Text = "Profil uygulanamadı. WSL hataları bölümünden ayrıntıları incele.";
                _lastOperationErrorCode = "ACCESS_PROFILE_EXIT_" + profileExitCode.ToString(CultureInfo.InvariantCulture);
                if (!fromInstall) CompleteOperationProgress(false, "Erişim profili uygulanamadı", _lastOperationErrorCode);
                await RefreshStatusAsync(true);
                return false;
            }
            AppendLog("Erişim profili uygulandı: " + mode + " — aktif adres: " + GetActiveServerUrl(), "OK", "WSL");
            if (mode == "lan") await OfferLanFirewallRuleAsync(lanPort);
            if (_accessStateLabel is not null) _accessStateLabel.Text = "Aktif profil: " + mode + " — " + GetActiveServerUrl();
            await RefreshStatusAsync(true);
            if (!fromInstall) CompleteOperationProgress(true, "Erişim profili ve sağlık kontrolü tamamlandı");
            return true;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            _accessLogTimer.Stop();
            ReadAccessLogTail();
            try { if (File.Exists(Path.Combine(RootDir, "nfsu2-access.sh"))) File.Delete(Path.Combine(RootDir, "nfsu2-access.sh")); } catch { }
            AppendLog("Erişim profili kullanıcı tarafından durduruldu.", "UYARI", "WSL");
            if (_accessStateLabel is not null) _accessStateLabel.Text = "Erişim profili durduruldu.";
            _lastOperationErrorCode = "CANCELLED / EXIT 130";
            if (!fromInstall) CompleteOperationProgress(false, "Erişim profili kullanıcı tarafından iptal edildi", "CANCELLED_BY_USER / EXIT 130");
            return false;
        }
        catch (Exception ex)
        {
            _lastOperationErrorCode = GetOperationFailureCode(ex);
            AppendLog("Erişim profili hatası: " + ex.Message, "HATA", "WSL");
            if (_accessStateLabel is not null) _accessStateLabel.Text = ex.Message;
            if (!fromInstall) CompleteOperationProgress(false, "Erişim profili hata ile durdu", _lastOperationErrorCode);
            MessageBox.Show(this, ex.Message, "Erişim profili", MessageBoxButton.OK, MessageBoxImage.Error);
            try { if (File.Exists(Path.Combine(RootDir, "nfsu2-access.sh"))) File.Delete(Path.Combine(RootDir, "nfsu2-access.sh")); } catch { }
            return false;
        }
        finally { if (!fromInstall) _operationBusy = false; }
    }

    private void ReadAccessLogTail()
    {
        try
        {
            if (!File.Exists(_accessLogPath)) return;
            using var fs = new FileStream(_accessLogPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            if (fs.Length < _accessLogOffset) _accessLogOffset = 0;
            fs.Seek(_accessLogOffset, SeekOrigin.Begin); using var reader = new StreamReader(fs, Encoding.UTF8, true);
            while (!reader.EndOfStream)
            {
                var line = reader.ReadLine();
                if (line is null) continue;
                if (line.StartsWith("[NFSU2-PROGRESS]|", StringComparison.Ordinal)) ParseProgressMarker(line);
                else AppendLog("[ACCESS] " + line, IsErrorLogLine(line, "INFO") ? "HATA" : "INFO", "WSL");
            }
            _accessLogOffset = fs.Position;
        } catch { }
    }

    private async Task RestartWslAsync()
    {
        if (_operationBusy) { AppendLog("WSL kurtarma başlatılmadı: başka bir işlem sürüyor.", "UYARI", "WSL"); return; }
        var answer = MessageBox.Show(this,
            "WSL --shutdown tüm açık WSL dağıtımlarını kapatır. Ubuntu içindeki servisler ve Docker Desktop'ın WSL altyapısı geçici olarak durabilir. Linux dosyaları silinmez; ancak çalışan diğer WSL işlerin kesilir. Devam edilsin mi?",
            "WSL bağlantısını kurtar", MessageBoxButton.YesNo, MessageBoxImage.Warning);
        if (answer != MessageBoxResult.Yes) return;

        var wsl = FindWslExe();
        if (wsl is null) { MessageBox.Show(this, "wsl.exe bulunamadı.", "WSL", MessageBoxButton.OK, MessageBoxImage.Error); return; }
        _operationBusy = true;
        BeginOperationProgress("WSL dağıtımları güvenli biçimde kapatılıyor");
        ShowPage("Logs");
        try
        {
            var shutdown = await RunCommandAsync(wsl, new[] { "--shutdown" }, TimeSpan.FromSeconds(40), logCategory: "WSL");
            if (shutdown.ExitCode != 0)
                throw new OperationFailureException("wsl --shutdown başarısız oldu (kod " + shutdown.ExitCode + "). " + TailText(shutdown.Output, 1200), "WSL_SHUTDOWN_EXIT_" + shutdown.ExitCode.ToString(CultureInfo.InvariantCulture));

            SetOperationProgress(55, "WSL yeniden algılanıyor; Ubuntu kaydı kontrol ediliyor");
            await Task.Delay(1200);
            var listing = await ReadWslDistributionsAsync(wsl, true);
            if (!listing.Succeeded)
                throw new InvalidOperationException("WSL kapatıldı ancak yeniden yanıt vermedi. Windows'u yeniden başlatmadan önce WSL güncellemesini ve Windows WSL hizmetini kontrol et. Ayrıntı: " + listing.Error);

            var ubuntu = listing.Names.FirstOrDefault(x => x.StartsWith("Ubuntu", StringComparison.OrdinalIgnoreCase));
            if (ubuntu is not null)
            {
                SetOperationProgress(76, "Ubuntu dağıtımı yeniden başlatılıyor");
                var boot = await RunCommandAsync(wsl, new[] { "-d", ubuntu, "-u", "root", "--", "/bin/true" }, TimeSpan.FromSeconds(30), logCategory: "WSL");
                if (boot.ExitCode != 0)
                    AppendLog("Ubuntu yeniden başlatılamadı; dağıtım kaydı duruyor. Ayrıntı: " + TailText(boot.Output, 1000), "UYARI", "WSL");
                else
                    AppendLog("Ubuntu WSL komutu başarıyla çalıştırıldı.", "OK", "WSL");
            }
            else
            {
                AppendLog("WSL yeniden yanıt verdi ancak kayıtlı Ubuntu dağıtımı bulunamadı.", "UYARI", "WSL");
            }

            SetOperationProgress(92, "Docker Engine ve NFSU2 sağlık durumu yeniden kontrol ediliyor");
            await RefreshStatusAsync(true);
            CompleteOperationProgress(true, "WSL kapatıldı ve yeniden algılandı; Docker durumu yukarıda");
            AppendLog("WSL bağlantı kurtarma tamamlandı. Docker Desktop WSL altyapısı henüz hazır değilse Docker Desktop'ı açıp durumu yenile.", "OK", "WSL");
        }
        catch (Exception ex)
        {
            CompleteOperationProgress(false, "WSL bağlantı kurtarma tamamlanamadı", GetOperationFailureCode(ex));
            AppendLog("WSL bağlantı kurtarma hatası: " + ex.Message, "HATA", "WSL");
            MessageBox.Show(this, ex.Message, "WSL kurtarma", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally { _operationBusy = false; }
    }

    private async Task RunDiagnosticsAsync()
    {
        if (_operationBusy) { AppendLog("Tanılama başlatılmadı: başka bir işlem sürüyor.", "UYARI"); return; }
        _operationBusy = true; ShowPage("Logs"); AppendLog("=== TAM SİSTEM DENETİMİ ===", "INFO");
        try
        {
            var wsl = FindWslExe(); AppendLog("WSL exe: " + (wsl ?? "bulunamadı"));
            if (wsl is not null) { var a = await RunCommandAsync(wsl, new[] { "--status" }, TimeSpan.FromSeconds(8), logCategory: "WSL"); var b = await RunCommandAsync(wsl, new[] { "--list", "--verbose" }, TimeSpan.FromSeconds(8), logCategory: "WSL"); AppendLog("WSL status exit: " + a.ExitCode); AppendLog("WSL distribution list exit: " + b.ExitCode); }
            var docker = FindDockerExe(); AppendLog("Docker CLI: " + (docker ?? "bulunamadı"));
            if (docker is not null) { await RunCommandAsync(docker, new[] { "info" }, TimeSpan.FromSeconds(15)); await RunCommandAsync(docker, new[] { "ps", "-a", "--format", "{{.Names}}|{{.Image}}|{{.Status}}|{{.Ports}}" }, TimeSpan.FromSeconds(10)); }
            var speed = Path.Combine(RootDir, "game", "SPEED2.EXE"); AppendLog(File.Exists(speed) ? $"SPEED2.EXE var; boyut {new FileInfo(speed).Length}" : "SPEED2.EXE bulunamadı: " + speed, File.Exists(speed) ? "OK" : "UYARI");
            var module = Path.Combine(RootDir, "module", "nfsu2.wasm"); AppendLog(File.Exists(module) ? $"nfsu2.wasm var; boyut {new FileInfo(module).Length}" : "nfsu2.wasm henüz bulunamadı", File.Exists(module) ? "OK" : "UYARI");
            AppendLog("=== DENETİM TAMAMLANDI ===", "OK"); await RefreshStatusAsync(true);
        }
        finally { _operationBusy = false; }
    }

    private async Task SafeRepairAsync()
    {
        if (_operationBusy) { AppendLog("Onarım başlatılmadı: başka bir işlem sürüyor.", "UYARI"); return; }
        if (MessageBox.Show(this, "WSL içinde paket durumu denetlenecek. Aktif apt/dpkg süreçleri bulunursa onarım yapılmadan durulacak. Devam edilsin mi?", "Güvenli onarım", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return;
        _operationBusy = true; ShowPage("Logs");
        try
        {
            var wsl = FindWslExe(); if (wsl is null) throw new InvalidOperationException("wsl.exe bulunamadı.");
            AppendLog("WSL dağıtım listesi okunuyor.", "INFO", "WSL");
            var distroResult = await ReadWslDistributionsAsync(wsl, true);
            if (!distroResult.Succeeded) throw new InvalidOperationException("WSL dağıtım listesi okunamadı; onarım güvenlik için durduruldu. Ayrıntı: " + distroResult.Error);
            var distro = distroResult.Names.FirstOrDefault(x => x.StartsWith("Ubuntu", StringComparison.OrdinalIgnoreCase));
            if (distro is null) throw new InvalidOperationException("Ubuntu dağıtımı bulunamadı.");
            // Run as WSL root after checking that no package manager is active; never delete lock files.
            var repair = await RunCommandAsync(wsl, new[] { "-d", distro, "-u", "root", "--", "bash", "-lc", "if pgrep -x apt >/dev/null || pgrep -x apt-get >/dev/null || pgrep -x dpkg >/dev/null || pgrep -f '[a]pt-get install' >/dev/null; then echo '[CHECK] Paket yöneticisi hâlâ çalışıyor; kilitlere dokunulmadı.'; exit 3; fi; export DEBIAN_FRONTEND=noninteractive APT_LISTCHANGES_FRONTEND=none; dpkg --configure -a && apt-get check" }, TimeSpan.FromMinutes(5), logCategory: "WSL");
            AppendLog(repair.ExitCode == 0 ? "Güvenli paket denetimi tamamlandı." : $"Güvenli paket denetimi/onarımı tamamlanamadı (kod {repair.ExitCode}). Ayrıntıyı kontrol et.", repair.ExitCode == 0 ? "OK" : "HATA");
        }
        finally { _operationBusy = false; }
    }

    private async Task UninstallAsync(bool containers, bool images, bool tunnel, bool volume, bool projectFiles, bool gameFiles, bool removeUbuntu, bool removeDockerDesktop)
    {
        if (_operationBusy) { MessageBox.Show(this, "Başka bir işlem devam ediyor.", "İşlem sürüyor", MessageBoxButton.OK, MessageBoxImage.Information); return; }
        var warnings = new StringBuilder("Seçili NFSU2 kaynakları kaldırılacak:\n");
        if (containers) warnings.AppendLine("• NFSU2 konteynerleri"); if (images) warnings.AppendLine("• NFSU2 imajları"); if (tunnel) warnings.AppendLine("• NFSU2 Local/Public erişim işaretçileri ve yalnızca sahip olunan tünel süreci"); if (volume) warnings.AppendLine("• Kayıt hacmi ve oyun kayıtları (kalıcı silme)"); if (projectFiles) warnings.AppendLine("• C:\\nfsu2 kaynak kodu/modül/ayarları ve doğrulanmış NFSU2 WSL proje klasörü"); if (gameFiles) warnings.AppendLine("• C:\\nfsu2\\game (oyun dosyaları)");
        if (removeUbuntu) warnings.AppendLine("• Ubuntu* WSL dağıtımları ve içindeki tüm Linux dosyaları"); if (removeDockerDesktop) warnings.AppendLine("• Docker Desktop uygulaması");
        warnings.AppendLine("Diğer Docker projeleri NFSU2 temizliği sırasında hedeflenmez.");
        if (MessageBox.Show(this, warnings.ToString(), "NFSU2 kaldırma onayı", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return;
        if (removeUbuntu && MessageBox.Show(this, "Ubuntu ile başlayan WSL dağıtımları tek tek kaldırılacak. Dağıtım içindeki tüm kullanıcı dosyaları kalıcı olarak silinir. Devam edilsin mi?", "Ubuntu verileri silinecek", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return;
        if (removeDockerDesktop && MessageBox.Show(this, "Docker Desktop kaldırılırsa diğer projelerin konteynerlerini çalıştıramazsın. Devam edilsin mi?", "Docker Desktop kaldırma", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return;
        _operationBusy = true; ShowPage("Logs"); AppendLog("=== NFSU2 KALDIRMA BAŞLADI ===", "WARN");
        bool cleanupHadWarnings = false;
        try
        {
            var docker = FindDockerExe();
            if (tunnel) await StopOwnedTunnelAsync();
            var targetContainers = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (docker is not null && containers)
            {
                foreach (var label in new[] { "nfsu2-web-local", "nfsu2-web", "nfsu2-web-rs" })
                {
                    var query = await RunCommandAsync(docker, new[] { "ps", "-a", "--filter", "label=com.docker.compose.project=" + label, "--format", "{{.ID}}|{{.Names}}|{{.Image}}" }, TimeSpan.FromSeconds(10), logOutput: false);
                    if (query.ExitCode != 0) { cleanupHadWarnings = true; AppendLog($"NFSU2 konteyner sorgusu başarısız (kod {query.ExitCode}); Docker Engine durumunu kontrol et.", "HATA"); }
                    foreach (var line in query.Output.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)) { var parts = line.Split('|'); if (parts.Length >= 3) targetContainers.Add(parts[0].Trim()); }
                }
                var all = await RunCommandAsync(docker, new[] { "ps", "-a", "--format", "{{.ID}}|{{.Names}}|{{.Image}}" }, TimeSpan.FromSeconds(10), logOutput: false);
                if (all.ExitCode != 0) { cleanupHadWarnings = true; AppendLog($"Docker konteyner listesi alınamadı (kod {all.ExitCode}).", "HATA"); }
                foreach (var line in all.Output.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries))
                {
                    var p = line.Split('|'); if (p.Length < 3) continue; var name=p[1].Trim(); var img=p[2].Trim();
                    if ((name.StartsWith("nfsu2-web", StringComparison.OrdinalIgnoreCase) || name.Equals("nfsu2-web-server-1", StringComparison.OrdinalIgnoreCase)) && (img.Contains("nfsu2", StringComparison.OrdinalIgnoreCase) || name.StartsWith("nfsu2-web", StringComparison.OrdinalIgnoreCase))) targetContainers.Add(p[0].Trim());
                }
                foreach (var id in targetContainers)
                {
                    var removeContainer = await RunCommandAsync(docker, new[] { "rm", "-f", id }, TimeSpan.FromSeconds(30));
                    if (removeContainer.ExitCode != 0) { cleanupHadWarnings = true; AppendLog($"NFSU2 konteyneri kaldırılamadı: {id} (kod {removeContainer.ExitCode}).", "HATA"); }
                }
                if (targetContainers.Count == 0) AppendLog("NFSU2 konteyneri bulunamadı; başka konteynerlere dokunulmadı.", "UYARI");
            }
            if (docker is null && (containers || images || volume)) { cleanupHadWarnings = true; AppendLog("Docker CLI bulunamadı; seçilen Docker kaynakları temizlenemedi.", "UYARI"); }
            if (images && docker is not null)
            {
                var imagesResult = await RunCommandAsync(docker, new[] { "images", "--format", "{{.Repository}}:{{.Tag}}|{{.ID}}" }, TimeSpan.FromSeconds(10), logOutput: false);
                if (imagesResult.ExitCode != 0) { cleanupHadWarnings = true; AppendLog($"Docker imaj listesi alınamadı (kod {imagesResult.ExitCode}).", "HATA"); }
                foreach (var line in imagesResult.Output.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries))
                {
                    var p=line.Split('|'); if(p.Length<2) continue; var repository=p[0].Trim();
                    if (repository.Equals("ghcr.io/trpolaris/nfsu2-web-rs:latest", StringComparison.OrdinalIgnoreCase) || repository.Equals("ghcr.io/th0mk/nfsu2-web-rs:latest", StringComparison.OrdinalIgnoreCase) || repository.Equals("nfsu2-web-rs:local", StringComparison.OrdinalIgnoreCase) || repository.Equals("nfsu2-web-rs:a20daa9", StringComparison.OrdinalIgnoreCase))
                    {
                        var removeImage = await RunCommandAsync(docker, new[] { "image", "rm", repository }, TimeSpan.FromSeconds(20));
                        if (removeImage.ExitCode != 0) { cleanupHadWarnings = true; AppendLog($"NFSU2 imajı tamamen kaldırılamadı: {repository} (kod {removeImage.ExitCode}).", "UYARI"); }
                    }
                }
            }
            if (volume && docker is not null)
            {
                foreach (var vol in new[] { "nfsu2-web-local_state", "nfsu2-web_state", "nfsu2-web-local-state" })
                {
                    var inspect = await RunCommandAsync(docker, new[] { "volume", "inspect", vol }, TimeSpan.FromSeconds(5), logOutput: false);
                    if (inspect.ExitCode != 0) continue;
                    var users = await RunCommandAsync(docker, new[] { "ps", "-a", "--filter", "volume=" + vol, "--format", "{{.ID}}|{{.Names}}" }, TimeSpan.FromSeconds(8), logOutput: false);
                    if (users.ExitCode != 0) { cleanupHadWarnings = true; AppendLog($"Kayıt hacmi kullanıcıları sorgulanamadı; güvenlik için silinmedi: {vol}.", "UYARI"); continue; }
                    if (!string.IsNullOrWhiteSpace(users.Output.Trim())) { cleanupHadWarnings = true; AppendLog($"Kayıt hacmi kullanımda; silinmedi: {vol}. Önce konteynerlerin kaldırıldığını doğrula.", "UYARI"); continue; }
                    var removeVolume = await RunCommandAsync(docker, new[] { "volume", "rm", vol }, TimeSpan.FromSeconds(15));
                    if (removeVolume.ExitCode != 0) { cleanupHadWarnings = true; AppendLog($"Kayıt hacmi kaldırılamadı: {vol} (kod {removeVolume.ExitCode}).", "UYARI"); }
                }
            }
            await RemoveNfsu2FirewallRulesAsync();
            if (projectFiles && Directory.Exists(RootDir))
            {
                await RemoveWslProjectDirectoryAsync();
                var projectCleanupFailures = new List<string>();
                foreach (var path in new[] { Path.Combine(RootDir, "source"), Path.Combine(RootDir, "module"), Path.Combine(RootDir, "logs"), Path.Combine(RootDir, "NFSU2-PROJECT-PATH.txt"), Path.Combine(RootDir, "compose.yml"), Path.Combine(RootDir, "compose.local.yml"), Path.Combine(RootDir, ".env"), Path.Combine(RootDir, ".env.local"), Path.Combine(RootDir, "nfsu2-access.sh"), Path.Combine(RootDir, "nfsu2-one-click-wsl.sh"), Path.Combine(RootDir, "nfsu2-stop-owned-tunnel.sh"), Path.Combine(RootDir, "nfsu2-remove-verified-project.sh"), Path.Combine(RootDir, "nfsu2-project-lookup.sh"), Path.Combine(RootDir, "nfsu2-git-update.sh"), Path.Combine(RootDir, "nfsu2-access-mode.txt"), Path.Combine(RootDir, "nfsu2-access-url.txt"), Path.Combine(RootDir, "NFSU2-PUBLIC-URL.txt"), Path.Combine(RootDir, ".nfsu2-cloudflared.pid") })
                {
                    if (Directory.Exists(path))
                    {
                        _ = await DeleteDirectoryTreeBestEffortAsync(path, projectCleanupFailures, repairAccessDenied: true);
                    }
                    else if (File.Exists(path))
                    {
                        try { File.SetAttributes(path, FileAttributes.Normal); File.Delete(path); }
                        catch (Exception ex) { projectCleanupFailures.Add($"Dosya kaldırılamadı: {path}: {ex.Message}"); }
                    }
                }
                if (projectCleanupFailures.Count == 0) AppendLog("NFSU2 proje dosyaları için seçili temizlik tamamlandı. Game klasörü ayrıca seçilmediyse korundu.", "OK");
                else
                {
                    cleanupHadWarnings = true;
                    AppendLog($"NFSU2 proje temizliği kısmen tamamlandı; silinemeyen öğe sayısı: {projectCleanupFailures.Count}.", "UYARI");
                    foreach (var failure in projectCleanupFailures.Take(20)) AppendLog(failure, "UYARI");
                }
            }
            if (gameFiles && Directory.Exists(Path.Combine(RootDir, "game")))
            {
                var gamePath = Path.Combine(RootDir, "game");
                var deleteFailures = new List<string>();
                bool deleted = await DeleteDirectoryTreeBestEffortAsync(gamePath, deleteFailures, repairAccessDenied: true);
                if (deleted && deleteFailures.Count == 0 && !Directory.Exists(gamePath)) AppendLog("Oyun klasörü ve içindeki dosyalar kaldırıldı.", "WARN");
                else
                {
                    cleanupHadWarnings = true;
                    AppendLog($"Oyun klasörü tamamen silinemedi. Erişim engelli veya kullanımda olan öğe sayısı: {deleteFailures.Count}.", "UYARI");
                    foreach (var failure in deleteFailures.Take(30)) AppendLog(failure, "UYARI");
                    AppendLog("Kilitli dosyalar için güvenli izin düzeltme denendi. Dosya hâlâ kullanımdaysa oyunu/araçları kapatıp tekrar dene.", "INFO");
                }
            }
            if (removeUbuntu && !await RemoveUbuntuDistributionsAsync()) cleanupHadWarnings = true;
            if (removeDockerDesktop && !await RemoveDockerDesktopAsync()) cleanupHadWarnings = true;
            AppendLog(cleanupHadWarnings
                ? "Kaldırma işlemi bitti ancak bazı öğeler kaldı veya doğrulanamadı. WSL hataları ve uygulama hataları günlüklerini incele."
                : "Kaldırma adımları tamamlandı. Seçilmeyen bileşenler korunmuştur.", cleanupHadWarnings ? "UYARI" : "OK");
            if (!removeDockerDesktop && !removeUbuntu) await RefreshStatusAsync(true);
        }
        catch (Exception ex) { AppendLog("Kaldırma hatası: " + ex, "HATA"); MessageBox.Show(this, ex.Message, "Kaldırma hatası", MessageBoxButton.OK, MessageBoxImage.Error); }
        finally { _operationBusy = false; }
    }

    private async Task<bool> DeleteDirectoryTreeBestEffortAsync(string path, List<string> failures, bool repairAccessDenied)
    {
        int initialFailureCount = failures.Count;
        try
        {
            if (!Directory.Exists(path)) return true;

            // Never walk through junctions or symbolic links into a different directory tree.
            try
            {
                if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)
                {
                    Directory.Delete(path, false);
                    return !Directory.Exists(path);
                }
            }
            catch (Exception ex)
            {
                failures.Add($"Bağlantı/klasör kaldırılamadı: {path}: {ex.Message}");
                return false;
            }

            string[] files;
            try { files = Directory.GetFiles(path, "*", SearchOption.TopDirectoryOnly); }
            catch (Exception ex) { failures.Add($"Klasör okunamadı: {path}: {ex.Message}"); return false; }

            foreach (var file in files)
            {
                try
                {
                    File.SetAttributes(file, FileAttributes.Normal);
                    File.Delete(file);
                    if (File.Exists(file)) throw new IOException("Delete komutu sonrasında dosya hâlâ mevcut.");
                }
                catch (Exception firstException)
                {
                    bool removed = false;
                    if (repairAccessDenied && firstException is UnauthorizedAccessException)
                    {
                        try
                        {
                            AppendLog("Dosya erişimi reddedildi; yalnızca kaldırılması seçilen NFSU2 dosyası için yönetici sahiplik/izin düzeltmesi deneniyor: " + file, "UYARI");
                            var systemDir = Environment.SystemDirectory;
                            var takeown = Path.Combine(systemDir, "takeown.exe");
                            var icacls = Path.Combine(systemDir, "icacls.exe");
                            if (File.Exists(takeown)) await RunCommandAsync(takeown, new[] { "/f", file, "/a" }, TimeSpan.FromSeconds(12), logOutput: false);
                            if (File.Exists(icacls)) await RunCommandAsync(icacls, new[] { file, "/grant", "*S-1-5-32-544:F", "/c" }, TimeSpan.FromSeconds(12), logOutput: false);
                            File.SetAttributes(file, FileAttributes.Normal);
                            File.Delete(file);
                            removed = !File.Exists(file);
                        }
                        catch { removed = false; }
                    }
                    if (!removed) failures.Add($"Dosya kaldırılamadı: {file}: {firstException.Message}{(repairAccessDenied && firstException is UnauthorizedAccessException ? " (sahiplik/izin düzeltmesi denendi; dosya kilitli veya ACL tarafından korunuyor olabilir)" : string.Empty)}");
                }
            }

            string[] directories;
            try { directories = Directory.GetDirectories(path, "*", SearchOption.TopDirectoryOnly); }
            catch (Exception ex) { failures.Add($"Alt klasörler okunamadı: {path}: {ex.Message}"); return false; }
            foreach (var directory in directories)
                _ = await DeleteDirectoryTreeBestEffortAsync(directory, failures, repairAccessDenied);

            try { File.SetAttributes(path, FileAttributes.Directory); } catch { }
            try
            {
                Directory.Delete(path, false);
                return !Directory.Exists(path) && failures.Count == initialFailureCount;
            }
            catch (Exception ex)
            {
                bool removedAfterRepair = false;
                if (repairAccessDenied && ex is UnauthorizedAccessException)
                {
                    try
                    {
                        AppendLog("Klasör erişimi reddedildi; seçili kaldırma hedefinde sahiplik/ACL düzeltmesi deneniyor: " + path, "UYARI");
                        var systemDir = Environment.SystemDirectory;
                        var takeown = Path.Combine(systemDir, "takeown.exe");
                        var icacls = Path.Combine(systemDir, "icacls.exe");
                        if (File.Exists(takeown)) await RunCommandAsync(takeown, new[] { "/f", path, "/a" }, TimeSpan.FromSeconds(15), logOutput: false);
                        if (File.Exists(icacls)) await RunCommandAsync(icacls, new[] { path, "/grant", "*S-1-5-32-544:(OI)(CI)F", "/c", "/q" }, TimeSpan.FromSeconds(15), logOutput: false);
                        Directory.Delete(path, false);
                        removedAfterRepair = !Directory.Exists(path);
                    }
                    catch { removedAfterRepair = false; }
                }
                if (removedAfterRepair) return failures.Count == initialFailureCount;
                failures.Add($"Klasör kaldırılamadı: {path}: {ex.Message}");
                return false;
            }
        }
        catch (Exception ex)
        {
            failures.Add($"Kaldırma sırasında erişilemedi: {path}: {ex.Message}");
            return false;
        }
    }

    private async Task RemoveWslProjectDirectoryAsync()
    {
        var wsl = FindWslExe(); if (wsl is null) return;
        AppendLog("WSL dağıtım listesi okunuyor.", "INFO", "WSL");
        var distroResult = await ReadWslDistributionsAsync(wsl, true);
        if (!distroResult.Succeeded) { AppendLog("WSL yanıt vermedi; doğrulanmış proje klasörü kaldırma adımı atlandı.", "UYARI", "WSL"); return; }
        var distro = distroResult.Names.FirstOrDefault(x => x.StartsWith("Ubuntu", StringComparison.OrdinalIgnoreCase));
        if (distro is null) return;
        const string shell = """
set -u
uid="$(getent passwd | awk -F: '$3 >= 1000 && $3 < 60000 && $6 ~ /^\/home\// {print $3; exit}')"
if [[ -z "$uid" ]]; then echo "No regular WSL user was detected; no project directory was removed."; exit 0; fi
home="$(getent passwd "$uid" | cut -d: -f6)"
p="$home/nfsu2-web-trpolaris"
if [[ -d "$p/.git" ]]; then
  origin="$(git -c safe.directory="$p" -C "$p" remote get-url origin 2>/dev/null || true)"
  origin="${origin/ git@github.com:/https://github.com/}"
  origin="${origin%.git}"
  origin="${origin%/}"
  if [[ "${origin,,}" == "https://github.com/trpolaris/nfsu2-web" ]]; then
    echo "Removing verified NFSU2 project: $p"
    rm -rf -- "$p"
  else
    echo "Preserving project with unverified origin: $p"
  fi
else
  echo "Verified NFSU2 project directory not found for $home; nothing removed."
fi
""";
        var path = Path.Combine(RootDir, "nfsu2-remove-verified-project.sh");
        Directory.CreateDirectory(RootDir); File.WriteAllText(path, shell.Replace("\r\n", "\n"), new UTF8Encoding(false));
        AppendLog("WSL içinde doğrulanmış NFSU2 proje klasörü aranıyor; Git remote eşleşmiyorsa korunacak.", "INFO", "WSL");
        var removeResult = await RunCommandAsync(wsl, new[] { "-d", distro, "-u", "root", "--", "bash", "/mnt/c/nfsu2/nfsu2-remove-verified-project.sh" }, TimeSpan.FromMinutes(3), logCategory: "WSL");
        try { if (File.Exists(path)) File.Delete(path); } catch { }
        if (removeResult.ExitCode != 0) AppendLog($"WSL proje klasörü temizliği tamamlanamadı (kod {removeResult.ExitCode}); WSL günlüğündeki ayrıntıları kontrol et.", "UYARI", "WSL");
    }

    private async Task<bool> RemoveUbuntuDistributionsAsync()
    {
        var wsl = FindWslExe();
        if (wsl is null) { AppendLog("wsl.exe bulunamadı; Ubuntu dağıtımı kaldırılamadı.", "HATA", "WSL"); return false; }
        AppendLog("Kaldırma öncesinde kayıtlı WSL dağıtımları listeleniyor.", "INFO", "WSL");
        var result = await ReadWslDistributionsAsync(wsl, true);
        if (!result.Succeeded) { AppendLog("WSL dağıtımları listelenemedi; Ubuntu kaldırma güvenlik için durduruldu. " + result.Error, "HATA", "WSL"); return false; }
        var distros = result.Names.Where(x => x.StartsWith("Ubuntu", StringComparison.OrdinalIgnoreCase)).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        if (distros.Length == 0) { AppendLog("Ubuntu dağıtımı bulunamadı; kaldırılacak Ubuntu dağıtımı yok.", "INFO", "WSL"); return true; }

        bool allRemoved = true;
        foreach (var distro in distros)
        {
            AppendLog("Ubuntu WSL dağıtımı kaldırılıyor: " + distro, "UYARI", "WSL");
            AppendLog($"WSL dağıtımı '{distro}' için kayıt ve sanal disk kaldırma işlemi başlatıldı; bu adım birkaç dakika sürebilir.", "INFO", "WSL");
            var unreg = await RunCommandAsync(wsl, new[] { "--unregister", distro }, TimeSpan.FromMinutes(15), logOutput: false, logCategory: "WSL");
            ExecResult verifyResult = await RunCommandAsync(wsl, new[] { "--list", "--quiet" }, TimeSpan.FromSeconds(30), logOutput: false, logCategory: "WSL");
            string verifyOutput = verifyResult.Output ?? string.Empty;
            verifyOutput = verifyOutput.Replace("\0", string.Empty, StringComparison.Ordinal);
            string[] remainingDistros = verifyOutput.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            bool stillRegistered = verifyResult.ExitCode != 0 || remainingDistros.Any(x => x.Equals(distro, StringComparison.OrdinalIgnoreCase));
            if (!stillRegistered)
            {
                AppendLog(unreg.ExitCode == 0
                    ? "Ubuntu dağıtımı kaldırıldı ve WSL listesinde bulunmadığı doğrulandı: " + distro
                    : $"Ubuntu dağıtımı WSL listesinde bulunmadığı için kaldırılmış sayıldı (komut çıkış kodu {unreg.ExitCode}): {distro}", "OK", "WSL");
            }
            else
            {
                allRemoved = false;
                AppendLog($"Ubuntu dağıtımı kaldırılamadı veya doğrulanamadı: {distro}; unregister kodu {unreg.ExitCode}, doğrulama kodu {verifyResult.ExitCode}.", "HATA", "WSL");
            }
        }
        return allRemoved;
    }

    private async Task<bool> RemoveDockerDesktopAsync()
    {
        var winget = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Microsoft", "WindowsApps", "winget.exe");
        if (!File.Exists(winget))
        {
            var path = (Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Select(d => Path.Combine(d, "winget.exe")).FirstOrDefault(File.Exists);
            if (path is not null) winget = path;
        }
        if (!File.Exists(winget)) { AppendLog("winget.exe bulunamadı; Docker Desktop otomatik kaldırılmadı. Windows Ayarları > Uygulamalar bölümünü kullan.", "UYARI"); return false; }
        var result = await RunCommandAsync(winget, new[] { "uninstall", "--id", "Docker.DockerDesktop", "--exact", "--silent", "--accept-source-agreements" }, TimeSpan.FromMinutes(10));
        if (result.ExitCode == 0) { AppendLog("Docker Desktop kaldırma komutu tamamlandı.", "OK"); return true; }
        AppendLog("Docker Desktop kaldırma komutu başarısız (kod " + result.ExitCode + ").", "HATA");
        return false;
    }

    private async Task StopOwnedTunnelAsync()
    {
        var wsl = FindWslExe();
        if (wsl is null) { AppendLog("WSL bulunamadı; tünel temizliği atlandı.", "UYARI"); return; }
        AppendLog("WSL dağıtım listesi okunuyor.", "INFO", "WSL");
        var distroResult = await ReadWslDistributionsAsync(wsl, true);
        if (!distroResult.Succeeded) { AppendLog("WSL yanıt vermedi; tünel kapatma atlandı. Ubuntu eksik varsayılmadı.", "UYARI", "WSL"); return; }
        var distro = distroResult.Names.FirstOrDefault(x => x.StartsWith("Ubuntu", StringComparison.OrdinalIgnoreCase));
        if (distro is null) { AppendLog("Ubuntu dağıtımı yok; tünel PID dosyası varsa Windows tarafındaki proje temizliğinde kaldırılacak.", "UYARI"); return; }
        Directory.CreateDirectory(RootDir);
        var helperPath = Path.Combine(RootDir, "nfsu2-stop-owned-tunnel.sh");
        const string shell = """
#!/usr/bin/env bash
set -u
root=/mnt/c/nfsu2
pf="$root/.nfsu2-cloudflared.pid"
port="${1:-9093}"
if [[ -f "$pf" ]]; then
  p="$(cat "$pf" 2>/dev/null || true)"
  if [[ "$p" =~ ^[0-9]+$ && -r "/proc/$p/cmdline" ]]; then
    cmd="$(tr '\0' ' ' < "/proc/$p/cmdline" 2>/dev/null || true)"
    if [[ "$cmd" == *cloudflared* && "$cmd" == *tunnel* && "$cmd" == *"--url"* && ( "$cmd" == *"127.0.0.1:$port"* || "$cmd" == *"localhost:$port"* ) ]]; then
      kill "$p" 2>/dev/null || true
      echo "Stopped owned NFSU2 Quick Tunnel PID $p"
    else
      echo "PID marker did not match an NFSU2 tunnel; no process was killed."
    fi
  fi
  rm -f "$pf"
fi
rm -f "$root/NFSU2-PUBLIC-URL.txt" "$root/nfsu2-access-url.txt" "$root/nfsu2-access-mode.txt"
echo "NFSU2 access URL/profile markers cleared."
""";
        File.WriteAllText(helperPath, shell.Replace("\r\n", "\n"), new UTF8Encoding(false));
        var stopResult = await RunCommandAsync(wsl, new[] { "-d", distro, "-u", "root", "--", "bash", "/mnt/c/nfsu2/nfsu2-stop-owned-tunnel.sh", GetConfiguredPort().ToString() }, TimeSpan.FromSeconds(30), logCategory: "WSL");
        try { if (File.Exists(helperPath)) File.Delete(helperPath); } catch { }
        if (stopResult.ExitCode != 0) AppendLog($"NFSU2 tünel temizliği başarısız (kod {stopResult.ExitCode}).", "UYARI", "WSL");
    }

    private async Task RemoveNfsu2FirewallRulesAsync()
    {
        try
        {
            string script = "$names = @('TRPOLARIS NFSU2 LAN HTTPS','TRPOLARIS NFSU2 Web Control Center LAN'); " +
                "foreach ($name in $names) { Get-NetFirewallRule -DisplayName $name -ErrorAction SilentlyContinue | Remove-NetFirewallRule -ErrorAction SilentlyContinue }";
            string encoded = Convert.ToBase64String(Encoding.Unicode.GetBytes(script));
            var result = await RunCommandAsync(GetPowerShellExe(),
                new[] { "-NoProfile", "-NonInteractive", "-ExecutionPolicy", "Bypass", "-EncodedCommand", encoded },
                TimeSpan.FromSeconds(20), logOutput: false);
            if (result.ExitCode == 0) AppendLog("NFSU2 Windows Güvenlik Duvarı kuralları temizlendi.", "OK");
            else AppendLog($"NFSU2 Windows Güvenlik Duvarı kuralları temizlenemedi (kod {result.ExitCode}): {result.Output}", "UYARI");
        }
        catch (Exception ex) { AppendLog("Güvenlik Duvarı kuralı temizlenemedi: " + ex.Message, "UYARI"); }
    }

    private static bool IsTcpPortAvailable(int port)
    {
        if (port is < 1 or > 65535) return false;
        TcpListener? listener = null;
        try
        {
            listener = new TcpListener(IPAddress.Any, port);
            listener.Start(1);
            return true;
        }
        catch (SocketException) { return false; }
        catch (UnauthorizedAccessException) { return false; }
        finally { try { listener?.Stop(); } catch { } }
    }

    private static int FindAvailableLanPort(int preferred = 8443, int last = 8500)
    {
        for (int port = Math.Max(1024, preferred); port <= Math.Min(last, 65535); port++)
            if (port is not 80 and not 443 && IsTcpPortAvailable(port)) return port;
        return -1;
    }

    private int GetInstallPort() => int.TryParse(_portBox?.Text.Trim(), out int value) ? value : GetConfiguredPort();

    private string GetActiveAccessMode()
    {
        string mode = ReadTextFile(Path.Combine(RootDir, "nfsu2-access-mode.txt"), "").Trim().ToLowerInvariant();
        if (mode is "local" or "lan" or "public") return mode;
        string savedUrl = ReadTextFile(Path.Combine(RootDir, "nfsu2-access-url.txt"), "").Trim();
        if (savedUrl.Contains("trycloudflare.com", StringComparison.OrdinalIgnoreCase)) return "public";
        if (Uri.TryCreate(savedUrl, UriKind.Absolute, out var parsed) && parsed.Scheme == Uri.UriSchemeHttps && IPAddress.TryParse(parsed.Host, out var ip) && IsPrivateIpv4(ip)) return "lan";
        return "local";
    }

    private string GetActiveServerUrl()
    {
        string savedUrl = ReadTextFile(Path.Combine(RootDir, "nfsu2-access-url.txt"), "").Trim();
        if (Uri.TryCreate(savedUrl, UriKind.Absolute, out var uri) &&
            (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps))
            return new UriBuilder(uri) { Path = "/", Query = "", Fragment = "" }.Uri.AbsoluteUri;
        return $"http://localhost:{GetConfiguredPort()}/";
    }

    private string BuildServerProbeUrl(string mode, string activeUrl)
    {
        if (!Uri.TryCreate(activeUrl, UriKind.Absolute, out var activeUri))
            activeUri = new Uri($"http://localhost:{GetConfiguredPort()}/");
        var builder = new UriBuilder(activeUri) { Path = "/game.json", Query = "", Fragment = "" };
        if (mode == "lan")
        {
            // Probe the Windows-published LAN TLS port locally. The saved URL is the LAN IP:port,
            // but the health check should not depend on a local route back to the NIC's own IP.
            int lanPort = activeUri.Scheme == Uri.UriSchemeHttps && !activeUri.IsDefaultPort
                ? activeUri.Port
                : GetConfiguredLanPort();
            builder.Scheme = Uri.UriSchemeHttps;
            builder.Host = "127.0.0.1";
            builder.Port = lanPort;
        }
        return builder.Uri.AbsoluteUri;
    }

    private int GetConfiguredLanPort()
    {
        try
        {
            string marker = Path.Combine(RootDir, "NFSU2-LAN-PORT.txt");
            if (File.Exists(marker) && int.TryParse(File.ReadAllText(marker).Trim(), out int saved) && saved is >= 1024 and <= 65535)
                return saved;
        }
        catch { }
        try
        {
            var env = File.ReadAllLines(Path.Combine(RootDir, ".env.local"))
                .FirstOrDefault(x => x.TrimStart().StartsWith("NFSU2_LAN_HTTPS_PORT="));
            if (env is not null && int.TryParse(env.Split('=', 2).Last().Trim().Trim('"'), out int p) && p is >= 1024 and <= 65535) return p;
        }
        catch { }
        return 8443;
    }

    private int GetConfiguredPort()
    {
        try { var env = File.ReadAllLines(Path.Combine(RootDir, ".env.local")).FirstOrDefault(x => x.TrimStart().StartsWith("NFSU2_HTTP_PORT=")); if (env is not null && int.TryParse(env.Split('=', 2).Last().Trim(), out int p) && p is >=1024 and <=65535) return p; } catch { }
        try { var compose = File.ReadAllText(Path.Combine(RootDir, "compose.yml")); var m = Regex.Match(compose, @"NFSU2_HTTP_PORT\s*[:=]\s*['""]?(\d+)"); if (m.Success && int.TryParse(m.Groups[1].Value, out int p) && p is >=1024 and <=65535) return p; } catch { }
        return 9093;
    }

    private static string DisplayVersion(string? value) => string.IsNullOrWhiteSpace(value) ? "bilinmiyor" : value.Trim();

    private static string GetDockerDesktopVersion()
    {
        var executableCandidates = new List<string>
        {
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Docker", "Docker", "Docker Desktop.exe"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", "DockerDesktop", "Docker Desktop.exe"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", "Docker", "Docker", "Docker Desktop.exe")
        };
        var registryVersionFallbacks = new List<string>();

        // When running, use the exact executable path of the active Desktop process first.
        foreach (var process in Process.GetProcessesByName("Docker Desktop"))
        {
            try
            {
                string? path = process.MainModule?.FileName;
                if (!string.IsNullOrWhiteSpace(path)) executableCandidates.Insert(0, path);
            }
            catch { }
            finally { process.Dispose(); }
        }

        // Installation paths vary by installer and per-user setup. Discover InstallLocation
        // and DisplayIcon before falling back to cached DisplayVersion registry metadata.
        foreach (var root in new[] { Registry.LocalMachine, Registry.CurrentUser })
        foreach (var uninstallPath in new[]
        {
            @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall",
            @"SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall"
        })
        {
            try
            {
                using var uninstall = root.OpenSubKey(uninstallPath);
                if (uninstall is null) continue;
                foreach (var subName in uninstall.GetSubKeyNames())
                {
                    try
                    {
                        using var app = uninstall.OpenSubKey(subName);
                        var displayName = app?.GetValue("DisplayName") as string;
                        if (string.IsNullOrWhiteSpace(displayName) || !displayName.Contains("Docker Desktop", StringComparison.OrdinalIgnoreCase)) continue;
                        var installLocation = app?.GetValue("InstallLocation") as string;
                        if (!string.IsNullOrWhiteSpace(installLocation))
                        {
                            executableCandidates.Add(Path.Combine(installLocation.Trim().Trim('"'), "Docker Desktop.exe"));
                        }
                        var displayIcon = app?.GetValue("DisplayIcon") as string;
                        if (!string.IsNullOrWhiteSpace(displayIcon))
                        {
                            var match = Regex.Match(displayIcon.Trim(), """^\s*"?(.+?\.exe)"?(?:,\d+)?\s*$""", RegexOptions.IgnoreCase);
                            if (match.Success) executableCandidates.Add(match.Groups[1].Value.Trim().Trim('"'));
                        }
                        var version = app?.GetValue("DisplayVersion") as string ?? app?.GetValue("Version") as string;
                        if (!string.IsNullOrWhiteSpace(version)) registryVersionFallbacks.Add(version.Trim());
                    }
                    catch { }
                }
            }
            catch { }
        }

        foreach (var path in executableCandidates.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            try
            {
                if (!File.Exists(path)) continue;
                var info = FileVersionInfo.GetVersionInfo(path);
                if (!string.IsNullOrWhiteSpace(info.ProductVersion)) return info.ProductVersion.Trim();
                if (!string.IsNullOrWhiteSpace(info.FileVersion)) return info.FileVersion.Trim();
            }
            catch { }
        }
        return registryVersionFallbacks.FirstOrDefault() ?? string.Empty;
    }

    private static string? FindWslExe()
    {
        // The application is published win-x64, so System32 is the native WSL location.
        // Keep Sysnative as a fallback for development launches from a 32-bit host process.
        string windows = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
        var candidates = new[]
        {
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "wsl.exe"),
            Path.Combine(windows, "System32", "wsl.exe"),
            Path.Combine(windows, "Sysnative", "wsl.exe")
        };

        foreach (string candidate in candidates.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            try { if (File.Exists(candidate)) return candidate; }
            catch { }
        }

        foreach (string directory in (Environment.GetEnvironmentVariable("PATH") ?? string.Empty)
            .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            try
            {
                string candidate = Path.Combine(directory.Trim('"'), "wsl.exe");
                if (File.Exists(candidate)) return candidate;
            }
            catch { }
        }

        return null;
    }

    private static string GetPowerShellExe()
    {
        string windows = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
        var candidates = new[]
        {
            Path.Combine(windows, "System32", "WindowsPowerShell", "v1.0", "powershell.exe"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "WindowsPowerShell", "v1.0", "powershell.exe"),
            Path.Combine(windows, "Sysnative", "WindowsPowerShell", "v1.0", "powershell.exe")
        };

        foreach (string candidate in candidates.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            try { if (File.Exists(candidate)) return candidate; }
            catch { }
        }

        foreach (string executable in new[] { "powershell.exe", "pwsh.exe" })
        foreach (string directory in (Environment.GetEnvironmentVariable("PATH") ?? string.Empty)
            .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            try
            {
                string candidate = Path.Combine(directory.Trim('"'), executable);
                if (File.Exists(candidate)) return candidate;
            }
            catch { }
        }

        throw new FileNotFoundException("Windows PowerShell (powershell.exe) bulunamadı. Windows PowerShell 5.1 veya PowerShell 7 kurulumunu doğrula.");
    }

    private static string? FindDockerExe()
    {
        var candidates = new[] { Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Docker", "Docker", "resources", "bin", "docker.exe"), Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", "Docker", "Docker", "resources", "bin", "docker.exe"), Environment.GetEnvironmentVariable("DOCKER_CLI_PATH") };
        foreach (var p in candidates) if (!string.IsNullOrWhiteSpace(p) && File.Exists(p)) return p;
        var paths = (Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        foreach (var dir in paths) { var p = Path.Combine(dir, "docker.exe"); if (File.Exists(p)) return p; }
        return null;
    }

    private static string GetPrimaryLanIPv4()
    {
        try
        {
            foreach (var nic in NetworkInterface.GetAllNetworkInterfaces().Where(n => n.OperationalStatus == OperationalStatus.Up && n.NetworkInterfaceType is not NetworkInterfaceType.Loopback and not NetworkInterfaceType.Tunnel))
            {
                var props = nic.GetIPProperties(); if (!props.GatewayAddresses.Any(g => g.Address.AddressFamily == AddressFamily.InterNetwork)) continue;
                var ip = props.UnicastAddresses.Select(x => x.Address).FirstOrDefault(x => x.AddressFamily == AddressFamily.InterNetwork && !IPAddress.IsLoopback(x) && !x.ToString().StartsWith("169.254.", StringComparison.Ordinal));
                if (ip is not null) return ip.ToString();
            }
        } catch { }
        return "";
    }

    private static bool IsPrivateIpv4(IPAddress ip)
    {
        var b = ip.GetAddressBytes(); return b[0] == 10 || (b[0] == 172 && b[1] >= 16 && b[1] <= 31) || (b[0] == 192 && b[1] == 168);
    }

    private string ExtractResource(string resourceName, string fileName, string? destination = null)
    {
        destination ??= Path.Combine(RuntimeDir, fileName);
        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(resourceName) ?? throw new FileNotFoundException("Embedded resource missing: " + resourceName);
        using var ms = new MemoryStream(); stream.CopyTo(ms); var bytes = ms.ToArray();
        File.WriteAllBytes(destination, bytes);
        AppendLog($"Kaynak hazırlandı: {Path.GetFileName(destination)} ({bytes.Length:N0} bayt)");
        return destination;
    }

    private static string ReadTextFile(string path, string fallback) { try { return File.Exists(path) ? File.ReadAllText(path, Encoding.UTF8) : fallback; } catch { return fallback; } }
    private static string TailText(string path, int maxChars)
    {
        try
        {
            if (maxChars <= 0 || !File.Exists(path)) return string.Empty;
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            if (stream.Length == 0) return string.Empty;
            // UTF-8 uses at most four bytes per code point. Read only a bounded tail so opening
            // a multi-megabyte build log does not allocate or parse the whole file on the UI thread.
            long bytesToRead = Math.Min(stream.Length, Math.Min((long)maxChars * 4 + 4096, 8_000_000L));
            stream.Seek(-bytesToRead, SeekOrigin.End);
            var bytes = new byte[(int)bytesToRead];
            int offset = 0;
            while (offset < bytes.Length)
            {
                int read = stream.Read(bytes, offset, bytes.Length - offset);
                if (read <= 0) break;
                offset += read;
            }
            string text = Encoding.UTF8.GetString(bytes, 0, offset);
            if (bytesToRead < stream.Length)
            {
                int firstLineBreak = text.IndexOf('\n');
                if (firstLineBreak >= 0 && firstLineBreak + 1 < text.Length) text = text[(firstLineBreak + 1)..];
            }
            if (text.Length > maxChars) text = text[^maxChars..];
            return text;
        }
        catch { return string.Empty; }
    }

    private static async Task<string?> TryReadTextAsync(Uri url)
    {
        try { using var res = await Http.GetAsync(url); if (!res.IsSuccessStatusCode) return null; return await res.Content.ReadAsStringAsync(); } catch { return null; }
    }
}
