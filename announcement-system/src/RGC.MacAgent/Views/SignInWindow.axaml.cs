using Avalonia.Controls;
using RGC.MacAgent.Services;

namespace RGC.MacAgent.Views;

/// <summary>Asks the user to sign in once with their Microsoft 365 work account.</summary>
public partial class SignInWindow : Window
{
    private readonly AgentConnection _connection = null!;

    // For the XAML previewer.
    public SignInWindow() => InitializeComponent();

    public SignInWindow(AgentConnection connection)
    {
        InitializeComponent();
        _connection = connection;
        var signIn = SignInButton;
        var error = ErrorText;

        signIn.Click += async (_, _) =>
        {
            signIn.IsEnabled = false;
            signIn.Content = "Waiting for the browser…";
            error.IsVisible = false;
            try
            {
                await _connection.SignInAsync();
                Close();
            }
            catch (Exception ex)
            {
                AgentLog.Error("Microsoft 365 sign-in failed", ex);
                error.Text = $"Sign-in did not complete: {ex.Message}";
                error.IsVisible = true;
                signIn.IsEnabled = true;
                signIn.Content = "Sign in with Microsoft 365";
            }
        };
        LaterButton.Click += (_, _) => Close();
    }
}
