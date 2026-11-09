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

        Title = "NFSU2 Server control - Güncelleme tercihleri";
        Width = 650;
        Height = 650;
        MinWidth = 650;
        MinHeight = 650;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Owner = Application.Current.MainWindow;
        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.NoResize;
        Background = Brush("#081523");
        Foreground = Brush("#EDF4FC");
        FontFamily = new FontFamily("Segoe UI Variable Text, Segoe UI");
        ShowInTaskbar = false;
        UseLayoutRounding = true;
        SnapsToDevicePixels = true;

        var windowGrid = new Grid();
        windowGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(42) });
        windowGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

        var root = new Grid { Margin = new Thickness(24, 18, 24, 20) };
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        var caption = new Border
        {
            Background = Brush("#0A1828"),
            BorderBrush = Brush("#20384E"),
            BorderThickness = new Thickness(0, 0, 0, 1),
            Padding = new Thickness(17, 0, 8, 0)
        };
        var captionGrid = new Grid();
        captionGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        captionGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var captionText = new TextBlock
        {
            Text = "NFSU2 SERVER CONTROL",
            FontSize = 9,
            FontWeight = FontWeights.Bold,
            Foreground = Brush("#68DCCC"),
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
            Foreground = Brush("#D9E8F5"),
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
            if (e.OriginalSource is not Button && e.LeftButton == System.Windows.Input.MouseButtonState.Pressed)
                DragMove();
        };
        Grid.SetRow(caption, 0);
        windowGrid.Children.Add(caption);
        Grid.SetRow(root, 1);
        windowGrid.Children.Add(root);
        Content = new Border
        {
            Background = Brush("#081523"),
            BorderBrush = Brush("#31506A"),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(12),
            ClipToBounds = true,
            Child = windowGrid
        };

        var heading = new StackPanel { Margin = new Thickness(0, 0, 0, 18) };
        heading.Children.Add(new TextBlock
        {
            Text = "Güncelleme tercihleri",
            FontSize = 23,
            FontWeight = FontWeights.SemiBold,
            Foreground = Brush("#F1F7FF")
        });
        heading.Children.Add(new TextBlock
        {
            Text = "Sürüm kanalı, kontrol sıklığı ve son güncelleme geçmişi.",
            FontSize = 11,
            Foreground = Brush("#93A9BE"),
            Margin = new Thickness(0, 6, 0, 0)
        });
        Grid.SetRow(heading, 0);
        root.Children.Add(heading);

        var settingsCard = new Border
        {
            Background = Brush("#102033"),
            BorderBrush = Brush("#2A435A"),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(10),
            Padding = new Thickness(16),
            Margin = new Thickness(0, 0, 0, 17)
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
            Text = "GÜNCELLEME GEÇMİŞİ",
            FontSize = 9,
            FontWeight = FontWeights.Bold,
            Foreground = Brush("#70D7CE"),
            Margin = new Thickness(2, 0, 0, 9)
        });
        var historyList = new ListBox
        {
            Style = (Style)Application.Current.FindResource("DarkListBox"),
            MinHeight = 76,
            MaxHeight = 260,
            HorizontalContentAlignment = HorizontalAlignment.Stretch
        };
        if (history.Count == 0)
        {
            historyList.Items.Add(new ListBoxItem
            {
                Content = "Henüz tamamlanmış bir uygulama güncellemesi kaydı yok.",
                Foreground = Brush("#90A6BB"),
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
                    Foreground = Brush("#DCE8F5")
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
            Margin = new Thickness(0, 16, 0, 0)
        };
        var cancel = new Button { Content = "Vazgeç", MinWidth = 95, Padding = new Thickness(14, 8, 14, 8), Margin = new Thickness(0, 0, 8, 0) };
        var save = new Button { Content = "Kaydet", MinWidth = 110, Padding = new Thickness(14, 8, 14, 8), IsDefault = true };
        StyleButton(cancel, "#1A2C40", "#B7C9DA");
        StyleButton(save, "#176B70", "#F2FFFF");
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
            FontSize = 8,
            FontWeight = FontWeights.Bold,
            Foreground = Brush("#83A0B8"),
            Margin = new Thickness(0, 0, 0, 7)
        });
        foreach (var item in items) combo.Items.Add(item);
        combo.Style = (Style)Application.Current.FindResource("DarkComboBox");
        combo.SelectedIndex = selectedIndex;
        combo.Height = 42;
        combo.MaxDropDownHeight = 270;
        combo.Margin = new Thickness(0, 0, 0, 18);
        combo.Background = Brush("#0D1D2D");
        combo.Foreground = Brush("#EDF5FF");
        combo.BorderBrush = Brush("#3B5872");
        combo.Padding = new Thickness(10, 7, 10, 7);
        parent.Children.Add(combo);
    }

    private static void StyleButton(Button button, string background, string foreground)
    {
        button.Background = Brush(background);
        button.Foreground = Brush(foreground);
        button.BorderBrush = Brush("#36536A");
        button.BorderThickness = new Thickness(1);
        button.FontWeight = FontWeights.SemiBold;
        button.Cursor = System.Windows.Input.Cursors.Hand;
    }

    private static SolidColorBrush Brush(string value) => new((Color)ColorConverter.ConvertFromString(value));
}
