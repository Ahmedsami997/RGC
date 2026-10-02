using System.ComponentModel;
using System.Windows;
using RGC.AdminConsole.Services;
using RGC.AdminConsole.ViewModels;
using RGC.Shared;

namespace RGC.AdminConsole.Views;

public partial class MainWindow : Window
{
    private readonly MainViewModel _vm;
    private readonly ApiClient _api;

    public event Action? SignedOut;
    public bool IsSigningOut { get; private set; }

    public MainWindow(ApiClient api)
    {
        InitializeComponent();
        _api = api;
        _vm = new MainViewModel(api);
        DataContext = _vm;

        _vm.SignOutRequested += SignOut;
        _vm.PropertyChanged += SyncPriorityRadios;
        PriNormal.IsChecked = true;

        Loaded += async (_, _) => await _vm.InitializeAsync();
    }

    // The priority cards are radio buttons; keep them and the view model in sync.
    private void Priority_Checked(object sender, RoutedEventArgs e)
    {
        _vm.ComposePriority = sender == PriCritical ? AnnouncementPriority.Critical
            : sender == PriImportant ? AnnouncementPriority.Important
            : AnnouncementPriority.Normal;
    }

    private void SyncPriorityRadios(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(MainViewModel.ComposePriority)) return;
        (_vm.ComposePriority switch
        {
            AnnouncementPriority.Critical => PriCritical,
            AnnouncementPriority.Important => PriImportant,
            _ => PriNormal
        }).IsChecked = true;
    }

    private void SignOut()
    {
        if (IsSigningOut) return;
        IsSigningOut = true;
        Close();
        SignedOut?.Invoke();
    }

    protected override async void OnClosed(EventArgs e)
    {
        base.OnClosed(e);
        await _vm.ShutdownAsync();
        _api.Dispose();
    }
}
