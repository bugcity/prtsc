using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using Point = System.Windows.Point;

namespace Prtsc;

internal sealed class CaptureLoupe : Border
{
    private readonly System.Windows.Controls.Image image = new() { Width = 125, Height = 125, Stretch = Stretch.Fill };
    private readonly TextBlock label = new() { Foreground = Brushes.White, Margin = new Thickness(6), TextAlignment = TextAlignment.Center, FontSize = 12 };
    private readonly Line horizontal = new() { X1 = 0, X2 = 125, Stroke = Brushes.Red, StrokeThickness = 1 };
    private readonly Line vertical = new() { Y1 = 0, Y2 = 125, Stroke = Brushes.Red, StrokeThickness = 1 };
    internal CaptureLoupe()
    {
        Background = Brushes.Black; BorderBrush = Brushes.White; BorderThickness = new Thickness(1); IsHitTestVisible = false;
        var stack = new StackPanel(); var view = new Grid { Width = 125, Height = 125 };
        view.Children.Add(image); var cross = new Canvas { IsHitTestVisible = false }; cross.Children.Add(horizontal); cross.Children.Add(vertical); view.Children.Add(cross);
        stack.Children.Add(view); stack.Children.Add(label); Child = stack;
        RenderOptions.SetBitmapScalingMode(image, BitmapScalingMode.NearestNeighbor);
    }
    internal static Int32Rect SampleRect(int x, int y, int width, int height)
    {
        int w = Math.Min(25, width), h = Math.Min(25, height);
        return new Int32Rect(Math.Clamp(x - w / 2, 0, width - w), Math.Clamp(y - h / 2, 0, height - h), w, h);
    }
    internal void Update(BitmapSource source, Point point, double width, double height, Int32Rect selection, Rect? monitor = null)
    {
        if (width <= 0 || height <= 0) return;
        int x = Math.Clamp((int)Math.Floor(point.X / width * source.PixelWidth), 0, source.PixelWidth - 1);
        int y = Math.Clamp((int)Math.Floor(point.Y / height * source.PixelHeight), 0, source.PixelHeight - 1);
        var sample = SampleRect(x, y, source.PixelWidth, source.PixelHeight);
        image.Source = new CroppedBitmap(source, sample);
        vertical.X1 = vertical.X2 = (x - sample.X + 0.5) * 125 / sample.Width;
        horizontal.Y1 = horizontal.Y2 = (y - sample.Y + 0.5) * 125 / sample.Height;
        label.Text = selection.IsEmpty ? $"X:{x} Y:{y}" : $"{selection.Width} × {selection.Height} px";
        var available = monitor ?? new Rect(0, 0, width, height);
        double left = point.X + 24, top = point.Y + 24;
        if (left + 130 > available.Right) left = point.X - 150;
        if (top + 165 > available.Bottom) top = point.Y - 180;
        Canvas.SetLeft(this, Math.Clamp(left, available.Left, Math.Max(available.Left, available.Right - 130)));
        Canvas.SetTop(this, Math.Clamp(top, available.Top, Math.Max(available.Top, available.Bottom - 165)));
    }
}
