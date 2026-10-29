using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using TextBox = System.Windows.Controls.TextBox;
using ComboBox = System.Windows.Controls.ComboBox;

namespace Prtsc;

internal sealed class TextStyleEditor : System.Windows.Controls.UserControl
{
    public TextStyle TextFormat { get; private set; }
    public bool IsValid { get; private set; }
    public event EventHandler? Changed;
    internal ComboBox SizeBox { get; }
    internal ComboBox FontBox { get; }
    internal ComboBox ColorBox { get; }
    internal ComboBox BackgroundBox { get; }
    internal ComboBox BorderColorBox { get; }
    internal TextBox BorderWidthInput { get; }
    public string TextColor { get; private set; }
    internal TextStyleEditor(TextStyle initial, string color = "#1E293B")
    {
        TextFormat = initial.Normalize();
        TextColor = TextStyle.IsColor(color) ? color : "#1E293B";
        var panel = new StackPanel(); Content = panel;
        Grid Row(double rightWidth = 0)
        {
            var row = new Grid { Margin = new Thickness(0, 0, 0, 10) };
            row.ColumnDefinitions.Add(new ColumnDefinition());
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(12) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = rightWidth > 0 ? new GridLength(rightWidth) : new GridLength(1, GridUnitType.Star) });
            panel.Children.Add(row); return row;
        }
        void Field(Grid row, int column, string label, FrameworkElement input)
        {
            var field = new StackPanel(); field.Children.Add(new TextBlock { Text = label, Margin = new Thickness(0, 0, 0, 5) });
            field.Children.Add(input); Grid.SetColumn(field, column); row.Children.Add(field);
        }
        var font = FontBox = new ComboBox { IsEditable = true, ItemsSource = Fonts.SystemFontFamilies.Select(f => f.Source).OrderBy(s => s).ToList(), Text = TextFormat.FontFamily };
        double initialPoints = TextFormat.Size * 72 / 96, initialSize = TextFormat.Size;
        var size = SizeBox = new ComboBox
        {
            IsEditable = true, ItemsSource = new[] { "8", "9", "10", "10.5", "11", "12", "14", "16", "18", "20", "22", "24", "26", "28", "32", "36", "48", "60", "72" },
            Text = initialPoints.ToString(CultureInfo.InvariantCulture), ToolTip = "代表的なポイント数を選択するか、4.5〜225 pt を入力できます。"
        };
        var fonts = Row(120); Field(fonts, 0, "フォント", font); Field(fonts, 2, "文字サイズ（pt）", size);
        ComboBox ColorInput(string value, bool transparent = true) => new()
        {
            IsEditable = true,
            ItemsSource = (transparent ? new[] { "透明" } : Array.Empty<string>()).Concat(new[] { "White", "Black", "#F43F5E", "#6366F1", "#0EA5E9", "#10B981", "#FFFDE68A", "#FF1E293B" }).ToArray(),
            Text = transparent && ((Color)ColorConverter.ConvertFromString(value)).A == 0 ? "透明" : value,
            ToolTip = "色名、#RRGGBB または #AARRGGBB。背景・枠線は「透明」を選択できます。"
        };
        static string ColorValue(ComboBox box) => box.Text.Trim() == "透明" ? "Transparent" : box.Text.Trim();
        var foreground = ColorBox = ColorInput(TextColor, false);
        var textColorRow = Row(); Field(textColorRow, 0, "文字色", foreground);
        var background = BackgroundBox = ColorInput(TextFormat.Background); var border = BorderColorBox = ColorInput(TextFormat.BorderColor);
        var colors = Row(); Field(colors, 0, "背景色", background); Field(colors, 2, "枠線の色", border);
        var width = BorderWidthInput = new TextBox { Text = TextFormat.BorderWidth.ToString(CultureInfo.InvariantCulture), Padding = new Thickness(7) };
        var borderRow = Row(120); Field(borderRow, 2, "枠線の太さ（px）", width); width.ToolTip = "0〜20 px。0で枠線なし。";
        var error = new TextBlock { Foreground = Brushes.Firebrick, TextWrapping = TextWrapping.Wrap }; panel.Children.Add(error);
        void Update()
        {
            bool valid = double.TryParse(size.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out double points) && double.IsFinite(points) && points is >= 4.5 and <= 225
                && double.TryParse(width.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out double w0) && double.IsFinite(w0) && w0 is >= 0 and <= 20;
            double.TryParse(width.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out double w);
            valid &= !string.IsNullOrWhiteSpace(font.Text) && font.Text.Length <= 200 && TextStyle.IsColor(ColorValue(foreground)) && TextStyle.IsColor(ColorValue(background)) && TextStyle.IsColor(ColorValue(border));
            IsValid = valid; error.Text = valid ? "" : "文字サイズ・色・枠線の太さを確認してください。";
            if (valid)
            {
                double s = points == initialPoints ? initialSize : points * 96 / 72;
                TextFormat = new TextStyle { Size = s, FontFamily = font.Text, Background = ColorValue(background), BorderColor = ColorValue(border), BorderWidth = w };
                TextColor = ColorValue(foreground);
            }
            Changed?.Invoke(this, EventArgs.Empty);
        }
        size.AddHandler(TextBox.TextChangedEvent, new TextChangedEventHandler((_, _) => Update()));
        size.SelectionChanged += (_, _) => { if (size.SelectedItem is string selected) { size.Text = selected; Update(); } };
        width.TextChanged += (_, _) => Update();
        foreach (var box in new[] { font, foreground, background, border })
        {
            box.AddHandler(TextBox.TextChangedEvent, new TextChangedEventHandler((_, _) => Update()));
            box.SelectionChanged += (_, _) => { if (box.SelectedItem is string selected) { box.Text = selected; Update(); } };
        }
        Update();
    }
}
