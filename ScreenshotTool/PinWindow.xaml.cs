using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media.Imaging;

namespace ScreenshotTool;

public partial class PinWindow : Window
{
    private const double MinImageWidth = 120;
    private const double MaxImageWidth = 1200;
    private readonly double _aspectRatio;

    public PinWindow(Bitmap image)
    {
        InitializeComponent();
        Left = SystemParameters.WorkArea.Right - Math.Min(image.Width, 420) - 24;
        Top = SystemParameters.WorkArea.Bottom - Math.Min(image.Height, 320) - 72;

        _aspectRatio = image.Width / (double)image.Height;
        var initialWidth = Math.Min(image.Width, 420);
        PinnedImage.Width = initialWidth;
        PinnedImage.Height = initialWidth / _aspectRatio;

        using var stream = new MemoryStream();
        image.Save(stream, ImageFormat.Png);
        stream.Position = 0;
        var bitmap = new BitmapImage();
        bitmap.BeginInit();
        bitmap.CacheOption = BitmapCacheOption.OnLoad;
        bitmap.StreamSource = stream;
        bitmap.EndInit();
        bitmap.Freeze();
        PinnedImage.Source = bitmap;
        image.Dispose();
    }

    private void CardMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.OriginalSource is System.Windows.Controls.Button) return;
        if (e.LeftButton == MouseButtonState.Pressed) DragMove();
    }

    private void CloseClick(object sender, RoutedEventArgs e) => Close();

    private void PinWindowMouseWheel(object sender, MouseWheelEventArgs e)
    {
        var factor = e.Delta > 0 ? 1.1 : 1 / 1.1;
        var width = Math.Clamp(PinnedImage.Width * factor, MinImageWidth, MaxImageWidth);
        PinnedImage.Width = width;
        PinnedImage.Height = width / _aspectRatio;
        e.Handled = true;
    }
}
