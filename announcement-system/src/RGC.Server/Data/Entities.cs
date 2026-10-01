using RGC.Shared;

namespace RGC.Server.Data;

public class AdminUser
{
    public int Id { get; set; }
    public string Username { get; set; } = "";
    public string DisplayName { get; set; } = "";
    public string PasswordHash { get; set; } = "";
    public bool IsActive { get; set; } = true;
    public int FailedLoginCount { get; set; }
    public DateTime? LockoutEndUtc { get; set; }
    public DateTime CreatedAtUtc { get; set; }
    public DateTime? LastLoginUtc { get; set; }
}

public class ClientComputer
{
    public Guid Id { get; set; }
    public string MachineName { get; set; } = "";
    public string UserName { get; set; } = "";
    public string? IpAddress { get; set; }
    public string? OsVersion { get; set; }
    public string AgentVersion { get; set; } = "";
    public bool IsOnline { get; set; }
    public DateTime FirstSeenUtc { get; set; }
    public DateTime LastSeenUtc { get; set; }
}

public class Announcement
{
    public Guid Id { get; set; }
    public string Title { get; set; } = "";
    public string Message { get; set; } = "";
    public AnnouncementPriority Priority { get; set; }
    public DateTime CreatedAtUtc { get; set; }
    public string CreatedBy { get; set; } = "";
    public List<AnnouncementRecipient> Recipients { get; set; } = new();
}

public class AnnouncementRecipient
{
    public Guid AnnouncementId { get; set; }
    public Announcement Announcement { get; set; } = null!;
    public Guid ClientId { get; set; }
    public ClientComputer Client { get; set; } = null!;
    /// <summary>When the agent confirmed it received the announcement.</summary>
    public DateTime? DeliveredAtUtc { get; set; }
    /// <summary>When the popup was shown on the PC (agent clock).</summary>
    public DateTime? DisplayedAtUtc { get; set; }
    /// <summary>When the user closed the popup (agent clock).</summary>
    public DateTime? AcknowledgedAtUtc { get; set; }
    /// <summary>When the server received the acknowledgement (server clock).</summary>
    public DateTime? AckReceivedAtUtc { get; set; }
    public string? AcknowledgedBy { get; set; }
}
