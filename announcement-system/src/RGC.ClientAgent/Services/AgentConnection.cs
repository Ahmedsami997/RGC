using Microsoft.AspNetCore.SignalR.Client;
using RGC.Shared;

namespace RGC.ClientAgent.Services;

public enum ConnectionState { Connecting, Connected, Disconnected }

/// <summary>Keeps a SignalR connection to the RGC server alive forever and relays announcements.</summary>
public sealed class AgentConnection : IAsyncDisposable
{
    private readonly AgentSettings _settings;
    private readonly AckStore _acks;
    private readonly Guid _clientId;
    private readonly HubConnection _hub;
    private readonly CancellationTokenSource _cts = new();

    public event Action<AnnouncementMessage>? AnnouncementReceived;
    public event Action<ConnectionState>? StateChanged;

    public ConnectionState State { get; private set; } = ConnectionState.Disconnected;

    public AgentConnection(AgentSettings settings, AckStore acks, Guid clientId)
    {
        _settings = settings;
        _acks = acks;
        _clientId = clientId;

        _hub = new HubConnectionBuilder()
            .WithUrl(settings.ServerUrl + HubRoutes.AgentHub, o =>
            {
                o.Headers[HubRoutes.AgentKeyHeader] = settings.AgentKey;
            })
            .WithAutomaticReconnect(new ForeverRetryPolicy())
            .Build();

        _hub.ServerTimeout = TimeSpan.FromSeconds(60);
        _hub.KeepAliveInterval = TimeSpan.FromSeconds(15);

        _hub.On<AnnouncementMessage>(AgentClientMethods.ReceiveAnnouncement, OnAnnouncementAsync);

        _hub.Reconnecting += _ =>
        {
            SetState(ConnectionState.Connecting);
            AgentLog.Info("Connection lost, reconnecting...");
            return Task.CompletedTask;
        };
        _hub.Reconnected += async _ =>
        {
            AgentLog.Info("Reconnected");
            await RegisterAsync();
        };
        _hub.Closed += async ex =>
        {
            SetState(ConnectionState.Disconnected);
            if (ex is not null) AgentLog.Error("Connection closed", ex);
            if (!_cts.IsCancellationRequested) await ConnectLoopAsync();
        };
    }

    public Task StartAsync() => ConnectLoopAsync();

    /// <summary>Tries to connect until it succeeds (server may be down when the PC boots).</summary>
    private async Task ConnectLoopAsync()
    {
        var delays = new[] { 2, 5, 10, 20, 30, 60 };
        var attempt = 0;
        while (!_cts.IsCancellationRequested)
        {
            try
            {
                SetState(ConnectionState.Connecting);
                await _hub.StartAsync(_cts.Token);
                AgentLog.Info($"Connected to {_settings.ServerUrl}");
                await RegisterAsync();
                return;
            }
            catch (OperationCanceledException) when (_cts.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
                SetState(ConnectionState.Disconnected);
                var delay = delays[Math.Min(attempt++, delays.Length - 1)];
                AgentLog.Error($"Could not connect to {_settings.ServerUrl}; retrying in {delay}s", ex);
                try { await Task.Delay(TimeSpan.FromSeconds(delay), _cts.Token); }
                catch (OperationCanceledException) { return; }
            }
        }
    }

    private async Task RegisterAsync()
    {
        try
        {
            await _hub.InvokeAsync(AgentServerMethods.Register, AgentIdentity.BuildRegistration(_clientId), _cts.Token);
            SetState(ConnectionState.Connected);
            await FlushPendingAcksAsync();
        }
        catch (Exception ex)
        {
            AgentLog.Error("Registration failed", ex);
            // Force a reconnect cycle so we try again.
            try { await _hub.StopAsync(); } catch { /* Closed handler restarts the loop */ }
        }
    }

    private async Task OnAnnouncementAsync(AnnouncementMessage message)
    {
        AgentLog.Info($"Announcement received: {message.Id} '{message.Title}' ({message.Priority})");

        if (_acks.IsAcknowledged(message.Id))
        {
            // Already read on this PC; the server just hasn't got the acknowledgement yet.
            var pending = _acks.FindPending(message.Id);
            if (pending is not null) await SendAckAsync(pending);
            return;
        }

        try
        {
            await _hub.InvokeAsync(AgentServerMethods.ConfirmDelivered, message.Id, _cts.Token);
        }
        catch (Exception ex)
        {
            AgentLog.Error($"Could not confirm delivery of {message.Id}", ex);
        }

        AnnouncementReceived?.Invoke(message);
    }

    /// <summary>Stores the acknowledgement locally first, then sends it (retried on reconnect).</summary>
    public async Task AcknowledgeAsync(AcknowledgementDto ack)
    {
        _acks.Record(ack);
        AgentLog.Info($"Announcement {ack.AnnouncementId} acknowledged by {ack.UserName} at {ack.AcknowledgedAtUtc:o} (shown {ack.DisplayedAtUtc:o})");
        await SendAckAsync(ack);
    }

    private async Task SendAckAsync(AcknowledgementDto ack)
    {
        if (_hub.State != HubConnectionState.Connected) return;
        try
        {
            await _hub.InvokeAsync(AgentServerMethods.Acknowledge, ack, _cts.Token);
            _acks.MarkSent(ack.AnnouncementId);
        }
        catch (Exception ex)
        {
            AgentLog.Error($"Could not send acknowledgement for {ack.AnnouncementId}; will retry on reconnect", ex);
        }
    }

    private async Task FlushPendingAcksAsync()
    {
        foreach (var ack in _acks.GetPending())
            await SendAckAsync(ack);
    }

    private void SetState(ConnectionState state)
    {
        if (State == state) return;
        State = state;
        StateChanged?.Invoke(state);
    }

    public async ValueTask DisposeAsync()
    {
        _cts.Cancel();
        try { await _hub.DisposeAsync(); } catch { /* shutting down */ }
    }

    private sealed class ForeverRetryPolicy : IRetryPolicy
    {
        private static readonly TimeSpan[] Delays =
            [TimeSpan.Zero, TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(30)];

        public TimeSpan? NextRetryDelay(RetryContext ctx) =>
            Delays[Math.Min((int)ctx.PreviousRetryCount, Delays.Length - 1)];
    }
}
