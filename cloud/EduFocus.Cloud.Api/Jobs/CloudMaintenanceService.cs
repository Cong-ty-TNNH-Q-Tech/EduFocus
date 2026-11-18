using EduFocus.Cloud.Api.Options;
using EduFocus.Cloud.Api.Push;
using EduFocus.Cloud.Data;
using EduFocus.Cloud.Data.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace EduFocus.Cloud.Api.Jobs;

public sealed record RetentionCounts(
    int PairingSessions,
    int SyncEvents,
    int Notifications,
    int DailySummaries,
    int CloudAuditLogs,
    int SoftDeletedFamilies)
{
    public int Total =>
        PairingSessions + SyncEvents + Notifications + DailySummaries + CloudAuditLogs + SoftDeletedFamilies;
}

public sealed record MaintenanceResult(
    bool LockAcquired,
    int SummaryNotifications,
    RetentionCounts Deleted)
{
    public static MaintenanceResult Skipped { get; } =
        new(false, 0, new RetentionCounts(0, 0, 0, 0, 0, 0));
}

public sealed class CloudMaintenanceService(
    CloudDbContext db,
    IOptions<JobOptions> options,
    TimeProvider timeProvider,
    ILogger<CloudMaintenanceService> logger)
{
    public const long AdvisoryLockKey = 7_309_114_026;

    public async Task<MaintenanceResult> RunAsync(CancellationToken cancellationToken)
    {
        await db.Database.OpenConnectionAsync(cancellationToken);
        try
        {
            var acquired = await db.Database
                .SqlQueryRaw<bool>(
                    $"SELECT pg_try_advisory_lock({AdvisoryLockKey}) AS \"Value\"")
                .SingleAsync(cancellationToken);
            if (!acquired)
            {
                return MaintenanceResult.Skipped;
            }

            try
            {
                var now = timeProvider.GetUtcNow();
                var summaryNotifications = await CreateDailySummaryNotificationsAsync(now, cancellationToken);
                var deleted = await ApplyRetentionAsync(now, cancellationToken);

                logger.LogInformation(
                    "Cloud maintenance finished: {SummaryNotifications} summary notifications queued, " +
                    "{DeletedTotal} expired rows deleted.",
                    summaryNotifications,
                    deleted.Total);
                return new MaintenanceResult(true, summaryNotifications, deleted);
            }
            finally
            {
                await db.Database.ExecuteSqlRawAsync(
                    $"SELECT pg_advisory_unlock({AdvisoryLockKey})",
                    cancellationToken);
            }
        }
        finally
        {
            await db.Database.CloseConnectionAsync();
        }
    }

    private async Task<int> CreateDailySummaryNotificationsAsync(
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var families = await db.Families
            .AsNoTracking()
            .Where(f => f.DeletedAtUtc == null)
            .ToListAsync(cancellationToken);

        var created = 0;
        foreach (var family in families)
        {
            var preferences = await db.NotificationPreferences
                .AsNoTracking()
                .FirstOrDefaultAsync(p => p.FamilyId == family.Id, cancellationToken);
            if (preferences is not null && !preferences.DailySummary)
            {
                continue;
            }

            var timeZoneId = preferences?.TimeZoneId ?? family.TimeZoneId;
            var zone = TimeZoneInfo.TryFindSystemTimeZoneById(timeZoneId, out var found)
                ? found
                : TimeZoneInfo.Utc;
            var localNow = TimeZoneInfo.ConvertTime(now, zone);
            var localMinutes = (localNow.Hour * 60) + localNow.Minute;
            var summaryMinutes = preferences?.DailySummaryMinutes ?? 20 * 60 + 30;
            if (PushRules.IsQuietHours(
                    now,
                    timeZoneId,
                    preferences?.QuietStartMinutes ?? 22 * 60,
                    preferences?.QuietEndMinutes ?? 6 * 60))
            {
                continue;
            }

            var localDate = DateOnly.FromDateTime(localNow.DateTime);
            // Catch up yesterday's summary if its scheduled time fell inside overnight quiet hours.
            if (await TryCreateDailySummaryNotificationAsync(
                    family.Id,
                    localDate.AddDays(-1),
                    now,
                    cancellationToken))
            {
                created++;
            }

            if (localMinutes >= summaryMinutes
                && await TryCreateDailySummaryNotificationAsync(
                    family.Id,
                    localDate,
                    now,
                    cancellationToken))
            {
                created++;
            }
        }

        if (created > 0)
        {
            await db.SaveChangesAsync(cancellationToken);
        }

        return created;
    }

    private async Task<bool> TryCreateDailySummaryNotificationAsync(
        Guid familyId,
        DateOnly localDate,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var hasSummary = await db.DailySummaries
            .AsNoTracking()
            .AnyAsync(
                s => s.FamilyId == familyId && s.LocalDate == localDate,
                cancellationToken);
        if (!hasSummary)
        {
            return false;
        }

        var dedupeKey = NotificationPlanner.DailySummary(localDate).DedupeKey;
        var exists = db.Notifications.Local.Any(
                n => n.FamilyId == familyId && n.DedupeKey == dedupeKey)
            || await db.Notifications
                .AsNoTracking()
                .AnyAsync(
                    n => n.FamilyId == familyId && n.DedupeKey == dedupeKey,
                    cancellationToken);
        if (exists)
        {
            return false;
        }

        db.Notifications.Add(new NotificationRecord
        {
            Id = Guid.NewGuid(),
            FamilyId = familyId,
            Category = NotificationCategory.DailySummary,
            DedupeKey = dedupeKey,
            LocalDate = localDate,
            CreatedAtUtc = now,
            Status = NotificationStatus.Pending,
        });
        return true;
    }

    private async Task<RetentionCounts> ApplyRetentionAsync(
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var settings = options.Value;

        var pairingSessions = await db.PairingSessions
            .Where(s => s.ExpiresAtUtc < now.AddHours(-settings.PairingSessionHours))
            .ExecuteDeleteAsync(cancellationToken);
        var syncEvents = await db.SyncEvents
            .Where(e => e.ReceivedAtUtc < now.AddDays(-settings.SyncEventDays))
            .ExecuteDeleteAsync(cancellationToken);
        var notifications = await db.Notifications
            .Where(n => n.CreatedAtUtc < now.AddDays(-settings.NotificationDays))
            .ExecuteDeleteAsync(cancellationToken);
        var dailySummaries = await db.DailySummaries
            .Where(s => s.UpdatedAtUtc < now.AddDays(-settings.DailySummaryDays))
            .ExecuteDeleteAsync(cancellationToken);
        var auditLogs = await db.CloudAuditLogs
            .Where(a => a.CreatedAtUtc < now.AddDays(-settings.CloudAuditDays))
            .ExecuteDeleteAsync(cancellationToken);
        var families = await db.Families
            .Where(f => f.DeletedAtUtc != null
                && f.DeletedAtUtc < now.AddDays(-settings.SoftDeletedFamilyDays))
            .ExecuteDeleteAsync(cancellationToken);

        return new RetentionCounts(
            pairingSessions,
            syncEvents,
            notifications,
            dailySummaries,
            auditLogs,
            families);
    }
}
