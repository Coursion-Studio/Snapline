using System.Windows;
using Snapline.Core;

namespace Snapline;

public partial class App : Application
{
    private Mutex? _mutex;

    protected override void OnStartup(StartupEventArgs e)
    {
        bool dev = e.Args.Any(a => a.StartsWith("--dev", StringComparison.OrdinalIgnoreCase) || a == "--export-icons");
        if (!dev)
        {
            _mutex = new Mutex(true, "Snapline.SingleInstance.v1", out bool created);
            if (!created) { Shutdown(); return; }
        }

        base.OnStartup(e);
        DispatcherUnhandledException += (_, a) =>
        {
            Log.Error(a.Exception);
            a.Handled = true;
        };
        Snap.Initialize(e.Args);
    }

    protected override void OnExit(ExitEventArgs e)
    {
        Snap.Shutdown();
        base.OnExit(e);
    }
}
