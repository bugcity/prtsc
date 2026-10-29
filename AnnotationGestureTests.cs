using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media.Imaging;
using Point = System.Windows.Point;

namespace Prtsc;

internal static class AnnotationGestureTests
{
    internal static void Run(string root, BitmapSource image)
    {
        static void Check(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
        var store = new CaptureStore(root);
        var window = new MainWindow(store, _ => Task.CompletedTask, false);
        var entry = window.AddCapture(image, "Line and Shift selection");
        window.SetDisplayZoom(1);
        var tools = (Panel)window.FindName("Tools");
        System.Windows.Controls.Button Tool(string name) => tools.Children.OfType<System.Windows.Controls.Button>().Single(b => b.Tag as string == name);
        void Choose(string name) => Tool(name).RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));
        bool Active(string name) => ((System.Windows.Media.SolidColorBrush)Tool(name).Background).Color.ToString() == "#FFEEF0FF";
        void Click(Point point, ModifierKeys modifiers = ModifierKeys.Shift)
        {
            window.BeginAnnotationGesture(point, modifiers);
            window.EndAnnotationGesture(point, modifiers);
        }
        void Drag(Point start, Point end, ModifierKeys modifiers = ModifierKeys.None)
        {
            window.BeginAnnotationGesture(start, modifiers);
            window.MoveAnnotationGesture(end, modifiers);
            window.EndAnnotationGesture(end, modifiers);
        }
        Choose("Line"); Drag(new(20, 20), new(120, 20));
        Check(entry.Marks.Count == 1 && entry.Marks[0].Tool == "Line" && entry.Marks[0].X2 == 120, "Line tool must create a horizontal editable line");
        var canvas = (Canvas)window.FindName("Editor");
        Check(AnnotationSelection.HasEndpoints(entry.Marks[0]) && AnnotationSelection.HitTest(canvas, entry.Marks, new(60, 24)) == 0, "Thin lines must be selectable near their stroke and expose endpoints");
        Choose("Select"); window.SelectAnnotation(0);
        Drag(new(120, 20), new(130, 70));
        Check(entry.Marks[0].X == 20 && entry.Marks[0].X2 == 130 && entry.Marks[0].Y2 == 70, "Line endpoint editing must keep its other end fixed");
        Check(AnnotationSelection.HitTest(canvas, entry.Marks, new(125, 22)) == -1, "Diagonal lines must not be selected through empty corners of their bounding box");
        Choose("Line"); Drag(new(200, 150), new(280, 200), ModifierKeys.Shift);
        Check(Math.Abs(entry.Marks[1].X2 - entry.Marks[1].X - (entry.Marks[1].Y2 - entry.Marks[1].Y)) < 0.001, "Shift drawing must constrain the line to 45 degrees");
        foreach (string name in new[] { "Arrow", "Pen", "Text", "Crop", "Line" })
        {
            Choose(name);
            var before = entry.Marks[0]; Click(new(before.X, before.Y));
            Check(Active("Select") && canvas.Cursor == System.Windows.Input.Cursors.Arrow, "Shift selecting an object must visibly enter temporary selection mode");
            window.NudgeSelection(new Vector(1, 0));
            var center = new Point((entry.Marks[0].X + entry.Marks[0].X2) / 2, (entry.Marks[0].Y + entry.Marks[0].Y2) / 2);
            Drag(center, center + new Vector(2, 1));
            Check(entry.Marks[0].X == before.X + 3 && Active("Select"), "Temporary selection must allow ordinary dragging after Shift is released");
            Click(new(500, 300), ModifierKeys.None);
            Check(entry.Marks.Count == 2 && Active(name) && canvas.Cursor == System.Windows.Input.Cursors.Cross, "Outside clicks must restore the original drawing tool without drawing text, crops or extra marks");
            var after = entry.Marks[0]; window.NudgeSelection(new Vector(1, 0));
            Check(entry.Marks[0] == after, "Restoring the drawing tool must clear temporary selection");
        }
        Choose("Arrow"); var endpoint = new Point(entry.Marks[0].X2, entry.Marks[0].Y2);
        Click(new(entry.Marks[0].X, entry.Marks[0].Y));
        Drag(endpoint + new Vector(0, 9), endpoint + new Vector(10, 20));
        Check(Active("Select") && entry.Marks[0].X2 == endpoint.X + 10 && entry.Marks[0].Y2 == endpoint.Y + 20, "Endpoint handles must remain draggable in temporary mode, including their hit margin");
        Click(new(entry.Marks[1].X, entry.Marks[1].Y), ModifierKeys.None);
        Check(Active("Arrow") && entry.Marks.Count == 2, "Clicking a different unselected object must also restore the previous tool without drawing");
        Choose("Pen"); Click(new(entry.Marks[0].X, entry.Marks[0].Y)); Choose("Line");
        Click(new(500, 300), ModifierKeys.None);
        Check(Active("Line"), "Explicit tool changes must discard the temporary return tool");
        Choose("Select"); Click(new(entry.Marks[0].X, entry.Marks[0].Y)); Click(new(500, 300), ModifierKeys.None);
        Check(Active("Select"), "Explicit selection mode must remain selected on outside clicks");
        Choose("Pen"); Click(new(entry.Marks[0].X, entry.Marks[0].Y)); window.SelectAllAnnotations();
        Click(new(500, 300), ModifierKeys.None);
        Check(Active("Pen"), "Select all during temporary selection must preserve the original return tool");
        Choose("Pen");
        var first = entry.Marks[0]; var second = entry.Marks[1];
        Click(new(first.X, first.Y)); Click(new(second.X, second.Y));
        Drag(new(first.X, first.Y), new(first.X + 10, first.Y + 10), ModifierKeys.Shift);
        Check(entry.Marks[0].X == first.X + 10 && entry.Marks[1].X == second.X + 10, "Shift dragging an already selected object must move the whole selection without deselecting it");
        first = entry.Marks[0]; second = entry.Marks[1];
        Click(new(first.X, first.Y)); window.NudgeSelection(new Vector(2, 0));
        Check(entry.Marks[0] == first && entry.Marks[1].X == second.X + 2, "Shift clicking a selected object must deselect only that object");
        // A new object joins the selection before a drag in temporary selection mode.
        first = entry.Marks[0]; second = entry.Marks[1];
        Drag(new(first.X, first.Y), new(first.X + 5, first.Y), ModifierKeys.Shift);
        Check(entry.Marks[0].X == first.X + 5 && entry.Marks[1].X == second.X + 5, "Shift dragging an unselected object must add it and move the selection");
        var persisted = store.Load()[0];
        Check(persisted.Marks.Count == 2 && persisted.Marks.All(m => m.Tool == "Line") && persisted.Marks[0].X == entry.Marks[0].X, "Line geometry and edits must survive reloading the library");
        var document = new AnnotationDocument(entry); first = entry.Marks[0];
        document.Change(marks => marks[0] = AnnotationDocument.ResizeEndpoint(first, true, new(5, 5)));
        Check(document.Undo() && AnnotationDocument.Endpoints(entry.Marks[0]) == AnnotationDocument.Endpoints(first) && document.Redo() && entry.Marks[0].X == 5, "Line endpoint editing must support undo and redo");
        var output = AnnotationRenderer.Render(image, persisted.Marks);
        var baseline = AnnotationRenderer.Render(image, []);
        byte[] pixels = new byte[image.PixelWidth * image.PixelHeight * 4], blank = new byte[pixels.Length];
        output.CopyPixels(pixels, image.PixelWidth * 4, 0); baseline.CopyPixels(blank, image.PixelWidth * 4, 0);
        Check(!pixels.SequenceEqual(blank), "Line annotations must be flattened into normal image output");
        foreach (string name in new[] { "Line", "Arrow", "Rectangle", "Ellipse", "Highlight", "Pen", "Number" })
        {
            Choose(name);
            int previousCount = entry.Marks.Count;
            if (name == "Number") Click(new(350, 200), ModifierKeys.None);
            else Drag(new(350, 200), new(400, 230));
            Check(entry.Marks.Count == previousCount + 1 && Active("Select"), "A completed annotation must be automatically selected in temporary selection mode");
            var previousColors = entry.Marks.Take(previousCount).Select(m => m.Color).ToArray();
            var palette = (Panel)window.FindName("AnnotationProperties");
            var blue = palette.Children.OfType<System.Windows.Controls.Button>().Single(b => b.Tag as string == "#0EA5E9");
            blue.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));
            Check(entry.Marks[^1].Color == "#0EA5E9" && entry.Marks.Take(previousCount).Select(m => m.Color).SequenceEqual(previousColors) && store.Load()[0].Marks[^1].Color == "#0EA5E9", "Changing color after drawing must update and persist only the new annotation");
            var slider = (Slider)window.FindName("StrokeSlider"); slider.Value = slider.Value == 7 ? 8 : 7;
            Check(entry.Marks[^1].Width == slider.Value, "Changing thickness after drawing must affect the new selected annotation");
            Click(new(600, 330), ModifierKeys.None);
            Check(Active(name) && entry.Marks.Count == previousCount + 1, "Outside clicks must return to the tool that created the annotation without creating another mark");
        }
        Choose("Line"); int beforeClick = entry.Marks.Count;
        Click(new(550, 300), ModifierKeys.None);
        Check(Active("Line") && entry.Marks.Count == beforeClick, "An incomplete drawing click must not enter automatic selection");
        window.RequestExit();
    }
}
