using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using Point = System.Windows.Point;
using Rectangle = System.Windows.Shapes.Rectangle;

namespace Prtsc;

internal static class CaptureService
{
    public static BitmapSource Desktop()
    {
        var bounds = System.Windows.Forms.SystemInformation.VirtualScreen;
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
    [DllImport("gdi32.dll")] private static extern bool DeleteObject(IntPtr handle);
}

internal sealed class RegionWindow : Window
{
    private readonly Canvas surface = new();
    private readonly Rectangle selection = new() { Stroke = Brushes.White, StrokeThickness = 2, Fill = new SolidColorBrush(System.Windows.Media.Color.FromArgb(25, 99, 102, 241)) };
    private readonly BitmapSource screenshot;
    private readonly IReadOnlyList<WindowTarget> targets;
    private readonly System.Drawing.Rectangle desktopBounds = System.Windows.Forms.SystemInformation.VirtualScreen;
    private readonly TextBlock hint = new() { Foreground = Brushes.White, FontSize = 15, TextAlignment = TextAlignment.Center, TextWrapping = TextWrapping.Wrap };
    private readonly CaptureLoupe loupe = new();
    private bool windowOnly;
    private WindowTarget? hovered, pressedTarget;
    private Point start;
    private bool dragging;
    public BitmapSource? Result { get; private set; }
    public string? SelectedTitle { get; private set; }
    public Int32Rect? SelectedBounds { get; private set; }
    public RegionWindow(BitmapSource source, IReadOnlyList<WindowTarget>? windowTargets = null, bool selectWindow = false)
    {
        screenshot = source;
        targets = windowTargets ?? []; windowOnly = selectWindow;
        WindowStyle = WindowStyle.None; ResizeMode = ResizeMode.NoResize; Topmost = true;
        ShowInTaskbar = false; Background = Brushes.Black; Cursor = System.Windows.Input.Cursors.Cross;
        var grid = new Grid();
        grid.Children.Add(new System.Windows.Controls.Image { Source = source, Stretch = Stretch.Fill });
        grid.Children.Add(new Border { Background = new SolidColorBrush(System.Windows.Media.Color.FromArgb(65, 0, 0, 0)), IsHitTestVisible = false });
        grid.Children.Add(surface); surface.Background = Brushes.Transparent; surface.Children.Add(selection);
        surface.Children.Add(loupe);
        grid.Children.Add(new Border { HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(20, 32, 20, 0), Background = new SolidColorBrush(System.Windows.Media.Color.FromRgb(24, 29, 45)), CornerRadius = new CornerRadius(10), Padding = new Thickness(22, 13, 22, 13), IsHitTestVisible = false, Child = hint });
        Content = grid;
        UpdateHint();
        Loaded += (_, _) =>
        {
            var b = desktopBounds;
            SetWindowPos(new WindowInteropHelper(this).Handle, new IntPtr(-1), b.X, b.Y, b.Width, b.Height, 0x0040);
            Activate(); Focus();
            UpdateLayout();
            var cursor = System.Windows.Forms.Cursor.Position;
            Hover(new Point((cursor.X - b.X) * surface.ActualWidth / b.Width, (cursor.Y - b.Y) * surface.ActualHeight / b.Height));
        };
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
            else rect = PixelRect(start, end, surface.ActualWidth, surface.ActualHeight, source.PixelWidth, source.PixelHeight);
            if (rect.Width < 2 || rect.Height < 2) { Hover(end); return; }
            SelectedBounds = rect;
            Result = new CroppedBitmap(screenshot, rect); Result.Freeze(); DialogResult = true;
        };
        surface.LostMouseCapture += (_, _) => { if (dragging) { dragging = false; Hover(System.Windows.Input.Mouse.GetPosition(surface)); } };
    }
    private WindowTarget? FindTarget(Point point)
    {
        if (surface.ActualWidth <= 0 || surface.ActualHeight <= 0) return null;
        int x = desktopBounds.X + (int)Math.Floor(point.X / surface.ActualWidth * screenshot.PixelWidth);
        int y = desktopBounds.Y + (int)Math.Floor(point.Y / surface.ActualHeight * screenshot.PixelHeight);
        return WindowCapture.At(targets, x, y, !Keyboard.Modifiers.HasFlag(ModifierKeys.Alt));
    }
    private void Hover(Point point)
    {
        hovered = FindTarget(point);
        var rect = hovered is null ? Int32Rect.Empty : WindowCapture.CropBounds(hovered.Bounds, desktopBounds);
        selection.Visibility = rect.IsEmpty ? Visibility.Collapsed : Visibility.Visible;
        if (!rect.IsEmpty)
        {
            Canvas.SetLeft(selection, rect.X * surface.ActualWidth / screenshot.PixelWidth);
            Canvas.SetTop(selection, rect.Y * surface.ActualHeight / screenshot.PixelHeight);
            selection.Width = rect.Width * surface.ActualWidth / screenshot.PixelWidth;
            selection.Height = rect.Height * surface.ActualHeight / screenshot.PixelHeight;
        }
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
        selection.Visibility = Visibility.Visible;
        Canvas.SetLeft(selection, Math.Min(start.X, end.X)); Canvas.SetTop(selection, Math.Min(start.Y, end.Y));
        selection.Width = Math.Abs(end.X - start.X); selection.Height = Math.Abs(end.Y - start.Y);
        var rect = PixelRect(start, end, surface.ActualWidth, surface.ActualHeight, screenshot.PixelWidth, screenshot.PixelHeight);
        UpdateLoupe(end, rect);
    }
    private void UpdateLoupe(Point point, Int32Rect rect)
    {
        if (surface.ActualWidth <= 0 || surface.ActualHeight <= 0) return;
        double sx = surface.ActualWidth / screenshot.PixelWidth, sy = surface.ActualHeight / screenshot.PixelHeight;
        var position = new System.Drawing.Point(desktopBounds.X + (int)(point.X / sx), desktopBounds.Y + (int)(point.Y / sy));
        var monitor = System.Windows.Forms.Screen.FromPoint(position).Bounds;
        loupe.Update(screenshot, point, surface.ActualWidth, surface.ActualHeight, rect,
            new Rect((monitor.X - desktopBounds.X) * sx, (monitor.Y - desktopBounds.Y) * sy, monitor.Width * sx, monitor.Height * sy));
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
}
