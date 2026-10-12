namespace RGC.Shared;

public enum AnnouncementPriority
{
    Normal = 0,
    Important = 1,
    Critical = 2
}

/// <summary>Names of the SignalR hubs, routes and methods shared by server and clients.</summary>
public static class HubRoutes
{
    public const string AgentHub = "/hubs/agent";
    public const string AdminHub = "/hubs/admin";

    /// <summary>Header carrying the shared agent key when a Client Agent connects.</summary>
    public const string AgentKeyHeader = "X-RGC-Agent-Key";
}

/// <summary>Methods the server invokes on Client Agents.</summary>
public static class AgentClientMethods
{
    public const string ReceiveAnnouncement = "ReceiveAnnouncement";
    public const string ReceiveChat = "ReceiveChat";
}

/// <summary>Methods Client Agents invoke on the server.</summary>
public static class AgentServerMethods
{
    public const string Register = "Register";
    public const string ConfirmDelivered = "ConfirmDelivered";
    public const string Acknowledge = "Acknowledge";
    /// <summary>The PC's user writes to IT (string text).</summary>
    public const string SendChat = "SendChat";
}

/// <summary>Methods the server invokes on Admin Consoles.</summary>
public static class AdminClientMethods
{
    public const string ClientsChanged = "ClientsChanged";
    public const string RecipientUpdated = "RecipientUpdated";
    public const string AnnouncementCreated = "AnnouncementCreated";
    public const string ChatMessage = "ChatMessage";
}

public sealed record AgentRegistration(
    Guid ClientId,
    string MachineName,
    string UserName,
    string? IpAddress,
    string AgentVersion,
    string? OsVersion,
    // Windows account on the PC (older agents leave it empty and send it as UserName).
    string? WindowsUser = null);

public sealed record AnnouncementMessage(
    Guid Id,
    string Title,
    string Message,
    AnnouncementPriority Priority,
    DateTime CreatedAtUtc,
    string CreatedBy);

public sealed record AcknowledgementDto(
    Guid AnnouncementId,
    DateTime DisplayedAtUtc,
    DateTime AcknowledgedAtUtc,
    string UserName);

public sealed record LoginRequest(string Username, string Password);

public sealed record LoginResponse(string Token, DateTime ExpiresAtUtc, string Username, string DisplayName);

/// <param name="ClientIds">Computers to send to; null or empty sends to every computer.</param>
public sealed record SendAnnouncementRequest(
    string Title,
    string Message,
    AnnouncementPriority Priority,
    List<Guid>? ClientIds = null);

public sealed record ClientDto(
    Guid Id,
    string MachineName,
    string UserName,
    string? IpAddress,
    string? OsVersion,
    string AgentVersion,
    bool IsOnline,
    DateTime FirstSeenUtc,
    DateTime LastSeenUtc,
    string? WindowsUser = null,
    string? UserDisplayName = null,
    string? PublicIp = null,
    int Received = 0,
    int Read = 0,
    DateTime? LastReadAtUtc = null,
    double? AvgMinutesToRead = null);

public sealed record AnnouncementSummaryDto(
    Guid Id,
    string Title,
    string Message,
    AnnouncementPriority Priority,
    DateTime CreatedAtUtc,
    string CreatedBy,
    int TotalRecipients,
    int Delivered,
    int Acknowledged,
    // "All computers" or e.g. "3 selected computers"; null on announcements from before 1.2.
    string? Audience = null);

public sealed record RecipientStatusDto(
    Guid AnnouncementId,
    Guid ClientId,
    string MachineName,
    string UserName,
    bool IsOnline,
    DateTime? DeliveredAtUtc,
    DateTime? DisplayedAtUtc,
    DateTime? AcknowledgedAtUtc,
    string? AcknowledgedBy);

/// <summary>Tells the desktop apps how to sign in (served anonymously at /api/auth/config).</summary>
public sealed record AuthConfigDto(
    bool EntraEnabled,
    string? TenantId,
    string? ClientId,
    string? Scope,
    bool LocalLoginEnabled,
    bool AgentKeyEnabled);

/// <summary>One point of a per-day series (Day is a local calendar date).</summary>
public sealed record DailyPointDto(DateTime Day, int Value);

public sealed record PriorityStatDto(AnnouncementPriority Priority, int Announcements, int Recipients, int Read);

/// <summary>A person, keyed by Microsoft 365 email (or Windows account for agent-key PCs).</summary>
public sealed record PersonSummaryDto(
    string User,
    string? DisplayName,
    string Computers,
    int Received,
    int Read,
    DateTime? LastReadAtUtc,
    double? AvgMinutesToRead);

public sealed record StatsDto(
    int Days,
    int TotalPcs,
    int OnlinePcs,
    int ActivePcs,
    int Announcements,
    int Recipients,
    int Delivered,
    int Read,
    int WaitingToBeRead,
    double? AvgMinutesToRead,
    double? MedianMinutesToRead,
    double? AvgSecondsOnScreen,
    List<DailyPointDto> ActivePcsPerDay,
    List<DailyPointDto> AnnouncementsPerDay,
    List<DailyPointDto> ReadsPerDay,
    List<PriorityStatDto> ByPriority,
    List<PersonSummaryDto> MostUnread);

/// <summary>One announcement as received on one PC (per-person and per-PC reports).</summary>
public sealed record ReadRecordDto(
    Guid AnnouncementId,
    string Title,
    AnnouncementPriority Priority,
    DateTime CreatedAtUtc,
    Guid ClientId,
    string MachineName,
    string User,
    DateTime? DeliveredAtUtc,
    DateTime? DisplayedAtUtc,
    DateTime? AcknowledgedAtUtc);

public sealed record ResendResultDto(int Unread, int SentNow);

public sealed record MeDto(string Name, string? Email, bool IsAdmin);

public static class AuthRoutes
{
    public const string Config = "api/auth/config";
    public const string Login = "api/auth/login";
    public const string Me = "api/me";
}

/// <summary>One line of an IT support chat with a PC.</summary>
public sealed record ChatMessageDto(
    long Id,
    Guid ClientId,
    string MachineName,
    // True when IT wrote it, false when the PC's user did.
    bool FromAdmin,
    string Author,
    string Text,
    DateTime SentAtUtc);

public sealed record SendChatRequest(string Text);

public static class ChatLimits
{
    public const int MaxLength = 2000;
}
