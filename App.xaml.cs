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
        base.OnStartup(e);
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
