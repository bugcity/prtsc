using System.IO;
using System.Text.Json;

namespace Prtsc;

internal sealed record CaptureSettings
{
    public int MaxCaptures { get; init; }
    public double StrokeWidth { get; init; } = 4;
    public string Color { get; init; } = "#F43F5E";
    public string ArrowStyle { get; init; } = "Tapered";
    public TextStyle TextStyle { get; init; } = new();
    public uint HotkeyModifiers { get; init; }
    public uint HotkeyKey { get; init; } = 0x2C;
    public bool StartWithWindows { get; init; }
    public string RenderingMethod { get; init; } = "Software";
    public int NotificationSeconds { get; init; } = 3;
    public const int MaximumNotificationSeconds = 60;
    public const int MaximumLimit = 100000;
}

internal sealed class CaptureSettingsStore(string path)
{
    public CaptureSettings Load()
    {
        try
        {
            var settings = JsonSerializer.Deserialize<CaptureSettings>(File.ReadAllText(path));
            if (settings is null) return new();
            return settings with
            {
                MaxCaptures = settings.MaxCaptures is >= 0 and <= CaptureSettings.MaximumLimit ? settings.MaxCaptures : 0,
                Color = TextStyle.IsColor(settings.Color) ? settings.Color : "#F43F5E",
                ArrowStyle = ArrowStyles.Options.Any(o => o.Key == settings.ArrowStyle) ? settings.ArrowStyle : "Tapered",
                TextStyle = (settings.TextStyle ?? new()).Normalize(),
                RenderingMethod = settings.RenderingMethod == "Automatic" ? "Automatic" : "Software",
                NotificationSeconds = settings.NotificationSeconds is >= 0 and <= CaptureSettings.MaximumNotificationSeconds ? settings.NotificationSeconds : 3,
                HotkeyModifiers = HotkeyBinding.IsValid(settings.HotkeyModifiers, settings.HotkeyKey) ? settings.HotkeyModifiers : 0,
                HotkeyKey = HotkeyBinding.IsValid(settings.HotkeyModifiers, settings.HotkeyKey) ? settings.HotkeyKey : 0x2C,
                StrokeWidth = double.IsFinite(settings.StrokeWidth) && settings.StrokeWidth is >= 2 and <= 12 ? settings.StrokeWidth : 4
            };
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException) { return new(); }
    }
    public void Save(CaptureSettings settings)
    {
        if (settings.MaxCaptures < 0 || settings.MaxCaptures > CaptureSettings.MaximumLimit) throw new ArgumentOutOfRangeException(nameof(settings));
        if (settings.NotificationSeconds < 0 || settings.NotificationSeconds > CaptureSettings.MaximumNotificationSeconds) throw new ArgumentOutOfRangeException(nameof(settings));
        if (!double.IsFinite(settings.StrokeWidth) || settings.StrokeWidth is < 2 or > 12) throw new ArgumentOutOfRangeException(nameof(settings));
        if (settings.RenderingMethod is not ("Software" or "Automatic") || !TextStyle.IsColor(settings.Color) || settings.TextStyle is null || settings.TextStyle != settings.TextStyle.Normalize()
            || !ArrowStyles.Options.Any(o => o.Key == settings.ArrowStyle) || !HotkeyBinding.IsValid(settings.HotkeyModifiers, settings.HotkeyKey))
            throw new ArgumentException("設定値が不正です。", nameof(settings));
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        File.WriteAllText(path + ".tmp", JsonSerializer.Serialize(settings)); File.Move(path + ".tmp", path, true);
    }
}

internal static class CaptureRetention
{
    // The library is ordered newest first, including newly inserted captures with equal timestamps.
    public static List<CaptureEntry> Excess(IEnumerable<CaptureEntry> newestFirst, int limit)
        => limit <= 0 ? [] : newestFirst.Skip(limit).Reverse().ToList();
}
