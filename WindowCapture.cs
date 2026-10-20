using System.Runtime.InteropServices;
using System.Text;
using System.Windows;

namespace Prtsc;

internal enum CaptureMode { Region, Window, FullScreen }

internal sealed record WindowTarget(IntPtr Handle, string Title, System.Drawing.Rectangle Bounds,
    IReadOnlyList<System.Drawing.Rectangle>? PageBounds = null, bool IsPage = false);

internal static class WindowCapture
{
    // EnumWindows returns top-level windows from front to back. Snapshot before opening the overlay.
    public static List<WindowTarget> Enumerate()
    {
        List<WindowTarget> targets = [];
        EnumWindows((handle, _) =>
        {
            if (!IsWindowVisible(handle) || IsIconic(handle)) return true;
            if (DwmGetInt(handle, 14, out int cloaked, sizeof(int)) == 0 && cloaked != 0) return true;
            var className = new StringBuilder(256); GetClassName(handle, className, className.Capacity);
            if (className.ToString() is "Progman" or "WorkerW" or "Shell_TrayWnd" or "Shell_SecondaryTrayWnd") return true;
            if (DwmGetRect(handle, 9, out var rect, Marshal.SizeOf<NativeRect>()) != 0 && !GetWindowRect(handle, out rect)) return true;
            if (rect.Right <= rect.Left || rect.Bottom <= rect.Top) return true;
            var title = new StringBuilder(512); GetWindowText(handle, title, title.Capacity);
            targets.Add(new WindowTarget(handle, title.Length == 0 ? "ウィンドウ" : title.ToString(),
                System.Drawing.Rectangle.FromLTRB(rect.Left, rect.Top, rect.Right, rect.Bottom)));
            return true;
        }, IntPtr.Zero);
        return targets;
    }

    public static async Task<List<WindowTarget>> EnumerateTargetsAsync()
    {
        var before = Enumerate();
        var pages = await BrowserPageCapture.ReadAsync(before);
        // Providers can take time to initialize. Use fresh z-order and discard regions
        // if their window moved, resized or changed tabs while discovery was running.
        return AttachPages(Enumerate(), before, pages);
    }

    internal static List<WindowTarget> AttachPages(IEnumerable<WindowTarget> current, IReadOnlyList<WindowTarget> before, IReadOnlyList<BrowserPageRegion> pages)
        => current.Select(window => before.Any(old => old.Handle == window.Handle && old.Bounds == window.Bounds && old.Title == window.Title)
            ? window with { PageBounds = pages.Where(p => p.Handle == window.Handle.ToInt64()).Select(p => BrowserPageCapture.Clip(p, window.Bounds)).Where(r => r.Width >= 32 && r.Height >= 32).Distinct().ToArray() }
            : window).ToList();

    public static WindowTarget? At(IEnumerable<WindowTarget> targets, int screenX, int screenY, bool preferPage = true)
    {
        var window = targets.FirstOrDefault(target => target.Bounds.Contains(screenX, screenY));
        if (window is null || !preferPage) return window;
        var page = window.PageBounds?.FirstOrDefault(bounds => bounds.Contains(screenX, screenY));
        return page is { Width: > 0, Height: > 0 } ? window with { Bounds = page.Value, PageBounds = null, IsPage = true } : window;
    }

    public static Int32Rect CropBounds(System.Drawing.Rectangle window, System.Drawing.Rectangle desktop)
    {
        var clipped = System.Drawing.Rectangle.Intersect(window, desktop);
        return clipped.Width <= 0 || clipped.Height <= 0 ? Int32Rect.Empty
            : new Int32Rect(clipped.X - desktop.X, clipped.Y - desktop.Y, clipped.Width, clipped.Height);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRect { public int Left, Top, Right, Bottom; }
    private delegate bool EnumWindowsCallback(IntPtr handle, IntPtr parameter);
    [DllImport("user32.dll")] private static extern bool EnumWindows(EnumWindowsCallback callback, IntPtr parameter);
    [DllImport("user32.dll")] private static extern bool IsWindowVisible(IntPtr handle);
    [DllImport("user32.dll")] private static extern bool IsIconic(IntPtr handle);
    [DllImport("user32.dll")] private static extern bool GetWindowRect(IntPtr handle, out NativeRect rect);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetWindowText(IntPtr handle, StringBuilder text, int count);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetClassName(IntPtr handle, StringBuilder text, int count);
    [DllImport("dwmapi.dll", EntryPoint = "DwmGetWindowAttribute")] private static extern int DwmGetRect(IntPtr handle, int attribute, out NativeRect rect, int size);
    [DllImport("dwmapi.dll", EntryPoint = "DwmGetWindowAttribute")] private static extern int DwmGetInt(IntPtr handle, int attribute, out int value, int size);
}
