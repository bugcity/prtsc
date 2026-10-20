using System.Windows;

namespace Prtsc;

// The document keeps vectors; PNG rendering is only an output operation.
internal sealed class AnnotationDocument(CaptureEntry entry)
{
    private readonly Stack<List<Mark>> undo = new(), redo = new();
    public IReadOnlyList<Mark> Marks => entry.Marks;
    private static List<Mark> Snapshot(IEnumerable<Mark> marks) => marks.Select(m => m with { Points = m.Points.Select(p => p.ToArray()).ToList() }).ToList();
    public void Change(Action<List<Mark>> change)
    {
        undo.Push(Snapshot(entry.Marks)); redo.Clear(); change(entry.Marks);
    }
    public bool Undo()
    {
        if (undo.Count == 0) return false;
        redo.Push(Snapshot(entry.Marks)); entry.Marks = undo.Pop(); return true;
    }
    public bool Redo()
    {
        if (redo.Count == 0) return false;
        undo.Push(Snapshot(entry.Marks)); entry.Marks = redo.Pop(); return true;
    }
    public static Mark Move(Mark mark, Vector delta) => mark with
    {
        X = mark.X + delta.X, Y = mark.Y + delta.Y,
        X2 = mark.X2 + delta.X, Y2 = mark.Y2 + delta.Y,
        Points = mark.Points.Select(p => new[] { p[0] + delta.X, p[1] + delta.Y }).ToList()
    };
}
