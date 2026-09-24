using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;

namespace ScreenshotTool;

// Downward-only, integer-pixel translation matching. No OCR or learned model.
public sealed class ScrollStitcher : IDisposable
{
    private Bitmap _last;
    private Bitmap _result;
    public int Frames { get; private set; } = 1;
    public int Height => _result.Height;
    public ScrollStitcher(Bitmap first) { _last = (Bitmap)first.Clone(); _result = (Bitmap)first.Clone(); }
    public Bitmap Snapshot() => (Bitmap)_result.Clone();

    public bool Append(Bitmap next, out string message)
    {
        if (next.Size != _last.Size) { message = "画面尺寸变化，请重新开始。"; return false; }
        var shift = FindShift(_last, next);
        if (shift == 0) { message = "画面没有变化，请向下滚动后再采集。"; return false; }
        if (shift < 0) { message = "未找到可靠重叠。请往回滚一点，避开固定栏、动画和视频后重试。"; return false; }
        if (_result.Height + shift > 30000 || (long)_result.Width * (_result.Height + shift) > 40_000_000)
        { message = "已到长图大小上限，请完成并保存。"; return false; }
        var merged = new Bitmap(_result.Width, _result.Height + shift, PixelFormat.Format32bppPArgb);
        try
        {
            using var g = Graphics.FromImage(merged);
            g.DrawImageUnscaled(_result, 0, 0);
            g.DrawImage(next, new Rectangle(0, _result.Height, next.Width, shift), new Rectangle(0, next.Height - shift, next.Width, shift), GraphicsUnit.Pixel);
            var newLast = (Bitmap)next.Clone();
            _result.Dispose(); _result = merged;
            _last.Dispose(); _last = newLast;
            Frames++;
        }
        catch { merged.Dispose(); throw; }
        message = $"已拼接 {Frames} 帧，{_result.Width} × {_result.Height} 像素";
        return true;
    }

    public static int FindShift(Bitmap previous, Bitmap next)
    {
        if (previous.Size != next.Size || previous.Height < 64 || previous.Width < 32) return -1;
        var a = Samples(previous); var b = Samples(next);
        var h = previous.Height;
        double Error(int shift)
        {
            double total = 0;
            var overlap = h - shift;
            for (var row = 0; row < 32; row++)
            {
                var y = row * (overlap - 1) / 31;
                for (var x = 0; x < 32; x++) total += Math.Abs(a[(y + shift) * 32 + x] - b[y * 32 + x]);
            }
            return total / 1024;
        }
        if (Error(0) < .8) return 0;
        var scores = new double[h * 3 / 4 + 1];
        var best = double.MaxValue; var offset = -1;
        for (var shift = 1; shift < scores.Length; shift++)
        {
            scores[shift] = Error(shift);
            if (scores[shift] < best) { best = scores[shift]; offset = shift; }
        }
        if (best > 5) return -1;
        // Repeated rows / blank pages can give multiple plausible translations.
        for (var shift = 1; shift < scores.Length; shift++)
            if (Math.Abs(shift - offset) > 3 && scores[shift] <= best + 1) return -1;
        return offset;
    }

    private static byte[] Samples(Bitmap input)
    {
        using var bitmap = input.Clone(new Rectangle(0, 0, input.Width, input.Height), PixelFormat.Format32bppArgb);
        var data = bitmap.LockBits(new Rectangle(0, 0, bitmap.Width, bitmap.Height), ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
        try
        {
            var result = new byte[bitmap.Height * 32];
            var row = new byte[bitmap.Width * 4];
            for (var y = 0; y < bitmap.Height; y++)
            {
                Marshal.Copy(IntPtr.Add(data.Scan0, y * data.Stride), row, 0, row.Length);
                for (var column = 0; column < 32; column++)
                {
                    var x = (column + 1) * (bitmap.Width - 1) / 33 * 4;
                    result[y * 32 + column] = (byte)((row[x] * 29 + row[x + 1] * 150 + row[x + 2] * 77) >> 8);
                }
            }
            return result;
        }
        finally { bitmap.UnlockBits(data); }
    }
    public void Dispose() { _last.Dispose(); _result.Dispose(); }
}
