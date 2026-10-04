using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace RGC.Server.Data;

public class RgcDbContext(DbContextOptions<RgcDbContext> options) : DbContext(options)
{
    public DbSet<AdminUser> AdminUsers => Set<AdminUser>();
    public DbSet<ClientComputer> Clients => Set<ClientComputer>();
    public DbSet<Announcement> Announcements => Set<Announcement>();
    public DbSet<AnnouncementRecipient> AnnouncementRecipients => Set<AnnouncementRecipient>();
    public DbSet<ClientActivity> ClientActivity => Set<ClientActivity>();

    protected override void OnModelCreating(ModelBuilder b)
    {
        b.Entity<AdminUser>(e =>
        {
            e.ToTable("AdminUsers");
            e.HasIndex(x => x.Username).IsUnique();
            e.Property(x => x.Username).HasMaxLength(100).IsRequired();
            e.Property(x => x.DisplayName).HasMaxLength(200);
            e.Property(x => x.PasswordHash).HasMaxLength(500).IsRequired();
        });

        b.Entity<ClientComputer>(e =>
        {
            e.ToTable("Clients");
            e.Property(x => x.MachineName).HasMaxLength(100).IsRequired();
            e.Property(x => x.UserName).HasMaxLength(200);
            e.Property(x => x.UserDisplayName).HasMaxLength(200);
            e.Property(x => x.WindowsUser).HasMaxLength(200);
            e.Property(x => x.IpAddress).HasMaxLength(100);
            e.Property(x => x.PublicIp).HasMaxLength(100);
            e.Property(x => x.OsVersion).HasMaxLength(200);
            e.Property(x => x.AgentVersion).HasMaxLength(50);
            e.HasIndex(x => x.MachineName);
        });

        b.Entity<Announcement>(e =>
        {
            e.ToTable("Announcements");
            e.Property(x => x.Title).HasMaxLength(200).IsRequired();
            e.Property(x => x.Message).IsRequired();
            e.Property(x => x.CreatedBy).HasMaxLength(100);
            e.Property(x => x.Priority).HasConversion<int>();
            e.HasIndex(x => x.CreatedAtUtc);
        });

        b.Entity<AnnouncementRecipient>(e =>
        {
            e.ToTable("AnnouncementRecipients");
            e.HasKey(x => new { x.AnnouncementId, x.ClientId });
            e.Property(x => x.AcknowledgedBy).HasMaxLength(200);
            e.HasOne(x => x.Announcement).WithMany(a => a.Recipients)
                .HasForeignKey(x => x.AnnouncementId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne(x => x.Client).WithMany()
                .HasForeignKey(x => x.ClientId).OnDelete(DeleteBehavior.Cascade);
            e.HasIndex(x => new { x.ClientId, x.AcknowledgedAtUtc });
        });

        b.Entity<ClientActivity>(e =>
        {
            e.ToTable("ClientActivity");
            e.HasKey(x => new { x.Day, x.ClientId });
            e.Property(x => x.Day).HasColumnType("date");
        });

        // Everything is stored as UTC; make sure values read back are flagged as UTC
        // so clients convert them to local time correctly.
        var utc = new ValueConverter<DateTime, DateTime>(
            v => v.ToUniversalTime(), v => DateTime.SpecifyKind(v, DateTimeKind.Utc));
        var utcNullable = new ValueConverter<DateTime?, DateTime?>(
            v => v.HasValue ? v.Value.ToUniversalTime() : v,
            v => v.HasValue ? DateTime.SpecifyKind(v.Value, DateTimeKind.Utc) : v);

        foreach (var entity in b.Model.GetEntityTypes())
        foreach (var prop in entity.GetProperties())
        {
            if (prop.ClrType == typeof(DateTime)) prop.SetValueConverter(utc);
            else if (prop.ClrType == typeof(DateTime?)) prop.SetValueConverter(utcNullable);
        }
    }
}
