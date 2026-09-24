using System.Drawing;
using System.Drawing.Imaging;
using ScreenshotTool;

internal static class Program
{
    private static int _checks;
    [STAThread]
    private static int Main()
    {
        try
        {
            using var plain = new Bitmap(400, 300);
            using (var g = Graphics.FromImage(plain)) g.Clear(Color.White);
            foreach (var kind in Enum.GetValues<AnnotationKind>())
            {
                using var source = (Bitmap)plain.Clone();
                if (kind is AnnotationKind.MosaicRectangle or AnnotationKind.MosaicBrush)
                    for (var y = 0; y < source.Height; y++) for (var x = 0; x < source.Width; x++) source.SetPixel(x, y, (x + y) % 2 == 0 ? Color.Black : Color.White);
                var doc = new AnnotationDocument();
                var item = new Annotation { Kind = kind, Color = Color.Red, Width = 5, FontSize = 24, Text = "中文测试 Test", Opacity = .3f };
                item.Points.Add(new PointF(30, 40)); item.Points.Add(new PointF(180, 160));
                doc.Add(item);
                using var output = doc.Render(source);
                Check(Different(source, output), kind + " changes pixels");
                Check(output.Size == source.Size, kind + " preserves crop dimensions");
                doc.Undo(); using var undone = doc.Render(source); Check(!Different(source, undone), kind + " undo restores exact original");
                doc.Redo(); using var redone = doc.Render(source); Check(!Different(output, redone), kind + " redo exact match");
                doc.Undo(); doc.Add(item); Check(!doc.CanRedo, "New stroke invalidates redo");
            }
            var circle = AnnotationDocument.Constrain(new(20, 30), new(120, 70), AnnotationKind.Ellipse, new Size(400, 300));
            Check(circle.X - 20 == circle.Y - 30, "Shift circle keeps equal sides");
            var line = AnnotationDocument.Constrain(new(20, 30), new(120, 70), AnnotationKind.Line, new Size(400, 300));
            Check(line.Y == 30, "Shift line horizontal constraint");
            foreach (var kind in new[] { AnnotationKind.Pen, AnnotationKind.Highlight, AnnotationKind.MosaicBrush })
            {
                var doc = new AnnotationDocument(); var dot = new Annotation { Kind = kind }; dot.Points.Add(new(50, 50)); doc.Add(dot);
                using var output = doc.Render(plain); Check(output.Width == 400, kind + " single click safe");
            }
            using var page = new Bitmap(160, 800);
            var random = new Random(72);
            for (var y = 0; y < page.Height; y++) for (var x = 0; x < page.Width; x++) page.SetPixel(x, y, Color.FromArgb(random.Next(256), random.Next(256), random.Next(256)));
            using var first = page.Clone(new Rectangle(0, 0, 160, 320), PixelFormat.Format32bppPArgb);
            using var second = page.Clone(new Rectangle(0, 120, 160, 320), PixelFormat.Format32bppPArgb);
            using var third = page.Clone(new Rectangle(0, 240, 160, 320), PixelFormat.Format32bppPArgb);
            Check(ScrollStitcher.FindShift(first, second) == 120, "Detect exact scroll offset");
            Check(ScrollStitcher.FindShift(first, first) == 0, "Reject duplicate frame");
            Check(ScrollStitcher.FindShift(second, first) == -1, "Reject upward scroll");
            using var stitch = new ScrollStitcher(first);
            Check(stitch.Append(second, out _), "Append second frame");
            Check(stitch.Append(third, out _), "Append third frame");
            using var result = stitch.Snapshot();
            using var expected = page.Clone(new Rectangle(0, 0, 160, 560), PixelFormat.Format32bppPArgb);
            Check(!Different(result, expected), "Stitched output is pixel-exact across seams");
            Check(!stitch.Append(third, out _), "Duplicate does not grow result");
            using var unrelated = page.Clone(new Rectangle(0, 470, 160, 320), PixelFormat.Format32bppPArgb);
            Check(ScrollStitcher.FindShift(first, unrelated) == -1, "Reject unrelated frames");
            using var stream = new System.IO.MemoryStream(); result.Save(stream, ImageFormat.Png); stream.Position = 0;
            using var png = new Bitmap(stream); Check(!Different(result, png), "PNG roundtrip exact");
            using var jpgStream = new System.IO.MemoryStream(); result.Save(jpgStream, ImageFormat.Jpeg); jpgStream.Position = 0;
            using var jpg = new Bitmap(jpgStream); Check(jpg.Size == result.Size, "JPEG roundtrip dimensions");
            var app = new App { ShutdownMode = System.Windows.ShutdownMode.OnExplicitShutdown };
            var selection = new SelectionWindow((Bitmap)plain.Clone(), new Rectangle(0, 0, 400, 300));
            selection.Show();
            Check(selection.IsVisible, "Selection window constructs and shows");
            const System.Reflection.BindingFlags flags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
            var kindField = typeof(SelectionWindow).GetField("_mode", flags)!;
            var docField = typeof(SelectionWindow).GetField("_document", flags)!;
            var cropField = typeof(SelectionWindow).GetField("_crop", flags)!;
            cropField.SetValue(selection, (Bitmap)plain.Clone());
            typeof(SelectionWindow).GetField("_selection", flags)!.SetValue(selection, new System.Windows.Rect(0, 0, 400, 300));
            var toolClick = typeof(SelectionWindow).GetMethod("ToolClick", flags)!;
            foreach (var kind in Enum.GetValues<AnnotationKind>())
            {
                toolClick.Invoke(selection, [new System.Windows.Controls.Button { Tag = kind.ToString() }, new System.Windows.RoutedEventArgs(System.Windows.Controls.Button.ClickEvent)]);
                Check((AnnotationKind)kindField.GetValue(selection)! == kind, "Toolbar dispatch " + kind);
            }
            var editor = (System.Windows.Controls.TextBox)selection.FindName("TextEditor");
            editor.Width = 240; editor.Text = "中文输入测试\nSecond line";
            var text = new Annotation { Kind = AnnotationKind.Text }; text.Points.Add(new PointF(10, 10));
            typeof(SelectionWindow).GetField("_textAnnotation", flags)!.SetValue(selection, text);
            typeof(SelectionWindow).GetMethod("CommitText", flags)!.Invoke(selection, null);
            var windowDoc = (AnnotationDocument)docField.GetValue(selection)!;
            Check(windowDoc.Items.Last().Text == editor.Text, "Window commits multiline Chinese text");
            Check(editor.Visibility == System.Windows.Visibility.Collapsed, "Commit hides text editor");
            toolClick.Invoke(selection, [new System.Windows.Controls.Button { Tag = "undo" }, new System.Windows.RoutedEventArgs(System.Windows.Controls.Button.ClickEvent)]);
            Check(!windowDoc.CanUndo && windowDoc.CanRedo, "Toolbar undo dispatch");
            toolClick.Invoke(selection, [new System.Windows.Controls.Button { Tag = "redo" }, new System.Windows.RoutedEventArgs(System.Windows.Controls.Button.ClickEvent)]);
            Check(windowDoc.CanUndo && !windowDoc.CanRedo, "Toolbar redo dispatch");
            var keyEvent = new System.Windows.Input.KeyEventArgs(System.Windows.Input.Keyboard.PrimaryDevice,
                System.Windows.PresentationSource.FromVisual(selection), 0, System.Windows.Input.Key.Escape)
                { RoutedEvent = System.Windows.Input.Keyboard.KeyDownEvent };
            typeof(SelectionWindow).GetMethod("HandleKeyDown", flags)!.Invoke(selection, [selection, keyEvent]);
            Check(!selection.IsVisible && keyEvent.Handled, "Escape routes cancel without exception");
            var scrollWindow = new ScrollCaptureWindow(new Rectangle(0, 0, 160, 320), (Bitmap)first.Clone());
            scrollWindow.Show();
            typeof(ScrollCaptureWindow).GetMethod("Finish", flags)!.Invoke(scrollWindow, null);
            Check((bool)typeof(ScrollCaptureWindow).GetField("_finished", flags)!.GetValue(scrollWindow)!, "Scroll capture opens result preview");
            Check(!(bool)typeof(ScrollCaptureWindow).GetField("_f8", flags)!.GetValue(scrollWindow)!, "Scroll preview releases capture hotkey");
            scrollWindow.Close();
            app.Shutdown();
            Console.WriteLine($"All {_checks} checks passed."); return 0;
        }
        catch (Exception ex) { Console.Error.WriteLine(ex); return 1; }
    }
    private static bool Different(Bitmap a, Bitmap b)
    {
        if (a.Size != b.Size) return true;
        for (var y = 0; y < a.Height; y++) for (var x = 0; x < a.Width; x++)
            if (a.GetPixel(x, y).ToArgb() != b.GetPixel(x, y).ToArgb()) return true;
        return false;
    }
    private static void Check(bool condition, string label)
    { if (!condition) throw new Exception("FAIL: " + label); _checks++; Console.WriteLine("PASS: " + label); }
}
