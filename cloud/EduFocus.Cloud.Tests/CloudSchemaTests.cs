using EduFocus.Cloud.Data;
using EduFocus.Cloud.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace EduFocus.Cloud.Tests;

[Collection("Postgres")]
public sealed class CloudSchemaTests
{
    private const string TestDbVariable = "EDUFOCUS_CLOUD_TEST_DB";

    [Fact]
    public async Task Initial_schema_enforces_uniqueness_upsert_and_cascade()
    {
        var connectionString = Environment.GetEnvironmentVariable(TestDbVariable);
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            return;
        }

        await using var db = CreateContext(connectionString);
        await db.Database.EnsureDeletedAsync();
        await db.Database.MigrateAsync();

        var now = DateTimeOffset.UtcNow;
        var family = new Family
        {
            Id = Guid.NewGuid(),
            CreatedAtUtc = now,
            TimeZoneId = "Asia/Ho_Chi_Minh",
        };
        var desktop = new Device
        {
            Id = Guid.NewGuid(),
            FamilyId = family.Id,
            Kind = DeviceKind.Desktop,
            DisplayName = "Student PC",
            Platform = "windows",
            CreatedAtUtc = now,
        };
        var mobile = new Device
        {
            Id = Guid.NewGuid(),
            FamilyId = family.Id,
            Kind = DeviceKind.Mobile,
            DisplayName = "Parent phone",
            Platform = "android",
            CreatedAtUtc = now,
        };
        var localDate = DateOnly.FromDateTime(now.UtcDateTime);

        db.AddRange(family, desktop, mobile);
        db.DeviceTokens.Add(new DeviceToken
        {
            Id = Guid.NewGuid(),
            DeviceId = desktop.Id,
            TokenHash = new string('a', 64),
            Scopes = "sync,family:admin",
            CreatedAtUtc = now,
        });
        db.PushRegistrations.Add(new PushRegistration
        {
            Id = Guid.NewGuid(),
            DeviceId = mobile.Id,
            FcmToken = "test-fcm-token",
            Platform = "android",
            UpdatedAtUtc = now,
        });
        db.NotificationPreferences.Add(new NotificationPreference
        {
            FamilyId = family.Id,
            UpdatedAtUtc = now,
        });
        db.DailySummaries.Add(CreateSummary(family.Id, desktop.Id, localDate, now, studySeconds: 600));
        db.SyncEvents.Add(new SyncEvent
        {
            Id = Guid.NewGuid(),
            FamilyId = family.Id,
            ClientEventId = Guid.NewGuid().ToString("D"),
            Type = SyncEventType.DailySummary,
            ReceivedAtUtc = now,
            ProcessedAtUtc = now,
            Status = SyncEventStatus.Processed,
        });
        db.Notifications.Add(new NotificationRecord
        {
            Id = Guid.NewGuid(),
            FamilyId = family.Id,
            Category = NotificationCategory.DailySummary,
            DedupeKey = $"daily_summary:{localDate:yyyy-MM-dd}",
            LocalDate = localDate,
            CreatedAtUtc = now,
            SentAtUtc = now,
            Status = NotificationStatus.Sent,
        });
        db.CloudAuditLogs.Add(new CloudAuditLog
        {
            Id = Guid.NewGuid(),
            FamilyId = family.Id,
            ActorDeviceId = desktop.Id,
            Action = "family.created",
            MetadataJson = "{}",
            CreatedAtUtc = now,
        });
        await db.SaveChangesAsync();

        await Assert_sync_event_is_unique(connectionString, family.Id, now);
        await Assert_daily_summary_supports_upsert(connectionString, family.Id, desktop.Id, localDate, now);
        await Assert_pairing_code_is_unique_only_while_unclaimed(
            connectionString, family.Id, desktop.Id, mobile.Id, now);
        await Assert_family_delete_cascades_and_audit_stays_anonymous(connectionString, family.Id);
        await Assert_schema_has_no_disallowed_columns(db);
    }

    private static async Task Assert_sync_event_is_unique(
        string connectionString, Guid familyId, DateTimeOffset now)
    {
        var clientEventId = Guid.NewGuid().ToString("D");
        await using (var seed = CreateContext(connectionString))
        {
            seed.SyncEvents.Add(new SyncEvent
            {
                Id = Guid.NewGuid(),
                FamilyId = familyId,
                ClientEventId = clientEventId,
                Type = SyncEventType.AttentionAlert,
                ReceivedAtUtc = now,
                Status = SyncEventStatus.Processed,
            });
            await seed.SaveChangesAsync();
        }

        await using var duplicate = CreateContext(connectionString);
        duplicate.SyncEvents.Add(new SyncEvent
        {
            Id = Guid.NewGuid(),
            FamilyId = familyId,
            ClientEventId = clientEventId,
            Type = SyncEventType.AttentionAlert,
            ReceivedAtUtc = now,
            Status = SyncEventStatus.Processed,
        });

        await Assert.ThrowsAsync<DbUpdateException>(() => duplicate.SaveChangesAsync());
    }

    private static async Task Assert_daily_summary_supports_upsert(
        string connectionString, Guid familyId, Guid desktopDeviceId, DateOnly localDate, DateTimeOffset now)
    {
        await using var db = CreateContext(connectionString);

        await db.Database.ExecuteSqlAsync($"""
            INSERT INTO "DailySummaries"
                ("Id", "FamilyId", "DesktopDeviceId", "LocalDate", "SessionsCount", "ActiveSeconds",
                 "StudySeconds", "IdleSeconds", "OnlineClassSeconds", "MonitoringEligibleSeconds",
                 "MonitoredSeconds", "FocusedSeconds", "DistractedSeconds", "AbsentSeconds",
                 "DrowsySeconds", "BlockedAttempts", "AttentionPercent", "CoveragePercent", "UpdatedAtUtc")
            VALUES
                ({Guid.NewGuid()}, {familyId}, {desktopDeviceId}, {localDate}, 1, 1800,
                 1800, 0, 0, 1800, 1800, 1500, 300, 0, 0, 0, 83, 95, {now})
            ON CONFLICT ("FamilyId", "DesktopDeviceId", "LocalDate")
            DO UPDATE SET
                "StudySeconds" = EXCLUDED."StudySeconds",
                "UpdatedAtUtc" = EXCLUDED."UpdatedAtUtc"
            """);

        var summary = await db.DailySummaries
            .AsNoTracking()
            .SingleAsync(x => x.FamilyId == familyId
                && x.DesktopDeviceId == desktopDeviceId
                && x.LocalDate == localDate);

        Assert.Equal(1800, summary.StudySeconds);
    }

    private static async Task Assert_pairing_code_is_unique_only_while_unclaimed(
        string connectionString, Guid familyId, Guid desktopDeviceId, Guid mobileDeviceId, DateTimeOffset now)
    {
        var codeHash = new string('b', 64);
        var first = new PairingSession
        {
            Id = Guid.NewGuid(),
            FamilyId = familyId,
            DesktopDeviceId = desktopDeviceId,
            CodeHash = codeHash,
            CreatedAtUtc = now,
            ExpiresAtUtc = now.AddMinutes(10),
        };
        await using (var seed = CreateContext(connectionString))
        {
            seed.PairingSessions.Add(first);
            await seed.SaveChangesAsync();
        }

        await using (var duplicate = CreateContext(connectionString))
        {
            duplicate.PairingSessions.Add(new PairingSession
            {
                Id = Guid.NewGuid(),
                FamilyId = familyId,
                DesktopDeviceId = desktopDeviceId,
                CodeHash = codeHash,
                CreatedAtUtc = now,
                ExpiresAtUtc = now.AddMinutes(10),
            });

            await Assert.ThrowsAsync<DbUpdateException>(() => duplicate.SaveChangesAsync());
        }

        await using (var claim = CreateContext(connectionString))
        {
            var session = await claim.PairingSessions.SingleAsync(x => x.Id == first.Id);
            session.ClaimedAtUtc = now;
            session.ClaimedByDeviceId = mobileDeviceId;
            await claim.SaveChangesAsync();
        }

        await using (var reuse = CreateContext(connectionString))
        {
            reuse.PairingSessions.Add(new PairingSession
            {
                Id = Guid.NewGuid(),
                FamilyId = familyId,
                DesktopDeviceId = desktopDeviceId,
                CodeHash = codeHash,
                CreatedAtUtc = now,
                ExpiresAtUtc = now.AddMinutes(10),
            });

            await reuse.SaveChangesAsync();
        }
    }

    private static async Task Assert_family_delete_cascades_and_audit_stays_anonymous(
        string connectionString, Guid familyId)
    {
        await using var db = CreateContext(connectionString);
        await db.Families.Where(x => x.Id == familyId).ExecuteDeleteAsync();

        Assert.False(await db.Devices.AnyAsync(x => x.FamilyId == familyId));
        Assert.False(await db.DailySummaries.AnyAsync(x => x.FamilyId == familyId));
        Assert.False(await db.SyncEvents.AnyAsync(x => x.FamilyId == familyId));
        Assert.False(await db.Notifications.AnyAsync(x => x.FamilyId == familyId));
        Assert.False(await db.NotificationPreferences.AnyAsync(x => x.FamilyId == familyId));
        Assert.False(await db.PairingSessions.AnyAsync(x => x.FamilyId == familyId));
        Assert.False(await db.DeviceTokens.AnyAsync());
        Assert.False(await db.PushRegistrations.AnyAsync());

        var audit = await db.CloudAuditLogs.AsNoTracking()
            .SingleAsync(x => x.Action == "family.created");
        Assert.Null(audit.FamilyId);
        Assert.Null(audit.ActorDeviceId);
    }

    private static async Task Assert_schema_has_no_disallowed_columns(CloudDbContext db)
    {
        var columns = await db.Database.SqlQueryRaw<string>(
            "SELECT table_name || '.' || column_name AS \"Value\" FROM information_schema.columns " +
            "WHERE table_schema = 'public'").ToListAsync();

        Assert.NotEmpty(columns);
        string[] forbidden = ["url", "windowtitle", "domain", "screenshot", "image", "blob", "pin", "recoverycode"];
        Assert.DoesNotContain(columns, column => forbidden.Any(
            token => column.Contains(token, StringComparison.OrdinalIgnoreCase)));
    }

    private static DailySummary CreateSummary(
        Guid familyId, Guid desktopDeviceId, DateOnly localDate, DateTimeOffset now, long studySeconds) =>
        new()
        {
            Id = Guid.NewGuid(),
            FamilyId = familyId,
            DesktopDeviceId = desktopDeviceId,
            LocalDate = localDate,
            SessionsCount = 1,
            ActiveSeconds = studySeconds,
            StudySeconds = studySeconds,
            MonitoringEligibleSeconds = studySeconds,
            MonitoredSeconds = studySeconds,
            FocusedSeconds = studySeconds,
            AttentionPercent = 100,
            CoveragePercent = 100,
            UpdatedAtUtc = now,
        };

    private static CloudDbContext CreateContext(string connectionString) =>
        new(new DbContextOptionsBuilder<CloudDbContext>().UseNpgsql(connectionString).Options);
}
