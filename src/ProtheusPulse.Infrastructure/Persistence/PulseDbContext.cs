using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using ProtheusPulse.Domain.Monitoring;

namespace ProtheusPulse.Infrastructure.Persistence;

public sealed class PulseDbContext(DbContextOptions<PulseDbContext> options) : DbContext(options)
{
    public DbSet<Installation> Installations => Set<Installation>();
    public DbSet<Component> Components => Set<Component>();
    public DbSet<WindowsServiceTarget> WindowsServiceTargets => Set<WindowsServiceTarget>();
    public DbSet<ProcessTarget> ProcessTargets => Set<ProcessTarget>();
    public DbSet<FileTarget> FileTargets => Set<FileTarget>();
    public DbSet<TcpCheck> TcpChecks => Set<TcpCheck>();
    public DbSet<HttpCheck> HttpChecks => Set<HttpCheck>();
    public DbSet<LogSource> LogSources => Set<LogSource>();
    public DbSet<HeartbeatDefinition> HeartbeatDefinitions => Set<HeartbeatDefinition>();
    public DbSet<ProbeResult> ProbeResults => Set<ProbeResult>();
    public DbSet<MetricSample> MetricSamples => Set<MetricSample>();
    public DbSet<AlertRule> AlertRules => Set<AlertRule>();
    public DbSet<AlertOccurrence> AlertOccurrences => Set<AlertOccurrence>();
    public DbSet<NotificationChannel> NotificationChannels => Set<NotificationChannel>();
    public DbSet<MaintenanceWindow> MaintenanceWindows => Set<MaintenanceWindow>();
    public DbSet<User> Users => Set<User>();
    public DbSet<AuditEvent> AuditEvents => Set<AuditEvent>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        var dateTimeOffsetConverter = new ValueConverter<DateTimeOffset, long>(
            value => value.UtcTicks,
            value => new DateTimeOffset(value, TimeSpan.Zero));
        var nullableDateTimeOffsetConverter = new ValueConverter<DateTimeOffset?, long?>(
            value => value.HasValue ? value.Value.UtcTicks : null,
            value => value.HasValue ? new DateTimeOffset(value.Value, TimeSpan.Zero) : null);

        foreach (var entityType in modelBuilder.Model.GetEntityTypes())
        {
            foreach (var property in entityType.GetProperties())
            {
                if (property.ClrType == typeof(DateTimeOffset))
                {
                    property.SetValueConverter(dateTimeOffsetConverter);
                }
                else if (property.ClrType == typeof(DateTimeOffset?))
                {
                    property.SetValueConverter(nullableDateTimeOffsetConverter);
                }
            }
        }

        modelBuilder.Entity<Installation>(entity =>
        {
            entity.Property(item => item.Name).HasMaxLength(160);
            entity.Property(item => item.CustomEnvironmentName).HasMaxLength(80);
            entity.HasIndex(item => item.Name);
        });

        modelBuilder.Entity<Component>(entity =>
        {
            entity.Property(item => item.Name).HasMaxLength(160);
            entity.HasIndex(item => new { item.InstallationId, item.Name }).IsUnique();
            entity.HasOne(item => item.Installation).WithMany(item => item.Components).HasForeignKey(item => item.InstallationId).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<WindowsServiceTarget>(entity =>
        {
            entity.Property(item => item.ServiceName).HasMaxLength(256);
            entity.Property(item => item.DisplayName).HasMaxLength(256);
            entity.HasOne(item => item.Component).WithMany(item => item.WindowsServiceTargets).HasForeignKey(item => item.ComponentId).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<ProcessTarget>(entity =>
        {
            entity.Property(item => item.ExecutablePath).HasMaxLength(2048);
            entity.Property(item => item.ExpectedFileVersion).HasMaxLength(80);
            entity.HasOne(item => item.Component).WithMany(item => item.ProcessTargets).HasForeignKey(item => item.ComponentId).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<FileTarget>(entity =>
        {
            entity.Property(item => item.Path).HasMaxLength(2048);
            entity.HasOne(item => item.Component).WithMany(item => item.FileTargets).HasForeignKey(item => item.ComponentId).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<TcpCheck>(entity =>
        {
            entity.Property(item => item.Host).HasMaxLength(253);
            entity.HasOne(item => item.Component).WithMany(item => item.TcpChecks).HasForeignKey(item => item.ComponentId).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<HttpCheck>(entity =>
        {
            entity.Property(item => item.Url).HasMaxLength(2048);
            entity.Property(item => item.Method).HasMaxLength(8);
            entity.Property(item => item.BodyPattern).HasMaxLength(500);
            entity.HasOne(item => item.Component).WithMany(item => item.HttpChecks).HasForeignKey(item => item.ComponentId).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<LogSource>(entity =>
        {
            entity.Property(item => item.Path).HasMaxLength(2048);
            entity.Property(item => item.EncodingName).HasMaxLength(32);
            entity.Property(item => item.FileIdentity).HasMaxLength(300);
            entity.HasOne(item => item.Component).WithMany(item => item.LogSources).HasForeignKey(item => item.ComponentId).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<HeartbeatDefinition>(entity =>
        {
            entity.Property(item => item.Name).HasMaxLength(160);
            entity.Property(item => item.JobKey).HasMaxLength(180);
            entity.Property(item => item.TokenHash).HasMaxLength(600);
            entity.HasIndex(item => item.JobKey).IsUnique();
            entity.HasOne(item => item.Component).WithMany(item => item.HeartbeatDefinitions).HasForeignKey(item => item.ComponentId).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<ProbeResult>(entity =>
        {
            entity.Property(item => item.Message).HasMaxLength(1_000);
            entity.HasIndex(item => new { item.ComponentId, item.ObservedAt });
            entity.HasOne(item => item.Component).WithMany(item => item.ProbeResults).HasForeignKey(item => item.ComponentId).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<MetricSample>(entity =>
        {
            entity.Property(item => item.Name).HasMaxLength(120);
            entity.Property(item => item.Unit).HasMaxLength(32);
            entity.HasIndex(item => new { item.ComponentId, item.Name, item.ObservedAt });
            entity.HasOne(item => item.Component).WithMany(item => item.MetricSamples).HasForeignKey(item => item.ComponentId).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<AlertRule>(entity =>
        {
            entity.Property(item => item.Name).HasMaxLength(200);
            entity.Property(item => item.RuleKey).HasMaxLength(180);
            entity.HasIndex(item => item.RuleKey).IsUnique();
            entity.HasOne(item => item.Component).WithMany(item => item.AlertRules).HasForeignKey(item => item.ComponentId).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<AlertOccurrence>(entity =>
        {
            entity.Property(item => item.Evidence).HasMaxLength(2_000);
            entity.HasIndex(item => new { item.State, item.StartedAt });
            entity.HasIndex(item => item.CorrelationId).IsUnique();
            entity.HasOne(item => item.AlertRule).WithMany(item => item.Occurrences).HasForeignKey(item => item.AlertRuleId).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<NotificationChannel>(entity =>
        {
            entity.Property(item => item.Name).HasMaxLength(160);
            entity.Property(item => item.ProtectedConfiguration).HasMaxLength(8_000);
        });

        modelBuilder.Entity<MaintenanceWindow>(entity =>
        {
            entity.Property(item => item.Name).HasMaxLength(160);
            entity.Property(item => item.Reason).HasMaxLength(500);
            entity.HasOne(item => item.Installation).WithMany().HasForeignKey(item => item.InstallationId).OnDelete(DeleteBehavior.Cascade);
            entity.HasOne(item => item.Component).WithMany().HasForeignKey(item => item.ComponentId).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<User>(entity =>
        {
            entity.Property(item => item.Username).HasMaxLength(120);
            entity.Property(item => item.DisplayName).HasMaxLength(160);
            entity.Property(item => item.Email).HasMaxLength(254);
            entity.Property(item => item.PasswordHash).HasMaxLength(600);
            entity.HasIndex(item => item.Username).IsUnique();
        });

        modelBuilder.Entity<AuditEvent>(entity =>
        {
            entity.Property(item => item.Action).HasMaxLength(160);
            entity.Property(item => item.EntityType).HasMaxLength(160);
            entity.Property(item => item.EntityId).HasMaxLength(160);
            entity.Property(item => item.RemoteAddress).HasMaxLength(64);
            entity.HasIndex(item => item.OccurredAt);
            entity.HasOne(item => item.User).WithMany().HasForeignKey(item => item.UserId).OnDelete(DeleteBehavior.SetNull);
        });
    }
}
