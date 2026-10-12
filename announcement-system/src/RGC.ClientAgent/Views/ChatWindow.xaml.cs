using System.Windows;
using RGC.Branding.Controls;
using RGC.ClientAgent.Services;
using RGC.Shared;

namespace RGC.ClientAgent.Views;

/// <summary>Chat with IT support. Opens when IT writes, or from the tray menu.</summary>
public partial class ChatWindow : Window
{
    public ChatWindow(AgentConnection connection)
    {
        InitializeComponent();
        Chat.Send = connection.SendChatAsync;
        // Bottom-right, above the taskbar, like a chat pop-up.
        var area = SystemParameters.WorkArea;
        Left = area.Right - Width - 16;
        Top = area.Bottom - Height - 16;
    }

    public void Add(ChatMessageDto m) =>
        Chat.Add(new ChatLine(m.Id, m.FromAdmin ? $"IT – {m.Author}" : "You", m.Text, m.SentAtUtc, Mine: !m.FromAdmin));

    /// <summary>Shows the window; when <paramref name="activate"/> brings it in front of other windows.</summary>
    public void Reveal(bool activate)
    {
        if (!IsVisible) Show();
        if (WindowState == WindowState.Minimized) WindowState = WindowState.Normal;
        if (!activate) return;
        // Briefly topmost so it appears above whatever the user is working in, then behave normally.
        Topmost = true;
        Activate();
        Topmost = false;
        Chat.FocusInput();
    }
}
