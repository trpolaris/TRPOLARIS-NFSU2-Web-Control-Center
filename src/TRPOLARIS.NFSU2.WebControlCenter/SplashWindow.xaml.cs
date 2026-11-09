using System.Windows;

namespace TRPOLARIS.NFSU2.WebControlCenter;

public partial class SplashWindow : Window
{
    public SplashWindow()
    {
        InitializeComponent();
        Title = $"NFSU2 Server control {AppVersion.Display}";
        SplashVersionText.Text = AppVersion.Display;
    }

    private void SplashSurface_MouseLeftButtonDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (e.LeftButton == System.Windows.Input.MouseButtonState.Pressed)
            DragMove();
    }

    public void ReportProgress(int percent, string status, string? detail = null)
    {
        void Apply()
        {
            SplashProgress.Value = Math.Clamp(percent, 0, 100);
            SplashPercent.Text = Math.Clamp(percent, 0, 100).ToString() + "%";
            SplashStatus.Text = status;
            SplashDetail.Text = detail ?? string.Empty;
        }

        if (Dispatcher.CheckAccess()) Apply();
        else Dispatcher.BeginInvoke(Apply);
    }
}
