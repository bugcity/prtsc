using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace Prtsc;

internal sealed class SettingsDialog : Window
{
    internal System.Windows.Controls.TextBox CountInput { get; }
    internal CheckBox Unlimited { get; }
    internal TextBlock Summary { get; }
    internal System.Windows.Controls.Button ApplyButton { get; }
    public int Limit { get; private set; }
    public SettingsDialog(int currentLimit, int libraryCount)
    {
        Title = "ライブラリの設定"; Icon = AppIcon.Source; Width = 440; SizeToContent = SizeToContent.Height;
        ResizeMode = ResizeMode.NoResize; WindowStartupLocation = WindowStartupLocation.CenterOwner; Background = Brushes.White;
        var panel = new StackPanel { Margin = new Thickness(24) };
        panel.Children.Add(new TextBlock { Text = "画像の保存枚数", FontSize = 21, FontWeight = FontWeights.SemiBold });
        panel.Children.Add(new TextBlock { Text = "上限を超えた画像は古い順に削除します。\n画像の元データと注釈も削除されます。", Foreground = Brushes.SlateGray, Margin = new Thickness(0, 12, 0, 18), TextWrapping = TextWrapping.Wrap });
        Unlimited = new CheckBox { Content = "無制限に保存する", IsChecked = currentLimit == 0, Margin = new Thickness(0, 0, 0, 14) }; panel.Children.Add(Unlimited);
        var row = new StackPanel { Orientation = Orientation.Horizontal };
        CountInput = new System.Windows.Controls.TextBox { Text = (currentLimit == 0 ? 100 : currentLimit).ToString(CultureInfo.InvariantCulture), Width = 130, Padding = new Thickness(8), FontSize = 16 };
        row.Children.Add(CountInput); row.Children.Add(new TextBlock { Text = "枚まで保存", Margin = new Thickness(10, 0, 0, 0) }); panel.Children.Add(row);
        Summary = new TextBlock { Margin = new Thickness(0, 16, 0, 18), TextWrapping = TextWrapping.Wrap, Foreground = Brushes.SlateGray }; panel.Children.Add(Summary);
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        buttons.Children.Add(new System.Windows.Controls.Button { Content = "キャンセル", IsCancel = true });
        ApplyButton = new System.Windows.Controls.Button { Content = "保存して適用", Background = new SolidColorBrush(System.Windows.Media.Color.FromRgb(99, 102, 241)), Foreground = Brushes.White };
        ApplyButton.Click += (_, _) => DialogResult = true; buttons.Children.Add(ApplyButton); panel.Children.Add(buttons); Content = panel;
        void Update()
        {
            bool unlimited = Unlimited.IsChecked == true;
            bool valid = int.TryParse(CountInput.Text, NumberStyles.None, CultureInfo.InvariantCulture, out int count) && count is > 0 and <= CaptureSettings.MaximumLimit;
            CountInput.IsEnabled = !unlimited; ApplyButton.IsEnabled = unlimited || valid; Limit = unlimited ? 0 : count;
            Summary.Text = unlimited ? $"現在 {libraryCount} 枚。自動削除しません。" : !valid ? "1〜100,000 の整数を入力してください。"
                : libraryCount > count ? $"適用すると、古い画像 {libraryCount - count} 枚を削除します。" : $"現在 {libraryCount} 枚。上限を超えた分を古い順に削除します。";
        }
        Unlimited.Checked += (_, _) => Update(); Unlimited.Unchecked += (_, _) => Update(); CountInput.TextChanged += (_, _) => Update(); Update();
    }
}
