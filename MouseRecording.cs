using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Prtsc;

// All sizes are physical output pixels. Input events only observe the mouse and never consume it.
internal sealed class MouseHighlight
{
    private double start = 24, target = 24, transition;
    internal double Diameter(double seconds) { double t = Math.Clamp((seconds - transition) / 0.15, 0, 1); return start + (target - start) * t * t * (3 - 2 * t); }
    internal void SetPressed(bool pressed, double seconds)
    {
        double next = pressed ? 48 : 24; if (next == target) return;
        start = Diameter(seconds); target = next; transition = seconds;
    }
    internal static BitmapSource Sprite()
    {
        var visual = new DrawingVisual();
        using (var drawing = visual.RenderOpen()) drawing.DrawEllipse(new SolidColorBrush(Color.FromArgb(90, 255, 0, 0)), null, new System.Windows.Point(24, 24), 24, 24);
        var bitmap = new RenderTargetBitmap(48, 48, 96, 96, PixelFormats.Pbgra32); bitmap.Render(visual); bitmap.Freeze(); return bitmap;
    }
}

internal sealed class RecordingMouseInput : IDisposable
{
    private readonly HookCallback callback;
    private IntPtr hook;
    private int buttons;
    internal readonly MouseHighlight Highlight = new();
    private readonly Stopwatch clock = Stopwatch.StartNew();
    internal RecordingMouseInput()
    {
        buttons = (GetAsyncKeyState(1) < 0 ? 1 : 0) | (GetAsyncKeyState(2) < 0 ? 2 : 0) | (GetAsyncKeyState(4) < 0 ? 4 : 0)
            | (GetAsyncKeyState(5) < 0 ? 8 : 0) | (GetAsyncKeyState(6) < 0 ? 16 : 0);
        Highlight.SetPressed(buttons != 0, 0);
        callback = Hook;
        hook = SetWindowsHookEx(14, callback, GetModuleHandle(null), 0);
        if (hook == IntPtr.Zero) throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error(), "マウス操作の記録を開始できません。");
    }
    private IntPtr Hook(int code, IntPtr message, IntPtr data)
    {
        if (code >= 0)
        {
            switch (message.ToInt32())
            {
                case 0x201: buttons |= 1; break; case 0x202: buttons &= ~1; break;
                case 0x204: buttons |= 2; break; case 0x205: buttons &= ~2; break;
                case 0x207: buttons |= 4; break; case 0x208: buttons &= ~4; break;
                case 0x20B: buttons |= ((Marshal.ReadInt32(data, 8) >> 16) & 0xFFFF) == 1 ? 8 : 16; break;
                case 0x20C: buttons &= ~(((Marshal.ReadInt32(data, 8) >> 16) & 0xFFFF) == 1 ? 8 : 16); break;
            }
            Highlight.SetPressed(buttons != 0, clock.Elapsed.TotalSeconds);
        }
        return CallNextHookEx(hook, code, message, data);
    }
    internal (int X, int Y, double Diameter) Sample()
    {
        GetCursorPos(out var point); return (point.X, point.Y, Highlight.Diameter(clock.Elapsed.TotalSeconds));
    }
    public void Dispose() { if (hook != IntPtr.Zero) { UnhookWindowsHookEx(hook); hook = IntPtr.Zero; } }
    private delegate IntPtr HookCallback(int code, IntPtr message, IntPtr data);
    [StructLayout(LayoutKind.Sequential)] private struct NativePoint { public int X, Y; }
    [DllImport("user32.dll", SetLastError = true)] private static extern IntPtr SetWindowsHookEx(int id, HookCallback callback, IntPtr module, uint thread);
    [DllImport("user32.dll")] private static extern IntPtr CallNextHookEx(IntPtr hook, int code, IntPtr message, IntPtr data);
    [DllImport("user32.dll")] private static extern bool UnhookWindowsHookEx(IntPtr hook);
    [DllImport("user32.dll")] private static extern bool GetCursorPos(out NativePoint point);
    [DllImport("user32.dll")] private static extern short GetAsyncKeyState(int key);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] private static extern IntPtr GetModuleHandle(string? name);
}
