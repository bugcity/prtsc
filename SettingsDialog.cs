using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Input;

namespace Prtsc;

internal sealed class SettingsDialog : Window
{
    internal System.Windows.Controls.TextBox CountInput { get; }
    internal CheckBox Unlimited { get; }
    internal TextBlock Summary { get; }
    internal System.Windows.Controls.Button ApplyButton { get; }
    public int Limit { get; private set; }
    public uint HotkeyModifiers { get; private set; }
    public uint HotkeyKey { get; private set; }
    public bool StartWithWindows => startup.IsChecked == true;
    internal ComboBox RenderingInput { get; }
    internal System.Windows.Controls.TextBox NotificationInput { get; }
    public int NotificationSeconds { get; private set; }
    public string RenderingMethod => RenderingInput.SelectedIndex == 1 ? "Automatic" : "Software";
    private readonly CheckBox startup;
    public SettingsDialog(int currentLimit, int libraryCount, CaptureSettings? preferences = null)
    {
        preferences ??= new(); HotkeyModifiers = preferences.HotkeyModifiers; HotkeyKey = preferences.HotkeyKey;
        Title = "Prtsc の設定"; Icon = AppIcon.Source; Width = 480; SizeToContent = SizeToContent.Height;
        ResizeMode = ResizeMode.NoResize; WindowStartupLocation = WindowStartupLocation.CenterOwner; Background = Brushes.White;
        var panel = new StackPanel { Margin = new Thickness(24) };
        panel.Children.Add(new TextBlock { Text = "ライブラリの保存上限", FontSize = 21, FontWeight = FontWeights.SemiBold });
        panel.Children.Add(new TextBlock { Text = "画像・動画をそれぞれ1件として数えます。\n上限を超えた古い項目は、元データごと削除します。", Foreground = Brushes.SlateGray, Margin = new Thickness(0, 12, 0, 18), TextWrapping = TextWrapping.Wrap });
        Unlimited = new CheckBox { Content = "無制限に保存する", IsChecked = currentLimit == 0, Margin = new Thickness(0, 0, 0, 14) }; panel.Children.Add(Unlimited);
        var row = new StackPanel { Orientation = Orientation.Horizontal };
        CountInput = new System.Windows.Controls.TextBox { Text = (currentLimit == 0 ? 100 : currentLimit).ToString(CultureInfo.InvariantCulture), Width = 130, Padding = new Thickness(8), FontSize = 16 };
        row.Children.Add(CountInput); row.Children.Add(new TextBlock { Text = "枚まで保存", Margin = new Thickness(10, 0, 0, 0) }); panel.Children.Add(row);
        Summary = new TextBlock { Margin = new Thickness(0, 16, 0, 18), TextWrapping = TextWrapping.Wrap, Foreground = Brushes.SlateGray }; panel.Children.Add(Summary);
        panel.Children.Add(new TextBlock { Text = "撮影ホットキー", FontWeight = FontWeights.SemiBold });
        var hotkey = new System.Windows.Controls.TextBox { IsReadOnly = true, Text = HotkeyBinding.Label(HotkeyModifiers, HotkeyKey), Padding = new Thickness(8), Margin = new Thickness(0, 8, 0, 6) }; panel.Children.Add(hotkey);
        var hotkeyHelp = new TextBlock { Text = "欄をクリックしてキーを押してください。単独キーは PrtSc / F1〜F24、それ以外は Ctrl・Alt・Shift と組み合わせます。", TextWrapping = TextWrapping.Wrap, Foreground = Brushes.SlateGray }; panel.Children.Add(hotkeyHelp);
        hotkey.PreviewKeyDown += (_, e) =>
        {
            var key = e.Key == Key.System ? e.SystemKey : e.Key;
            if (key is Key.Tab or Key.Escape) return;
            e.Handled = true;
            uint modifiers = (Keyboard.Modifiers.HasFlag(ModifierKeys.Control) ? 2u : 0) | (Keyboard.Modifiers.HasFlag(ModifierKeys.Alt) ? 1u : 0) | (Keyboard.Modifiers.HasFlag(ModifierKeys.Shift) ? 4u : 0);
            uint virtualKey = (uint)KeyInterop.VirtualKeyFromKey(key);
            if (Keyboard.Modifiers.HasFlag(ModifierKeys.Windows) || !HotkeyBinding.IsValid(modifiers, virtualKey)) return;
            HotkeyModifiers = modifiers; HotkeyKey = virtualKey; hotkey.Text = HotkeyBinding.Label(modifiers, virtualKey);
        };
        var reset = new System.Windows.Controls.Button { Content = "PrtSc に戻す", HorizontalAlignment = HorizontalAlignment.Left };
        reset.Click += (_, _) => { HotkeyModifiers = 0; HotkeyKey = 0x2C; hotkey.Text = "PrtSc"; }; panel.Children.Add(reset);
        startup = new CheckBox { Content = "Windows 起動時にトレイで起動する", IsChecked = preferences.StartWithWindows, Margin = new Thickness(0, 14, 0, 18) }; panel.Children.Add(startup);
        panel.Children.Add(new TextBlock { Text = "画面の描画方法", FontWeight = FontWeights.SemiBold });
        RenderingInput = new ComboBox { Margin = new Thickness(0, 8, 0, 6), ItemsSource = new[] { "ソフトウェア（既定・省メモリ）", "ハードウェア優先（自動判定）" }, SelectedIndex = preferences.RenderingMethod == "Automatic" ? 1 : 0 };
        panel.Children.Add(RenderingInput);
        panel.Children.Add(new TextBlock { Text = "描画方法はアプリの再起動後に反映されます。", Foreground = Brushes.SlateGray, Margin = new Thickness(0, 0, 0, 14) });
        panel.Children.Add(new TextBlock { Text = "撮影後の通知", FontWeight = FontWeights.SemiBold });
        var notificationRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 8, 0, 6) };
        NotificationInput = new System.Windows.Controls.TextBox { Text = preferences.NotificationSeconds.ToString(CultureInfo.InvariantCulture), Width = 100, Padding = new Thickness(8) };
        notificationRow.Children.Add(NotificationInput);
        notificationRow.Children.Add(new TextBlock { Text = "秒で消える", Margin = new Thickness(10, 0, 0, 0) }); panel.Children.Add(notificationRow);
        var notificationHelp = new TextBlock { Text = "0〜60秒。0秒なら通知を表示しません。", Foreground = Brushes.SlateGray, Margin = new Thickness(0, 0, 0, 14) }; panel.Children.Add(notificationHelp);
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        buttons.Children.Add(new System.Windows.Controls.Button { Content = "キャンセル", IsCancel = true });
        ApplyButton = new System.Windows.Controls.Button { Content = "保存して適用", Background = new SolidColorBrush(System.Windows.Media.Color.FromRgb(99, 102, 241)), Foreground = Brushes.White };
        ApplyButton.Click += (_, _) => DialogResult = true; buttons.Children.Add(ApplyButton);
        // Keep actions visible when the settings body needs scrolling on a small display.
        var layout = new DockPanel { MaxHeight = Math.Max(300, SystemParameters.WorkArea.Height - 60) };
        buttons.Margin = new Thickness(24, 0, 24, 24); DockPanel.SetDock(buttons, Dock.Bottom); layout.Children.Add(buttons);
        layout.Children.Add(new ScrollViewer { Content = panel, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled }); Content = layout;
        void Update()
        {
            bool unlimited = Unlimited.IsChecked == true;
            bool valid = int.TryParse(CountInput.Text, NumberStyles.None, CultureInfo.InvariantCulture, out int count) && count is > 0 and <= CaptureSettings.MaximumLimit;
            bool validNotification = int.TryParse(NotificationInput.Text, NumberStyles.None, CultureInfo.InvariantCulture, out int seconds)
                && seconds is >= 0 and <= CaptureSettings.MaximumNotificationSeconds;
            NotificationSeconds = validNotification ? seconds : preferences.NotificationSeconds;
            notificationHelp.Text = validNotification ? "0〜60秒。0秒なら通知を表示しません。" : "通知時間は0〜60の整数を入力してください。";
            notificationHelp.Foreground = validNotification ? Brushes.SlateGray : Brushes.Firebrick;
            CountInput.IsEnabled = !unlimited; ApplyButton.IsEnabled = (unlimited || valid) && validNotification; Limit = unlimited ? 0 : count;
            Summary.Text = unlimited ? $"現在 {libraryCount} 枚。自動削除しません。" : !valid ? "1〜100,000 の整数を入力してください。"
                : libraryCount > count ? $"適用すると、古い画像 {libraryCount - count} 枚を削除します。" : $"現在 {libraryCount} 枚。上限を超えた分を古い順に削除します。";
        }
        Unlimited.Checked += (_, _) => Update(); Unlimited.Unchecked += (_, _) => Update(); CountInput.TextChanged += (_, _) => Update(); NotificationInput.TextChanged += (_, _) => Update(); Update();
    }
}
