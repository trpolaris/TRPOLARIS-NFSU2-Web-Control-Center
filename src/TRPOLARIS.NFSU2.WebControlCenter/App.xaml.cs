using System.Windows;
using System.Diagnostics;

namespace TRPOLARIS.NFSU2.WebControlCenter;

public partial class App : Application
{
    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        ShutdownMode = ShutdownMode.OnExplicitShutdown;

        if (NativeUpdateHelper.IsHelperInvocation(e.Args))
        {
            int helperExitCode = await NativeUpdateHelper.RunHelperAsync(e.Args);
            Shutdown(helperExitCode);
            return;
        }

        string? updateHealthMarker = NativeUpdateHelper.GetArgumentValue(e.Args, "--update-health");
        string? helperCleanupPath = NativeUpdateHelper.GetArgumentValue(e.Args, "--cleanup-helper");
        NativeUpdateHelper.ScheduleHelperCleanup(helperCleanupPath);

        string? runningExecutable = Environment.ProcessPath;
        if (string.IsNullOrWhiteSpace(updateHealthMarker) &&
            NativeUpdateHelper.NeedsExecutableNameMigration(runningExecutable))
        {
            NativeUpdateHelper.StartLegacyNameMigration(runningExecutable!);
            Shutdown();
            return;
        }

        var startupClock = Stopwatch.StartNew();
        var splash = new SplashWindow();
        splash.Show();
        splash.ReportProgress(4, "Program başlatılıyor", "NFSU2 Server control başlangıç bileşenleri yükleniyor.");
        await Task.Delay(450);
        splash.ReportProgress(12, "Güncelleme sunucusuna bağlanılıyor", "GitHub Release bilgileri kontrol için hazırlanıyor.");

        MainWindow main = new();
        MainWindow = main;
        try
        {
            bool updateStarted = await main.RunStartupUpdateCheckAsync(splash.ReportProgress);
            if (updateStarted) return;

            await KeepSplashVisibleAsync(splash, startupClock, minimumDuration: TimeSpan.FromMilliseconds(5600));
            splash.ReportProgress(100, "Program başlatılıyor", "NFSU2 Server control hazır. Ana pencere açılıyor.");
            await Task.Delay(180);
            main.Show();
            ShutdownMode = ShutdownMode.OnMainWindowClose;
            NativeUpdateHelper.WriteHealthMarker(updateHealthMarker);
            splash.Close();
        }
        catch (Exception)
        {
            splash.ReportProgress(84, "Mevcut sürümle devam ediliyor", "Başlangıç denetimi tamamlanamadı; kurulu dosyalar korunuyor.");
            await KeepSplashVisibleAsync(splash, startupClock, minimumDuration: TimeSpan.FromMilliseconds(5600));
            splash.ReportProgress(100, "Program başlatılıyor", "Mevcut NFSU2 Server control sürümü açılıyor.");
            await Task.Delay(180);
            main.Show();
            ShutdownMode = ShutdownMode.OnMainWindowClose;
            NativeUpdateHelper.WriteHealthMarker(updateHealthMarker);
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
