using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace Prtsc;

internal sealed class TextDialog : Window
{
    internal System.Windows.Controls.TextBox Input { get; }
    internal System.Windows.Controls.Button AcceptButton { get; }
    internal System.Windows.Controls.Button ApplyButton { get; }
    internal TextStyleEditor? FormatEditor { get; }
    internal Border InputSurface { get; }
    public TextStyle? TextFormat => FormatEditor?.TextFormat;
    public string TextColor => FormatEditor?.TextColor ?? initialColor;
    private readonly string initialColor;
    public TextDialog(string initialText = "", TextStyle? initialStyle = null, string color = "#1E293B", bool showFormatting = true, Func<TextDialog, bool>? apply = null)
    {
        initialColor = color;
        Title = initialText.Length == 0 ? "テキストを追加" : "テキストを編集";
        Width = showFormatting ? 540 : 440; SizeToContent = SizeToContent.Height; ResizeMode = ResizeMode.NoResize;
        MaxHeight = Math.Max(240, SystemParameters.WorkArea.Height - 24);
        WindowStartupLocation = WindowStartupLocation.CenterOwner; Background = Brushes.White;
        Icon = AppIcon.Source;
        var grid = new Grid { Margin = new Thickness(22) };
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.Children.Add(new TextBlock { Text = "画像に入れるテキスト", FontWeight = FontWeights.SemiBold });
        var body = new StackPanel();
        Input = new System.Windows.Controls.TextBox { Text = initialText, Padding = new Thickness(4), AcceptsReturn = true, Height = 140, TextWrapping = TextWrapping.Wrap, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        InputSurface = new Border { Child = Input, Margin = new Thickness(0, 14, 0, 16), Padding = new Thickness(showFormatting ? 8 : 0), Background = new SolidColorBrush(Color.FromRgb(190, 194, 201)) };
        body.Children.Add(InputSurface);
        if (showFormatting)
        {
            FormatEditor = new TextStyleEditor(initialStyle ?? new TextStyle(), color);
            Input.Template = (ControlTemplate)System.Windows.Markup.XamlReader.Parse("""
                <ControlTemplate xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation" TargetType="TextBox">
                    <Border Background="{TemplateBinding Background}" BorderBrush="{TemplateBinding BorderBrush}" BorderThickness="{TemplateBinding BorderThickness}" Padding="{TemplateBinding Padding}">
                        <ScrollViewer Name="PART_ContentHost" Focusable="False"/>
                    </Border>
                </ControlTemplate>
                """);
            Input.FontWeight = FontWeights.Bold;
            body.Children.Add(new TextBlock { Text = "入力欄に書式を反映します。透明な部分はグレーの下地で表示します。", Foreground = Brushes.DimGray, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 12) });
            body.Children.Add(FormatEditor);
        }
        var scroll = new ScrollViewer { Content = body, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, MaxHeight = Math.Max(140, SystemParameters.WorkArea.Height - 180) };
        Grid.SetRow(scroll, 1); grid.Children.Add(scroll);
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        var cancel = new System.Windows.Controls.Button { Content = "キャンセル", IsCancel = true };
        AcceptButton = new System.Windows.Controls.Button { Content = initialText.Length == 0 ? "追加" : "更新", MinWidth = 80, Background = new SolidColorBrush(System.Windows.Media.Color.FromRgb(99, 102, 241)), Foreground = Brushes.White };
        ApplyButton = new System.Windows.Controls.Button { Content = "適用", MinWidth = 70, ToolTip = "画像に反映して、ダイアログを開いたまま編集を続けます。適用済みの変更はキャンセルしても保持されます。" };
        void Validate()
        {
            AcceptButton.IsEnabled = !string.IsNullOrWhiteSpace(Input.Text) && (FormatEditor?.IsValid ?? true);
            ApplyButton.IsEnabled = AcceptButton.IsEnabled && apply is not null;
        }
        Input.TextChanged += (_, _) => Validate();
        void ApplyInputFormat()
        {
            if (FormatEditor is not { IsValid: true }) return;
            Input.FontFamily = new FontFamily(FormatEditor.TextFormat.FontFamily);
            Input.FontSize = FormatEditor.TextFormat.Size;
            Input.Foreground = (Brush)new BrushConverter().ConvertFromString(FormatEditor.TextColor)!;
            Input.Background = (Brush)new BrushConverter().ConvertFromString(FormatEditor.TextFormat.Background)!;
            Input.BorderBrush = (Brush)new BrushConverter().ConvertFromString(FormatEditor.TextFormat.BorderColor)!;
            Input.BorderThickness = new Thickness(FormatEditor.TextFormat.BorderWidth);
        }
        if (FormatEditor is not null) FormatEditor.Changed += (_, _) => { ApplyInputFormat(); Validate(); };
        ApplyInputFormat();
        bool Apply()
        {
            if (!AcceptButton.IsEnabled || apply?.Invoke(this) == false) return false;
            if (apply is not null) { AcceptButton.Content = "更新"; Title = "テキストを編集"; }
            return true;
        }
        Validate(); AcceptButton.Click += (_, _) => { if (Apply()) DialogResult = true; };
        ApplyButton.Click += (_, _) => { if (ApplyButton.IsEnabled && Apply()) Input.Focus(); };
        buttons.Margin = new Thickness(0, 12, 0, 0);
        buttons.Children.Add(cancel); buttons.Children.Add(ApplyButton); buttons.Children.Add(AcceptButton); Grid.SetRow(buttons, 2); grid.Children.Add(buttons);
        Content = grid; Loaded += (_, _) => { Input.Focus(); Input.SelectAll(); };
        PreviewKeyDown += (_, e) => { if (e.Key == Key.Escape) Close(); };
    }
}
