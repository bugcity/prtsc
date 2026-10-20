using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Prtsc;

internal static class CropAndZoomTests
{
    public static void Run(string root, BitmapSource image)
    {
        static void Check(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
        static byte[] Pixels(BitmapSource bitmap)
        {
            var converted = new FormatConvertedBitmap(bitmap, PixelFormats.Pbgra32, null, 0);
            byte[] bytes = new byte[bitmap.PixelWidth * bitmap.PixelHeight * 4]; converted.CopyPixels(bytes, bitmap.PixelWidth * 4, 0); return bytes;
        }
        static IEnumerable<System.Windows.Controls.Button> Buttons(DependencyObject element)
        {
            for (int i = 0; i < VisualTreeHelper.GetChildrenCount(element); i++)
            {
                var child = VisualTreeHelper.GetChild(element, i);
                if (child is System.Windows.Controls.Button button) yield return button;
                foreach (var nested in Buttons(child)) yield return nested;
            }
        }
        var store = new CaptureStore(root); BitmapSource? copied = null;
        var window = new MainWindow(store, bitmap => { copied = bitmap; return Task.CompletedTask; }, false);
        var entry = window.AddCapture(image, "zoom and crop");
        entry.Marks.Add(new Mark { Tool = "Arrow", ArrowStyle = "Tapered", X = 100, Y = 100, X2 = 340, Y2 = 220, Width = 6 });
        entry.Marks.Add(new Mark { Tool = "Text", X = 250, Y = 70, Text = "Long text that must not reflow when the image is cropped.\n再編集できるテキスト", Color = "#1E293B", Width = 4 });
        store.Save(entry, AnnotationRenderer.Render(image, entry.Marks)); window.SelectAnnotation(0);
        var surface = (FrameworkElement)window.Content;
        void Layout(double width = 1280) { surface.Measure(new System.Windows.Size(width, 800)); surface.Arrange(new Rect(0, 0, width, 800)); surface.UpdateLayout(); }
        void Click(string label) => Buttons(surface).Single(button => button.Content as string == label).RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));
        Layout();
        Check(((FrameworkElement)window.FindName("EditorToolbar")).ActualHeight <= 100, "Toolbar must fit two compact rows at the default window width");
        Check(((FrameworkElement)window.FindName("EditorViewport")).ActualHeight >= 570, "Rearranged toolbar must leave more space for the image");
        Check(Buttons(surface).Single(button => button.Tag as string == "Arrow").Padding == new Thickness(13, 9, 13, 9)
            && ((ComboBox)window.FindName("ArrowStyleBox")).Width == 215 && ((Slider)window.FindName("StrokeSlider")).Width == 75, "Toolbar compaction must preserve existing control sizes");
        byte[] originalFile = File.ReadAllBytes(Path.Combine(store.Folder(entry), "original.png"));
        byte[] previewFile = File.ReadAllBytes(Path.Combine(store.Folder(entry), "preview.png"));
        var full = AnnotationRenderer.Render(image, entry.Marks);
        window.SetDisplayZoom(0.0001); Check(((ScaleTransform)window.FindName("EditorScale")).ScaleX == 0.01, "Manual zoom must support very large images down to one percent");
        window.SetDisplayZoom(100); Check(((ScaleTransform)window.FindName("EditorScale")).ScaleX == 8, "Zoom must clamp excessive magnification");
        window.SetDisplayZoom(2); Layout();
        Check(((ScaleTransform)window.FindName("EditorScale")).ScaleX == 2, "Zoom must apply a display transform");
        window.CopyEntryAsync(entry).GetAwaiter().GetResult();
        Check(copied is { PixelWidth: 640, PixelHeight: 360 } && Pixels(copied).SequenceEqual(Pixels(full)), "Display zoom must not alter image export pixels or dimensions");
        Check(File.ReadAllBytes(Path.Combine(store.Folder(entry), "preview.png")).SequenceEqual(previewFile), "Zoom must not rewrite stored preview data");
        var crop = new CropRegion(80, 40, 320, 220);
        var expected = new CroppedBitmap(full, new Int32Rect(80, 40, 320, 220));
        Check(Pixels(AnnotationRenderer.Render(image, entry.Marks, crop)).SequenceEqual(Pixels(expected)), "Cropping must match the original rendered pixels, including text wrapping and arrow gradients");
        Check(ImageCrop.FromPoints(new(450, 260), new(90.2, 30.1), new Int32Rect(80, 40, 320, 220)) == new CropRegion(90, 40, 310, 220), "Crop drags must handle reverse direction, pixel rounding and current-view boundaries");
        window.ApplyCrop(crop); Layout();
        var editor = (Canvas)window.FindName("Editor");
        Check(entry.Crop == crop && editor.Width == 320 && editor.Height == 220 && entry.Marks[0].X == 100, "Cropping must change the view while preserving original annotation coordinates");
        Check(store.Load()[0].Crop == crop && store.Original(entry).PixelWidth == 640, "Crop metadata must persist without overwriting original pixels");
        Click("↶"); Check(entry.Crop is null && editor.Width == 640, "Undo must restore the uncropped image");
        Click("↷"); Check(entry.Crop == crop && editor.Width == 320, "Redo must restore the cropped image");
        window.SelectAnnotation(0); Layout();
        Check(Canvas.GetLeft(editor.Children[^1]) == 254 && Canvas.GetTop(editor.Children[^1]) == 174, "Annotation handles must align with cropped image coordinates");
        window.SetDisplayZoom(4); window.CopyEntryAsync(entry).GetAwaiter().GetResult();
        Check(copied is { PixelWidth: 320, PixelHeight: 220 } && Pixels(copied).SequenceEqual(Pixels(expected)), "Clipboard output must honor cropping and ignore display magnification/selection handles");
        var drag = new CaptureDragExport(store).Create(entry);
        var paths = drag.GetData(DataFormats.FileDrop) as string[] ?? throw new InvalidOperationException("No drag file");
        var dragPixels = Pixels(CaptureStore.ReadImage(paths[0])); var expectedPixels = Pixels(expected);
        // PNG converts premultiplied alpha to straight alpha; an 8-bit channel may round by one.
        Check(dragPixels.Length == expectedPixels.Length && dragPixels.Zip(expectedPixels, (a, b) => Math.Abs(a - b)).All(difference => difference <= 1), "Drag exports must honor cropped pixels and dimensions");
        window.ApplyCrop(new(100, 80, 200, 120)); Check(editor.Width == 200, "Repeated cropping must use original-image coordinates");
        Click("↶"); Check(entry.Crop == crop, "Undoing a second crop must recover the first crop");
        bool invalid = false; try { window.ApplyCrop(new(0, 0, 640, 360)); } catch (ArgumentException) { invalid = true; }
        Check(invalid && entry.Crop == crop, "A crop must not extend beyond the currently visible region");
        Buttons(surface).Single(button => button.Tag as string == "Crop").RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));
        Check(((Border)window.FindName("CropActions")).Visibility == Visibility.Visible && !((System.Windows.Controls.Button)window.FindName("ApplyCropButton")).IsEnabled, "Crop mode must request a range before applying");
        Click("キャンセル"); Check(((Border)window.FindName("CropActions")).Visibility == Visibility.Collapsed && entry.Crop == crop, "Cancelling crop selection must leave the saved image untouched");
        window.SelectAnnotation(0); ((Slider)window.FindName("StrokeSlider")).Value = 9;
        Check(store.Load()[0].Marks[0].Width == 9 && store.Load()[0].Crop == crop, "Annotations must remain editable after cropping");
        Layout(1000); Check(((ComboBox)window.FindName("ArrowStyleBox")).ActualWidth == 215, "Narrow layouts must wrap controls without shrinking them");
        Layout(); window.SetDisplayZoom(2); Layout();
        var preview = new RenderTargetBitmap(1280, 800, 96, 96, PixelFormats.Pbgra32); preview.Render(surface);
        CaptureStore.WriteImage(Path.Combine("artifacts", "editor-zoom-preview.png"), preview);
        Check(File.ReadAllBytes(Path.Combine(store.Folder(entry), "original.png")).SequenceEqual(originalFile), "All crop/edit/zoom operations must retain the original source file");
        window.RequestExit(); window = new MainWindow(store, _ => Task.CompletedTask, false);
        editor = (Canvas)window.FindName("Editor");
        Check(editor.Width == 320 && editor.Height == 220 && store.Load()[0].Marks[0].Width == 9, "Crop and editable annotations must survive reopening the app");
        window.RequestExit();
    }
}
