using RGC.Shared;

namespace RGC.AdminConsole.ViewModels;

public static class Fmt
{
    /// <summary>12 → "12 min", 135 → "2 h 15 min", 3000 → "2 d 2 h".</summary>
    public static string Duration(double? minutes)
    {
        if (minutes is not { } m) return "—";
        if (m < 1) return "< 1 min";
        if (m < 60) return $"{m:0} min";
        if (m < 24 * 60) return $"{(int)(m / 60)} h {(int)(m % 60)} min";
        return $"{(int)(m / 1440)} d {(int)(m % 1440 / 60)} h";
    }

    public static string Seconds(double? seconds) =>
        seconds is not { } s ? "—" : s < 60 ? $"{s:0} s" : Duration(s / 60);

    public static string Percent(int part, int total) => total == 0 ? "—" : $"{100.0 * part / total:0}%";

    public static double PercentValue(int part, int total) => total == 0 ? 0 : 100.0 * part / total;

    public static double? MinutesBetween(DateTime from, DateTime? to) =>
        to is { } t ? Math.Max(0, (t - from).TotalMinutes) : null;
}

/// <summary>A PC on the Computers page.</summary>
public sealed class ComputerRow(ClientDto dto)
{
    public ClientDto Dto { get; } = dto;
    public Guid Id => Dto.Id;
    public string MachineName => Dto.MachineName;
    public bool IsOnline => Dto.IsOnline;
    public string UserName => Dto.UserName;
    public string UserText => string.IsNullOrWhiteSpace(Dto.UserDisplayName) ? Dto.UserName : Dto.UserDisplayName!;
    public string? WindowsUser => Dto.WindowsUser;
    public string? OsVersion => Dto.OsVersion;
    public string? IpAddress => Dto.IpAddress;
    public string? PublicIp => Dto.PublicIp;
    public string AgentVersion => Dto.AgentVersion;
    public DateTime FirstSeenUtc => Dto.FirstSeenUtc;
    public DateTime LastSeenUtc => Dto.LastSeenUtc;
    public DateTime? LastReadAtUtc => Dto.LastReadAtUtc;
    public int Received => Dto.Received;
    public int Read => Dto.Read;
    public int Unread => Dto.Received - Dto.Read;
    public string ReadText => $"{Dto.Read} / {Dto.Received}";
    public string ReadRateText => Fmt.Percent(Dto.Read, Dto.Received);
    public double ReadRate => Fmt.PercentValue(Dto.Read, Dto.Received);
    public string AvgTimeText => Fmt.Duration(Dto.AvgMinutesToRead);
}

/// <summary>A person on the People page.</summary>
public sealed class PersonRow(PersonSummaryDto dto)
{
    public PersonSummaryDto Dto { get; } = dto;
    public string User => Dto.User;
    public string Name => string.IsNullOrWhiteSpace(Dto.DisplayName) ? Dto.User : Dto.DisplayName!;
    public string Computers => Dto.Computers;
    public int Received => Dto.Received;
    public int Read => Dto.Read;
    public int Unread => Dto.Received - Dto.Read;
    public string ReadRateText => Fmt.Percent(Dto.Read, Dto.Received);
    public double ReadRate => Fmt.PercentValue(Dto.Read, Dto.Received);
    public string AvgTimeText => Fmt.Duration(Dto.AvgMinutesToRead);
    public DateTime? LastReadAtUtc => Dto.LastReadAtUtc;
}

/// <summary>One announcement on one PC, in the per-person and per-PC reports.</summary>
public sealed class ReadRecordRow(ReadRecordDto dto)
{
    public ReadRecordDto Dto { get; } = dto;
    public string Title => Dto.Title;
    public AnnouncementPriority Priority => Dto.Priority;
    public DateTime CreatedAtUtc => Dto.CreatedAtUtc;
    public string MachineName => Dto.MachineName;
    public string User => Dto.User;
    public DateTime? DeliveredAtUtc => Dto.DeliveredAtUtc;
    public DateTime? DisplayedAtUtc => Dto.DisplayedAtUtc;
    public DateTime? AcknowledgedAtUtc => Dto.AcknowledgedAtUtc;

    public RecipientState State =>
        Dto.AcknowledgedAtUtc is not null ? RecipientState.Read :
        Dto.DeliveredAtUtc is not null ? RecipientState.Delivered : RecipientState.Pending;

    public string StateText => State switch
    {
        RecipientState.Read => "Read",
        RecipientState.Delivered => "Not read yet",
        _ => "Not delivered"
    };

    public string TimeToReadText => Fmt.Duration(Fmt.MinutesBetween(Dto.CreatedAtUtc, Dto.AcknowledgedAtUtc));

    public string OnScreenText => Dto.DisplayedAtUtc is { } shown && Dto.AcknowledgedAtUtc is { } closed
        ? Fmt.Seconds(Math.Max(0, (closed - shown).TotalSeconds))
        : "—";
}

/// <summary>A bar in a <see cref="Views.BarChart"/>.</summary>
public sealed record ChartPoint(string Label, double Value, string Tooltip);

/// <summary>A row of the "read rate by priority" card.</summary>
public sealed record PriorityRateRow(AnnouncementPriority Priority, double Percent, string PercentText, string Detail);
