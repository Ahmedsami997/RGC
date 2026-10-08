using System.Diagnostics;
using System.Drawing;
using System.Windows.Forms;

namespace RGC.ClientAgent.Services;

/// <summary>System tray icon with connection status and a small menu.</summary>
public sealed class TrayIcon : IDisposable
{
    private readonly NotifyIcon _icon;
    private readonly ToolStripMenuItem _status;
    private readonly ToolStripMenuItem _signIn;

    public event Action? ShowLastRequested;
    public event Action? SignInRequested;
    public event Action? ChatRequested;
    public event Action? ExitRequested;

    public TrayIcon(bool allowExit)
    {
        _status = new ToolStripMenuItem("Connecting...") { Enabled = false };

        var menu = new ContextMenuStrip();
        menu.Items.Add(new ToolStripMenuItem("RGC Announcements") { Enabled = false, Font = new Font(SystemFonts.MenuFont ?? Control.DefaultFont, FontStyle.Bold) });
        menu.Items.Add(_status);
        menu.Items.Add(new ToolStripSeparator());
        _signIn = new ToolStripMenuItem("Sign in with Microsoft 365...", null, (_, _) => SignInRequested?.Invoke()) { Visible = false };
        menu.Items.Add(_signIn);
        menu.Items.Add("Message IT support...", null, (_, _) => ChatRequested?.Invoke());
        menu.Items.Add("Show last announcement", null, (_, _) => ShowLastRequested?.Invoke());
        menu.Items.Add("Open log folder", null, (_, _) => OpenLogFolder());
        if (allowExit)
        {
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add("Exit", null, (_, _) => ExitRequested?.Invoke());
        }

        _icon = new NotifyIcon
        {
            Icon = LoadIcon(),
            Text = "RGC Announcements – connecting",
            ContextMenuStrip = menu,
            Visible = true
        };
    }

    public void SetState(ConnectionState state, string? signedInAs = null)
    {
        _signIn.Visible = state == ConnectionState.SignInRequired;
        var (text, tip) = state switch
        {
            ConnectionState.Connected when signedInAs is not null => ($"● Connected as {signedInAs}", "RGC Announcements – connected"),
            ConnectionState.Connected => ("● Connected", "RGC Announcements – connected"),
            ConnectionState.SignInRequired => ("○ Sign-in required", "RGC Announcements – please sign in"),
            ConnectionState.Connecting => ("○ Connecting...", "RGC Announcements – connecting"),
            _ => ("○ Offline – retrying", "RGC Announcements – offline")
        };
        _status.Text = text;
        _icon.Text = tip.Length > 63 ? tip[..63] : tip;
    }

    private static Icon LoadIcon()
    {
        try
        {
            var res = System.Windows.Application.GetResourceStream(
                new Uri("pack://application:,,,/RGC.Branding;component/Assets/rgc.ico"));
            if (res is not null) return new Icon(res.Stream, SystemInformation.SmallIconSize);
        }
        catch (Exception ex)
        {
            AgentLog.Error("Could not load tray icon", ex);
        }
        return SystemIcons.Information;
    }

    private static void OpenLogFolder()
    {
        try { Process.Start(new ProcessStartInfo(AgentPaths.LogDir) { UseShellExecute = true }); }
        catch (Exception ex) { AgentLog.Error("Could not open log folder", ex); }
    }

    public void ShowBalloon(string title, string text)
    {
        try { _icon.ShowBalloonTip(8000, title, text, ToolTipIcon.Info); } catch { /* best effort */ }
    }

    public void Dispose()
    {
        _icon.Visible = false;
        _icon.Dispose();
    }
}
