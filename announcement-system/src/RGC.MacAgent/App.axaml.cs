using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Platform;
using Avalonia.Threading;
using RGC.MacAgent.Services;
using RGC.MacAgent.Views;

namespace RGC.MacAgent;

/// <summary>
/// RGC Agent for macOS: no main window, lives in the menu bar, connects to the RGC server
/// and shows announcements as locked always-on-top popups.
/// </summary>
public sealed class App : Application
{
    private AgentConnection? _connection;
    private Notifier? _notifier;
    private TrayIcon? _tray;
    private NativeMenuItem? _statusItem;
    private NativeMenuItem? _signInItem;
    private SignInWindow? _signInWindow;

    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.ShutdownRequested += (_, _) => Notifier.SessionEnding = true;
            Dispatcher.UIThread.UnhandledException += (_, e) =>
            {
                // Keep the agent alive; it must stay connected.
                AgentLog.Error("Unhandled UI exception", e.Exception);
                e.Handled = true;
            };
            _ = StartAsync(desktop);
        }
        base.OnFrameworkInitializationCompleted();
    }

    private async Task StartAsync(IClassicDesktopStyleApplicationLifetime desktop)
    {
        var settings = MacSettings.Load();
        AgentLog.Info($"RGC Agent for Mac starting (server {settings.ServerUrl}, user {MacIdentity.CurrentUser})");

        _connection = new AgentConnection(settings, new AckStore(), MacIdentity.GetClientId());
        _notifier = new Notifier(_connection, settings);
        CreateMenuBarIcon(settings, desktop);

        _connection.StateChanged += state => Dispatcher.UIThread.Post(() => UpdateStatus(state));
        _connection.SignInRequired += () => Dispatcher.UIThread.Post(ShowSignIn);
        _connection.AnnouncementReceived += _notifier.Enqueue;

        await _connection.StartAsync();
    }

    private void CreateMenuBarIcon(MacSettings settings, IClassicDesktopStyleApplicationLifetime desktop)
    {
        _statusItem = new NativeMenuItem("Connecting…") { IsEnabled = false };
        _signInItem = new NativeMenuItem("Sign in with Microsoft 365…") { IsVisible = false };
        _signInItem.Click += (_, _) => ShowSignIn();
        var lastItem = new NativeMenuItem("Show last announcement");
        lastItem.Click += (_, _) => _notifier?.ShowLast();

        var menu = new NativeMenu();
        menu.Items.Add(_statusItem);
        menu.Items.Add(new NativeMenuItemSeparator());
        menu.Items.Add(lastItem);
        menu.Items.Add(_signInItem);
        if (settings.AllowUserExit)
        {
            var quit = new NativeMenuItem("Quit RGC");
            quit.Click += (_, _) => desktop.Shutdown();
            menu.Items.Add(new NativeMenuItemSeparator());
            menu.Items.Add(quit);
        }

        _tray = new TrayIcon
        {
            Icon = new WindowIcon(AssetLoader.Open(new Uri("avares://RGC.Agent/Assets/rgc-icon.png"))),
            ToolTipText = "RGC Announcements",
            Menu = menu,
            IsVisible = true
        };
        TrayIcon.SetIcons(this, [_tray]);
    }

    private void UpdateStatus(ConnectionState state)
    {
        if (_statusItem is null || _signInItem is null) return;
        _statusItem.Header = state switch
        {
            ConnectionState.Connected => _connection?.SignedInAs is { } who ? $"Connected as {who}" : "Connected",
            ConnectionState.SignInRequired => "Sign-in required",
            ConnectionState.Connecting => "Connecting…",
            _ => "Offline – retrying"
        };
        _signInItem.IsVisible = state == ConnectionState.SignInRequired;
        if (_tray is not null) _tray.ToolTipText = $"RGC Announcements – {_statusItem.Header}";
    }

    /// <summary>Microsoft 365 sign-in, needed once per user on each Mac.</summary>
    private void ShowSignIn()
    {
        if (_connection is null) return;
        if (_signInWindow is not null)
        {
            _signInWindow.Activate();
            return;
        }
        _signInWindow = new SignInWindow(_connection);
        _signInWindow.Closed += (_, _) => _signInWindow = null;
        _signInWindow.Show();
        _signInWindow.Activate();
    }
}
