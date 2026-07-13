using System.Drawing; using System.Drawing.Imaging; using System.IO; using System.Windows.Media.Imaging; using Clipboard=System.Windows.Clipboard;
namespace ScreenshotTool;
internal static class ClipboardService { public static void SetImage(Bitmap bitmap) { using var stream=new MemoryStream(); bitmap.Save(stream,ImageFormat.Png); stream.Position=0; var image=new BitmapImage(); image.BeginInit(); image.CacheOption=BitmapCacheOption.OnLoad; image.StreamSource=stream; image.EndInit(); image.Freeze(); Clipboard.SetImage(image); } }
