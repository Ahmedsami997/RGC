using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows.Data;
using System.Windows.Input;

namespace RGC.AdminConsole.ViewModels;

/// <summary>A computer in the "Send to" picker of the New announcement page.</summary>
public sealed class TargetRow(ComputerRow computer, bool isSelected, Action<TargetRow> selectionChanged) : ObservableObject
{
    public ComputerRow Computer { get; } = computer;
    public Guid Id => Computer.Id;
    public string MachineName => Computer.MachineName;
    public string UserText => Computer.UserText;
    public bool IsOnline => Computer.IsOnline;

    private bool _isSelected = isSelected;
    public bool IsSelected
    {
        get => _isSelected;
        set { if (Set(ref _isSelected, value)) selectionChanged(this); }
    }
}

// "Send to": all computers, or only the ones ticked in the picker.
public sealed partial class MainViewModel
{
    private readonly HashSet<Guid> _selectedTargets = new();

    public ObservableCollection<TargetRow> Targets { get; } = new();
    public ICollectionView TargetsView { get; private set; } = null!;
    public ICommand SelectAllTargetsCommand { get; private set; } = null!;
    public ICommand ClearTargetsCommand { get; private set; } = null!;
    public ICommand SendToComputerCommand { get; private set; } = null!;

    private void InitializeTargets()
    {
        TargetsView = CollectionViewSource.GetDefaultView(Targets);
        TargetsView.Filter = o => o is TargetRow t && MatchesTargetFilter(t);
        SelectAllTargetsCommand = new RelayCommand(() =>
        {
            foreach (var t in TargetsView.Cast<TargetRow>()) t.IsSelected = true;
        });
        ClearTargetsCommand = new RelayCommand(() =>
        {
            foreach (var t in Targets) t.IsSelected = false;
        });
        SendToComputerCommand = new RelayCommand(() =>
        {
            if (SelectedComputer is not { } pc) return;
            foreach (var t in Targets) t.IsSelected = t.Id == pc.Id;
            SendToSelected = true;
            IsCompose = true;
        }, () => SelectedComputer is not null);
    }

    private bool _sendToSelected;
    public bool SendToSelected
    {
        get => _sendToSelected;
        set
        {
            if (!Set(ref _sendToSelected, value)) return;
            OnPropertyChanged(nameof(SendToAll));
            OnTargetsChanged();
        }
    }

    public bool SendToAll
    {
        get => !SendToSelected;
        set => SendToSelected = !value;
    }

    private string _targetFilter = "";
    public string TargetFilter
    {
        get => _targetFilter;
        set { if (Set(ref _targetFilter, value)) TargetsView.Refresh(); }
    }

    private bool MatchesTargetFilter(TargetRow t) =>
        string.IsNullOrWhiteSpace(TargetFilter) ||
        t.MachineName.Contains(TargetFilter, StringComparison.OrdinalIgnoreCase) ||
        t.UserText.Contains(TargetFilter, StringComparison.OrdinalIgnoreCase) ||
        t.Computer.UserName.Contains(TargetFilter, StringComparison.OrdinalIgnoreCase);

    public int SelectedTargetCount => _selectedTargets.Count;
    public string SelectedTargetText => $"{_selectedTargets.Count} selected";

    public string SendButtonText => !SendToSelected
        ? "Send to all computers"
        : $"Send to {_selectedTargets.Count} computer{(_selectedTargets.Count == 1 ? "" : "s")}";

    /// <summary>Keeps the picker in step with the Computers list; ticks survive refreshes.</summary>
    private void RebuildTargets()
    {
        var known = Clients.Select(c => c.Id).ToHashSet();
        _selectedTargets.IntersectWith(known);
        Targets.Clear();
        foreach (var c in Clients.OrderBy(c => c.MachineName, StringComparer.OrdinalIgnoreCase))
            Targets.Add(new TargetRow(c, _selectedTargets.Contains(c.Id), OnTargetToggled));
        OnTargetsChanged();
    }

    private void OnTargetToggled(TargetRow row)
    {
        if (row.IsSelected) _selectedTargets.Add(row.Id);
        else _selectedTargets.Remove(row.Id);
        OnTargetsChanged();
    }

    private void OnTargetsChanged()
    {
        OnPropertyChanged(nameof(SelectedTargetCount));
        OnPropertyChanged(nameof(SelectedTargetText));
        OnPropertyChanged(nameof(SendButtonText));
        OnPropertyChanged(nameof(RecipientSummary));
        CommandManager.InvalidateRequerySuggested();
    }

    private string TargetSummary()
    {
        if (_selectedTargets.Count == 0) return "Tick the computers that should receive this announcement.";
        var online = Clients.Count(c => c.IsOnline && _selectedTargets.Contains(c.Id));
        var offline = _selectedTargets.Count - online;
        return $"Will be delivered to {_selectedTargets.Count} selected computer(s): {online} online now" +
               (offline > 0 ? $", {offline} when they next connect." : ".");
    }

    private List<Guid>? TargetIds() => SendToSelected ? _selectedTargets.ToList() : null;
}
