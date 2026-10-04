using Microsoft.EntityFrameworkCore;
using RGC.Server.Data;
using RGC.Server.Security;

namespace RGC.Server;

internal static class Startup
{
    public static async Task InitializeDatabaseAsync(IServiceProvider services, IConfiguration config, ILogger log)
    {
        using var scope = services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<RgcDbContext>();

        // Creates the database and tables on first run (see database/CreateDatabase.sql for the equivalent script).
        await db.Database.EnsureCreatedAsync();
        // EnsureCreated doesn't change an existing database; add what later versions need.
        if (db.Database.IsSqlServer()) await UpgradeSchemaAsync(db);

        // Nobody is connected right after a (re)start; agents re-register as they reconnect.
        await db.Clients.Where(c => c.IsOnline).ExecuteUpdateAsync(s => s.SetProperty(c => c.IsOnline, false));

        if (!await db.AdminUsers.AnyAsync())
        {
            var username = config["BootstrapAdmin:Username"];
            var password = config["BootstrapAdmin:Password"];
            if (string.IsNullOrWhiteSpace(username) || string.IsNullOrWhiteSpace(password) || password.Length < 8)
            {
                log.LogWarning("No admin users exist. Set BootstrapAdmin:Username/Password (8+ chars) or run 'RGC.Server add-admin <user> <password>'.");
                return;
            }
            db.AdminUsers.Add(new AdminUser
            {
                Username = username.Trim(),
                DisplayName = config["BootstrapAdmin:DisplayName"] ?? username.Trim(),
                PasswordHash = PasswordHasher.Hash(password),
                CreatedAtUtc = DateTime.UtcNow
            });
            await db.SaveChangesAsync();
            log.LogWarning("Created bootstrap admin '{User}'. Remove BootstrapAdmin:Password from configuration now.", username);
        }
    }

    /// <summary>Idempotent: safe to run on every start, on new and existing databases.</summary>
    private static Task UpgradeSchemaAsync(RgcDbContext db) => db.Database.ExecuteSqlRawAsync("""
        IF COL_LENGTH('Clients', 'UserDisplayName') IS NULL ALTER TABLE Clients ADD UserDisplayName nvarchar(200) NULL;
        IF COL_LENGTH('Clients', 'WindowsUser') IS NULL ALTER TABLE Clients ADD WindowsUser nvarchar(200) NULL;
        IF COL_LENGTH('Clients', 'PublicIp') IS NULL ALTER TABLE Clients ADD PublicIp nvarchar(100) NULL;
        IF COL_LENGTH('Announcements', 'LastResentAtUtc') IS NULL ALTER TABLE Announcements ADD LastResentAtUtc datetime2 NULL;
        IF OBJECT_ID('ClientActivity') IS NULL
            CREATE TABLE ClientActivity (
                Day date NOT NULL,
                ClientId uniqueidentifier NOT NULL,
                CONSTRAINT PK_ClientActivity PRIMARY KEY (Day, ClientId));
        """);
}

internal static class AdminCli
{
    public static async Task RunAsync(IServiceProvider services, string[] args)
    {
        if (args.Length < 3)
        {
            Console.Error.WriteLine("Usage: RGC.Server add-admin <username> <password> [display name]");
            Console.Error.WriteLine("       RGC.Server reset-password <username> <password>");
            Environment.ExitCode = 1;
            return;
        }
        if (args[2].Length < 8)
        {
            Console.Error.WriteLine("Password must be at least 8 characters.");
            Environment.ExitCode = 1;
            return;
        }

        using var scope = services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<RgcDbContext>();
        await db.Database.EnsureCreatedAsync();

        var username = args[1].Trim();
        var user = await db.AdminUsers.FirstOrDefaultAsync(u => u.Username == username);

        if (args[0] == "add-admin")
        {
            if (user is not null) { Console.Error.WriteLine($"Admin '{username}' already exists."); Environment.ExitCode = 1; return; }
            db.AdminUsers.Add(new AdminUser
            {
                Username = username,
                DisplayName = args.Length > 3 ? string.Join(' ', args[3..]) : username,
                PasswordHash = PasswordHasher.Hash(args[2]),
                CreatedAtUtc = DateTime.UtcNow
            });
            Console.WriteLine($"Admin '{username}' created.");
        }
        else
        {
            if (user is null) { Console.Error.WriteLine($"Admin '{username}' not found."); Environment.ExitCode = 1; return; }
            user.PasswordHash = PasswordHasher.Hash(args[2]);
            user.FailedLoginCount = 0;
            user.LockoutEndUtc = null;
            Console.WriteLine($"Password for '{username}' reset.");
        }
        await db.SaveChangesAsync();
    }
}
