using System.Windows;
using System.Diagnostics;

namespace TRPOLARIS.NFSU2.WebControlCenter;

public partial class App : Application
{
    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        ShutdownMode = ShutdownMode.OnExplicitShutdown;

        var startupClock = Stopwatch.StartNew();
        var splash = new SplashWindow();
        splash.Show();
        splash.ReportProgress(4, "Program başlatılıyor", "TRPOLARIS başlangıç bileşenleri yükleniyor.");
        await Task.Delay(450);
        splash.ReportProgress(12, "Güncelleme sunucusuna bağlanılıyor", "GitHub Release bilgileri kontrol için hazırlanıyor.");

        MainWindow main = new();
        MainWindow = main;
        try
        {
            bool updateStarted = await main.RunStartupUpdateCheckAsync(splash.ReportProgress);
            if (updateStarted) return;

            await KeepSplashVisibleAsync(splash, startupClock, minimumDuration: TimeSpan.FromMilliseconds(5600));
            splash.ReportProgress(100, "Program başlatılıyor", "Control Center hazır. Ana pencere açılıyor.");
            await Task.Delay(180);
            main.Show();
            ShutdownMode = ShutdownMode.OnMainWindowClose;
            splash.Close();
        }
        catch (Exception)
        {
            splash.ReportProgress(84, "Mevcut sürümle devam ediliyor", "Başlangıç denetimi tamamlanamadı; kurulu dosyalar korunuyor.");
            await KeepSplashVisibleAsync(splash, startupClock, minimumDuration: TimeSpan.FromMilliseconds(5600));
            splash.ReportProgress(100, "Program başlatılıyor", "Mevcut Control Center sürümü açılıyor.");
            await Task.Delay(180);
            main.Show();
            ShutdownMode = ShutdownMode.OnMainWindowClose;
            splash.Close();
        }
    }

    private static async Task KeepSplashVisibleAsync(SplashWindow splash, Stopwatch clock, TimeSpan minimumDuration)
    {
        // Keep the branded startup screen visible for a consistent 5.6 seconds
        // when the real GitHub check finishes quickly or no update is available.
        var phases = new (int Percent, string Status, string Detail)[]
        {
            (86, "Güncelleme kontrolü tamamlandı", "Kurulu uygulama sürümü ve güncelleme sonucu doğrulandı."),
            (90, "Uygulama bileşenleri hazırlanıyor", "Ana pencere ve yerel çalışma alanı açılışa hazırlanıyor."),
            (95, "Başlatma tamamlanıyor", "Kurulum ve altyapı araçları kullanıma hazırlanıyor.")
        };

        foreach (var phase in phases)
        {
            var remaining = minimumDuration - clock.Elapsed;
            if (remaining <= TimeSpan.Zero) break;
            splash.ReportProgress(phase.Percent, phase.Status, phase.Detail);
            var pause = remaining < TimeSpan.FromMilliseconds(650)
                ? remaining
                : TimeSpan.FromMilliseconds(650);
            await Task.Delay(pause);
        }

        var finalRemaining = minimumDuration - clock.Elapsed;
        if (finalRemaining > TimeSpan.Zero)
        {
            splash.ReportProgress(98, "Başlatma tamamlanıyor", "Control Center ana ekranı açılmak üzere.");
            await Task.Delay(finalRemaining);
        }
    }
}
