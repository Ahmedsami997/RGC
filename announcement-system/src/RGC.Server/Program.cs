using System.Security.Claims;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.EntityFrameworkCore;
using RGC.Server;
using RGC.Server.Data;
using RGC.Server.Hubs;
using RGC.Server.Security;
using RGC.Server.Services;
using RGC.Shared;

var builder = WebApplication.CreateBuilder(args);

// Allows the server to run as a Windows Service (sc.exe create ...). No-op when run from a console.
builder.Host.UseWindowsService(o => o.ServiceName = "RGC Announcement Server");

builder.Services.Configure<JwtOptions>(builder.Configuration.GetSection("Jwt"));
builder.Services.Configure<AnnouncementOptions>(builder.Configuration.GetSection("Announcements"));
builder.Services.Configure<EntraOptions>(builder.Configuration.GetSection("Entra"));
builder.Services.Configure<AuthModeOptions>(builder.Configuration.GetSection("Authentication"));

var jwt = builder.Configuration.GetSection("Jwt").Get<JwtOptions>() ?? new JwtOptions();
var entra = builder.Configuration.GetSection("Entra").Get<EntraOptions>() ?? new EntraOptions();
var authMode = builder.Configuration.GetSection("Authentication").Get<AuthModeOptions>() ?? new AuthModeOptions();

// Configuration problems don't crash the server: it starts anyway and reports them on /api/health,
// so a wrong setting on a hosted server (e.g. Azure App Service) can be seen in a browser.
var problems = new List<string>();
if (!entra.Enabled && (!authMode.AllowLocalAdminLogin || !authMode.AllowAgentKey))
    problems.Add("Entra:TenantId and Entra:ClientId must be set (valid GUIDs) when local admin login or the agent key is turned off.");
// The signing key also protects local admin tokens, so it is always required.
if (jwt.SigningKey.Length < 32 || jwt.SigningKey.StartsWith("CHANGE-ME", StringComparison.Ordinal))
{
    problems.Add("Jwt:SigningKey is missing or shorter than 32 characters. Local admin sign-in is disabled until it is set.");
    // Use a throw-away key so the server still runs; local tokens won't survive a restart.
    jwt.SigningKey = Convert.ToBase64String(System.Security.Cryptography.RandomNumberGenerator.GetBytes(48));
    authMode.AllowLocalAdminLogin = false;
}
var agentKey = builder.Configuration["Announcements:AgentKey"] ?? "";
if (authMode.AllowAgentKey && (agentKey.Length < 16 || agentKey.StartsWith("CHANGE-ME", StringComparison.Ordinal)))
{
    problems.Add("Announcements:AgentKey is missing or shorter than 16 characters. Agents can only connect with Microsoft 365 sign-in (or set Authentication:AllowAgentKey to false).");
    authMode.AllowAgentKey = false;
}
if (string.IsNullOrWhiteSpace(builder.Configuration.GetConnectionString("RgcDatabase")))
    problems.Add("ConnectionStrings:RgcDatabase is not set.");
// Keep the options seen by hubs/endpoints in line with what was decided above.
builder.Services.PostConfigure<AuthModeOptions>(o =>
{
    o.AllowAgentKey = authMode.AllowAgentKey;
    o.AllowLocalAdminLogin = authMode.AllowLocalAdminLogin;
});
builder.Services.PostConfigure<JwtOptions>(o => o.SigningKey = jwt.SigningKey);

// Listen on port 5080 unless the host (IIS, Azure App Service, ASPNETCORE_URLS, Kestrel config) says otherwise.
if (string.IsNullOrEmpty(builder.Configuration["urls"]) &&
    string.IsNullOrEmpty(builder.Configuration["HTTP_PORTS"]) &&
    string.IsNullOrEmpty(builder.Configuration["HTTPS_PORTS"]) &&
    !builder.Configuration.GetSection("Kestrel:Endpoints").Exists() &&
    Environment.GetEnvironmentVariable("WEBSITE_SITE_NAME") is null)
{
    builder.WebHost.UseUrls("http://0.0.0.0:5080");
}

// Behind Azure App Service / a reverse proxy: trust X-Forwarded-For/Proto so client IPs and HTTPS are seen correctly.
builder.Services.Configure<ForwardedHeadersOptions>(o =>
{
    o.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
    o.KnownNetworks.Clear();
    o.KnownProxies.Clear();
});

builder.Services.AddDbContext<RgcDbContext>(o =>
    o.UseSqlServer(builder.Configuration.GetConnectionString("RgcDatabase"),
        sql => sql.EnableRetryOnFailure()));

builder.Services.AddRgcAuthentication(jwt, entra);
builder.Services.AddAuthorization(o =>
{
    // Local admins carry role "Admin"; Microsoft 365 admins carry the app role configured in Entra:AdminRole.
    o.AddPolicy("Admin", p => p.RequireAuthenticatedUser().RequireAssertion(ctx =>
        ctx.User.IsInRole("Admin") || ctx.User.IsInRole(entra.AdminRole)));
});

builder.Services.AddRateLimiter(o =>
{
    o.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    o.AddPolicy("login", ctx => RateLimitPartition.GetFixedWindowLimiter(
        ctx.Connection.RemoteIpAddress?.ToString() ?? "unknown",
        _ => new FixedWindowRateLimiterOptions { PermitLimit = 10, Window = TimeSpan.FromMinutes(1) }));
});

builder.Services.AddSignalR(o =>
{
    o.KeepAliveInterval = TimeSpan.FromSeconds(15);
    o.ClientTimeoutInterval = TimeSpan.FromSeconds(45);
    o.EnableDetailedErrors = builder.Environment.IsDevelopment();
});

builder.Services.AddSingleton<ConnectionRegistry>();
builder.Services.AddSingleton<TokenService>();
builder.Services.AddScoped<ClientDirectory>();
builder.Services.AddScoped<AnnouncementService>();
builder.Services.AddScoped<ReportService>();
builder.Services.AddHostedService<ActivityTracker>();

var app = builder.Build();

// --- Command-line admin management -------------------------------------------------
//   RGC.Server.exe add-admin <username> <password> [display name]
//   RGC.Server.exe reset-password <username> <password>
if (args.Length > 0 && args[0] is "add-admin" or "reset-password")
{
    await AdminCli.RunAsync(app.Services, args);
    return;
}

foreach (var problem in problems) app.Logger.LogError("Configuration problem: {Problem}", problem);
// Azure SQL can refuse the first connections while it wakes up (serverless auto-pause) or
// during maintenance, so retry a few times, then keep retrying in the background. /api/health
// reports the error until it succeeds; a wrong password or firewall rule never will.
async Task<bool> InitializeDatabaseAsync()
{
    try
    {
        await Startup.InitializeDatabaseAsync(app.Services, app.Configuration, app.Logger);
        lock (problems) problems.RemoveAll(p => p.StartsWith("Database:", StringComparison.Ordinal));
        return true;
    }
    catch (Exception ex)
    {
        app.Logger.LogError(ex, "Database initialisation failed");
        lock (problems)
        {
            problems.RemoveAll(p => p.StartsWith("Database:", StringComparison.Ordinal));
            problems.Add($"Database: {ex.GetBaseException().Message}");
        }
        return false;
    }
}

var databaseReady = await InitializeDatabaseAsync();
for (var attempt = 1; attempt <= 3 && !databaseReady; attempt++)
{
    await Task.Delay(TimeSpan.FromSeconds(10 * attempt));
    databaseReady = await InitializeDatabaseAsync();
}
if (!databaseReady)
{
    _ = Task.Run(async () =>
    {
        while (!app.Lifetime.ApplicationStopping.IsCancellationRequested && !await InitializeDatabaseAsync())
            await Task.Delay(TimeSpan.FromMinutes(1), app.Lifetime.ApplicationStopping).ContinueWith(_ => { });
    });
}

app.UseForwardedHeaders();
app.UseRateLimiter();
app.UseAuthentication();
app.UseAuthorization();

app.MapGet("/api/health", () =>
{
    string[] current;
    lock (problems) current = problems.ToArray();
    return Results.Ok(new
    {
        status = current.Length == 0 ? "ok" : "error",
        problems = current,
        utc = DateTime.UtcNow,
        version = typeof(Program).Assembly.GetName().Version?.ToString(),
        microsoft365 = entra.Enabled
    });
});

app.MapGet("/" + AuthRoutes.Config, () => Results.Ok(new AuthConfigDto(
    entra.Enabled,
    entra.Enabled ? entra.TenantId : null,
    entra.Enabled ? entra.ClientId : null,
    entra.Enabled ? entra.Scope : null,
    authMode.AllowLocalAdminLogin,
    authMode.AllowAgentKey)));

app.MapGet("/" + AuthRoutes.Me, (ClaimsPrincipal user) => Results.Ok(new MeDto(
    AuthSetup.GetDisplayName(user),
    AuthSetup.GetEmail(user),
    user.IsInRole("Admin") || user.IsInRole(entra.AdminRole)))).RequireAuthorization();

app.MapPost("/" + AuthRoutes.Login, async (LoginRequest req, RgcDbContext db, TokenService tokens, ILogger<Program> log, HttpContext http) =>
{
    if (!authMode.AllowLocalAdminLogin)
        return Results.Problem("Local sign-in is turned off. Use 'Sign in with Microsoft 365'.", statusCode: StatusCodes.Status403Forbidden);

    const int maxFailures = 5;
    var lockout = TimeSpan.FromMinutes(15);
    var invalid = Results.Problem("Invalid username or password.", statusCode: StatusCodes.Status401Unauthorized);

    if (string.IsNullOrWhiteSpace(req.Username) || string.IsNullOrEmpty(req.Password)) return invalid;

    var user = await db.AdminUsers.FirstOrDefaultAsync(u => u.Username == req.Username.Trim());
    if (user is null || !user.IsActive)
    {
        // Burn comparable CPU so response time doesn't reveal whether the user exists.
        PasswordHasher.Verify(req.Password, PasswordHasher.Hash("dummy-password"));
        log.LogWarning("Failed login for unknown/inactive user {User} from {Ip}", req.Username, http.Connection.RemoteIpAddress);
        return invalid;
    }

    if (user.LockoutEndUtc > DateTime.UtcNow)
        return Results.Problem("Account temporarily locked after too many failed attempts. Try again later.",
            statusCode: StatusCodes.Status423Locked);

    if (!PasswordHasher.Verify(req.Password, user.PasswordHash))
    {
        user.FailedLoginCount++;
        if (user.FailedLoginCount >= maxFailures)
        {
            user.LockoutEndUtc = DateTime.UtcNow.Add(lockout);
            user.FailedLoginCount = 0;
        }
        await db.SaveChangesAsync();
        log.LogWarning("Failed login for {User} from {Ip}", user.Username, http.Connection.RemoteIpAddress);
        return invalid;
    }

    user.FailedLoginCount = 0;
    user.LockoutEndUtc = null;
    user.LastLoginUtc = DateTime.UtcNow;
    await db.SaveChangesAsync();

    var (token, expires) = tokens.Create(user);
    log.LogInformation("Admin {User} logged in from {Ip}", user.Username, http.Connection.RemoteIpAddress);
    return Results.Ok(new LoginResponse(token, expires, user.Username, user.DisplayName));
}).RequireRateLimiting("login");

var api = app.MapGroup("/api").RequireAuthorization("Admin");

api.MapGet("/clients", (ReportService reports, CancellationToken ct) => reports.GetClientsAsync(ct));

api.MapGet("/clients/{id:guid}/records", (Guid id, ReportService reports, CancellationToken ct) =>
    reports.GetClientRecordsAsync(id, ct));

// tz = the admin's UTC offset in minutes, so per-day charts follow their calendar.
api.MapGet("/stats", (ReportService reports, int? days, int? tz, CancellationToken ct) =>
    reports.GetStatsAsync(days ?? 30, tz ?? 0, ct));

api.MapGet("/people", (ReportService reports, CancellationToken ct) => reports.GetPeopleAsync(ct));

api.MapGet("/people/records", (string user, ReportService reports, CancellationToken ct) =>
    reports.GetPersonRecordsAsync(user, ct));

api.MapGet("/announcements", (AnnouncementService svc, int? take, CancellationToken ct) =>
    svc.GetHistoryAsync(Math.Clamp(take ?? 500, 1, 5000), ct));

api.MapGet("/announcements/{id:guid}/recipients", (Guid id, AnnouncementService svc, CancellationToken ct) =>
    svc.GetRecipientsAsync(id, ct));

api.MapPost("/announcements", async (SendAnnouncementRequest req, AnnouncementService svc, ClaimsPrincipal user, CancellationToken ct) =>
{
    var error = svc.Validate(req);
    if (error is not null) return Results.ValidationProblem(new Dictionary<string, string[]> { ["announcement"] = [error] });
    var result = await svc.BroadcastAsync(req, AuthSetup.GetActorName(user), ct);
    return Results.Ok(result);
});

api.MapPost("/announcements/{id:guid}/resend", async (Guid id, AnnouncementService svc, ClaimsPrincipal user, CancellationToken ct) =>
    await svc.ResendToUnreadAsync(id, AuthSetup.GetActorName(user), ct) is { } result ? Results.Ok(result) : Results.NotFound());

app.MapHub<AgentHub>(HubRoutes.AgentHub);
app.MapHub<AdminHub>(HubRoutes.AdminHub);

app.Run();
