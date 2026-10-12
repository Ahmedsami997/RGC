using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Options;
using RGC.Server.Security;
using RGC.Server.Services;
using RGC.Shared;

namespace RGC.Server.Hubs;

/// <summary>Hub Client Agents connect to. Agents authenticate with the shared agent key.</summary>
public sealed class AgentHub(
    ConnectionRegistry registry,
    ClientDirectory directory,
    AnnouncementService announcements,
    ChatService chat,
    IOptions<AnnouncementOptions> options,
    IOptions<AuthModeOptions> authMode,
    ILogger<AgentHub> log) : Hub
{
    public const string AgentsGroup = "agents";

    public override async Task OnConnectedAsync()
    {
        var http = Context.GetHttpContext();
        // Either a Microsoft 365 sign-in or (if still allowed) the shared agent key.
        var signedIn = AuthSetup.IsEntraUser(Context.User);
        var keyOk = authMode.Value.AllowAgentKey &&
                    KeyMatches(http?.Request.Headers[HubRoutes.AgentKeyHeader].ToString(), options.Value.AgentKey);
        if (!signedIn && !keyOk)
        {
            log.LogWarning("Rejected agent connection from {Ip}: no Microsoft 365 sign-in and no valid agent key",
                http?.Connection.RemoteIpAddress);
            Context.Abort();
            return;
        }
        await base.OnConnectedAsync();
    }

    public async Task Register(AgentRegistration registration)
    {
        if (registration.ClientId == Guid.Empty || string.IsNullOrWhiteSpace(registration.MachineName))
            throw new HubException("Invalid registration.");

        var ct = Context.ConnectionAborted;
        var remoteIp = Context.GetHttpContext()?.Connection.RemoteIpAddress?.ToString();

        // Older agents only send the Windows account, as UserName.
        var windowsUser = string.IsNullOrWhiteSpace(registration.WindowsUser) ? registration.UserName : registration.WindowsUser;
        string? displayName = null;
        // Trust the Microsoft 365 identity over whatever the PC reports.
        if (AuthSetup.GetEmail(Context.User) is { } email && AuthSetup.IsEntraUser(Context.User))
        {
            registration = registration with { UserName = email };
            displayName = AuthSetup.GetDisplayName(Context.User);
        }

        registry.Add(Context.ConnectionId, registration.ClientId);
        await directory.UpsertOnlineAsync(registration, windowsUser, displayName, remoteIp, ct);
        await Groups.AddToGroupAsync(Context.ConnectionId, AgentsGroup, ct);

        log.LogInformation("Agent registered: {Machine} ({User}) {ClientId}",
            registration.MachineName, registration.UserName, registration.ClientId);

        // Deliver anything that was sent while this PC was offline, or that was shown
        // but never acknowledged (e.g. the PC was restarted with the popup open).
        foreach (var pending in await announcements.GetPendingForClientAsync(registration.ClientId, ct))
            await Clients.Caller.SendAsync(AgentClientMethods.ReceiveAnnouncement, pending, ct);
    }

    public async Task ConfirmDelivered(Guid announcementId)
    {
        var clientId = RequireClient();
        await announcements.MarkDeliveredAsync(announcementId, clientId, Context.ConnectionAborted);
    }

    public async Task Acknowledge(AcknowledgementDto ack)
    {
        var clientId = RequireClient();
        if (AuthSetup.IsEntraUser(Context.User) && AuthSetup.GetEmail(Context.User) is { } email)
            ack = ack with { UserName = email };
        await announcements.AcknowledgeAsync(ack, clientId, Context.ConnectionAborted);
    }

    /// <summary>The PC's user writes to IT support.</summary>
    public async Task SendChat(string text)
    {
        var clientId = RequireClient();
        if (ChatService.Validate(text) is { } error) throw new HubException(error);
        var author = AuthSetup.IsEntraUser(Context.User)
            ? AuthSetup.GetDisplayName(Context.User) ?? AuthSetup.GetEmail(Context.User) ?? "PC user"
            : "PC user";
        await chat.SendFromPcAsync(clientId, text, author, Context.ConnectionAborted);
    }

    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        var clientId = registry.Remove(Context.ConnectionId);
        if (clientId is { } id && !registry.HasConnections(id))
        {
            await directory.MarkOfflineAsync(id, CancellationToken.None);
            log.LogInformation("Agent disconnected: {ClientId}", id);
        }
        await base.OnDisconnectedAsync(exception);
    }

    private Guid RequireClient() =>
        registry.GetClientId(Context.ConnectionId) ?? throw new HubException("Agent is not registered.");

    private static bool KeyMatches(string? presented, string expected)
    {
        if (string.IsNullOrEmpty(presented) || string.IsNullOrEmpty(expected)) return false;
        return CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(presented), Encoding.UTF8.GetBytes(expected));
    }
}
