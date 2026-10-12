using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;
using RGC.Shared;

namespace RGC.Server.Security;

/// <summary>Microsoft 365 / Entra ID sign-in settings ("Entra" section).</summary>
public sealed class EntraOptions
{
    public string TenantId { get; set; } = "";
    /// <summary>Application (client) ID of the "RGC Announcements" app registration.</summary>
    public string ClientId { get; set; } = "";
    /// <summary>App role value that grants access to the Admin Console.</summary>
    public string AdminRole { get; set; } = "Admin";

    public bool Enabled => Guid.TryParse(TenantId, out _) && Guid.TryParse(ClientId, out _);
    public string Scope => $"api://{ClientId}/access_as_user";
}

/// <summary>Which sign-in methods the server accepts ("Authentication" section).</summary>
public sealed class AuthModeOptions
{
    /// <summary>Username/password admins stored in the database.</summary>
    public bool AllowLocalAdminLogin { get; set; } = true;
    /// <summary>Agents may connect with the shared agent key instead of a Microsoft 365 sign-in.</summary>
    public bool AllowAgentKey { get; set; } = true;
}

public static class AuthSetup
{
    public const string LocalScheme = "Local";
    public const string EntraScheme = "Entra";
    private const string SelectorScheme = "Bearer";

    public static void AddRgcAuthentication(this IServiceCollection services, JwtOptions jwt, EntraOptions entra)
    {
        var auth = services.AddAuthentication(SelectorScheme)
            // Picks the right validator by looking at who issued the token.
            .AddPolicyScheme(SelectorScheme, SelectorScheme, o => o.ForwardDefaultSelector = ctx =>
            {
                var token = ReadToken(ctx.Request);
                if (entra.Enabled && token is not null)
                {
                    try
                    {
                        var iss = new JwtSecurityTokenHandler().ReadJwtToken(token).Issuer;
                        if (iss.Contains("login.microsoftonline.com", StringComparison.OrdinalIgnoreCase) ||
                            iss.Contains("sts.windows.net", StringComparison.OrdinalIgnoreCase))
                            return EntraScheme;
                    }
                    catch { /* not a JWT – let the local scheme reject it */ }
                }
                return LocalScheme;
            })
            .AddJwtBearer(LocalScheme, o =>
            {
                o.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidateIssuer = true,
                    ValidIssuer = jwt.Issuer,
                    ValidateAudience = true,
                    ValidAudience = jwt.Audience,
                    ValidateIssuerSigningKey = true,
                    IssuerSigningKey = jwt.GetKey(),
                    ValidateLifetime = true,
                    ClockSkew = TimeSpan.FromMinutes(1),
                    NameClaimType = ClaimTypes.Name,
                    RoleClaimType = ClaimTypes.Role
                };
                o.Events = HubTokenEvents();
            });

        if (entra.Enabled)
        {
            auth.AddJwtBearer(EntraScheme, o =>
            {
                o.Authority = $"https://login.microsoftonline.com/{entra.TenantId}/v2.0";
                o.MapInboundClaims = false;
                o.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidateIssuer = true,
                    // v1 and v2 access tokens use different issuers.
                    ValidIssuers =
                    [
                        $"https://login.microsoftonline.com/{entra.TenantId}/v2.0",
                        $"https://sts.windows.net/{entra.TenantId}/"
                    ],
                    ValidateAudience = true,
                    ValidAudiences = [entra.ClientId, $"api://{entra.ClientId}"],
                    ValidateLifetime = true,
                    ClockSkew = TimeSpan.FromMinutes(2),
                    NameClaimType = "name",
                    RoleClaimType = "roles"
                };
                o.Events = HubTokenEvents();
            });
        }
    }

    /// <summary>Email/UPN of a Microsoft 365 user, or the local admin name.</summary>
    public static string? GetEmail(ClaimsPrincipal? user) =>
        user?.FindFirst("preferred_username")?.Value
        ?? user?.FindFirst("upn")?.Value
        ?? user?.FindFirst(ClaimTypes.Upn)?.Value
        ?? user?.FindFirst("email")?.Value;

    public static string GetDisplayName(ClaimsPrincipal? user) =>
        user?.FindFirst("name")?.Value
        ?? user?.FindFirst("display_name")?.Value
        ?? user?.Identity?.Name
        ?? GetEmail(user)
        ?? "admin";

    /// <summary>What to store as "sent by": the 365 email when available, else the local admin username.</summary>
    public static string GetActorName(ClaimsPrincipal? user) =>
        GetEmail(user) ?? user?.Identity?.Name ?? "admin";

    /// <summary>True when the caller signed in with Microsoft 365 (Entra tokens carry a tenant id).</summary>
    public static bool IsEntraUser(ClaimsPrincipal? user) =>
        user?.Identity?.IsAuthenticated == true && user.FindFirst("tid") is not null;

    private static JwtBearerEvents HubTokenEvents() => new()
    {
        // SignalR may send the token in the query string (e.g. for WebSockets); accept it on hub routes only.
        OnMessageReceived = ctx =>
        {
            var token = ctx.Request.Query["access_token"];
            if (!string.IsNullOrEmpty(token) && ctx.HttpContext.Request.Path.StartsWithSegments("/hubs"))
                ctx.Token = token;
            return Task.CompletedTask;
        }
    };

    private static string? ReadToken(HttpRequest request)
    {
        var header = request.Headers.Authorization.ToString();
        if (header.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase)) return header["Bearer ".Length..].Trim();
        if (request.Path.StartsWithSegments("/hubs") && request.Query.TryGetValue("access_token", out var q)) return q.ToString();
        return null;
    }
}
