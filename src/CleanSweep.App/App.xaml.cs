using System.Windows;

namespace CleanSweep.App;

public partial class App : Application
{
    public static string? ScreenshotOutputDir { get; private set; }

    protected override void OnStartup(StartupEventArgs e)
    {
        for (int i = 0; i < e.Args.Length - 1; i++)
        {
            if (e.Args[i].Equals("--capture-screenshots", StringComparison.OrdinalIgnoreCase))
            {
                ScreenshotOutputDir = e.Args[i + 1];
                break;
            }
        }

        DispatcherUnhandledException += (_, args) =>
        {
            MessageBox.Show(
                $"Beklenmeyen bir hata oluştu:\n{args.Exception.Message}",
                "CleanSweep Hatası",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            args.Handled = true;
        };

        base.OnStartup(e);
    }
}
