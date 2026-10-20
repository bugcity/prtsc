using System.Windows;

namespace Prtsc;

public partial class App : System.Windows.Application
{
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
        MainWindow = new MainWindow(); MainWindow.Show();
    }
}
