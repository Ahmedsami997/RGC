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
}

/// <summary>Methods Client Agents invoke on the server.</summary>
public static class AgentServerMethods
{
    public const string Register = "Register";
    public const string ConfirmDelivered = "ConfirmDelivered";
    public const string Acknowledge = "Acknowledge";
}

/// <summary>Methods the server invokes on Admin Consoles.</summary>
public static class AdminClientMethods
{
    public const string ClientsChanged = "ClientsChanged";
    public const string RecipientUpdated = "RecipientUpdated";
    public const string AnnouncementCreated = "AnnouncementCreated";
}

public sealed record AgentRegistration(
    Guid ClientId,
    string MachineName,
    string UserName,
    string? IpAddress,
    string AgentVersion,
    string? OsVersion);

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

public sealed record SendAnnouncementRequest(string Title, string Message, AnnouncementPriority Priority);

public sealed record ClientDto(
    Guid Id,
    string MachineName,
    string UserName,
    string? IpAddress,
    string? OsVersion,
    string AgentVersion,
    bool IsOnline,
    DateTime FirstSeenUtc,
    DateTime LastSeenUtc);

public sealed record AnnouncementSummaryDto(
    Guid Id,
    string Title,
    string Message,
    AnnouncementPriority Priority,
    DateTime CreatedAtUtc,
    string CreatedBy,
    int TotalRecipients,
    int Delivered,
    int Acknowledged);

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

public sealed record MeDto(string Name, string? Email, bool IsAdmin);

public static class AuthRoutes
{
    public const string Config = "api/auth/config";
    public const string Login = "api/auth/login";
    public const string Me = "api/me";
}
