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
    /// <summary>Microsoft 365 email when signed in, otherwise the Windows account.</summary>
    public string UserName { get; set; } = "";
    public string? UserDisplayName { get; set; }
    public string? WindowsUser { get; set; }
    /// <summary>Address on the PC's own network.</summary>
    public string? IpAddress { get; set; }
    /// <summary>Address the server saw the connection come from.</summary>
    public string? PublicIp { get; set; }
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
    /// <summary>Last "resend to unread"; restarts the pending-delivery window.</summary>
    public DateTime? LastResentAtUtc { get; set; }
    /// <summary>"All computers" or "N selected computers".</summary>
    public string? Audience { get; set; }
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

/// <summary>One row per PC per (UTC) day it was connected; feeds the "active PCs per day" chart.</summary>
public class ClientActivity
{
    public DateTime Day { get; set; }
    public Guid ClientId { get; set; }
}
