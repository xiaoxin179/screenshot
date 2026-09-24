using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace ScreenshotTool;

/// <summary>One consistent 24-unit vector icon family, independent of installed fonts.</summary>
public sealed class ToolIcon : System.Windows.Controls.Control
{
    public static readonly DependencyProperty KindProperty = DependencyProperty.Register(
        nameof(Kind), typeof(string), typeof(ToolIcon), new FrameworkPropertyMetadata("", FrameworkPropertyMetadataOptions.AffectsRender));
    public string Kind { get => (string)GetValue(KindProperty); set => SetValue(KindProperty, value); }

    private static readonly Dictionary<string, Geometry> Icons = new()
    {
        ["Rectangle"] = Shape("M4,4 H20 V20 H4 Z"),
        ["Ellipse"] = Shape("M12,3 A9,9 0 1 1 12,21 A9,9 0 1 1 12,3"),
        ["Line"] = Shape("M4,20 L20,4"),
        ["Arrow"] = Shape("M4,20 L20,4 M9,4 H20 V15"),
        ["Pen"] = Shape("M4,15 L15,4 Q16,3 17,4 L20,7 Q21,8 20,9 L9,20 L3,21 Z M13,6 L18,11 M4,15 L9,20 M3,23 H20"),
        ["Text"] = Shape("M4,6 V3 H20 V6 M12,3 V21 M8,21 H16"),
        ["MosaicRectangle"] = Shape("M3,3 H9 V9 H3 Z M11,3 H17 V9 H11 Z M3,11 H9 V17 H3 Z M11,11 H17 V17 H11 Z M19,11 H23 V17 H19 Z M11,19 H17 V23 H11 Z M19,19 H23 V23 H19 Z"),
        ["MosaicBrush"] = Shape("M3,3 H9 V9 H3 Z M11,3 H17 V9 H11 Z M3,11 H9 V17 H3 Z M11,11 H17 V17 H11 Z M19,11 H23 V17 H19 Z M11,19 H17 V23 H11 Z M19,19 H23 V23 H19 Z"),
        ["Highlight"] = Shape("M4,14 L15,3 L21,9 L10,20 Z M4,14 L10,20 M5,15 L3,20 L7,21 L10,20 M2,23 H13"),
        ["Label"] = Shape("M12,3 A9,9 0 1 1 12,21 A9,9 0 1 1 12,3 M10,9 L12,7 V17 M10,17 H14"),
        ["Watermark"] = Shape("M5,17 L12,3 L19,17 M8,12 H16 M3,21 H21"),
        ["more"] = Shape("M5,11 A1,1 0 1 1 5,13 A1,1 0 1 1 5,11 M12,11 A1,1 0 1 1 12,13 A1,1 0 1 1 12,11 M19,11 A1,1 0 1 1 19,13 A1,1 0 1 1 19,11"),
        ["scroll"] = Shape("M7,3 H3 V7 M17,3 H21 V7 M3,17 V21 H7 M21,17 V21 H17 M12,5 V19 M9,8 L12,5 L15,8 M9,16 L12,19 L15,16"),
        ["pin"] = Shape("M14,3 L21,10 L18,11 L14,15 L14,18 L6,10 L9,10 L13,6 Z M10,14 L3,21"),
        ["undo"] = Shape("M9,5 L4,10 L9,15 M4,10 H15 Q21,10 21,16 Q21,21 16,21"),
        ["redo"] = Shape("M15,5 L20,10 L15,15 M20,10 H9 Q3,10 3,16 Q3,21 8,21"),
        ["save"] = Shape("M12,3 V16 M7,11 L12,16 L17,11 M4,16 V21 H20 V16"),
        ["cancel"] = Shape("M5,5 L19,19 M19,5 L5,19"),
        ["confirm"] = Shape("M4,12 L10,18 L21,5")
    };
    private static Geometry Shape(string path) { var geometry = Geometry.Parse(path); geometry.Freeze(); return geometry; }
    protected override void OnRender(DrawingContext drawingContext)
    {
        base.OnRender(drawingContext);
        if (!Icons.TryGetValue(Kind, out var geometry)) return;
        var scale = Math.Min(ActualWidth, ActualHeight) / 26;
        drawingContext.PushTransform(new TranslateTransform((ActualWidth - 24 * scale) / 2, (ActualHeight - 24 * scale) / 2));
        drawingContext.PushTransform(new ScaleTransform(scale, scale));
        var filled = Kind is "MosaicRectangle" or "MosaicBrush" or "more";
        var pen = new System.Windows.Media.Pen(Foreground, 1.8) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round, LineJoin = PenLineJoin.Round };
        drawingContext.DrawGeometry(filled ? Foreground : null, filled ? null : pen, geometry);
        drawingContext.Pop(); drawingContext.Pop();
    }
}
