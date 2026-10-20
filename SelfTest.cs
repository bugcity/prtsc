using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Prtsc;

internal static class SelfTest
{
    public static void Run()
    {
        static void Check(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
        var rect = RegionWindow.PixelRect(new System.Windows.Point(150, 80), new System.Windows.Point(20, 10), 200, 100, 400, 200);
        Check(rect == new Int32Rect(40, 20, 260, 140), "Reverse drag must preserve pixel coordinates at 200% DPI");
        rect = RegionWindow.PixelRect(new System.Windows.Point(-40, -10), new System.Windows.Point(250, 110), 200, 100, 400, 200);
        Check(rect == new Int32Rect(0, 0, 400, 200), "Selection must clamp to image bounds");
        var desktopBounds = new System.Drawing.Rectangle(-1920, -200, 3840, 1280);
        var front = new WindowTarget(new IntPtr(1), "前面", new System.Drawing.Rectangle(-2000, -300, 800, 600));
        var back = new WindowTarget(new IntPtr(2), "背面", new System.Drawing.Rectangle(-1800, -100, 1200, 800));
        Check(WindowCapture.At([front, back], -1700, 0) == front, "Overlapping windows must select the frontmost candidate");
        Check(WindowCapture.At([front, back], -1000, 500) == back, "Exposed area of a background window must remain selectable");
        Check(WindowCapture.At([front, back], 1500, 500) is null, "Desktop background must not select a window");
        rect = WindowCapture.CropBounds(front.Bounds, desktopBounds);
        Check(rect == new Int32Rect(0, 0, 720, 500), "Window crop must clip negative desktop origins and off-screen borders");
        Check(WindowCapture.CropBounds(new System.Drawing.Rectangle(100, 50, 640, 480), desktopBounds) == new Int32Rect(2020, 250, 640, 480), "Window crop must offset screen pixels into the virtual desktop bitmap");
        Check(WindowCapture.CropBounds(new System.Drawing.Rectangle(5000, 0, 640, 480), desktopBounds).IsEmpty, "Off-screen windows must not produce an invalid crop");
        var image = BitmapSource.Create(640, 360, 96, 96, PixelFormats.Bgr32, null, Enumerable.Repeat((byte)240, 640 * 360 * 4).ToArray(), 640 * 4);
        List<Mark> marks = [];
        string[] tools = ["Arrow", "Rectangle", "Ellipse", "Pen", "Highlight", "Text", "Number"];
        for (int i = 0; i < tools.Length; i++) marks.Add(new Mark { Tool = tools[i], X = 30 + i * 75, Y = 70, X2 = 90 + i * 75, Y2 = 180, Text = tools[i] == "Number" ? "1" : "テスト", Points = [[250, 250], [300, 230], [350, 260]] });
        var rendered = AnnotationRenderer.Render(image, marks);
        Check(rendered.PixelWidth == 640 && rendered.PixelHeight == 360, "Export dimensions changed");
        byte[] before = new byte[640 * 360 * 4], after = new byte[before.Length];
        new FormatConvertedBitmap(image, PixelFormats.Pbgra32, null, 0).CopyPixels(before, 640 * 4, 0); rendered.CopyPixels(after, 640 * 4, 0);
        Check(!before.SequenceEqual(after), "Annotations must modify exported pixels");
        var root = Path.Combine(Environment.CurrentDirectory, "prtsc-test-" + Guid.NewGuid().ToString("N"));
        var store = new CaptureStore(root); var entry = new CaptureEntry { Title = "テスト", Marks = marks };
        try
        {
            store.Create(entry, image); store.Save(entry, rendered);
            var loaded = store.Load(); Check(loaded.Count == 1 && loaded[0].Marks.Count == 7, "Annotation persistence failed");
            Check(loaded[0].Marks[3].Points.Count == 3, "Pen points not restored");
            Check(store.Original(entry).PixelWidth == 640, "Original lost");
            var window = new MainWindow(store);
            var surface = (System.Windows.FrameworkElement)window.Content;
            surface.Measure(new System.Windows.Size(1280, 800));
            surface.Arrange(new Rect(0, 0, 1280, 800)); surface.UpdateLayout();
            Check(((System.Windows.Controls.TextBlock)window.FindName("HotkeyLabel")).Text.Contains("PrtSc"), "Main window must display the current capture hotkey");
            static IEnumerable<System.Windows.Controls.Button> Buttons(DependencyObject node)
            {
                for (int i = 0; i < VisualTreeHelper.GetChildrenCount(node); i++)
                {
                    var child = VisualTreeHelper.GetChild(node, i);
                    if (child is System.Windows.Controls.Button button) yield return button;
                    foreach (var nested in Buttons(child)) yield return nested;
                }
            }
            void Click(string label) => Buttons(surface).Single(b => b.Content as string == label).RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));
            Check(Buttons(surface).Any(b => b.Content as string == "▣  ウィンドウを選択して撮影"), "Window capture must be accessible from the sidebar");
            Click("↶");
            Check(store.Load()[0].Marks.Count == 6, "Undo must persist the changed document");
            Click("↷");
            Check(store.Load()[0].Marks.Count == 7, "Redo must survive the history thumbnail refresh");
            var preview = new RenderTargetBitmap(1280, 800, 96, 96, PixelFormats.Pbgra32);
            preview.Render(surface);
            Directory.CreateDirectory("artifacts"); CaptureStore.WriteImage(Path.Combine("artifacts", "editor-preview.png"), preview);
            Check(surface.ActualWidth == 1280, "Main window layout failed");
            window.Close();
            // The files must be unlocked after loading so deletion works on Windows.
            store.Delete(entry); Check(store.Load().Count == 0, "Deletion failed");
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
        File.WriteAllText("self-test-result.txt", "PASS: region DPI mapping, bounds, window hit testing and z-order, negative desktop origins, clipped window crops, PrtSc label, window capture button, all annotation tools, render dimensions, pixel changes, persistence, original image, main window layout, Undo/Redo persistence, unlocked deletion.");
    }
}
