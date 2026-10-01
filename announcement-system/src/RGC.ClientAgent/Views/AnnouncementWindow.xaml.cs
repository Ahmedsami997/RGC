using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Threading;
using RGC.Branding;
using RGC.ClientAgent.Services;
using RGC.Shared;

namespace RGC.ClientAgent.Views;

/// <summary>
/// Locked announcement popup: always on top, cannot be closed (no X, no Alt+F4) until the
/// countdown has finished and the user has scrolled through the whole message.
/// </summary>
public partial class AnnouncementWindow : Window
{
    private const int WM_SYSCOMMAND = 0x0112;
    private const int SC_CLOSE = 0xF060;
    private static readonly IntPtr HWND_TOPMOST = new(-1);
    private const uint SWP_NOMOVE = 0x0002, SWP_NOSIZE = 0x0001, SWP_NOACTIVATE = 0x0010;

    private readonly AnnouncementMessage _message;
    private readonly int _countdownSeconds;
    private readonly DispatcherTimer _countdownTimer = new() { Interval = TimeSpan.FromSeconds(1) };
    private readonly DispatcherTimer _topmostTimer = new() { Interval = TimeSpan.FromSeconds(2) };
    private int _remaining;
    private bool _countdownDone;
    private bool _readToEnd;
    private bool _allowClose;
    private DateTime _displayedAtUtc;

    /// <summary>Set when the user closes the window via the Close button.</summary>
    public AcknowledgementDto? Acknowledgement { get; private set; }

    public AnnouncementWindow(AnnouncementMessage message, int countdownSeconds)
    {
        InitializeComponent();
        _message = message;
        _countdownSeconds = countdownSeconds;
        _remaining = countdownSeconds;

        // Never taller than the screen; long messages scroll.
        MaxHeight = SystemParameters.WorkArea.Height * 0.9;
        MessageScroll.MaxHeight = Math.Max(160, SystemParameters.WorkArea.Height * 0.9 - 330);

        TitleText.Text = message.Title;
        MessageText.Text = message.Message;
        MetaText.Text = $"Sent {message.CreatedAtUtc.ToLocalTime():dddd d MMMM yyyy, HH:mm} by {message.CreatedBy}";

        var accent = PriorityStyles.Accent(message.Priority);
        AccentBar.Background = accent;
        PriorityBadge.Background = accent;
        PriorityText.Text = PriorityStyles.Label(message.Priority);
        CountdownBar.Foreground = accent;
        CountdownBar.Maximum = Math.Max(1, countdownSeconds);
        CountdownBar.Value = 0;
        if (message.Priority == AnnouncementPriority.Critical)
            HeadingText.Text = "CRITICAL COMPANY ANNOUNCEMENT";

        _countdownTimer.Tick += (_, _) => Tick();
        _topmostTimer.Tick += (_, _) => KeepOnTop();

        PreviewKeyDown += BlockKeys;
        Deactivated += (_, _) => KeepOnTop();
        ContentRendered += OnContentRendered;
        // Let the user drag the window by its header/body.
        MouseLeftButtonDown += (_, e) => { if (e.ButtonState == MouseButtonState.Pressed) try { DragMove(); } catch { } };
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        // Swallow SC_CLOSE (Alt+F4, taskbar "Close window", etc.) at the Win32 level too.
        (PresentationSource.FromVisual(this) as HwndSource)?.AddHook(WndProc);
    }

    private void OnContentRendered(object? sender, EventArgs e)
    {
        _displayedAtUtc = DateTime.UtcNow;
        Activate();
        KeepOnTop();
        _topmostTimer.Start();
        UpdateReadState();
        UpdateCountdownText();
        if (_countdownSeconds <= 0) FinishCountdown();
        else _countdownTimer.Start();
    }

    private void Tick()
    {
        _remaining--;
        CountdownBar.Value = _countdownSeconds - _remaining;
        if (_remaining <= 0) FinishCountdown();
        else UpdateCountdownText();
    }

    private void FinishCountdown()
    {
        _countdownTimer.Stop();
        _countdownDone = true;
        CountdownBar.Value = CountdownBar.Maximum;
        UpdateCloseState();
    }

    private void UpdateCountdownText()
    {
        CountdownText.Text = $"You can close this message in {_remaining} second{(_remaining == 1 ? "" : "s")}...";
    }

    private void MessageScroll_ScrollChanged(object sender, ScrollChangedEventArgs e) => UpdateReadState();

    /// <summary>The user must have seen the end of the message before it can be dismissed.</summary>
    private void UpdateReadState()
    {
        if (_readToEnd) return;
        var atEnd = MessageScroll.ScrollableHeight <= 0 ||
                    MessageScroll.VerticalOffset >= MessageScroll.ScrollableHeight - 2;
        if (!atEnd) return;
        _readToEnd = true;
        UpdateCloseState();
    }

    private void UpdateCloseState()
    {
        if (!_countdownDone) return;

        if (!_readToEnd)
        {
            CountdownText.Text = "Please scroll down and read the entire message to continue.";
            return;
        }

        _allowClose = true;
        CountdownText.Text = "Thank you. Please confirm you have read this announcement.";
        CloseButton.Visibility = Visibility.Visible;
        CloseButton.IsEnabled = true;
        CloseX.Visibility = Visibility.Visible;
        CloseButton.Focus();
    }

    private void Close_Click(object sender, RoutedEventArgs e)
    {
        if (!_allowClose) return;
        Acknowledgement = new AcknowledgementDto(_message.Id, _displayedAtUtc, DateTime.UtcNow, AgentIdentity.CurrentUser);
        Close();
    }

    private void BlockKeys(object sender, KeyEventArgs e)
    {
        // Alt+F4 never closes the popup; dismissal is only through the Close button so it is
        // always a deliberate acknowledgement. Escape is ignored as well.
        var key = e.Key == Key.System ? e.SystemKey : e.Key;
        if (key == Key.F4 && Keyboard.Modifiers.HasFlag(ModifierKeys.Alt)) e.Handled = true;
        if (key == Key.Escape) e.Handled = true;
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        // Only the Close button (after the countdown) or Windows shutting down may close it.
        if (Acknowledgement is null && !NotificationManager.SessionEnding) e.Cancel = true;
        base.OnClosing(e);
    }

    protected override void OnClosed(EventArgs e)
    {
        _countdownTimer.Stop();
        _topmostTimer.Stop();
        base.OnClosed(e);
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == WM_SYSCOMMAND && (wParam.ToInt64() & 0xFFF0) == SC_CLOSE &&
            Acknowledgement is null && !NotificationManager.SessionEnding)
            handled = true;
        return IntPtr.Zero;
    }

    private void KeepOnTop()
    {
        var hwnd = new WindowInteropHelper(this).Handle;
        if (hwnd == IntPtr.Zero) return;
        if (WindowState == WindowState.Minimized) WindowState = WindowState.Normal;
        SetWindowPos(hwnd, HWND_TOPMOST, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE);
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int x, int y, int cx, int cy, uint flags);
}
