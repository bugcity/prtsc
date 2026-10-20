using System.Runtime.InteropServices;
using System.Windows.Media.Imaging;

namespace Prtsc;

internal static class ImageClipboard
{
    public static async Task CopyFilesAsync(System.Windows.DataObject files)
    {
        for (int attempt = 0; ; attempt++)
        {
            try { System.Windows.Clipboard.SetDataObject(files, true); return; }
            catch (ExternalException) when (attempt < 5) { await Task.Delay(80); }
        }
    }
    public static async Task CopyAsync(BitmapSource bitmap)
    {
        // Other applications sometimes hold the clipboard briefly. Keep retries on the STA dispatcher.
        for (int attempt = 0; ; attempt++)
        {
            try { System.Windows.Clipboard.SetImage(bitmap); return; }
            catch (ExternalException) when (attempt < 5) { await Task.Delay(80); }
        }
    }
}
