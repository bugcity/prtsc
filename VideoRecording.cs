using System.IO;
using System.Windows;
using System.Windows.Media.Imaging;

namespace Prtsc;

public partial class MainWindow
{
    private bool recordingBusy, exitAfterRecording, discardRecording;
    private VideoRecordingSession? recording;
    private RecordingControls? recordingControls;
    private Window? recordingSetup;
    private readonly Dictionary<string, VideoPlayerWindow> videoPlayers = [];
    private async void StartVideoRecording(object sender, RoutedEventArgs e) => await RecordVideo();
    private async Task RecordVideo()
    {
        if (recordingBusy || capturing || settingsOpen || !IsEnabled || closing) return;
        recordingBusy = true; discardRecording = false;
        bool restoreEditor = IsVisible && WindowState != WindowState.Minimized, restoreGallery = gallery?.IsVisible == true;
        string? work = null;
        bool published = false;
        try
        {
            var dialog = new RecordingDialog(); recordingSetup = dialog;
            if (restoreEditor) dialog.Owner = this; else if (restoreGallery) dialog.Owner = gallery;
            if (dialog.ShowDialog() != true || exitAfterRecording) return;
            recordingSetup = null;
            toast?.Close(); gallery?.Hide(); Hide();
            foreach (var player in videoPlayers.Values.ToArray()) player.Close();
            await Task.Delay(240);
            if (exitAfterRecording) return;
            var bounds = System.Windows.Forms.SystemInformation.VirtualScreen;
            var targets = dialog.Mode.SelectedIndex == 0 ? await WindowCapture.EnumerateTargetsAsync() : [];
            if (exitAfterRecording) return;
            BitmapSource thumbnail;
            if (dialog.Mode.SelectedIndex == 0)
            {
                var selector = new RegionWindow(targets); recordingSetup = selector;
                if (selector.ShowDialog() != true || selector.SelectedScreenBounds is not { } area || exitAfterRecording) return;
                bounds = area; thumbnail = await CaptureService.AfterSelectionAsync(bounds);
                recordingSetup = null;
            }
            else thumbnail = CaptureService.Desktop();
            if (exitAfterRecording) return;
            bounds = RecordingRequest.EvenBounds(bounds);
            var request = new RecordingRequest(bounds, dialog.SystemAudio.IsChecked == true, dialog.Microphone.IsChecked == true, dialog.MouseActions.IsChecked == true);
            work = Path.Combine(store.Root, ".recordings", Guid.NewGuid().ToString("N")); Directory.CreateDirectory(work);
            var path = Path.Combine(work, "recording.mp4");
            recording = new VideoRecordingSession(request, work, Dispatcher);
            tray?.UpdateRecording(true);
            recordingControls = new RecordingControls(recording, StopVideoRecording, () => { discardRecording = true; StopVideoRecording(); });
            recordingControls.Show();
            await Task.Delay(200);
            if (exitAfterRecording || recording.IsStopping) return;
            await recording.StartAsync(path);
            var recorded = await recording.Completion;
            var elapsed = recording.Elapsed;
            recordingControls.CloseAfterRecording(); recordingControls = null;
            recording.Dispose(); recording = null;
            if (!discardRecording)
            {
                var entry = new CaptureEntry { IsVideo = true, Title = "録画 " + DateTime.Now.ToString("HH:mm:ss"), DurationSeconds = elapsed.TotalSeconds, VideoWidth = bounds.Width, VideoHeight = bounds.Height, SystemAudio = request.SystemAudio, Microphone = request.Microphone };
                store.CreateVideo(entry, recorded, thumbnail); published = true;
                entries.Insert(0, entry); EnforceCaptureLimit(); if (current is null) History.SelectedItem = entry;
                StatusLabel.Text = "動画を保存しました · ギャラリーから再生できます";
            }
            else StatusLabel.Text = "録画を破棄しました";
        }
        catch (OperationCanceledException) { StatusLabel.Text = "録画をキャンセルしました"; }
        catch (Exception ex)
        {
            recording?.Stop();
            string recovery = work is not null && File.Exists(Path.Combine(work, "recording.mp4")) ? "\n録画ファイルの保存先：" + work : "";
            if (!closing) MessageBox.Show(ex.Message + recovery, "録画を完了できませんでした", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        finally
        {
            recordingSetup = null; recordingControls?.CloseAfterRecording(); recordingControls = null;
            recording?.Dispose(); recording = null; recordingBusy = false;
            tray?.UpdateRecording(false);
            if (work is not null && (published || discardRecording || !File.Exists(Path.Combine(work, "recording.mp4"))))
            {
                // This is a freshly generated staging directory, never a library item or user-selected folder.
                try { Directory.Delete(work, true); } catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
            }
            if (exitAfterRecording) { exitAfterRecording = false; RequestExit(); }
            else if (!closing)
            {
                if (restoreEditor) ShowEditor();
                if (restoreGallery) ShowGallery();
                if (!restoreEditor && !restoreGallery) { HideToTray(); if (published) ShowGallery(); }
            }
        }
    }
    private void StopVideoRecording()
    {
        recordingControls?.Finishing(); recording?.Stop();
    }
    private void OpenVideo(CaptureEntry entry)
    {
        if (videoPlayers.TryGetValue(entry.Id, out var existing)) { WindowActivation.Show(existing); return; }
        var player = new VideoPlayerWindow(entry, store.VideoPath(entry), () => SaveVideo(entry)); videoPlayers.Add(entry.Id, player);
        player.Closed += (_, _) => videoPlayers.Remove(entry.Id); WindowActivation.Show(player);
    }
    private void PlaySelectedVideo(object sender, RoutedEventArgs e) { if (current?.IsVideo == true) OpenVideo(current); }
    private void SaveVideo(CaptureEntry entry)
    {
        var dialog = new Microsoft.Win32.SaveFileDialog { Filter = "MP4 動画|*.mp4", FileName = "prtsc-" + entry.Created.ToString("yyyyMMdd-HHmmss") + ".mp4", AddExtension = true };
        if (dialog.ShowDialog() == true) Run(() => File.Copy(store.VideoPath(entry), dialog.FileName, true));
    }
    private void CloseVideo(CaptureEntry entry) { if (videoPlayers.TryGetValue(entry.Id, out var player)) player.Close(); }
}
