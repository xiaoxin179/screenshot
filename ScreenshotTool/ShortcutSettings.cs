using Microsoft.Win32;

namespace ScreenshotTool;

internal static class ShortcutSettings
{
    private const string KeyPath = "Software\\ScreenshotTool";
    public static string Load() => Registry.CurrentUser.OpenSubKey(KeyPath)?.GetValue("Hotkey") as string ?? "Alt+Shift+S";
    public static void Save(string value) { using var key = Registry.CurrentUser.CreateSubKey(KeyPath); key.SetValue("Hotkey", value); }
}
