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
    private Func<BitmapSource?>? thumbnailLoader;
    internal void SetThumbnailLoader(Func<BitmapSource?> loader)
    {
        thumbnail = null; thumbnailLoader = loader;
        PropertyChanged?.Invoke(this, new(nameof(Thumbnail)));
    }
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Title { get; set; } = "キャプチャ";
    public DateTime Created { get; set; } = DateTime.Now;
    public List<Mark> Marks { get; set; } = [];
    public CropRegion? Crop { get; set; }
    public bool IsVideo { get; set; }
    public double DurationSeconds { get; set; }
    public int VideoWidth { get; set; }
    public int VideoHeight { get; set; }
    public bool SystemAudio { get; set; }
    public bool Microphone { get; set; }
    [System.Text.Json.Serialization.JsonIgnore] public BitmapSource? Thumbnail
    {
        get => thumbnailLoader is null ? thumbnail : thumbnailLoader();
        set { thumbnailLoader = null; thumbnail = value; PropertyChanged?.Invoke(this, new(nameof(Thumbnail))); }
    }
    [System.Text.Json.Serialization.JsonIgnore] public string Caption => Created.ToString("MM/dd  HH:mm") + (IsVideo ? "  ·  ▶ " + TimeSpan.FromSeconds(Math.Clamp(DurationSeconds, 0, 359999)).ToString(@"hh\:mm\:ss") : "");
    [System.Text.Json.Serialization.JsonIgnore] public string OpenLabel => IsVideo ? "再生" : "編集";
    [System.Text.Json.Serialization.JsonIgnore] public string OpenHint => IsVideo ? "クリックで動画を再生 · ドラッグでMP4ファイルとして共有" : "クリックで編集 · ドラッグでPNGファイルとして共有";
    [System.Text.Json.Serialization.JsonIgnore] public string CopyHint => IsVideo ? "MP4ファイルとしてコピー" : "注釈を含む画像としてコピー";
}

public sealed class CaptureStore
{
    public static string DefaultRoot => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Prtsc", "Captures");
    private readonly ThumbnailCache thumbnails = new();
    internal int CachedThumbnailCount => thumbnails.Count;
    public string Root { get; }
    public CaptureStore(string? root = null)
    {
        Root = root ?? DefaultRoot;
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
                if (entry.IsVideo && !File.Exists(VideoPath(entry))) continue;
                if (entry.IsVideo && (!double.IsFinite(entry.DurationSeconds) || entry.DurationSeconds < 0 || entry.VideoWidth < 2 || entry.VideoHeight < 2)) continue;
                AttachThumbnail(entry);
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
        thumbnails.Remove(Path.Combine(Folder(entry), "preview.png"));
        AttachThumbnail(entry);
    }
    public BitmapSource Original(CaptureEntry entry) => ReadImage(Path.Combine(Folder(entry), "original.png"));
    public string VideoPath(CaptureEntry entry) => Path.Combine(Folder(entry), "recording.mp4");
    internal void CreateVideo(CaptureEntry entry, string video, BitmapSource preview)
    {
        if (!entry.IsVideo || !File.Exists(video) || new FileInfo(video).Length == 0) throw new ArgumentException("録画ファイルがありません。");
        Directory.CreateDirectory(Folder(entry));
        // The item is ignored by Load until the MP4 exists. Publish metadata first, then
        // move the finalized video on the same volume without duplicating a large file.
        Save(entry, preview); File.Move(video, VideoPath(entry), false);
    }
    private void AttachThumbnail(CaptureEntry entry)
    {
        entry.SetThumbnailLoader(() => thumbnails.Get(Path.Combine(Folder(entry), "preview.png")));
    }
    public void Delete(CaptureEntry entry)
    {
        Directory.Delete(Folder(entry), true);
        thumbnails.Remove(Path.Combine(Folder(entry), "preview.png"));
        entry.SetThumbnailLoader(() => null);
    }
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
