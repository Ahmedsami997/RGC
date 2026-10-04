using RGC.Shared;

namespace RGC.AdminConsole.ViewModels;

public sealed class AnnouncementRow(AnnouncementSummaryDto dto) : ObservableObject
{
    public Guid Id { get; } = dto.Id;
    public string Title { get; } = dto.Title;
    public string Message { get; } = dto.Message;
    public AnnouncementPriority Priority { get; } = dto.Priority;
    public DateTime CreatedAtUtc { get; } = dto.CreatedAtUtc;
    public string CreatedBy { get; } = dto.CreatedBy;

    private int _total = dto.TotalRecipients, _delivered = dto.Delivered, _acknowledged = dto.Acknowledged;
    public int TotalRecipients { get => _total; private set { if (Set(ref _total, value)) Notify(); } }
    public int Delivered { get => _delivered; private set { if (Set(ref _delivered, value)) Notify(); } }
    public int Acknowledged { get => _acknowledged; private set { if (Set(ref _acknowledged, value)) Notify(); } }

    public string DeliveredText => $"{Delivered} / {TotalRecipients}";
    public string AcknowledgedText => $"{Acknowledged} / {TotalRecipients}";
    public double ReadPercent => TotalRecipients == 0 ? 0 : 100.0 * Acknowledged / TotalRecipients;

    public void Update(AnnouncementSummaryDto dto)
    {
        TotalRecipients = dto.TotalRecipients;
        Delivered = dto.Delivered;
        Acknowledged = dto.Acknowledged;
    }

    private void Notify()
    {
        OnPropertyChanged(nameof(DeliveredText));
        OnPropertyChanged(nameof(AcknowledgedText));
        OnPropertyChanged(nameof(ReadPercent));
    }
}

public enum RecipientState { Pending, Delivered, Read }

public sealed class RecipientRow : ObservableObject
{
    public Guid ClientId { get; }
    private RecipientStatusDto _dto;

    private readonly DateTime _sentAtUtc;

    public RecipientRow(RecipientStatusDto dto, DateTime sentAtUtc)
    {
        ClientId = dto.ClientId;
        _dto = dto;
        _sentAtUtc = sentAtUtc;
    }

    public string MachineName => _dto.MachineName;
    public string UserName => _dto.UserName;
    public bool IsOnline => _dto.IsOnline;
    public DateTime? DeliveredAtUtc => _dto.DeliveredAtUtc;
    public DateTime? DisplayedAtUtc => _dto.DisplayedAtUtc;
    public DateTime? AcknowledgedAtUtc => _dto.AcknowledgedAtUtc;
    public string? AcknowledgedBy => _dto.AcknowledgedBy;

    public RecipientState State =>
        _dto.AcknowledgedAtUtc is not null ? RecipientState.Read :
        _dto.DeliveredAtUtc is not null ? RecipientState.Delivered : RecipientState.Pending;

    public string TimeToReadText => Fmt.Duration(Fmt.MinutesBetween(_sentAtUtc, _dto.AcknowledgedAtUtc));

    public string StateText => State switch
    {
        RecipientState.Read => "Read",
        RecipientState.Delivered => "Delivered – not read yet",
        _ => "Pending delivery"
    };

    public void Update(RecipientStatusDto dto)
    {
        _dto = dto;
        OnPropertyChanged(string.Empty); // refresh every binding on this row
    }
}
