using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Prtsc;

internal static class LibraryFeaturesTests
{
    public static void Run(string root, BitmapSource image)
    {
        static void Check(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
        var store = new CaptureStore(Path.Combine(root, "captures"));
        var settingsStore = new CaptureSettingsStore(Path.Combine(store.Root, "settings.json"));
        Check(settingsStore.Load().NotificationSeconds == 3, "Missing preferences must retain the three-second notification default");
        settingsStore.Save(new() { NotificationSeconds = 12 });
        Check(settingsStore.Load().NotificationSeconds == 12, "Notification duration must survive reloading preferences");
        settingsStore.Save(new() { NotificationSeconds = 0 });
        Check(settingsStore.Load().NotificationSeconds == 0, "Disabled notifications must survive restarting");
        bool badNotification = false;
        try { settingsStore.Save(new() { NotificationSeconds = -1 }); } catch (ArgumentOutOfRangeException) { badNotification = true; }
        Check(badNotification && settingsStore.Load().NotificationSeconds == 0, "Invalid notification values must not overwrite preferences");
        File.WriteAllText(Path.Combine(store.Root, "settings.json"), "{}");
        Check(settingsStore.Load().NotificationSeconds == 3, "Existing settings without notification duration must default to three seconds");
        File.WriteAllText(Path.Combine(store.Root, "settings.json"), "{\"NotificationSeconds\":61}");
        Check(settingsStore.Load().NotificationSeconds == 3, "Out-of-range stored notification duration must fall back safely");
        settingsStore.Save(new());
        var window = new MainWindow(store, _ => Task.CompletedTask, false);
        var captures = Enumerable.Range(1, 4).Select(i => window.AddCapture(image, "capture-" + i)).ToArray();
        Check(store.Load().Count == 4, "Default retention must preserve all captures");
        window.ApplyCaptureLimit(2);
        Check(settingsStore.Load().MaxCaptures == 2, "Capture count must persist");
        Check(store.Load().Select(e => e.Title).Order().SequenceEqual(new[] { "capture-3", "capture-4" }), "Lowering the limit must remove the oldest captures immediately");
        Check(!Directory.Exists(store.Folder(captures[0])) && !Directory.Exists(store.Folder(captures[1])), "Retention must remove originals and editable metadata from disk");
        var history = (ListBox)window.FindName("History"); history.SelectedItem = history.Items.OfType<CaptureEntry>().Single(e => e.Title == "capture-3");
        window.AddCapture(image, "capture-5", false);
        Check(store.Load().Count == 2 && history.SelectedItem is null, "Automatic retention must clear a selected capture that was removed");
        window.RequestExit();
        window = new MainWindow(store, _ => Task.CompletedTask, false);
        window.AddCapture(image, "capture-6"); Check(store.Load().Count == 2, "Capture limit must remain active after restarting");
        window.ApplyCaptureLimit(0); window.AddCapture(image, "capture-7");
        Check(store.Load().Count == 3, "Unlimited mode must stop automatic deletion");
        bool invalid = false;
        try { window.ApplyCaptureLimit(-1); } catch (ArgumentOutOfRangeException) { invalid = true; }
        Check(invalid && settingsStore.Load().MaxCaptures == 0, "Invalid limits must not change saved settings");
        bool unsafePath = false;
        try { store.Delete(new CaptureEntry { Id = ".." }); } catch (ArgumentException) { unsafePath = true; }
        Check(unsafePath && Directory.Exists(store.Root), "Automatic deletion must reject paths outside a capture ID folder");
        var entry = window.AddCapture(image, "CON: drag/test?");
        entry.Marks.Add(new Mark { Tool = "Arrow", ArrowStyle = "Tapered", X = 40, Y = 160, X2 = 500, Y2 = 100, Width = 7 });
        store.Save(entry, AnnotationRenderer.Render(image, entry.Marks));
        var export = new CaptureDragExport(store); var data = export.Create(entry);
        var paths = data.GetData(DataFormats.FileDrop) as string[] ?? throw new InvalidOperationException("Missing file drop data");
        Check(paths.Length == 1 && Path.IsPathFullyQualified(paths[0]) && File.Exists(paths[0]), "Drag export must provide a real absolute PNG file path");
        Check(data.GetDataPresent(DataFormats.Bitmap) && data.GetData(CaptureDragExport.CaptureIdFormat) as string == entry.Id, "Drag data must provide bitmap and source identity formats");
        var snapshot = CaptureStore.ReadImage(paths[0]);
        Check(snapshot.PixelWidth == image.PixelWidth && snapshot.PixelHeight == image.PixelHeight, "Drag export must use full image dimensions rather than thumbnails");
        byte[] expected = new byte[image.PixelWidth * image.PixelHeight * 4], actual = new byte[expected.Length];
        AnnotationRenderer.Render(image, entry.Marks).CopyPixels(expected, image.PixelWidth * 4, 0); snapshot.CopyPixels(actual, image.PixelWidth * 4, 0);
        Check(expected.SequenceEqual(actual), "Dragged PNG must include all editable annotations as rendered pixels");
        var firstBytes = File.ReadAllBytes(paths[0]); entry.Marks.Clear();
        store.Save(entry, image); var secondData = export.Create(entry);
        var secondPath = (secondData.GetData(DataFormats.FileDrop) as string[] ?? throw new InvalidOperationException("Missing second file drop data"))[0];
        Check(secondPath != paths[0] && File.ReadAllBytes(paths[0]).SequenceEqual(firstBytes), "Later edits and drags must not overwrite a file handed to another app");
        window.ApplyCaptureLimit(1); window.AddCapture(image, "next");
        Check(!Directory.Exists(store.Folder(entry)) && File.Exists(paths[0]), "Dragged files must survive source capture retention");
        File.SetLastWriteTimeUtc(paths[0], DateTime.UtcNow.AddDays(-2)); export.Cleanup();
        Check(!File.Exists(paths[0]) && File.Exists(secondPath), "Only expired drag snapshots should be cleaned up");
        Check(!ThumbnailDrag.ShouldStart(new(0, 0), new(0.1, 0.1), true)
            && !ThumbnailDrag.ShouldStart(new(0, 0), new(100, 100), false)
            && ThumbnailDrag.ShouldStart(new(0, 0), new(100, 100), true), "Thumbnail clicks and mouse releases must not accidentally begin a drag");
        var dialog = new SettingsDialog(0, 8);
        Check(dialog.NotificationSeconds == 3, "Settings must display the default notification duration");
        dialog.NotificationInput.Text = "0";
        Check(dialog.NotificationSeconds == 0 && dialog.ApplyButton.IsEnabled, "Zero seconds must be a valid notification-off preference");
        foreach (string invalidSeconds in new[] { "-1", "61", "abc", "1.5", "" })
        { dialog.NotificationInput.Text = invalidSeconds; Check(!dialog.ApplyButton.IsEnabled, "Invalid notification duration must disable saving"); }
        dialog.NotificationInput.Text = "12";
        Check(dialog.NotificationSeconds == 12 && dialog.ApplyButton.IsEnabled, "Valid notification duration must re-enable saving");
        Check(dialog.Unlimited.IsChecked == true && dialog.Limit == 0, "Settings must display unlimited mode correctly");
        dialog.Unlimited.IsChecked = false; dialog.CountInput.Text = "3";
        Check(dialog.Limit == 3 && dialog.Summary.Text.Contains("5 枚") && dialog.ApplyButton.IsEnabled, "Settings must disclose how many existing captures will be removed");
        var content = (FrameworkElement)dialog.Content; content.Measure(new System.Windows.Size(440, double.PositiveInfinity));
        double height = Math.Ceiling(content.DesiredSize.Height); content.Arrange(new Rect(0, 0, 440, height)); content.UpdateLayout();
        var background = new DrawingVisual(); using (var drawing = background.RenderOpen()) drawing.DrawRectangle(Brushes.White, null, new Rect(0, 0, 440, height));
        var preview = new RenderTargetBitmap(440, (int)height, 96, 96, PixelFormats.Pbgra32); preview.Render(background); preview.Render(content);
        CaptureStore.WriteImage(Path.Combine("artifacts", "settings-preview.png"), preview);
        var bounds = dialog.ApplyButton.TransformToAncestor(content).TransformBounds(new Rect(dialog.ApplyButton.RenderSize));
        Check(bounds.Bottom <= content.ActualHeight, "Settings actions must fit inside the dialog");
        dialog.CountInput.Text = "0"; Check(!dialog.ApplyButton.IsEnabled, "Zero must require explicit unlimited mode");
        dialog.CountInput.Text = "abc"; Check(!dialog.ApplyButton.IsEnabled, "Non-numeric limits must be rejected");
        dialog.Unlimited.IsChecked = true; Check(dialog.Limit == 0 && dialog.ApplyButton.IsEnabled, "Unlimited mode must not depend on the numeric input");
        dialog.Close(); window.RequestExit();
        File.WriteAllText(Path.Combine(store.Root, "settings.json"), "invalid JSON");
        Check(settingsStore.Load().MaxCaptures == 0, "Corrupt settings must fall back to preserving captures");
    }
}
