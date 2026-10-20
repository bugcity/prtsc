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
    private readonly TextBlock previewText = new() { Text = "文字のプレビュー Aa 123", TextWrapping = TextWrapping.Wrap, FontWeight = FontWeights.Bold };
    public void SetPreviewText(string text) => previewText.Text = string.IsNullOrWhiteSpace(text) ? "文字のプレビュー Aa 123" : text;
    internal TextStyleEditor(TextStyle initial, string color = "#1E293B")
    {
        TextFormat = initial.Normalize();
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
        var font = new ComboBox { IsEditable = true, ItemsSource = Fonts.SystemFontFamilies.Select(f => f.Source).OrderBy(s => s).ToList(), Text = TextFormat.FontFamily };
        double initialPoints = TextFormat.Size * 72 / 96, initialSize = TextFormat.Size;
        var size = SizeBox = new ComboBox
        {
            IsEditable = true, ItemsSource = new[] { "8", "9", "10", "10.5", "11", "12", "14", "16", "18", "20", "22", "24", "26", "28", "32", "36", "48", "60", "72" },
            Text = initialPoints.ToString(CultureInfo.InvariantCulture), ToolTip = "代表的なポイント数を選択するか、4.5〜225 pt を入力できます。"
        };
        var fonts = Row(120); Field(fonts, 0, "フォント", font); Field(fonts, 2, "文字サイズ（pt）", size);
        ComboBox ColorInput(string value) => new() { IsEditable = true, ItemsSource = new[] { "Transparent", "White", "Black", "#FFFDE68A", "#FF1E293B" }, Text = value, ToolTip = "Transparent、色名、#RRGGBB または #AARRGGBB" };
        var background = ColorInput(TextFormat.Background); var border = ColorInput(TextFormat.BorderColor);
        var colors = Row(); Field(colors, 0, "背景色", background); Field(colors, 2, "枠線の色", border);
        var width = new TextBox { Text = TextFormat.BorderWidth.ToString(CultureInfo.InvariantCulture), Padding = new Thickness(7) };
        var borderRow = Row(120); Field(borderRow, 2, "枠線の太さ（px）", width); width.ToolTip = "0〜20 px。0で枠線なし。";
        var preview = new Border { Padding = new Thickness(4), Margin = new Thickness(0, 4, 0, 8), Child = previewText };
        previewText.Foreground = (Brush)new BrushConverter().ConvertFromString(color)!;
        panel.Children.Add(new ScrollViewer { Content = preview, MaxHeight = 120, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled });
        var error = new TextBlock { Foreground = Brushes.Firebrick, TextWrapping = TextWrapping.Wrap }; panel.Children.Add(error);
        void Update()
        {
            bool valid = double.TryParse(size.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out double points) && double.IsFinite(points) && points is >= 4.5 and <= 225
                && double.TryParse(width.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out double w0) && double.IsFinite(w0) && w0 is >= 0 and <= 20;
            double.TryParse(width.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out double w);
            valid &= !string.IsNullOrWhiteSpace(font.Text) && font.Text.Length <= 200 && TextStyle.IsColor(background.Text) && TextStyle.IsColor(border.Text);
            IsValid = valid; error.Text = valid ? "" : "文字サイズ・色・枠線の太さを確認してください。";
            if (valid)
            {
                double s = points == initialPoints ? initialSize : points * 96 / 72;
                TextFormat = new TextStyle { Size = s, FontFamily = font.Text, Background = background.Text, BorderColor = border.Text, BorderWidth = w };
                preview.Background = (Brush)new BrushConverter().ConvertFromString(TextFormat.Background)!;
                preview.BorderBrush = (Brush)new BrushConverter().ConvertFromString(TextFormat.BorderColor)!; preview.BorderThickness = new Thickness(w);
                previewText.FontFamily = new FontFamily(TextFormat.FontFamily); previewText.FontSize = Math.Min(s, 48);
            }
            Changed?.Invoke(this, EventArgs.Empty);
        }
        size.AddHandler(TextBox.TextChangedEvent, new TextChangedEventHandler((_, _) => Update()));
        size.SelectionChanged += (_, _) => { if (size.SelectedItem is string selected) { size.Text = selected; Update(); } };
        width.TextChanged += (_, _) => Update();
        foreach (var box in new[] { font, background, border }) box.AddHandler(TextBox.TextChangedEvent, new TextChangedEventHandler((_, _) => Update()));
        Update();
    }
}
