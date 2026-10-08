using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using RGC.Shared;

namespace RGC.Branding.Controls;

/// <summary>A line in a <see cref="ChatPanel"/>; <see cref="Mine"/> lines sit on the right.</summary>
public sealed record ChatLine(long Id, string Author, string Text, DateTime SentAtUtc, bool Mine)
{
    public string Time => SentAtUtc.ToLocalTime().ToString(SentAtUtc.ToLocalTime().Date == DateTime.Today ? "HH:mm" : "dd MMM HH:mm");
}

/// <summary>Conversation view with a message box, shared by the Admin Console and the PC agent.</summary>
public partial class ChatPanel : UserControl
{
    private readonly ObservableCollection<ChatLine> _lines = new();
    private readonly HashSet<long> _ids = new();

    /// <summary>Called with the text to send; throw to show the error and keep the text.</summary>
    public Func<string, Task>? Send { get; set; }

    public ChatPanel()
    {
        InitializeComponent();
        List.ItemsSource = _lines;
    }

    /// <summary>Adds a line unless it is already shown (messages can arrive both as a reply and as a push).</summary>
    public void Add(ChatLine line)
    {
        if (line.Id != 0 && !_ids.Add(line.Id)) return;
        _lines.Add(line);
        EmptyText.Visibility = Visibility.Collapsed;
        Dispatcher.InvokeAsync(Scroller.ScrollToEnd, System.Windows.Threading.DispatcherPriority.Background);
    }

    public void Clear()
    {
        _lines.Clear();
        _ids.Clear();
        EmptyText.Visibility = Visibility.Visible;
    }

    public void SetNotice(string? text)
    {
        NoticeText.Text = text ?? "";
        NoticeText.Visibility = string.IsNullOrEmpty(text) ? Visibility.Collapsed : Visibility.Visible;
    }

    public void FocusInput() => Input.Focus();

    private void Input_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter) return;
        if (Keyboard.Modifiers.HasFlag(ModifierKeys.Shift))
        {
            var caret = Input.CaretIndex;
            Input.Text = Input.Text.Insert(caret, Environment.NewLine);
            Input.CaretIndex = caret + Environment.NewLine.Length;
        }
        else
        {
            _ = SendAsync();
        }
        e.Handled = true;
    }

    private void Send_Click(object sender, RoutedEventArgs e) => _ = SendAsync();

    private async Task SendAsync()
    {
        var text = Input.Text.Trim();
        if (text.Length == 0 || Send is null || !SendButton.IsEnabled) return;
        if (text.Length > ChatLimits.MaxLength) text = text[..ChatLimits.MaxLength];

        SendButton.IsEnabled = false;
        try
        {
            await Send(text);
            Input.Clear();
            SetNotice(null);
        }
        catch (Exception ex)
        {
            SetNotice(ex.Message);
        }
        finally
        {
            SendButton.IsEnabled = true;
            Input.Focus();
        }
    }
}
