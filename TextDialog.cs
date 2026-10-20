using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace Prtsc;

internal sealed class TextDialog : Window
{
    internal System.Windows.Controls.TextBox Input { get; }
    internal System.Windows.Controls.Button AcceptButton { get; }
    public TextDialog(string initialText = "")
    {
        Title = initialText.Length == 0 ? "テキストを追加" : "テキストを編集";
        Width = 440; SizeToContent = SizeToContent.Height; ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.CenterOwner; Background = Brushes.White;
        Icon = AppIcon.Source;
        var grid = new Grid { Margin = new Thickness(22) };
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.Children.Add(new TextBlock { Text = "画像に入れるテキスト", FontWeight = FontWeights.SemiBold });
        Input = new System.Windows.Controls.TextBox { Text = initialText, Margin = new Thickness(0, 14, 0, 16), Padding = new Thickness(10), AcceptsReturn = true, Height = 130, TextWrapping = TextWrapping.Wrap, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        Grid.SetRow(Input, 1); grid.Children.Add(Input);
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        var cancel = new System.Windows.Controls.Button { Content = "キャンセル", IsCancel = true };
        AcceptButton = new System.Windows.Controls.Button { Content = initialText.Length == 0 ? "追加" : "更新", MinWidth = 80, Background = new SolidColorBrush(System.Windows.Media.Color.FromRgb(99, 102, 241)), Foreground = Brushes.White };
        AcceptButton.Click += (_, _) => DialogResult = true;
        buttons.Children.Add(cancel); buttons.Children.Add(AcceptButton); Grid.SetRow(buttons, 2); grid.Children.Add(buttons);
        Content = grid; Loaded += (_, _) => { Input.Focus(); Input.SelectAll(); };
        PreviewKeyDown += (_, e) => { if (e.Key == Key.Escape) Close(); };
    }
}
