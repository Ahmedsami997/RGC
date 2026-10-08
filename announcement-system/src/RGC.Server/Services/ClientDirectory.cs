using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using RGC.Server.Data;
using RGC.Server.Hubs;
using RGC.Shared;

namespace RGC.Server.Services;

/// <summary>Keeps the Clients table in sync with agent connections.</summary>
public sealed class ClientDirectory(RgcDbContext db, IHubContext<AdminHub> adminHub)
{
    /// <param name="windowsUser">Windows account on the PC.</param>
    /// <param name="displayName">Microsoft 365 display name, when signed in.</param>
    public async Task UpsertOnlineAsync(AgentRegistration reg, string? windowsUser, string? displayName,
        string? remoteIp, CancellationToken ct)
    {
        var now = DateTime.UtcNow;
        var client = await db.Clients.FindAsync([reg.ClientId], ct);
        if (client is null)
        {
            client = new ClientComputer { Id = reg.ClientId, FirstSeenUtc = now };
            db.Clients.Add(client);
        }

        client.MachineName = Truncate(reg.MachineName, 100);
        client.UserName = Truncate(reg.UserName, 200);
        client.WindowsUser = Truncate(windowsUser, 200);
        if (!string.IsNullOrWhiteSpace(displayName)) client.UserDisplayName = Truncate(displayName, 200);
        client.IpAddress = Truncate(reg.IpAddress ?? remoteIp, 100);
        client.PublicIp = Truncate(remoteIp, 100);
        client.OsVersion = Truncate(reg.OsVersion, 200);
        client.AgentVersion = Truncate(reg.AgentVersion, 50);
        client.IsOnline = true;
        client.LastSeenUtc = now;
        await db.SaveChangesAsync(ct);
        await RecordActivityAsync([reg.ClientId], ct);
        await NotifyAsync(ct);
    }

    /// <summary>Marks the PCs as active today (UTC). Called on connect and periodically for online PCs.</summary>
    public async Task RecordActivityAsync(IReadOnlyCollection<Guid> clientIds, CancellationToken ct)
    {
        if (clientIds.Count == 0) return;
        var today = DateTime.SpecifyKind(DateTime.UtcNow.Date, DateTimeKind.Utc);
        var existing = await db.ClientActivity
            .Where(a => a.Day == today && clientIds.Contains(a.ClientId))
            .Select(a => a.ClientId)
            .ToListAsync(ct);
        var missing = clientIds.Except(existing).ToList();
        if (missing.Count == 0) return;

        db.ClientActivity.AddRange(missing.Select(id => new ClientActivity { Day = today, ClientId = id }));
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException)
        {
            // Another connection of the same PC inserted it first; the row exists, which is all we need.
            db.ChangeTracker.Clear();
        }
    }

    public async Task MarkOfflineAsync(Guid clientId, CancellationToken ct)
    {
        var client = await db.Clients.FindAsync([clientId], ct);
        if (client is null) return;
        client.IsOnline = false;
        client.LastSeenUtc = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);
        await NotifyAsync(ct);
    }

    public async Task<List<ClientDto>> GetAllAsync(CancellationToken ct) =>
        await db.Clients
            .OrderByDescending(c => c.IsOnline).ThenBy(c => c.MachineName)
            .Select(c => new ClientDto(c.Id, c.MachineName, c.UserName, c.IpAddress, c.OsVersion,
                c.AgentVersion, c.IsOnline, c.FirstSeenUtc, c.LastSeenUtc,
                c.WindowsUser, c.UserDisplayName, c.PublicIp, 0, 0, null, null))
            .ToListAsync(ct);

    private Task NotifyAsync(CancellationToken ct) =>
        adminHub.Clients.All.SendAsync(AdminClientMethods.ClientsChanged, ct);

    private static string Truncate(string? s, int max) =>
        string.IsNullOrEmpty(s) ? "" : s.Length <= max ? s : s[..max];
}
