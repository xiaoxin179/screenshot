using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;

namespace ScreenshotTool;

public enum AnnotationKind { Rectangle, Ellipse, Line, Arrow, Pen, Text, MosaicRectangle, MosaicBrush, Highlight, Label, Watermark }

public sealed class Annotation
{
    public AnnotationKind Kind { get; init; }
    public List<PointF> Points { get; } = [];
    public Color Color { get; init; } = Color.Red;
    public float Width { get; init; } = 3;
    public float FontSize { get; init; } = 22;
    public float Opacity { get; init; } = .3f;
    public string Text { get; set; } = "";
    public float TextWidth { get; set; }
    public int Number { get; init; } = 1;
}

public sealed class AnnotationDocument
{
    private readonly List<Annotation> _items = [];
    private readonly Stack<Annotation> _redo = new();
    public IReadOnlyList<Annotation> Items => _items;
    public bool CanUndo => _items.Count > 0;
    public bool CanRedo => _redo.Count > 0;
    public void Add(Annotation item) { _items.Add(item); _redo.Clear(); }
    public void Undo() { if (CanUndo) { _redo.Push(_items[^1]); _items.RemoveAt(_items.Count - 1); } }
    public void Redo() { if (CanRedo) _items.Add(_redo.Pop()); }
    public void Clear() { _items.Clear(); _redo.Clear(); }

    public Bitmap Render(Bitmap background, Annotation? active = null)
    {
        var result = background.Clone(new Rectangle(0, 0, background.Width, background.Height), PixelFormat.Format32bppPArgb);
        try
        {
            foreach (var item in _items) Draw(result, item);
            if (active != null) Draw(result, active);
            return result;
        }
        catch { result.Dispose(); throw; }
    }

    public static PointF Constrain(PointF start, PointF end, AnnotationKind kind, Size bounds)
    {
        if (kind is AnnotationKind.Line or AnnotationKind.Arrow)
        {
            if (Math.Abs(end.X - start.X) >= Math.Abs(end.Y - start.Y)) end.Y = start.Y;
            else end.X = start.X;
        }
        else if (kind is AnnotationKind.Ellipse or AnnotationKind.Rectangle)
        {
            var sx = end.X < start.X ? -1 : 1;
            var sy = end.Y < start.Y ? -1 : 1;
            var size = Math.Max(Math.Abs(end.X - start.X), Math.Abs(end.Y - start.Y));
            size = Math.Min(size, sx < 0 ? start.X : bounds.Width - start.X);
            size = Math.Min(size, sy < 0 ? start.Y : bounds.Height - start.Y);
            end = new PointF(start.X + sx * size, start.Y + sy * size);
        }
        return end;
    }

    private static void Draw(Bitmap target, Annotation item)
    {
        if (item.Points.Count == 0) return;
        var a = item.Points[0];
        var b = item.Points[^1];
        var rect = RectangleF.FromLTRB(Math.Min(a.X, b.X), Math.Min(a.Y, b.Y), Math.Max(a.X, b.X), Math.Max(a.Y, b.Y));
        if (item.Kind is AnnotationKind.MosaicRectangle or AnnotationKind.MosaicBrush)
        {
            Mosaic(target, item, rect);
            return;
        }
        using var g = Graphics.FromImage(target);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        using var pen = new Pen(item.Color, item.Width) { StartCap = LineCap.Round, EndCap = LineCap.Round, LineJoin = LineJoin.Round };
        using var brush = new SolidBrush(item.Color);
        switch (item.Kind)
        {
            case AnnotationKind.Rectangle:
                if (rect.Width > 0 && rect.Height > 0) g.DrawRectangle(pen, rect.X, rect.Y, rect.Width, rect.Height);
                break;
            case AnnotationKind.Ellipse:
                if (rect.Width > 0 && rect.Height > 0) g.DrawEllipse(pen, rect);
                break;
            case AnnotationKind.Line: g.DrawLine(pen, a, b); break;
            case AnnotationKind.Arrow:
                using (var cap = new AdjustableArrowCap(4, 5, true))
                { pen.CustomEndCap = cap; g.DrawLine(pen, a, b); }
                break;
            case AnnotationKind.Pen:
                if (item.Points.Count > 1) g.DrawLines(pen, item.Points.ToArray());
                else g.FillEllipse(brush, a.X - item.Width / 2, a.Y - item.Width / 2, item.Width, item.Width);
                break;
            case AnnotationKind.Highlight:
                // Fill one unioned path so overlapping segments in one stroke do not darken.
                using (var path = new GraphicsPath(FillMode.Winding))
                using (var fill = new SolidBrush(Color.FromArgb(80, item.Color)))
                {
                    var width = Math.Max(12, item.Width * 5);
                    if (item.Points.Count > 1)
                    {
                        path.AddLines(item.Points.ToArray());
                        using var wide = new Pen(Color.Black, width) { StartCap = LineCap.Round, EndCap = LineCap.Round, LineJoin = LineJoin.Round };
                        path.Widen(wide);
                    }
                    else path.AddEllipse(a.X - width / 2, a.Y - width / 2, width, width);
                    g.FillPath(fill, path);
                }
                break;
            case AnnotationKind.Text:
            case AnnotationKind.Watermark:
                using (var font = new Font("Microsoft YaHei UI", item.FontSize, FontStyle.Regular, GraphicsUnit.Pixel))
                using (var textBrush = new SolidBrush(item.Kind == AnnotationKind.Watermark ? Color.FromArgb((int)(255 * item.Opacity), item.Color) : item.Color))
                    g.DrawString(item.Text, font, textBrush, new RectangleF(a.X, a.Y, item.TextWidth > 0 ? item.TextWidth : Math.Max(1, target.Width - a.X), Math.Max(1, target.Height - a.Y)));
                break;
            case AnnotationKind.Label:
                var diameter = item.FontSize * 1.6f;
                g.FillEllipse(brush, a.X, a.Y, diameter, diameter);
                using (var font = new Font("Microsoft YaHei UI", item.FontSize, FontStyle.Bold, GraphicsUnit.Pixel))
                using (var center = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center })
                {
                    g.DrawString(item.Number.ToString(), font, Brushes.White, new RectangleF(a.X, a.Y, diameter, diameter), center);
                    if (!string.IsNullOrWhiteSpace(item.Text))
                    {
                        var size = g.MeasureString(item.Text, font);
                        g.FillRectangle(brush, a.X + diameter + 4, a.Y, size.Width + 12, Math.Max(diameter, size.Height));
                        g.DrawString(item.Text, font, Brushes.White, a.X + diameter + 10, a.Y + 2);
                    }
                }
                break;
        }
    }

    private static void Mosaic(Bitmap target, Annotation item, RectangleF rect)
    {
        using var source = (Bitmap)target.Clone();
        using var region = new Region();
        region.MakeEmpty();
        if (item.Kind == AnnotationKind.MosaicRectangle) region.Union(rect);
        else
        {
            var width = Math.Max(12, item.Width * 6);
            using var path = new GraphicsPath();
            if (item.Points.Count > 1)
            {
                path.AddLines(item.Points.ToArray());
                using var pen = new Pen(Color.Black, width) { StartCap = LineCap.Round, EndCap = LineCap.Round, LineJoin = LineJoin.Round };
                path.Widen(pen);
            }
            else path.AddEllipse(item.Points[0].X - width / 2, item.Points[0].Y - width / 2, width, width);
            region.Union(path);
        }
        using var g = Graphics.FromImage(target);
        g.SetClip(region, CombineMode.Intersect);
        var area = Rectangle.Intersect(Rectangle.Ceiling(region.GetBounds(g)), new Rectangle(0, 0, target.Width, target.Height));
        var block = Math.Max(8, (int)item.Width * 3);
        for (var y = area.Top / block * block; y < area.Bottom; y += block)
            for (var x = area.Left / block * block; x < area.Right; x += block)
            {
                var color = source.GetPixel(Math.Min(x + block / 2, target.Width - 1), Math.Min(y + block / 2, target.Height - 1));
                using var brush = new SolidBrush(color);
                g.FillRectangle(brush, x, y, block, block);
            }
    }
}
