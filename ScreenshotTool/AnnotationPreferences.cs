using Microsoft.Win32;

namespace ScreenshotTool;

internal sealed class AnnotationPreferences
{
    public string Color { get; set; } = "#E34D59";
    public double Width { get; set; } = 3;
    public double FontSize { get; set; } = 22;
    public double Opacity { get; set; } = 30;
    public static AnnotationPreferences Load()
    {
        using var key = Registry.CurrentUser.OpenSubKey("Software\\ScreenshotTool\\Annotation");
        var value = new AnnotationPreferences();
        if (key == null) return value;
        var color = key.GetValue("Color") as string;
        if (color != null && System.Text.RegularExpressions.Regex.IsMatch(color, "^#[0-9a-fA-F]{6}$")) value.Color = color;
        if (double.TryParse(key.GetValue("Width")?.ToString(), out var width)) value.Width = Math.Clamp(width, 1, 12);
        if (double.TryParse(key.GetValue("FontSize")?.ToString(), out var font)) value.FontSize = Math.Clamp(font, 12, 72);
        if (double.TryParse(key.GetValue("Opacity")?.ToString(), out var opacity)) value.Opacity = Math.Clamp(opacity, 10, 100);
        return value;
    }
    public void Save()
    {
        using var key = Registry.CurrentUser.CreateSubKey("Software\\ScreenshotTool\\Annotation");
        key.SetValue("Color", Color);
        key.SetValue("Width", (int)Width);
        key.SetValue("FontSize", (int)FontSize);
        key.SetValue("Opacity", (int)Opacity);
    }
}
