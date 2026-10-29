using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Button = System.Windows.Controls.Button;

namespace Prtsc;

internal sealed class TextStyleDialog : Window
{
    private readonly TextStyleEditor editor;
    public TextStyle TextFormat => editor.TextFormat;
    public string TextColor => editor.TextColor;
    internal ComboBox SizeBox => editor.SizeBox;
    public TextStyleDialog(TextStyle initial, string color = "#1E293B")
    {
        Title = "文字の書式"; Icon = AppIcon.Source; Width = 470; SizeToContent = SizeToContent.Height;
        ResizeMode = ResizeMode.NoResize; WindowStartupLocation = WindowStartupLocation.CenterOwner; Background = Brushes.White;
        var panel = new StackPanel { Margin = new Thickness(24) }; Content = panel;
        editor = new TextStyleEditor(initial, color); panel.Children.Add(editor);
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 12, 0, 0) };
        buttons.Children.Add(new Button { Content = "キャンセル", IsCancel = true });
        var apply = new Button { Content = "適用", IsDefault = true }; buttons.Children.Add(apply); panel.Children.Add(buttons);
        editor.Changed += (_, _) => apply.IsEnabled = editor.IsValid;
        apply.IsEnabled = editor.IsValid; apply.Click += (_, _) => { if (editor.IsValid) DialogResult = true; };
    }
}
