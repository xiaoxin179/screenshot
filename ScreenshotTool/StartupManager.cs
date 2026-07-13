using Microsoft.Win32;

namespace ScreenshotTool;

internal static class StartupManager
{
    private const string RunKey = "Software\\Microsoft\\Windows\\CurrentVersion\\Run";
    private const string AppName = "ScreenshotTool";
    public static bool IsEnabled() => Registry.CurrentUser.OpenSubKey(RunKey)?.GetValue(AppName) is not null;
    public static void SetEnabled(bool enabled)
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKey, true) ?? Registry.CurrentUser.CreateSubKey(RunKey);
        if (enabled) key.SetValue(AppName, $"\"{Environment.ProcessPath}\" --autostart"); else key.DeleteValue(AppName, false);
    }
}
