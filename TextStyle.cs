using System.Windows.Media;

namespace Prtsc;

public sealed record TextStyle
{
    public double Size { get; init; } = 24;
    public string FontFamily { get; init; } = "Yu Gothic UI";
    public string Background { get; init; } = "#00FFFFFF";
    public string BorderColor { get; init; } = "#001E293B";
    public double BorderWidth { get; init; } = 1;
    public TextStyle Normalize() => this with
    {
        Size = double.IsFinite(Size) && Size is >= 6 and <= 300 ? Size : 24,
        FontFamily = string.IsNullOrWhiteSpace(FontFamily) || FontFamily.Length > 200 ? "Yu Gothic UI" : FontFamily,
        Background = IsColor(Background) ? Background : "#00FFFFFF",
        BorderColor = IsColor(BorderColor) ? BorderColor : "#001E293B",
        BorderWidth = double.IsFinite(BorderWidth) && BorderWidth is >= 0 and <= 20 ? BorderWidth : 1
    };
    internal static bool IsColor(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return false;
        try { return ColorConverter.ConvertFromString(value) is Color; }
        catch (System.Exception ex) when (ex is System.FormatException or System.NotSupportedException or System.ArgumentException) { return false; }
    }
}
