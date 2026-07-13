using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;

namespace ScreenshotTool;

public partial class SelectionWindow : Window
{
    private enum ToolMode { Select, Rectangle, Pen }

    private readonly Bitmap _screen;
    private readonly List<System.Windows.Rect> _rectangles = [];
    private readonly List<List<System.Windows.Point>> _penStrokes = [];
    private System.Windows.Point _start;
    private System.Windows.Rect _selection;
    private ToolMode _mode = ToolMode.Select;
    private bool _dragging;
    private System.Windows.Shapes.Rectangle? _activeRectangle;
    private Polyline? _activeStroke;
    private List<System.Windows.Point>? _activeStrokePoints;
    private double _pixelScaleX = 1;
    private double _pixelScaleY = 1;

    public SelectionWindow(Bitmap screen, System.Drawing.Rectangle physicalBounds)
    {
        InitializeComponent();
        _screen = screen;

        Left = SystemParameters.VirtualScreenLeft;
        Top = SystemParameters.VirtualScreenTop;
        Width = SystemParameters.VirtualScreenWidth;
        Height = SystemParameters.VirtualScreenHeight;

        Loaded += (_, _) =>
        {
            Overlay.Width = ActualWidth;
            Overlay.Height = ActualHeight;
            ScreenImage.Width = ActualWidth;
            ScreenImage.Height = ActualHeight;
            UpdateDimmedArea();
            _pixelScaleX = _screen.Width / ActualWidth;
            _pixelScaleY = _screen.Height / ActualHeight;
            Activate();
            Focus();
        };

        using var stream = new MemoryStream();
        screen.Save(stream, ImageFormat.Png);
        stream.Position = 0;
        var image = new BitmapImage();
        image.BeginInit();
        image.CacheOption = BitmapCacheOption.OnLoad;
        image.StreamSource = stream;
        image.EndInit();
        image.Freeze();
        ScreenImage.Source = image;

        MouseLeftButtonDown += HandleMouseDown;
        MouseMove += HandleMouseMove;
        MouseLeftButtonUp += HandleMouseUp;
        KeyDown += HandleKeyDown;
    }

    private void HandleMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (Toolbar.IsMouseOver) return;
        var point = e.GetPosition(Overlay);

        if (_mode != ToolMode.Select && !_selection.Contains(point)) return;

        _start = point;
        _dragging = true;
        CaptureMouse();

        if (_mode == ToolMode.Select)
        {
            Toolbar.Visibility = Visibility.Collapsed;
            AnnotationLayer.Children.Clear();
            _rectangles.Clear();
            _penStrokes.Clear();
            _selection = System.Windows.Rect.Empty;
            UpdateDimmedArea();
            SelectionBorder.Visibility = Visibility.Visible;
        }
        else if (_mode == ToolMode.Rectangle)
        {
            _activeRectangle = new System.Windows.Shapes.Rectangle
            {
                Stroke = System.Windows.Media.Brushes.Red,
                StrokeThickness = 3,
                Fill = System.Windows.Media.Brushes.Transparent
            };
            AnnotationLayer.Children.Add(_activeRectangle);
        }
        else
        {
            _activeStrokePoints = [point];
            _activeStroke = new Polyline
            {
                Stroke = System.Windows.Media.Brushes.Red,
                StrokeThickness = 3,
                StrokeLineJoin = PenLineJoin.Round
            };
            _activeStroke.Points.Add(point);
            AnnotationLayer.Children.Add(_activeStroke);
        }

        e.Handled = true;
    }

    private void HandleMouseMove(object sender, System.Windows.Input.MouseEventArgs e)
    {
        if (!_dragging) return;
        var point = e.GetPosition(Overlay);

        if (_mode == ToolMode.Pen && _activeStroke != null && _activeStrokePoints != null)
        {
            point = ClampToSelection(point);
            _activeStroke.Points.Add(point);
            _activeStrokePoints.Add(point);
            return;
        }

        var rect = CreateRect(_start, point);
        if (_mode != ToolMode.Select) rect.Intersect(_selection);

        if (_mode == ToolMode.Select)
        {
            _selection = rect;
            PositionElement(SelectionBorder, rect);
            UpdateDimmedArea();
        }
        else if (_activeRectangle != null)
        {
            PositionElement(_activeRectangle, rect);
        }
    }

    private void HandleMouseUp(object sender, MouseButtonEventArgs e)
    {
        if (!_dragging) return;
        _dragging = false;
        ReleaseMouseCapture();

        if (_mode == ToolMode.Select)
        {
            if (_selection.Width < 3 || _selection.Height < 3) { Close(); return; }
            ShowToolbar();
            Cursor = System.Windows.Input.Cursors.Arrow;
        }
        else if (_mode == ToolMode.Rectangle && _activeRectangle != null)
        {
            var rect = new System.Windows.Rect(Canvas.GetLeft(_activeRectangle), Canvas.GetTop(_activeRectangle), _activeRectangle.Width, _activeRectangle.Height);
            if (rect.Width > 2 && rect.Height > 2) _rectangles.Add(rect);
            else AnnotationLayer.Children.Remove(_activeRectangle);
            _activeRectangle = null;
        }
        else if (_mode == ToolMode.Pen && _activeStrokePoints != null)
        {
            if (_activeStrokePoints.Count > 1) _penStrokes.Add(_activeStrokePoints);
            _activeStroke = null;
            _activeStrokePoints = null;
        }

        e.Handled = true;
    }

    private void ToolClick(object sender, RoutedEventArgs e)
    {
        e.Handled = true;
        var action = (sender as System.Windows.Controls.Button)?.Tag?.ToString();
        if (action == "cancel") { Close(); return; }
        if (action == "confirm") { ConfirmSelection(); return; }
        if (action == "pin") { PinSelection(); return; }
        _mode = action == "pen" ? ToolMode.Pen : ToolMode.Rectangle;
        Cursor = action == "pen" ? System.Windows.Input.Cursors.Pen : System.Windows.Input.Cursors.Cross;
    }

    private void ActionButtonMouseUp(object sender, MouseButtonEventArgs e)
    {
        e.Handled = true;
        var action = (sender as System.Windows.Controls.Button)?.Tag?.ToString();
        if (action == "cancel") Close();
        else if (action == "confirm") ConfirmSelection();
    }

    private Bitmap CreateRenderedSelection()
    {
        var source = new System.Drawing.Rectangle(
            (int)Math.Round(_selection.Left * _pixelScaleX),
            (int)Math.Round(_selection.Top * _pixelScaleY),
            Math.Max(1, (int)Math.Round(_selection.Width * _pixelScaleX)),
            Math.Max(1, (int)Math.Round(_selection.Height * _pixelScaleY)));
        source.Intersect(new System.Drawing.Rectangle(0, 0, _screen.Width, _screen.Height));

        var crop = new Bitmap(source.Width, source.Height, System.Drawing.Imaging.PixelFormat.Format32bppPArgb);
        using var graphics = Graphics.FromImage(crop);
        graphics.SmoothingMode = SmoothingMode.AntiAlias;
        graphics.DrawImage(_screen, new System.Drawing.Rectangle(0, 0, crop.Width, crop.Height), source, GraphicsUnit.Pixel);

        using var pen = new System.Drawing.Pen(System.Drawing.Color.Red, Math.Max(3, (float)(3 * _pixelScaleX)))
        {
            LineJoin = System.Drawing.Drawing2D.LineJoin.Round,
            StartCap = LineCap.Round,
            EndCap = LineCap.Round
        };

        foreach (var rect in _rectangles)
        {
            graphics.DrawRectangle(pen,
                (float)((rect.Left - _selection.Left) * _pixelScaleX),
                (float)((rect.Top - _selection.Top) * _pixelScaleY),
                (float)(rect.Width * _pixelScaleX),
                (float)(rect.Height * _pixelScaleY));
        }

        foreach (var stroke in _penStrokes)
        {
            if (stroke.Count < 2) continue;
            var points = stroke.Select(point => new PointF(
                (float)((point.X - _selection.Left) * _pixelScaleX),
                (float)((point.Y - _selection.Top) * _pixelScaleY))).ToArray();
            graphics.DrawLines(pen, points);
        }

        return crop;
    }

    private void CopySelection()
    {
        using var crop = CreateRenderedSelection();
        ClipboardService.SetImage(crop);
    }

    private void ConfirmSelection()
    {
        CopySelection();
        DialogResult = true;
        Close();
    }

    private void PinSelection()
    {
        var card = new PinWindow(CreateRenderedSelection());
        card.Show();
        Close();
    }

    private void ShowToolbar()
    {
        Toolbar.Visibility = Visibility.Visible;
        Toolbar.UpdateLayout();
        var left = Math.Clamp(_selection.Right - Toolbar.ActualWidth, 8, Math.Max(8, ActualWidth - Toolbar.ActualWidth - 8));
        var below = _selection.Bottom + 8;
        var top = below + Toolbar.ActualHeight <= ActualHeight ? below : Math.Max(8, _selection.Top - Toolbar.ActualHeight - 8);
        Canvas.SetLeft(Toolbar, left);
        Canvas.SetTop(Toolbar, top);
    }

    private void UpdateDimmedArea()
    {
        var width = ActualWidth;
        var height = ActualHeight;
        if (width <= 0 || height <= 0) return;

        var selection = _selection;
        if (selection.IsEmpty || selection.Width <= 0 || selection.Height <= 0)
        {
            PositionElement(DimTop, new System.Windows.Rect(0, 0, width, height));
            PositionElement(DimLeft, new System.Windows.Rect(0, 0, 0, 0));
            PositionElement(DimRight, new System.Windows.Rect(0, 0, 0, 0));
            PositionElement(DimBottom, new System.Windows.Rect(0, 0, 0, 0));
            return;
        }
        PositionElement(DimTop, new System.Windows.Rect(0, 0, width, Math.Max(0, selection.Top)));
        PositionElement(DimLeft, new System.Windows.Rect(0, selection.Top, Math.Max(0, selection.Left), Math.Max(0, selection.Height)));
        PositionElement(DimRight, new System.Windows.Rect(selection.Right, selection.Top, Math.Max(0, width - selection.Right), Math.Max(0, selection.Height)));
        PositionElement(DimBottom, new System.Windows.Rect(0, selection.Bottom, width, Math.Max(0, height - selection.Bottom)));
    }

    private System.Windows.Point ClampToSelection(System.Windows.Point point) => new(
        Math.Clamp(point.X, _selection.Left, _selection.Right),
        Math.Clamp(point.Y, _selection.Top, _selection.Bottom));

    private static System.Windows.Rect CreateRect(System.Windows.Point first, System.Windows.Point second) => new(
        Math.Min(first.X, second.X),
        Math.Min(first.Y, second.Y),
        Math.Abs(second.X - first.X),
        Math.Abs(second.Y - first.Y));

    private static void PositionElement(FrameworkElement element, System.Windows.Rect rect)
    {
        Canvas.SetLeft(element, rect.Left);
        Canvas.SetTop(element, rect.Top);
        element.Width = rect.Width;
        element.Height = rect.Height;
    }

    private void HandleKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key == Key.Escape) Close();
        else if (e.Key == Key.Enter && _selection.Width > 2) ConfirmSelection();
    }

    protected override void OnClosed(EventArgs e)
    {
        _screen.Dispose();
        base.OnClosed(e);
    }
}
