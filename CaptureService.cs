using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using System.Windows.Threading;
using Point = System.Windows.Point;
using Rectangle = System.Windows.Shapes.Rectangle;

namespace Prtsc;

internal static class CaptureService
{
    public static BitmapSource Desktop()
        => Region(System.Windows.Forms.SystemInformation.VirtualScreen);

    public static BitmapSource Region(System.Drawing.Rectangle bounds)
    {
        if (bounds.Width <= 0 || bounds.Height <= 0) throw new ArgumentOutOfRangeException(nameof(bounds));
        using var bitmap = new System.Drawing.Bitmap(bounds.Width, bounds.Height);
        using (var graphics = System.Drawing.Graphics.FromImage(bitmap))
            graphics.CopyFromScreen(bounds.X, bounds.Y, 0, 0, bounds.Size, System.Drawing.CopyPixelOperation.SourceCopy);
        var handle = bitmap.GetHbitmap();
        try
        {
            var source = Imaging.CreateBitmapSourceFromHBitmap(handle, IntPtr.Zero, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
            source.Freeze(); return source;
        }
        finally { DeleteObject(handle); }
    }
    internal static async Task<BitmapSource> AfterSelectionAsync(System.Drawing.Rectangle bounds)
    {
        // The selector has closed. Let the compositor remove its frame and loupe
        // before sampling current pixels, rather than cropping a pre-selection image.
        await Task.Delay(60).ConfigureAwait(false);
        var clipped = System.Drawing.Rectangle.Intersect(bounds, System.Windows.Forms.SystemInformation.VirtualScreen);
        return Region(clipped);
    }
    [DllImport("gdi32.dll")] private static extern bool DeleteObject(IntPtr handle);
}

internal sealed class RegionWindow : Window
{
    private readonly Canvas surface = new();
    private readonly Rectangle selectionOutline = new() { Stroke = Brushes.Black, StrokeThickness = 4, IsHitTestVisible = false };
    private readonly Rectangle selection = new() { Stroke = Brushes.White, StrokeThickness = 2, Fill = new SolidColorBrush(System.Windows.Media.Color.FromArgb(25, 99, 102, 241)) };
    private readonly IReadOnlyList<WindowTarget> targets;
    private readonly System.Drawing.Rectangle desktopBounds;
    private readonly Func<System.Drawing.Rectangle, BitmapSource> sampleReader;
    private readonly DispatcherTimer sampleTimer;
    private bool canSample;
    private Point loupePoint;
    private readonly TextBlock hint = new() { Foreground = Brushes.White, FontSize = 15, TextAlignment = TextAlignment.Center, TextWrapping = TextWrapping.Wrap };
    private readonly CaptureLoupe loupe = new();
    private bool windowOnly;
    private WindowTarget? hovered, pressedTarget;
    private Point start;
    private bool dragging;
    public string? SelectedTitle { get; private set; }
    public Int32Rect? SelectedBounds { get; private set; }
    public System.Drawing.Rectangle? SelectedScreenBounds => SelectedBounds is { } area
        ? new(desktopBounds.X + area.X, desktopBounds.Y + area.Y, area.Width, area.Height) : null;
    public RegionWindow(IReadOnlyList<WindowTarget>? windowTargets = null, bool selectWindow = false,
        System.Drawing.Rectangle? bounds = null, Func<System.Drawing.Rectangle, BitmapSource>? sampleReader = null)
    {
        desktopBounds = bounds ?? System.Windows.Forms.SystemInformation.VirtualScreen;
        this.sampleReader = sampleReader ?? CaptureService.Region;
        sampleTimer = new DispatcherTimer(DispatcherPriority.Background, Dispatcher) { Interval = TimeSpan.FromMilliseconds(80) };
        sampleTimer.Tick += SampleLoupe;
        targets = windowTargets ?? []; windowOnly = selectWindow;
        WindowStyle = WindowStyle.None; ResizeMode = ResizeMode.NoResize; Topmost = true;
        AllowsTransparency = true;
        // A minimally opaque surface receives selection clicks while DWM keeps
        // presenting the real applications underneath it.
        ShowInTaskbar = false; Background = new SolidColorBrush(System.Windows.Media.Color.FromArgb(1, 0, 0, 0)); Cursor = System.Windows.Input.Cursors.Cross;
        var grid = new Grid();
        grid.Children.Add(surface); surface.Background = Brushes.Transparent;
        surface.Children.Add(selectionOutline); surface.Children.Add(selection);
        surface.Children.Add(loupe);
        grid.Children.Add(new Border { HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(20, 32, 20, 0), Background = new SolidColorBrush(System.Windows.Media.Color.FromRgb(24, 29, 45)), CornerRadius = new CornerRadius(10), Padding = new Thickness(22, 13, 22, 13), IsHitTestVisible = false, Child = hint });
        Content = grid;
        UpdateHint();
        SourceInitialized += (_, _) =>
        {
            // Size the still-hidden HWND before WPF's first show. Showing it from
            // Loaded exposes the default-sized black surface for an initial frame.
            var b = desktopBounds;
            var handle = new WindowInteropHelper(this).Handle;
            canSample = OperatingSystem.IsWindowsVersionAtLeast(10, 0, 19041) && SetWindowDisplayAffinity(handle, 0x11);
            SetWindowPos(handle, new IntPtr(-1), b.X, b.Y, b.Width, b.Height, 0x0010); // SWP_NOACTIVATE; never SWP_SHOWWINDOW
        };
        Loaded += (_, _) =>
        {
            var b = desktopBounds;
            Activate(); Focus();
            UpdateLayout();
            var cursor = System.Windows.Forms.Cursor.Position;
            Hover(new Point((cursor.X - b.X) * surface.ActualWidth / b.Width, (cursor.Y - b.Y) * surface.ActualHeight / b.Height));
            if (canSample) sampleTimer.Start();
        };
        Closed += (_, _) => { sampleTimer.Stop(); sampleTimer.Tick -= SampleLoupe; loupe.ClearSample(); };
        PreviewKeyDown += (_, e) =>
        {
            var key = e.Key == Key.System ? e.SystemKey : e.Key;
            if (key is Key.LeftAlt or Key.RightAlt)
            { Hover(System.Windows.Input.Mouse.GetPosition(surface)); e.Handled = true; return; }
            if (e.Key == Key.Escape) { e.Handled = true; Close(); return; }
            if (e.Key == Key.Tab)
            {
                dragging = false; surface.ReleaseMouseCapture(); windowOnly = !windowOnly;
                Hover(System.Windows.Input.Mouse.GetPosition(surface)); e.Handled = true;
            }
        };
        PreviewKeyUp += (_, e) =>
        {
            var key = e.Key == Key.System ? e.SystemKey : e.Key;
            if (key is Key.LeftAlt or Key.RightAlt)
            { Hover(System.Windows.Input.Mouse.GetPosition(surface)); e.Handled = true; }
        };
        surface.MouseLeftButtonDown += (_, e) =>
        {
            start = e.GetPosition(surface); Hover(start); pressedTarget = hovered;
            dragging = true; surface.CaptureMouse();
        };
        surface.MouseMove += (_, e) =>
        {
            var point = e.GetPosition(surface);
            if (dragging && !windowOnly && (point - start).Length >= 4) Update(point);
            else if (!dragging || windowOnly) Hover(point);
            else UpdateLoupe(point, Int32Rect.Empty);
        };
        surface.MouseLeftButtonUp += (_, e) =>
        {
            if (!dragging) return;
            dragging = false; surface.ReleaseMouseCapture();
            var end = e.GetPosition(surface);
            SelectedTitle = null;
            Int32Rect rect;
            if (windowOnly || (end - start).Length < 4)
            {
                var target = windowOnly ? FindTarget(end) : pressedTarget;
                if (target is null) { Hover(end); return; }
                rect = WindowCapture.CropBounds(target.Bounds, desktopBounds);
                SelectedTitle = target.Title + (target.IsPage ? "（ページ領域）" : "");
            }
            else rect = PixelRect(start, end, surface.ActualWidth, surface.ActualHeight, desktopBounds.Width, desktopBounds.Height);
            if (rect.Width < 2 || rect.Height < 2) { Hover(end); return; }
            SelectedBounds = rect;
            sampleTimer.Stop(); DialogResult = true;
        };
        surface.LostMouseCapture += (_, _) => { if (dragging) { dragging = false; Hover(System.Windows.Input.Mouse.GetPosition(surface)); } };
    }
    private WindowTarget? FindTarget(Point point)
    {
        if (surface.ActualWidth <= 0 || surface.ActualHeight <= 0) return null;
        int x = desktopBounds.X + (int)Math.Floor(point.X / surface.ActualWidth * desktopBounds.Width);
        int y = desktopBounds.Y + (int)Math.Floor(point.Y / surface.ActualHeight * desktopBounds.Height);
        return WindowCapture.At(targets, x, y, !Keyboard.Modifiers.HasFlag(ModifierKeys.Alt));
    }
    private void Hover(Point point)
    {
        hovered = FindTarget(point);
        var rect = hovered is null ? Int32Rect.Empty : WindowCapture.CropBounds(hovered.Bounds, desktopBounds);
        ShowSelection(rect.IsEmpty ? Rect.Empty : new Rect(rect.X * surface.ActualWidth / desktopBounds.Width,
            rect.Y * surface.ActualHeight / desktopBounds.Height, rect.Width * surface.ActualWidth / desktopBounds.Width,
            rect.Height * surface.ActualHeight / desktopBounds.Height));
        UpdateHint();
        UpdateLoupe(point, rect);
    }
    private void UpdateHint()
    {
        string instructions = windowOnly ? "クリックしてウィンドウ / ページ領域を撮影  ·  Alt でウィンドウ全体  ·  Tab で範囲選択  ·  Esc で中止"
            : "ドラッグで範囲 / クリックでウィンドウ・ページ領域を撮影  ·  Alt でウィンドウ全体  ·  Tab で切替  ·  Esc で中止";
        string title = hovered?.Title ?? "";
        if (title.Length > 60) title = title[..60] + "…";
        hint.Text = instructions + (title.Length == 0 ? "" : "\n" + (hovered?.IsPage == true ? "ページ領域 · " : "ウィンドウ · ") + title);
    }
    private void Update(Point end)
    {
        ShowSelection(new Rect(start, end));
        var rect = PixelRect(start, end, surface.ActualWidth, surface.ActualHeight, desktopBounds.Width, desktopBounds.Height);
        UpdateLoupe(end, rect);
    }
    private void ShowSelection(Rect bounds)
    {
        // Share the exact geometry: contrast must never change the captured area.
        foreach (var frame in new[] { selectionOutline, selection })
        {
            frame.Visibility = bounds.IsEmpty ? Visibility.Collapsed : Visibility.Visible;
            if (bounds.IsEmpty) continue;
            Canvas.SetLeft(frame, bounds.X); Canvas.SetTop(frame, bounds.Y);
            frame.Width = bounds.Width; frame.Height = bounds.Height;
        }
    }
    private void UpdateLoupe(Point point, Int32Rect rect)
    {
        if (surface.ActualWidth <= 0 || surface.ActualHeight <= 0) return;
        loupePoint = point;
        double sx = surface.ActualWidth / desktopBounds.Width, sy = surface.ActualHeight / desktopBounds.Height;
        var position = new System.Drawing.Point(desktopBounds.X + (int)(point.X / sx), desktopBounds.Y + (int)(point.Y / sy));
        var monitor = System.Windows.Forms.Screen.FromPoint(position).Bounds;
        loupe.UpdateLabel(point, surface.ActualWidth, surface.ActualHeight, desktopBounds.Width, desktopBounds.Height, rect,
            new Rect((monitor.X - desktopBounds.X) * sx, (monitor.Y - desktopBounds.Y) * sy, monitor.Width * sx, monitor.Height * sy));
    }
    private void SampleLoupe(object? sender, EventArgs e)
    {
        if (!canSample || !IsVisible || surface.ActualWidth <= 0 || surface.ActualHeight <= 0) return;
        int x = Math.Clamp((int)(loupePoint.X / surface.ActualWidth * desktopBounds.Width), 0, desktopBounds.Width - 1);
        int y = Math.Clamp((int)(loupePoint.Y / surface.ActualHeight * desktopBounds.Height), 0, desktopBounds.Height - 1);
        var sample = CaptureLoupe.SampleRect(x, y, desktopBounds.Width, desktopBounds.Height);
        try { loupe.UpdateSample(sampleReader(new(desktopBounds.X + sample.X, desktopBounds.Y + sample.Y, sample.Width, sample.Height)), x - sample.X, y - sample.Y); }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or ArgumentException or InvalidOperationException)
        { canSample = false; sampleTimer.Stop(); loupe.ClearSample(); }
    }
    internal static Int32Rect PixelRect(Point start, Point end, double width, double height, int pixelsX, int pixelsY)
    {
        int x = Math.Clamp((int)Math.Floor(Math.Min(start.X, end.X) / width * pixelsX), 0, pixelsX);
        int y = Math.Clamp((int)Math.Floor(Math.Min(start.Y, end.Y) / height * pixelsY), 0, pixelsY);
        int right = Math.Clamp((int)Math.Ceiling(Math.Max(start.X, end.X) / width * pixelsX), 0, pixelsX);
        int bottom = Math.Clamp((int)Math.Ceiling(Math.Max(start.Y, end.Y) / height * pixelsY), 0, pixelsY);
        return new Int32Rect(x, y, right - x, bottom - y);
    }
    [DllImport("user32.dll")] private static extern bool SetWindowPos(IntPtr hwnd, IntPtr after, int x, int y, int width, int height, uint flags);
    [DllImport("user32.dll")] private static extern bool SetWindowDisplayAffinity(IntPtr hwnd, uint affinity);
}
