namespace RGC.Server.Services;

public sealed class AnnouncementOptions
{
    /// <summary>Shared secret every Client Agent must present to connect.</summary>
    public string AgentKey { get; set; } = "";

    /// <summary>
    /// A PC that was offline when an announcement was sent still receives it when it
    /// reconnects, as long as the announcement is younger than this.
    /// </summary>
    public int PendingDeliveryWindowHours { get; set; } = 72;

    /// <summary>PCs not seen for longer than this are not added as recipients.</summary>
    public int InactiveClientDays { get; set; } = 30;

    public int MaxTitleLength { get; set; } = 200;
    public int MaxMessageLength { get; set; } = 8000;
}
