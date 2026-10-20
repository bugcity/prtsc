using System.Collections.ObjectModel;
using System.Windows;

namespace Prtsc;

public partial class GalleryWindow : Window
{
    private readonly Func<CaptureEntry, Task> copy;
    private readonly Action<CaptureEntry> edit;
    private readonly Action capture;
    internal GalleryWindow(ObservableCollection<CaptureEntry> entries, Func<CaptureEntry, Task> copyImage, Action<CaptureEntry> editImage, Action startCapture)
    {
        InitializeComponent(); Icon = AppIcon.Source;
        Cards.ItemsSource = entries; copy = copyImage; edit = editImage; capture = startCapture;
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
        if (((System.Windows.Controls.Button)sender).Tag is CaptureEntry entry) edit(entry);
    }
    private void CaptureClick(object sender, RoutedEventArgs e) => capture();
}
