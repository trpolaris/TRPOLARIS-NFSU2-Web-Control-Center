using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Effects;

namespace TRPOLARIS.NFSU2.WebControlCenter;

/// <summary>
/// Branded modal dialog used in place of the Windows MessageBox surface.
/// The return values deliberately match MessageBoxResult so existing
/// confirmation and validation flows keep their current behavior.
/// </summary>
internal sealed class ThemedDialogWindow : Window
{
    private MessageBoxResult _result = MessageBoxResult.Cancel;

    private ThemedDialogWindow(
        string message,
        string caption,
        MessageBoxButton buttons,
        MessageBoxImage image)
    {
        Title = string.IsNullOrWhiteSpace(caption) ? "NFSU2 Server control" : caption;
        Width = 570;
        MinWidth = 500;
        MaxWidth = 760;
        MinHeight = 210;
        MaxHeight = 760;
        SizeToContent = SizeToContent.Height;
        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;
        ShowActivated = true;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Background = Brush("#0A1020");
        Foreground = Brush("#EDF3FF");
        FontFamily = new FontFamily("Segoe UI Variable Text, Segoe UI");
        SnapsToDevicePixels = true;
        UseLayoutRounding = true;

        var (accent, glyph) = GetIconStyle(image);

        var layout = new Grid();
        layout.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        layout.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        layout.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        var captionBar = new Border
        {
            Height = 44,
            Background = Brush("#0D1527"),
            BorderBrush = Brush("#263858"),
            BorderThickness = new Thickness(0, 0, 0, 1),
            Padding = new Thickness(17, 0, 8, 0)
        };
        var captionGrid = new Grid();
        captionGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        captionGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var appLabel = new TextBlock
        {
            Text = "NFSU2 SERVER CONTROL",
            FontSize = 9,
            FontWeight = FontWeights.Bold,
            Foreground = Brush("#70A5FF"),
            VerticalAlignment = VerticalAlignment.Center
        };
        captionGrid.Children.Add(appLabel);
        var close = new Button
        {
            Content = "×",
            Width = 34,
            Height = 30,
            FontSize = 19,
            Padding = new Thickness(0),
            Margin = new Thickness(6, 0, 0, 0),
            Foreground = Brush("#D7E3F8"),
            Background = Brush("Transparent"),
            BorderBrush = Brush("Transparent"),
            BorderThickness = new Thickness(0),
            Cursor = Cursors.Hand,
            Focusable = false,
            ToolTip = "Kapat"
        };
        close.Click += (_, _) => Finish(MessageBoxResult.Cancel);
        try { close.Style = (Style)Application.Current.FindResource("WindowCloseButton"); } catch { }
        Grid.SetColumn(close, 1);
        captionGrid.Children.Add(close);
        captionBar.Child = captionGrid;
        captionBar.MouseLeftButtonDown += (_, e) =>
        {
            if (e.OriginalSource is DependencyObject source && IsInsideButton(source)) return;
            if (e.LeftButton == MouseButtonState.Pressed && e.ClickCount == 1)
                DragMove();
        };
        Grid.SetRow(captionBar, 0);
        layout.Children.Add(captionBar);

        var messagePanel = new Grid { Margin = new Thickness(24, 24, 24, 22) };
        messagePanel.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(54) });
        messagePanel.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        var iconSurface = new Border
        {
            Width = 42,
            Height = 42,
            CornerRadius = new CornerRadius(14),
            Background = Brush("#182540"),
            BorderBrush = Brush(accent),
            BorderThickness = new Thickness(1),
            VerticalAlignment = VerticalAlignment.Top,
            Child = new TextBlock
            {
                Text = glyph,
                FontSize = glyph == "i" ? 20 : 23,
                FontWeight = FontWeights.Bold,
                Foreground = Brush(accent),
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                TextAlignment = TextAlignment.Center
            }
        };
        messagePanel.Children.Add(iconSurface);

        var messageText = new TextBlock
        {
            Text = message ?? string.Empty,
            FontSize = 13,
            LineHeight = 21,
            TextWrapping = TextWrapping.Wrap,
            Foreground = Brush("#EDF3FF"),
            VerticalAlignment = VerticalAlignment.Center
        };
        var messageScroll = new ScrollViewer
        {
            Content = messageText,
            MaxHeight = 420,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            CanContentScroll = false
        };
        var messageContent = new StackPanel();
        messageContent.Children.Add(new TextBlock
        {
            Text = caption,
            FontSize = 16,
            FontWeight = FontWeights.SemiBold,
            Foreground = Brush("#EDF3FF"),
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 10)
        });
        messageContent.Children.Add(messageScroll);
        Grid.SetColumn(messageContent, 1);
        messagePanel.Children.Add(messageContent);
        Grid.SetRow(messagePanel, 1);
        layout.Children.Add(messagePanel);

        var footer = new Border
        {
            Background = Brush("#0D1527"),
            BorderBrush = Brush("#263858"),
            BorderThickness = new Thickness(0, 1, 0, 0),
            Padding = new Thickness(20, 13, 20, 13)
        };
        var footerPanel = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right
        };
        foreach (var item in GetButtons(buttons))
        {
            var button = new Button
            {
                Content = item.Label,
                MinWidth = item.Result == MessageBoxResult.Yes || item.Result == MessageBoxResult.OK ? 104 : 90,
                MinHeight = 38,
                Margin = new Thickness(8, 0, 0, 0),
                Padding = new Thickness(15, 7, 15, 7),
                FontSize = 12,
                FontWeight = FontWeights.SemiBold,
                Cursor = Cursors.Hand,
                IsDefault = item.IsDefault,
                IsCancel = item.IsCancel
            };
            try
            {
                button.Style = (Style)Application.Current.FindResource(item.IsDefault ? "ModernButton" : "SecondaryButton");
            }
            catch
            {
                button.Background = Brush(item.IsDefault ? "#3E6EB2" : "#182540");
                button.Foreground = Brush("#EDF3FF");
                button.BorderBrush = Brush("#354B70");
            }
            var result = item.Result;
            button.Click += (_, _) => Finish(result);
            footerPanel.Children.Add(button);
        }
        footer.Child = footerPanel;
        Grid.SetRow(footer, 2);
        layout.Children.Add(footer);

        var shell = new Border
        {
            Background = Brush("#0A1020"),
            BorderBrush = Brush("#263858"),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(13),
            ClipToBounds = true,
            Effect = new DropShadowEffect
            {
                Color = Color.FromRgb(0, 0, 0),
                BlurRadius = 28,
                ShadowDepth = 8,
                Opacity = 0.45
            },
            Child = layout
        };
        Content = shell;
        PreviewKeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape)
            {
                Finish(MessageBoxResult.Cancel);
                e.Handled = true;
            }
        };
        Loaded += (_, _) => Focus();
    }

    public static MessageBoxResult Show(
        Window? owner,
        string message,
        string caption,
        MessageBoxButton buttons,
        MessageBoxImage image)
    {
        var dialog = new ThemedDialogWindow(message, caption, buttons, image);
        var activeOwner = owner;
        if (activeOwner is null && Application.Current?.MainWindow is { IsVisible: true } main)
            activeOwner = main;

        if (activeOwner is not null && activeOwner != dialog)
            dialog.Owner = activeOwner;
        else
            dialog.WindowStartupLocation = WindowStartupLocation.CenterScreen;

        dialog.ShowDialog();
        return dialog._result;
    }

    private void Finish(MessageBoxResult result)
    {
        _result = result;
        DialogResult = result is MessageBoxResult.OK or MessageBoxResult.Yes;
    }

    private static bool IsInsideButton(DependencyObject source)
    {
        for (DependencyObject? current = source; current is not null; current = VisualTreeHelper.GetParent(current))
            if (current is Button) return true;
        return false;
    }

    private static (string Accent, string Glyph) GetIconStyle(MessageBoxImage image) => image switch
    {
        MessageBoxImage.Error or MessageBoxImage.Hand or MessageBoxImage.Stop => ("#F47C93", "×"),
        MessageBoxImage.Warning or MessageBoxImage.Exclamation => ("#F6C66B", "!"),
        MessageBoxImage.Question => ("#9C8CFF", "?"),
        _ => ("#70A5FF", "i")
    };

    private static IEnumerable<(string Label, MessageBoxResult Result, bool IsDefault, bool IsCancel)> GetButtons(MessageBoxButton buttons)
    {
        return buttons switch
        {
            MessageBoxButton.OKCancel => new[]
            {
                ("Vazgeç", MessageBoxResult.Cancel, false, true),
                ("Tamam", MessageBoxResult.OK, true, false)
            },
            MessageBoxButton.YesNo => new[]
            {
                ("Hayır", MessageBoxResult.No, false, true),
                ("Evet", MessageBoxResult.Yes, true, false)
            },
            MessageBoxButton.YesNoCancel => new[]
            {
                ("İptal", MessageBoxResult.Cancel, false, true),
                ("Hayır", MessageBoxResult.No, false, false),
                ("Evet", MessageBoxResult.Yes, true, false)
            },
            _ => new[]
            {
                ("Tamam", MessageBoxResult.OK, true, true)
            }
        };
    }

    private static SolidColorBrush Brush(string value)
    {
        if (value == "Transparent") return new SolidColorBrush(Colors.Transparent);
        var brush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(value));
        brush.Freeze();
        return brush;
    }
}