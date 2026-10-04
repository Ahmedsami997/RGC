using Avalonia;
using Avalonia.Controls;
using RGC.MacAgent.Services;

namespace RGC.MacAgent;

internal static class Program
{
    private static FileStream? _instanceLock;

    [STAThread]
    public static int Main(string[] args)
    {
        // One agent per user: a second launch (e.g. login item + manual open) exits quietly.
        try
        {
            _instanceLock = new FileStream(Path.Combine(MacPaths.DataDir, "agent.lock"),
                FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
        }
        catch (IOException)
        {
            return 0;
        }

        AppDomain.CurrentDomain.UnhandledException += (_, e) => AgentLog.Error("Unhandled exception", e.ExceptionObject as Exception);
        TaskScheduler.UnobservedTaskException += (_, e) =>
        {
            AgentLog.Error("Unobserved task exception", e.Exception);
            e.SetObserved();
        };

        return BuildAvaloniaApp().StartWithClassicDesktopLifetime(args, ShutdownMode.OnExplicitShutdown);
    }

    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<App>()
            .UsePlatformDetect()
            // Menu-bar app: no Dock icon (Info.plist also sets LSUIElement).
            .With(new MacOSPlatformOptions { ShowInDock = false })
            .LogToTrace();
}
