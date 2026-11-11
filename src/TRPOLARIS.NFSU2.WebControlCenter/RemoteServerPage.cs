using Microsoft.Win32;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace TRPOLARIS.NFSU2.WebControlCenter;

public partial class MainWindow
{
    private RemoteServerSession? _remoteServerSession;
    private RemoteServerProfile? _remoteConnectedProfile;
    private string? _activeRemoteProfileId;
    private ComboBox? _remoteProfileBox;
    private TextBox? _remoteNameBox;
    private TextBox? _remoteHostBox;
    private TextBox? _remotePortBox;
    private TextBox? _remoteUsernameBox;
    private ComboBox? _remoteAuthenticationBox;
    private TextBox? _remotePrivateKeyBox;
    private PasswordBox? _remotePasswordBox;
    private PasswordBox? _remoteKeyPassphraseBox;
    private TextBlock? _remoteConnectionState;
    private TextBox? _remoteDiagnosticsBox;
    private TextBox? _remoteGameFolderBox;
    private TextBox? _remoteWasmPathBox;
    private TextBox? _remoteJsonPathBox;
    private TextBox? _remoteDomainBox;
    private TextBox? _remoteInstallRootBox;
    private TextBox? _remoteHttpPortBox;
    private ComboBox? _remoteAccessModeBox;
    private TextBlock? _remoteAccessAddressLabel;
    private TextBlock? _remoteAccessHint;
    private ComboBox? _remoteProxyModeBox;
    private TextBlock? _remoteDeploymentState;
    private ProgressBar? _remoteDeploymentProgress;
    private TextBox? _remoteFilePathBox;
    private TextBox? _remoteNewFolderBox;
    private ListBox? _remoteFileList;
    private TextBlock? _remoteFileStatus;



    private UIElement BuildRemotePage()
    {
        var root = NewStack(17);
        root.Children.Add(Heading(
            "Uzak Linux sunucusu",
            "SSH üzerinden Ubuntu veya Debian sunucularına bağlan. Bu mod yerel Windows / WSL / Docker Desktop kurulumundan bağımsız çalışır."));

        var profilePanel = RemotePanel();
        var profileStack = NewStack(9);
        profileStack.Children.Add(MutedLabel("KAYITLI BAĞLANTILAR"));
        _remoteProfileBox = new ComboBox
        {
            Height = 36,
            Style = (Style)FindResource("DarkComboBox"),
            Background = BrushHex("#0A1724"),
            Foreground = BrushHex("#EAF3FD"),
            BorderBrush = BrushHex("#34506A"),
            BorderThickness = new Thickness(1),
            Padding = new Thickness(8, 5, 8, 5),
            DisplayMemberPath = nameof(RemoteServerProfile.Name),
            ItemsSource = RemoteServerProfileStore.LoadAll().ToList()
        };
        _remoteProfileBox.SelectionChanged += RemoteProfileSelectionChanged;
        var profileRow = new DockPanel { LastChildFill = true };
        var newProfileButton = MakeButton("Yeni profil", "SecondaryButton", (_, _) => NewRemoteProfile(), new Thickness(8, 0, 0, 0));
        DockPanel.SetDock(newProfileButton, Dock.Right);
        profileRow.Children.Add(newProfileButton);
        profileRow.Children.Add(_remoteProfileBox);
        profileStack.Children.Add(profileRow);
        profilePanel.Child = profileStack;
        root.Children.Add(profilePanel);

        var columns = new Grid();
        columns.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.08, GridUnitType.Star) });
        columns.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(0.92, GridUnitType.Star) });

        var formPanel = RemotePanel();
        formPanel.Margin = new Thickness(0, 0, 8, 0);
        var form = NewStack(10);
        form.Children.Add(RemoteSectionTitle("SSH BAĞLANTI AYARLARI"));

        _remoteNameBox = RemoteTextBox();
        _remoteHostBox = RemoteTextBox();
        _remotePortBox = RemoteTextBox();
        _remotePortBox.Text = "22";
        _remoteUsernameBox = RemoteTextBox();
        _remoteAuthenticationBox = new ComboBox
        {
            Height = 36,
            Style = (Style)FindResource("DarkComboBox"),
            Background = BrushHex("#0A1724"),
            Foreground = BrushHex("#EAF3FD"),
            BorderBrush = BrushHex("#34506A"),
            BorderThickness = new Thickness(1),
            Padding = new Thickness(8, 5, 8, 5),
            ItemsSource = new[] { "Parola", "SSH özel anahtarı" },
            SelectedIndex = 0
        };
        _remotePrivateKeyBox = RemoteTextBox();
        _remotePasswordBox = new PasswordBox
        {
            Height = 36,
            Background = BrushHex("#0A1724"),
            Foreground = BrushHex("#EAF3FD"),
            BorderBrush = BrushHex("#34506A"),
            BorderThickness = new Thickness(1),
            Padding = new Thickness(8, 5, 8, 5)
        };
        _remoteKeyPassphraseBox = new PasswordBox
        {
            Height = 36,
            Background = BrushHex("#0A1724"),
            Foreground = BrushHex("#EAF3FD"),
            BorderBrush = BrushHex("#34506A"),
            BorderThickness = new Thickness(1),
            Padding = new Thickness(8, 5, 8, 5)
        };

        form.Children.Add(RemoteField("Profil adı", _remoteNameBox));
        form.Children.Add(RemoteField("Sunucu IP adresi veya domain", _remoteHostBox));

        var connectionGrid = new Grid();
        connectionGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        connectionGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        var portField = RemoteField("SSH portu", _remotePortBox);
        var userField = RemoteField("Linux kullanıcı adı", _remoteUsernameBox);
        userField.Margin = new Thickness(8, 0, 0, 0);
        connectionGrid.Children.Add(portField);
        Grid.SetColumn(userField, 1);
        connectionGrid.Children.Add(userField);
        form.Children.Add(connectionGrid);
        form.Children.Add(RemoteField("Kimlik doğrulama", _remoteAuthenticationBox));

        var keyGrid = new Grid();
        keyGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        keyGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        keyGrid.Children.Add(RemoteField("Özel anahtar dosyası", _remotePrivateKeyBox));
        var browse = MakeButton("Gözat", "SecondaryButton", BrowseRemotePrivateKey, new Thickness(8, 21, 0, 0));
        browse.MinWidth = 76;
        Grid.SetColumn(browse, 1);
        keyGrid.Children.Add(browse);
        form.Children.Add(keyGrid);
        form.Children.Add(RemoteField("SSH parolası (profilde saklanmaz)", _remotePasswordBox));
        form.Children.Add(RemoteField("Anahtar parolası (varsa)", _remoteKeyPassphraseBox));

        var buttonGrid = new WrapPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 5, 0, 0) };
        buttonGrid.Children.Add(MakeButton("Profili kaydet", "SecondaryButton", (_, _) => SaveRemoteProfile(), new Thickness(0, 0, 7, 7)));
        buttonGrid.Children.Add(MakeButton("Bağlan", "ModernButton", async (_, _) => await ConnectRemoteAsync(keepConnected: true), new Thickness(0, 0, 7, 7)));
        buttonGrid.Children.Add(MakeButton("Bağlantıyı test et", "SecondaryButton", async (_, _) => await ConnectRemoteAsync(keepConnected: false), new Thickness(0, 0, 7, 7)));
        buttonGrid.Children.Add(MakeButton("Bağlantıyı kes", "DangerButton", (_, _) => DisconnectRemoteSession(), new Thickness(0, 0, 7, 7)));
        form.Children.Add(buttonGrid);
        form.Children.Add(MakeButton("Seçili profili sil", "DangerButton", (_, _) => DeleteRemoteProfile(), new Thickness(0, 2, 0, 0)));
        formPanel.Child = form;
        Grid.SetColumn(formPanel, 0);
        columns.Children.Add(formPanel);

        var statusPanel = RemotePanel();
        statusPanel.Margin = new Thickness(8, 0, 0, 0);
        var status = NewStack(10);
        status.Children.Add(RemoteSectionTitle("UZAK SUNUCU DURUMU"));
        _remoteConnectionState = new TextBlock
        {
            Text = _remoteServerSession?.IsConnected == true ? "SSH BAĞLANTISI AÇIK" : "BAĞLANTI BEKLENİYOR",
            Foreground = _remoteServerSession?.IsConnected == true ? BrushHex("#65D6B0") : BrushHex("#E5B65B"),
            FontSize = 10,
            FontWeight = FontWeights.Bold,
            TextWrapping = TextWrapping.Wrap
        };
        status.Children.Add(_remoteConnectionState);
        var detailRow = new DockPanel { LastChildFill = true };
        var refresh = MakeButton("Durumu yenile", "SecondaryButton", async (_, _) => await RefreshRemoteDiagnosticsAsync(), new Thickness(0, 0, 0, 4));
        DockPanel.SetDock(refresh, Dock.Right);
        detailRow.Children.Add(refresh);
        detailRow.Children.Add(new TextBlock
        {
            Text = "Sistem bilgileri ve Docker kontrolü",
            Foreground = BrushHex("#A9BED1"),
            FontSize = 10,
            VerticalAlignment = VerticalAlignment.Center
        });
        status.Children.Add(detailRow);
        _remoteDiagnosticsBox = new TextBox
        {
            IsReadOnly = true,
            AcceptsReturn = true,
            TextWrapping = TextWrapping.Wrap,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
            MinHeight = 290,
            Background = BrushHex("#07111C"),
            Foreground = BrushHex("#CFE1EF"),
            BorderBrush = BrushHex("#263D52"),
            BorderThickness = new Thickness(1),
            Padding = new Thickness(12),
            FontFamily = new FontFamily("Consolas"),
            FontSize = 11,
            Text = _remoteServerSession?.IsConnected == true
                ? "Bağlantı açık. Sunucu bilgilerini görüntülemek için 'Durumu yenile' seçeneğini kullan."
                : "Henüz uzak sunucuya bağlanılmadı."
        };
        status.Children.Add(_remoteDiagnosticsBox);
        status.Children.Add(new TextBlock
        {
            Text = "Güvenlik: İlk bağlantıda SSH host key parmak izi gösterilir ve açık onay olmadan bağlantı kurulmaz. Parola ve özel anahtar parolası profil dosyasına kaydedilmez.",
            Foreground = BrushHex("#8FA8BD"),
            FontSize = 10,
            TextWrapping = TextWrapping.Wrap
        });
        status.Children.Add(MakeButton("Kayıtlı host key bilgisini sıfırla", "SecondaryButton", (_, _) => ForgetRemoteHostKey()));
        statusPanel.Child = status;
        Grid.SetColumn(statusPanel, 1);
        columns.Children.Add(statusPanel);

        root.Children.Add(columns);

        RefreshRemoteProfileList(_activeRemoteProfileId);
        return new ScrollViewer
        {
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            Content = root
        };
    }

    private UIElement BuildRemoteDeploymentPage()
    {
        var root = NewStack(22);
        root.Children.Add(Heading("Uzak sunucu dağıtımı", "Oyun dosyaları, WASM modülü, yayın adresi ve servis yaşam döngüsü. SSH oturumunu ayrı Bağlantılar ekranından yönet."));
        var deployPanel = RemotePanel();
        var deployStack = NewStack(10);
        deployStack.Children.Add(RemoteSectionTitle("UZAK SUNUCU KURULUMU VE YÖNETİMİ"));
        deployStack.Children.Add(new TextBlock
        {
            Text = "Kullanıcının kendi oyun klasöründen gerekli dosyalar güvenli dosya listesiyle VPS'ye aktarılır. WASM modülü yerel NFSU2 modül çıktısından alınır. Oyun arşivi veya başka kişisel dosyalar gönderilmez.",
            Foreground = BrushHex("#9FB4C8"),
            FontSize = 10,
            TextWrapping = TextWrapping.Wrap
        });

        _remoteGameFolderBox = RemoteTextBox();
        _remoteGameFolderBox.Text = Nfsu2ConfigStore.Read(RootDir).Install.WindowsGameDirectory;
        _remoteWasmPathBox = RemoteTextBox();
        _remoteWasmPathBox.Text = Path.Combine(RootDir, "module", "nfsu2.wasm");
        _remoteJsonPathBox = RemoteTextBox();
        _remoteJsonPathBox.Text = Path.Combine(RootDir, "module", "nfsu2.json");
        _remoteDomainBox = RemoteTextBox();
        _remoteAccessModeBox = new ComboBox
        {
            Height = 38,
            Style = (Style)FindResource("DarkComboBox"),
            ItemsSource = new[] { "Yerel ağ (LAN)", "Public IP (İnternet)", "Domain (HTTPS)" },
            SelectedIndex = 2
        };
        _remoteAccessModeBox.SelectionChanged += (_, _) => ApplyRemoteAccessModeUi();
        _remoteInstallRootBox = RemoteTextBox();
        _remoteInstallRootBox.Text = "/srv/nfsu2";
        _remoteInstallRootBox.TextChanged += (_, _) =>
        {
            if (_remoteFilePathBox is not null)
                _remoteFilePathBox.Text = _remoteInstallRootBox.Text.TrimEnd('/');
        };
        _remoteHttpPortBox = RemoteTextBox();
        _remoteHttpPortBox.Text = "8080";
        _remoteProxyModeBox = new ComboBox
        {
            Height = 36,
            Style = (Style)FindResource("DarkComboBox"),
            Background = BrushHex("#0A1724"),
            Foreground = BrushHex("#EAF3FD"),
            BorderBrush = BrushHex("#34506A"),
            BorderThickness = new Thickness(1),
            Padding = new Thickness(8, 5, 8, 5),
            ItemsSource = new[] { "Caddy ile otomatik HTTPS (80/443 boş olmalı)", "Mevcut Nginx / OpenLiteSpeed / CyberPanel proxy" },
            SelectedIndex = 0
        };

        var gameRow = new DockPanel { LastChildFill = true };
        gameRow.Children.Add(RemoteField("Yerel NFSU2 oyun klasörü (SPEED2.EXE içermeli)", _remoteGameFolderBox));
        var gameBrowse = MakeButton("Klasör seç", "SecondaryButton", BrowseRemoteGameFolder, new Thickness(8, 21, 0, 0));
        DockPanel.SetDock(gameBrowse, Dock.Right);
        gameRow.Children.Insert(0, gameBrowse);
        deployStack.Children.Add(gameRow);

        var wasmRow = new Grid();
        wasmRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        wasmRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        wasmRow.Children.Add(RemoteField("WASM modülü", _remoteWasmPathBox));
        var wasmBrowse = MakeButton("Dosya seç", "SecondaryButton", BrowseRemoteWasm, new Thickness(8, 21, 0, 0));
        wasmBrowse.MinWidth = 84;
        Grid.SetColumn(wasmBrowse, 1);
        wasmRow.Children.Add(wasmBrowse);
        deployStack.Children.Add(wasmRow);

        var jsonRow = new Grid();
        jsonRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        jsonRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        jsonRow.Children.Add(RemoteField("WASM sürüm bilgisi (nfsu2.json)", _remoteJsonPathBox));
        var jsonBrowse = MakeButton("Dosya seç", "SecondaryButton", BrowseRemoteJson, new Thickness(8, 21, 0, 0));
        jsonBrowse.MinWidth = 84;
        Grid.SetColumn(jsonBrowse, 1);
        jsonRow.Children.Add(jsonBrowse);
        deployStack.Children.Add(jsonRow);

        deployStack.Children.Add(RemoteField("Erişim türü", _remoteAccessModeBox));
        var endpointField = RemoteField("Oyuncuların kullanacağı domain veya IP", _remoteDomainBox);
        _remoteAccessAddressLabel = (TextBlock)endpointField.Children[0];
        deployStack.Children.Add(endpointField);
        _remoteAccessHint = new TextBlock
        {
            Text = "Domain için DNS kaydını VPS IP adresine yönlendir. Caddy HTTPS sertifikasını otomatik alır.",
            Foreground = BrushHex("#A9BED1"),
            FontSize = 10,
            TextWrapping = TextWrapping.Wrap
        };
        deployStack.Children.Add(_remoteAccessHint);

        var configGrid = new Grid();
        configGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        configGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.2, GridUnitType.Star) });
        configGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        configGrid.Children.Add(RemoteField("Kurulum dizini", _remoteInstallRootBox));
        var httpField = RemoteField("İç HTTP portu", _remoteHttpPortBox);
        httpField.Margin = new Thickness(8, 0, 0, 0);
        Grid.SetColumn(httpField, 1);
        configGrid.Children.Add(httpField);
        var proxyField = RemoteField("HTTPS / reverse proxy", _remoteProxyModeBox);
        proxyField.Margin = new Thickness(8, 0, 0, 0);
        Grid.SetColumn(proxyField, 2);
        configGrid.Children.Add(proxyField);
        deployStack.Children.Add(configGrid);

        _remoteDeploymentState = new TextBlock
        {
            Text = "Kurulum veya güncelleme için uzak sunucuya bağlan.",
            Foreground = BrushHex("#E5B65B"),
            FontSize = 10,
            TextWrapping = TextWrapping.Wrap
        };
        deployStack.Children.Add(_remoteDeploymentState);
        _remoteDeploymentProgress = new ProgressBar
        {
            Height = 7,
            Minimum = 0,
            Maximum = 100,
            Value = 0,
            Background = BrushHex("#091421"),
            Foreground = BrushHex("#42C9C0")
        };
        deployStack.Children.Add(_remoteDeploymentProgress);

        var deployButtons = new WrapPanel { Orientation = Orientation.Horizontal };
        deployButtons.Children.Add(MakeButton("Uzak kurulumu başlat", "ModernButton", async (_, _) => await InstallRemoteServerAsync(), new Thickness(0, 0, 7, 7)));
        deployButtons.Children.Add(MakeButton("Altyapıyı güncelle", "SecondaryButton", async (_, _) => await UpdateRemoteServerAsync(), new Thickness(0, 0, 7, 7)));
        deployButtons.Children.Add(MakeButton("Sunucuyu başlat", "SecondaryButton", async (_, _) => await RunRemoteServiceActionAsync("start"), new Thickness(0, 0, 7, 7)));
        deployButtons.Children.Add(MakeButton("Sunucuyu durdur", "SecondaryButton", async (_, _) => await RunRemoteServiceActionAsync("stop"), new Thickness(0, 0, 7, 7)));
        deployButtons.Children.Add(MakeButton("Yeniden başlat", "SecondaryButton", async (_, _) => await RunRemoteServiceActionAsync("restart"), new Thickness(0, 0, 7, 7)));
        deployButtons.Children.Add(MakeButton("Canlı günlükler", "SecondaryButton", async (_, _) => await ReadRemoteLogsAsync(), new Thickness(0, 0, 7, 7)));
        deployButtons.Children.Add(MakeButton("LAN sertifikasını indir", "SecondaryButton", async (_, _) => await DownloadRemoteLanCertificateAsync(true), new Thickness(0, 0, 7, 7)));
        deployButtons.Children.Add(MakeButton("Uzak kurulumu kaldır", "DangerButton", async (_, _) => await UninstallRemoteServerAsync(), new Thickness(0, 0, 7, 7)));
        deployButtons.Children.Add(MakeButton("Tüm verileri kalıcı sil", "DangerButton", async (_, _) => await PurgeRemoteServerAsync(), new Thickness(0, 0, 7, 7)));
        deployStack.Children.Add(deployButtons);
        deployStack.Children.Add(new TextBlock
        {
            Text = "Normal kaldırma oyun dosyalarını ve kayıt hacmini korur. 'Tüm verileri kalıcı sil' ayrı iki onay ister ve yalnızca bu uzak kurulumun oyun/modül klasörünü, konteynerlerini ve Compose volume'lerini kaldırır.",
            Foreground = BrushHex("#8FA8BD"),
            FontSize = 10,
            TextWrapping = TextWrapping.Wrap
        });
        deployPanel.Child = deployStack;
        root.Children.Add(deployPanel);

        if (_remoteProfileBox?.SelectedItem is RemoteServerProfile selectedProfile)
        {
            _remoteInstallRootBox.Text = string.IsNullOrWhiteSpace(selectedProfile.RemoteProjectDirectory) ? "/srv/nfsu2" : selectedProfile.RemoteProjectDirectory;
            _remoteDomainBox.Text = selectedProfile.Domain ?? "";
            _remoteAccessModeBox.SelectedIndex = selectedProfile.AccessMode switch { "lan" => 0, "public-ip" => 1, _ => 2 };
            _remoteHttpPortBox.Text = selectedProfile.HttpPort.ToString();
            _remoteProxyModeBox.SelectedIndex = string.Equals(selectedProfile.ProxyMode, "existing-proxy", StringComparison.OrdinalIgnoreCase) ? 1 : 0;
        }
        ApplyRemoteAccessModeUi();
        return WrapScrollable(root);
    }

    private UIElement BuildRemoteFilesPage()
    {
        var root = NewStack(22);
        root.Children.Add(Heading("Uzak dosyalar", "SFTP çalışma alanı. Gezinti yalnızca seçili NFSU2 kurulumunun işaretlenmiş dizini içinde kalır."));
        var filePanel = RemotePanel();
        var fileStack = NewStack(9);
        fileStack.Children.Add(RemoteSectionTitle("UZAK DOSYA YÖNETİCİSİ · SFTP"));
        fileStack.Children.Add(new TextBlock
        {
            Text = "Dosyalar yalnızca yukarıdaki kurulum dizini içinde görüntülenebilir. Sembolik bağlantılar gösterilmez. Klasör silme işlemi yalnızca boş klasörlerde çalışır.",
            Foreground = BrushHex("#9FB4C8"),
            FontSize = 10,
            TextWrapping = TextWrapping.Wrap
        });
        _remoteFilePathBox = RemoteTextBox();
        _remoteFilePathBox.Text = GetRemoteInstallRootValue().TrimEnd('/');
        _remoteFilePathBox.IsReadOnly = true;
        var pathRow = new DockPanel { LastChildFill = true };
        var listButton = MakeButton("Listele", "SecondaryButton", async (_, _) => await RefreshRemoteFileListAsync(), new Thickness(8, 0, 0, 0));
        DockPanel.SetDock(listButton, Dock.Right);
        var upButton = MakeButton("Üst klasör", "SecondaryButton", async (_, _) => await NavigateRemoteParentAsync(), new Thickness(8, 0, 0, 0));
        DockPanel.SetDock(upButton, Dock.Right);
        pathRow.Children.Add(listButton);
        pathRow.Children.Add(upButton);
        pathRow.Children.Add(_remoteFilePathBox);
        var pathField = new StackPanel { Orientation = Orientation.Vertical };
        pathField.Children.Add(new TextBlock { Text = "Uzak klasör yolu", FontSize = 10, Foreground = BrushHex("#9FB4C8"), Margin = new Thickness(0, 0, 0, 5) });
        pathField.Children.Add(pathRow);
        fileStack.Children.Add(pathField);

        _remoteFileList = new ListBox
        {
            MinHeight = 190,
            MaxHeight = 280,
            Background = BrushHex("#07111C"),
            Foreground = BrushHex("#CFE1EF"),
            BorderBrush = BrushHex("#263D52"),
            BorderThickness = new Thickness(1),
            Padding = new Thickness(6),
            FontFamily = new FontFamily("Consolas"),
            FontSize = 10,
            SelectionMode = SelectionMode.Single
        };
        _remoteFileList.MouseDoubleClick += RemoteFileList_MouseDoubleClick;
        fileStack.Children.Add(_remoteFileList);

        var fileActions = new WrapPanel { Orientation = Orientation.Horizontal };
        fileActions.Children.Add(MakeButton("Dosya indir", "SecondaryButton", async (_, _) => await DownloadSelectedRemoteFileAsync(), new Thickness(0, 0, 7, 7)));
        fileActions.Children.Add(MakeButton("Dosya yükle", "SecondaryButton", async (_, _) => await UploadRemoteFileAsync(), new Thickness(0, 0, 7, 7)));
        fileActions.Children.Add(MakeButton("Seçileni sil", "DangerButton", async (_, _) => await DeleteSelectedRemoteEntryAsync(), new Thickness(0, 0, 7, 7)));
        fileStack.Children.Add(fileActions);

        _remoteNewFolderBox = RemoteTextBox();
        _remoteNewFolderBox.Width = 240;
        var folderRow = new DockPanel { LastChildFill = true };
        var createFolderButton = MakeButton("Klasör oluştur", "SecondaryButton", async (_, _) => await CreateRemoteFolderAsync(), new Thickness(8, 0, 0, 0));
        DockPanel.SetDock(createFolderButton, Dock.Right);
        folderRow.Children.Add(createFolderButton);
        folderRow.Children.Add(_remoteNewFolderBox);
        var folderField = new StackPanel { Orientation = Orientation.Vertical };
        folderField.Children.Add(new TextBlock { Text = "Yeni klasör adı", FontSize = 10, Foreground = BrushHex("#9FB4C8"), Margin = new Thickness(0, 0, 0, 5) });
        folderField.Children.Add(folderRow);
        fileStack.Children.Add(folderField);
        _remoteFileStatus = new TextBlock
        {
            Text = "SSH bağlantısı kurulduğunda Listele ile uzak dosyaları görüntüleyebilirsin.",
            Foreground = BrushHex("#8FA8BD"),
            FontSize = 10,
            TextWrapping = TextWrapping.Wrap
        };
        fileStack.Children.Add(_remoteFileStatus);
        filePanel.Child = fileStack;
        root.Children.Add(filePanel);

        return WrapScrollable(root);
    }

    private static Border RemotePanel() => new()
    {
        Background = BrushHex("#101C2A"),
        BorderBrush = BrushHex("#29445D"),
        BorderThickness = new Thickness(1),
        CornerRadius = new CornerRadius(12),
        Padding = new Thickness(16)
    };

    private static TextBlock RemoteSectionTitle(string text) => new()
    {
        Text = text,
        FontSize = 9,
        FontWeight = FontWeights.Bold,
        Foreground = BrushHex("#70CFC8")
    };

    private static TextBox RemoteTextBox() => new()
    {
        Height = 36,
        Background = BrushHex("#0A1724"),
        Foreground = BrushHex("#EAF3FD"),
        BorderBrush = BrushHex("#34506A"),
        BorderThickness = new Thickness(1),
        Padding = new Thickness(9, 6, 9, 6),
        VerticalContentAlignment = VerticalAlignment.Center
    };

    private static StackPanel RemoteField(string label, Control control)
    {
        var panel = new StackPanel { Orientation = Orientation.Vertical };
        panel.Children.Add(new TextBlock
        {
            Text = label,
            FontSize = 10,
            Foreground = BrushHex("#9FB4C8"),
            Margin = new Thickness(0, 0, 0, 5)
        });
        panel.Children.Add(control);
        return panel;
    }

    private RemoteServerProfile? GetSelectedRemoteProfile()
    {
        if (_remoteProfileBox?.SelectedItem is RemoteServerProfile selected)
            return selected;
        var profiles = RemoteServerProfileStore.LoadAll().ToList();
        return profiles.FirstOrDefault(x => string.Equals(x.Id, _activeRemoteProfileId, StringComparison.Ordinal))
            ?? profiles.FirstOrDefault();
    }

    private string GetRemoteInstallRootValue()
    {
        string configured = _remoteInstallRootBox?.Text.Trim() ?? "";
        if (!string.IsNullOrWhiteSpace(configured))
            return ValidateRemoteInstallRoot(configured);
        var profile = GetSelectedRemoteProfile();
        return ValidateRemoteInstallRoot(profile?.RemoteProjectDirectory ?? "/srv/nfsu2");
    }

    private string GetRemoteAccessMode() => _remoteAccessModeBox?.SelectedIndex switch
    {
        0 => "lan",
        1 => "public-ip",
        _ => "domain"
    };

    private void ApplyRemoteAccessModeUi()
    {
        string mode = GetRemoteAccessMode();
        if (_remoteAccessAddressLabel is not null)
            _remoteAccessAddressLabel.Text = mode switch
            {
                "lan" => "LAN IPv4 adresi (boş bırakılırsa otomatik algılanır)",
                "public-ip" => "Public IPv4 adresi (boş bırakılırsa otomatik algılanır)",
                _ => "Domain adı (DNS bu VPS IP adresine yönelmeli)"
            };
        if (_remoteAccessHint is not null)
        {
            _remoteAccessHint.Text = mode switch
            {
                "lan" => "Caddy yerel CA ile HTTPS kurar. Diğer cihazlarda LAN sertifikasını indirip Güvenilen Kök Sertifika Yetkilileri deposuna eklemelisin. Caddy yalnızca seçilen LAN IP'sine bağlanır.",
                "public-ip" => "Public IP için Let's Encrypt kısa ömürlü IP sertifikası kullanılır. Sunucunun IP'si sabit olmalı; TCP 80 ve 443 sağlayıcı güvenlik duvarında açık olmalı.",
                _ => "Domain için DNS kaydını VPS IP adresine yönlendir. Caddy otomatik HTTPS sertifikası alır veya mevcut Nginx/OpenLiteSpeed proxy kullanılır."
            };
        }
        if (_remoteProxyModeBox is not null)
        {
            bool domain = mode == "domain";
            _remoteProxyModeBox.IsEnabled = domain;
            _remoteProxyModeBox.Opacity = domain ? 1 : 0.55;
            if (!domain) _remoteProxyModeBox.SelectedIndex = 0;
        }
    }

    private void RefreshRemoteProfileList(string? selectedId)
    {
        if (_remoteProfileBox is null) return;
        var profiles = RemoteServerProfileStore.LoadAll().ToList();
        _remoteProfileBox.ItemsSource = profiles;
        _remoteProfileBox.SelectedItem = profiles.FirstOrDefault(x =>
            string.Equals(x.Id, selectedId, StringComparison.Ordinal));
        if (_remoteProfileBox.SelectedItem is null && profiles.Count > 0)
            _remoteProfileBox.SelectedIndex = 0;
    }

    private void NewRemoteProfile()
    {
        if (_remoteServerSession?.IsConnected == true) DisconnectRemoteSession();
        if (_remoteProfileBox is not null) _remoteProfileBox.SelectedItem = null;
        _activeRemoteProfileId = null;
        if (_remoteNameBox is not null) _remoteNameBox.Text = "";
        if (_remoteHostBox is not null) _remoteHostBox.Text = "";
        if (_remotePortBox is not null) _remotePortBox.Text = "22";
        if (_remoteUsernameBox is not null) _remoteUsernameBox.Text = "";
        if (_remoteAuthenticationBox is not null) _remoteAuthenticationBox.SelectedIndex = 0;
        if (_remotePrivateKeyBox is not null) _remotePrivateKeyBox.Text = "";
        if (_remotePasswordBox is not null) _remotePasswordBox.Password = "";
        if (_remoteKeyPassphraseBox is not null) _remoteKeyPassphraseBox.Password = "";
        if (_remoteInstallRootBox is not null) _remoteInstallRootBox.Text = "/srv/nfsu2";
        if (_remoteDomainBox is not null) _remoteDomainBox.Text = "";
        if (_remoteAccessModeBox is not null) _remoteAccessModeBox.SelectedIndex = 2;
        if (_remoteHttpPortBox is not null) _remoteHttpPortBox.Text = "8080";
        if (_remoteProxyModeBox is not null) _remoteProxyModeBox.SelectedIndex = 0;
        if (_remoteFilePathBox is not null) _remoteFilePathBox.Text = "/srv/nfsu2";
        SetRemoteStatus("Yeni bağlantı profili oluşturuluyor.", connected: false);
    }

    private void RemoteProfileSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_remoteProfileBox?.SelectedItem is not RemoteServerProfile profile) return;
        if (_remoteServerSession?.IsConnected == true &&
            !string.Equals(_activeRemoteProfileId, profile.Id, StringComparison.Ordinal))
            DisconnectRemoteSession();
        _remoteNameBox!.Text = profile.Name;
        _remoteHostBox!.Text = profile.Host;
        _remotePortBox!.Text = profile.Port.ToString();
        _remoteUsernameBox!.Text = profile.Username;
        _remoteAuthenticationBox!.SelectedIndex =
            string.Equals(profile.AuthenticationMode, "private-key", StringComparison.OrdinalIgnoreCase) ? 1 : 0;
        _remotePrivateKeyBox!.Text = profile.PrivateKeyPath;
        if (_remoteInstallRootBox is not null)
            _remoteInstallRootBox.Text = string.IsNullOrWhiteSpace(profile.RemoteProjectDirectory) ? "/srv/nfsu2" : profile.RemoteProjectDirectory;
        if (_remoteDomainBox is not null) _remoteDomainBox.Text = profile.Domain ?? "";
        if (_remoteAccessModeBox is not null) _remoteAccessModeBox.SelectedIndex = profile.AccessMode switch { "lan" => 0, "public-ip" => 1, _ => 2 };
        if (_remoteHttpPortBox is not null) _remoteHttpPortBox.Text = profile.HttpPort.ToString();
        if (_remoteProxyModeBox is not null)
            _remoteProxyModeBox.SelectedIndex = string.Equals(profile.ProxyMode, "existing-proxy", StringComparison.OrdinalIgnoreCase) ? 1 : 0;
        if (_remoteFilePathBox is not null)
            _remoteFilePathBox.Text = (profile.RemoteProjectDirectory ?? "/srv/nfsu2").TrimEnd('/');
        if (_remotePasswordBox is not null) _remotePasswordBox.Password = "";
        if (_remoteKeyPassphraseBox is not null) _remoteKeyPassphraseBox.Password = "";
        _activeRemoteProfileId = profile.Id;
    }

    private RemoteServerProfile ReadRemoteProfile()
    {
        string name = _remoteNameBox?.Text.Trim() ?? "";
        string host = _remoteHostBox?.Text.Trim() ?? "";
        string username = _remoteUsernameBox?.Text.Trim() ?? "";
        if (string.IsNullOrWhiteSpace(name)) name = host;
        if (string.IsNullOrWhiteSpace(name)) throw new InvalidOperationException("Profil adı gir.");
        if (Uri.CheckHostName(host) == UriHostNameType.Unknown)
            throw new InvalidOperationException("Geçerli bir sunucu IP adresi veya domain gir.");
        if (!int.TryParse(_remotePortBox?.Text.Trim(), out int port) || port is < 1 or > 65535)
            throw new InvalidOperationException("SSH portu 1 ile 65535 arasında olmalı.");
        if (string.IsNullOrWhiteSpace(username) || username.Length > 128)
            throw new InvalidOperationException("Geçerli bir Linux kullanıcı adı gir.");

        var selected = _remoteProfileBox?.SelectedItem as RemoteServerProfile;
        bool privateKey = _remoteAuthenticationBox?.SelectedIndex == 1;
        string keyPath = _remotePrivateKeyBox?.Text.Trim() ?? "";
        if (privateKey && (string.IsNullOrWhiteSpace(keyPath) || !File.Exists(keyPath)))
            throw new InvalidOperationException("SSH özel anahtar dosyasını seç.");

        return new RemoteServerProfile
        {
            Id = selected?.Id ?? Guid.NewGuid().ToString("N"),
            Name = name,
            Host = host,
            Port = port,
            Username = username,
            AuthenticationMode = privateKey ? "private-key" : "password",
            PrivateKeyPath = privateKey ? keyPath : "",
            HostKeyFingerprint = selected?.HostKeyFingerprint ?? "",
            RemoteProjectDirectory = _remoteInstallRootBox is not null
                ? ValidateRemoteInstallRoot(_remoteInstallRootBox.Text)
                : ValidateRemoteInstallRoot(selected?.RemoteProjectDirectory ?? "/srv/nfsu2"),
            AccessMode = _remoteAccessModeBox?.SelectedIndex switch
                { 0 => "lan", 1 => "public-ip", _ => _remoteAccessModeBox is null ? selected?.AccessMode ?? "domain" : "domain" },
            Domain = _remoteDomainBox?.Text.Trim() ?? selected?.Domain ?? "",
            HttpPort = _remoteHttpPortBox is not null ? ParseRemoteHttpPort() : selected?.HttpPort ?? 8080,
            ProxyMode = _remoteProxyModeBox is not null
                ? (_remoteProxyModeBox.SelectedIndex == 1 ? "existing-proxy" : "caddy")
                : selected?.ProxyMode ?? "caddy",
            LastConnectedUtc = selected?.LastConnectedUtc
        };
    }

    private void SaveRemoteProfile()
    {
        try
        {
            var profile = ReadRemoteProfile();
            var previous = _remoteProfileBox?.SelectedItem as RemoteServerProfile;
            bool connectionChanged = previous is not null &&
                (!string.Equals(previous.Host, profile.Host, StringComparison.OrdinalIgnoreCase) ||
                 previous.Port != profile.Port ||
                 !string.Equals(previous.Username, profile.Username, StringComparison.Ordinal) ||
                 !string.Equals(previous.AuthenticationMode, profile.AuthenticationMode, StringComparison.OrdinalIgnoreCase) ||
                 !string.Equals(previous.PrivateKeyPath, profile.PrivateKeyPath, StringComparison.OrdinalIgnoreCase));
            if (_remoteServerSession?.IsConnected == true && (previous is null || connectionChanged))
                DisconnectRemoteSession();
            RemoteServerProfileStore.SaveOrUpdate(profile);
            _activeRemoteProfileId = profile.Id;
            RefreshRemoteProfileList(profile.Id);
            SetRemoteStatus("Profil kaydedildi. Parola güvenlik nedeniyle kaydedilmedi.", connected: false);
            AppendLog($"Uzak sunucu profili kaydedildi: {profile.Name} ({profile.Host}:{profile.Port})", "OK", "REMOTE");
        }
        catch (Exception ex) { ShowRemoteError(ex.Message); }
    }

    private async Task ConnectRemoteAsync(bool keepConnected)
    {
        RemoteServerSession? session = null;
        try
        {
            var profile = ReadRemoteProfile();
            if (_remoteServerSession?.IsConnected == true &&
                _remoteConnectedProfile is not null &&
                IsSameRemoteEndpoint(_remoteConnectedProfile, profile) &&
                keepConnected)
            {
                await RefreshRemoteDiagnosticsAsync();
                return;
            }
            string password = _remotePasswordBox?.Password ?? "";
            string keyPassphrase = _remoteKeyPassphraseBox?.Password ?? "";
            if (_remoteServerSession?.IsConnected == true)
                DisconnectRemoteSession();
            if (!string.Equals(profile.AuthenticationMode, "private-key", StringComparison.OrdinalIgnoreCase) &&
                string.IsNullOrEmpty(password))
                throw new InvalidOperationException("SSH parolasını gir. Güvenlik nedeniyle parola profilde saklanmaz.");

            SetRemoteStatus("SSH bağlantısı kuruluyor...", connected: false);
            session = new RemoteServerSession(profile);

            try
            {
                await session.ConnectAsync(password, keyPassphrase);
            }
            catch (RemoteHostKeyTrustRequiredException trustRequest)
            {
                string prompt = $"Sunucu: {profile.Host}:{profile.Port}\nAnahtar türü: SSH host key\nParmak izi: {trustRequest.Fingerprint}\n\nBu parmak izini sunucu sağlayıcının konsolundan veya güvenilir başka bir kanaldan doğrula. Doğruladıysan bu anahtara güvenip bağlanmak istiyor musun?";
                if (MessageBox.Show(prompt, "İlk SSH bağlantısı · Host key doğrulaması",
                    MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes)
                {
                    SetRemoteStatus("Host key onayı verilmedi; bağlantı reddedildi.", connected: false);
                    return;
                }

                profile.HostKeyFingerprint = trustRequest.Fingerprint;
                RemoteServerProfileStore.SaveOrUpdate(profile);
                RefreshRemoteProfileList(profile.Id);
                await session.ConnectAsync(password, keyPassphrase);
            }

            if (_remotePasswordBox is not null) _remotePasswordBox.Password = "";
            if (_remoteKeyPassphraseBox is not null) _remoteKeyPassphraseBox.Password = "";
            string diagnostics = await session.GetServerDiagnosticsAsync();
            profile.LastConnectedUtc = DateTimeOffset.UtcNow;
            RemoteServerProfileStore.SaveOrUpdate(profile);
            _activeRemoteProfileId = profile.Id;
            RefreshRemoteProfileList(profile.Id);

            if (keepConnected)
            {
                _remoteServerSession?.Dispose();
                _remoteServerSession = session;
                _remoteConnectedProfile = profile;
                session = null;
                SetRemoteStatus($"Bağlandı: {profile.Username}@{profile.Host}:{profile.Port}", connected: true);
            }
            else
            {
                SetRemoteStatus("Bağlantı testi başarılı. Oturum kapatıldı.", connected: false);
            }

            if (_remoteDiagnosticsBox is not null) _remoteDiagnosticsBox.Text = diagnostics;
            AppendLog($"Uzak Linux sunucusuna SSH bağlantısı başarılı: {profile.Host}:{profile.Port}", "OK", "REMOTE");
        }
        catch (RemoteHostKeyMismatchException ex)
        {
            SetRemoteStatus("Host key değişikliği algılandı; bağlantı güvenlik nedeniyle reddedildi.", connected: false);
            ShowRemoteError(ex.Message + Environment.NewLine +
                "Kayıtlı: " + ex.ExpectedFingerprint + Environment.NewLine +
                "Sunucudan gelen: " + ex.ReceivedFingerprint +
                Environment.NewLine + "Sunucu yeniden kurulduysa veya SSH anahtarı bilinçli değiştirildiyse yeni parmak izini ayrı bir güvenilir kanaldan doğruladıktan sonra host key bilgisini sıfırla.");
            AppendLog("Uzak sunucunun SSH host key değeri değişti; bağlantı reddedildi.", "HATA", "REMOTE");
        }
        catch (Exception ex)
        {
            SetRemoteStatus("Bağlantı başarısız.", connected: false);
            ShowRemoteError(ex.Message);
            AppendLog("Uzak sunucu bağlantısı başarısız: " + ex.Message, "HATA", "REMOTE");
        }
        finally { session?.Dispose(); }
    }

    private async Task RefreshRemoteDiagnosticsAsync()
    {
        if (_remoteServerSession?.IsConnected != true)
        {
            ShowRemoteError("Önce uzak sunucuya bağlan.");
            return;
        }

        try
        {
            SetRemoteStatus("Sunucu bilgileri alınıyor...", connected: true);
            string details = await _remoteServerSession.GetServerDiagnosticsAsync();
            if (_remoteDiagnosticsBox is not null) _remoteDiagnosticsBox.Text = details;
            SetRemoteStatus("SSH bağlantısı açık.", connected: true);
            AppendLog("Uzak sunucu durum bilgileri yenilendi.", "OK", "REMOTE");
        }
        catch (Exception ex)
        {
            SetRemoteStatus("Sunucu bilgileri alınamadı.", connected: false);
            ShowRemoteError(ex.Message);
            AppendLog("Uzak sunucu durum sorgusu başarısız: " + ex.Message, "HATA", "REMOTE");
        }
    }

    private void DisconnectRemoteSession()
    {
        _remoteServerSession?.Dispose();
        _remoteServerSession = null;
        _remoteConnectedProfile = null;
        if (_remotePasswordBox is not null) _remotePasswordBox.Password = "";
        if (_remoteKeyPassphraseBox is not null) _remoteKeyPassphraseBox.Password = "";
        SetRemoteStatus("SSH bağlantısı kapatıldı.", connected: false);
        AppendLog("Uzak sunucu SSH bağlantısı kapatıldı.", "INFO", "REMOTE");
    }

    private void DeleteRemoteProfile()
    {
        if (_remoteProfileBox?.SelectedItem is not RemoteServerProfile profile)
        {
            ShowRemoteError("Silmek için kayıtlı bir profil seç.");
            return;
        }
        if (MessageBox.Show($"'{profile.Name}' bağlantı profilini silmek istiyor musun?",
            "Uzak sunucu profilini sil", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes)
            return;

        if (string.Equals(_activeRemoteProfileId, profile.Id, StringComparison.Ordinal))
            DisconnectRemoteSession();
        RemoteServerProfileStore.Delete(profile.Id);
        _activeRemoteProfileId = null;
        RefreshRemoteProfileList(null);
        SetRemoteStatus("Bağlantı profili silindi.", connected: false);
    }

    private void ForgetRemoteHostKey()
    {
        if (_remoteProfileBox?.SelectedItem is not RemoteServerProfile profile)
        {
            ShowRemoteError("Host key bilgisini sıfırlamak için kayıtlı bir profil seç.");
            return;
        }
        if (string.IsNullOrWhiteSpace(profile.HostKeyFingerprint))
        {
            ShowRemoteError("Bu profil için kayıtlı host key bulunmuyor.");
            return;
        }

        string warning = $"'{profile.Host}' için kayıtlı SSH host key silinecek.\n\nSunucunun anahtarının gerçekten değiştiğini başka bir güvenilir kanaldan doğrulamadan bu işlemi yapma. Devam edilsin mi?";
        if (MessageBox.Show(warning, "SSH host key güvenini sıfırla",
            MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes)
            return;

        if (_remoteServerSession?.IsConnected == true &&
            string.Equals(_activeRemoteProfileId, profile.Id, StringComparison.Ordinal))
            DisconnectRemoteSession();
        profile.HostKeyFingerprint = "";
        RemoteServerProfileStore.SaveOrUpdate(profile);
        RefreshRemoteProfileList(profile.Id);
        SetRemoteStatus("Host key kaydı silindi. Sonraki bağlantıda yeni anahtar tekrar sorulacak.", connected: false);
    }

    private void BrowseRemotePrivateKey(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Title = "SSH özel anahtar dosyasını seç",
            CheckFileExists = true,
            Multiselect = false,
            Filter = "SSH key files|id_ed25519;id_rsa;*.pem;*.key;*.*|All files|*.*"
        };
        if (dialog.ShowDialog(this) == true && _remotePrivateKeyBox is not null)
            _remotePrivateKeyBox.Text = dialog.FileName;
    }

    private void SetRemoteStatus(string message, bool connected)
    {
        if (_remoteConnectionState is null) return;
        _remoteConnectionState.Text = message.ToUpperInvariant();
        _remoteConnectionState.Foreground = connected ? BrushHex("#65D6B0") : BrushHex("#E5B65B");
    }

    private static void ShowRemoteError(string message) =>
        MessageBox.Show(message, "Uzak sunucu", MessageBoxButton.OK, MessageBoxImage.Warning);

    private void DisposeRemoteServerSession()
    {
        _remoteServerSession?.Dispose();
        _remoteServerSession = null;
        _remoteConnectedProfile = null;
    }

    private static bool IsSameRemoteEndpoint(RemoteServerProfile left, RemoteServerProfile right) =>
        string.Equals(left.Host, right.Host, StringComparison.OrdinalIgnoreCase) &&
        left.Port == right.Port &&
        string.Equals(left.Username, right.Username, StringComparison.Ordinal) &&
        string.Equals(left.AuthenticationMode, right.AuthenticationMode, StringComparison.OrdinalIgnoreCase) &&
        string.Equals(left.PrivateKeyPath, right.PrivateKeyPath, StringComparison.OrdinalIgnoreCase) &&
        string.Equals(left.HostKeyFingerprint, right.HostKeyFingerprint, StringComparison.Ordinal);
}
