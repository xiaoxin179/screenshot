using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Point = System.Windows.Point;
using Rect = System.Windows.Rect;
using Button = System.Windows.Controls.Button;
using PixelFormat = System.Drawing.Imaging.PixelFormat;

namespace ScreenshotTool;

public partial class SelectionWindow : Window
{
    private readonly Bitmap _screen;
    private readonly System.Drawing.Rectangle _physicalBounds;
    private readonly AnnotationDocument _document = new();
    private readonly AnnotationPreferences _preferences = AnnotationPreferences.Load();
    private Bitmap? _crop;
    private Point _start;
    private Rect _selection;
    private AnnotationKind? _mode;
    private Annotation? _active, _textAnnotation;
    private bool _dragging, _ready, _finishing;
    private double _scaleX = 1, _scaleY = 1;

    public SelectionWindow(Bitmap screen, System.Drawing.Rectangle physicalBounds)
    {
        InitializeComponent();
        _screen = screen; _physicalBounds = physicalBounds;
        Left = SystemParameters.VirtualScreenLeft; Top = SystemParameters.VirtualScreenTop;
        Width = SystemParameters.VirtualScreenWidth; Height = SystemParameters.VirtualScreenHeight;
        ScreenImage.Source = ToImage(screen);
        UpdateStyleControls();
        _ready = true;
        Loaded += (_, _) =>
        {
            ScreenImage.Width = Overlay.Width = ActualWidth; ScreenImage.Height = Overlay.Height = ActualHeight;
            _scaleX = _screen.Width / ActualWidth; _scaleY = _screen.Height / ActualHeight;
            UpdateDimmedArea(); Activate(); Focus();
        };
        MouseLeftButtonDown += HandleMouseDown; MouseMove += HandleMouseMove; MouseLeftButtonUp += HandleMouseUp; KeyDown += HandleKeyDown;
    }

    internal static BitmapSource ToImage(Bitmap bitmap)
    {
        var data = bitmap.LockBits(new System.Drawing.Rectangle(0, 0, bitmap.Width, bitmap.Height), ImageLockMode.ReadOnly, PixelFormat.Format32bppPArgb);
        try
        {
            var image = BitmapSource.Create(bitmap.Width, bitmap.Height, 96, 96, PixelFormats.Pbgra32, null, data.Scan0, data.Stride * bitmap.Height, data.Stride);
            image.Freeze(); return image;
        }
        finally { bitmap.UnlockBits(data); }
    }

    private void SaveStyle()
    {
        if (!_ready) return;
        try { _preferences.Save(); } catch (Exception ex) { SetStatus("样式未保存：" + ex.Message); }
    }

    private static SolidColorBrush Brush(string hex) => new((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(hex));

    private void UpdateStyleControls()
    {
        FontValue.Text = ((int)_preferences.FontSize).ToString();
        OpacityValue.Text = ((int)_preferences.Opacity).ToString();
        foreach (Button button in ColorOptions.Children.OfType<Button>())
            button.BorderBrush = Brush((string)button.Tag == _preferences.Color ? "#536B60" : "#00FFFFFF");
        var nearest = new[] { 1d, 3d, 6d }.MinBy(width => Math.Abs(width - _preferences.Width));
        foreach (Button button in WidthOptions.Children.OfType<Button>())
            button.Background = Brush(double.Parse((string)button.Tag) == nearest ? "#E4EFE8" : "#00FFFFFF");
    }

    private void ColorClick(object sender, RoutedEventArgs e)
    {
        e.Handled = true; CommitText();
        _preferences.Color = (string)((Button)sender).Tag;
        UpdateStyleControls(); SaveStyle(); Focus();
    }

    private void WidthClick(object sender, RoutedEventArgs e)
    {
        e.Handled = true;
        _preferences.Width = double.Parse((string)((Button)sender).Tag);
        UpdateStyleControls(); SaveStyle(); Focus();
    }

    private void CommitNumbers()
    {
        if (!_ready) return;
        if (int.TryParse(FontValue.Text, out var font)) _preferences.FontSize = Math.Clamp(font, 12, 72);
        if (int.TryParse(OpacityValue.Text, out var opacity)) _preferences.Opacity = Math.Clamp(opacity, 10, 100);
        UpdateStyleControls(); SaveStyle();
    }
    private void NumberLostFocus(object sender, KeyboardFocusChangedEventArgs e) => CommitNumbers();
    private void NumberKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key == Key.Enter) { CommitNumbers(); Focus(); e.Handled = true; }
        else if (e.Key == Key.Escape) { UpdateStyleControls(); Focus(); e.Handled = true; }
    }
    private void NumberStep(object sender, RoutedEventArgs e)
    {
        e.Handled = true; CommitNumbers();
        _preferences.FontSize = Math.Clamp(_preferences.FontSize + ((string)((Button)sender).Tag == "font+" ? 2 : -2), 12, 72);
        UpdateStyleControls(); SaveStyle(); Focus();
    }

    private void UpdateToolOptions()
    {
        var mosaic = _mode is AnnotationKind.MosaicRectangle or AnnotationKind.MosaicBrush;
        var text = _mode is AnnotationKind.Text or AnnotationKind.Label or AnnotationKind.Watermark;
        MosaicOptions.Visibility = mosaic ? Visibility.Visible : Visibility.Collapsed;
        WidthOptions.Visibility = text ? Visibility.Collapsed : Visibility.Visible;
        WidthDivider.Visibility = mosaic ? Visibility.Collapsed : Visibility.Visible;
        FontOptions.Visibility = text ? Visibility.Visible : Visibility.Collapsed;
        ColorOptions.Visibility = mosaic ? Visibility.Collapsed : Visibility.Visible;
        OpacityOptions.Visibility = _mode == AnnotationKind.Watermark ? Visibility.Visible : Visibility.Collapsed;
        OptionsBar.Visibility = _mode.HasValue && MorePanel.Visibility != Visibility.Visible ? Visibility.Visible : Visibility.Collapsed;
        MosaicRectOption.Background = Brush(_mode == AnnotationKind.MosaicRectangle ? "#E4EFE8" : "#00FFFFFF");
        MosaicBrushOption.Background = Brush(_mode == AnnotationKind.MosaicBrush ? "#E4EFE8" : "#00FFFFFF");
        foreach (Button button in ToolsPanel.Children.OfType<Button>())
        {
            var tag = button.Tag.ToString();
            if (tag is "confirm" or "cancel") continue;
            var selected = tag == _mode?.ToString() || (mosaic && button == MosaicButton) || (_mode == AnnotationKind.Watermark && button == MoreButton);
            button.Background = Brush(selected ? "#DCEEE5" : "#00FFFFFF");
            button.Foreground = Brush(selected ? "#287561" : "#343936");
        }
        PositionAuxiliaryPanels();
    }

    private void SetStatus(string text)
    {
        StatusText.Text = text;
        StatusHint.Visibility = string.IsNullOrEmpty(text) ? Visibility.Collapsed : Visibility.Visible;
        PositionAuxiliaryPanels();
    }

    private Point Clamp(Point point) => new(Math.Clamp(point.X, 0, ActualWidth), Math.Clamp(point.Y, 0, ActualHeight));
    private PointF PixelPoint(Point point) => new((float)Math.Clamp((point.X - _selection.Left) * _scaleX, 0, _crop!.Width - 1), (float)Math.Clamp((point.Y - _selection.Top) * _scaleY, 0, _crop!.Height - 1));
    private Annotation NewAnnotation(AnnotationKind kind, Point point)
    {
        var item = new Annotation { Kind = kind, Color = ColorTranslator.FromHtml(_preferences.Color), Width = (float)(_preferences.Width * _scaleX), FontSize = (float)(_preferences.FontSize * _scaleY), Opacity = (float)(_preferences.Opacity / 100), Number = _document.Items.Count(x => x.Kind == AnnotationKind.Label) + 1 };
        item.Points.Add(PixelPoint(point)); return item;
    }

    private void HandleMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (Toolbar.IsMouseOver || OptionsBar.IsMouseOver || MorePanel.IsMouseOver || TextEditor.IsMouseOver || _finishing) return;
        MorePanel.Visibility = Visibility.Collapsed;
        UpdateToolOptions();
        CommitText(); Focus();
        var point = Clamp(e.GetPosition(Overlay));
        if (_mode.HasValue && !_selection.Contains(point)) return;
        _start = point;
        if (_mode is AnnotationKind.Text or AnnotationKind.Watermark or AnnotationKind.Label)
        {
            _textAnnotation = NewAnnotation(_mode.Value, point);
            TextEditor.Text = _mode == AnnotationKind.Watermark ? "水印" : "";
            TextEditor.FontSize = _preferences.FontSize;
            TextEditor.Width = Math.Min(280, Math.Max(40, _selection.Right - point.X));
            TextEditor.Height = Math.Min(100, Math.Max(30, _selection.Bottom - point.Y));
            Canvas.SetLeft(TextEditor, point.X); Canvas.SetTop(TextEditor, point.Y);
            TextEditor.Visibility = Visibility.Visible; TextEditor.Focus(); TextEditor.SelectAll();
            SetStatus("Ctrl+Enter 确认文字 · Esc 放弃"); e.Handled = true; return;
        }
        _dragging = true; CaptureMouse();
        if (_mode == null)
        {
            Toolbar.Visibility = OptionsBar.Visibility = MorePanel.Visibility = StatusHint.Visibility = Visibility.Collapsed; AnnotationPreview.Source = null;
            _document.Clear(); _crop?.Dispose(); _crop = null;
            _selection = new Rect(point, point); SelectionBorder.Visibility = Visibility.Visible;
            PositionElement(SelectionBorder, _selection); UpdateDimmedArea();
        }
        else _active = NewAnnotation(_mode.Value, point);
        e.Handled = true;
    }

    private void HandleMouseMove(object sender, System.Windows.Input.MouseEventArgs e)
    {
        if (!_dragging) return;
        var point = Clamp(e.GetPosition(Overlay));
        if (_mode == null) { _selection = new Rect(_start, point); PositionElement(SelectionBorder, _selection); UpdateDimmedArea(); }
        else if (_active != null)
        {
            var pixel = PixelPoint(point);
            if ((Keyboard.Modifiers & ModifierKeys.Shift) != 0) pixel = AnnotationDocument.Constrain(_active.Points[0], pixel, _active.Kind, _crop!.Size);
            if (_active.Kind is AnnotationKind.Pen or AnnotationKind.MosaicBrush or AnnotationKind.Highlight)
            { if (_active.Points[^1] != pixel) _active.Points.Add(pixel); }
            else { if (_active.Points.Count > 1) _active.Points.RemoveAt(1); _active.Points.Add(pixel); }
            RefreshPreview();
        }
    }

    private void HandleMouseUp(object sender, MouseButtonEventArgs e)
    {
        if (!_dragging) return;
        HandleMouseMove(sender, e); _dragging = false; ReleaseMouseCapture();
        if (_mode == null)
        {
            if (_selection.Width < 3 || _selection.Height < 3) { SelectionBorder.Visibility = Visibility.Collapsed; return; }
            _crop = _screen.Clone(CropBounds(), PixelFormat.Format32bppPArgb);
            ShowToolbar(); RefreshPreview(); Cursor = System.Windows.Input.Cursors.Arrow;
        }
        else if (_active != null) { _document.Add(_active); _active = null; RefreshPreview(); }
        e.Handled = true;
    }

    private System.Drawing.Rectangle CropBounds()
    {
        var left = (int)Math.Round(_selection.Left * _scaleX); var top = (int)Math.Round(_selection.Top * _scaleY);
        var right = (int)Math.Round(_selection.Right * _scaleX); var bottom = (int)Math.Round(_selection.Bottom * _scaleY);
        return System.Drawing.Rectangle.Intersect(System.Drawing.Rectangle.FromLTRB(left, top, right, bottom), new(0, 0, _screen.Width, _screen.Height));
    }

    private void CommitText()
    {
        if (_textAnnotation == null) return;
        _textAnnotation.Text = TextEditor.Text;
        _textAnnotation.TextWidth = (float)Math.Max(1, (TextEditor.Width - 10) * _scaleX);
        if (!string.IsNullOrWhiteSpace(_textAnnotation.Text) || _textAnnotation.Kind == AnnotationKind.Label) _document.Add(_textAnnotation);
        _textAnnotation = null; TextEditor.Visibility = Visibility.Collapsed; RefreshPreview();
    }
    private void EditorKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key == Key.Escape) { _textAnnotation = null; TextEditor.Visibility = Visibility.Collapsed; Focus(); e.Handled = true; }
        else if (e.Key == Key.Enter && (Keyboard.Modifiers & ModifierKeys.Control) != 0) { CommitText(); Focus(); e.Handled = true; }
    }
    private void RefreshPreview()
    {
        if (_crop == null) return;
        using var rendered = _document.Render(_crop, _active);
        AnnotationPreview.Source = ToImage(rendered); PositionElement(AnnotationPreview, _selection);
        UndoButton.IsEnabled = _document.CanUndo; RedoButton.IsEnabled = _document.CanRedo;
        SizeText.Text = $"{_crop.Width} × {_crop.Height}";
        SetStatus("");
    }

    private void ToolClick(object sender, RoutedEventArgs e)
    {
        e.Handled = true;
        if (_finishing) return;
        var action = (sender as Button)?.Tag?.ToString();
        if (action == "cancel") { Close(); return; }
        CommitNumbers();
        CommitText();
        if (action == "more")
        {
            MorePanel.Visibility = MorePanel.Visibility == Visibility.Visible ? Visibility.Collapsed : Visibility.Visible;
            UpdateToolOptions(); return;
        }
        MorePanel.Visibility = Visibility.Collapsed;
        if (Enum.TryParse<AnnotationKind>(action, out var mode))
        {
            _mode = mode;
            UpdateToolOptions();
            Cursor = mode is AnnotationKind.Text or AnnotationKind.Watermark ? System.Windows.Input.Cursors.IBeam : System.Windows.Input.Cursors.Cross;
            SetStatus(mode == AnnotationKind.Label ? "点击添加序号 · 可输入说明" : "");
            Focus(); return;
        }
        try
        {
            switch (action)
            {
                case "undo": _document.Undo(); RefreshPreview(); break;
                case "redo": _document.Redo(); RefreshPreview(); break;
                case "confirm": Complete(false); break;
                case "pin": Complete(true); break;
                case "save": SaveSelection(); break;
                case "scroll": StartScroll(); break;
            }
        }
        catch (Exception ex) { _finishing = false; SetStatus("操作失败：" + ex.Message); }
    }
    private void Complete(bool pin)
    {
        if (_crop == null || _finishing) return;
        _finishing = true;
        using var image = _document.Render(_crop);
        if (pin) new PinWindow((Bitmap)image.Clone()).Show(); else ClipboardService.SetImage(image);
        Close();
    }
    private void SaveSelection()
    {
        if (_crop == null) return;
        var dialog = new Microsoft.Win32.SaveFileDialog { Filter = "PNG 图片|*.png|JPEG 图片|*.jpg", FileName = $"截图_{DateTime.Now:yyyyMMdd_HHmmss}", AddExtension = true, DefaultExt = ".png" };
        if (dialog.ShowDialog(this) != true) return;
        using var image = _document.Render(_crop);
        image.Save(dialog.FileName, dialog.FilterIndex == 2 ? ImageFormat.Jpeg : ImageFormat.Png); SetStatus("已保存");
    }
    private void StartScroll()
    {
        if (_crop == null) return;
        if (_document.CanUndo) { SetStatus("长截图请先撤销标注"); return; }
        var bounds = CropBounds(); bounds.Offset(_physicalBounds.Left, _physicalBounds.Top);
        var scroll = new ScrollCaptureWindow(bounds, (Bitmap)_crop.Clone()); scroll.Show(); Close();
    }
    private void ShowToolbar()
    {
        Toolbar.Visibility = Visibility.Visible; Toolbar.UpdateLayout();
        // Never wrap the icon row; scale only on exceptionally narrow desktops.
        Toolbar.LayoutTransform = new ScaleTransform(Math.Min(1, Math.Max(0.3, (ActualWidth - 16) / Toolbar.DesiredSize.Width)), Math.Min(1, Math.Max(0.3, (ActualWidth - 16) / Toolbar.DesiredSize.Width)));
        Toolbar.UpdateLayout();
        Canvas.SetLeft(Toolbar, Math.Clamp(_selection.Right - Toolbar.ActualWidth, 8, Math.Max(8, ActualWidth - Toolbar.ActualWidth - 8)));
        var below = _selection.Bottom + 8;
        Canvas.SetTop(Toolbar, below + Toolbar.ActualHeight <= ActualHeight ? below : Math.Max(8, _selection.Top - Toolbar.ActualHeight - 8));
        UpdateToolOptions();
    }

    private void PositionAuxiliaryPanels()
    {
        if (Toolbar.Visibility != Visibility.Visible || ActualWidth <= 0) return;
        var button = _mode is AnnotationKind.MosaicBrush ? MosaicButton : ToolsPanel.Children.OfType<Button>().FirstOrDefault(b => b.Tag.ToString() == _mode?.ToString()) ?? MoreButton;
        var anchor = button.TranslatePoint(new Point(button.ActualWidth / 2, 0), Overlay).X;
        void Place(Border panel, double center)
        {
            if (panel.Visibility != Visibility.Visible) return;
            panel.UpdateLayout();
            Canvas.SetLeft(panel, Math.Clamp(center - panel.ActualWidth / 2, 8, Math.Max(8, ActualWidth - panel.ActualWidth - 8)));
            var below = Canvas.GetTop(Toolbar) + Toolbar.ActualHeight + 6;
            Canvas.SetTop(panel, below + panel.ActualHeight <= ActualHeight - 8 ? below : Math.Max(8, Canvas.GetTop(Toolbar) - panel.ActualHeight - 6));
        }
        Place(OptionsBar, anchor);
        Place(MorePanel, MoreButton.TranslatePoint(new Point(MoreButton.ActualWidth / 2, 0), Overlay).X);
        if (StatusHint.Visibility == Visibility.Visible)
        {
            StatusHint.UpdateLayout();
            Canvas.SetLeft(StatusHint, Math.Clamp(Canvas.GetLeft(Toolbar), 8, Math.Max(8, ActualWidth - StatusHint.ActualWidth - 8)));
            var active = MorePanel.Visibility == Visibility.Visible ? MorePanel : OptionsBar;
            var top = Canvas.GetTop(Toolbar) + Toolbar.ActualHeight + 6;
            if (active.Visibility == Visibility.Visible) top = Math.Max(top, Canvas.GetTop(active) + active.ActualHeight + 6);
            Canvas.SetTop(StatusHint, top + StatusHint.ActualHeight <= ActualHeight - 8 ? top : Math.Max(8, Canvas.GetTop(Toolbar) - StatusHint.ActualHeight - 6 - (active.Visibility == Visibility.Visible ? active.ActualHeight + 6 : 0)));
        }
    }
    private void UpdateDimmedArea()
    {
        var width = ActualWidth; var height = ActualHeight;
        if (width <= 0 || height <= 0) return;
        if (_selection.Width <= 0 || _selection.Height <= 0)
        {
            SizeBadge.Visibility = Visibility.Collapsed;
            PositionElement(DimTop, new Rect(0, 0, width, height));
            foreach (var element in new[] { DimLeft, DimRight, DimBottom }) PositionElement(element, new Rect(0, 0, 0, 0));
            return;
        }
        SizeBadge.Visibility = Visibility.Visible;
        SizeText.Text = $"{Math.Round(_selection.Width * _scaleX)} × {Math.Round(_selection.Height * _scaleY)}";
        SizeBadge.UpdateLayout();
        Canvas.SetLeft(SizeBadge, Math.Clamp(_selection.Left, 4, Math.Max(4, width - SizeBadge.ActualWidth - 4)));
        Canvas.SetTop(SizeBadge, Math.Max(4, _selection.Top - SizeBadge.ActualHeight - 5));
        PositionElement(DimTop, new Rect(0, 0, width, _selection.Top));
        PositionElement(DimLeft, new Rect(0, _selection.Top, _selection.Left, _selection.Height));
        PositionElement(DimRight, new Rect(_selection.Right, _selection.Top, Math.Max(0, width - _selection.Right), _selection.Height));
        PositionElement(DimBottom, new Rect(0, _selection.Bottom, width, Math.Max(0, height - _selection.Bottom)));
    }
    private static void PositionElement(FrameworkElement element, Rect rect)
    { Canvas.SetLeft(element, rect.Left); Canvas.SetTop(element, rect.Top); element.Width = rect.Width; element.Height = rect.Height; }
    private void HandleKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (TextEditor.IsKeyboardFocusWithin || FontValue.IsKeyboardFocusWithin || OpacityValue.IsKeyboardFocusWithin || _finishing) return;
        string? action = null;
        if (e.Key == Key.Escape) action = "cancel";
        else if ((Keyboard.Modifiers & ModifierKeys.Control) != 0) action = e.Key switch { Key.Z => "undo", Key.Y => "redo", Key.S => "save", _ => null };
        else if (e.Key == Key.Enter) action = "confirm";
        if (action == null) return;
        ToolClick(new Button { Tag = action }, new RoutedEventArgs(Button.ClickEvent)); e.Handled = true;
    }
    protected override void OnClosed(EventArgs e)
    { _finishing = true; ReleaseMouseCapture(); _crop?.Dispose(); _screen.Dispose(); base.OnClosed(e); }
}
