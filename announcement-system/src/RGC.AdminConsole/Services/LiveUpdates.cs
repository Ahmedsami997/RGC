using Microsoft.AspNetCore.SignalR.Client;
using RGC.Shared;

namespace RGC.AdminConsole.Services;

/// <summary>Receives live status pushes from the server's admin hub.</summary>
public sealed class LiveUpdates : IAsyncDisposable
{
    private readonly HubConnection _hub;

    public event Action? ClientsChanged;
    public event Action<RecipientStatusDto>? RecipientUpdated;
    public event Action<AnnouncementSummaryDto>? AnnouncementCreated;
    public event Action<ChatMessageDto>? ChatMessage;
    public event Action<bool>? ConnectedChanged;

    public LiveUpdates(string serverUrl, Func<Task<string?>> tokenProvider)
    {
        _hub = new HubConnectionBuilder()
            .WithUrl(serverUrl + HubRoutes.AdminHub, o => o.AccessTokenProvider = tokenProvider)
            .WithAutomaticReconnect([TimeSpan.Zero, TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(30), TimeSpan.FromSeconds(30)])
            .Build();

        _hub.On(AdminClientMethods.ClientsChanged, () => ClientsChanged?.Invoke());
        _hub.On<RecipientStatusDto>(AdminClientMethods.RecipientUpdated, r => RecipientUpdated?.Invoke(r));
        _hub.On<AnnouncementSummaryDto>(AdminClientMethods.AnnouncementCreated, a => AnnouncementCreated?.Invoke(a));
        _hub.On<ChatMessageDto>(AdminClientMethods.ChatMessage, m => ChatMessage?.Invoke(m));

        _hub.Reconnecting += _ => { ConnectedChanged?.Invoke(false); return Task.CompletedTask; };
        _hub.Reconnected += _ => { ConnectedChanged?.Invoke(true); ClientsChanged?.Invoke(); return Task.CompletedTask; };
        _hub.Closed += _ => { ConnectedChanged?.Invoke(false); return Task.CompletedTask; };
    }

    public async Task StartAsync()
    {
        await _hub.StartAsync();
        ConnectedChanged?.Invoke(true);
    }

    public async ValueTask DisposeAsync()
    {
        try { await _hub.DisposeAsync(); } catch { /* closing */ }
    }
}
