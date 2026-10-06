using System.Windows;
using System.Windows.Threading;
using RGC.ClientAgent.Services;
using RGC.ClientAgent.Views;

namespace RGC.ClientAgent;

/// <summary>
/// RGC Client Agent: no main window, lives in the system tray, connects to the RGC server
/// and shows announcements as locked always-on-top popups.
/// </summary>
public partial class App : Application
{
    private Mutex? _singleInstance;
    private TrayIcon? _tray;
    private AgentConnection? _connection;
    private NotificationManager? _notifications;

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // One agent per user session.
        _singleInstance = new Mutex(true, @"Local\RGC.Agent.SingleInstance", out var isFirst);
        if (!isFirst)
        {
            Shutdown();
            return;
        }

        DispatcherUnhandledException += OnDispatcherUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
            AgentLog.Error("Unhandled exception", args.ExceptionObject as Exception);
        TaskScheduler.UnobservedTaskException += (_, args) =>
        {
            AgentLog.Error("Unobserved task exception", args.Exception);
            args.SetObserved();
        };

        var settings = AgentSettings.Load();
        AgentLog.Info($"RGC Agent starting (server {settings.ServerUrl}, user {AgentIdentity.CurrentUser})");

        AutoStart.Ensure(settings.AutoStart);

        var acks = new AckStore();
        _connection = new AgentConnection(settings, acks, AgentIdentity.GetClientId());
        _notifications = new NotificationManager(_connection, settings, Dispatcher);

        _tray = new TrayIcon(settings.AllowUserExit);
        _tray.ShowLastRequested += ShowLast;
        _tray.ExitRequested += () => Shutdown();

        _tray.SignInRequested += ShowSignIn;
        _connection.StateChanged += state => Dispatcher.InvokeAsync(() => _tray?.SetState(state, _connection?.SignedInAs));
        _connection.SignInRequired += () => Dispatcher.InvokeAsync(ShowSignIn);
        _connection.AnnouncementReceived += _notifications.Enqueue;
        _tray.ChatRequested += () => ShowChat(activate: true);
        _connection.ChatReceived += m => Dispatcher.InvokeAsync(() => OnChat(m));

        await _connection.StartAsync();
    }

    private ChatWindow? _chatWindow;

    private ChatWindow ShowChat(bool activate)
    {
        if (_chatWindow is null)
        {
            _chatWindow = new ChatWindow(_connection!);
            _chatWindow.Closed += (_, _) => _chatWindow = null;
        }
        _chatWindow.Reveal(activate);
        return _chatWindow;
    }

    private void OnChat(RGC.Shared.ChatMessageDto message)
    {
        // A message from IT opens the chat in front of the user; echoes of the user's own lines just append.
        if (!message.FromAdmin && _chatWindow is null) return;
        ShowChat(activate: message.FromAdmin).Add(message);
    }

    private SignInWindow? _signInWindow;

    /// <summary>Microsoft 365 sign-in, needed once per user on PCs that aren't joined to Entra.</summary>
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

    /// <summary>Re-shows the last announcement for reference (already acknowledged, closes immediately).</summary>
    private void ShowLast()
    {
        var last = _notifications?.LastShown;
        if (last is null)
        {
            MessageBox.Show("No announcements have been received yet.", "RGC Announcements",
                MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        new AnnouncementWindow(last, countdownSeconds: 0).Show();
    }

    protected override void OnSessionEnding(SessionEndingCancelEventArgs e)
    {
        // Never block Windows from shutting down or logging off.
        NotificationManager.SessionEnding = true;
        foreach (var w in Windows.Cast<Window>().ToList()) w.Close();
        base.OnSessionEnding(e);
    }

    protected override void OnExit(ExitEventArgs e)
    {
        AgentLog.Info("RGC Agent exiting");
        _tray?.Dispose();
        _connection?.DisposeAsync().AsTask().Wait(TimeSpan.FromSeconds(3));
        _singleInstance?.Dispose();
        base.OnExit(e);
    }

    private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        // Keep the agent alive; it must stay connected.
        AgentLog.Error("Unhandled UI exception", e.Exception);
        e.Handled = true;
    }
}
