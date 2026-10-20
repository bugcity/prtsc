using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using Point = System.Windows.Point;

namespace Prtsc;

internal static class AnnotationSelection
{
    internal static bool HasEndpoints(Mark mark) => mark.Tool is "Arrow" or "Rectangle" or "Ellipse" or "Highlight" || mark.Tool == "Pen" && mark.Points.Count >= 2;
    internal static Rect Bounds(Canvas canvas, int index)
    {
        var element = (FrameworkElement)canvas.Children[index + 1];
        var bounds = VisualTreeHelper.GetDescendantBounds(element);
        return bounds.IsEmpty ? new Rect(0, 0, 1, 1) : element.TransformToAncestor(canvas).TransformBounds(bounds);
    }
    internal static int HitTest(Canvas canvas, IReadOnlyList<Mark> marks, Point point)
    {
        canvas.UpdateLayout();
        for (int i = marks.Count - 1; i >= 0; i--)
        {
            if (marks[i].Tool == "Arrow")
            {
                if (ArrowStyles.HitTest(marks[i], point)) return i;
            }
            else if (marks[i].Tool == "Pen")
            {
                var points = marks[i].Points;
                for (int p = 1; p < points.Count; p++)
                {
                    var a = new Point(points[p - 1][0], points[p - 1][1]);
                    var b = new Point(points[p][0], points[p][1]); var line = b - a;
                    double t = line.LengthSquared == 0 ? 0 : Math.Clamp(Vector.Multiply(point - a, line) / line.LengthSquared, 0, 1);
                    if ((point - (a + line * t)).Length <= marks[i].Width / 2 + 6) return i;
                }
            }
            else
            {
                var bounds = Bounds(canvas, i); bounds.Inflate(5, 5);
                if (bounds.Contains(point)) return i;
            }
        }
        return -1;
    }
    internal static void Draw(Canvas canvas, int index, Mark mark)
    {
        canvas.UpdateLayout();
        var bounds = Bounds(canvas, index); bounds.Inflate(5, 5);
        var frame = new System.Windows.Shapes.Rectangle { Width = bounds.Width, Height = bounds.Height, Stroke = Brushes.SlateBlue, StrokeThickness = 1.5, StrokeDashArray = [4, 3], IsHitTestVisible = false };
        Canvas.SetLeft(frame, bounds.X); Canvas.SetTop(frame, bounds.Y); canvas.Children.Add(frame);
        if (!HasEndpoints(mark)) return;
        var endpoints = AnnotationDocument.Endpoints(mark);
        foreach (var point in new[] { endpoints.Start, endpoints.End })
        {
            var handle = new Ellipse { Width = 12, Height = 12, Fill = Brushes.White, Stroke = Brushes.SlateBlue, StrokeThickness = 2, IsHitTestVisible = false };
            Canvas.SetLeft(handle, point.X - 6); Canvas.SetTop(handle, point.Y - 6); canvas.Children.Add(handle);
        }
    }
}
