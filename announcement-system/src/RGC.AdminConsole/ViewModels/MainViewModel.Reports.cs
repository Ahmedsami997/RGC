using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Input;
using RGC.Shared;

namespace RGC.AdminConsole.ViewModels;

public sealed record PeriodOption(string Label, int Days);

// Dashboard statistics, the Computers detail panel and the People page.
public sealed partial class MainViewModel
{
    // ---------------------------------------------------------------- dashboard statistics
    public IReadOnlyList<PeriodOption> Periods { get; } =
    [
        new("Last 7 days", 7), new("Last 30 days", 30), new("Last 90 days", 90), new("Last 12 months", 365)
    ];

    private PeriodOption? _period;
    public PeriodOption SelectedPeriod
    {
        get => _period ??= Periods[1];
        set { if (Set(ref _period, value)) _ = LoadStatsAsync(); }
    }

    private StatsDto? _stats;
    public StatsDto? Stats { get => _stats; private set => Set(ref _stats, value); }

    public string ActivePcsText => Stats is null ? "—" : Stats.ActivePcs.ToString();
    public string AnnouncementsText => Stats is null ? "—" : Stats.Announcements.ToString();
    public string ReadRateText => Stats is null ? "—" : Fmt.Percent(Stats.Read, Stats.Recipients);
    public string ReadRateDetail => Stats is null ? "" : $"{Stats.Read} of {Stats.Recipients} popups closed";
    public string AvgTimeText => Fmt.Duration(Stats?.AvgMinutesToRead);
    public string AvgTimeDetail => Stats?.MedianMinutesToRead is { } m ? $"Median {Fmt.Duration(m)} · on screen {Fmt.Seconds(Stats.AvgSecondsOnScreen)}" : "";
    public string WaitingText => Stats is null ? "—" : Stats.WaitingToBeRead.ToString();

    public IReadOnlyList<ChartPoint> ActivePcsSeries { get; private set; } = [];
    public IReadOnlyList<ChartPoint> SentSeries { get; private set; } = [];
    public IReadOnlyList<ChartPoint> ReadsSeries { get; private set; } = [];
    public IReadOnlyList<PriorityRateRow> PriorityRates { get; private set; } = [];
    public IReadOnlyList<PersonRow> MostUnread { get; private set; } = [];
    public bool HasMostUnread => MostUnread.Count > 0;

    private async Task LoadStatsAsync()
    {
        var days = SelectedPeriod.Days;
        var stats = await Guard(() => _api.GetStatsAsync(days));
        if (stats is null || stats.Days != SelectedPeriod.Days) return;

        Stats = stats;
        ActivePcsSeries = Series(stats.ActivePcsPerDay, "PC", "PCs");
        SentSeries = Series(stats.AnnouncementsPerDay, "announcement", "announcements");
        ReadsSeries = Series(stats.ReadsPerDay, "popup read", "popups read");
        PriorityRates = stats.ByPriority
            .Select(p => new PriorityRateRow(p.Priority, Fmt.PercentValue(p.Read, p.Recipients), Fmt.Percent(p.Read, p.Recipients),
                $"{p.Announcements} sent · {p.Read} of {p.Recipients} read"))
            .ToList();
        MostUnread = stats.MostUnread.Select(p => new PersonRow(p)).ToList();

        foreach (var name in new[]
                 {
                     nameof(ActivePcsText), nameof(AnnouncementsText), nameof(ReadRateText), nameof(ReadRateDetail),
                     nameof(AvgTimeText), nameof(AvgTimeDetail), nameof(WaitingText), nameof(ActivePcsSeries),
                     nameof(SentSeries), nameof(ReadsSeries), nameof(PriorityRates), nameof(MostUnread), nameof(HasMostUnread)
                 })
            OnPropertyChanged(name);
    }

    private static IReadOnlyList<ChartPoint> Series(List<DailyPointDto> points, string one, string many) =>
        points.Select(p => new ChartPoint(p.Day.ToString("dd MMM"), p.Value,
            $"{p.Day:ddd dd MMM}: {p.Value} {(p.Value == 1 ? one : many)}")).ToList();

    // ---------------------------------------------------------------- computer details
    private ComputerRow? _selectedComputer;
    public ComputerRow? SelectedComputer
    {
        get => _selectedComputer;
        set
        {
            if (!Set(ref _selectedComputer, value)) return;
            OnPropertyChanged(nameof(HasComputerSelection));
            ComputerRecords.Clear();
            if (value is not null) _ = LoadComputerRecordsAsync(value.Id);
        }
    }
    public bool HasComputerSelection => SelectedComputer is not null;
    public ObservableCollection<ReadRecordRow> ComputerRecords { get; } = new();

    private async Task LoadComputerRecordsAsync(Guid clientId)
    {
        var records = await Guard(() => _api.GetClientRecordsAsync(clientId));
        if (records is null || SelectedComputer?.Id != clientId) return;
        ComputerRecords.Clear();
        foreach (var r in records) ComputerRecords.Add(new ReadRecordRow(r));
    }

    public ICommand ExportComputersCommand { get; }

    private void ExportComputers() =>
        ExportCsv($"RGC-computers-{DateTime.Now:yyyyMMdd}.csv",
            ["Computer,Status,User,365 / account,Windows user,Windows version,Local IP,Public IP,Agent,Received,Read,Read rate,Avg time to read,Last read,Last seen,First seen"],
            ClientsView.Cast<ComputerRow>().Select(c => string.Join(',',
                Csv(c.MachineName), c.IsOnline ? "Online" : "Offline", Csv(c.UserText), Csv(c.UserName), Csv(c.WindowsUser),
                Csv(c.OsVersion), Csv(c.IpAddress), Csv(c.PublicIp), Csv(c.AgentVersion), c.Received, c.Read,
                Csv(c.ReadRateText), Csv(c.AvgTimeText), CsvTime(c.LastReadAtUtc), CsvTime(c.LastSeenUtc), CsvTime(c.FirstSeenUtc))));

    // ---------------------------------------------------------------- people
    public ObservableCollection<PersonRow> People { get; } = new();
    public ICollectionView PeopleView { get; }
    public ObservableCollection<ReadRecordRow> PersonRecords { get; } = new();

    private string _personFilter = "";
    public string PersonFilter
    {
        get => _personFilter;
        set { if (Set(ref _personFilter, value)) PeopleView.Refresh(); }
    }

    public IReadOnlyList<string> PersonStatusFilters { get; } = ["Everyone", "Has unread", "Read everything"];

    private string _personStatusFilter = "Everyone";
    public string PersonStatusFilter
    {
        get => _personStatusFilter;
        set { if (Set(ref _personStatusFilter, value)) PeopleView.Refresh(); }
    }

    private bool MatchesPersonFilter(PersonRow p)
    {
        var status = PersonStatusFilter switch
        {
            "Has unread" => p.Unread > 0,
            "Read everything" => p.Unread == 0,
            _ => true
        };
        return status && (string.IsNullOrWhiteSpace(PersonFilter) ||
            p.Name.Contains(PersonFilter, StringComparison.OrdinalIgnoreCase) ||
            p.User.Contains(PersonFilter, StringComparison.OrdinalIgnoreCase) ||
            p.Computers.Contains(PersonFilter, StringComparison.OrdinalIgnoreCase));
    }

    private PersonRow? _selectedPerson;
    public PersonRow? SelectedPerson
    {
        get => _selectedPerson;
        set
        {
            if (!Set(ref _selectedPerson, value)) return;
            OnPropertyChanged(nameof(HasPersonSelection));
            PersonRecords.Clear();
            if (value is not null) _ = LoadPersonRecordsAsync(value.User);
        }
    }
    public bool HasPersonSelection => SelectedPerson is not null;

    private async Task LoadPeopleAsync()
    {
        var people = await Guard(_api.GetPeopleAsync);
        if (people is null) return;
        var selectedUser = SelectedPerson?.User;
        People.Clear();
        foreach (var p in people) People.Add(new PersonRow(p));
        _selectedPerson = People.FirstOrDefault(p => string.Equals(p.User, selectedUser, StringComparison.OrdinalIgnoreCase));
        OnPropertyChanged(nameof(SelectedPerson));
        OnPropertyChanged(nameof(HasPersonSelection));
        if (_selectedPerson is not null) await LoadPersonRecordsAsync(_selectedPerson.User);
    }

    private async Task LoadPersonRecordsAsync(string user)
    {
        var records = await Guard(() => _api.GetPersonRecordsAsync(user));
        if (records is null || SelectedPerson?.User != user) return;
        PersonRecords.Clear();
        foreach (var r in records) PersonRecords.Add(new ReadRecordRow(r));
    }

    /// <summary>Opens the People page on one person (from the dashboard or a PC's details).</summary>
    public ICommand ShowPersonCommand { get; }

    private void ShowPerson(string? user)
    {
        if (string.IsNullOrWhiteSpace(user)) return;
        PersonFilter = "";
        PersonStatusFilter = "Everyone";
        IsPeople = true;
        SelectedPerson = People.FirstOrDefault(p => string.Equals(p.User, user, StringComparison.OrdinalIgnoreCase));
    }

    public ICommand ExportPeopleCommand { get; }
    public ICommand ExportPersonRecordsCommand { get; }

    private void ExportPeople() =>
        ExportCsv($"RGC-people-{DateTime.Now:yyyyMMdd}.csv",
            ["Name,Account,Computers,Received,Read,Unread,Read rate,Avg time to read,Last read"],
            PeopleView.Cast<PersonRow>().Select(p => string.Join(',',
                Csv(p.Name), Csv(p.User), Csv(p.Computers), p.Received, p.Read, p.Unread,
                Csv(p.ReadRateText), Csv(p.AvgTimeText), CsvTime(p.LastReadAtUtc))));

    private void ExportPersonRecords()
    {
        if (SelectedPerson is not { } person) return;
        ExportCsv($"RGC-{SafeFileName(person.User)}-{DateTime.Now:yyyyMMdd}.csv",
            [$"Person,{Csv(person.Name)},{Csv(person.User)}", "",
             "Announcement,Priority,Sent,Computer,Status,Delivered at,Shown at,Read at,Time to read,On screen"],
            PersonRecords.Select(r => string.Join(',',
                Csv(r.Title), r.Priority, CsvTime(r.CreatedAtUtc), Csv(r.MachineName), Csv(r.StateText),
                CsvTime(r.DeliveredAtUtc), CsvTime(r.DisplayedAtUtc), CsvTime(r.AcknowledgedAtUtc),
                Csv(r.TimeToReadText), Csv(r.OnScreenText))));
    }

    // ---------------------------------------------------------------- CSV export
    private static string Csv(string? s) => $"\"{(s ?? "").Replace("\"", "\"\"")}\"";
    private static string CsvTime(DateTime? t) => t?.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss") ?? "";

    private static string SafeFileName(string s) =>
        string.Concat(s.Select(ch => Path.GetInvalidFileNameChars().Contains(ch) || ch == '@' ? '-' : ch));

    private void ExportCsv(string fileName, IEnumerable<string> header, IEnumerable<string> lines)
    {
        var dialog = new Microsoft.Win32.SaveFileDialog { FileName = fileName, Filter = "CSV files (*.csv)|*.csv" };
        if (dialog.ShowDialog() != true) return;

        var sb = new StringBuilder();
        foreach (var h in header) sb.AppendLine(h);
        var count = 0;
        foreach (var line in lines) { sb.AppendLine(line); count++; }
        try
        {
            // UTF-8 with BOM so Excel shows Arabic names correctly.
            File.WriteAllText(dialog.FileName, sb.ToString(), new UTF8Encoding(true));
            StatusMessage = $"Exported {count} rows to {dialog.FileName}";
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "RGC – Export failed", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }
}
