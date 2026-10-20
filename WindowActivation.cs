using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Threading;

namespace Prtsc;

internal static class WindowActivation
{
    // Call while the clicked window/menu still belongs to this foreground process.
    public static void Show(Window window)
    {
        window.ShowActivated = true;
        if (window.WindowState == WindowState.Minimized) window.WindowState = WindowState.Normal;
        window.Show();
        var handle = new WindowInteropHelper(window).Handle;
        Activate(window, handle);
        // Finish layout and transfer focus after the originating notification/gallery has been dismissed.
        window.Dispatcher.BeginInvoke(DispatcherPriority.Loaded, new Action(() =>
        {
            if (window.IsVisible && window.IsEnabled)
            {
                if (window.WindowState == WindowState.Minimized) window.WindowState = WindowState.Normal;
                Activate(window, handle);
            }
        }));
    }
    private static void Activate(Window window, IntPtr handle)
    {
        if (handle == IntPtr.Zero) return;
        if (IsIconic(handle)) ShowWindow(handle, 9); // SW_RESTORE: raising a minimized HWND alone does not restore it.
        SetWindowPos(handle, IntPtr.Zero, 0, 0, 0, 0, 0x0043); // HWND_TOP, NOMOVE | NOSIZE | SHOWWINDOW
        SetForegroundWindow(handle);
        window.Activate(); window.Focus();
    }
    [DllImport("user32.dll")] private static extern bool SetForegroundWindow(IntPtr handle);
    [DllImport("user32.dll")] private static extern bool IsIconic(IntPtr handle);
    [DllImport("user32.dll")] private static extern bool ShowWindow(IntPtr handle, int command);
    [DllImport("user32.dll")] private static extern bool SetWindowPos(IntPtr handle, IntPtr after, int x, int y, int width, int height, uint flags);
}
