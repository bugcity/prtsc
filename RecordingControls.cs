using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using ScreenRecorderLib;
using Button = System.Windows.Controls.Button;

namespace Prtsc;

internal sealed class RecordingControls : Window
{
    private readonly TextBlock status = new() { Foreground = Brushes.White, MinWidth = 150, Margin = new Thickness(8, 0, 12, 0) };
    private readonly Button pause = new() { Content = "一時停止" };
    private readonly Button stop = new() { Content = "停止して保存", Background = Brushes.IndianRed, Foreground = Brushes.White };
    private readonly Button cancel = new() { Content = "破棄" };
    private readonly DispatcherTimer timer;
    private readonly Action stopRecording;
    private bool allowClose;
    internal RecordingControls(VideoRecordingSession session, Action stopRecording, Action cancelRecording)
    {
        this.stopRecording = stopRecording;
        Title = "Prtsc — 録画中"; Icon = AppIcon.Source; ShowInTaskbar = false; Topmost = true;
        WindowStyle = WindowStyle.None; ResizeMode = ResizeMode.NoResize; SizeToContent = SizeToContent.WidthAndHeight;
        WindowStartupLocation = WindowStartupLocation.CenterScreen; Background = new SolidColorBrush(Color.FromRgb(23, 28, 46));
        var panel = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(10) }; Content = panel;
        status.Cursor = System.Windows.Input.Cursors.SizeAll; status.ToolTip = "ドラッグして録画パネルを移動";
        status.MouseLeftButtonDown += (_, _) => DragMove();
        panel.Children.Add(status); panel.Children.Add(pause); panel.Children.Add(stop); panel.Children.Add(cancel);
        pause.Click += (_, _) => { session.TogglePause(); Update(); };
        stop.Click += (_, _) => stopRecording(); cancel.Click += (_, _) => cancelRecording();
        timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(200) }; timer.Tick += (_, _) => Update();
        void Update()
        {
            if (session.Status == RecorderStatus.Idle) { status.Text = "録画を準備中…"; pause.IsEnabled = false; return; }
            pause.IsEnabled = session.Status is RecorderStatus.Recording or RecorderStatus.Paused;
            bool paused = session.Status == RecorderStatus.Paused;
            status.Text = (paused ? "❚❚ 一時停止 " : "● 録画中 ") + session.Elapsed.ToString(@"hh\:mm\:ss"); pause.Content = paused ? "再開" : "一時停止";
        }
        SourceInitialized += (_, _) =>
        {
            if (!Recorder.SetExcludeFromCapture(new WindowInteropHelper(this).Handle, true))
                throw new InvalidOperationException("録画パネルを映像から除外できませんでした。");
        };
        Loaded += (_, _) => { var area = SystemParameters.WorkArea; Left = Math.Max(area.Left, area.Right - ActualWidth - 16); Top = Math.Max(area.Top, area.Bottom - ActualHeight - 16); Update(); timer.Start(); };
        Closing += OnClosing; Closed += (_, _) => timer.Stop();
    }
    internal void Finishing() { pause.IsEnabled = stop.IsEnabled = cancel.IsEnabled = false; timer.Stop(); status.Text = "保存しています…"; }
    internal void CloseAfterRecording() { allowClose = true; Close(); }
    private void OnClosing(object? sender, CancelEventArgs e) { if (!allowClose) { e.Cancel = true; stopRecording(); } }
}
