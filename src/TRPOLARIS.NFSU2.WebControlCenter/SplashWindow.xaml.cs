using System.Windows;

namespace TRPOLARIS.NFSU2.WebControlCenter;

public partial class SplashWindow : Window
{
    public SplashWindow()
    {
        InitializeComponent();
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
