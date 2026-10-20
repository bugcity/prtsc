using System.IO;
using System.Windows;
using System.Windows.Media.Imaging;

namespace Prtsc;

internal sealed class CaptureDragExport(CaptureStore store)
{
    internal const string CaptureIdFormat = "Prtsc.CaptureId";
    public string Root => Path.Combine(store.Root, ".drag-exports");
    public DataObject Create(CaptureEntry entry)
    {
        Directory.CreateDirectory(Root); Cleanup();
        var title = string.Concat(entry.Title.Select(c => Path.GetInvalidFileNameChars().Contains(c) || char.IsControl(c) ? '_' : c)).Trim().TrimEnd('.');
        if (title.Length > 70) title = title[..70];
        if (title.Length == 0) title = "capture";
        // Prefix avoids Windows device names; unique files preserve earlier drag snapshots after later edits.
        var path = Path.Combine(Root, $"prtsc-{Guid.NewGuid():N}-{title}" + (entry.IsVideo ? ".mp4" : ".png"));
        if (entry.IsVideo)
        {
            File.Copy(store.VideoPath(entry), path);
            var videoData = new DataObject(); videoData.SetData(DataFormats.FileDrop, new[] { path }); videoData.SetData(CaptureIdFormat, entry.Id); return videoData;
        }
        var image = AnnotationRenderer.Render(store.Original(entry), entry.Marks, entry.Crop);
        CaptureStore.WriteImage(path, image);
        var data = new DataObject(); data.SetData(DataFormats.FileDrop, new[] { path });
        data.SetData(DataFormats.Bitmap, BitmapFrame.Create(image)); data.SetData(CaptureIdFormat, entry.Id);
        return data;
    }
    public void Cleanup()
    {
        if (!Directory.Exists(Root)) return;
        foreach (var path in Directory.EnumerateFiles(Root, "prtsc-*.*").Where(p => Path.GetExtension(p) is ".png" or ".mp4"))
        {
            try { if (File.GetLastWriteTimeUtc(path) < DateTime.UtcNow.AddDays(-1)) File.Delete(path); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        }
    }
}
