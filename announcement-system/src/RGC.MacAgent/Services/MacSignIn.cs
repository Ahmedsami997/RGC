using Microsoft.Identity.Client;
using Microsoft.Identity.Client.Extensions.Msal;
using RGC.Shared;

namespace RGC.MacAgent.Services;

/// <summary>
/// Microsoft 365 (Entra ID) sign-in on macOS. Signs in through the default browser and keeps the
/// session in the login Keychain, so the user signs in once and stays signed in across restarts.
/// </summary>
public sealed class MacSignIn
{
    private readonly IPublicClientApplication _app;
    private readonly string[] _scopes;
    private readonly Task _cacheReady;

    public MacSignIn(AuthConfigDto config)
    {
        if (!config.EntraEnabled || config.ClientId is null || config.TenantId is null || config.Scope is null)
            throw new InvalidOperationException("Microsoft 365 sign-in is not configured on the server.");

        _scopes = [config.Scope];
        // "http://localhost" is registered on the Entra app for desktop clients; MSAL picks a free port.
        _app = PublicClientApplicationBuilder.Create(config.ClientId)
            .WithAuthority(AzureCloudInstance.AzurePublic, config.TenantId)
            .WithRedirectUri("http://localhost")
            .Build();
        _cacheReady = AttachCacheAsync();
    }

    public string? Username { get; private set; }

    private async Task AttachCacheAsync()
    {
        try
        {
            var keychain = new StorageCreationPropertiesBuilder("msal.cache", MacPaths.DataDir)
                .WithMacKeyChain("com.royalgolfclub.rgc.agent", "RGC Announcements")
                .Build();
            var helper = await MsalCacheHelper.CreateAsync(keychain);
            helper.VerifyPersistence();
            helper.RegisterCache(_app.UserTokenCache);
        }
        catch (Exception ex)
        {
            // Keychain refused (unsigned app on some setups): fall back to a file only this user can read.
            AgentLog.Error("Keychain token cache unavailable; using a private file instead", ex);
            try
            {
                var file = new StorageCreationPropertiesBuilder("msal.cache", MacPaths.DataDir).WithUnprotectedFile().Build();
                var helper = await MsalCacheHelper.CreateAsync(file);
                helper.RegisterCache(_app.UserTokenCache);
                if (!OperatingSystem.IsWindows())
                    File.SetUnixFileMode(Path.Combine(MacPaths.DataDir, "msal.cache"), UnixFileMode.UserRead | UnixFileMode.UserWrite);
            }
            catch (Exception inner)
            {
                AgentLog.Error("No persistent token cache; the user will be asked to sign in after each restart", inner);
            }
        }
    }

    /// <summary>Gets a token without showing any UI; null when the user has to sign in.</summary>
    public async Task<string?> TryGetTokenSilentlyAsync(CancellationToken ct = default)
    {
        await _cacheReady;
        var account = (await _app.GetAccountsAsync()).FirstOrDefault();
        if (account is null) return null;
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

    /// <summary>Opens the Microsoft sign-in page in the default browser and waits for it to finish.</summary>
    public async Task<string> SignInInteractiveAsync(CancellationToken ct = default)
    {
        await _cacheReady;
        var result = await _app.AcquireTokenInteractive(_scopes)
            .WithPrompt(Prompt.SelectAccount)
            .WithUseEmbeddedWebView(false)
            .WithSystemWebViewOptions(new SystemWebViewOptions
            {
                HtmlMessageSuccess = "<html><body style='font-family:-apple-system;text-align:center;padding-top:80px'>" +
                                     "<h2>Signed in to RGC Announcements</h2><p>You can close this tab.</p></body></html>"
            })
            .ExecuteAsync(ct);
        Username = result.Account?.Username;
        return result.AccessToken;
    }
}
