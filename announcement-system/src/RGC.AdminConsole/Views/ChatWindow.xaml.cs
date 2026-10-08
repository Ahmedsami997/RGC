using System.Windows;
using RGC.AdminConsole.Services;
using RGC.AdminConsole.ViewModels;
using RGC.Branding.Controls;
using RGC.Shared;

namespace RGC.AdminConsole.Views;

/// <summary>IT support chat with the user of one PC.</summary>
public partial class ChatWindow : Window
{
    private readonly ApiClient _api;
    private readonly MainViewModel _vm;
    private readonly string _machine;

    public Guid ClientId { get; }

    public ChatWindow(ApiClient api, MainViewModel vm, Guid clientId, string machineName, string userText)
    {
        InitializeComponent();
        _api = api;
        _vm = vm;
        _machine = machineName;
        ClientId = clientId;
        Title = $"RGC – Chat with {machineName}";
        MachineText.Text = machineName;
        UserText.Text = userText;
        Chat.Send = async text => Add(await _api.SendChatAsync(ClientId, text));
        Loaded += async (_, _) => await LoadHistoryAsync();
    }

    public void Add(ChatMessageDto m) =>
        Chat.Add(new ChatLine(m.Id, m.FromAdmin ? m.Author : $"{m.Author} ({m.MachineName})", m.Text, m.SentAtUtc, Mine: m.FromAdmin));

    private async Task LoadHistoryAsync()
    {
        try
        {
            foreach (var m in await _api.GetChatAsync(ClientId)) Add(m);
            Chat.FocusInput();
        }
        catch (Exception ex)
        {
            Chat.SetNotice($"Could not load earlier messages: {ex.Message}");
        }
    }

    public void Reveal()
    {
        if (!IsVisible) Show();
        if (WindowState == WindowState.Minimized) WindowState = WindowState.Normal;
        Activate();
    }

    private void Connect_Click(object sender, RoutedEventArgs e) => _vm.Connect(_machine);
}
