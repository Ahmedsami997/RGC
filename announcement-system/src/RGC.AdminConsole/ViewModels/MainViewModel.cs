using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Threading;
using RGC.AdminConsole.Services;
using RGC.Shared;

namespace RGC.AdminConsole.ViewModels;

public sealed class MainViewModel : ObservableObject
{
    private readonly ApiClient _api;
    private readonly DispatcherTimer _clientsDebounce;
    private readonly DispatcherTimer _historyDebounce;
    private LiveUpdates? _live;

    public event Action? SignOutRequested;

    public MainViewModel(ApiClient api)
    {
        _api = api;
        AdminName = api.DisplayName;
        ServerUrl = api.ServerUrl;

        ClientsView = CollectionViewSource.GetDefaultView(Clients);
        ClientsView.Filter = o => o is ClientDto c && MatchesClientFilter(c);
        RecipientsView = CollectionViewSource.GetDefaultView(Recipients);
        RecipientsView.Filter = o => o is RecipientRow r && MatchesRecipientFilter(r);

        _clientsDebounce = Debounce(async () => await LoadClientsAsync());
        _historyDebounce = Debounce(async () => await LoadHistoryAsync());

        RefreshCommand = new AsyncRelayCommand(RefreshAllAsync);
        SendCommand = new AsyncRelayCommand(SendAsync, CanSend);
        ClearComposeCommand = new RelayCommand(ClearCompose);
        ExportRecipientsCommand = new RelayCommand(ExportRecipients, () => SelectedAnnouncement is not null);
        SignOutCommand = new RelayCommand(() => SignOutRequested?.Invoke());
        GoComposeCommand = new RelayCommand(() => IsCompose = true);
    }

    // ---------------------------------------------------------------- shell
    public string AdminName { get; }
    public string ServerUrl { get; }

    private bool _isDashboard = true, _isCompose, _isHistory;
    public bool IsDashboard { get => _isDashboard; set => Set(ref _isDashboard, value); }
    public bool IsCompose { get => _isCompose; set => Set(ref _isCompose, value); }
    public bool IsHistory { get => _isHistory; set => Set(ref _isHistory, value); }

    private bool _liveConnected;
    public bool LiveConnected { get => _liveConnected; private set { if (Set(ref _liveConnected, value)) OnPropertyChanged(nameof(LiveStatusText)); } }
    public string LiveStatusText => LiveConnected ? "Live" : "Reconnecting...";

    private string? _status;
    public string? StatusMessage { get => _status; private set => Set(ref _status, value); }

    public ICommand RefreshCommand { get; }
    public ICommand SignOutCommand { get; }
    public ICommand GoComposeCommand { get; }

    public async Task InitializeAsync()
    {
        await RefreshAllAsync();
        try
        {
            _live = new LiveUpdates(_api.ServerUrl, _api.TokenProvider);
            _live.ConnectedChanged += c => Ui(() => LiveConnected = c);
            _live.ClientsChanged += () => Ui(() => Restart(_clientsDebounce));
            _live.AnnouncementCreated += _ => Ui(() => Restart(_historyDebounce));
            _live.RecipientUpdated += r => Ui(() => OnRecipientUpdated(r));
            await _live.StartAsync();
        }
        catch (Exception ex)
        {
            StatusMessage = $"Live updates unavailable: {ex.Message}";
        }
    }

    public async Task ShutdownAsync()
    {
        if (_live is not null) await _live.DisposeAsync();
    }

    private async Task RefreshAllAsync()
    {
        await LoadClientsAsync();
        await LoadHistoryAsync();
        if (SelectedAnnouncement is not null) await LoadRecipientsAsync(SelectedAnnouncement.Id);
    }

    // ---------------------------------------------------------------- dashboard
    public ObservableCollection<ClientDto> Clients { get; } = new();
    public ICollectionView ClientsView { get; }

    private string _clientFilter = "";
    public string ClientFilter
    {
        get => _clientFilter;
        set { if (Set(ref _clientFilter, value)) ClientsView.Refresh(); }
    }

    public int TotalClients => Clients.Count;
    public int OnlineClients => Clients.Count(c => c.IsOnline);
    public int OfflineClients => TotalClients - OnlineClients;
    public int AnnouncementsToday => History.Count(a => a.CreatedAtUtc.ToLocalTime().Date == DateTime.Today);

    private bool MatchesClientFilter(ClientDto c) =>
        string.IsNullOrWhiteSpace(ClientFilter) ||
        c.MachineName.Contains(ClientFilter, StringComparison.OrdinalIgnoreCase) ||
        c.UserName.Contains(ClientFilter, StringComparison.OrdinalIgnoreCase) ||
        (c.IpAddress?.Contains(ClientFilter, StringComparison.OrdinalIgnoreCase) ?? false);

    private async Task LoadClientsAsync()
    {
        var clients = await Guard(_api.GetClientsAsync);
        if (clients is null) return;
        Clients.Clear();
        foreach (var c in clients) Clients.Add(c);
        OnPropertyChanged(nameof(TotalClients));
        OnPropertyChanged(nameof(OnlineClients));
        OnPropertyChanged(nameof(OfflineClients));
        OnPropertyChanged(nameof(RecipientSummary));
    }

    // ---------------------------------------------------------------- compose
    private string _title = "", _message = "";
    private AnnouncementPriority _priority = AnnouncementPriority.Normal;

    public string ComposeTitle { get => _title; set { if (Set(ref _title, value)) OnPropertyChanged(nameof(TitleCount)); } }
    public string ComposeMessage { get => _message; set { if (Set(ref _message, value)) OnPropertyChanged(nameof(MessageCount)); } }
    public AnnouncementPriority ComposePriority { get => _priority; set => Set(ref _priority, value); }
    public IReadOnlyList<AnnouncementPriority> Priorities { get; } = Enum.GetValues<AnnouncementPriority>();

    public string TitleCount => $"{ComposeTitle.Length} / 200";
    public string MessageCount => $"{ComposeMessage.Length} / 8000";
    public string RecipientSummary => $"Will be delivered immediately to {OnlineClients} online PC(s). " +
                                      $"{OfflineClients} offline PC(s) receive it when they next connect.";

    public ICommand SendCommand { get; }
    public ICommand ClearComposeCommand { get; }

    private bool CanSend() =>
        !string.IsNullOrWhiteSpace(ComposeTitle) && !string.IsNullOrWhiteSpace(ComposeMessage) &&
        ComposeTitle.Length <= 200 && ComposeMessage.Length <= 8000;

    private async Task SendAsync()
    {
        var confirm = MessageBox.Show(
            $"Broadcast this {ComposePriority.ToString().ToUpperInvariant()} announcement to all computers?\n\n\"{ComposeTitle.Trim()}\"\n\n{RecipientSummary}",
            "RGC – Confirm broadcast", MessageBoxButton.YesNo,
            ComposePriority == AnnouncementPriority.Critical ? MessageBoxImage.Warning : MessageBoxImage.Question);
        if (confirm != MessageBoxResult.Yes) return;

        var result = await Guard(() => _api.SendAnnouncementAsync(new SendAnnouncementRequest(ComposeTitle, ComposeMessage, ComposePriority)));
        if (result is null) return;

        StatusMessage = $"Announcement \"{result.Title}\" sent to {result.TotalRecipients} PC(s) at {result.CreatedAtUtc.ToLocalTime():HH:mm:ss}.";
        ClearCompose();
        await LoadHistoryAsync();
        SelectedAnnouncement = History.FirstOrDefault(a => a.Id == result.Id);
        IsHistory = true;
    }

    private void ClearCompose()
    {
        ComposeTitle = "";
        ComposeMessage = "";
        ComposePriority = AnnouncementPriority.Normal;
    }

    // ---------------------------------------------------------------- history
    public ObservableCollection<AnnouncementRow> History { get; } = new();
    public ObservableCollection<RecipientRow> Recipients { get; } = new();
    public ICollectionView RecipientsView { get; }
    public ICommand ExportRecipientsCommand { get; }

    public IReadOnlyList<string> RecipientFilters { get; } = ["All computers", "Pending delivery", "Not read yet", "Read"];

    private string _recipientFilter = "All computers";
    public string RecipientFilter
    {
        get => _recipientFilter;
        set { if (Set(ref _recipientFilter, value)) RecipientsView.Refresh(); }
    }

    private bool MatchesRecipientFilter(RecipientRow r) => RecipientFilter switch
    {
        "Pending delivery" => r.State == RecipientState.Pending,
        "Not read yet" => r.State != RecipientState.Read,
        "Read" => r.State == RecipientState.Read,
        _ => true
    };

    private AnnouncementRow? _selected;
    public AnnouncementRow? SelectedAnnouncement
    {
        get => _selected;
        set
        {
            if (!Set(ref _selected, value)) return;
            OnPropertyChanged(nameof(HasSelection));
            Recipients.Clear();
            if (value is not null) _ = LoadRecipientsAsync(value.Id);
        }
    }
    public bool HasSelection => SelectedAnnouncement is not null;

    private async Task LoadHistoryAsync()
    {
        var items = await Guard(_api.GetAnnouncementsAsync);
        if (items is null) return;

        // Merge in place so the selection and scroll position survive live refreshes.
        var byId = History.ToDictionary(h => h.Id);
        for (var i = 0; i < items.Count; i++)
        {
            if (byId.TryGetValue(items[i].Id, out var existing))
            {
                existing.Update(items[i]);
                var idx = History.IndexOf(existing);
                if (idx != i) History.Move(idx, i);
            }
            else
            {
                History.Insert(i, new AnnouncementRow(items[i]));
            }
        }
        while (History.Count > items.Count) History.RemoveAt(History.Count - 1);
        OnPropertyChanged(nameof(AnnouncementsToday));
    }

    private async Task LoadRecipientsAsync(Guid announcementId)
    {
        var items = await Guard(() => _api.GetRecipientsAsync(announcementId));
        if (items is null || SelectedAnnouncement?.Id != announcementId) return;
        Recipients.Clear();
        foreach (var r in items) Recipients.Add(new RecipientRow(r));
    }

    private void OnRecipientUpdated(RecipientStatusDto dto)
    {
        if (SelectedAnnouncement?.Id == dto.AnnouncementId)
        {
            var row = Recipients.FirstOrDefault(r => r.ClientId == dto.ClientId);
            if (row is null) Recipients.Add(new RecipientRow(dto));
            else row.Update(dto);
            RecipientsView.Refresh();
        }
        Restart(_historyDebounce);
    }

    private void ExportRecipients()
    {
        if (SelectedAnnouncement is null) return;
        var dialog = new Microsoft.Win32.SaveFileDialog
        {
            FileName = $"RGC-announcement-{SelectedAnnouncement.CreatedAtUtc.ToLocalTime():yyyyMMdd-HHmm}.csv",
            Filter = "CSV files (*.csv)|*.csv"
        };
        if (dialog.ShowDialog() != true) return;

        static string Csv(string? s) => $"\"{(s ?? "").Replace("\"", "\"\"")}\"";
        static string Time(DateTime? t) => t?.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss") ?? "";

        var sb = new StringBuilder();
        sb.AppendLine($"Announcement,{Csv(SelectedAnnouncement.Title)}");
        sb.AppendLine($"Priority,{SelectedAnnouncement.Priority}");
        sb.AppendLine($"Sent,{Time(SelectedAnnouncement.CreatedAtUtc)},by,{Csv(SelectedAnnouncement.CreatedBy)}");
        sb.AppendLine();
        sb.AppendLine("Computer,User,Status,Delivered at,Displayed at,Acknowledged at,Acknowledged by");
        foreach (var r in Recipients)
            sb.AppendLine(string.Join(',', Csv(r.MachineName), Csv(r.UserName), Csv(r.StateText),
                Time(r.DeliveredAtUtc), Time(r.DisplayedAtUtc), Time(r.AcknowledgedAtUtc), Csv(r.AcknowledgedBy)));

        try
        {
            File.WriteAllText(dialog.FileName, sb.ToString(), Encoding.UTF8);
            StatusMessage = $"Exported {Recipients.Count} rows to {dialog.FileName}";
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "RGC – Export failed", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    // ---------------------------------------------------------------- helpers
    private async Task<T?> Guard<T>(Func<Task<T>> call) where T : class
    {
        try
        {
            var result = await call();
            if (StatusMessage?.StartsWith("Error") == true) StatusMessage = null;
            return result;
        }
        catch (SessionExpiredException ex)
        {
            MessageBox.Show(ex.Message, "RGC", MessageBoxButton.OK, MessageBoxImage.Information);
            SignOutRequested?.Invoke();
        }
        catch (Exception ex)
        {
            StatusMessage = $"Error: {ex.Message}";
        }
        return null;
    }

    private static DispatcherTimer Debounce(Func<Task> action)
    {
        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(600) };
        timer.Tick += async (_, _) =>
        {
            timer.Stop();
            await action();
        };
        return timer;
    }

    private static void Restart(DispatcherTimer timer)
    {
        timer.Stop();
        timer.Start();
    }

    private static void Ui(Action action) => Application.Current?.Dispatcher.InvokeAsync(action);
}
