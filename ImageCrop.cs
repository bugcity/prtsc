using System.Windows;
using System.Windows.Media.Imaging;

namespace Prtsc;

public sealed record CropRegion(int X, int Y, int Width, int Height);

internal static class ImageCrop
{
    public static Int32Rect Bounds(BitmapSource original, CropRegion? crop)
    {
        if (crop is null) return new(0, 0, original.PixelWidth, original.PixelHeight);
        if (crop.X < 0 || crop.Y < 0 || crop.Width < 1 || crop.Height < 1 || (long)crop.X + crop.Width > original.PixelWidth || (long)crop.Y + crop.Height > original.PixelHeight)
            throw new ArgumentException("トリム範囲が画像の外にあります。", nameof(crop));
        return new(crop.X, crop.Y, crop.Width, crop.Height);
    }
    public static CropRegion FromPoints(System.Windows.Point first, System.Windows.Point second, Int32Rect bounds)
    {
        int left = (int)Math.Clamp(Math.Floor(Math.Min(first.X, second.X)), bounds.X, bounds.X + bounds.Width);
        int top = (int)Math.Clamp(Math.Floor(Math.Min(first.Y, second.Y)), bounds.Y, bounds.Y + bounds.Height);
        int right = (int)Math.Clamp(Math.Ceiling(Math.Max(first.X, second.X)), bounds.X, bounds.X + bounds.Width);
        int bottom = (int)Math.Clamp(Math.Ceiling(Math.Max(first.Y, second.Y)), bounds.Y, bounds.Y + bounds.Height);
        return new(left, top, right - left, bottom - top);
    }
    public static Mark DisplayMark(Mark mark, CropRegion? crop) => crop is null ? mark : AnnotationDocument.Move(mark, new Vector(-crop.X, -crop.Y));
}
