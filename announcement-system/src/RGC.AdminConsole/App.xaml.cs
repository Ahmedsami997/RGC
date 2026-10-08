using System.Windows;
using System.Windows.Threading;
using RGC.AdminConsole.Services;
using RGC.AdminConsole.Views;

namespace RGC.AdminConsole;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        DispatcherUnhandledException += OnUnhandled;
        ShowLogin();
    }

    private void ShowLogin()
    {
        var login = new LoginWindow(AdminSettings.Load());
        if (login.ShowDialog() != true || login.Api is null)
        {
            Shutdown();
            return;
        }

        var main = new MainWindow(login.Api);
        main.SignedOut += ShowLogin;
        main.Closed += (_, _) => { if (!main.IsSigningOut) Shutdown(); };
        MainWindow = main;
        main.Show();
    }

    private static void OnUnhandled(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        MessageBox.Show(e.Exception.Message, "RGC – Unexpected error", MessageBoxButton.OK, MessageBoxImage.Error);
        e.Handled = true;
    }
}
