using System.Reflection;
using System.Windows.Interop;
using ScreenshotTool;

internal static class Program
{
    [STAThread]
    private static int Main()
    {
        var app = new App { ShutdownMode = System.Windows.ShutdownMode.OnExplicitShutdown };
        var window = new MainWindow();
        app.MainWindow = window;
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
        var initialize = typeof(MainWindow).GetMethod("InitializeBackgroundServices", flags)!;
        var registered = typeof(MainWindow).GetField("_hotkeyRegistered", flags)!;
        try
        {
            initialize.Invoke(window, null);
            Check(!window.IsVisible, "Background initialization does not show settings");
            Check(new WindowInteropHelper(window).Handle != IntPtr.Zero, "Hidden window has a message handle");
            Check((bool)registered.GetValue(window)!, "Hotkey registered before settings is ever shown");
            initialize.Invoke(window, null);
            Check((bool)registered.GetValue(window)!, "Repeated initialization keeps registration");
            window.Show();
            window.Close();
            Check(!window.IsVisible && (bool)registered.GetValue(window)!, "Closing settings hides it and retains hotkey");
            return 0;
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine(exception);
            return 1;
        }
        finally
        {
            typeof(MainWindow).GetField("_exitRequested", flags)!.SetValue(window, true);
            window.Close();
        }
    }

    private static void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
        Console.WriteLine("PASS: " + message);
    }
}
