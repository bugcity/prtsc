using System.IO;
using System.Text.Json;
using System.Windows.Media.Imaging;

namespace Prtsc;

public sealed record Mark
{
    public string Tool { get; init; } = "Arrow";
    public string ArrowStyle { get; init; } = "Open";
    public string Color { get; init; } = "#F43F5E";
    public double Width { get; init; } = 4;
    public double X { get; init; }
    public double Y { get; init; }
    public double X2 { get; init; }
    public double Y2 { get; init; }
    public string Text { get; init; } = "";
    public TextStyle? TextStyle { get; init; }
    public List<double[]> Points { get; init; } = [];
}

public sealed class CaptureEntry : System.ComponentModel.INotifyPropertyChanged
{
    public event System.ComponentModel.PropertyChangedEventHandler? PropertyChanged;
    private BitmapSource? thumbnail;
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Title { get; set; } = "キャプチャ";
    public DateTime Created { get; set; } = DateTime.Now;
    public List<Mark> Marks { get; set; } = [];
    public CropRegion? Crop { get; set; }
    [System.Text.Json.Serialization.JsonIgnore] public BitmapSource? Thumbnail
    {
        get => thumbnail;
        set { thumbnail = value; PropertyChanged?.Invoke(this, new(nameof(Thumbnail))); }
    }
    [System.Text.Json.Serialization.JsonIgnore] public string Caption => Created.ToString("MM/dd  HH:mm");
}

public sealed class CaptureStore
{
    public string Root { get; }
    public CaptureStore(string? root = null)
    {
        Root = root ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Prtsc", "Captures");
        Directory.CreateDirectory(Root);
    }
    public string Folder(CaptureEntry entry)
    {
        if (!Guid.TryParseExact(entry.Id, "N", out _)) throw new ArgumentException("キャプチャ ID が不正です。", nameof(entry));
        return Path.GetFullPath(Path.Combine(Root, entry.Id));
    }
    public List<CaptureEntry> Load()
    {
        List<CaptureEntry> entries = [];
        foreach (var folder in Directory.EnumerateDirectories(Root))
        {
            try
            {
                var entry = JsonSerializer.Deserialize<CaptureEntry>(File.ReadAllText(Path.Combine(folder, "capture.json")));
                if (entry is null || entry.Id != Path.GetFileName(folder) || !Guid.TryParseExact(entry.Id, "N", out _)) continue;
                entry.Thumbnail = ReadImage(Path.Combine(folder, "preview.png"), 420);
                entries.Add(entry);
            }
            catch (Exception ex) when (ex is IOException or JsonException or NotSupportedException or ArgumentException) { }
        }
        return entries.OrderByDescending(e => e.Created).ToList();
    }
    public void Create(CaptureEntry entry, BitmapSource original)
    {
        Directory.CreateDirectory(Folder(entry));
        WriteImage(Path.Combine(Folder(entry), "original.png"), original);
        Save(entry, original);
    }
    public void Save(CaptureEntry entry, BitmapSource preview)
    {
        WriteImage(Path.Combine(Folder(entry), "preview.png"), preview);
        var path = Path.Combine(Folder(entry), "capture.json");
        File.WriteAllText(path + ".tmp", JsonSerializer.Serialize(entry));
        File.Move(path + ".tmp", path, true);
        entry.Thumbnail = ReadImage(Path.Combine(Folder(entry), "preview.png"), 420);
    }
    public BitmapSource Original(CaptureEntry entry) => ReadImage(Path.Combine(Folder(entry), "original.png"));
    public void Delete(CaptureEntry entry) => Directory.Delete(Folder(entry), true);
    public static BitmapSource ReadImage(string path, int decodeWidth = 0)
    {
        using var stream = File.OpenRead(path);
        var image = new BitmapImage();
        image.BeginInit(); image.CacheOption = BitmapCacheOption.OnLoad;
        if (decodeWidth > 0) image.DecodePixelWidth = decodeWidth;
        image.StreamSource = stream; image.EndInit(); image.Freeze();
        return image;
    }
    public static void WriteImage(string path, BitmapSource image)
    {
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(image));
        using var stream = File.Create(path); encoder.Save(stream);
    }
}
