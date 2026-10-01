using Microsoft.Identity.Client;
using Microsoft.Identity.Client.Broker;
using RGC.Shared;

namespace RGC.Branding.Auth;

/// <summary>
/// Microsoft 365 (Entra ID) sign-in for the RGC desktop apps. Uses the Windows account broker (WAM),
/// so a user who has signed in once – or whose PC is joined to Entra – gets tokens silently afterwards.
/// </summary>
public sealed class EntraSignIn
{
    private readonly IPublicClientApplication _app;
    private readonly string[] _scopes;

    public EntraSignIn(AuthConfigDto config)
    {
        if (!config.EntraEnabled || config.ClientId is null || config.TenantId is null || config.Scope is null)
            throw new InvalidOperationException("Microsoft 365 sign-in is not configured on the server.");

        _scopes = [config.Scope];
        _app = PublicClientApplicationBuilder.Create(config.ClientId)
            .WithAuthority(AzureCloudInstance.AzurePublic, config.TenantId)
            .WithDefaultRedirectUri()
            .WithBroker(new BrokerOptions(BrokerOptions.OperatingSystems.Windows) { Title = "RGC Announcements" })
            .Build();
    }

    /// <summary>The signed-in account's email, once known.</summary>
    public string? Username { get; private set; }

    /// <summary>Gets a token without showing any UI; null when the user has to sign in.</summary>
    public async Task<string?> TryGetTokenSilentlyAsync(CancellationToken ct = default)
    {
        var accounts = await _app.GetAccountsAsync();
        // No cached account yet: try the account Windows is signed in with (works on Entra/hybrid-joined PCs).
        var account = accounts.FirstOrDefault() ?? PublicClientApplication.OperatingSystemAccount;
        try
        {
            var result = await _app.AcquireTokenSilent(_scopes, account).ExecuteAsync(ct);
            Username = result.Account?.Username;
            return result.AccessToken;
        }
        catch (MsalUiRequiredException)
        {
            return null;
        }
    }

    /// <summary>Shows the Microsoft sign-in dialog, parented to <paramref name="windowHandle"/>.</summary>
    public async Task<string> SignInInteractiveAsync(IntPtr windowHandle, CancellationToken ct = default)
    {
        var result = await _app.AcquireTokenInteractive(_scopes)
            .WithParentActivityOrWindow(windowHandle)
            .WithPrompt(Prompt.SelectAccount)
            .ExecuteAsync(ct);
        Username = result.Account?.Username;
        return result.AccessToken;
    }

    public async Task SignOutAsync()
    {
        foreach (var account in await _app.GetAccountsAsync())
            await _app.RemoveAsync(account);
        Username = null;
    }
}
