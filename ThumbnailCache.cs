using System.IO;
using System.Windows.Media.Imaging;

namespace Prtsc;

// UI-thread owned. Entries never keep a second strong reference to cached bitmaps.
// Visible Image controls retain their source until their container is recycled.
internal sealed class ThumbnailCache
{
    internal const int Capacity = 32;
    private readonly Dictionary<string, LinkedListNode<(string Path, BitmapSource Image)>> images = new();
    private readonly LinkedList<(string Path, BitmapSource Image)> recent = new();
    internal int Count => images.Count;
    internal BitmapSource? Get(string path)
    {
        if (images.TryGetValue(path, out var node))
        {
            recent.Remove(node); recent.AddFirst(node); return node.Value.Image;
        }
        try
        {
            using var stream = File.OpenRead(path);
            // Read dimensions without decoding full pixels, including very tall captures.
            var decoder = BitmapDecoder.Create(stream, BitmapCreateOptions.DelayCreation, BitmapCacheOption.None);
            int width = decoder.Frames[0].PixelWidth, height = decoder.Frames[0].PixelHeight;
            double scale = Math.Min(1, Math.Min(420d / width, 300d / height));
            stream.Position = 0;
            var image = new BitmapImage(); image.BeginInit();
            image.CacheOption = BitmapCacheOption.OnLoad;
            image.DecodePixelWidth = Math.Max(1, (int)Math.Floor(width * scale));
            image.DecodePixelHeight = Math.Max(1, (int)Math.Floor(height * scale));
            image.StreamSource = stream; image.EndInit(); image.Freeze();
            images.Add(path, recent.AddFirst((path, image)));
            if (images.Count > Capacity) { images.Remove(recent.Last!.Value.Path); recent.RemoveLast(); }
            return image;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException or ArgumentException or System.IO.FileFormatException)
        { return null; }
    }
    internal void Remove(string path)
    {
        if (images.Remove(path, out var node)) recent.Remove(node);
    }
}
