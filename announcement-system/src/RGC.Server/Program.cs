using System.Security.Claims;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
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

var jwt = builder.Configuration.GetSection("Jwt").Get<JwtOptions>() ?? new JwtOptions();
if (jwt.SigningKey.Length < 32 || jwt.SigningKey.StartsWith("CHANGE-ME", StringComparison.Ordinal))
    throw new InvalidOperationException("Jwt:SigningKey must be set to a random secret of at least 32 characters.");
var agentKey = builder.Configuration["Announcements:AgentKey"] ?? "";
if (agentKey.Length < 16 || agentKey.StartsWith("CHANGE-ME", StringComparison.Ordinal))
    throw new InvalidOperationException("Announcements:AgentKey must be set to a random secret of at least 16 characters.");

builder.Services.AddDbContext<RgcDbContext>(o =>
    o.UseSqlServer(builder.Configuration.GetConnectionString("RgcDatabase"),
        sql => sql.EnableRetryOnFailure()));

builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(o =>
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
        // SignalR WebSockets cannot send headers from the browser stack; accept the token from the query string for hubs.
        o.Events = new JwtBearerEvents
        {
            OnMessageReceived = ctx =>
            {
                var token = ctx.Request.Query["access_token"];
                if (!string.IsNullOrEmpty(token) && ctx.HttpContext.Request.Path.StartsWithSegments(HubRoutes.AdminHub))
                    ctx.Token = token;
                return Task.CompletedTask;
            }
        };
    });
builder.Services.AddAuthorization();

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

var app = builder.Build();

// --- Command-line admin management -------------------------------------------------
//   RGC.Server.exe add-admin <username> <password> [display name]
//   RGC.Server.exe reset-password <username> <password>
if (args.Length > 0 && args[0] is "add-admin" or "reset-password")
{
    await AdminCli.RunAsync(app.Services, args);
    return;
}

await Startup.InitializeDatabaseAsync(app.Services, app.Configuration, app.Logger);

app.UseRateLimiter();
app.UseAuthentication();
app.UseAuthorization();

app.MapGet("/api/health", () => Results.Ok(new { status = "ok", utc = DateTime.UtcNow }));

app.MapPost("/api/auth/login", async (LoginRequest req, RgcDbContext db, TokenService tokens, ILogger<Program> log, HttpContext http) =>
{
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

var api = app.MapGroup("/api").RequireAuthorization(p => p.RequireRole("Admin"));

api.MapGet("/clients", (ClientDirectory dir, CancellationToken ct) => dir.GetAllAsync(ct));

api.MapGet("/announcements", (AnnouncementService svc, int? take, CancellationToken ct) =>
    svc.GetHistoryAsync(Math.Clamp(take ?? 500, 1, 5000), ct));

api.MapGet("/announcements/{id:guid}/recipients", (Guid id, AnnouncementService svc, CancellationToken ct) =>
    svc.GetRecipientsAsync(id, ct));

api.MapPost("/announcements", async (SendAnnouncementRequest req, AnnouncementService svc, ClaimsPrincipal user, CancellationToken ct) =>
{
    var error = svc.Validate(req);
    if (error is not null) return Results.ValidationProblem(new Dictionary<string, string[]> { ["announcement"] = [error] });
    var result = await svc.BroadcastAsync(req, user.Identity?.Name ?? "admin", ct);
    return Results.Ok(result);
});

app.MapHub<AgentHub>(HubRoutes.AgentHub);
app.MapHub<AdminHub>(HubRoutes.AdminHub);

app.Run();
