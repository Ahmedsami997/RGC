using System.Media;
using System.Windows;
using System.Windows.Threading;
using RGC.ClientAgent.Views;
using RGC.Shared;

namespace RGC.ClientAgent.Services;

/// <summary>
/// Shows announcements one at a time as modal, always-on-top popups. Announcements that
/// arrive while one is open are queued and shown next.
/// </summary>
public sealed class NotificationManager(AgentConnection connection, AgentSettings settings, Dispatcher dispatcher)
{
    private readonly Queue<AnnouncementMessage> _queue = new();
    private readonly HashSet<Guid> _seen = new();
    private bool _showing;

    public AnnouncementMessage? LastShown { get; private set; }

    /// <summary>Set when Windows is shutting down / logging off so popups don't block it.</summary>
    public static bool SessionEnding { get; set; }

    public void Enqueue(AnnouncementMessage message) => dispatcher.InvokeAsync(() =>
    {
        // The server re-sends unacknowledged announcements on every reconnect; show each once.
        if (!_seen.Add(message.Id)) return;
        _queue.Enqueue(message);
        ShowQueued();
    });

    private void ShowQueued()
    {
        if (_showing) return;
        _showing = true;
        // The PC can't be used until every queued announcement has been acknowledged.
        var screenLock = settings.LockScreen ? new ScreenLock() : null;
        try
        {
            screenLock?.Show();
            while (_queue.Count > 0 && !SessionEnding)
            {
                var message = _queue.Dequeue();
                LastShown = message;
                PlaySound(message.Priority);

                var window = new AnnouncementWindow(message, settings.CountdownSeconds);
                if (screenLock is not null) window.Owner = screenLock.Cover;   // owned windows stay above the cover
                window.ShowDialog();

                if (window.Acknowledgement is { } ack)
                {
                    _ = connection.AcknowledgeAsync(ack);
                }
                else
                {
                    // Closed without acknowledging (Windows shutdown) – allow it to show again next time.
                    _seen.Remove(message.Id);
                }
            }
        }
        finally
        {
            screenLock?.Dispose();
            _showing = false;
        }
    }

    private static void PlaySound(AnnouncementPriority priority)
    {
        try
        {
            (priority switch
            {
                AnnouncementPriority.Critical => SystemSounds.Hand,
                AnnouncementPriority.Important => SystemSounds.Exclamation,
                _ => SystemSounds.Asterisk
            }).Play();
        }
        catch { /* sound is best effort */ }
    }
}
