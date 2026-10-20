using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using Point = System.Windows.Point;
using Brush = System.Windows.Media.Brush;

namespace Prtsc;

internal static class AnnotationRenderer
{
    public static void Draw(Canvas canvas, BitmapSource original, IEnumerable<Mark> marks)
    {
        canvas.Children.Clear(); canvas.Width = original.PixelWidth; canvas.Height = original.PixelHeight;
        canvas.Children.Add(new System.Windows.Controls.Image { Source = original, Width = canvas.Width, Height = canvas.Height, Stretch = Stretch.Fill, IsHitTestVisible = false });
        foreach (var mark in marks) Add(canvas, mark);
    }
    public static void Add(Canvas canvas, Mark mark)
    {
        var brush = (Brush)new BrushConverter().ConvertFromString(mark.Color)!;
        double x = Math.Min(mark.X, mark.X2), y = Math.Min(mark.Y, mark.Y2);
        double width = Math.Max(1, Math.Abs(mark.X2 - mark.X)), height = Math.Max(1, Math.Abs(mark.Y2 - mark.Y));
        FrameworkElement shape;
        switch (mark.Tool)
        {
            case "Arrow":
                double angle = Math.Atan2(mark.Y2 - mark.Y, mark.X2 - mark.X);
                double length = Math.Min(Math.Max(15, mark.Width * 4), Math.Sqrt(width * width + height * height) * 0.55);
                var geometry = new StreamGeometry();
                using (var context = geometry.Open())
                {
                    context.BeginFigure(new Point(mark.X, mark.Y), false, false); context.LineTo(new Point(mark.X2, mark.Y2), true, false);
                    context.BeginFigure(new Point(mark.X2 - length * Math.Cos(angle - 0.48), mark.Y2 - length * Math.Sin(angle - 0.48)), false, false);
                    context.LineTo(new Point(mark.X2, mark.Y2), true, false);
                    context.LineTo(new Point(mark.X2 - length * Math.Cos(angle + 0.48), mark.Y2 - length * Math.Sin(angle + 0.48)), true, false);
                }
                shape = new System.Windows.Shapes.Path { Data = geometry, Stroke = brush, StrokeThickness = mark.Width, StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round, StrokeLineJoin = PenLineJoin.Round };
                break;
            case "Pen":
                shape = new Polyline { Points = new PointCollection(mark.Points.Select(p => new Point(p[0], p[1]))), Stroke = brush, StrokeThickness = mark.Width, StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round, StrokeLineJoin = PenLineJoin.Round }; break;
            case "Text":
                shape = new TextBlock { Text = mark.Text, Foreground = brush, FontSize = Math.Max(18, mark.Width * 6), FontWeight = FontWeights.Bold, TextWrapping = TextWrapping.Wrap, MaxWidth = Math.Max(1, canvas.Width - mark.X) };
                Canvas.SetLeft(shape, mark.X); Canvas.SetTop(shape, mark.Y); break;
            case "Number":
                double size = Math.Max(32, mark.Width * 9);
                shape = new Border { Width = size, Height = size, CornerRadius = new CornerRadius(size / 2), Background = brush, Child = new TextBlock { Text = mark.Text, Foreground = Brushes.White, FontWeight = FontWeights.Bold, FontSize = size * 0.52, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center } };
                Canvas.SetLeft(shape, mark.X - size / 2); Canvas.SetTop(shape, mark.Y - size / 2); break;
            default:
                Shape area = mark.Tool == "Ellipse" ? new Ellipse() : new System.Windows.Shapes.Rectangle();
                area.Width = width; area.Height = height;
                if (mark.Tool == "Highlight") { area.Fill = brush; area.Opacity = 0.3; }
                else { area.Stroke = brush; area.StrokeThickness = mark.Width; }
                Canvas.SetLeft(area, x); Canvas.SetTop(area, y); shape = area; break;
        }
        shape.IsHitTestVisible = false; canvas.Children.Add(shape);
    }
    public static BitmapSource Render(BitmapSource original, IEnumerable<Mark> marks)
    {
        var canvas = new Canvas { ClipToBounds = true, Background = Brushes.White };
        Draw(canvas, original, marks);
        canvas.Measure(new System.Windows.Size(canvas.Width, canvas.Height)); canvas.Arrange(new Rect(0, 0, canvas.Width, canvas.Height)); canvas.UpdateLayout();
        var bitmap = new RenderTargetBitmap(original.PixelWidth, original.PixelHeight, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(canvas); bitmap.Freeze(); return bitmap;
    }
}
