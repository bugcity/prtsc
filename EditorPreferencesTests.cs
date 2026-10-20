using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Prtsc;

internal static class EditorPreferencesTests
{
    public static void Run(string root, BitmapSource image)
    {
        static void Check(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
        var store = new CaptureStore(root); var path = Path.Combine(root, "settings.json"); var preferences = new CaptureSettingsStore(path);
        var window = new MainWindow(store, _ => Task.CompletedTask, false);
        var entry = window.AddCapture(image, "pen resize");
        var pen = new Mark { Tool = "Pen", X = 999, Y = 999, X2 = -99, Y2 = -99, Width = 4, Points = [[10, 20], [30, 40], [60, 20]] };
        entry.Marks.Add(pen); store.Save(entry, AnnotationRenderer.Render(image, entry.Marks));
        var slider = (Slider)window.FindName("StrokeSlider"); slider.Value = 8;
        Check(preferences.Load().StrokeWidth == 8, "Manual stroke width changes must persist immediately");
        window.SelectAnnotation(0); Check(slider.Value == 4 && preferences.Load().StrokeWidth == 8, "Selecting an existing thin stroke must not overwrite the saved drawing preference");
        window.RequestExit(); window = new MainWindow(store, _ => Task.CompletedTask, false);
        slider = (Slider)window.FindName("StrokeSlider"); Check(slider.Value == 8, "Editor stroke width must survive a process restart");
        window.ApplyCaptureLimit(3); Check(preferences.Load().StrokeWidth == 8, "Retention settings must not reset the saved stroke width");
        window.SelectAnnotation(0); slider.Value = 6;
        Check(preferences.Load().StrokeWidth == 6 && store.Load()[0].Marks[0].Width == 6, "Explicitly changing a selected stroke must update both the annotation and future drawing width");
        window.RequestExit(); window = new MainWindow(store, _ => Task.CompletedTask, false);
        Check(((Slider)window.FindName("StrokeSlider")).Value == 6 && preferences.Load().MaxCaptures == 3, "Stroke width and retention must survive restart together");
        window.SelectAnnotation(0);
        var editor = (Canvas)window.FindName("Editor");
        Check(AnnotationSelection.HasEndpoints(pen), "Freehand pen strokes must expose endpoint handles");
        var ends = AnnotationDocument.Endpoints(pen);
        Check(ends.Start == new System.Windows.Point(10, 20) && ends.End == new System.Windows.Point(60, 20), "Legacy pen handles must use actual first/last points rather than stale bounding coordinates");
        Check(Canvas.GetLeft(editor.Children[^1]) == 54 && Canvas.GetTop(editor.Children[^1]) == 14, "Pen endpoint handle must be drawn at its true final point");
        window.RequestExit();
        var resized = AnnotationDocument.ResizeEndpoint(pen, false, new(110, 20));
        Check(resized.Points[0].SequenceEqual(new double[] { 10, 20 }) && resized.Points[1].SequenceEqual(new double[] { 50, 60 }) && resized.Points[^1].SequenceEqual(new double[] { 110, 20 }), "Resizing a pen endpoint must scale the entire stroke around the fixed endpoint");
        Check(pen.Points[1].SequenceEqual(new double[] { 30, 40 }) && resized.Width == pen.Width, "Resizing must preserve source points and stroke thickness");
        var rotated = AnnotationDocument.ResizeEndpoint(pen, true, new(60, 70));
        Check(AnnotationDocument.Endpoints(rotated) == (new System.Windows.Point(60, 70), new System.Windows.Point(60, 20)), "Start endpoint resizing must keep the final endpoint fixed");
        var loop = pen with { Points = [[10, 10], [20, 30], [40, 10], [10, 10]] };
        var opened = AnnotationDocument.ResizeEndpoint(loop, false, new(80, 50));
        Check(AnnotationDocument.Endpoints(opened) == (new System.Windows.Point(10, 10), new System.Windows.Point(80, 50)) && opened.Points.SelectMany(p => p).All(double.IsFinite), "Closed pen loops must resize without division by zero");
        var collapsed = pen with { Points = [[10, 10], [10, 10], [10, 10]] };
        var stretched = AnnotationDocument.ResizeEndpoint(collapsed, true, new(0, 0));
        Check(stretched.Points[1].SequenceEqual(new double[] { 5, 5 }), "Collapsed pen strokes must remain resizable");
        entry.Marks = [pen]; var document = new AnnotationDocument(entry);
        document.Change(marks => marks[0] = resized); Check(document.Undo() && entry.Marks[0].Points[^1][0] == 60 && document.Redo() && entry.Marks[0].Points[^1][0] == 110, "Pen resizing must support undo and redo");
        store.Save(entry, AnnotationRenderer.Render(image, entry.Marks));
        var reopened = store.Load()[0].Marks[0]; Check(reopened.X2 == 110 && reopened.Points[1][0] == 50, "Resized pen geometry must persist for later editing");
        var canvas = new Canvas(); AnnotationRenderer.Draw(canvas, image, [resized]);
        canvas.Measure(new System.Windows.Size(image.PixelWidth, image.PixelHeight)); canvas.Arrange(new Rect(0, 0, image.PixelWidth, image.PixelHeight)); canvas.UpdateLayout();
        AnnotationSelection.Draw(canvas, 0, resized);
        var preview = new RenderTargetBitmap(image.PixelWidth, image.PixelHeight, 96, 96, PixelFormats.Pbgra32); preview.Render(canvas);
        CaptureStore.WriteImage(Path.Combine("artifacts", "pen-resize-preview.png"), preview);
        File.WriteAllText(path, "{\"MaxCaptures\":2}"); Check(preferences.Load().StrokeWidth == 4 && preferences.Load().MaxCaptures == 2, "Old settings without stroke width must migrate safely");
        File.WriteAllText(path, "{\"MaxCaptures\":2,\"StrokeWidth\":999}"); Check(preferences.Load().StrokeWidth == 4 && preferences.Load().MaxCaptures == 2, "Invalid stroke width must not discard the retention limit");
        bool rejected = false; try { preferences.Save(new CaptureSettings { StrokeWidth = double.NaN }); } catch (ArgumentOutOfRangeException) { rejected = true; }
        Check(rejected, "Non-finite stroke widths must not be written");
    }
}
