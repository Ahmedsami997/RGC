using System.Net.Http;
using System.Net.Http.Json;
using Microsoft.AspNetCore.SignalR.Client;
using RGC.Branding.Auth;
using RGC.Shared;

namespace RGC.ClientAgent.Services;

public enum ConnectionState { Connecting, Connected, Disconnected, SignInRequired }

/// <summary>Keeps a SignalR connection to the RGC server alive forever and relays announcements.</summary>
public sealed class AgentConnection : IAsyncDisposable
{
    private readonly AgentSettings _settings;
    private readonly AckStore _acks;
    private readonly Guid _clientId;
    private HubConnection _hub = null!;
    private readonly CancellationTokenSource _cts = new();
    private EntraSignIn? _entra;
    private bool _signInRequested;
    private DateTime _lastSignInPromptUtc;
    // A PC nobody has signed in on gets no announcements, so ask again if the window was dismissed.
    private static readonly TimeSpan SignInReminder = TimeSpan.FromMinutes(30);
    private TaskCompletionSource _signedIn = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public event Action<AnnouncementMessage>? AnnouncementReceived;
    /// <summary>A chat line from IT (or an echo of one this PC's user sent).</summary>
    public event Action<ChatMessageDto>? ChatReceived;
    public event Action<ConnectionState>? StateChanged;
    /// <summary>Raised (once until signed in) when the user must sign in with Microsoft 365.</summary>
    public event Action? SignInRequired;

    public ConnectionState State { get; private set; } = ConnectionState.Disconnected;
    public string? SignedInAs => _entra?.Username;
    public bool UsesMicrosoft365 => _entra is not null;

    public AgentConnection(AgentSettings settings, AckStore acks, Guid clientId)
    {
        _settings = settings;
        _acks = acks;
        _clientId = clientId;
    }

    public async Task StartAsync()
    {
        await LoadAuthConfigAsync();
        BuildHub();
        await ConnectLoopAsync();
    }

    /// <summary>Asks the server whether Microsoft 365 sign-in is on. Retries until the server answers.</summary>
    private async Task LoadAuthConfigAsync()
    {
        using var http = new HttpClient { BaseAddress = new Uri(_settings.ServerUrl + "/"), Timeout = TimeSpan.FromSeconds(15) };
        var delays = new[] { 2, 5, 10, 20, 30, 60 };
        for (var attempt = 0; !_cts.IsCancellationRequested; attempt++)
        {
            try
            {
                SetState(ConnectionState.Connecting);
                var config = await http.GetFromJsonAsync<AuthConfigDto>(AuthRoutes.Config, _cts.Token);
                if (config?.EntraEnabled == true)
                {
                    _entra = new EntraSignIn(config, "agent");
                    AgentLog.Info("Server uses Microsoft 365 sign-in");
                }
                return;
            }
            catch (OperationCanceledException) when (_cts.IsCancellationRequested)
            {
                return;
            }
            catch (HttpRequestException ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
            {
                return; // older server without the endpoint: agent key only
            }
            catch (Exception ex)
            {
                SetState(ConnectionState.Disconnected);
                var delay = delays[Math.Min(attempt, delays.Length - 1)];
                AgentLog.Error($"Could not reach {_settings.ServerUrl}; retrying in {delay}s", ex);
                try { await Task.Delay(TimeSpan.FromSeconds(delay), _cts.Token); }
                catch (OperationCanceledException) { return; }
            }
        }
    }

    private void BuildHub()
    {
        _hub = new HubConnectionBuilder()
            .WithUrl(_settings.ServerUrl + HubRoutes.AgentHub, o =>
            {
                if (HasAgentKey) o.Headers[HubRoutes.AgentKeyHeader] = _settings.AgentKey;
                if (_entra is not null) o.AccessTokenProvider = GetTokenAsync;
            })
            .WithAutomaticReconnect(new ForeverRetryPolicy())
            .Build();

        _hub.ServerTimeout = TimeSpan.FromSeconds(60);
        _hub.KeepAliveInterval = TimeSpan.FromSeconds(15);

        _hub.On<AnnouncementMessage>(AgentClientMethods.ReceiveAnnouncement, OnAnnouncementAsync);
        _hub.On<ChatMessageDto>(AgentClientMethods.ReceiveChat, m =>
        {
            AgentLog.Info($"Chat {(m.FromAdmin ? "from IT (" + m.Author + ")" : "sent")}");
            ChatReceived?.Invoke(m);
        });

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
            if (_cts.IsCancellationRequested) return;
            // Small pause so a server that refuses us right away doesn't cause a tight reconnect loop.
            try { await Task.Delay(TimeSpan.FromSeconds(5), _cts.Token); } catch (OperationCanceledException) { return; }
            await ConnectLoopAsync();
        };
    }

    private bool HasAgentKey =>
        !string.IsNullOrWhiteSpace(_settings.AgentKey) && !_settings.AgentKey.StartsWith("CHANGE-ME", StringComparison.Ordinal);

    private async Task<string?> GetTokenAsync()
    {
        if (_entra is null) return null;
        try
        {
            var token = await _entra.TryGetTokenSilentlyAsync(_cts.Token);
            if (token is not null)
            {
                _signInRequested = false;
                return token;
            }
        }
        catch (Exception ex)
        {
            AgentLog.Error("Microsoft 365 token request failed", ex);
        }

        // Without the agent key the server will refuse us until the user signs in once.
        if (!_signInRequested || DateTime.UtcNow - _lastSignInPromptUtc >= SignInReminder)
        {
            AgentLog.Info(_signInRequested ? "Microsoft 365 sign-in still required; asking again" : "Microsoft 365 sign-in required");
            _signInRequested = true;
            _lastSignInPromptUtc = DateTime.UtcNow;
            SignInRequired?.Invoke();
        }
        return null;
    }

    /// <summary>Interactive Microsoft 365 sign-in (called from the sign-in window), then reconnect.</summary>
    public async Task SignInAsync(IntPtr windowHandle)
    {
        if (_entra is null) return;
        await _entra.SignInInteractiveAsync(windowHandle, _cts.Token);
        _signInRequested = false;
        AgentLog.Info($"Signed in to Microsoft 365 as {_entra.Username}");
        if (_hub.State == HubConnectionState.Connected)
        {
            // Already connected (with the agent key): reconnect so the server sees the 365 identity.
            try { await _hub.StopAsync(); } catch { /* Closed handler restarts the loop */ }
        }
        else
        {
            // The connect loop is waiting for this sign-in.
            var signedIn = _signedIn;
            _signedIn = new(TaskCreationOptions.RunContinuationsAsynchronously);
            signedIn.TrySetResult();
        }
    }


    /// <summary>Tries to connect until it succeeds (server may be down when the PC boots).</summary>
    private async Task ConnectLoopAsync()
    {
        var delays = new[] { 2, 5, 10, 20, 30, 60 };
        var attempt = 0;
        while (!_cts.IsCancellationRequested)
        {
            // With Microsoft 365 sign-in and no agent key, connecting is pointless until the user has signed in.
            if (_entra is not null && !HasAgentKey && await GetTokenAsync() is null)
            {
                SetState(ConnectionState.SignInRequired);
                var wait = Task.Delay(TimeSpan.FromMinutes(5), _cts.Token);
                try { await Task.WhenAny(_signedIn.Task, wait); }
                catch (OperationCanceledException) { return; }
                continue;
            }

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

    /// <summary>Sends a message to IT support. Throws with a readable message when it can't.</summary>
    public async Task SendChatAsync(string text)
    {
        if (_hub is null || _hub.State != HubConnectionState.Connected)
            throw new InvalidOperationException("Not connected to the RGC server right now. Please try again in a moment.");
        try
        {
            await _hub.InvokeAsync(AgentServerMethods.SendChat, text, _cts.Token);
        }
        catch (Exception ex)
        {
            AgentLog.Error("Could not send chat message", ex);
            throw new InvalidOperationException("The message could not be sent. Please try again.");
        }
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
        if (_hub is null || _hub.State != HubConnectionState.Connected) return;
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
        try { if (_hub is not null) await _hub.DisposeAsync(); } catch { /* shutting down */ }
    }

    private sealed class ForeverRetryPolicy : IRetryPolicy
    {
        private static readonly TimeSpan[] Delays =
            [TimeSpan.Zero, TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(30)];

        public TimeSpan? NextRetryDelay(RetryContext ctx) =>
            Delays[Math.Min((int)ctx.PreviousRetryCount, Delays.Length - 1)];
    }
}
