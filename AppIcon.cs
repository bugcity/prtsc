using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Prtsc;

internal static class AppIcon
{
    public static ImageSource Source { get; } = BitmapFrame.Create(new Uri("pack://application:,,,/Assets/Prtsc.ico"));
    public static System.Drawing.Icon CreateTrayIcon()
    {
        using var resource = System.Windows.Application.GetResourceStream(new Uri("pack://application:,,,/Assets/Prtsc.ico")).Stream;
        using var icon = new System.Drawing.Icon(resource, 32, 32);
        return (System.Drawing.Icon)icon.Clone();
    }
}
