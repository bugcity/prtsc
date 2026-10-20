using System.Collections.ObjectModel;
using System.IO;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace Prtsc;

internal static class MemoryFeaturesTests
{
    private const BindingFlags Fields = BindingFlags.Instance | BindingFlags.NonPublic;
    private static void Check(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
    public static void Run(string root, BitmapSource image)
    {
        Directory.CreateDirectory(root);
        var preferences = new CaptureSettingsStore(Path.Combine(root, "settings.json"));
        Check(preferences.Load().RenderingMethod == "Software", "New installs must default to software rendering");
        File.WriteAllText(Path.Combine(root, "settings.json"), "{}");
        Check(preferences.Load().RenderingMethod == "Software", "Existing preferences must migrate to software rendering");
        preferences.Save(new() { RenderingMethod = "Automatic" });
        Check(preferences.Load().RenderingMethod == "Automatic", "Rendering preference must persist");
        File.WriteAllText(Path.Combine(root, "settings.json"), "{\"RenderingMethod\":\"invalid\"}");
        Check(preferences.Load().RenderingMethod == "Software", "Unknown rendering methods must fall back to software");
        var dialog = new SettingsDialog(0, 0, new() { RenderingMethod = "Automatic" });
        Check(dialog.RenderingMethod == "Automatic", "Rendering selector must display saved preferences");
        dialog.RenderingInput.SelectedIndex = 0;
        Check(dialog.RenderingMethod == "Software", "Rendering selector must support software"); dialog.Close();

        var store = new CaptureStore(Path.Combine(root, "captures"));
        var portrait = BitmapSource.Create(40, 2000, 96, 96, PixelFormats.Bgra32, null, new byte[40 * 2000 * 4], 40 * 4);
        for (int i = 0; i < ThumbnailCache.Capacity + 5; i++) store.Create(new() { Title = "portrait-" + i }, portrait);
        var small = new CaptureEntry { Title = "small" };
        store.Create(small, BitmapSource.Create(8, 6, 96, 96, PixelFormats.Bgra32, null, new byte[8 * 6 * 4], 8 * 4));
        Check(store.CachedThumbnailCount == 0, "Saving captures must not eagerly decode thumbnails");
        var entries = store.Load();
        Check(store.CachedThumbnailCount == 0, "Loading metadata must not decode any thumbnails");
        var evicted = ThumbnailReference(entries[0]);
        LoadThumbnails(entries);
        Check(store.CachedThumbnailCount == ThumbnailCache.Capacity, "Thumbnail cache must remain bounded");
        Collect(); Collect(); Check(!evicted.IsAlive, "Entries must not retain thumbnails evicted from the cache");
        var edited = entries[^1]; var previous = edited.Thumbnail;
        store.Save(edited, image);
        Check(!ReferenceEquals(previous, edited.Thumbnail), "Saving edits must invalidate the cached preview");
        store.Delete(edited); Check(edited.Thumbnail is null, "Deleting a capture must release its thumbnail provider");

        int requests = 0;
        var largeLibrary = new ObservableCollection<CaptureEntry>(Enumerable.Range(0, 10000).Select(i =>
        {
            var entry = new CaptureEntry { Title = "virtual-" + i };
            entry.SetThumbnailLoader(() => { requests++; return image; }); return entry;
        }));
        var gallery = new GalleryWindow(largeLibrary, _ => Task.CompletedTask, _ => { }, () => { });
        var content = (FrameworkElement)gallery.Content;
        content.Measure(new System.Windows.Size(730, 440)); content.Arrange(new Rect(0, 0, 730, 440)); content.UpdateLayout();
        var cards = (ListBox)gallery.FindName("Cards");
        Check(requests > 0 && requests < 100, "A 10,000-item gallery must only realize visible thumbnail rows");
        cards.ScrollIntoView(cards.Items[^1]); content.UpdateLayout();
        Check(requests < 150 && cards.ItemContainerGenerator.ContainerFromIndex(cards.Items.Count - 1) is not null,
            "Scrolling must load the last rows without loading every thumbnail in between");
        gallery.Close();

        var window = new MainWindow(new CaptureStore(Path.Combine(root, "editor")), _ => Task.CompletedTask, false);
        var history = (ListBox)window.FindName("History");
        window.AddCapture(image, "first");
        var document = (AnnotationDocument)typeof(MainWindow).GetField("document", Fields)!.GetValue(window)!;
        for (int i = 0; i < 200; i++) document.Change(marks => marks.Add(new Mark { X = i }));
        window.HideToTray();
        Check(ReferenceEquals(document, typeof(MainWindow).GetField("document", Fields)!.GetValue(window)), "Hiding the editor must preserve its Undo document");
        for (int i = 0; i < 200; i++) Check(document.Undo(), "Undo must retain the complete active editing session");
        Check(!document.Undo(), "All 200 session operations must be undoable");
        var previousOriginal = OriginalReference(window);
        window.AddCapture(image, "second");
        var nextDocument = (AnnotationDocument)typeof(MainWindow).GetField("document", Fields)!.GetValue(window)!;
        Check(!ReferenceEquals(document, nextDocument) && !nextDocument.Undo(), "Switching images must end the previous Undo session");
        Collect(); Collect(); Check(!previousOriginal.IsAlive, "Switching images must release the previous full-size source");
        var original = OriginalReference(window);
        ((ObservableCollection<CaptureEntry>)history.ItemsSource).Clear();
        Check(((Canvas)window.FindName("Editor")).Children.Count == 0, "Empty libraries must clear the editor's image visual");
        Collect(); Collect(); Check(!original.IsAlive, "Empty libraries must release the last full-size source bitmap");
        window.RequestExit();
    }
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static WeakReference ThumbnailReference(CaptureEntry entry) => new(entry.Thumbnail);
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void LoadThumbnails(IEnumerable<CaptureEntry> entries)
    {
        foreach (var entry in entries)
        {
            var thumbnail = entry.Thumbnail ?? throw new InvalidOperationException("Missing lazy thumbnail");
            Check(thumbnail.PixelWidth <= 420 && thumbnail.PixelHeight <= 300, "Thumbnail decoding must bound both axes");
            if (entry.Title == "small") Check(thumbnail.PixelWidth == 8 && thumbnail.PixelHeight == 6, "Small thumbnails must not be upscaled");
        }
    }
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static WeakReference OriginalReference(MainWindow window) => new(typeof(MainWindow).GetField("original", Fields)!.GetValue(window));
    private static void Collect()
    {
        // Diagnostic only: production never forces GC to mask retained references.
        GC.Collect(); GC.WaitForPendingFinalizers();
        var frame = new DispatcherFrame(); Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, new Action(() => frame.Continue = false));
        Dispatcher.PushFrame(frame); GC.Collect();
    }
}
