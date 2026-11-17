using EduFocus.Cloud.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace EduFocus.Cloud.Data;

public sealed class CloudDbContext(DbContextOptions<CloudDbContext> options) : DbContext(options)
{
    public DbSet<Family> Families => Set<Family>();
    public DbSet<Device> Devices => Set<Device>();
    public DbSet<DeviceToken> DeviceTokens => Set<DeviceToken>();
    public DbSet<PairingSession> PairingSessions => Set<PairingSession>();
    public DbSet<PushRegistration> PushRegistrations => Set<PushRegistration>();
    public DbSet<NotificationPreference> NotificationPreferences => Set<NotificationPreference>();
    public DbSet<DailySummary> DailySummaries => Set<DailySummary>();
    public DbSet<SyncEvent> SyncEvents => Set<SyncEvent>();
    public DbSet<NotificationRecord> Notifications => Set<NotificationRecord>();
    public DbSet<CloudAuditLog> CloudAuditLogs => Set<CloudAuditLog>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(CloudDbContext).Assembly);
    }
}
