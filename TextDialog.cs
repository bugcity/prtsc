using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace Prtsc;

internal sealed class TextDialog : Window
{
    internal System.Windows.Controls.TextBox Input { get; }
    internal System.Windows.Controls.Button AcceptButton { get; }
    internal TextStyleEditor? FormatEditor { get; }
    public TextStyle? TextFormat => FormatEditor?.TextFormat;
    public TextDialog(string initialText = "", TextStyle? initialStyle = null, string color = "#1E293B", bool showFormatting = true)
    {
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
        Input = new System.Windows.Controls.TextBox { Text = initialText, Margin = new Thickness(0, 14, 0, 16), Padding = new Thickness(10), AcceptsReturn = true, Height = 110, TextWrapping = TextWrapping.Wrap, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        body.Children.Add(Input);
        if (showFormatting)
        {
            FormatEditor = new TextStyleEditor(initialStyle ?? new TextStyle(), color);
            FormatEditor.SetPreviewText(Input.Text); body.Children.Add(FormatEditor);
        }
        var scroll = new ScrollViewer { Content = body, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, MaxHeight = Math.Max(140, SystemParameters.WorkArea.Height - 180) };
        Grid.SetRow(scroll, 1); grid.Children.Add(scroll);
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        var cancel = new System.Windows.Controls.Button { Content = "キャンセル", IsCancel = true };
        AcceptButton = new System.Windows.Controls.Button { Content = initialText.Length == 0 ? "追加" : "更新", MinWidth = 80, Background = new SolidColorBrush(System.Windows.Media.Color.FromRgb(99, 102, 241)), Foreground = Brushes.White };
        void Validate() => AcceptButton.IsEnabled = !string.IsNullOrWhiteSpace(Input.Text) && (FormatEditor?.IsValid ?? true);
        Input.TextChanged += (_, _) => { FormatEditor?.SetPreviewText(Input.Text); Validate(); };
        if (FormatEditor is not null) FormatEditor.Changed += (_, _) => Validate();
        Validate(); AcceptButton.Click += (_, _) => { if (AcceptButton.IsEnabled) DialogResult = true; };
        buttons.Margin = new Thickness(0, 12, 0, 0);
        buttons.Children.Add(cancel); buttons.Children.Add(AcceptButton); Grid.SetRow(buttons, 2); grid.Children.Add(buttons);
        Content = grid; Loaded += (_, _) => { Input.Focus(); Input.SelectAll(); };
        PreviewKeyDown += (_, e) => { if (e.Key == Key.Escape) Close(); };
    }
}
