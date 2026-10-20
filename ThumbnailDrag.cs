using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Point = System.Windows.Point;

namespace Prtsc;

internal sealed class ThumbnailDrag(Func<CaptureEntry, DataObject> export, Action<Exception> reportError)
{
    private Point start;
    private CaptureEntry? entry;
    public bool SuppressClick { get; private set; }
    public void Attach(FrameworkElement surface)
    {
        surface.PreviewMouseLeftButtonDown += (_, e) =>
        {
            SuppressClick = false; entry = null; start = e.GetPosition(surface);
            var current = e.OriginalSource as DependencyObject;
            while (current is not null)
            {
                if (current is System.Windows.Controls.Image image && image.DataContext is CaptureEntry capture) { entry = capture; break; }
                if (current == surface) break;
                current = current is Visual ? VisualTreeHelper.GetParent(current) : LogicalTreeHelper.GetParent(current);
            }
        };
        surface.PreviewMouseMove += (_, e) =>
        {
            if (entry is null || !ShouldStart(start, e.GetPosition(surface), e.LeftButton == MouseButtonState.Pressed)) return;
            var capture = entry; entry = null; SuppressClick = true; e.Handled = true;
            System.Windows.Input.Mouse.Capture(null); // Cancel the thumbnail button's click when a drag starts.
            try { System.Windows.DragDrop.DoDragDrop(surface, export(capture), System.Windows.DragDropEffects.Copy); }
            catch (Exception ex) { reportError(ex); }
        };
        surface.PreviewMouseLeftButtonUp += (_, _) => entry = null;
    }
    internal static bool ShouldStart(Point start, Point end, bool pressed)
        => pressed && (Math.Abs(end.X - start.X) >= SystemParameters.MinimumHorizontalDragDistance || Math.Abs(end.Y - start.Y) >= SystemParameters.MinimumVerticalDragDistance);
}
