using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Point = System.Windows.Point;
using Path = System.Windows.Shapes.Path;

namespace Prtsc;

internal sealed record ArrowStyleOption(string Key, string Name, BitmapSource Preview);

internal static class ArrowStyles
{
    private sealed record Drawing(Geometry Shaft, Geometry Head, bool FilledHead = false, bool Block = false, bool Hollow = false, bool Dashed = false);
    public static IReadOnlyList<ArrowStyleOption> Options { get; } = new[]
    {
        ("Tapered", "グラデーション"), ("Open", "標準"), ("Filled", "塗りつぶし"), ("Block", "太い矢印"), ("Outline", "白抜き"),
        ("Double", "両矢印"), ("Dashed", "破線"), ("Curve", "曲線"), ("CircleTail", "丸付き")
    }.Select(item => new ArrowStyleOption(item.Item1, item.Item2, Preview(item.Item1))).ToArray();

    private static StreamGeometry Figure(IEnumerable<Point> points, bool filled = false, bool closed = false)
    {
        var geometry = new StreamGeometry(); var array = points.ToArray();
        if (array.Length == 0) return geometry;
        using (var context = geometry.Open())
        {
            context.BeginFigure(array[0], filled, closed);
            foreach (var point in array.Skip(1)) context.LineTo(point, true, false);
        }
        geometry.Freeze(); return geometry;
    }
    private static Geometry Head(Point tip, Vector forward, double length, bool filled)
    {
        var normal = new Vector(-forward.Y, forward.X);
        return filled
            ? Figure([tip - forward * length + normal * length * 0.52, tip, tip - forward * length - normal * length * 0.52], true, true)
            : Figure([tip - forward * (length * Math.Cos(0.48)) + normal * (length * Math.Sin(0.48)), tip,
                tip - forward * (length * Math.Cos(0.48)) - normal * (length * Math.Sin(0.48))]);
    }
    private static Drawing Build(Mark mark)
    {
        var start = new Point(mark.X, mark.Y); var end = new Point(mark.X2, mark.Y2);
        var direction = end - start; double distance = direction.Length;
        if (distance < 0.01) return new Drawing(Geometry.Empty, Geometry.Empty);
        direction.Normalize(); var normal = new Vector(-direction.Y, direction.X);
        double headLength = Math.Min(Math.Max(15, mark.Width * 4), distance * 0.55);
        if (mark.ArrowStyle == "Tapered")
        {
            double length = Math.Min(Math.Max(25, mark.Width * 6), distance * 0.35);
            double halfHead = Math.Min(Math.Max(13, mark.Width * 3.4), distance * 0.35);
            double halfShaft = halfHead * 0.48;
            var neck = end - direction * length;
            // A pointed tail grows into the shaft, followed by a distinct triangular head.
            return new Drawing(Figure([start, neck + normal * halfShaft, neck + normal * halfHead,
                end, neck - normal * halfHead, neck - normal * halfShaft], true, true), Geometry.Empty, Block: true);
        }
        if (mark.ArrowStyle is "Block" or "Outline")
        {
            double length = Math.Min(Math.Max(22, mark.Width * 7), distance * 0.6);
            double halfShaft = Math.Min(Math.Max(3, mark.Width * 1.2), distance * 0.12);
            double halfHead = Math.Min(Math.Max(12, mark.Width * 3.4), distance * 0.4);
            var neck = end - direction * length;
            return new Drawing(Figure([start + normal * halfShaft, neck + normal * halfShaft, neck + normal * halfHead,
                end, neck - normal * halfHead, neck - normal * halfShaft, start - normal * halfShaft], true, true), Geometry.Empty,
                Block: true, Hollow: mark.ArrowStyle == "Outline");
        }
        if (mark.ArrowStyle == "Curve")
        {
            var control = start + (end - start) * 0.5 + normal * distance * 0.32;
            var curve = new StreamGeometry();
            using (var context = curve.Open()) { context.BeginFigure(start, false, false); context.QuadraticBezierTo(control, end, true, false); }
            var tangent = end - control; tangent.Normalize();
            return new Drawing(curve, Head(end, tangent, headLength, false));
        }
        if (mark.ArrowStyle == "Filled")
            return new Drawing(Figure([start, end - direction * (headLength * 0.8)]), Head(end, direction, headLength, true), FilledHead: true);
        var heads = new GeometryGroup(); heads.Children.Add(Head(end, direction, headLength, false));
        if (mark.ArrowStyle == "Double") heads.Children.Add(Head(start, -direction, headLength, false));
        bool circle = mark.ArrowStyle == "CircleTail";
        double radius = Math.Min(Math.Max(5, mark.Width * 1.6), distance * 0.15);
        if (circle) heads.Children.Add(new EllipseGeometry(start, radius, radius));
        return new Drawing(Figure([circle ? start + direction * radius : start, end]), heads,
            FilledHead: circle, Dashed: mark.ArrowStyle == "Dashed");
    }
    public static Canvas Create(Mark mark)
    {
        var drawing = Build(mark);
        var brush = (System.Windows.Media.Brush)new BrushConverter().ConvertFromString(mark.Color)!;
        if (mark.ArrowStyle == "Tapered")
        {
            var color = ((SolidColorBrush)brush).Color;
            System.Windows.Media.Color Alpha(double opacity) => System.Windows.Media.Color.FromArgb((byte)Math.Round(color.A * opacity), color.R, color.G, color.B);
            // Absolute image coordinates keep the fade aligned with the actual arrow, including reversed and diagonal arrows.
            brush = new LinearGradientBrush
            {
                MappingMode = BrushMappingMode.Absolute, StartPoint = new Point(mark.X, mark.Y), EndPoint = new Point(mark.X2, mark.Y2),
                GradientStops = [new GradientStop(Alpha(0.2), 0), new GradientStop(Alpha(0.62), 0.45), new GradientStop(color, 0.85), new GradientStop(color, 1)]
            };
        }
        var canvas = new Canvas { IsHitTestVisible = false };
        Path MakePath(Geometry geometry) => new()
        {
            Data = geometry, Stroke = brush, StrokeThickness = mark.Width,
            StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round, StrokeLineJoin = PenLineJoin.Round,
            IsHitTestVisible = false
        };
        var shaft = MakePath(drawing.Shaft);
        if (drawing.Block)
        {
            shaft.Fill = drawing.Hollow ? Brushes.White : brush;
            shaft.StrokeThickness = drawing.Hollow ? Math.Max(1.5, mark.Width * 0.55) : 0;
        }
        else if (drawing.Dashed) shaft.StrokeDashArray = [3, 2];
        canvas.Children.Add(shaft);
        if (!drawing.Block)
        {
            var head = MakePath(drawing.Head); if (drawing.FilledHead) head.Fill = brush;
            canvas.Children.Add(head);
        }
        return canvas;
    }
    public static bool HitTest(Mark mark, Point point)
    {
        var drawing = Build(mark); var pen = new System.Windows.Media.Pen(Brushes.Black, mark.Width + 12);
        return drawing.Shaft.StrokeContains(pen, point) || drawing.Head.StrokeContains(pen, point)
            || drawing.Block && drawing.Shaft.FillContains(point) || drawing.FilledHead && drawing.Head.FillContains(point);
    }
    private static BitmapSource Preview(string style)
    {
        var canvas = new Canvas { Width = 98, Height = 34, Background = Brushes.White, ClipToBounds = true };
        canvas.Children.Add(Create(new Mark { ArrowStyle = style, Color = "#6366F1", Width = 2.5, X = 10, Y = 15, X2 = 87, Y2 = 15 }));
        canvas.Measure(new System.Windows.Size(98, 34)); canvas.Arrange(new Rect(0, 0, 98, 34)); canvas.UpdateLayout();
        var bitmap = new RenderTargetBitmap(98, 34, 96, 96, PixelFormats.Pbgra32); bitmap.Render(canvas); bitmap.Freeze(); return bitmap;
    }
}
