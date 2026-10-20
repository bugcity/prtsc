using System.Runtime.InteropServices;
using System.Windows.Input;
using Microsoft.Win32;

namespace Prtsc;

internal static class HotkeyBinding
{
    internal static bool IsValid(uint modifiers, uint key) => (modifiers & ~7u) == 0 && key is >= 8 and <= 254
        && key is not (0x10 or 0x11 or 0x12 or 0x5B or 0x5C) && (modifiers != 0 || key == 0x2C || key is >= 0x70 and <= 0x87);
    internal static string Label(uint modifiers, uint key) => ((modifiers & 2) != 0 ? "Ctrl+" : "") + ((modifiers & 1) != 0 ? "Alt+" : "")
        + ((modifiers & 4) != 0 ? "Shift+" : "") + (key == 0x2C ? "PrtSc" : KeyInterop.KeyFromVirtualKey((int)key).ToString());
    internal static bool Register(IntPtr hwnd, int id, uint modifiers, uint key) => RegisterHotKey(hwnd, id, modifiers | 0x4000, key);
    [DllImport("user32.dll", SetLastError = true)] private static extern bool RegisterHotKey(IntPtr hwnd, int id, uint modifiers, uint key);
}

internal static class StartupRegistration
{
    internal static string Command(string executable) => "\"" + executable + "\" --startup";
    internal static void Apply(bool enabled)
    {
        const string name = "Prtsc";
        using var run = Registry.CurrentUser.CreateSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run", true);
        if (enabled)
        {
            var path = Environment.ProcessPath ?? throw new InvalidOperationException("実行ファイルの場所を取得できません。");
            if (!string.Equals(System.IO.Path.GetFileNameWithoutExtension(path), "Prtsc", StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("自動起動は Prtsc.exe から起動して設定してください。");
            run.SetValue(name, Command(path));
        }
        else run.DeleteValue(name, false);
    }
}
