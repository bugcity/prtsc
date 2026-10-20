using System.Diagnostics;
using System.IO;
using System.Windows.Threading;
using ScreenRecorderLib;

namespace Prtsc;

internal sealed class VideoRecordingSession : IDisposable
{
    private readonly Recorder recorder;
    private readonly RecordingRequest request;
    private readonly DispatcherTimer timer;
    private readonly ImageOverlay? halo;
    private RecordingMouseInput? mouse;
    private readonly TaskCompletionSource started = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TaskCompletionSource<string> complete = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly Stopwatch elapsed = new();
    private readonly Func<(int X, int Y, double Diameter)>? mouseSample;
    private bool stopping, disposed;
    internal RecorderStatus Status => recorder.Status;
    internal bool IsStopping => stopping;
    internal TimeSpan Elapsed => elapsed.Elapsed;
    internal Task<string> Completion => complete.Task;
    internal VideoRecordingSession(RecordingRequest request, string work, Dispatcher dispatcher, IEnumerable<RecordingSourceBase>? sources = null,
        Func<(int X, int Y, double Diameter)>? mouseSample = null, Action<FrameRecordedEventArgs>? preview = null)
    {
        this.request = request; this.mouseSample = mouseSample; Directory.CreateDirectory(work);
        if (request.Mouse)
        {
            var spritePath = Path.Combine(work, "mouse-highlight.png"); CaptureStore.WriteImage(spritePath, MouseHighlight.Sprite());
            halo = new ImageOverlay { SourcePath = spritePath, AnchorPoint = Anchor.TopLeft, Offset = new ScreenSize(-100, -100), Size = new ScreenSize(24, 24), Stretch = StretchMode.Fill };
        }
        sources ??= RecordingRequest.Sources(request.Bounds, System.Windows.Forms.Screen.AllScreens.Select(s => (s.DeviceName, s.Bounds)), request.Mouse);
        var options = request.CreateOptions(sources, halo);
        if (preview is not null)
        {
            options.OutputOptions.IsVideoFramePreviewEnabled = true; options.OutputOptions.VideoFramePreviewSize = new ScreenSize(request.Bounds.Width, request.Bounds.Height);
            options.LogOptions = new LogOptions { IsLogEnabled = true, LogFilePath = Path.Combine(work, "native-recorder.log"), LogSeverityLevel = LogLevel.Debug };
        }
        recorder = Recorder.CreateRecorder(options);
        if (preview is not null) recorder.OnFrameRecorded += (_, e) => preview(e);
        recorder.OnStatusChanged += (_, e) => { if (e.Status == RecorderStatus.Recording) started.TrySetResult(); };
        recorder.OnRecordingComplete += (_, e) => { elapsed.Stop(); complete.TrySetResult(e.FilePath); };
        recorder.OnRecordingFailed += (_, e) => { elapsed.Stop(); var error = new InvalidOperationException("録画に失敗しました · " + e.Error); started.TrySetException(error); complete.TrySetException(error); };
        timer = new DispatcherTimer(DispatcherPriority.Render, dispatcher) { Interval = TimeSpan.FromMilliseconds(1000.0 / 60) };
        timer.Tick += (_, _) => UpdateMouse();
    }
    internal async Task StartAsync(string path)
    {
        if (stopping) throw new OperationCanceledException();
        try
        {
            if (request.Mouse && mouseSample is null) mouse = new RecordingMouseInput();
            recorder.Record(path);
            await started.Task.WaitAsync(TimeSpan.FromSeconds(20));
            elapsed.Start(); timer.Start();
        }
        catch { Stop(); throw; }
    }
    private void UpdateMouse()
    {
        if (mouse is null && mouseSample is null || halo is null || stopping || Status != RecorderStatus.Recording) return;
        try
        {
            var state = mouseSample?.Invoke() ?? mouse!.Sample();
            bool inside = request.Bounds.Contains(state.X, state.Y);
            halo.Size = new ScreenSize(state.Diameter, state.Diameter);
            halo.Offset = new ScreenSize(inside ? state.X - request.Bounds.X - state.Diameter / 2 : -100, inside ? state.Y - request.Bounds.Y - state.Diameter / 2 : -100);
            recorder.GetDynamicOptionsBuilder().SetUpdatedOverlay(halo).Apply();
        }
        catch (Exception ex) { complete.TrySetException(new InvalidOperationException("マウス操作を合成できません。", ex)); Stop(); }
    }
    internal void TogglePause()
    {
        if (stopping) return;
        if (Status == RecorderStatus.Recording) { recorder.Pause(); elapsed.Stop(); }
        else if (Status == RecorderStatus.Paused) { recorder.Resume(); elapsed.Start(); }
    }
    internal void Stop()
    {
        if (stopping || disposed) return;
        stopping = true; timer.Stop(); mouse?.Dispose(); mouse = null; elapsed.Stop(); recorder.Stop();
        started.TrySetCanceled();
    }
    public void Dispose()
    {
        if (disposed) return;
        Stop(); disposed = true; recorder.Dispose();
    }
}
