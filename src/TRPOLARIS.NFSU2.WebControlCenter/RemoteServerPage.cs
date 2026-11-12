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
    private TextBox? _remoteCommandBox;
    private TextBox? _remoteCommandOutputBox;
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
    private TextBox? _remoteFileRootBox;
    private TextBox? _remoteFilePathBox;
    private TextBlock? _remoteFileRootStatus;
    private Button? _remoteFileClaimButton;
    private TextBox? _remoteNewFolderBox;
    private ListBox? _remoteFileList;
    private TextBlock? _remoteFileStatus;



    private UIElement BuildRemotePage()
    {
        var root = NewStack(12);
        root.Children.Add(Heading(
            "SSH bağlantı merkezi",
            "Ubuntu ve Debian sunucularını güvenli SSH oturumlarıyla yönet. Bağlantı profillerini kaydet, bağlantıyı doğrula ve uzak dağıtım alanına geç."));

        // Connection profile bar
        var profileBar = RemotePanel();
        var profileGrid = new Grid();
        profileGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        profileGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        profileGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var profileInfo = NewStack(5);
        profileInfo.Children.Add(MutedLabel("AKTİF BAĞLANTI PROFİLİ"));
        _remoteProfileBox = new ComboBox
        {
            Height = 38,
            MinWidth = 230,
            Style = (Style)FindResource("DarkComboBox"),
            Background = BrushHex("#111821"),
            Foreground = BrushHex("#F4F6F8"),
            BorderBrush = BrushHex("#3A4755"),
            BorderThickness = new Thickness(1),
            Padding = new Thickness(11, 7, 11, 7),
            DisplayMemberPath = nameof(RemoteServerProfile.Name),
            ItemsSource = RemoteServerProfileStore.LoadAll().ToList()
        };
        _remoteProfileBox.SelectionChanged += RemoteProfileSelectionChanged;
        profileInfo.Children.Add(_remoteProfileBox);
        profileGrid.Children.Add(profileInfo);

        var newProfile = MakeButton("＋  Yeni profil", "SecondaryButton", (_, _) => NewRemoteProfile(), new Thickness(10, 16, 0, 0));
        newProfile.MinWidth = 122;
        Grid.SetColumn(newProfile, 1);
        profileGrid.Children.Add(newProfile);

        var manageProfiles = MakeButton("Profili sil", "DangerButton", (_, _) => DeleteRemoteProfile(), new Thickness(7, 16, 0, 0));
        manageProfiles.MinWidth = 100;
        Grid.SetColumn(manageProfiles, 2);
        profileGrid.Children.Add(manageProfiles);
        profileBar.Child = profileGrid;
        root.Children.Add(profileBar);

        // Main connection workspace
        var workspace = new Grid();
        workspace.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.15, GridUnitType.Star) });
        workspace.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(0.85, GridUnitType.Star) });

        var formPanel = RemotePanel();
        formPanel.Margin = new Thickness(0, 0, 8, 0);
        var form = NewStack(9);
        var formHeader = new Grid();
        formHeader.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        formHeader.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var formHeading = NewStack(4);
        formHeading.Children.Add(RemoteSectionTitle("BAĞLANTI BİLGİLERİ"));
        formHeading.Children.Add(new TextBlock { Text = "Sunucu adresini ve kimlik doğrulama yöntemini tanımla.", FontSize = 11, Foreground = BrushHex("#9BA7B4"), TextWrapping = TextWrapping.Wrap });
        formHeader.Children.Add(formHeading);
        var lockTag = new Border
        {
            Background = BrushHex("#202B37"),
            BorderBrush = BrushHex("#3A4755"),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(9),
            Padding = new Thickness(10, 7, 10, 7),
            VerticalAlignment = VerticalAlignment.Center
        };
        lockTag.Child = new TextBlock { Text = "SSH  /  ENCRYPTED", FontSize = 9, FontWeight = FontWeights.Bold, Foreground = BrushHex("#74C9D0") };
        Grid.SetColumn(lockTag, 1);
        formHeader.Children.Add(lockTag);
        form.Children.Add(formHeader);

        _remoteNameBox = RemoteTextBox();
        _remoteHostBox = RemoteTextBox();
        _remotePortBox = RemoteTextBox();
        _remotePortBox.Text = "22";
        _remoteUsernameBox = RemoteTextBox();

        var identityGrid = new Grid();
        identityGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        identityGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.35, GridUnitType.Star) });
        var nameField = RemoteField("Profil adı", _remoteNameBox);
        var hostField = RemoteField("Sunucu IP adresi veya domain", _remoteHostBox);
        hostField.Margin = new Thickness(12, 0, 0, 0);
        identityGrid.Children.Add(nameField);
        Grid.SetColumn(hostField, 1);
        identityGrid.Children.Add(hostField);
        form.Children.Add(identityGrid);

        var loginGrid = new Grid();
        loginGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(0.7, GridUnitType.Star) });
        loginGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.3, GridUnitType.Star) });
        var portField = RemoteField("SSH portu", _remotePortBox);
        var userField = RemoteField("Linux kullanıcı adı", _remoteUsernameBox);
        userField.Margin = new Thickness(12, 0, 0, 0);
        loginGrid.Children.Add(portField);
        Grid.SetColumn(userField, 1);
        loginGrid.Children.Add(userField);
        form.Children.Add(loginGrid);

        _remoteAuthenticationBox = new ComboBox
        {
            Height = 38,
            Style = (Style)FindResource("DarkComboBox"),
            Background = BrushHex("#111821"),
            Foreground = BrushHex("#F4F6F8"),
            BorderBrush = BrushHex("#3A4755"),
            BorderThickness = new Thickness(1),
            Padding = new Thickness(13, 8, 13, 8),
            ItemsSource = new[] { "Parola", "SSH özel anahtarı" },
            SelectedIndex = 0
        };
        form.Children.Add(RemoteField("Kimlik doğrulama yöntemi", _remoteAuthenticationBox));

        _remotePrivateKeyBox = RemoteTextBox();
        var keyGrid = new Grid();
        keyGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        keyGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        keyGrid.Children.Add(RemoteField("Özel anahtar dosyası", _remotePrivateKeyBox));
        var browse = MakeButton("Dosya seç", "SecondaryButton", BrowseRemotePrivateKey, new Thickness(9, 17, 0, 0));
        browse.MinWidth = 92;
        Grid.SetColumn(browse, 1);
        keyGrid.Children.Add(browse);
        form.Children.Add(keyGrid);

        _remotePasswordBox = new PasswordBox
        {
            Height = 38,
            Background = BrushHex("#111821"),
            Foreground = BrushHex("#F4F6F8"),
            BorderBrush = BrushHex("#3A4755"),
            BorderThickness = new Thickness(1),
            Padding = new Thickness(13, 9, 13, 9)
        };
        _remoteKeyPassphraseBox = new PasswordBox
        {
            Height = 38,
            Background = BrushHex("#111821"),
            Foreground = BrushHex("#F4F6F8"),
            BorderBrush = BrushHex("#3A4755"),
            BorderThickness = new Thickness(1),
            Padding = new Thickness(13, 9, 13, 9)
        };
        var secretGrid = new Grid();
        secretGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        secretGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        secretGrid.Children.Add(RemoteField("SSH parolası", _remotePasswordBox));
        var passphraseField = RemoteField("Anahtar parolası", _remoteKeyPassphraseBox);
        passphraseField.Margin = new Thickness(12, 0, 0, 0);
        Grid.SetColumn(passphraseField, 1);
        secretGrid.Children.Add(passphraseField);
        form.Children.Add(secretGrid);

        var secretNote = new Border
        {
            Background = BrushHex("#252019"),
            BorderBrush = BrushHex("#65452F"),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(10),
            Padding = new Thickness(12, 10, 12, 10)
        };
        secretNote.Child = new TextBlock
        {
            Text = "Güvenlik: SSH parolası ve anahtar parolası bu Windows hesabına bağlı DPAPI ile şifrelenerek profilde saklanır; başka bir Windows hesabında açılamaz. İlk bağlantıda host key parmak izini ayrıca doğrula.",
            FontSize = 10.5,
            Foreground = BrushHex("#D4B39B"),
            TextWrapping = TextWrapping.Wrap
        };
        form.Children.Add(secretNote);

        var actions = new WrapPanel { Margin = new Thickness(0, 4, 0, 0) };
        var save = MakeButton("Profili kaydet", "SecondaryButton", (_, _) => SaveRemoteProfile());
        save.MinWidth = 120;
        actions.Children.Add(save);
        var connect = MakeButton("Bağlan", "ModernButton", async (_, _) => await ConnectRemoteAsync(keepConnected: true), new Thickness(8, 0, 0, 0));
        connect.MinWidth = 110;
        actions.Children.Add(connect);
        actions.Children.Add(MakeButton("Bağlantıyı test et", "SecondaryButton", async (_, _) => await ConnectRemoteAsync(keepConnected: false), new Thickness(8, 0, 0, 0)));
        actions.Children.Add(MakeButton("Bağlantıyı kes", "DangerButton", (_, _) => DisconnectRemoteSession(), new Thickness(8, 0, 0, 0)));
        form.Children.Add(actions);
        formPanel.Child = form;
        Grid.SetColumn(formPanel, 0);
        workspace.Children.Add(formPanel);

        // Live host status
        var statusPanel = RemotePanel();
        statusPanel.Margin = new Thickness(8, 0, 0, 0);
        var status = NewStack(9);
        var statusHeader = new Grid();
        statusHeader.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        statusHeader.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var statusTitles = NewStack(4);
        statusTitles.Children.Add(RemoteSectionTitle("UZAK SUNUCU"));
        statusTitles.Children.Add(new TextBlock { Text = "Canlı bağlantı ve altyapı bilgileri", FontSize = 11, Foreground = BrushHex("#9BA7B4") });
        statusHeader.Children.Add(statusTitles);
        var statusPill = new Border
        {
            Background = BrushHex(_remoteServerSession?.IsConnected == true ? "#183328" : "#352A20"),
            BorderBrush = BrushHex(_remoteServerSession?.IsConnected == true ? "#366A50" : "#7A5836"),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(9),
            Padding = new Thickness(11, 8, 11, 8),
            VerticalAlignment = VerticalAlignment.Center
        };
        _remoteConnectionState = new TextBlock
        {
            Text = _remoteServerSession?.IsConnected == true ? "BAĞLI" : "ÇEVRİMDIŞI",
            Foreground = BrushHex(_remoteServerSession?.IsConnected == true ? "#72D6A9" : "#F5C36A"),
            FontSize = 10,
            FontWeight = FontWeights.ExtraBold
        };
        statusPill.Child = _remoteConnectionState;
        Grid.SetColumn(statusPill, 1);
        statusHeader.Children.Add(statusPill);
        status.Children.Add(statusHeader);

        var diagnosticActions = new Grid();
        diagnosticActions.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        diagnosticActions.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        diagnosticActions.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        diagnosticActions.Children.Add(new TextBlock
        {
            Text = "SSH ÜZERİNDEN SİSTEM TANILAMASI",
            FontSize = 9,
            FontWeight = FontWeights.Bold,
            Foreground = BrushHex("#8593A2"),
            VerticalAlignment = VerticalAlignment.Center
        });
        var connectAndScan = MakeButton("SSH ile bağlan ve tara", "ModernButton", async (_, _) => await ConnectRemoteAndScanAsync());
        connectAndScan.MinWidth = 132;
        Grid.SetColumn(connectAndScan, 1);
        diagnosticActions.Children.Add(connectAndScan);
        var refresh = MakeButton("Yenile", "SecondaryButton", async (_, _) => await RefreshRemoteDiagnosticsAsync(), new Thickness(7, 0, 0, 0));
        refresh.MinWidth = 76;
        Grid.SetColumn(refresh, 2);
        diagnosticActions.Children.Add(refresh);
        status.Children.Add(diagnosticActions);

        _remoteDiagnosticsBox = new TextBox
        {
            IsReadOnly = true,
            AcceptsReturn = true,
            TextWrapping = TextWrapping.NoWrap,
            VerticalContentAlignment = VerticalAlignment.Top,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
            MinHeight = 220,
            Background = BrushHex("#111821"),
            Foreground = BrushHex("#D3DCE5"),
            BorderBrush = BrushHex("#303D4D"),
            BorderThickness = new Thickness(1),
            Padding = new Thickness(16),
            FontFamily = new FontFamily("Cascadia Mono, Consolas"),
            FontSize = 11,
            Text = _remoteServerSession?.IsConnected == true
                ? "SSH bağlantısı açık.\nSunucu bilgilerini almak için Yenile seçeneğini kullan."
                : "Henüz uzak sunucuya bağlanılmadı.\nBir profil seçip Bağlan düğmesini kullan."
        };
        status.Children.Add(_remoteDiagnosticsBox);

        var sshConsole = new Border
        {
            Background = BrushHex("#171F29"),
            BorderBrush = BrushHex("#3A4755"),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(11),
            Padding = new Thickness(13)
        };
        var sshConsoleStack = NewStack(7);
        sshConsoleStack.Children.Add(RemoteSectionTitle("SSH KOMUT PENCERESİ"));
        sshConsoleStack.Children.Add(new TextBlock
        {
            Text = "Komutlar kayıtlı profildeki Linux kullanıcısıyla çalışır. Yetki gerekiyorsa komutta sudo kullan.",
            FontSize = 10,
            Foreground = BrushHex("#A5B0BE"),
            TextWrapping = TextWrapping.Wrap
        });
        _remoteCommandBox = new TextBox
        {
            Height = 62,
            MinHeight = 62,
            AcceptsReturn = true,
            TextWrapping = TextWrapping.Wrap,
            VerticalContentAlignment = VerticalAlignment.Top,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            Padding = new Thickness(11, 8, 11, 8),
            FontSize = 11,
            ToolTip = "Örnek: docker ps -a veya systemctl status nfsu2 --no-pager"
        };
        _remoteCommandBox.SetResourceReference(FrameworkElement.StyleProperty, "DarkTextBox");
        sshConsoleStack.Children.Add(_remoteCommandBox);
        var commandActions = new WrapPanel();
        var runCommand = MakeButton("Komutu SSH üzerinden çalıştır", "ModernButton", async (_, _) => await RunRemoteSshCommandAsync());
        runCommand.MinWidth = 175;
        commandActions.Children.Add(runCommand);
        sshConsoleStack.Children.Add(commandActions);
        _remoteCommandOutputBox = new TextBox
        {
            Height = 92,
            MinHeight = 92,
            IsReadOnly = true,
            AcceptsReturn = true,
            TextWrapping = TextWrapping.Wrap,
            VerticalContentAlignment = VerticalAlignment.Top,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            Padding = new Thickness(11, 8, 11, 8),
            FontSize = 10.5,
            Text = "SSH komut çıktısı burada görüntülenir."
        };
        _remoteCommandOutputBox.SetResourceReference(FrameworkElement.StyleProperty, "DarkTextBox");
        sshConsoleStack.Children.Add(_remoteCommandOutputBox);
        sshConsole.Child = sshConsoleStack;
        status.Children.Add(sshConsole);

        var securityPanel = new Border
        {
            Background = BrushHex("#202A35"),
            BorderBrush = BrushHex("#3A4755"),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(12),
            Padding = new Thickness(14)
        };
        var securityText = NewStack(5);
        securityText.Children.Add(new TextBlock { Text = "HOST KEY GÜVENLİĞİ", FontSize = 9, FontWeight = FontWeights.ExtraBold, Foreground = BrushHex("#FF9B62") });
        securityText.Children.Add(new TextBlock
        {
            Text = "İlk bağlantıda fingerprint onaylanır. Kayıtlı host key değişirse bağlantı durdurulur ve yeniden doğrulama istenir.",
            FontSize = 10.5,
            Foreground = BrushHex("#B7C0CA"),
            TextWrapping = TextWrapping.Wrap
        });
        securityText.Children.Add(MakeButton("Kayıtlı host key bilgisini sıfırla", "SecondaryButton", (_, _) => ForgetRemoteHostKey()));
        securityPanel.Child = securityText;
        status.Children.Add(securityPanel);
        statusPanel.Child = status;
        Grid.SetColumn(statusPanel, 1);
        workspace.Children.Add(statusPanel);

        root.Children.Add(workspace);
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
        var root = NewStack(8);
        root.Children.Add(Heading(
            "Uzak sunucu dağıtımı",
            "Kurulum kaynağını, yayın adresini ve servis işlemlerini ayrı adımlarda yapılandır. SSH oturum yönetimi bu ekrandan bağımsızdır."));

        var profile = GetSelectedRemoteProfile();
        var profileBar = RemotePanel();
        var profileRow = new Grid();
        profileRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        profileRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var profileInfo = NewStack(4);
        profileInfo.Children.Add(MutedLabel("HEDEF SUNUCU"));
        profileInfo.Children.Add(new TextBlock
        {
            Text = profile is null ? "Henüz bir bağlantı profili seçilmedi" : profile.Name + "  ·  " + profile.Username + "@" + profile.Host + ":" + profile.Port,
            FontSize = 11,
            FontWeight = FontWeights.SemiBold,
            Foreground = BrushHex("#EFF6FF")
        });
        profileInfo.Children.Add(new TextBlock
        {
            Text = _remoteServerSession?.IsConnected == true ? "SSH oturumu bağlı · dağıtım işlemleri kullanılabilir" : "Dağıtımdan önce SSH bağlantısı kur",
            FontSize = 10,
            Foreground = BrushHex(_remoteServerSession?.IsConnected == true ? "#3DD6AE" : "#F6C66B")
        });
        profileRow.Children.Add(profileInfo);
        var selectProfile = MakeButton("Bağlantıları yönet", "SecondaryButton", (_, _) => ShowPage("Remote"), new Thickness(18, 0, 0, 0));
        Grid.SetColumn(selectProfile, 1);
        profileRow.Children.Add(selectProfile);
        profileBar.Child = profileRow;
        root.Children.Add(profileBar);

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
        _remoteInstallRootBox.Text = profile?.RemoteProjectDirectory ?? "/srv/nfsu2";
        _remoteInstallRootBox.TextChanged += (_, _) =>
        {
            if (_remoteFilePathBox is not null)
                _remoteFilePathBox.Text = _remoteInstallRootBox.Text.TrimEnd('/');
        };
        _remoteHttpPortBox = RemoteTextBox();
        _remoteHttpPortBox.Text = (profile?.HttpPort ?? 8080).ToString();
        _remoteProxyModeBox = new ComboBox
        {
            Height = 33,
            Style = (Style)FindResource("DarkComboBox"),
            ItemsSource = new[] { "Caddy otomatik HTTPS", "Mevcut Nginx / OpenLiteSpeed / CyberPanel proxy" },
            SelectedIndex = string.Equals(profile?.ProxyMode, "existing-proxy", StringComparison.OrdinalIgnoreCase) ? 1 : 0
        };

        if (profile is not null)
        {
            _remoteDomainBox.Text = profile.Domain ?? "";
            _remoteAccessModeBox.SelectedIndex = profile.AccessMode switch { "lan" => 0, "public-ip" => 1, _ => 2 };
        }

        var columns = new Grid();
        columns.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        columns.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        var sourcePanel = RemotePanel();
        sourcePanel.Margin = new Thickness(0, 0, 9, 0);
        var sourceStack = NewStack(9);
        sourceStack.Children.Add(RemoteSectionTitle("01  /  OYUN VE MODÜL KAYNAĞI"));
        sourceStack.Children.Add(new TextBlock
        {
            Text = "Kendi bilgisayarındaki doğrulanmış oyun klasörü ve yerel WASM çıktıları gönderilir. Oyun arşivinin tamamı veya ilgisiz kişisel dosyalar aktarılmaz.",
            FontSize = 10,
            Foreground = BrushHex("#A5B5CA"),
            TextWrapping = TextWrapping.Wrap
        });

        var gameRow = new DockPanel { LastChildFill = true };
        gameRow.Children.Add(RemoteField("NFSU2 oyun klasörü · SPEED2.EXE", _remoteGameFolderBox));
        var gameBrowse = MakeButton("Klasör seç", "SecondaryButton", BrowseRemoteGameFolder, new Thickness(7, 16, 0, 0));
        DockPanel.SetDock(gameBrowse, Dock.Right);
        gameRow.Children.Insert(0, gameBrowse);
        sourceStack.Children.Add(gameRow);

        var wasmRow = new Grid();
        wasmRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        wasmRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        wasmRow.Children.Add(RemoteField("WASM modülü · nfsu2.wasm", _remoteWasmPathBox));
        var wasmBrowse = MakeButton("Dosya seç", "SecondaryButton", BrowseRemoteWasm, new Thickness(9, 21, 0, 0));
        Grid.SetColumn(wasmBrowse, 1);
        wasmRow.Children.Add(wasmBrowse);
        sourceStack.Children.Add(wasmRow);

        var jsonRow = new Grid();
        jsonRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        jsonRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        jsonRow.Children.Add(RemoteField("Modül bilgisi · nfsu2.json", _remoteJsonPathBox));
        var jsonBrowse = MakeButton("Dosya seç", "SecondaryButton", BrowseRemoteJson, new Thickness(9, 21, 0, 0));
        Grid.SetColumn(jsonBrowse, 1);
        jsonRow.Children.Add(jsonBrowse);
        sourceStack.Children.Add(jsonRow);

        var sourceFoot = new Border
        {
            Background = BrushHex("#0D1B2D"),
            BorderBrush = BrushHex("#2A4360"),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(7),
            Padding = new Thickness(8),
            Child = new TextBlock
            {
                Text = "Dosya doğrulaması: SPEED2.EXE sürüm / SHA-256 · seçilmiş oyun veri klasörleri · WASM ve JSON dosyaları",
                FontSize = 9,
                Foreground = BrushHex("#AFC1D5"),
                TextWrapping = TextWrapping.Wrap
            }
        };
        sourceStack.Children.Add(sourceFoot);
        sourcePanel.Child = sourceStack;
        Grid.SetColumn(sourcePanel, 0);
        columns.Children.Add(sourcePanel);

        var targetPanel = RemotePanel();
        targetPanel.Margin = new Thickness(9, 0, 0, 0);
        var targetStack = NewStack(9);
        targetStack.Children.Add(RemoteSectionTitle("02  /  YAYIN HEDEFİ"));
        targetStack.Children.Add(new TextBlock
        {
            Text = "Oyuncuların kullanacağı erişim türünü seç. SSH bağlantı adresiyle oyuncu adresi birbirinden bağımsızdır.",
            FontSize = 10,
            Foreground = BrushHex("#A5B5CA"),
            TextWrapping = TextWrapping.Wrap
        });
        targetStack.Children.Add(RemoteField("Erişim modu", _remoteAccessModeBox));
        var endpointField = RemoteField("Oyuncu adresi · IP veya domain", _remoteDomainBox);
        _remoteAccessAddressLabel = (TextBlock)endpointField.Children[0];
        targetStack.Children.Add(endpointField);
        _remoteAccessHint = new TextBlock
        {
            Text = "Domain için DNS kaydını VPS adresine yönlendir. Caddy otomatik TLS kullanır.",
            FontSize = 9,
            Foreground = BrushHex("#9AA9BE"),
            TextWrapping = TextWrapping.Wrap
        };
        targetStack.Children.Add(_remoteAccessHint);

        var configGrid = new Grid();
        configGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.25, GridUnitType.Star) });
        configGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(0.75, GridUnitType.Star) });
        configGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        configGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        configGrid.Children.Add(RemoteField("Linux kurulum yolu", _remoteInstallRootBox));
        var httpField = RemoteField("HTTP portu", _remoteHttpPortBox);
        Grid.SetColumn(httpField, 1);
        configGrid.Children.Add(httpField);
        var proxyField = RemoteField("HTTPS / proxy", _remoteProxyModeBox);
        Grid.SetRow(proxyField, 1);
        proxyField.Margin = new Thickness(0, 9, 0, 0);
        configGrid.Children.Add(proxyField);
        targetStack.Children.Add(configGrid);
        targetStack.Children.Add(MakeButton(
            "Dağıtım ayarlarını bağlantı profiline kaydet",
            "SecondaryButton",
            (_, _) => SaveRemoteDeploymentSettings()));
        targetPanel.Child = targetStack;
        Grid.SetColumn(targetPanel, 1);
        columns.Children.Add(targetPanel);
        root.Children.Add(columns);

        var operationPanel = RemotePanel();
        var operationStack = NewStack(8);
        var operationHeader = new DockPanel { LastChildFill = true };
        operationHeader.Children.Add(RemoteSectionTitle("03  /  DAĞITIM İŞ AKIŞI"));
        var prerequisites = new TextBlock
        {
            Text = "Ubuntu / Debian  ·  Root veya parola istemeyen sudo  ·  5 GB boş alan",
            FontSize = 9,
            Foreground = BrushHex("#A5B5CA"),
            VerticalAlignment = VerticalAlignment.Center
        };
        DockPanel.SetDock(prerequisites, Dock.Right);
        operationHeader.Children.Add(prerequisites);
        operationStack.Children.Add(operationHeader);

        _remoteDeploymentState = new TextBlock
        {
            Text = "Dağıtım başlatılmaya hazır. Önce SSH bağlantısını kur.",
            Foreground = BrushHex("#F6C66B"),
            FontSize = 10,
            TextWrapping = TextWrapping.Wrap
        };
        operationStack.Children.Add(_remoteDeploymentState);
        _remoteDeploymentProgress = new ProgressBar
        {
            Height = 8,
            Minimum = 0,
            Maximum = 100,
            Value = 0,
            Background = BrushHex("#0D1B2D"),
            Foreground = BrushHex("#70A5FF")
        };
        operationStack.Children.Add(_remoteDeploymentProgress);

        var deploymentActions = new WrapPanel();
        deploymentActions.Children.Add(MakeButton("İlk kurulumu başlat", "ModernButton", async (_, _) => await InstallRemoteServerAsync(), new Thickness(0, 0, 8, 0)));
        deploymentActions.Children.Add(MakeButton("WASM / sunucu imajını güncelle", "SecondaryButton", async (_, _) => await UpdateRemoteServerAsync()));
        operationStack.Children.Add(deploymentActions);
        operationPanel.Child = operationStack;
        root.Children.Add(operationPanel);

        var serviceGrid = new Grid();
        serviceGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        serviceGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        var servicePanel = RemotePanel();
        servicePanel.Margin = new Thickness(0, 0, 9, 0);
        var serviceStack = NewStack(8);
        serviceStack.Children.Add(RemoteSectionTitle("04  /  SERVİS KONTROLÜ"));
        serviceStack.Children.Add(new TextBlock
        {
            Text = "Dağıtılmış NFSU2 servisinin yaşam döngüsünü yönet.",
            FontSize = 10,
            Foreground = BrushHex("#A5B5CA")
        });
        var serviceButtons = new WrapPanel();
        serviceButtons.Children.Add(MakeButton("Başlat", "ModernButton", async (_, _) => await RunRemoteServiceActionAsync("start")));
        serviceButtons.Children.Add(MakeButton("Durdur", "SecondaryButton", async (_, _) => await RunRemoteServiceActionAsync("stop"), new Thickness(6, 0, 0, 0)));
        serviceButtons.Children.Add(MakeButton("Yeniden başlat", "SecondaryButton", async (_, _) => await RunRemoteServiceActionAsync("restart"), new Thickness(7, 0, 0, 0)));
        serviceButtons.Children.Add(MakeButton("Canlı günlükler", "SecondaryButton", async (_, _) => await ReadRemoteLogsAsync(), new Thickness(7, 0, 0, 0)));
        serviceStack.Children.Add(serviceButtons);
        servicePanel.Child = serviceStack;
        Grid.SetColumn(servicePanel, 0);
        serviceGrid.Children.Add(servicePanel);

        var maintenancePanel = RemotePanel();
        maintenancePanel.Margin = new Thickness(9, 0, 0, 0);
        var maintenanceStack = NewStack(8);
        maintenanceStack.Children.Add(RemoteSectionTitle("05  /  SERTİFİKA VE KURULUM TEMİZLİĞİ"));
        maintenanceStack.Children.Add(new TextBlock
        {
            Text = "LAN HTTPS için CA sertifikasını indir. Normal kaldırma oyun dosyalarını ve kayıt hacmini korur; tam silme iki ayrı onay ister.",
            FontSize = 10,
            Foreground = BrushHex("#A5B5CA"),
            TextWrapping = TextWrapping.Wrap
        });
        var maintenanceButtons = new WrapPanel();
        maintenanceButtons.Children.Add(MakeButton("LAN CA sertifikasını indir", "SecondaryButton", async (_, _) => await DownloadRemoteLanCertificateAsync(true)));
        maintenanceButtons.Children.Add(MakeButton("Kurulumu kaldır", "SecondaryButton", async (_, _) => await UninstallRemoteServerAsync(), new Thickness(7, 0, 0, 0)));
        maintenanceButtons.Children.Add(MakeButton("Tüm verileri kalıcı sil", "DangerButton", async (_, _) => await PurgeRemoteServerAsync(), new Thickness(7, 0, 0, 0)));
        maintenanceStack.Children.Add(maintenanceButtons);
        maintenancePanel.Child = maintenanceStack;
        Grid.SetColumn(maintenancePanel, 1);
        serviceGrid.Children.Add(maintenancePanel);
        root.Children.Add(serviceGrid);

        ApplyRemoteAccessModeUi();
        return WrapScrollable(root);
    }

    private UIElement BuildRemoteFilesPage()
    {
        var root = NewStack(8);
        var profile = GetSelectedRemoteProfile();
        root.Children.Add(Heading("Uzak dosyalar", "SFTP dosya çalışma alanı. Gezinme, aktarım ve klasör işlemleri uzak dağıtım ekranından bağımsızdır."));

        var context = RemotePanel();
        var contextRow = new DockPanel { LastChildFill = true };
        contextRow.Children.Add(new TextBlock
        {
            Text = profile is null ? "Bağlantı profili seçilmedi" : "HEDEF  /  " + profile.Name + "  ·  " + (!string.IsNullOrWhiteSpace(profile.RemoteFileManagerRoot) ? profile.RemoteFileManagerRoot : profile.RemoteProjectDirectory ?? "/srv/nfsu2"),
            FontSize = 10,
            FontWeight = FontWeights.SemiBold,
            Foreground = BrushHex("#AFC1D5"),
            VerticalAlignment = VerticalAlignment.Center
        });
        var openConnections = MakeButton("SSH bağlantılarına git", "SecondaryButton", (_, _) => ShowPage("Remote"));
        DockPanel.SetDock(openConnections, Dock.Right);
        contextRow.Children.Add(openConnections);
        context.Child = contextRow;
        root.Children.Add(context);

        var rootPicker = RemotePanel();
        var rootPickerGrid = new Grid();
        rootPickerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        rootPickerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var rootPickerStack = NewStack(5);
        rootPickerStack.Children.Add(MutedLabel("UZAK DOSYA SİSTEMİ KÖK DİZİNİ"));
        _remoteFileRootBox = RemoteTextBox();
        _remoteFileRootBox.Text = GetRemoteFileManagerRootValue();
        rootPickerStack.Children.Add(_remoteFileRootBox);
        var rootPickerHint = new TextBlock
        {
            Text = "Eski kurulumun yolu farklıysa buradan değiştir. İşaretçisi olmayan klasörler önce salt okunur açılır.",
            FontSize = 9,
            Foreground = BrushHex("#A5B5CA"),
            TextWrapping = TextWrapping.Wrap
        };
        rootPickerStack.Children.Add(rootPickerHint);
        rootPickerGrid.Children.Add(rootPickerStack);
        var openRoot = MakeButton("Kökü aç", "ModernButton", async (_, _) => await ApplyRemoteFileManagerRootAsync(), new Thickness(8, 15, 0, 0));
        openRoot.MinWidth = 82;
        Grid.SetColumn(openRoot, 1);
        rootPickerGrid.Children.Add(openRoot);
        rootPicker.Child = rootPickerGrid;
        root.Children.Add(rootPicker);

        var workspace = new Grid();
        workspace.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.65, GridUnitType.Star) });
        workspace.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(0.85, GridUnitType.Star) });

        var browserPanel = RemotePanel();
        browserPanel.Margin = new Thickness(0, 0, 10, 0);
        var browser = NewStack(8);
        var browserHeader = new DockPanel { LastChildFill = true };
        browserHeader.Children.Add(RemoteSectionTitle("DOSYA SİSTEMİ"));
        var refreshList = MakeButton("Yenile", "SecondaryButton", async (_, _) => await RefreshRemoteFileListAsync());
        DockPanel.SetDock(refreshList, Dock.Right);
        browserHeader.Children.Add(refreshList);
        browser.Children.Add(browserHeader);

        _remoteFilePathBox = RemoteTextBox();
        _remoteFilePathBox.Text = GetRemoteFileManagerRootValue().TrimEnd('/');
        _remoteFilePathBox.IsReadOnly = true;
        var pathRow = new DockPanel { LastChildFill = true };
        var parentButton = MakeButton("Üst klasör", "SecondaryButton", async (_, _) => await NavigateRemoteParentAsync(), new Thickness(8, 0, 0, 0));
        DockPanel.SetDock(parentButton, Dock.Right);
        pathRow.Children.Add(parentButton);
        pathRow.Children.Add(_remoteFilePathBox);
        browser.Children.Add(pathRow);

        _remoteFileList = new ListBox
        {
            Style = (Style)FindResource("DarkListBox"),
            MinHeight = 370,
            MaxHeight = 520,
            Background = BrushHex("#0D1B2D"),
            Foreground = BrushHex("#E5F2FF"),
            BorderBrush = BrushHex("#2A4360"),
            BorderThickness = new Thickness(1),
            Padding = new Thickness(8),
            FontFamily = new FontFamily("Cascadia Mono, Consolas"),
            FontSize = 10,
            SelectionMode = SelectionMode.Single,
            HorizontalContentAlignment = HorizontalAlignment.Stretch
        };
        _remoteFileList.MouseDoubleClick += RemoteFileList_MouseDoubleClick;
        browser.Children.Add(_remoteFileList);

        _remoteFileStatus = new TextBlock
        {
            Text = "SSH bağlantısı kurulduğunda Listele ile uzak klasörü görüntüleyebilirsin.",
            Foreground = BrushHex("#A5B5CA"),
            FontSize = 10,
            TextWrapping = TextWrapping.Wrap
        };
        browser.Children.Add(_remoteFileStatus);
        browserPanel.Child = browser;
        Grid.SetColumn(browserPanel, 0);
        workspace.Children.Add(browserPanel);

        var actionsPanel = RemotePanel();
        actionsPanel.Margin = new Thickness(7, 0, 0, 0);
        var actions = NewStack(9);
        actions.Children.Add(RemoteSectionTitle("DOSYA İŞLEMLERİ"));
        actions.Children.Add(new TextBlock
        {
            Text = "Seçili dosya veya klasör üzerinde işlem yap. Dosya ağacı çift tıklamayla açılır.",
            FontSize = 10,
            Foreground = BrushHex("#A5B5CA"),
            TextWrapping = TextWrapping.Wrap
        });
        actions.Children.Add(MakeButton("Seçili dosyayı indir", "SecondaryButton", async (_, _) => await DownloadSelectedRemoteFileAsync()));
        actions.Children.Add(MakeButton("Bilgisayardan dosya yükle", "ModernButton", async (_, _) => await UploadRemoteFileAsync()));
        _remoteFileClaimButton = MakeButton("Bu dizini SFTP için etkinleştir", "SecondaryButton", async (_, _) => await ClaimRemoteFileManagerRootAsync());
        actions.Children.Add(_remoteFileClaimButton);
        actions.Children.Add(new Border { Height = 1, Background = BrushHex("#2A4360"), Margin = new Thickness(0, 3, 0, 1) });
        actions.Children.Add(RemoteSectionTitle("YENİ KLASÖR"));
        _remoteNewFolderBox = RemoteTextBox();
        _remoteNewFolderBox.Text = "";
        actions.Children.Add(RemoteField("Klasör adı", _remoteNewFolderBox));
        actions.Children.Add(MakeButton("Klasör oluştur", "SecondaryButton", async (_, _) => await CreateRemoteFolderAsync()));
        actions.Children.Add(new Border { Height = 1, Background = BrushHex("#2A4360"), Margin = new Thickness(0, 3, 0, 1) });
        actions.Children.Add(RemoteSectionTitle("KALICI SİLME"));
        actions.Children.Add(new TextBlock
        {
            Text = "Dosyalar kalıcı silinir. Klasörler yalnızca boş olduklarında silinebilir. Kurulum kökü ve sahiplik işaretçisi korunur.",
            FontSize = 10,
            Foreground = BrushHex("#A5B5CA"),
            TextWrapping = TextWrapping.Wrap
        });
        actions.Children.Add(MakeButton("Seçili dosya / boş klasörü sil", "DangerButton", async (_, _) => await DeleteSelectedRemoteEntryAsync()));
        actions.Children.Add(new Border
        {
            Background = BrushHex("#0D1B2D"),
            BorderBrush = BrushHex("#2A4360"),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(11),
            Child = new TextBlock
            {
                Text = "Güvenlik sınırları\n• Yalnızca uygulama kurulum dizini\n• Üst dizine çıkış engelli\n• Sembolik bağlantılar gizli\n• Sahiplik işaretçisi korumalı",
                FontSize = 9,
                Foreground = BrushHex("#AFC1D5"),
                TextWrapping = TextWrapping.Wrap
            }
        });
        actionsPanel.Child = actions;
        Grid.SetColumn(actionsPanel, 1);
        workspace.Children.Add(actionsPanel);
        root.Children.Add(workspace);
        root.Loaded += async (_, _) =>
        {
            if (_remoteServerSession?.IsConnected == true)
                await RefreshRemoteFileListAsync();
        };

        return WrapScrollable(root);
    }

    private static Border RemotePanel() => new()
    {
        Style = (Style)Application.Current.FindResource("CardBorder"),
        BorderThickness = new Thickness(1),
        CornerRadius = new CornerRadius(13),
        Padding = new Thickness(15)
    };

    private static TextBlock RemoteSectionTitle(string text) => new()
    {
        Text = text,
        FontSize = 11,
        FontWeight = FontWeights.ExtraBold,
        Foreground = BrushHex("#FF9B62")
    };

    private static TextBox RemoteTextBox()
    {
        var box = new TextBox
        {
            Height = 38,
            MinHeight = 38,
            Padding = new Thickness(11, 7, 11, 7),
            FontSize = 11,
            VerticalContentAlignment = VerticalAlignment.Center
        };
        box.SetResourceReference(FrameworkElement.StyleProperty, "DarkTextBox");
        return box;
    }

    private static StackPanel RemoteField(string label, Control control)
    {
        var panel = new StackPanel { Orientation = Orientation.Vertical };
        panel.Children.Add(new TextBlock
        {
            Text = label,
            FontSize = 11,
            FontWeight = FontWeights.SemiBold,
            Foreground = BrushHex("#B7C0CA"),
            Margin = new Thickness(0, 0, 0, 6)
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

    private void SaveRemoteDeploymentSettings()
    {
        try
        {
            var profile = GetSelectedRemoteProfile();
            if (profile is null)
                throw new InvalidOperationException("Önce SSH bağlantıları ekranından bir sunucu profili oluştur veya seç.");

            profile.RemoteProjectDirectory = ValidateRemoteInstallRoot(_remoteInstallRootBox?.Text ?? profile.RemoteProjectDirectory);
            profile.AccessMode = GetRemoteAccessMode();
            profile.Domain = _remoteDomainBox?.Text.Trim() ?? "";
            profile.HttpPort = ParseRemoteHttpPort();
            profile.ProxyMode = _remoteProxyModeBox?.SelectedIndex == 1 ? "existing-proxy" : "caddy";
            RemoteServerProfileStore.SaveOrUpdate(profile);
            _activeRemoteProfileId = profile.Id;

            AppendLog($"Uzak dağıtım ayarları kaydedildi: {profile.Name} ({profile.AccessMode})", "OK", "REMOTE");
            if (_remoteDeploymentState is not null)
            {
                _remoteDeploymentState.Text = "Dağıtım ayarları seçili bağlantı profiline kaydedildi.";
                _remoteDeploymentState.Foreground = BrushHex("#3DD6AE");
            }
        }
        catch (Exception ex)
        {
            ShowRemoteError(ex.Message);
        }
    }

    private string GetRemoteFileManagerRootValue()
    {
        string configured = _remoteFileRootBox?.Text.Trim() ?? "";
        if (!string.IsNullOrWhiteSpace(configured))
            return ValidateRemoteInstallRoot(configured);
        var profile = GetSelectedRemoteProfile();
        string saved = profile?.RemoteFileManagerRoot ?? "";
        if (string.IsNullOrWhiteSpace(saved))
            saved = profile?.RemoteProjectDirectory ?? "/srv/nfsu2";
        return ValidateRemoteInstallRoot(saved);
    }

    private async Task ApplyRemoteFileManagerRootAsync()
    {
        try
        {
            if (_remoteServerSession?.IsConnected != true)
                throw new InvalidOperationException("Önce SSH bağlantısı kur.");
            string root = ValidateRemoteInstallRoot(_remoteFileRootBox?.Text ?? "");
            var profile = GetSelectedRemoteProfile();
            if (profile is not null)
            {
                profile.RemoteFileManagerRoot = root;
                RemoteServerProfileStore.SaveOrUpdate(profile);
            }
            _remoteFilePathBox!.Text = root;
            await RefreshRemoteFileListAsync();
        }
        catch (Exception ex)
        {
            SetRemoteFileStatus("Kök dizin açılamadı: " + ex.Message, false);
        }
    }

    private async Task ClaimRemoteFileManagerRootAsync()
    {
        if (_remoteServerSession?.IsConnected != true)
        {
            SetRemoteFileStatus("Önce SSH bağlantısı kur.", false);
            return;
        }

        try
        {
            string root = ValidateRemoteInstallRoot(_remoteFileRootBox?.Text ?? GetRemoteFileManagerRootValue());
            var answer = ThemedDialogWindow.Show(
                this,
                $"'{root}' klasörü bu profilin SFTP çalışma alanı olarak işaretlenecek.\n\nBu işlem Docker Compose projesini sahiplenmez, konteynerleri değiştirmez ve mevcut dosyalara dokunmaz. Yalnızca bu dizin içinde dosya yükleme, klasör oluşturma ve silme işlemlerini açan bir güvenlik işaretçisi ekler.\n\nDevam edilsin mi?",
                "Mevcut dizini SFTP için etkinleştir",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning);
            if (answer != MessageBoxResult.Yes) return;

            await _remoteServerSession.ExecuteDeploymentCommandAsync(
                BuildRemoteFileManagerClaimCommand(root), TimeSpan.FromSeconds(20));
            var profile = GetSelectedRemoteProfile();
            if (profile is not null)
            {
                profile.RemoteFileManagerRoot = root;
                RemoteServerProfileStore.SaveOrUpdate(profile);
            }
            SetRemoteFileStatus("Dizin SFTP için etkinleştirildi. Dosya işlemleri açıldı; Docker kurulumu sahiplenilmedi.", true);
            if (_remoteFileClaimButton is not null) _remoteFileClaimButton.IsEnabled = false;
            await RefreshRemoteFileListAsync();
        }
        catch (Exception ex)
        {
            SetRemoteFileStatus("Dizin etkinleştirilemedi: " + ex.Message, false);
        }
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

        string fileRoot = string.IsNullOrWhiteSpace(profile.RemoteFileManagerRoot)
            ? profile.RemoteProjectDirectory ?? "/srv/nfsu2"
            : profile.RemoteFileManagerRoot;
        if (_remoteFileRootBox is not null) _remoteFileRootBox.Text = fileRoot;
        if (_remoteFilePathBox is not null) _remoteFilePathBox.Text = fileRoot.TrimEnd('/');

        try
        {
            if (_remotePasswordBox is not null)
                _remotePasswordBox.Password = RemoteSecretProtector.Unprotect(profile.ProtectedPassword);
            if (_remoteKeyPassphraseBox is not null)
                _remoteKeyPassphraseBox.Password = RemoteSecretProtector.Unprotect(profile.ProtectedKeyPassphrase);
        }
        catch (Exception ex)
        {
            if (_remotePasswordBox is not null) _remotePasswordBox.Password = "";
            if (_remoteKeyPassphraseBox is not null) _remoteKeyPassphraseBox.Password = "";
            SetRemoteStatus("Kaydedilmiş SSH parolası açılamadı. Parolayı yeniden girip profili kaydet. " + ex.Message, connected: false);
        }

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

        string typedPassword = _remotePasswordBox?.Password ?? "";
        string typedKeyPassphrase = _remoteKeyPassphraseBox?.Password ?? "";
        return new RemoteServerProfile
        {
            Id = selected?.Id ?? Guid.NewGuid().ToString("N"),
            Name = name,
            Host = host,
            Port = port,
            Username = username,
            AuthenticationMode = privateKey ? "private-key" : "password",
            PrivateKeyPath = privateKey ? keyPath : "",
            ProtectedPassword = string.IsNullOrEmpty(typedPassword)
                ? selected?.ProtectedPassword ?? ""
                : RemoteSecretProtector.Protect(typedPassword),
            ProtectedKeyPassphrase = string.IsNullOrEmpty(typedKeyPassphrase)
                ? selected?.ProtectedKeyPassphrase ?? ""
                : RemoteSecretProtector.Protect(typedKeyPassphrase),
            HostKeyFingerprint = selected?.HostKeyFingerprint ?? "",
            RemoteFileManagerRoot = selected?.RemoteFileManagerRoot ?? "",
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
            SetRemoteStatus("Profil ve parolalar Windows hesabına bağlı DPAPI ile şifrelenerek kaydedildi.", connected: false);
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

            string password = !string.IsNullOrEmpty(_remotePasswordBox?.Password)
                ? _remotePasswordBox!.Password
                : RemoteSecretProtector.Unprotect(profile.ProtectedPassword);
            string keyPassphrase = !string.IsNullOrEmpty(_remoteKeyPassphraseBox?.Password)
                ? _remoteKeyPassphraseBox!.Password
                : RemoteSecretProtector.Unprotect(profile.ProtectedKeyPassphrase);

            if (_remoteServerSession?.IsConnected == true)
                DisconnectRemoteSession();
            if (!string.Equals(profile.AuthenticationMode, "private-key", StringComparison.OrdinalIgnoreCase) &&
                string.IsNullOrEmpty(password))
                throw new InvalidOperationException("SSH parolasını gir veya parolası kaydedilmiş bir profil seç.");

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

            string diagnostics = await session.GetServerDiagnosticsAsync();
            if (!string.IsNullOrEmpty(password))
                profile.ProtectedPassword = RemoteSecretProtector.Protect(password);
            if (!string.IsNullOrEmpty(keyPassphrase))
                profile.ProtectedKeyPassphrase = RemoteSecretProtector.Protect(keyPassphrase);
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

    private async Task ConnectRemoteAndScanAsync()
    {
        await ConnectRemoteAsync(keepConnected: true);
    }

    private async Task RunRemoteSshCommandAsync()
    {
        if (_remoteServerSession?.IsConnected != true)
        {
            ShowRemoteError("Komut çalıştırmadan önce SSH bağlantısını kur.");
            return;
        }

        string command = _remoteCommandBox?.Text.Trim() ?? "";
        if (string.IsNullOrWhiteSpace(command))
        {
            ShowRemoteError("SSH komut penceresine çalıştırılacak komutu yaz.");
            return;
        }
        if (command.Length > 16000)
        {
            ShowRemoteError("SSH komutu 16.000 karakterden uzun olamaz.");
            return;
        }

        try
        {
            if (_remoteCommandOutputBox is not null)
                _remoteCommandOutputBox.Text = "Komut uzak sunucuda çalıştırılıyor...";
            SetRemoteStatus("SSH komutu çalıştırılıyor...", connected: true);
            string output = await _remoteServerSession.ExecuteDeploymentCommandAsync(command, TimeSpan.FromSeconds(60));
            if (_remoteCommandOutputBox is not null)
                _remoteCommandOutputBox.Text = string.IsNullOrWhiteSpace(output) ? "Komut tamamlandı; çıktı üretilmedi." : output.TrimEnd();
            SetRemoteStatus("SSH komutu tamamlandı.", connected: true);
            AppendLog("SSH komut penceresinde uzak komut tamamlandı.", "OK", "REMOTE");
        }
        catch (Exception ex)
        {
            if (_remoteCommandOutputBox is not null)
                _remoteCommandOutputBox.Text = "SSH KOMUT HATASI\n" + ex.Message;
            SetRemoteStatus("SSH komutu başarısız oldu.", connected: true);
            AppendLog("SSH komut penceresi başarısız: " + ex.Message, "HATA", "REMOTE");
        }
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
        SetRemoteStatus("SSH bağlantısı kapatıldı. Kaydedilmiş parolalar profil içinde şifreli olarak korundu.", connected: false);
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
