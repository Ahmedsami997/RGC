using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using RGC.Server.Data;
using RGC.Server.Hubs;
using RGC.Shared;

namespace RGC.Server.Services;

/// <summary>Keeps the Clients table in sync with agent connections.</summary>
public sealed class ClientDirectory(RgcDbContext db, IHubContext<AdminHub> adminHub)
{
    public async Task UpsertOnlineAsync(AgentRegistration reg, string? remoteIp, CancellationToken ct)
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
        client.IpAddress = Truncate(reg.IpAddress ?? remoteIp, 100);
        client.OsVersion = Truncate(reg.OsVersion, 200);
        client.AgentVersion = Truncate(reg.AgentVersion, 50);
        client.IsOnline = true;
        client.LastSeenUtc = now;
        await db.SaveChangesAsync(ct);
        await NotifyAsync(ct);
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
                c.AgentVersion, c.IsOnline, c.FirstSeenUtc, c.LastSeenUtc))
            .ToListAsync(ct);

    private Task NotifyAsync(CancellationToken ct) =>
        adminHub.Clients.All.SendAsync(AdminClientMethods.ClientsChanged, ct);

    private static string Truncate(string? s, int max) =>
        string.IsNullOrEmpty(s) ? "" : s.Length <= max ? s : s[..max];
}
