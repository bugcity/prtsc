using System.Windows;

namespace Prtsc;

// The document keeps vectors; PNG rendering is only an output operation.
internal sealed class AnnotationDocument(CaptureEntry entry)
{
    private sealed record State(List<Mark> Marks, CropRegion? Crop);
    private readonly Stack<State> undo = new(), redo = new();
    public IReadOnlyList<Mark> Marks => entry.Marks;
    private static List<Mark> Snapshot(IEnumerable<Mark> marks) => marks.Select(m => m with { Points = m.Points.Select(p => p.ToArray()).ToList() }).ToList();
    private State Capture() => new(Snapshot(entry.Marks), entry.Crop);
    private void Restore(State state) { entry.Marks = state.Marks; entry.Crop = state.Crop; }
    public void Change(Action<List<Mark>> change)
    {
        undo.Push(Capture()); redo.Clear(); change(entry.Marks);
    }
    public void Crop(CropRegion crop) => Change(_ => entry.Crop = crop);
    public bool Undo()
    {
        if (undo.Count == 0) return false;
        redo.Push(Capture()); Restore(undo.Pop()); return true;
    }
    public bool Redo()
    {
        if (redo.Count == 0) return false;
        undo.Push(Capture()); Restore(redo.Pop()); return true;
    }
    public static Mark Move(Mark mark, Vector delta) => mark with
    {
        X = mark.X + delta.X, Y = mark.Y + delta.Y,
        X2 = mark.X2 + delta.X, Y2 = mark.Y2 + delta.Y,
        Points = mark.Points.Select(p => new[] { p[0] + delta.X, p[1] + delta.Y }).ToList()
    };
    public static (Point Start, Point End) Endpoints(Mark mark)
        => mark.Tool == "Pen" && mark.Points.Count > 0
            ? (new(mark.Points[0][0], mark.Points[0][1]), new(mark.Points[^1][0], mark.Points[^1][1]))
            : (new(mark.X, mark.Y), new(mark.X2, mark.Y2));
    public static Mark ResizeEndpoint(Mark mark, bool start, Point position)
    {
        if (mark.Tool != "Pen" || mark.Points.Count < 2)
            return start ? mark with { X = position.X, Y = position.Y } : mark with { X2 = position.X, Y2 = position.Y };
        var ends = Endpoints(mark); var anchor = start ? ends.End : ends.Start;
        var moving = start ? ends.Start : ends.End; var oldAxis = moving - anchor; var newAxis = position - anchor;
        List<double[]> points;
        if (oldAxis.LengthSquared >= 1)
        {
            // Rotate and scale about the fixed endpoint to preserve a freehand stroke's shape.
            double real = Vector.Multiply(oldAxis, newAxis) / oldAxis.LengthSquared;
            double imaginary = Vector.CrossProduct(oldAxis, newAxis) / oldAxis.LengthSquared;
            points = mark.Points.Select(p =>
            {
                var v = new Point(p[0], p[1]) - anchor;
                return new[] { anchor.X + real * v.X - imaginary * v.Y, anchor.Y + imaginary * v.X + real * v.Y };
            }).ToList();
        }
        else
        {
            // Closed/near-closed loops have no stable endpoint axis. Distribute the displacement by arc length.
            double[] distance = new double[mark.Points.Count];
            for (int i = 1; i < distance.Length; i++)
                distance[i] = distance[i - 1] + (new Point(mark.Points[i][0], mark.Points[i][1]) - new Point(mark.Points[i - 1][0], mark.Points[i - 1][1])).Length;
            var delta = position - moving;
            points = mark.Points.Select((p, i) =>
            {
                double t = distance[^1] > 0.001 ? distance[i] / distance[^1] : (double)i / (distance.Length - 1);
                double weight = start ? 1 - t : t;
                return new[] { p[0] + delta.X * weight, p[1] + delta.Y * weight };
            }).ToList();
        }
        int index = start ? 0 : points.Count - 1; points[index] = [position.X, position.Y];
        return mark with { X = points[0][0], Y = points[0][1], X2 = points[^1][0], Y2 = points[^1][1], Points = points };
    }
}
