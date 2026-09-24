using System.Windows;

namespace ScreenshotTool;

public partial class App : System.Windows.Application
{
    private Mutex? _mutex;
    protected override void OnStartup(StartupEventArgs e)
    {
        _mutex = new Mutex(true, "ScreenshotTool.SingleInstance", out var created);
        if (!created) { Shutdown(); return; }
        base.OnStartup(e);
        var window = new MainWindow();
        MainWindow = window;
        window.InitializeBackgroundServices();
        if (!e.Args.Contains("--autostart", StringComparer.OrdinalIgnoreCase)) window.Show();
    }
}
