using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using TextBox = System.Windows.Controls.TextBox;
using ComboBox = System.Windows.Controls.ComboBox;
using Button = System.Windows.Controls.Button;

namespace Prtsc;

internal sealed class TextStyleDialog : Window
{
    public TextStyle TextFormat { get; private set; }
    internal ComboBox SizeBox { get; }
    public TextStyleDialog(TextStyle initial)
    {
        TextFormat = initial.Normalize();
        Title = "文字の書式"; Icon = AppIcon.Source; Width = 470; SizeToContent = SizeToContent.Height;
        ResizeMode = ResizeMode.NoResize; WindowStartupLocation = WindowStartupLocation.CenterOwner; Background = Brushes.White;
        var panel = new StackPanel { Margin = new Thickness(24) }; Content = panel;
        void Label(string label) => panel.Children.Add(new TextBlock { Text = label, Margin = new Thickness(0, 10, 0, 5) });
        Label("フォント");
        var font = new ComboBox { IsEditable = true, ItemsSource = Fonts.SystemFontFamilies.Select(f => f.Source).OrderBy(s => s).ToList(), Text = TextFormat.FontFamily }; panel.Children.Add(font);
        Label("文字サイズ（pt）");
        double initialPoints = TextFormat.Size * 72 / 96;
        double initialSize = TextFormat.Size;
        var size = SizeBox = new ComboBox
        {
            IsEditable = true,
            ItemsSource = new[] { "8", "9", "10", "10.5", "11", "12", "14", "16", "18", "20", "22", "24", "26", "28", "32", "36", "48", "60", "72" },
            Text = initialPoints.ToString(CultureInfo.InvariantCulture),
            ToolTip = "代表的なポイント数を選択するか、4.5〜225 pt を入力できます。"
        };
        panel.Children.Add(size);
        ComboBox ColorInput(string label, string value)
        {
            Label(label);
            var input = new ComboBox { IsEditable = true, ItemsSource = new[] { "Transparent", "White", "Black", "#FFFDE68A", "#FF1E293B" }, Text = value };
            panel.Children.Add(input); return input;
        }
        var background = ColorInput("背景色（Transparent または #AARRGGBB / #RRGGBB）", TextFormat.Background);
        var border = ColorInput("枠線の色（Transparent で非表示）", TextFormat.BorderColor);
        Label("枠線の太さ（px・0〜20）"); var width = new TextBox { Text = TextFormat.BorderWidth.ToString(CultureInfo.InvariantCulture), Padding = new Thickness(7) }; panel.Children.Add(width);
        var preview = new Border { Padding = new Thickness(4), Margin = new Thickness(0, 16, 0, 8), Child = new TextBlock { Text = "文字のプレビュー Aa 123", TextWrapping = TextWrapping.Wrap } }; panel.Children.Add(preview);
        var error = new TextBlock { Foreground = Brushes.Firebrick, TextWrapping = TextWrapping.Wrap }; panel.Children.Add(error);
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 12, 0, 0) };
        buttons.Children.Add(new Button { Content = "キャンセル", IsCancel = true });
        var apply = new Button { Content = "適用", IsDefault = true }; buttons.Children.Add(apply); panel.Children.Add(buttons);
        void Update()
        {
            bool valid = double.TryParse(size.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out double points) && double.IsFinite(points) && points is >= 4.5 and <= 225
                && double.TryParse(width.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out double w0) && double.IsFinite(w0) && w0 is >= 0 and <= 20;
            double.TryParse(width.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out double w);
            valid &= !string.IsNullOrWhiteSpace(font.Text) && font.Text.Length <= 200 && TextStyle.IsColor(background.Text) && TextStyle.IsColor(border.Text);
            apply.IsEnabled = valid; error.Text = valid ? "" : "文字サイズ・色・枠線の太さを確認してください。";
            if (!valid) return;
            double s = points == initialPoints ? initialSize : points * 96 / 72;
            TextFormat = new TextStyle { Size = s, FontFamily = font.Text, Background = background.Text, BorderColor = border.Text, BorderWidth = w };
            preview.Background = (Brush)new BrushConverter().ConvertFromString(TextFormat.Background)!;
            preview.BorderBrush = (Brush)new BrushConverter().ConvertFromString(TextFormat.BorderColor)!;
            preview.BorderThickness = new Thickness(w);
            var text = (TextBlock)preview.Child; text.FontFamily = new FontFamily(TextFormat.FontFamily); text.FontSize = Math.Min(s, 48);
        }
        size.AddHandler(TextBox.TextChangedEvent, new TextChangedEventHandler((_, _) => Update()));
        size.SelectionChanged += (_, _) => { if (size.SelectedItem is string selected) { size.Text = selected; Update(); } };
        width.TextChanged += (_, _) => Update();
        foreach (var box in new[] { font, background, border }) box.AddHandler(TextBox.TextChangedEvent, new TextChangedEventHandler((_, _) => Update()));
        apply.Click += (_, _) => { Update(); if (apply.IsEnabled) DialogResult = true; }; Update();
    }
}
