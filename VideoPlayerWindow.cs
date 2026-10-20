using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using Button = System.Windows.Controls.Button;

namespace Prtsc;

internal sealed class VideoPlayerWindow : Window
{
    private readonly MediaElement media;
    internal VideoPlayerWindow(CaptureEntry entry, string path, Action save)
    {
        Title = entry.Title + " — Prtsc"; Icon = AppIcon.Source; Width = 920; Height = 620; MinWidth = 500; MinHeight = 360;
        Background = Brushes.Black; WindowStartupLocation = WindowStartupLocation.CenterScreen;
        var grid = new Grid(); grid.RowDefinitions.Add(new RowDefinition()); grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); Content = grid;
        media = new MediaElement { Source = new Uri(path), LoadedBehavior = MediaState.Manual, UnloadedBehavior = MediaState.Close, Stretch = Stretch.Uniform, Volume = 1 }; grid.Children.Add(media);
        var controls = new DockPanel { Background = Brushes.White, LastChildFill = true, Margin = new Thickness(0) }; Grid.SetRow(controls, 1); grid.Children.Add(controls);
        var play = new Button { Content = "一時停止" }; DockPanel.SetDock(play, Dock.Left); controls.Children.Add(play);
        var export = new Button { Content = "MP4 を保存" }; DockPanel.SetDock(export, Dock.Right); controls.Children.Add(export); export.Click += (_, _) => save();
        var time = new TextBlock { Width = 160, TextAlignment = TextAlignment.Center }; DockPanel.SetDock(time, Dock.Right); controls.Children.Add(time);
        var seek = new Slider { Minimum = 0, Maximum = Math.Max(1, entry.DurationSeconds), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(10) }; controls.Children.Add(seek);
        bool paused = false, syncing = false;
        play.Click += (_, _) => { paused = !paused; if (paused) media.Pause(); else media.Play(); play.Content = paused ? "再生" : "一時停止"; };
        seek.ValueChanged += (_, _) => { if (!syncing) media.Position = TimeSpan.FromSeconds(seek.Value); };
        media.MediaOpened += (_, _) => { if (media.NaturalDuration.HasTimeSpan) seek.Maximum = Math.Max(entry.DurationSeconds, media.NaturalDuration.TimeSpan.TotalSeconds); };
        media.MediaEnded += (_, _) => { media.Pause(); media.Position = TimeSpan.Zero; paused = true; play.Content = "再生"; };
        media.MediaFailed += (_, e) => { time.Text = "動画を再生できません"; time.ToolTip = e.ErrorException.Message; };
        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) };
        timer.Tick += (_, _) => { syncing = true; seek.Value = media.Position.TotalSeconds; syncing = false; time.Text = media.Position.ToString(@"hh\:mm\:ss") + " / " + TimeSpan.FromSeconds(seek.Maximum).ToString(@"hh\:mm\:ss"); };
        Loaded += (_, _) => { media.Play(); timer.Start(); }; Closed += (_, _) => { timer.Stop(); media.Close(); media.Source = null; };
    }
}
