using System.Runtime.InteropServices;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Prtsc;

internal static class CaptureOverlayTests
{
    internal static void Run()
    {
        var image = BitmapSource.Create(64, 64, 96, 96, PixelFormats.Bgr32, null, new byte[64 * 64 * 4], 64 * 4);
        var overlay = new RegionWindow(image);
        try
        {
            var handle = new WindowInteropHelper(overlay).EnsureHandle();
            var desktop = System.Windows.Forms.SystemInformation.VirtualScreen;
            if (!GetWindowRect(handle, out var bounds) || bounds.Left != desktop.Left || bounds.Top != desktop.Top
                || bounds.Right != desktop.Right || bounds.Bottom != desktop.Bottom)
                throw new InvalidOperationException($"Capture overlay must cover the virtual desktop before its first show: got {bounds.Left},{bounds.Top},{bounds.Right},{bounds.Bottom}; expected {desktop}");
            if (IsWindowVisible(handle)) throw new InvalidOperationException("Preparing capture bounds must not show the window");
        }
        finally { overlay.Close(); }
    }
    [StructLayout(LayoutKind.Sequential)] private struct NativeRect { public int Left, Top, Right, Bottom; }
    [DllImport("user32.dll")] private static extern bool GetWindowRect(IntPtr window, out NativeRect rect);
    [DllImport("user32.dll")] private static extern bool IsWindowVisible(IntPtr window);
}
