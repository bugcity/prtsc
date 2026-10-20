using System.Windows;

namespace Prtsc;

public partial class App : System.Windows.Application
{
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
        new MainWindow().Show();
    }
}
