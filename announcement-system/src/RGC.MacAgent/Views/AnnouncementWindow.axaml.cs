using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Threading;
using RGC.MacAgent.Services;
using RGC.Shared;

namespace RGC.MacAgent.Views;

/// <summary>
/// Locked announcement popup: always on top, no title bar or close button, Cmd+W ignored,
/// until the countdown has finished and the user has scrolled through the whole message.
/// </summary>
public partial class AnnouncementWindow : Window
{
    private readonly AnnouncementMessage _message = null!;
    private readonly int _countdownSeconds;
    private readonly bool _reference;
    private readonly DispatcherTimer _countdownTimer = new() { Interval = TimeSpan.FromSeconds(1) };
    private int _remaining;
    private bool _countdownDone;
    private bool _readToEnd;
    private bool _allowClose;
    private DateTime _displayedAtUtc;

    /// <summary>Set when the user closes the window with the Close button.</summary>
    public AcknowledgementDto? Acknowledgement { get; private set; }

    // For the XAML previewer.
    public AnnouncementWindow() => InitializeComponent();

    /// <param name="reference">Re-showing an announcement already read: it can be closed at once.</param>
    public AnnouncementWindow(AnnouncementMessage message, int countdownSeconds, bool reference = false)
    {
        InitializeComponent();
        _message = message;
        _countdownSeconds = countdownSeconds;
        _reference = reference;
        _remaining = countdownSeconds;

        TitleText.Text = message.Title;
        MessageText.Text = message.Message;
        MetaText.Text = $"Sent {message.CreatedAtUtc.ToLocalTime():dddd d MMMM yyyy, HH:mm} by {message.CreatedBy}";

        var accent = new SolidColorBrush(message.Priority switch
        {
            AnnouncementPriority.Critical => Color.Parse("#C0392B"),
            AnnouncementPriority.Important => Color.Parse("#C98A12"),
            _ => Color.Parse("#39983E")
        });
        AccentBar.Background = accent;
        PriorityBadge.Background = accent;
        PriorityText.Text = message.Priority switch
        {
            AnnouncementPriority.Critical => "CRITICAL",
            AnnouncementPriority.Important => "IMPORTANT",
            _ => "NORMAL"
        };
        CountdownBar.Foreground = accent;
        CountdownBar.Maximum = Math.Max(1, countdownSeconds);
        CountdownBar.Value = 0;
        if (message.Priority == AnnouncementPriority.Critical) HeadingText.Text = "CRITICAL COMPANY ANNOUNCEMENT";

        _countdownTimer.Tick += (_, _) => Tick();
        MessageScroll.ScrollChanged += (_, _) => UpdateReadState();
        CloseButton.Click += Close_Click;
        // Without a title bar, let the user move the window by dragging its header.
        Header.PointerPressed += (_, e) =>
        {
            if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) BeginMoveDrag(e);
        };
        // If it loses focus, put it back on top.
        Deactivated += (_, _) => { Topmost = false; Topmost = true; };
        Opened += OnOpened;
    }

    private void OnOpened(object? sender, EventArgs e)
    {
        _displayedAtUtc = DateTime.UtcNow;
        Activate();
        // Layout has to finish before we know whether the message needs scrolling.
        Dispatcher.UIThread.Post(UpdateReadState, DispatcherPriority.Background);
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

    private void UpdateCountdownText() =>
        CountdownText.Text = $"You can close this message in {_remaining} second{(_remaining == 1 ? "" : "s")}...";

    /// <summary>The user must have seen the end of the message before it can be dismissed.</summary>
    private void UpdateReadState()
    {
        if (_readToEnd) return;
        var scrollable = MessageScroll.Extent.Height - MessageScroll.Viewport.Height;
        if (scrollable > 2 && MessageScroll.Offset.Y < scrollable - 2) return;
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
        CountdownText.Text = _reference ? "You have already read this announcement." : "Thank you. Please confirm you have read this announcement.";
        CloseButton.Content = _reference ? "Close" : "I have read this – Close";
        CloseButton.IsVisible = true;
        CloseButton.IsEnabled = true;
        CloseButton.Focus();
    }

    private void Close_Click(object? sender, RoutedEventArgs e)
    {
        if (!_allowClose) return;
        if (!_reference)
            Acknowledgement = new AcknowledgementDto(_message.Id, _displayedAtUtc, DateTime.UtcNow, MacIdentity.CurrentUser);
        _allowClose = true;
        Close();
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        // Escape and Cmd+W never dismiss the popup; only the Close button does.
        if (e.Key == Key.Escape || (e.Key == Key.W && e.KeyModifiers.HasFlag(KeyModifiers.Meta))) e.Handled = true;
        base.OnKeyDown(e);
    }

    protected override void OnClosing(WindowClosingEventArgs e)
    {
        // Only the Close button (after the countdown) or macOS logging out may close it.
        if (!_allowClose && !Notifier.SessionEnding) e.Cancel = true;
        base.OnClosing(e);
    }

    protected override void OnClosed(EventArgs e)
    {
        _countdownTimer.Stop();
        base.OnClosed(e);
    }
}
