using System.IO;
using System.Security.Cryptography;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Prtsc;

internal static class ArrowStyleTests
{
    public static void Run(BitmapSource image)
    {
        static void Check(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
        HashSet<string> outputs = [];
        foreach (var style in ArrowStyles.Options)
        {
            var mark = new Mark { ArrowStyle = style.Key, X = 80, Y = 100, X2 = 400, Y2 = 100, Width = 6 };
            var rendered = AnnotationRenderer.Render(image, [mark]);
            byte[] pixels = new byte[640 * 360 * 4]; rendered.CopyPixels(pixels, 640 * 4, 0);
            Check(outputs.Add(Convert.ToHexString(SHA256.HashData(pixels))), "Each arrow style must have a distinct rendered appearance: " + style.Key);
            Check(JsonSerializer.Deserialize<Mark>(JsonSerializer.Serialize(mark))?.ArrowStyle == style.Key, "Arrow style must survive JSON round-trip");
            var canvas = new Canvas(); AnnotationRenderer.Draw(canvas, image, [mark]);
            canvas.Measure(new System.Windows.Size(640, 360)); canvas.Arrange(new Rect(0, 0, 640, 360)); canvas.UpdateLayout();
            var center = new System.Windows.Point(240, style.Key == "Curve" ? 151.2 : 100);
            Check(AnnotationSelection.HitTest(canvas, [mark], center) == 0, "All arrow styles must remain selectable: " + style.Key);
            Check(!AnnotationSelection.Bounds(canvas, 0).IsEmpty, "All arrow styles need bounds for selection handles");
            foreach (var end in new[] { new System.Windows.Point(80, 100), new System.Windows.Point(80.001, 100), new System.Windows.Point(20, 30), new System.Windows.Point(80, 300) })
                AnnotationRenderer.Render(image, [mark with { X2 = end.X, Y2 = end.Y }]);
        }
        Check(JsonSerializer.Deserialize<Mark>("{\"Tool\":\"Arrow\"}")?.ArrowStyle == "Open", "Legacy captures without arrow style must retain the standard arrow");
        Check(!ArrowStyles.HitTest(new Mark { ArrowStyle = "Curve", X = 80, Y = 100, X2 = 400, Y2 = 100, Width = 6 }, new System.Windows.Point(240, 100)), "Curved arrows must not select the empty area inside their bounding rectangle");
        double previewHeight = 120 + Math.Ceiling(ArrowStyles.Options.Count / 2.0) * 128;
        var canvasPreview = new Canvas { Width = 760, Height = previewHeight, Background = Brushes.White };
        void Label(string text, double x, double y, double size, System.Windows.Media.Brush color)
        {
            var label = new TextBlock { Text = text, FontSize = size, Foreground = color, FontWeight = FontWeights.SemiBold };
            Canvas.SetLeft(label, x); Canvas.SetTop(label, y); canvasPreview.Children.Add(label);
        }
        Label("矢印スタイル", 30, 22, 25, Brushes.MidnightBlue);
        Label("描いた後も、選択してスタイルを変更できます", 30, 58, 13, Brushes.SlateGray);
        for (int i = 0; i < ArrowStyles.Options.Count; i++)
        {
            double x = 38 + i % 2 * 375, y = 103 + i / 2 * 128;
            Label(ArrowStyles.Options[i].Name, x, y, 16, Brushes.SlateGray);
            canvasPreview.Children.Add(ArrowStyles.Create(new Mark { ArrowStyle = ArrowStyles.Options[i].Key, X = x + 12, Y = y + 54, X2 = x + 275, Y2 = y + 54, Color = "#6366F1", Width = 5 }));
        }
        canvasPreview.Measure(new System.Windows.Size(760, previewHeight)); canvasPreview.Arrange(new Rect(0, 0, 760, previewHeight)); canvasPreview.UpdateLayout();
        var preview = new RenderTargetBitmap(760, (int)previewHeight, 96, 96, PixelFormats.Pbgra32); preview.Render(canvasPreview);
        Directory.CreateDirectory("artifacts"); CaptureStore.WriteImage(Path.Combine("artifacts", "arrow-styles.png"), preview);
        TestGradient(image);
    }

    private static void TestGradient(BitmapSource image)
    {
        // Sample actual composited output, rather than only inspecting the brush configuration.
        foreach (var endpoints in new[]
        {
            (new System.Windows.Point(60, 180), new System.Windows.Point(560, 180)),
            (new System.Windows.Point(560, 180), new System.Windows.Point(60, 180)),
            (new System.Windows.Point(320, 320), new System.Windows.Point(320, 40)),
            (new System.Windows.Point(70, 300), new System.Windows.Point(570, 60))
        })
        {
            var mark = new Mark { ArrowStyle = "Tapered", Color = "#FF0000", Width = 8, X = endpoints.Item1.X, Y = endpoints.Item1.Y, X2 = endpoints.Item2.X, Y2 = endpoints.Item2.Y };
            var output = AnnotationRenderer.Render(image, [mark]);
            byte[] pixels = new byte[640 * 360 * 4]; output.CopyPixels(pixels, 640 * 4, 0);
            int Green(double t)
            {
                var point = endpoints.Item1 + (endpoints.Item2 - endpoints.Item1) * t;
                return pixels[((int)Math.Round(point.Y) * 640 + (int)Math.Round(point.X)) * 4 + 1];
            }
            int tail = Green(0.15), middle = Green(0.5), head = Green(0.92);
            if (!(tail > middle + 25 && middle > head + 25 && head < 8))
                throw new InvalidOperationException($"Tapered arrow must fade from translucent tail to opaque head in every direction: {tail}, {middle}, {head}");
            var shape = ArrowStyles.Create(mark);
            var path = (System.Windows.Shapes.Path)shape.Children[0];
            var axis = endpoints.Item2 - endpoints.Item1; axis.Normalize(); var normal = new Vector(-axis.Y, axis.X);
            var tailSide = endpoints.Item1 + (endpoints.Item2 - endpoints.Item1) * 0.1 + normal * 5;
            var neckSide = endpoints.Item1 + (endpoints.Item2 - endpoints.Item1) * 0.75 + normal * 5;
            if (path.Data.FillContains(tailSide) || !path.Data.FillContains(neckSide))
                throw new InvalidOperationException("Tapered arrow must have a narrow tail and a wider shaft near the head");
        }
        var canvas = new Canvas { Width = 640, Height = 360, Background = Brushes.White };
        var title = new TextBlock { Text = "グラデーション矢印", FontSize = 24, Foreground = Brushes.MidnightBlue, FontWeight = FontWeights.SemiBold };
        Canvas.SetLeft(title, 26); Canvas.SetTop(title, 20); canvas.Children.Add(title);
        var subtitle = new TextBlock { Text = "細く半透明の始点から、太く不透明な矢じりへ", FontSize = 13, Foreground = Brushes.SlateGray };
        Canvas.SetLeft(subtitle, 26); Canvas.SetTop(subtitle, 57); canvas.Children.Add(subtitle);
        foreach (var mark in new[]
        {
            new Mark { ArrowStyle = "Tapered", X = 345, Y = 140, X2 = 60, Y2 = 140, Width = 6, Color = "#84CC16" },
            new Mark { ArrowStyle = "Tapered", X = 345, Y = 310, X2 = 80, Y2 = 205, Width = 7, Color = "#06B6D4" },
            new Mark { ArrowStyle = "Tapered", X = 490, Y = 320, X2 = 490, Y2 = 110, Width = 9, Color = "#EF233C" }
        }) canvas.Children.Add(ArrowStyles.Create(mark));
        canvas.Measure(new System.Windows.Size(640, 360)); canvas.Arrange(new Rect(0, 0, 640, 360)); canvas.UpdateLayout();
        var bitmap = new RenderTargetBitmap(640, 360, 96, 96, PixelFormats.Pbgra32); bitmap.Render(canvas);
        CaptureStore.WriteImage(Path.Combine("artifacts", "tapered-arrow-preview.png"), bitmap);
    }
}
