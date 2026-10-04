using Microsoft.EntityFrameworkCore;
using RGC.Server.Data;
using RGC.Shared;

namespace RGC.Server.Services;

/// <summary>Read-only statistics and reports for the Admin Console.</summary>
public sealed class ReportService(RgcDbContext db, ClientDirectory directory)
{
    private const int MaxRecords = 1000;

    // One announcement on one PC, flattened. Reports are computed in memory from these rows:
    // a club-sized install has at most a few hundred thousand of them.
    private sealed record Row(
        Guid AnnouncementId,
        string Title,
        AnnouncementPriority Priority,
        DateTime CreatedAtUtc,
        Guid ClientId,
        string MachineName,
        string ClientUser,
        string? ClientDisplayName,
        string? AcknowledgedBy,
        DateTime? DeliveredAtUtc,
        DateTime? DisplayedAtUtc,
        DateTime? AcknowledgedAtUtc);

    private static IQueryable<Row> Rows(IQueryable<AnnouncementRecipient> query) =>
        query.Select(r => new Row(r.AnnouncementId, r.Announcement.Title, r.Announcement.Priority, r.Announcement.CreatedAtUtc,
            r.ClientId, r.Client.MachineName, r.Client.UserName, r.Client.UserDisplayName, r.AcknowledgedBy,
            r.DeliveredAtUtc, r.DisplayedAtUtc, r.AcknowledgedAtUtc));

    /// <summary>Who the announcement was for: whoever read it, else the PC's current user.</summary>
    private static string UserOf(Row r) =>
        !string.IsNullOrWhiteSpace(r.AcknowledgedBy) ? r.AcknowledgedBy
        : !string.IsNullOrWhiteSpace(r.ClientUser) ? r.ClientUser
        : "(unknown)";

    /// <summary>Sent → closed, in minutes. The close time comes from the PC's clock, so never below zero.</summary>
    private static double? MinutesToRead(DateTime createdAtUtc, DateTime? acknowledgedAtUtc) =>
        acknowledgedAtUtc is { } ack ? Math.Max(0, (ack - createdAtUtc).TotalMinutes) : null;

    // ------------------------------------------------------------------ dashboard

    /// <param name="tzOffsetMinutes">The admin's UTC offset, so days match their calendar.</param>
    public async Task<StatsDto> GetStatsAsync(int days, int tzOffsetMinutes, CancellationToken ct)
    {
        days = Math.Clamp(days, 1, 366);
        var offset = TimeSpan.FromMinutes(Math.Clamp(tzOffsetMinutes, -14 * 60, 14 * 60));
        var today = (DateTime.UtcNow + offset).Date;
        var firstDay = today.AddDays(-(days - 1));
        var fromUtc = DateTime.SpecifyKind(firstDay - offset, DateTimeKind.Utc);
        DateTime LocalDay(DateTime utc) => (utc + offset).Date;

        var rows = await Rows(db.AnnouncementRecipients.Where(r => r.Announcement.CreatedAtUtc >= fromUtc)).ToListAsync(ct);
        var announcements = await db.Announcements
            .Where(a => a.CreatedAtUtc >= fromUtc)
            .Select(a => new { a.Priority, a.CreatedAtUtc })
            .ToListAsync(ct);

        var totalPcs = await db.Clients.CountAsync(ct);
        var onlinePcs = await db.Clients.CountAsync(c => c.IsOnline, ct);

        // Activity is recorded per UTC day; close enough to the local day for a daily chart.
        var activityFrom = DateTime.SpecifyKind(firstDay, DateTimeKind.Utc);
        var activity = await db.ClientActivity
            .Where(a => a.Day >= activityFrom)
            .Select(a => new { a.Day, a.ClientId })
            .ToListAsync(ct);
        var activeByDay = activity.GroupBy(a => a.Day.Date).ToDictionary(g => g.Key, g => g.Count());

        var read = rows.Where(r => r.AcknowledgedAtUtc is not null).ToList();
        var minutes = read.Select(r => MinutesToRead(r.CreatedAtUtc, r.AcknowledgedAtUtc)!.Value).OrderBy(m => m).ToList();
        var onScreen = read
            .Where(r => r.DisplayedAtUtc is not null)
            .Select(r => Math.Max(0, (r.AcknowledgedAtUtc!.Value - r.DisplayedAtUtc!.Value).TotalSeconds))
            .ToList();

        var dayList = Enumerable.Range(0, days).Select(i => firstDay.AddDays(i)).ToList();
        var sentByDay = announcements.GroupBy(a => LocalDay(a.CreatedAtUtc)).ToDictionary(g => g.Key, g => g.Count());
        var readByDay = read.GroupBy(r => LocalDay(r.AcknowledgedAtUtc!.Value)).ToDictionary(g => g.Key, g => g.Count());
        List<DailyPointDto> Series(Dictionary<DateTime, int> counts) =>
            dayList.Select(d => new DailyPointDto(d, counts.GetValueOrDefault(d))).ToList();

        var byPriority = Enum.GetValues<AnnouncementPriority>()
            .Select(p => new PriorityStatDto(p,
                announcements.Count(a => a.Priority == p),
                rows.Count(r => r.Priority == p),
                read.Count(r => r.Priority == p)))
            .ToList();

        var mostUnread = BuildPeople(rows)
            .Where(p => p.Received > p.Read)
            .OrderByDescending(p => p.Received - p.Read).ThenBy(p => p.User)
            .Take(5)
            .ToList();

        return new StatsDto(
            days, totalPcs, onlinePcs,
            ActivePcs: activity.Select(a => a.ClientId).Distinct().Count(),
            Announcements: announcements.Count,
            Recipients: rows.Count,
            Delivered: rows.Count(r => r.DeliveredAtUtc is not null),
            Read: read.Count,
            WaitingToBeRead: rows.Count - read.Count,
            AvgMinutesToRead: minutes.Count > 0 ? minutes.Average() : null,
            MedianMinutesToRead: minutes.Count > 0 ? Median(minutes) : null,
            AvgSecondsOnScreen: onScreen.Count > 0 ? onScreen.Average() : null,
            ActivePcsPerDay: Series(activeByDay),
            AnnouncementsPerDay: Series(sentByDay),
            ReadsPerDay: Series(readByDay),
            ByPriority: byPriority,
            MostUnread: mostUnread);
    }

    private static double Median(List<double> sorted) =>
        sorted.Count % 2 == 1 ? sorted[sorted.Count / 2] : (sorted[sorted.Count / 2 - 1] + sorted[sorted.Count / 2]) / 2;

    // ------------------------------------------------------------------ computers

    public async Task<List<ClientDto>> GetClientsAsync(CancellationToken ct)
    {
        var clients = await directory.GetAllAsync(ct);
        var rows = await db.AnnouncementRecipients
            .Select(r => new { r.ClientId, r.Announcement.CreatedAtUtc, r.AcknowledgedAtUtc })
            .ToListAsync(ct);
        var byClient = rows.GroupBy(r => r.ClientId).ToDictionary(g => g.Key, g =>
        {
            var minutes = g.Select(r => MinutesToRead(r.CreatedAtUtc, r.AcknowledgedAtUtc)).OfType<double>().ToList();
            return (Received: g.Count(), Read: minutes.Count, Last: g.Max(r => r.AcknowledgedAtUtc),
                Avg: minutes.Count > 0 ? minutes.Average() : (double?)null);
        });

        return clients.Select(c => byClient.TryGetValue(c.Id, out var s)
                ? c with { Received = s.Received, Read = s.Read, LastReadAtUtc = s.Last, AvgMinutesToRead = s.Avg }
                : c)
            .ToList();
    }

    public async Task<List<ReadRecordDto>> GetClientRecordsAsync(Guid clientId, CancellationToken ct)
    {
        var rows = await Rows(db.AnnouncementRecipients
                .Where(r => r.ClientId == clientId)
                .OrderByDescending(r => r.Announcement.CreatedAtUtc)
                .Take(MaxRecords))
            .ToListAsync(ct);
        return rows.Select(ToRecord).ToList();
    }

    // ------------------------------------------------------------------ people

    public async Task<List<PersonSummaryDto>> GetPeopleAsync(CancellationToken ct)
    {
        var rows = await Rows(db.AnnouncementRecipients).ToListAsync(ct);
        return BuildPeople(rows).OrderBy(p => p.DisplayName ?? p.User, StringComparer.OrdinalIgnoreCase).ToList();
    }

    public async Task<List<ReadRecordDto>> GetPersonRecordsAsync(string user, CancellationToken ct)
    {
        user = user.Trim();
        var rows = await Rows(db.AnnouncementRecipients
                .Where(r => r.AcknowledgedBy == user ||
                            ((r.AcknowledgedBy == null || r.AcknowledgedBy == "") && r.Client.UserName == user))
                .OrderByDescending(r => r.Announcement.CreatedAtUtc)
                .Take(MaxRecords))
            .ToListAsync(ct);
        return rows.Where(r => string.Equals(UserOf(r), user, StringComparison.OrdinalIgnoreCase))
            .Select(ToRecord)
            .ToList();
    }

    /// <summary>
    /// Groups rows by person. An announcement that reached two of someone's PCs counts once,
    /// and counts as read as soon as they read it on either.
    /// </summary>
    private static IEnumerable<PersonSummaryDto> BuildPeople(IEnumerable<Row> rows) =>
        rows.GroupBy(UserOf, StringComparer.OrdinalIgnoreCase).Select(person =>
        {
            var perAnnouncement = person.GroupBy(r => r.AnnouncementId)
                .Select(a => (a.First().CreatedAtUtc, FirstRead: a.Min(r => r.AcknowledgedAtUtc)))
                .ToList();
            var minutes = perAnnouncement.Select(a => MinutesToRead(a.CreatedAtUtc, a.FirstRead)).OfType<double>().ToList();
            var displayName = person
                .Where(r => string.Equals(r.ClientUser, person.Key, StringComparison.OrdinalIgnoreCase))
                .Select(r => r.ClientDisplayName)
                .FirstOrDefault(n => !string.IsNullOrWhiteSpace(n));
            var computers = string.Join(", ", person.Select(r => r.MachineName)
                .Distinct(StringComparer.OrdinalIgnoreCase).Order(StringComparer.OrdinalIgnoreCase));

            return new PersonSummaryDto(person.Key, displayName, computers,
                Received: perAnnouncement.Count,
                Read: minutes.Count,
                LastReadAtUtc: person.Max(r => r.AcknowledgedAtUtc),
                AvgMinutesToRead: minutes.Count > 0 ? minutes.Average() : null);
        });

    private static ReadRecordDto ToRecord(Row r) =>
        new(r.AnnouncementId, r.Title, r.Priority, r.CreatedAtUtc, r.ClientId, r.MachineName, UserOf(r),
            r.DeliveredAtUtc, r.DisplayedAtUtc, r.AcknowledgedAtUtc);
}
