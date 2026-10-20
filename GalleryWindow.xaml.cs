using System.Collections.ObjectModel;
using System.Windows;

namespace Prtsc;

public partial class GalleryWindow : Window
{
    private readonly Func<CaptureEntry, Task> copy;
    private readonly Action<CaptureEntry> edit;
    private readonly Action capture;
    private readonly Action? settings;
    private readonly Action? record;
    private readonly ThumbnailDrag? thumbnailDrag;
    private readonly ObservableCollection<CaptureEntry> entries;
    private int columns = 3;
    internal GalleryWindow(ObservableCollection<CaptureEntry> entries, Func<CaptureEntry, Task> copyImage, Action<CaptureEntry> editImage, Action startCapture, Func<CaptureEntry, DataObject>? export = null, Action? showSettings = null, Action? startRecording = null)
    {
        InitializeComponent(); Icon = AppIcon.Source;
        this.entries = entries; RebuildRows();
        entries.CollectionChanged += EntriesChanged;
        Cards.SizeChanged += (_, _) =>
        {
            int count = Math.Max(1, (int)((Cards.ActualWidth - 20) / 234));
            if (count != columns) { columns = count; RebuildRows(); }
        };
        Closed += (_, _) => { entries.CollectionChanged -= EntriesChanged; Cards.ItemsSource = null; };
        copy = copyImage; edit = editImage; capture = startCapture; settings = showSettings; record = startRecording;
        if (export is not null)
        {
            thumbnailDrag = new ThumbnailDrag(export, ex => GalleryStatus.Text = "ドラッグできませんでした · " + ex.Message);
            thumbnailDrag.Attach(Cards);
        }
    }
    private void EntriesChanged(object? sender, System.Collections.Specialized.NotifyCollectionChangedEventArgs e) => RebuildRows();
    private void RebuildRows() => Cards.ItemsSource = entries.Chunk(columns).ToList();
    private async void CopyClick(object sender, RoutedEventArgs e)
    {
        var button = (System.Windows.Controls.Button)sender;
        if (button.Tag is not CaptureEntry entry) return;
        button.IsEnabled = false;
        try { await copy(entry); GalleryStatus.Text = (entry.IsVideo ? "動画ファイル" : "画像") + "をコピーしました · " + entry.Title; }
        catch (Exception ex) { GalleryStatus.Text = "コピーできませんでした · " + ex.Message; }
        finally { button.IsEnabled = true; }
    }
    private void EditClick(object sender, RoutedEventArgs e)
    {
        var button = (System.Windows.Controls.Button)sender;
        if (button.Content is System.Windows.Controls.Image && thumbnailDrag?.SuppressClick == true) return;
        if (button.Tag is CaptureEntry entry) edit(entry);
    }
    private void CaptureClick(object sender, RoutedEventArgs e) => capture();
    private void SettingsClick(object sender, RoutedEventArgs e) => settings?.Invoke();
    private void RecordClick(object sender, RoutedEventArgs e) => record?.Invoke();
}
