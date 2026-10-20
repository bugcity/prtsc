using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Button = System.Windows.Controls.Button;

namespace Prtsc;

internal sealed class RecordingDialog : Window
{
    internal CheckBox SystemAudio { get; } = new() { Content = "PC の音を録音する", Margin = new Thickness(0, 10, 0, 8) };
    internal CheckBox Microphone { get; } = new() { Content = "マイクを録音する", Margin = new Thickness(0, 0, 0, 18) };
    internal CheckBox MouseActions { get; } = new() { Content = "カーソルとマウス操作を表示する", IsChecked = true, Margin = new Thickness(0, 0, 0, 8) };
    internal ComboBox Mode { get; } = new() { ItemsSource = new[] { "範囲／ウィンドウを選択", "画面全体（すべてのモニター）" }, SelectedIndex = 0, Margin = new Thickness(0, 10, 0, 14) };
    internal RecordingDialog()
    {
        Title = "動画を録画"; Icon = AppIcon.Source; Width = 480; SizeToContent = SizeToContent.Height; ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.CenterOwner; Background = Brushes.White;
        var panel = new StackPanel { Margin = new Thickness(24) }; Content = panel;
        panel.Children.Add(new TextBlock { Text = "動画を録画", FontSize = 22, FontWeight = FontWeights.SemiBold }); panel.Children.Add(Mode);
        panel.Children.Add(new TextBlock { Text = "音声", FontWeight = FontWeights.SemiBold }); panel.Children.Add(SystemAudio); panel.Children.Add(Microphone);
        panel.Children.Add(MouseActions);
        panel.Children.Add(new TextBlock { Text = "移動中は24px、押している間は48pxの赤い円。\nクリック・ドラッグに合わせて滑らかに変化します。", Foreground = Brushes.SlateGray, TextWrapping = TextWrapping.Wrap });
        panel.Children.Add(new TextBlock { Text = "MP4 · 30fps · 音声はチェックした項目のみ録音\nウィンドウは選択時の位置と範囲を固定して録画します。", Foreground = Brushes.SlateGray, Margin = new Thickness(0, 16, 0, 12), TextWrapping = TextWrapping.Wrap });
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        buttons.Children.Add(new Button { Content = "キャンセル", IsCancel = true });
        var start = new Button { Content = "録画を開始", Background = Brushes.IndianRed, Foreground = Brushes.White, IsDefault = true };
        start.Click += (_, _) => DialogResult = true; buttons.Children.Add(start); panel.Children.Add(buttons);
    }
}
