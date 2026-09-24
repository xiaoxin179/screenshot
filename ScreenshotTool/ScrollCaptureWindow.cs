using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using Button = System.Windows.Controls.Button;
using TextBlock = System.Windows.Controls.TextBlock;
using Image = System.Windows.Controls.Image;
using PixelFormat = System.Drawing.Imaging.PixelFormat;
using Orientation = System.Windows.Controls.Orientation;

namespace ScreenshotTool;

public sealed class ScrollCaptureWindow : Window
{
    private readonly Rectangle _bounds;
    private readonly ScrollStitcher _stitcher;
    private readonly TextBlock _status = new() { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 8, 0, 8) };
    private readonly Image _preview = new() { Stretch = Stretch.Uniform };
    private readonly Button _capture = new() { Content = "采集下一帧 (F8)", Padding = new Thickness(10, 6, 10, 6) };
    private readonly Button _finish = new() { Content = "结束采集 (F9)", Padding = new Thickness(10, 6, 10, 6) };
    private readonly StackPanel _actions = new() { Orientation = Orientation.Horizontal };
    private HwndSource? _source;
    private bool _busy, _closed, _finished;
    private bool _f8, _f9;
    [DllImport("user32.dll")] private static extern bool RegisterHotKey(IntPtr hwnd, int id, uint mods, uint key);
    [DllImport("user32.dll")] private static extern bool UnregisterHotKey(IntPtr hwnd, int id);
    [DllImport("dwmapi.dll")] private static extern int DwmFlush();

    public ScrollCaptureWindow(Rectangle bounds, Bitmap first)
    {
        _bounds = bounds;
        try { _stitcher = new ScrollStitcher(first); } finally { first.Dispose(); }
        Title = "Snaply · 滚动长截图"; Width = 510; Height = 290; Topmost = true;
        Background = new SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#FEFDFC"));
        var root = new DockPanel { Margin = new Thickness(18) }; Content = root;
        var header = new StackPanel(); DockPanel.SetDock(header, Dock.Top); root.Children.Add(header);
        header.Children.Add(new TextBlock { Text = "向下滚动，每次不超过半屏，再按 F8。", FontSize = 17, FontWeight = FontWeights.SemiBold });
        header.Children.Add(new TextBlock { Text = "选区应只包含滚动内容，避开固定导航栏及动画。采集时面板会暂时隐藏。", TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 6, 0, 0) });
        header.Children.Add(_status);
        header.Children.Add(_actions); _actions.Children.Add(_capture); _actions.Children.Add(_finish);
        var cancel = new Button { Content = "取消", Padding = new Thickness(10, 6, 10, 6) }; cancel.Click += (_, _) => Close(); _actions.Children.Add(cancel);
        _capture.Click += async (_, _) => await CaptureNext(); _finish.Click += (_, _) => Finish();
        root.Children.Add(new ScrollViewer { Content = _preview, VerticalScrollBarVisibility = ScrollBarVisibility.Auto });
        _status.Text = $"起始帧 {_bounds.Width} × {_bounds.Height}；F9 结束后预览、保存或复制。";
        SourceInitialized += (_, _) =>
        {
            _source = HwndSource.FromHwnd(new WindowInteropHelper(this).Handle)!; _source.AddHook(Hook);
            _f8 = RegisterHotKey(_source.Handle, 81, 0x4000, 0x77);
            _f9 = RegisterHotKey(_source.Handle, 82, 0x4000, 0x78);
            if (!_f8 || !_f9) _status.Text += " F8/F9 部分被占用，请使用面板按钮。";
        };
    }
    private IntPtr Hook(IntPtr hwnd, int message, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (message == 0x0312)
        {
            if (wParam.ToInt32() == 81) { Dispatcher.InvokeAsync(async () => await CaptureNext()); handled = true; }
            if (wParam.ToInt32() == 82) { Dispatcher.InvokeAsync(Finish); handled = true; }
        }
        return IntPtr.Zero;
    }
    private async Task CaptureNext()
    {
        if (_busy || _closed || _finished) return;
        _busy = true; _capture.IsEnabled = _finish.IsEnabled = false;
        Hide();
        try
        {
            await Task.Delay(220);
            if (_closed) return;
            DwmFlush();
            using var next = new Bitmap(_bounds.Width, _bounds.Height, PixelFormat.Format32bppPArgb);
            using (var g = Graphics.FromImage(next)) g.CopyFromScreen(_bounds.Location, System.Drawing.Point.Empty, _bounds.Size);
            _stitcher.Append(next, out var status); _status.Text = status;
        }
        catch (Exception ex) { _status.Text = "采集失败：" + ex.Message; }
        finally
        {
            _busy = false;
            if (!_closed) { Show(); _capture.IsEnabled = _finish.IsEnabled = true; }
        }
    }
    private void Finish()
    {
        if (_busy || _closed || _finished) return;
        _finished = true; ReleaseHotkeys();
        using var image = _stitcher.Snapshot(); _preview.Source = SelectionWindow.ToImage(image);
        Height = Math.Min(750, SystemParameters.WorkArea.Height - 40);
        _status.Text = $"长图预览：{image.Width} × {image.Height}，共 {_stitcher.Frames} 帧。";
        _actions.Children.Clear();
        AddAction("保存 PNG / JPG", () =>
        {
            var dialog = new Microsoft.Win32.SaveFileDialog { Filter = "PNG 图片|*.png|JPEG 图片|*.jpg", FileName = $"长截图_{DateTime.Now:yyyyMMdd_HHmmss}", DefaultExt = ".png" };
            if (dialog.ShowDialog(this) != true) return;
            using var bitmap = _stitcher.Snapshot(); bitmap.Save(dialog.FileName, dialog.FilterIndex == 2 ? ImageFormat.Jpeg : ImageFormat.Png); _status.Text = "已保存长截图";
        });
        AddAction("复制并关闭", () => { using var bitmap = _stitcher.Snapshot(); ClipboardService.SetImage(bitmap); Close(); });
        AddAction("贴图", () => { new PinWindow(_stitcher.Snapshot()).Show(); Close(); });
        AddAction("关闭", Close);
    }
    private void AddAction(string title, Action action)
    {
        var button = new Button { Content = title, Padding = new Thickness(10, 6, 10, 6), Margin = new Thickness(0, 0, 5, 0) };
        button.Click += (_, _) => { try { action(); } catch (Exception ex) { _status.Text = "操作失败：" + ex.Message; } };
        _actions.Children.Add(button);
    }
    private void ReleaseHotkeys()
    {
        if (_source == null) return;
        if (_f8) UnregisterHotKey(_source.Handle, 81);
        if (_f9) UnregisterHotKey(_source.Handle, 82);
        _f8 = _f9 = false;
    }
    protected override void OnClosed(EventArgs e)
    { _closed = true; ReleaseHotkeys(); _stitcher.Dispose(); base.OnClosed(e); }
}
