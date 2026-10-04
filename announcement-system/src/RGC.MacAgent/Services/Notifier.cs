using Avalonia.Threading;
using RGC.MacAgent.Views;
using RGC.Shared;

namespace RGC.MacAgent.Services;

/// <summary>
/// Shows announcements one at a time as locked, always-on-top popups. Announcements that
/// arrive while one is open are queued and shown next.
/// </summary>
public sealed class Notifier(AgentConnection connection, MacSettings settings)
{
    private readonly Queue<AnnouncementMessage> _queue = new();
    private readonly HashSet<Guid> _seen = new();
    private bool _showing;

    public AnnouncementMessage? LastShown { get; private set; }

    /// <summary>Set when macOS is logging out or shutting down so popups don't block it.</summary>
    public static bool SessionEnding { get; set; }

    public void Enqueue(AnnouncementMessage message) => Dispatcher.UIThread.Post(() =>
    {
        // The server re-sends unacknowledged announcements on every reconnect; show each once.
        if (!_seen.Add(message.Id)) return;
        _queue.Enqueue(message);
        _ = ShowQueuedAsync();
    });

    private async Task ShowQueuedAsync()
    {
        if (_showing) return;
        _showing = true;
        try
        {
            while (_queue.Count > 0 && !SessionEnding)
            {
                var message = _queue.Dequeue();
                LastShown = message;
                Beep(message.Priority);

                var window = new AnnouncementWindow(message, settings.CountdownSeconds);
                var closed = new TaskCompletionSource();
                window.Closed += (_, _) => closed.TrySetResult();
                window.Show();
                window.Activate();
                await closed.Task;

                if (window.Acknowledgement is { } ack) _ = connection.AcknowledgeAsync(ack);
                else _seen.Remove(message.Id); // closed by logout: show again next time
            }
        }
        finally
        {
            _showing = false;
        }
    }

    /// <summary>Re-shows the last announcement for reference (already read, can close right away).</summary>
    public void ShowLast()
    {
        if (LastShown is null) return;
        var window = new AnnouncementWindow(LastShown, countdownSeconds: 0, reference: true);
        window.Show();
        window.Activate();
    }

    private static void Beep(AnnouncementPriority priority)
    {
        // System alert sounds; best effort.
        var sound = priority switch
        {
            AnnouncementPriority.Critical => "Sosumi",
            AnnouncementPriority.Important => "Glass",
            _ => "Tink"
        };
        try
        {
            System.Diagnostics.Process.Start("/usr/bin/afplay", $"/System/Library/Sounds/{sound}.aiff")?.Dispose();
        }
        catch { /* no sound is fine */ }
    }
}
