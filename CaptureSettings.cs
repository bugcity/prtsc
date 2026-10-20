using System.IO;
using System.Text.Json;

namespace Prtsc;

internal sealed record CaptureSettings
{
    public int MaxCaptures { get; init; }
    public const int MaximumLimit = 100000;
}

internal sealed class CaptureSettingsStore(string path)
{
    public CaptureSettings Load()
    {
        try
        {
            var settings = JsonSerializer.Deserialize<CaptureSettings>(File.ReadAllText(path));
            return settings is { MaxCaptures: >= 0 and <= CaptureSettings.MaximumLimit } ? settings : new();
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException) { return new(); }
    }
    public void Save(CaptureSettings settings)
    {
        if (settings.MaxCaptures < 0 || settings.MaxCaptures > CaptureSettings.MaximumLimit) throw new ArgumentOutOfRangeException(nameof(settings));
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
