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
    public static void Draw(Canvas canvas, BitmapSource original, IEnumerable<Mark> marks, CropRegion? crop = null)
    {
        var bounds = ImageCrop.Bounds(original, crop);
        canvas.Children.Clear(); canvas.Width = bounds.Width; canvas.Height = bounds.Height;
        BitmapSource source = crop is null ? original : new CroppedBitmap(original, bounds);
        canvas.Children.Add(new System.Windows.Controls.Image { Source = source, Width = canvas.Width, Height = canvas.Height, Stretch = Stretch.Fill, IsHitTestVisible = false });
        foreach (var mark in marks) Add(canvas, ImageCrop.DisplayMark(mark, crop), Math.Max(1, original.PixelWidth - mark.X));
    }
    public static void Add(Canvas canvas, Mark mark, double? textMaxWidth = null)
    {
        var brush = (Brush)new BrushConverter().ConvertFromString(mark.Color)!;
        double x = Math.Min(mark.X, mark.X2), y = Math.Min(mark.Y, mark.Y2);
        double width = Math.Max(1, Math.Abs(mark.X2 - mark.X)), height = Math.Max(1, Math.Abs(mark.Y2 - mark.Y));
        FrameworkElement shape;
        switch (mark.Tool)
        {
            case "Line":
                shape = new Line { X1 = mark.X, Y1 = mark.Y, X2 = mark.X2, Y2 = mark.Y2, Stroke = brush, StrokeThickness = mark.Width, StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round }; break;
            case "Arrow":
                shape = ArrowStyles.Create(mark);
                break;
            case "Pen":
                shape = new Polyline { Points = new PointCollection(mark.Points.Select(p => new Point(p[0], p[1]))), Stroke = brush, StrokeThickness = mark.Width, StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round, StrokeLineJoin = PenLineJoin.Round }; break;
            case "Text":
                var style = mark.TextStyle?.Normalize();
                var text = new TextBlock { Text = mark.Text, Foreground = brush, FontSize = style?.Size ?? Math.Max(18, mark.Width * 6), FontFamily = new FontFamily(style?.FontFamily ?? "Segoe UI"), FontWeight = FontWeights.Bold, TextWrapping = TextWrapping.Wrap };
                double maxWidth = textMaxWidth ?? Math.Max(1, canvas.Width - mark.X);
                shape = style is null ? text : new Border { Child = text, Background = (Brush)new BrushConverter().ConvertFromString(style.Background)!, BorderBrush = (Brush)new BrushConverter().ConvertFromString(style.BorderColor)!, BorderThickness = new Thickness(style.BorderWidth), Padding = new Thickness(4) };
                shape.MaxWidth = maxWidth;
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
    public static BitmapSource Render(BitmapSource original, IEnumerable<Mark> marks, CropRegion? crop = null)
    {
        var canvas = new Canvas { ClipToBounds = true, Background = Brushes.White };
        Draw(canvas, original, marks, crop);
        canvas.Measure(new System.Windows.Size(canvas.Width, canvas.Height)); canvas.Arrange(new Rect(0, 0, canvas.Width, canvas.Height)); canvas.UpdateLayout();
        var bitmap = new RenderTargetBitmap((int)canvas.Width, (int)canvas.Height, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(canvas); bitmap.Freeze(); return bitmap;
    }
}
