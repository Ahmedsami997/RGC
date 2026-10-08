using System.Windows.Input;
using RGC.AdminConsole.Services;
using RGC.Shared;

namespace RGC.AdminConsole.ViewModels;

// IT support: chat with a PC's user and Windows Remote Assistance.
public sealed partial class MainViewModel
{
    /// <summary>The window should open (or bring forward) the chat with this PC.</summary>
    public event Action<Guid>? ChatOpenRequested;
    /// <summary>A chat line arrived from the server (from IT or from a PC).</summary>
    public event Action<ChatMessageDto>? ChatArrived;

    public ICommand OpenChatCommand { get; private set; } = null!;
    public ICommand RemoteAssistCommand { get; private set; } = null!;

    private void InitializeSupport()
    {
        OpenChatCommand = new RelayCommand(() =>
        {
            if (SelectedComputer is { } pc) ChatOpenRequested?.Invoke(pc.Id);
        }, () => SelectedComputer is not null);
        RemoteAssistCommand = new RelayCommand(() =>
        {
            if (SelectedComputer is { } pc) Connect(pc.MachineName);
        }, () => SelectedComputer is not null);
    }

    public ComputerRow? FindComputer(Guid id) => Clients.FirstOrDefault(c => c.Id == id);

    /// <summary>Opens Windows Remote Assistance to the PC; the user there clicks Allow.</summary>
    public void Connect(string machineName)
    {
        try
        {
            RemoteAssistance.Offer(machineName);
            StatusMessage = $"Remote Assistance started for {machineName}. The user must click Yes on their screen.";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Error: {ex.Message}";
        }
    }

    private void OnChatArrived(ChatMessageDto m)
    {
        if (!m.FromAdmin) StatusMessage = $"New message from {m.MachineName} at {m.SentAtUtc.ToLocalTime():HH:mm}.";
        ChatArrived?.Invoke(m);
    }
}
