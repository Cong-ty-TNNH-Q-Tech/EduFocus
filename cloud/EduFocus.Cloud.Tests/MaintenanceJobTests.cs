using EduFocus.Cloud.Api.Jobs;
using EduFocus.Cloud.Api.Options;
using EduFocus.Cloud.Data;
using EduFocus.Cloud.Data.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace EduFocus.Cloud.Tests;

[Collection("CloudDatabase")]
public sealed class MaintenanceJobTests
{
    private const string TestDbVariable = "EDUFOCUS_CLOUD_TEST_DB";
    private const string TimeZone = "Asia/Ho_Chi_Minh";

    // 2026-11-17 21:00 Asia/Ho_Chi_Minh.
    private static readonly DateTimeOffset Now = new(2026, 11, 17, 14, 0, 0, TimeSpan.Zero);
    private static readonly DateOnly Today = new(2026, 11, 17);

    [Fact]
    public async Task Daily_summary_notification_is_created_once_per_day()
    {
        var connectionString = TestConnectionString();
        if (connectionString is null)
        {
            return;
        }

        await ResetDatabaseAsync(connectionString);
        var seeded = await SeedFamilyAsync(connectionString);
        await SeedSummaryAsync(connectionString, seeded, Today);

        await using (var db = CreateContext(connectionString))
        {
            var result = await CreateService(db).RunAsync(CancellationToken.None);
            Assert.True(result.LockAcquired);
            Assert.Equal(1, result.SummaryNotifications);

            var notification = await db.Notifications.AsNoTracking().SingleAsync();
            Assert.Equal(NotificationCategory.DailySummary, notification.Category);
            Assert.Equal("daily_summary:20261117", notification.DedupeKey);
            Assert.Equal(Today, notification.LocalDate);
            Assert.Equal(NotificationStatus.Pending, notification.Status);
        }

        await using (var db = CreateContext(connectionString))
        {
            var result = await CreateService(db).RunAsync(CancellationToken.None);
            Assert.Equal(0, result.SummaryNotifications);
            Assert.Equal(1, await db.Notifications.CountAsync());
        }
    }

    [Fact]
    public async Task Daily_summary_respects_preferences_quiet_hours_and_missing_summary()
    {
        var connectionString = TestConnectionString();
        if (connectionString is null)
        {
            return;
        }

        await ResetDatabaseAsync(connectionString);

        var disabled = await SeedFamilyAsync(connectionString);
        await SeedSummaryAsync(connectionString, disabled, Today);
        await SeedPreferencesAsync(connectionString, disabled.FamilyId, prefs => prefs.DailySummary = false);

        var quiet = await SeedFamilyAsync(connectionString);
        await SeedSummaryAsync(connectionString, quiet, Today);
        await SeedPreferencesAsync(connectionString, quiet.FamilyId, prefs =>
        {
            prefs.QuietStartMinutes = 20 * 60;
            prefs.QuietEndMinutes = 23 * 60;
        });

        var missingSummary = await SeedFamilyAsync(connectionString);
        await SeedPreferencesAsync(connectionString, missingSummary.FamilyId, _ => { });

        var atThreshold = await SeedFamilyAsync(connectionString);
        await SeedSummaryAsync(connectionString, atThreshold, Today);
        await SeedPreferencesAsync(connectionString, atThreshold.FamilyId, prefs =>
            prefs.DailySummaryMinutes = 21 * 60);

        await using var db = CreateContext(connectionString);
        var result = await CreateService(db).RunAsync(CancellationToken.None);

        Assert.Equal(1, result.SummaryNotifications);
        var notification = await db.Notifications.AsNoTracking().SingleAsync();
        Assert.Equal(atThreshold.FamilyId, notification.FamilyId);
    }

    [Fact]
    public async Task Daily_summary_during_overnight_quiet_hours_is_created_once_quiet_hours_end()
    {
        var connectionString = TestConnectionString();
        if (connectionString is null)
        {
            return;
        }

        await ResetDatabaseAsync(connectionString);
        var family = await SeedFamilyAsync(connectionString);
        await SeedSummaryAsync(connectionString, family, Today);
        await SeedPreferencesAsync(connectionString, family.FamilyId, prefs =>
        {
            prefs.QuietStartMinutes = 22 * 60;
            prefs.QuietEndMinutes = 6 * 60;
            prefs.DailySummaryMinutes = 23 * 60;
        });

        // 23:15 local on Today (inside quiet hours): suppressed for now.
        var duringQuiet = new DateTimeOffset(2026, 11, 17, 16, 15, 0, TimeSpan.Zero);
        await using (var db = CreateContext(connectionString))
        {
            var result = await CreateService(db, duringQuiet).RunAsync(CancellationToken.None);
            Assert.Equal(0, result.SummaryNotifications);
        }

        // 06:05 local on Today + 1 (quiet hours ended): yesterday's summary is created once.
        var afterQuiet = new DateTimeOffset(2026, 11, 17, 23, 5, 0, TimeSpan.Zero);
        await using (var db = CreateContext(connectionString))
        {
            var result = await CreateService(db, afterQuiet).RunAsync(CancellationToken.None);
            Assert.Equal(1, result.SummaryNotifications);
            var notification = await db.Notifications.AsNoTracking().SingleAsync();
            Assert.Equal("daily_summary:20261117", notification.DedupeKey);
            Assert.Equal(Today, notification.LocalDate);
        }

        // Running again does not duplicate.
        await using (var db = CreateContext(connectionString))
        {
            var result = await CreateService(db, afterQuiet.AddMinutes(5)).RunAsync(CancellationToken.None);
            Assert.Equal(0, result.SummaryNotifications);
        }
    }

    [Fact]
    public async Task Retention_removes_only_expired_rows()
    {
        var connectionString = TestConnectionString();
        if (connectionString is null)
        {
            return;
        }

        await ResetDatabaseAsync(connectionString);
        var seeded = await SeedFamilyAsync(connectionString);

        Guid expiredPairingId;
        Guid keptPairingId;
        Guid expiredSyncId;
        Guid keptSyncId;
        Guid expiredNotificationId;
        Guid keptNotificationId;
        Guid expiredSummaryId;
        Guid keptSummaryId;
        Guid expiredAuditId;
        Guid keptAuditId;
        Guid expiredFamilyId;
        Guid keptFamilyId;

        await using (var db = CreateContext(connectionString))
        {
            expiredPairingId = Guid.NewGuid();
            keptPairingId = Guid.NewGuid();
            db.PairingSessions.AddRange(
                NewPairing(expiredPairingId, seeded, Now.AddHours(-25)),
                NewPairing(keptPairingId, seeded, Now.AddHours(-1)));

            expiredSyncId = Guid.NewGuid();
            keptSyncId = Guid.NewGuid();
            db.SyncEvents.AddRange(
                NewSyncEvent(expiredSyncId, seeded.FamilyId, "sync-old", Now.AddDays(-8)),
                NewSyncEvent(keptSyncId, seeded.FamilyId, "sync-new", Now.AddDays(-6)));

            expiredNotificationId = Guid.NewGuid();
            keptNotificationId = Guid.NewGuid();
            db.Notifications.AddRange(
                NewNotification(expiredNotificationId, seeded.FamilyId, "notify-old", Now.AddDays(-31)),
                NewNotification(keptNotificationId, seeded.FamilyId, "notify-new", Now.AddDays(-29)));

            expiredSummaryId = Guid.NewGuid();
            keptSummaryId = Guid.NewGuid();
            db.DailySummaries.AddRange(
                NewSummary(expiredSummaryId, seeded, new DateOnly(2026, 5, 20), Now.AddDays(-181)),
                NewSummary(keptSummaryId, seeded, new DateOnly(2026, 5, 22), Now.AddDays(-179)));

            expiredAuditId = Guid.NewGuid();
            keptAuditId = Guid.NewGuid();
            db.CloudAuditLogs.AddRange(
                NewAudit(expiredAuditId, Now.AddDays(-366)),
                NewAudit(keptAuditId, Now.AddDays(-364)));

            expiredFamilyId = Guid.NewGuid();
            keptFamilyId = Guid.NewGuid();
            db.Families.AddRange(
                new Family
                {
                    Id = expiredFamilyId,
                    CreatedAtUtc = Now.AddDays(-60),
                    TimeZoneId = TimeZone,
                    DeletedAtUtc = Now.AddDays(-31),
                },
                new Family
                {
                    Id = keptFamilyId,
                    CreatedAtUtc = Now.AddDays(-60),
                    TimeZoneId = TimeZone,
                    DeletedAtUtc = Now.AddDays(-1),
                });

            await db.SaveChangesAsync();
        }

        await using (var db = CreateContext(connectionString))
        {
            var result = await CreateService(db).RunAsync(CancellationToken.None);
            Assert.Equal(new RetentionCounts(1, 1, 1, 1, 1, 1), result.Deleted);
            Assert.Equal(6, result.Deleted.Total);
        }

        await using (var db = CreateContext(connectionString))
        {
            Assert.False(await db.PairingSessions.AnyAsync(x => x.Id == expiredPairingId));
            Assert.True(await db.PairingSessions.AnyAsync(x => x.Id == keptPairingId));
            Assert.False(await db.SyncEvents.AnyAsync(x => x.Id == expiredSyncId));
            Assert.True(await db.SyncEvents.AnyAsync(x => x.Id == keptSyncId));
            Assert.False(await db.Notifications.AnyAsync(x => x.Id == expiredNotificationId));
            Assert.True(await db.Notifications.AnyAsync(x => x.Id == keptNotificationId));
            Assert.False(await db.DailySummaries.AnyAsync(x => x.Id == expiredSummaryId));
            Assert.True(await db.DailySummaries.AnyAsync(x => x.Id == keptSummaryId));
            Assert.False(await db.CloudAuditLogs.AnyAsync(x => x.Id == expiredAuditId));
            Assert.True(await db.CloudAuditLogs.AnyAsync(x => x.Id == keptAuditId));
            Assert.False(await db.Families.AnyAsync(x => x.Id == expiredFamilyId));
            Assert.True(await db.Families.AnyAsync(x => x.Id == keptFamilyId));
        }
    }

    private static CloudMaintenanceService CreateService(CloudDbContext db, DateTimeOffset? now = null) =>
        new(
            db,
            Options.Create(new JobOptions()),
            new FixedTimeProvider(now ?? Now),
            NullLogger<CloudMaintenanceService>.Instance);

    private static PairingSession NewPairing(
        Guid id,
        SeededFamily seeded,
        DateTimeOffset expiresAtUtc) =>
        new()
        {
            Id = id,
            FamilyId = seeded.FamilyId,
            DesktopDeviceId = seeded.DesktopDeviceId,
            CodeHash = Guid.NewGuid().ToString("N") + Guid.NewGuid().ToString("N")[..32],
            CreatedAtUtc = expiresAtUtc.AddHours(-1),
            ExpiresAtUtc = expiresAtUtc,
        };

    private static SyncEvent NewSyncEvent(
        Guid id,
        Guid familyId,
        string clientEventId,
        DateTimeOffset receivedAtUtc) =>
        new()
        {
            Id = id,
            FamilyId = familyId,
            ClientEventId = clientEventId,
            Type = SyncEventType.SecurityAlert,
            ReceivedAtUtc = receivedAtUtc,
            Status = SyncEventStatus.Processed,
            ProcessedAtUtc = receivedAtUtc,
        };

    private static NotificationRecord NewNotification(
        Guid id,
        Guid familyId,
        string dedupeKey,
        DateTimeOffset createdAtUtc) =>
        new()
        {
            Id = id,
            FamilyId = familyId,
            Category = NotificationCategory.SecurityAlert,
            DedupeKey = dedupeKey,
            CreatedAtUtc = createdAtUtc,
            SentAtUtc = createdAtUtc,
            Status = NotificationStatus.Sent,
        };

    private static DailySummary NewSummary(
        Guid id,
        SeededFamily seeded,
        DateOnly localDate,
        DateTimeOffset updatedAtUtc) =>
        new()
        {
            Id = id,
            FamilyId = seeded.FamilyId,
            DesktopDeviceId = seeded.DesktopDeviceId,
            LocalDate = localDate,
            SessionsCount = 1,
            ActiveSeconds = 3600,
            StudySeconds = 3600,
            MonitoringEligibleSeconds = 3600,
            MonitoredSeconds = 3600,
            FocusedSeconds = 3000,
            UpdatedAtUtc = updatedAtUtc,
        };

    private static CloudAuditLog NewAudit(Guid id, DateTimeOffset createdAtUtc) =>
        new()
        {
            Id = id,
            Action = "TestAudit",
            MetadataJson = "{}",
            CreatedAtUtc = createdAtUtc,
        };

    private static async Task<SeededFamily> SeedFamilyAsync(string connectionString)
    {
        var familyId = Guid.NewGuid();
        var desktopDeviceId = Guid.NewGuid();
        await using var db = CreateContext(connectionString);
        db.Families.Add(new Family
        {
            Id = familyId,
            CreatedAtUtc = Now.AddDays(-30),
            TimeZoneId = TimeZone,
        });
        db.Devices.Add(new Device
        {
            Id = desktopDeviceId,
            FamilyId = familyId,
            Kind = DeviceKind.Desktop,
            DisplayName = "Student PC",
            Platform = "windows",
            CreatedAtUtc = Now.AddDays(-30),
        });
        await db.SaveChangesAsync();
        return new SeededFamily(familyId, desktopDeviceId);
    }

    private static async Task SeedPreferencesAsync(
        string connectionString,
        Guid familyId,
        Action<NotificationPreference> configure)
    {
        await using var db = CreateContext(connectionString);
        var preferences = new NotificationPreference
        {
            FamilyId = familyId,
            TimeZoneId = TimeZone,
            UpdatedAtUtc = Now,
        };
        configure(preferences);
        db.NotificationPreferences.Add(preferences);
        await db.SaveChangesAsync();
    }

    private static async Task SeedSummaryAsync(
        string connectionString,
        SeededFamily seeded,
        DateOnly localDate)
    {
        await using var db = CreateContext(connectionString);
        db.DailySummaries.Add(NewSummary(Guid.NewGuid(), seeded, localDate, Now));
        await db.SaveChangesAsync();
    }

    private static string? TestConnectionString() =>
        Environment.GetEnvironmentVariable(TestDbVariable) is { Length: > 0 } value
            ? value
            : null;

    private static async Task ResetDatabaseAsync(string connectionString)
    {
        await using var db = CreateContext(connectionString);
        await db.Database.EnsureDeletedAsync();
        await db.Database.MigrateAsync();
    }

    private static CloudDbContext CreateContext(string connectionString) =>
            new(new DbContextOptionsBuilder<CloudDbContext>().UseSqlite(connectionString).Options);

    private sealed record SeededFamily(Guid FamilyId, Guid DesktopDeviceId);

    private sealed class FixedTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => utcNow;
    }
}
