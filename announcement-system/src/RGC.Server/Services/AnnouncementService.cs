using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using RGC.Server.Data;
using RGC.Server.Hubs;
using RGC.Shared;

namespace RGC.Server.Services;

public sealed class AnnouncementService(
    RgcDbContext db,
    ConnectionRegistry registry,
    IHubContext<AgentHub> agentHub,
    IHubContext<AdminHub> adminHub,
    IOptions<AnnouncementOptions> options,
    ILogger<AnnouncementService> log)
{
    private readonly AnnouncementOptions _opt = options.Value;

    public async Task<AnnouncementSummaryDto> BroadcastAsync(SendAnnouncementRequest req, string adminName, CancellationToken ct)
    {
        var now = DateTime.UtcNow;
        var targeted = req.ClientIds is { Count: > 0 };
        List<Guid> clientIds;
        if (targeted)
        {
            // Chosen computers: send even if they haven't been seen for a while.
            var wanted = req.ClientIds!.Distinct().ToList();
            clientIds = await db.Clients.Where(c => wanted.Contains(c.Id)).Select(c => c.Id).ToListAsync(ct);
        }
        else
        {
            var activeSince = now.AddDays(-_opt.InactiveClientDays);
            clientIds = await db.Clients
                .Where(c => c.IsOnline || c.LastSeenUtc >= activeSince)
                .Select(c => c.Id)
                .ToListAsync(ct);
        }

        var announcement = new Announcement
        {
            Id = Guid.NewGuid(),
            Title = req.Title.Trim(),
            Message = req.Message.Trim(),
            Priority = req.Priority,
            CreatedAtUtc = now,
            CreatedBy = adminName,
            Audience = targeted ? $"{clientIds.Count} selected computer{(clientIds.Count == 1 ? "" : "s")}" : "All computers",
            Recipients = clientIds.Select(id => new AnnouncementRecipient { ClientId = id }).ToList()
        };

        db.Announcements.Add(announcement);
        await db.SaveChangesAsync(ct);

        if (targeted)
        {
            // Only the chosen computers that are online now; the rest get it when they reconnect.
            var connections = clientIds.SelectMany(registry.ConnectionsFor).ToList();
            if (connections.Count > 0)
                await agentHub.Clients.Clients(connections)
                    .SendAsync(AgentClientMethods.ReceiveAnnouncement, ToMessage(announcement), ct);
        }
        else
        {
            await agentHub.Clients.Group(AgentHub.AgentsGroup)
                .SendAsync(AgentClientMethods.ReceiveAnnouncement, ToMessage(announcement), ct);
        }

        log.LogInformation("Announcement {Id} '{Title}' ({Priority}) sent by {Admin} to {Audience} ({Count} PCs)",
            announcement.Id, announcement.Title, announcement.Priority, adminName, announcement.Audience, clientIds.Count);

        var summary = new AnnouncementSummaryDto(announcement.Id, announcement.Title, announcement.Message,
            announcement.Priority, announcement.CreatedAtUtc, announcement.CreatedBy, clientIds.Count, 0, 0, announcement.Audience);
        await adminHub.Clients.All.SendAsync(AdminClientMethods.AnnouncementCreated, summary, ct);
        return summary;
    }

    /// <summary>Announcements this PC should (still) show: recent and not yet acknowledged.</summary>
    public async Task<List<AnnouncementMessage>> GetPendingForClientAsync(Guid clientId, CancellationToken ct)
    {
        var since = DateTime.UtcNow.AddHours(-_opt.PendingDeliveryWindowHours);
        var pending = await db.AnnouncementRecipients
            .Where(r => r.ClientId == clientId && r.AcknowledgedAtUtc == null &&
                        (r.Announcement.LastResentAtUtc ?? r.Announcement.CreatedAtUtc) >= since)
            .OrderBy(r => r.Announcement.CreatedAtUtc)
            .Select(r => r.Announcement)
            .ToListAsync(ct);
        return pending.Select(ToMessage).ToList();
    }

    /// <summary>
    /// Sends the announcement again to every PC that hasn't read it. Online PCs get it now;
    /// offline ones when they reconnect (the pending-delivery window restarts from now).
    /// </summary>
    public async Task<ResendResultDto?> ResendToUnreadAsync(Guid announcementId, string adminName, CancellationToken ct)
    {
        var announcement = await db.Announcements.FindAsync([announcementId], ct);
        if (announcement is null) return null;

        var unread = await db.AnnouncementRecipients
            .Where(r => r.AnnouncementId == announcementId && r.AcknowledgedAtUtc == null)
            .Select(r => r.ClientId)
            .ToListAsync(ct);

        announcement.LastResentAtUtc = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);

        var connections = unread.SelectMany(registry.ConnectionsFor).ToList();
        if (connections.Count > 0)
            await agentHub.Clients.Clients(connections)
                .SendAsync(AgentClientMethods.ReceiveAnnouncement, ToMessage(announcement), ct);

        var sentNow = unread.Count(id => registry.HasConnections(id));
        log.LogInformation("Announcement {Id} resent by {Admin} to {Unread} unread PCs ({Online} online now)",
            announcementId, adminName, unread.Count, sentNow);
        return new ResendResultDto(unread.Count, sentNow);
    }

    public async Task MarkDeliveredAsync(Guid announcementId, Guid clientId, CancellationToken ct)
    {
        var r = await db.AnnouncementRecipients.FindAsync([announcementId, clientId], ct);
        if (r is null)
        {
            // PC registered after the broadcast went out but still received it live.
            if (!await db.Announcements.AnyAsync(a => a.Id == announcementId, ct)) return;
            r = new AnnouncementRecipient { AnnouncementId = announcementId, ClientId = clientId };
            db.AnnouncementRecipients.Add(r);
        }
        if (r.DeliveredAtUtc is not null) return;

        r.DeliveredAtUtc = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);
        await NotifyRecipientAsync(announcementId, clientId, ct);
    }

    public async Task AcknowledgeAsync(AcknowledgementDto ack, Guid clientId, CancellationToken ct)
    {
        var r = await db.AnnouncementRecipients.FindAsync([ack.AnnouncementId, clientId], ct);
        if (r is null)
        {
            if (!await db.Announcements.AnyAsync(a => a.Id == ack.AnnouncementId, ct)) return;
            r = new AnnouncementRecipient { AnnouncementId = ack.AnnouncementId, ClientId = clientId };
            db.AnnouncementRecipients.Add(r);
        }
        if (r.AcknowledgedAtUtc is not null) return;

        var now = DateTime.UtcNow;
        r.DeliveredAtUtc ??= now;
        r.DisplayedAtUtc = ack.DisplayedAtUtc.ToUniversalTime();
        r.AcknowledgedAtUtc = ack.AcknowledgedAtUtc.ToUniversalTime();
        r.AckReceivedAtUtc = now;
        r.AcknowledgedBy = Truncate(ack.UserName, 200);
        await db.SaveChangesAsync(ct);

        log.LogInformation("Announcement {Id} acknowledged on client {Client} by {User} at {At:o}",
            ack.AnnouncementId, clientId, ack.UserName, r.AcknowledgedAtUtc);
        await NotifyRecipientAsync(ack.AnnouncementId, clientId, ct);
    }

    public async Task<List<AnnouncementSummaryDto>> GetHistoryAsync(int take, CancellationToken ct) =>
        await db.Announcements
            .OrderByDescending(a => a.CreatedAtUtc)
            .Take(take)
            .Select(a => new AnnouncementSummaryDto(a.Id, a.Title, a.Message, a.Priority, a.CreatedAtUtc, a.CreatedBy,
                a.Recipients.Count,
                a.Recipients.Count(r => r.DeliveredAtUtc != null),
                a.Recipients.Count(r => r.AcknowledgedAtUtc != null),
                a.Audience))
            .ToListAsync(ct);

    public async Task<List<RecipientStatusDto>> GetRecipientsAsync(Guid announcementId, CancellationToken ct) =>
        await Project(db.AnnouncementRecipients
                .Where(r => r.AnnouncementId == announcementId)
                .OrderBy(r => r.Client.MachineName))
            .ToListAsync(ct);

    // Filter/sort on the entity first; EF cannot translate predicates on the projected record.
    private static IQueryable<RecipientStatusDto> Project(IQueryable<AnnouncementRecipient> query) =>
        query.Select(r => new RecipientStatusDto(
            r.AnnouncementId, r.ClientId, r.Client.MachineName, r.Client.UserName, r.Client.IsOnline,
            r.DeliveredAtUtc, r.DisplayedAtUtc, r.AcknowledgedAtUtc, r.AcknowledgedBy));

    private async Task NotifyRecipientAsync(Guid announcementId, Guid clientId, CancellationToken ct)
    {
        var dto = await Project(db.AnnouncementRecipients
                .Where(r => r.AnnouncementId == announcementId && r.ClientId == clientId))
            .FirstOrDefaultAsync(ct);
        if (dto is not null)
            await adminHub.Clients.All.SendAsync(AdminClientMethods.RecipientUpdated, dto, ct);
    }

    public string? Validate(SendAnnouncementRequest req)
    {
        if (string.IsNullOrWhiteSpace(req.Title)) return "Title is required.";
        if (string.IsNullOrWhiteSpace(req.Message)) return "Message is required.";
        if (req.Title.Trim().Length > _opt.MaxTitleLength) return $"Title must be at most {_opt.MaxTitleLength} characters.";
        if (req.Message.Trim().Length > _opt.MaxMessageLength) return $"Message must be at most {_opt.MaxMessageLength} characters.";
        if (!Enum.IsDefined(req.Priority)) return "Invalid priority.";
        if (req.ClientIds is { Count: > 5000 }) return "Too many computers selected.";
        return null;
    }

    private static AnnouncementMessage ToMessage(Announcement a) =>
        new(a.Id, a.Title, a.Message, a.Priority, a.CreatedAtUtc, a.CreatedBy);

    private static string Truncate(string? s, int max) =>
        string.IsNullOrEmpty(s) ? "" : s.Length <= max ? s : s[..max];
}
