using System.Runtime.InteropServices;
using System.Reflection;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace Prtsc;

internal static class CaptureOverlayTests
{
    internal static void Run()
    {
        var overlay = new RegionWindow();
        try
        {
            var handle = new WindowInteropHelper(overlay).EnsureHandle();
            var desktop = System.Windows.Forms.SystemInformation.VirtualScreen;
            if (!GetWindowRect(handle, out var bounds) || bounds.Left != desktop.Left || bounds.Top != desktop.Top
                || bounds.Right != desktop.Right || bounds.Bottom != desktop.Bottom)
                throw new InvalidOperationException($"Capture overlay must cover the virtual desktop before its first show: got {bounds.Left},{bounds.Top},{bounds.Right},{bounds.Bottom}; expected {desktop}");
            if (IsWindowVisible(handle)) throw new InvalidOperationException("Preparing capture bounds must not show the window");
            if (!overlay.AllowsTransparency || overlay.Background is not SolidColorBrush { Color.A: 1 })
                throw new InvalidOperationException("The selector must be transparent rather than display a frozen screenshot");
        }
        finally { overlay.Close(); }
    }
    internal static void RunLive()
    {
        // Draw our own solid-color fixture. Only sample a small rectangle wholly
        // inside it; never persist or inspect unrelated desktop content.
        var fixture = new Window
        {
            Title = "Prtsc live capture test", Width = 320, Height = 240, Left = 40, Top = 40,
            WindowStyle = WindowStyle.None, ResizeMode = ResizeMode.NoResize,
            ShowInTaskbar = false, Background = Brushes.Red, ShowActivated = false
        };
        RegionWindow? overlay = null;
        try
        {
            fixture.Show(); fixture.UpdateLayout(); Pump();
            var handle = new WindowInteropHelper(fixture).Handle; GetWindowRect(handle, out var bounds);
            var fixtureBounds = System.Drawing.Rectangle.FromLTRB(bounds.Left, bounds.Top, bounds.Right, bounds.Bottom);
            var sample = new System.Drawing.Rectangle((bounds.Left + bounds.Right) / 2, (bounds.Top + bounds.Bottom) / 2, 12, 12);
            var first = ColorOf(CaptureService.Region(sample));
            if (first.R < 200 || first.B > 40) throw new InvalidOperationException("Live fixture must be visible before capture");
            int reads = 0, fixtureReads = 0;
            overlay = new RegionWindow(sampleReader: area =>
            {
                reads++;
                if (area.Width > 25 || area.Height > 25) throw new InvalidOperationException("Live loupe must only sample a bounded 25px area");
                if (fixtureBounds.Contains(area)) { fixtureReads++; return CaptureService.Region(area); }
                return BitmapSource.Create(area.Width, area.Height, 96, 96, PixelFormats.Bgr32, null, new byte[area.Width * area.Height * 4], area.Width * 4);
            });
            overlay.Show(); overlay.UpdateLayout();
            var overlayCanvas = (System.Windows.Controls.Canvas)typeof(RegionWindow).GetField("surface", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(overlay)!;
            var desktop = System.Windows.Forms.SystemInformation.VirtualScreen;
            typeof(RegionWindow).GetMethod("Hover", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(overlay,
                [new System.Windows.Point((sample.X - desktop.X) * overlayCanvas.ActualWidth / desktop.Width, (sample.Y - desktop.Y) * overlayCanvas.ActualHeight / desktop.Height)]);
            Pump();
            fixture.Background = Brushes.Blue; fixture.UpdateLayout(); Pump();
            var live = ColorOf(CaptureService.Region(sample));
            if (live.B < 200 || live.R > 40) throw new InvalidOperationException("Changing content behind the selector must remain live");
            bool canSample = (bool)typeof(RegionWindow).GetField("canSample", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(overlay)!;
            if (canSample && (live.R != 0 || live.B != 255)) throw new InvalidOperationException("The live capture must exclude the selector's tint and controls");
            if (canSample && reads == 0)
            {
                var canvas = (System.Windows.Controls.Canvas)typeof(RegionWindow).GetField("surface", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(overlay)!;
                var timer = (DispatcherTimer)typeof(RegionWindow).GetField("sampleTimer", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(overlay)!;
                throw new InvalidOperationException($"Live loupe timer did not sample current pixels: visible={overlay.IsVisible}, loaded={overlay.IsLoaded}, canvas={canvas.ActualWidth}x{canvas.ActualHeight}, timer={timer.IsEnabled}");
            }
            if (canSample)
            {
                var loupe = (CaptureLoupe)typeof(RegionWindow).GetField("loupe", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(overlay)!;
                var magnified = (System.Windows.Controls.Image)typeof(CaptureLoupe).GetField("image", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(loupe)!;
                if (fixtureReads == 0 || magnified.Source is not BitmapSource source || ColorOf(source).B < 200)
                    throw new InvalidOperationException("The magnifier must show the fixture's updated blue pixels");
            }
            overlay.Close(); int stoppedReads = reads; Pump();
            if (reads != stoppedReads) throw new InvalidOperationException("Closing selection must stop live sampling");
            var final = CaptureService.AfterSelectionAsync(sample).GetAwaiter().GetResult();
            var color = ColorOf(final);
            if (color.B < 200 || color.R > 40 || final.PixelWidth != 12 || final.PixelHeight != 12)
                throw new InvalidOperationException("The final capture must contain current pixels, not the pre-selection red frame");
        }
        finally { overlay?.Close(); fixture.Close(); }
    }
    private static Color ColorOf(BitmapSource source)
    {
        var converted = new FormatConvertedBitmap(source, PixelFormats.Bgra32, null, 0);
        byte[] pixel = new byte[4]; converted.CopyPixels(new Int32Rect(0, 0, 1, 1), pixel, 4, 0);
        return Color.FromArgb(pixel[3], pixel[2], pixel[1], pixel[0]);
    }
    private static void Pump()
    {
        var frame = new DispatcherFrame();
        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(160) };
        timer.Tick += (_, _) => { timer.Stop(); frame.Continue = false; }; timer.Start(); Dispatcher.PushFrame(frame);
    }
    [StructLayout(LayoutKind.Sequential)] private struct NativeRect { public int Left, Top, Right, Bottom; }
    [DllImport("user32.dll")] private static extern bool GetWindowRect(IntPtr window, out NativeRect rect);
    [DllImport("user32.dll")] private static extern bool IsWindowVisible(IntPtr window);
}
