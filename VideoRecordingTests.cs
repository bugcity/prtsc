using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using ScreenRecorderLib;

namespace Prtsc;

internal static class VideoRecordingTests
{
    private static void Check(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
    internal static void Wait(Task task, int milliseconds = 30000)
    {
        var frame = new DispatcherFrame(); var clock = System.Diagnostics.Stopwatch.StartNew();
        var timer = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromMilliseconds(10) };
        timer.Tick += (_, _) => { if (task.IsCompleted || clock.ElapsedMilliseconds > milliseconds) frame.Continue = false; };
        timer.Start(); Dispatcher.PushFrame(frame); timer.Stop();
        if (!task.IsCompleted) throw new TimeoutException("Recording test timed out"); task.GetAwaiter().GetResult();
    }
    internal static void Run(string root)
    {
        Directory.CreateDirectory(root);
        var animation = new MouseHighlight(); Check(animation.Diameter(0) == 24, "Moving cursor highlight must be 24 physical pixels");
        animation.SetPressed(true, 1); Check(animation.Diameter(1) == 24 && animation.Diameter(1.075) > 24 && animation.Diameter(1.075) < 48 && animation.Diameter(2) == 48, "Press must smoothly expand to 48 pixels and retain that size during a drag");
        animation.SetPressed(false, 2); Check(animation.Diameter(2) == 48 && animation.Diameter(2.075) > 24 && animation.Diameter(3) == 24, "Release must smoothly shrink to 24 pixels");
        animation.SetPressed(true, 4); double before = animation.Diameter(4.05); animation.SetPressed(false, 4.05);
        Check(animation.Diameter(4.05) == before, "Rapid clicks must reverse the animation without jumping");
        var sprite = MouseHighlight.Sprite(); byte[] spritePixels = new byte[48 * 48 * 4]; sprite.CopyPixels(spritePixels, 48 * 4, 0);
        Check(spritePixels[(24 * 48 + 24) * 4 + 3] == 90 && spritePixels[3] == 0, "Mouse overlay must use a translucent red circle with transparent corners");
        var screens = new[] { ("left", new System.Drawing.Rectangle(-1920, 0, 1920, 1080)), ("right", new System.Drawing.Rectangle(0, 0, 1920, 1080)) };
        var bounds = RecordingRequest.EvenBounds(new(-120, 50, 301, 201));
        Check(bounds == new System.Drawing.Rectangle(-120, 50, 300, 200), "H264 output must have even dimensions without changing the origin");
        var sources = RecordingRequest.Sources(bounds, screens, true).Cast<DisplayRecordingSource>().ToArray();
        Check(sources.Length == 2 && sources[0].Position.Left == 0 && sources[1].Position.Left == 120 && sources[0].SourceRect.Left == 1800, "Recording coordinates must preserve negative monitor positions and span monitors");
        var quiet = new RecordingRequest(new(0, 0, 320, 180), Mouse: false).CreateOptions([new ImageRecordingSource("not-opened.png")], null);
        Check(quiet.AudioOptions.IsAudioEnabled == false && quiet.AudioOptions.AudioSources.Count == 0 && quiet.VideoEncoderOptions.Framerate == 30 && quiet.MouseOptions.IsMousePointerEnabled == false, "No audio device may be opened when both audio checkboxes are off");
        var dialog = new RecordingDialog(); Check(dialog.SystemAudio.IsChecked != true && dialog.Microphone.IsChecked != true && dialog.MouseActions.IsChecked == true, "Recording setup must default to silent video with mouse feedback enabled");
        var panel = (FrameworkElement)dialog.Content; panel.Measure(new System.Windows.Size(480, double.PositiveInfinity)); panel.Arrange(new Rect(0, 0, 480, panel.DesiredSize.Height)); panel.UpdateLayout();
        var visual = new DrawingVisual();
        using (var drawing = visual.RenderOpen()) { var area = new Rect(0, 0, panel.ActualWidth, panel.ActualHeight); drawing.DrawRectangle(Brushes.White, null, area); drawing.DrawRectangle(new VisualBrush(panel), null, area); }
        var screenshot = new RenderTargetBitmap((int)Math.Ceiling(panel.ActualWidth), (int)Math.Ceiling(panel.ActualHeight), 96, 96, PixelFormats.Pbgra32); screenshot.Render(visual);
        CaptureStore.WriteImage(Path.Combine("artifacts", "recording-setup-preview.png"), screenshot); dialog.Close();
        var store = new CaptureStore(Path.Combine(root, "library")); var image = BitmapSource.Create(320, 180, 96, 96, PixelFormats.Bgra32, null, Enumerable.Repeat((byte)255, 320 * 180 * 4).ToArray(), 320 * 4);
        var dummyVideo = Path.Combine(root, "dummy.mp4"); File.WriteAllBytes(dummyVideo, [0, 0, 0, 8, 102, 116, 121, 112]);
        var entry = new CaptureEntry { IsVideo = true, Title = "video", DurationSeconds = 2, VideoWidth = 320, VideoHeight = 180 };
        store.CreateVideo(entry, dummyVideo, image); Check(store.Load().Single().IsVideo && !File.Exists(dummyVideo), "Completed videos must publish atomically enough to survive a library reload");
        var export = new CaptureDragExport(store).Create(entry); var paths = export.GetData(DataFormats.FileDrop) as string[] ?? throw new InvalidOperationException("Video export has no file paths");
        Check(Path.GetExtension(paths[0]) == ".mp4" && !export.GetDataPresent(DataFormats.Bitmap) && File.ReadAllBytes(paths[0]).SequenceEqual(File.ReadAllBytes(store.VideoPath(entry))), "Video drag/copy must produce a full MP4 snapshot rather than a preview image");
        var editor = new MainWindow(store, _ => Task.CompletedTask, false);
        Check(((FrameworkElement)editor.FindName("VideoPreview")).Visibility == Visibility.Visible && !((FrameworkElement)editor.FindName("AnnotationProperties")).IsEnabled
            && ((FrameworkElement)editor.FindName("DeleteCaptureButton")).IsEnabled, "Selecting a video must display playback controls and allow deletion without enabling image annotations"); editor.RequestExit();
        store.Delete(entry); Check(store.Load().Count == 0 && File.Exists(paths[0]), "Deleting a video must preserve an exported snapshot");
    }
    // Separate from ordinary self-tests: this encodes synthetic frames only, never captures the desktop, microphone or PC audio.
    internal static void RunNative(string root)
    {
        string report = root; root = Path.Combine(root, Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        string input = Path.Combine(root, "synthetic.gif"), output = Path.Combine(root, "recording.mp4");
        WriteAnimatedFixture(input);
        int stage = 0, smallSeen = 0, largeSeen = 0, movedSeen = 0, frames = 0;
        var capturedStages = new bool[3];
        void Preview(FrameRecordedEventArgs args)
        {
            var data = args.BitmapData; if (data is null || data.Width != 320 || data.Height != 180) return;
            byte[] pixels = new byte[data.Length]; Marshal.Copy(data.Data, pixels, 0, pixels.Length);
            bool Red(int x, int y) { int offset = y * Math.Abs(data.Stride) + x * 4; return pixels[offset + 2] > 235 && pixels[offset + 1] is > 100 and < 220; }
            int state = Volatile.Read(ref stage);
            bool expected = state switch { 0 => Red(120, 90) && !Red(138, 90), 1 => Red(138, 90), _ => Red(200, 90) && !Red(120, 90) };
            if (args.FrameNumber > 4 && expected && !capturedStages[state])
            {
                capturedStages[state] = true;
                CaptureStore.WriteImage(Path.Combine(root, $"composited-stage-{state}.png"), BitmapSource.Create(data.Width, data.Height, 96, 96, PixelFormats.Bgra32, null, pixels, Math.Abs(data.Stride)));
            }
            if (state == 0 && Red(120, 90) && !Red(138, 90)) Interlocked.Exchange(ref smallSeen, 1);
            if (state == 1 && Red(138, 90)) Interlocked.Exchange(ref largeSeen, 1);
            if (state == 2 && Red(200, 90) && !Red(120, 90)) Interlocked.Exchange(ref movedSeen, 1);
            Interlocked.Increment(ref frames);
        }
        using var session = new VideoRecordingSession(new(new(0, 0, 320, 180)), root, Dispatcher.CurrentDispatcher,
            [new ImageRecordingSource(input)], () => Volatile.Read(ref stage) switch { 0 => (120, 90, 24d), 1 => (120, 90, 48d), _ => (200, 90, 24d) }, Preview);
        var controls = new RecordingControls(session, session.Stop, session.Stop);
        new System.Windows.Interop.WindowInteropHelper(controls).EnsureHandle(); controls.CloseAfterRecording();
        Wait(session.StartAsync(output)); Wait(Task.Delay(800));
        stage = 1; Wait(Task.Delay(500)); session.TogglePause(); Wait(Task.Delay(200));
        var pausedAt = session.Elapsed; Wait(Task.Delay(250)); Check(session.Elapsed == pausedAt, "Paused time must not count toward the saved recording duration");
        session.TogglePause(); stage = 2; Wait(Task.Delay(600)); session.Stop(); Wait(session.Completion);
        Check(smallSeen == 1 && largeSeen == 1 && movedSeen == 1 && frames > 15, $"The real recording compositor must burn in translucent moving and pressed mouse highlights (small={smallSeen}, large={largeSeen}, moved={movedSeen}, frames={frames})");
        byte[] mp4 = File.ReadAllBytes(output); string atoms = System.Text.Encoding.Latin1.GetString(mp4);
        Check(mp4.Length > 1000 && atoms.Contains("ftyp") && atoms.Contains("moov") && atoms.Contains("mdat") && atoms.Contains("avc1"), "Real encoding must produce a finalized playable H264 MP4");
        var player = new MediaPlayer(); var opened = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        player.MediaOpened += (_, _) => opened.TrySetResult(); player.MediaFailed += (_, e) => opened.TrySetException(e.ErrorException);
        player.Open(new Uri(output)); Wait(opened.Task);
        Check(player.NaturalVideoWidth == 320 && player.NaturalVideoHeight == 180 && player.NaturalDuration.HasTimeSpan && player.NaturalDuration.TimeSpan.TotalSeconds >= 1,
            $"The generated MP4 must decode with the expected dimensions and duration (width={player.NaturalVideoWidth}, height={player.NaturalVideoHeight}, duration={player.NaturalDuration}, frames={frames})"); player.Close();
        File.WriteAllText(Path.Combine(report, "result.txt"), "PASS: native H264 encoding, actual video compositor (24/48px alpha red halo and movement), pause/resume, finalization and MP4 decoding; synthetic image only, no audio or desktop capture.\n" + root);
    }
    private static void WriteAnimatedFixture(string path)
    {
        // A looping two-frame GIF, white except one changing corner pixel. It supplies fresh
        // frames after Resume without opening a window or capturing any desktop content.
        using var stream = File.Create(path); using var writer = new BinaryWriter(stream);
        writer.Write(System.Text.Encoding.ASCII.GetBytes("GIF89a")); writer.Write((ushort)320); writer.Write((ushort)180);
        writer.Write(new byte[] { 0x80, 0, 0, 255, 255, 255, 0, 0, 255 });
        writer.Write(new byte[] { 0x21, 0xFF, 11 }); writer.Write(System.Text.Encoding.ASCII.GetBytes("NETSCAPE2.0")); writer.Write(new byte[] { 3, 1, 0, 0, 0 });
        for (int frame = 0; frame < 2; frame++)
        {
            writer.Write(new byte[] { 0x21, 0xF9, 4, 4, 10, 0, 0, 0, 0x2C });
            writer.Write((ushort)0); writer.Write((ushort)0); writer.Write((ushort)320); writer.Write((ushort)180); writer.Write((byte)0); writer.Write((byte)2);
            var data = new List<byte>(); int packed = 0, bits = 0;
            void Code(int code) { packed |= code << bits; bits += 3; while (bits >= 8) { data.Add((byte)packed); packed >>= 8; bits -= 8; } }
            for (int pixel = 0; pixel < 320 * 180; pixel += 2) { Code(4); Code(pixel == 0 ? frame : 0); Code(0); }
            Code(5); if (bits > 0) data.Add((byte)packed);
            for (int offset = 0; offset < data.Count; offset += 255) { int count = Math.Min(255, data.Count - offset); writer.Write((byte)count); writer.Write(data.Skip(offset).Take(count).ToArray()); }
            writer.Write((byte)0);
        }
        writer.Write((byte)0x3B);
    }
}
