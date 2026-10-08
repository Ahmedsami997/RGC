using System.Net.Http;
using System.Windows;
using System.Windows.Interop;
using Microsoft.Identity.Client;
using RGC.AdminConsole.Services;
using RGC.Branding.Auth;

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
        LocalExpander.IsExpanded = settings.LastSignInWasLocal;
        PasswordBox.KeyDown += (_, e) => { if (e.Key == System.Windows.Input.Key.Enter) SignIn_Click(this, new RoutedEventArgs()); };
        Loaded += (_, _) => MsButton.Focus();
    }

    private string? ReadServer()
    {
        var server = ServerBox.Text.Trim().TrimEnd('/');
        if (Uri.TryCreate(server, UriKind.Absolute, out var uri) && (uri.Scheme == "http" || uri.Scheme == "https"))
            return server;
        ShowError("Enter the server address, e.g. https://rgc-announcements.azurewebsites.net");
        return null;
    }

    private async void Microsoft365_Click(object sender, RoutedEventArgs e)
    {
        if (ReadServer() is not { } server) return;
        MsButton.IsEnabled = false;
        MsButtonText.Text = "Signing in...";
        ErrorText.Visibility = Visibility.Collapsed;

        var api = new ApiClient(server);
        try
        {
            var config = await api.GetAuthConfigAsync();
            if (config is not { EntraEnabled: true })
                throw new ApiException("Microsoft 365 sign-in isn't set up on this server yet. Use a local admin account.");

            var entra = new EntraSignIn(config, "admin");
            // Silent first (already signed in on this PC), otherwise the Microsoft sign-in window.
            if (await entra.TryGetTokenSilentlyAsync() is null)
                await entra.SignInInteractiveAsync(new WindowInteropHelper(this).Handle);

            await api.UseMicrosoft365Async(async () =>
                await entra.TryGetTokenSilentlyAsync()
                ?? await Dispatcher.Invoke(() => entra.SignInInteractiveAsync(new WindowInteropHelper(Application.Current.MainWindow ?? this).Handle)));

            _settings.ServerUrl = server;
            _settings.LastSignInWasLocal = false;
            _settings.Save();
            Api = api;
            DialogResult = true;
        }
        catch (MsalClientException ex) when (ex.ErrorCode == "authentication_canceled")
        {
            api.Dispose();
            ShowError("Sign-in was cancelled.");
        }
        catch (Exception ex) when (ex is ApiException or MsalException)
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
            MsButton.IsEnabled = true;
            MsButtonText.Text = "Sign in with Microsoft 365";
        }
    }

    private async void SignIn_Click(object sender, RoutedEventArgs e)
    {
        if (ReadServer() is not { } server) return;
        var user = UserBox.Text.Trim();
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
            _settings.LastSignInWasLocal = true;
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
