using System.Windows;

namespace Prtsc;

public partial class App : System.Windows.Application
{
    private SingleInstanceGate? instance;
    protected override void OnExit(ExitEventArgs e) { instance?.Dispose(); base.OnExit(e); }
    protected override void OnSessionEnding(SessionEndingCancelEventArgs e)
    {
        base.OnSessionEnding(e);
        if (!e.Cancel && MainWindow is Prtsc.MainWindow editor) editor.PrepareForShutdown();
    }
    protected override void OnStartup(StartupEventArgs e)
    {
        if (e.Args.Contains(BrowserPageCapture.WorkerArgument))
        {
            ShutdownMode = ShutdownMode.OnExplicitShutdown;
            try { Task.Run(BrowserPageCapture.RunWorker).GetAwaiter().GetResult(); Shutdown(0); }
            catch (Exception) { Shutdown(1); }
            return;
        }
        var preferences = new CaptureSettingsStore(System.IO.Path.Combine(CaptureStore.DefaultRoot, "settings.json")).Load();
        System.Windows.Media.RenderOptions.ProcessRenderMode = preferences.RenderingMethod == "Automatic"
            ? System.Windows.Interop.RenderMode.Default : System.Windows.Interop.RenderMode.SoftwareOnly;
        base.OnStartup(e);
        if (e.Args.Contains("--live-capture-self-test"))
        {
            ShutdownMode = ShutdownMode.OnExplicitShutdown;
            try { CaptureOverlayTests.Run(); CaptureOverlayTests.RunLive(); System.IO.File.WriteAllText("self-test-live-capture-result.txt", "PASS: transparent live selection, current pixels after selection, bounded loupe samples and timer cleanup."); Shutdown(0); }
            catch (Exception ex) { System.IO.File.WriteAllText("self-test-live-capture-error.txt", ex.ToString()); Shutdown(1); }
            return;
        }
        if (e.Args.Contains("--recording-self-test"))
        {
            ShutdownMode = ShutdownMode.OnExplicitShutdown;
            try { VideoRecordingTests.RunNative(System.IO.Path.Combine(Environment.CurrentDirectory, "artifacts", "recording-test")); Shutdown(0); }
            catch (Exception ex) { System.IO.Directory.CreateDirectory("artifacts"); System.IO.File.WriteAllText(System.IO.Path.Combine("artifacts", "recording-test-error.txt"), ex.ToString()); Shutdown(1); }
            return;
        }
        if (e.Args.Contains("--self-test"))
        {
            ShutdownMode = ShutdownMode.OnExplicitShutdown;
            try { SelfTest.Run(); Shutdown(0); }
            catch (Exception ex) { System.IO.File.WriteAllText("self-test-error.txt", ex.ToString()); Shutdown(1); }
            return;
        }
        ShutdownMode = ShutdownMode.OnExplicitShutdown;
        var user = System.Security.Principal.WindowsIdentity.GetCurrent().User!.Value;
        instance = new SingleInstanceGate(@"Local\Prtsc." + user);
        if (!instance.IsFirst) { Shutdown(); return; }
        var editor = new MainWindow(); MainWindow = editor;
        instance.Listen(() => { if (!Dispatcher.HasShutdownStarted) Dispatcher.BeginInvoke(new Action(editor.ShowExistingInstance)); });
        if (e.Args.Contains("--startup"))
        {
            new System.Windows.Interop.WindowInteropHelper(editor).EnsureHandle(); editor.HideToTray();
        }
        else editor.Show();
    }
}
