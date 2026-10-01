using System.Windows;
using System.Windows.Interop;
using RGC.ClientAgent.Services;

namespace RGC.ClientAgent.Views;

/// <summary>Asks the user to sign in with Microsoft 365 the first time the agent runs on a PC.</summary>
public partial class SignInWindow : Window
{
    private readonly AgentConnection _connection;

    public SignInWindow(AgentConnection connection)
    {
        InitializeComponent();
        _connection = connection;
    }

    private async void SignIn_Click(object sender, RoutedEventArgs e)
    {
        SignInButton.IsEnabled = false;
        SignInButton.Content = "Signing in...";
        ErrorText.Visibility = Visibility.Collapsed;
        try
        {
            await _connection.SignInAsync(new WindowInteropHelper(this).Handle);
            Close();
        }
        catch (Exception ex)
        {
            AgentLog.Error("Microsoft 365 sign-in failed", ex);
            ErrorText.Text = ex is Microsoft.Identity.Client.MsalClientException { ErrorCode: "authentication_canceled" }
                ? "Sign-in was cancelled. Please try again."
                : $"Sign-in failed: {ex.Message}";
            ErrorText.Visibility = Visibility.Visible;
            SignInButton.IsEnabled = true;
            SignInButton.Content = "Sign in with Microsoft 365";
        }
    }

    private void Later_Click(object sender, RoutedEventArgs e) => Close();
}
