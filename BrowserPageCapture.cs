using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Windows;
using System.Windows.Automation;
using DrawingRect = System.Drawing.Rectangle;

namespace Prtsc;

internal sealed record BrowserPageRegion(long Handle, int X, int Y, int Width, int Height);

internal static class BrowserPageCapture
{
    internal const string WorkerArgument = "--browser-page-regions";
    internal const int TimeoutMilliseconds = 1200;
    private const int MaxWindows = 16, MaxPages = 8;

    // A hung accessibility provider must not freeze capture or leave abandoned UIA
    // threads retaining native resources. A short-lived helper gives us a hard limit.
    internal static Task<List<BrowserPageRegion>> ReadAsync(IReadOnlyList<WindowTarget> windows)
    {
        var handles = windows.Where(w => IsSupported(w.Handle)).Take(MaxWindows).Select(w => w.Handle.ToInt64()).ToArray();
        return handles.Length == 0 ? Task.FromResult(new List<BrowserPageRegion>()) : ReadWorkerAsync(WorkerStartInfo(), handles);
    }
    internal static async Task<List<BrowserPageRegion>> ReadWorkerAsync(ProcessStartInfo startInfo, long[] handles, int timeoutMilliseconds = TimeoutMilliseconds)
    {
        using var process = new Process { StartInfo = startInfo };
        try
        {
            if (!process.Start()) return [];
            var output = process.StandardOutput.ReadToEndAsync();
            var input = SendRequestAsync(process, handles);
            try { await Task.WhenAll(input, process.WaitForExitAsync()).WaitAsync(TimeSpan.FromMilliseconds(timeoutMilliseconds)).ConfigureAwait(false); }
            catch (TimeoutException) { Stop(process); }
            var text = await output.WaitAsync(TimeSpan.FromMilliseconds(300)).ConfigureAwait(false);
            // One flushed line per region preserves completed results if a later
            // browser/provider times out. Only accept responses for requested HWNDs.
            return Parse(text, handles);
        }
        catch (Exception ex) when (ex is IOException or System.ComponentModel.Win32Exception or InvalidOperationException or TimeoutException or JsonException or UnauthorizedAccessException)
        { return []; }
        finally { Stop(process); }
    }
    private static async Task SendRequestAsync(Process process, long[] handles)
    {
        await process.StandardInput.WriteAsync(JsonSerializer.Serialize(handles)).ConfigureAwait(false);
        process.StandardInput.Close();
    }
    private static ProcessStartInfo WorkerStartInfo()
    {
        var info = new ProcessStartInfo(Environment.ProcessPath!)
        {
            UseShellExecute = false, CreateNoWindow = true, WindowStyle = ProcessWindowStyle.Hidden,
            RedirectStandardInput = true, RedirectStandardOutput = true
        };
        if (string.Equals(Path.GetFileNameWithoutExtension(info.FileName), "dotnet", StringComparison.OrdinalIgnoreCase))
            info.ArgumentList.Add(typeof(App).Assembly.Location);
        info.ArgumentList.Add(WorkerArgument); return info;
    }
    private static void Stop(Process process)
    {
        try { if (!process.HasExited) { process.Kill(); process.WaitForExit(300); } }
        catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception) { }
    }
    internal static List<BrowserPageRegion> Parse(string output, IReadOnlyList<long> handles)
    {
        List<BrowserPageRegion> pages = [];
        foreach (string line in output.Split('\n', StringSplitOptions.RemoveEmptyEntries).Take(MaxWindows * MaxPages))
        {
            try
            {
                var page = JsonSerializer.Deserialize<BrowserPageRegion>(line);
                if (page is not null && handles.Contains(page.Handle) && page.Width >= 32 && page.Height >= 32
                    && page.Width <= 100000 && page.Height <= 100000 && Math.Abs((long)page.X) <= 1000000 && Math.Abs((long)page.Y) <= 1000000)
                    pages.Add(page);
            }
            catch (JsonException) { }
        }
        return pages;
    }
    internal static DrawingRect Clip(BrowserPageRegion page, DrawingRect window)
        => DrawingRect.Intersect(window, new DrawingRect(page.X, page.Y, page.Width, page.Height));
    private static bool IsSupported(IntPtr window)
    {
        try
        {
            GetWindowThreadProcessId(window, out uint id);
            using var process = Process.GetProcessById((int)id);
            return process.ProcessName is "chrome" or "msedge";
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or System.ComponentModel.Win32Exception) { return false; }
    }
    internal static void RunWorker()
    {
        var handles = JsonSerializer.Deserialize<long[]>(Console.In.ReadToEnd()) ?? [];
        foreach (long handle in handles.Take(MaxWindows))
        {
            if (!IsSupported(new IntPtr(handle))) continue;
            try
            {
                var root = AutomationElement.FromHandle(new IntPtr(handle));
                var pages = FindPages(root, handle);
                if (pages.Count == 0)
                {
                    // Chromium can enable its accessibility tree in response to the
                    // first query. Give it one bounded chance to publish the document.
                    System.Threading.Thread.Sleep(80); pages = FindPages(root, handle);
                }
                foreach (var page in pages) { Console.Out.WriteLine(JsonSerializer.Serialize(page)); Console.Out.Flush(); }
            }
            catch (Exception ex) when (ex is ElementNotAvailableException or InvalidOperationException or System.ComponentModel.Win32Exception or COMException or UnauthorizedAccessException) { }
        }
    }
    private static List<BrowserPageRegion> FindPages(AutomationElement root, long handle)
    {
        List<BrowserPageRegion> pages = [];
        var documents = root.FindAll(TreeScope.Descendants, new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Document));
        foreach (AutomationElement document in documents)
        {
            if (pages.Count == MaxPages) break;
            var current = document.Current;
            if (current.IsOffscreen || !TryBounds(current.BoundingRectangle, out var bounds)) continue;
            List<(bool Document, string ClassName)> ancestors = [];
            var parent = TreeWalker.ControlViewWalker.GetParent(document);
            for (int depth = 0; parent is not null && parent != root && depth < 32; depth++, parent = TreeWalker.ControlViewWalker.GetParent(parent))
            {
                var ancestor = parent.Current;
                ancestors.Add((ancestor.ControlType == ControlType.Document, ancestor.ClassName));
                if (ancestor.ControlType == ControlType.Document) break;
            }
            // Some browser chrome elements also identify as Document. Require a
            // content ancestor, and exclude iframe documents nested within the page.
            if (IsPageHierarchy(ancestors)) pages.Add(new(handle, bounds.X, bounds.Y, bounds.Width, bounds.Height));
        }
        return pages;
    }
    internal static bool IsPageHierarchy(IReadOnlyList<(bool Document, string ClassName)> ancestors)
        => !ancestors.Any(a => a.Document) && ancestors.Any(a => a.ClassName is "MultiContentsView" or "WebView" or "Chrome_RenderWidgetHostHWND");
    internal static bool TryBounds(Rect rect, out DrawingRect bounds)
    {
        bounds = DrawingRect.Empty;
        if (rect.IsEmpty || !double.IsFinite(rect.X) || !double.IsFinite(rect.Y) || !double.IsFinite(rect.Width) || !double.IsFinite(rect.Height)
            || Math.Abs(rect.X) > 1000000 || Math.Abs(rect.Y) > 1000000 || rect.Width < 32 || rect.Height < 32 || rect.Width > 100000 || rect.Height > 100000) return false;
        bounds = DrawingRect.FromLTRB((int)Math.Floor(rect.Left), (int)Math.Floor(rect.Top), (int)Math.Ceiling(rect.Right), (int)Math.Ceiling(rect.Bottom));
        return true;
    }
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr handle, out uint processId);
}
