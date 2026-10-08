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
        _vm.ChatOpenRequested += id => OpenChat(id);
        _vm.ChatArrived += OnChatArrived;
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

    private readonly Dictionary<Guid, ChatWindow> _chats = new();

    private ChatWindow OpenChat(Guid clientId, ChatMessageDto? about = null)
    {
        if (!_chats.TryGetValue(clientId, out var window))
        {
            var pc = _vm.FindComputer(clientId);
            window = new ChatWindow(_api, _vm, clientId, pc?.MachineName ?? about?.MachineName ?? "PC", pc?.UserText ?? "") { Owner = this };
            window.Closed += (_, _) => _chats.Remove(clientId);
            _chats[clientId] = window;
        }
        window.Reveal();
        return window;
    }

    private void OnChatArrived(ChatMessageDto m)
    {
        if (_chats.TryGetValue(m.ClientId, out var open)) open.Add(m);
        // A PC writing to IT opens its chat; IT's own lines from another Admin Console don't.
        else if (!m.FromAdmin) OpenChat(m.ClientId, m);
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
        foreach (var chat in _chats.Values.ToList()) chat.Close();
        await _vm.ShutdownAsync();
        _api.Dispose();
    }
}
