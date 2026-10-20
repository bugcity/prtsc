using System.Drawing;
using ScreenRecorderLib;

namespace Prtsc;

internal sealed record RecordingRequest(Rectangle Bounds, bool SystemAudio = false, bool Microphone = false, bool Mouse = true)
{
    internal static Rectangle EvenBounds(Rectangle bounds)
    {
        if (bounds.Width < 2 || bounds.Height < 2) throw new ArgumentException("2 × 2 px 以上の範囲を選択してください。");
        return new Rectangle(bounds.X, bounds.Y, bounds.Width & ~1, bounds.Height & ~1);
    }
    internal static List<RecordingSourceBase> Sources(Rectangle bounds, IEnumerable<(string Name, Rectangle Bounds)> screens, bool mouse)
    {
        var sources = new List<RecordingSourceBase>();
        foreach (var screen in screens)
        {
            var part = Rectangle.Intersect(bounds, screen.Bounds);
            if (part.Width <= 0 || part.Height <= 0) continue;
            sources.Add(new DisplayRecordingSource(screen.Name)
            {
                RecorderApi = RecorderApi.DesktopDuplication,
                SourceRect = new ScreenRect(part.X - screen.Bounds.X, part.Y - screen.Bounds.Y, part.Width, part.Height),
                OutputSize = new ScreenSize(part.Width, part.Height), Position = new ScreenPoint(part.X - bounds.X, part.Y - bounds.Y),
                IsCursorCaptureEnabled = mouse, AnchorPoint = Anchor.TopLeft, Stretch = StretchMode.Fill
            });
        }
        if (sources.Count == 0) throw new InvalidOperationException("録画できるモニターが見つかりません。");
        return sources;
    }
    internal RecorderOptions CreateOptions(IEnumerable<RecordingSourceBase> sources, ImageOverlay? halo)
    {
        var audio = new List<AudioSourceBase>();
        if (SystemAudio) audio.Add(LoopbackAudioSource.Default ?? throw new InvalidOperationException("PC の音声デバイスが見つかりません。"));
        if (Microphone) audio.Add(CaptureAudioSource.Default ?? throw new InvalidOperationException("マイクが見つかりません。"));
        return new RecorderOptions
        {
            SourceOptions = new SourceOptions { RecordingSources = sources.ToList() },
            OutputOptions = new OutputOptions { RecorderMode = RecorderMode.Video, OutputFrameSize = new ScreenSize(Bounds.Width, Bounds.Height), Stretch = StretchMode.Fill },
            VideoEncoderOptions = new VideoEncoderOptions { Framerate = 30, IsFixedFramerate = true, IsHardwareEncodingEnabled = true, IsThrottlingDisabled = false, Quality = 75, Encoder = new H264VideoEncoder { BitrateMode = H264BitrateControlMode.Quality, EncoderProfile = H264Profile.Main }, IsMp4FastStartEnabled = true },
            AudioOptions = new AudioOptions { IsAudioEnabled = SystemAudio || Microphone, AudioSources = audio, Channels = AudioChannels.Stereo, Bitrate = AudioBitrate.bitrate_128kbps },
            MouseOptions = new MouseOptions { IsMousePointerEnabled = Mouse, IsMouseClicksDetected = false },
            OverlayOptions = new OverLayOptions { Overlays = halo is null ? [] : [halo] },
            SnapshotOptions = new SnapshotOptions { SnapshotsWithVideo = false }
        };
    }
}
