using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Text.Json;
using System.Windows;
using DrawingRect = System.Drawing.Rectangle;

namespace Prtsc;

internal static class BrowserPageCaptureTests
{
    private static void Check(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
    internal static void Run(string root)
    {
        var browser = new WindowTarget(new IntPtr(1), "Browser", new DrawingRect(-1000, 100, 800, 600), [new(-990, 220, 780, 470)]);
        var behind = new WindowTarget(new IntPtr(2), "Behind", new DrawingRect(-1200, 0, 1600, 1000), [new(-1190, 120, 1580, 870)]);
        Check(WindowCapture.At([browser, behind], -500, 300) is { IsPage: true, Bounds.Width: 780 }, "Hovering content must select its page region");
        Check(WindowCapture.At([browser, behind], -500, 150) == browser, "Browser toolbar must select the whole window, never the page behind it");
        Check(WindowCapture.At([browser, behind], -500, 300, false) == browser, "Alt override must select the full browser window");
        Check(WindowCapture.At([browser, behind], -1100, 500) is { Handle: var h, IsPage: true } && h == behind.Handle, "Exposed background pages must remain selectable");
        var front = new WindowTarget(new IntPtr(3), "Other app", new DrawingRect(-600, 200, 200, 200));
        Check(WindowCapture.At([front, browser], -500, 300) == front, "Pages must never bypass the foremost overlapping window");
        var split = browser with { PageBounds = [new(-990, 220, 380, 470), new(-600, 220, 390, 470)] };
        Check(WindowCapture.At([split], -500, 300) is { IsPage: true, Bounds.X: -600 }, "Separate outer page regions must be selected by pointer position");
        Check(WindowCapture.At([browser with { PageBounds = null }], -500, 300)?.IsPage == false, "Missing page information must preserve window selection");
        Check(WindowCapture.CropBounds(WindowCapture.At([browser], -500, 300)!.Bounds, new(-1920, 0, 3840, 1080)) == new Int32Rect(930, 220, 780, 470), "Page crops must use physical coordinates with negative monitor origins");

        BrowserPageRegion[] pages = [new(1, -1010, 200, 820, 600), new(2, 0, 0, 10, 10)];
        var attached = WindowCapture.AttachPages([browser], [browser], pages);
        Check(attached[0].PageBounds!.Single() == new DrawingRect(-1000, 200, 800, 500), "Page candidates must be clipped to their owning window");
        Check(WindowCapture.AttachPages([browser with { Bounds = new(-900, 100, 800, 600), PageBounds = null }], [browser], pages)[0].PageBounds is null, "Moved windows must discard stale page geometry");
        Check(WindowCapture.AttachPages([browser with { Title = "Changed tab", PageBounds = null }], [browser], pages)[0].PageBounds is null, "Changed tabs must discard stale page geometry");
        Check(BrowserPageCapture.IsPageHierarchy([(false, "View"), (false, "MultiContentsView")]), "Chrome/Edge content ancestry must identify a page");
        Check(!BrowserPageCapture.IsPageHierarchy([(true, ""), (false, "MultiContentsView")]), "Nested iframe documents must not replace the outer page");
        Check(!BrowserPageCapture.IsPageHierarchy([(false, "LocationBarView"), (false, "ToolbarView")]), "Browser toolbar documents must never be treated as page content");
        Check(BrowserPageCapture.TryBounds(new Rect(-1000.5, 150.25, 700.1, 400.5), out var fractional)
            && fractional == new DrawingRect(-1001, 150, 701, 401), "Fractional accessibility bounds must round outward without losing pixels");
        Check(!BrowserPageCapture.TryBounds(Rect.Empty, out _) && !BrowserPageCapture.TryBounds(new Rect(0, 0, 1, 1), out _), "Empty or tiny accessibility nodes must not become page targets");
        var parsed = BrowserPageCapture.Parse(JsonSerializer.Serialize(pages[0]) + "\ninvalid\n" + JsonSerializer.Serialize(pages[1]) + "\n{", [1]);
        Check(parsed.Count == 1 && parsed[0] == pages[0], "Partial helper output must preserve valid regions and reject unknown handles or corrupt lines");
        Check(BrowserPageCapture.ReadAsync([]).GetAwaiter().GetResult().Count == 0, "No-browser capture must not start an accessibility helper");

        // Exercise the actual overlay's coordinate mapping and highlight with a
        // synthetic screenshot; do not capture the desktop or write to the clipboard.
        var desktop = System.Windows.Forms.SystemInformation.VirtualScreen;
        var original = System.Windows.Media.Imaging.BitmapSource.Create(640, 360, 96, 96, System.Windows.Media.PixelFormats.Bgra32, null, new byte[640 * 360 * 4], 640 * 4);
        var target = new WindowTarget(new IntPtr(7), "Test browser", new(desktop.X + 10, desktop.Y + 10, 620, 340), [new(desktop.X + 20, desktop.Y + 80, 600, 260)]);
        var overlay = new RegionWindow([target], bounds: new(desktop.X, desktop.Y, 640, 360), sampleReader: _ => original);
        var content = (FrameworkElement)overlay.Content; content.Measure(new System.Windows.Size(640, 360)); content.Arrange(new Rect(0, 0, 640, 360)); content.UpdateLayout();
        var flags = BindingFlags.Instance | BindingFlags.NonPublic;
        typeof(RegionWindow).GetMethod("Hover", flags)!.Invoke(overlay, [new System.Windows.Point(100, 150)]);
        var hovered = (WindowTarget?)typeof(RegionWindow).GetField("hovered", flags)!.GetValue(overlay);
        var highlight = (System.Windows.Shapes.Rectangle)typeof(RegionWindow).GetField("selection", flags)!.GetValue(overlay)!;
        Check(hovered?.IsPage == true && highlight.Width == 600 && highlight.Height == 260
            && System.Windows.Controls.Canvas.GetLeft(highlight) == 20 && System.Windows.Controls.Canvas.GetTop(highlight) == 80,
            "The actual overlay must highlight the page at screenshot coordinates");
        typeof(RegionWindow).GetMethod("Hover", flags)!.Invoke(overlay, [new System.Windows.Point(100, 40)]);
        Check(((WindowTarget?)typeof(RegionWindow).GetField("hovered", flags)!.GetValue(overlay))?.IsPage == false && highlight.Height == 340,
            "Moving the pointer onto browser chrome must restore its whole-window highlight");
        overlay.Close();

        // Simulate a provider that emits one valid region and then hangs. The parent
        // must keep that result and terminate the helper rather than leak a thread.
        Directory.CreateDirectory(root); string pidPath = Path.Combine(root, "provider.pid");
        var info = new ProcessStartInfo(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), @"WindowsPowerShell\v1.0\powershell.exe"))
        { UseShellExecute = false, CreateNoWindow = true, WindowStyle = ProcessWindowStyle.Hidden, RedirectStandardInput = true, RedirectStandardOutput = true };
        info.ArgumentList.Add("-NoProfile"); info.ArgumentList.Add("-NonInteractive"); info.ArgumentList.Add("-Command");
        info.ArgumentList.Add("[void][Console]::In.ReadToEnd(); [IO.File]::WriteAllText('" + pidPath.Replace("'", "''") + "', [string]$PID); [Console]::Out.WriteLine('{\"Handle\":1,\"X\":10,\"Y\":20,\"Width\":300,\"Height\":200}'); [Console]::Out.Flush(); Start-Sleep -Seconds 30");
        var watch = Stopwatch.StartNew();
        var partial = BrowserPageCapture.ReadWorkerAsync(info, [1], 3000).GetAwaiter().GetResult();
        Check(watch.ElapsedMilliseconds < 6000 && partial.Count == 1, "A hung helper must time out while preserving completed regions");
        int pid = int.Parse(File.ReadAllText(pidPath)); bool alive;
        try { using var child = Process.GetProcessById(pid); alive = !child.HasExited; } catch (ArgumentException) { alive = false; }
        Check(!alive, "The timed-out helper process must be terminated");
    }
}
