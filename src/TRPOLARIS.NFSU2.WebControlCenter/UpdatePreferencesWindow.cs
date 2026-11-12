using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace TRPOLARIS.NFSU2.WebControlCenter;

internal sealed class UpdatePreferencesWindow : Window
{
    private readonly ComboBox _channel = new();
    private readonly ComboBox _frequency = new();

    public UpdateSettings Settings { get; private set; }

    public UpdatePreferencesWindow(UpdateSettings settings, IReadOnlyList<UpdateHistoryEntry> history)
    {
        Settings = new UpdateSettings
        {
            Channel = settings.Channel,
            Frequency = settings.Frequency,
            LastCheckUtc = settings.LastCheckUtc,
            StableETag = settings.StableETag,
            StableReleaseJson = settings.StableReleaseJson,
            BetaETag = settings.BetaETag,
            BetaReleaseJson = settings.BetaReleaseJson
        };

        Title = "NFSU2 Server control - Güncelleme ve ayarlar";
        Width = 600;
        Height = 590;
        MinWidth = 600;
        MinHeight = 590;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Owner = Application.Current.MainWindow;
        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.NoResize;
        Background = Brush("#0D1219");
        Foreground = Brush("#F4F6F8");
        FontFamily = new FontFamily("Segoe UI Variable Text, Segoe UI");
        ShowInTaskbar = false;
        UseLayoutRounding = true;
        SnapsToDevicePixels = true;

        var windowGrid = new Grid();
        windowGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(38) });
        windowGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

        var root = new Grid { Margin = new Thickness(20, 16, 20, 16) };
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        var caption = new Border
        {
            Background = Brush("#10151D"),
            BorderBrush = Brush("#303D4D"),
            BorderThickness = new Thickness(0, 0, 0, 1),
            Padding = new Thickness(16, 0, 8, 0)
        };
        var captionGrid = new Grid();
        captionGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        captionGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var captionText = new TextBlock
        {
            Text = "NFSU2 SERVER CONTROL  /  SETTINGS  ·  V2.0",
            FontSize = 9,
            FontWeight = FontWeights.Bold,
            Foreground = Brush("#FF8243"),
            VerticalAlignment = VerticalAlignment.Center
        };
        captionGrid.Children.Add(captionText);
        var close = new Button
        {
            Content = "×",
            Width = 38,
            Height = 32,
            Padding = new Thickness(0),
            FontSize = 19,
            Focusable = false,
            Cursor = System.Windows.Input.Cursors.Hand,
            Foreground = Brush("#E8EDF2"),
            Background = Brush("Transparent"),
            BorderThickness = new Thickness(0)
        };
        try { close.Style = (Style)Application.Current.FindResource("WindowCloseButton"); } catch { }
        close.Click += (_, _) => Close();
        Grid.SetColumn(close, 1);
        captionGrid.Children.Add(close);
        caption.Child = captionGrid;
        caption.MouseLeftButtonDown += (_, e) =>
        {
            if (e.OriginalSource is DependencyObject source && IsInsideButton(source)) return;
            if (e.LeftButton == System.Windows.Input.MouseButtonState.Pressed)
                DragMove();
        };
        Grid.SetRow(caption, 0);
        windowGrid.Children.Add(caption);
        Grid.SetRow(root, 1);
        windowGrid.Children.Add(root);
        Content = new Border
        {
            Background = Brush("#111821"),
            BorderBrush = Brush("#3A4755"),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(16),
            ClipToBounds = true,
            Child = windowGrid
        };

        var heading = new StackPanel { Margin = new Thickness(0, 0, 0, 14) };
        heading.Children.Add(new TextBlock
        {
            Text = "Güncelleme ayarları",
            FontSize = 22,
            FontWeight = FontWeights.SemiBold,
            Foreground = Brush("#F4F6F8")
        });
        heading.Children.Add(new TextBlock
        {
            Text = $"Geçerli sürüm {AppVersion.Display}  ·  Sürüm kanalı, kontrol sıklığı ve işlem geçmişi.",
            FontSize = 11,
            Foreground = Brush("#A5B0BE"),
            Margin = new Thickness(0, 4, 0, 0)
        });
        Grid.SetRow(heading, 0);
        root.Children.Add(heading);

        var settingsCard = new Border
        {
            Background = Brush("#171F29"),
            BorderBrush = Brush("#303D4D"),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(11),
            Padding = new Thickness(16),
            Margin = new Thickness(0, 0, 0, 12)
        };
        var settingsStack = new StackPanel();
        AddField(settingsStack, "GÜNCELLEME KANALI", _channel,
            new[]
            {
                new ComboBoxItem { Content = "Stable · Yalnızca kararlı sürümler", Tag = UpdateChannel.Stable },
                new ComboBoxItem { Content = "Beta · Önizleme ve test sürümleri", Tag = UpdateChannel.Beta }
            },
            settings.Channel == UpdateChannel.Beta ? 1 : 0);
        AddField(settingsStack, "OTOMATİK KONTROL SIKLIĞI", _frequency,
            new[]
            {
                new ComboBoxItem { Content = "Her uygulama açılışında", Tag = UpdateCheckFrequency.EveryLaunch },
                new ComboBoxItem { Content = "Günde bir kez", Tag = UpdateCheckFrequency.Daily },
                new ComboBoxItem { Content = "Yalnızca manuel kontrol", Tag = UpdateCheckFrequency.Manual }
            },
            settings.Frequency switch
            {
                UpdateCheckFrequency.Daily => 1,
                UpdateCheckFrequency.Manual => 2,
                _ => 0
            });
        Grid.SetRow(settingsCard, 1);
        settingsCard.Child = settingsStack;
        root.Children.Add(settingsCard);

        var historySection = new StackPanel();
        historySection.Children.Add(new TextBlock
        {
            Text = "SON GÜNCELLEME İŞLEMLERİ",
            FontSize = 10,
            FontWeight = FontWeights.Bold,
            Foreground = Brush("#FF9B62"),
            Margin = new Thickness(2, 0, 0, 9)
        });
        var historyList = new ListBox
        {
            Style = (Style)Application.Current.FindResource("DarkListBox"),
            MinHeight = 74,
            MaxHeight = 160,
            HorizontalContentAlignment = HorizontalAlignment.Stretch
        };
        if (history.Count == 0)
        {
            historyList.Items.Add(new ListBoxItem
            {
                Content = "Henüz tamamlanmış bir uygulama güncellemesi kaydı yok.",
                Foreground = Brush("#91A3BE"),
                Padding = new Thickness(8)
            });
        }
        else
        {
            foreach (var item in history.OrderByDescending(x => x.DateUtc).Take(20))
            {
                string date = item.DateUtc.ToLocalTime().ToString("yyyy-MM-dd HH:mm");
                string text = $"{date}   v{item.FromVersion}  →  v{item.ToVersion}\n{item.Channel.ToUpperInvariant()} · {item.Result}";
                historyList.Items.Add(new ListBoxItem
                {
                    Content = text,
                    Padding = new Thickness(8),
                    Margin = new Thickness(0, 0, 0, 4),
                    Foreground = Brush("#E5F2FF")
                });
            }
        }
        Grid.SetRow(historySection, 2);
        historySection.Children.Add(historyList);
        root.Children.Add(historySection);

        var footer = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 10, 0, 0)
        };
        var cancel = new Button { Content = "Vazgeç", MinWidth = 82, Padding = new Thickness(14, 8, 14, 8), Margin = new Thickness(0, 0, 8, 0) };
        var save = new Button { Content = "Kaydet", MinWidth = 96, Padding = new Thickness(14, 8, 14, 8), IsDefault = true };
        StyleButton(cancel, "#192B44", "#D7EAF8");
        StyleButton(save, "#D87841", "#EFFBFF");
        cancel.IsCancel = true;
        cancel.Click += (_, _) => DialogResult = false;
        save.Click += (_, _) =>
        {
            Settings.Channel = (UpdateChannel)((ComboBoxItem)_channel.SelectedItem).Tag!;
            Settings.Frequency = (UpdateCheckFrequency)((ComboBoxItem)_frequency.SelectedItem).Tag!;
            DialogResult = true;
        };
        footer.Children.Add(cancel);
        footer.Children.Add(save);
        Grid.SetRow(footer, 3);
        root.Children.Add(footer);

        PreviewKeyDown += (_, e) =>
        {
            if (e.Key == System.Windows.Input.Key.Escape)
            {
                Close();
                e.Handled = true;
            }
        };
    }

    private static void AddField(StackPanel parent, string label, ComboBox combo, IEnumerable<ComboBoxItem> items, int selectedIndex)
    {
        parent.Children.Add(new TextBlock
        {
            Text = label,
            FontSize = 9,
            FontWeight = FontWeights.Bold,
            Foreground = Brush("#A5B0BE"),
            Margin = new Thickness(0, 0, 0, 7)
        });
        foreach (var item in items) combo.Items.Add(item);
        combo.Style = (Style)Application.Current.FindResource("DarkComboBox");
        combo.SelectedIndex = selectedIndex;
        combo.Height = 38;
        combo.MaxDropDownHeight = 190;
        combo.Margin = new Thickness(0, 0, 0, 10);
        combo.Background = Brush("#111821");
        combo.Foreground = Brush("#F4F6F8");
        combo.BorderBrush = Brush("#303D4D");
        combo.Padding = new Thickness(10, 7, 10, 7);
        parent.Children.Add(combo);
    }

    private static bool IsInsideButton(DependencyObject source)
    {
        for (DependencyObject? current = source; current is not null; current = VisualTreeHelper.GetParent(current))
            if (current is Button) return true;
        return false;
    }

    private static void StyleButton(Button button, string background, string foreground)
    {
        try { button.Style = (Style)Application.Current.FindResource(background is "#D87841" or "#D87841" ? "ModernButton" : "SecondaryButton"); } catch { }
        button.MinHeight = 46;
        button.Background = Brush(background);
        button.Foreground = Brush(foreground);
        button.BorderBrush = Brush("#4B5968");
        button.BorderThickness = new Thickness(1);
        button.FontWeight = FontWeights.SemiBold;
        button.Cursor = System.Windows.Input.Cursors.Hand;
    }

    private static string FormatVersion(string value) =>
        value.Equals("2.0.0", StringComparison.OrdinalIgnoreCase) ? "2.0" : value;

    private static SolidColorBrush Brush(string value) => new((Color)ColorConverter.ConvertFromString(value));
}
