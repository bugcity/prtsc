using System.Collections.ObjectModel;
using System.Windows;

namespace Prtsc;

public partial class GalleryWindow : Window
{
    private readonly Func<CaptureEntry, Task> copy;
    private readonly Action<CaptureEntry> edit;
    private readonly Action capture;
    private readonly Action? settings;
    private readonly ThumbnailDrag? thumbnailDrag;
    internal GalleryWindow(ObservableCollection<CaptureEntry> entries, Func<CaptureEntry, Task> copyImage, Action<CaptureEntry> editImage, Action startCapture, Func<CaptureEntry, DataObject>? export = null, Action? showSettings = null)
    {
        InitializeComponent(); Icon = AppIcon.Source;
        Cards.ItemsSource = entries; copy = copyImage; edit = editImage; capture = startCapture; settings = showSettings;
        if (export is not null)
        {
            thumbnailDrag = new ThumbnailDrag(export, ex => GalleryStatus.Text = "ドラッグできませんでした · " + ex.Message);
            thumbnailDrag.Attach(Cards);
        }
    }
    private async void CopyClick(object sender, RoutedEventArgs e)
    {
        var button = (System.Windows.Controls.Button)sender;
        if (button.Tag is not CaptureEntry entry) return;
        button.IsEnabled = false;
        try { await copy(entry); GalleryStatus.Text = "画像をコピーしました · " + entry.Title; }
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
}
