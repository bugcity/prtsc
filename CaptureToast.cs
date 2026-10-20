using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace Prtsc;

internal sealed class CaptureToast : Window
{
    internal static readonly TimeSpan Lifetime = TimeSpan.FromSeconds(3);
    private readonly Action edit;
    private bool opened, dismissed;
    public CaptureToast(CaptureEntry entry, bool copied, Action openEditor)
    {
        edit = openEditor;
        Width = 326; Height = 240; WindowStyle = WindowStyle.None; ResizeMode = ResizeMode.NoResize;
        AllowsTransparency = true; Background = Brushes.Transparent; ShowActivated = false; ShowInTaskbar = false; Topmost = true;
        var panel = new Grid { Margin = new Thickness(14) };
        panel.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        panel.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        panel.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        panel.Children.Add(new TextBlock { Text = copied ? "✓  クリップボードにコピーしました" : "保存しました · コピーは再試行できます", Foreground = new SolidColorBrush(System.Windows.Media.Color.FromRgb(99, 102, 241)), FontSize = 12, FontWeight = FontWeights.SemiBold });
        var image = new System.Windows.Controls.Image { Source = entry.Thumbnail, Stretch = Stretch.Uniform, Margin = new Thickness(0, 12, 0, 10) };
        Grid.SetRow(image, 1); panel.Children.Add(image);
        var hint = new TextBlock { Text = "クリックして編集  ↗", FontSize = 12, Foreground = Brushes.SlateGray };
        Grid.SetRow(hint, 2); panel.Children.Add(hint);
        Content = new Border { Background = Brushes.White, CornerRadius = new CornerRadius(12), BorderBrush = new SolidColorBrush(System.Windows.Media.Color.FromRgb(210, 214, 230)), BorderThickness = new Thickness(1), Child = panel };
        MouseLeftButtonUp += (_, _) => OpenEditor();
        Loaded += (_, _) =>
        {
            var area = (System.Windows.Forms.Screen.PrimaryScreen ?? System.Windows.Forms.Screen.AllScreens[0]).WorkingArea;
            var scale = PresentationSource.FromVisual(this)?.CompositionTarget?.TransformToDevice ?? Matrix.Identity;
            int width = (int)Math.Ceiling(ActualWidth * scale.M11), height = (int)Math.Ceiling(ActualHeight * scale.M22);
            SetWindowPos(new WindowInteropHelper(this).Handle, new IntPtr(-1), area.Right - width - 18, area.Bottom - height - 18, width, height, 0x0050);
            StartFade();
        };
        Closed += (_, _) => { dismissed = true; BeginAnimation(OpacityProperty, null); };
    }
    internal void StartFade()
    {
        var fade = new DoubleAnimation(1, 0, new Duration(Lifetime));
        fade.Completed += (_, _) => Close(); BeginAnimation(OpacityProperty, fade);
    }
    internal void OpenEditor()
    {
        if (opened) return;
        opened = true;
        try { edit(); }
        finally { if (!dismissed) Close(); }
    }
    [DllImport("user32.dll")] private static extern bool SetWindowPos(IntPtr handle, IntPtr after, int x, int y, int width, int height, uint flags);
}
