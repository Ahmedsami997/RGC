using System.Net.Http;
using System.Windows;
using RGC.AdminConsole.Services;

namespace RGC.AdminConsole.Views;

public partial class LoginWindow : Window
{
    private readonly AdminSettings _settings;

    public ApiClient? Api { get; private set; }

    public LoginWindow(AdminSettings settings)
    {
        InitializeComponent();
        _settings = settings;
        ServerBox.Text = settings.ServerUrl;
        UserBox.Text = settings.LastUsername ?? "";
        Loaded += (_, _) =>
        {
            if (string.IsNullOrEmpty(UserBox.Text)) UserBox.Focus();
            else PasswordBox.Focus();
        };
    }

    private async void SignIn_Click(object sender, RoutedEventArgs e)
    {
        var server = ServerBox.Text.Trim().TrimEnd('/');
        var user = UserBox.Text.Trim();
        if (!Uri.TryCreate(server, UriKind.Absolute, out var uri) || (uri.Scheme != "http" && uri.Scheme != "https"))
        {
            ShowError("Enter the server address, e.g. https://rgc-server:5443");
            return;
        }
        if (user.Length == 0 || PasswordBox.Password.Length == 0)
        {
            ShowError("Enter your username and password.");
            return;
        }

        SignInButton.IsEnabled = false;
        SignInButton.Content = "Signing in...";
        ErrorText.Visibility = Visibility.Collapsed;

        var api = new ApiClient(server);
        try
        {
            await api.LoginAsync(user, PasswordBox.Password);
            _settings.ServerUrl = server;
            _settings.LastUsername = user;
            _settings.Save();
            Api = api;
            DialogResult = true;
        }
        catch (ApiException ex)
        {
            api.Dispose();
            ShowError(ex.Message);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            api.Dispose();
            ShowError($"Cannot reach the RGC server at {server}. {ex.Message}");
        }
        finally
        {
            PasswordBox.Clear();
            SignInButton.IsEnabled = true;
            SignInButton.Content = "Sign in";
        }
    }

    private void ShowError(string message)
    {
        ErrorText.Text = message;
        ErrorText.Visibility = Visibility.Visible;
    }
}
