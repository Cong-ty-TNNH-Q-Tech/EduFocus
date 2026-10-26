using EduFocus.Domain.Entities;
using EduFocus.Domain.Enums;
using EduFocus.Ports.Outbound.Services;
using Microsoft.EntityFrameworkCore;

namespace EduFocus.Infrastructure.Persistence;

public sealed record RetentionResult(int ActivitySamplesDeleted, int AttentionEventsDeleted,
    int BlockedAttemptsDeleted, int StudySessionsDeleted, int AuditLogsDeleted)
{
    public int TotalDeleted => ActivitySamplesDeleted + AttentionEventsDeleted + BlockedAttemptsDeleted + StudySessionsDeleted + AuditLogsDeleted;
}

/// <summary>Deletes expired records in small idempotent batches while preserving active sessions and summaries with newer detail.</summary>
public sealed class RetentionService(AppDbContext db, IAppLogger logger)
{
    private const int BatchSize = 500;
    private static readonly SemaphoreSlim RunGate = new(1, 1);

    public async Task<RetentionResult> RunAsync(DateTimeOffset now, CancellationToken cancellationToken = default)
    {
        if (now.Offset != TimeSpan.Zero) throw new ArgumentException("Timestamp must be UTC.", nameof(now));
        await RunGate.WaitAsync(cancellationToken);
        try
        {
            var settings = await db.AppSettings.AsNoTracking().SingleOrDefaultAsync(cancellationToken);
            if (settings is null) return new(0, 0, 0, 0, 0);

            var activityCutoff = now.AddDays(-settings.ActivityRetentionDays);
            var eventCutoff = now.AddDays(-settings.EventRetentionDays);
            var sessionCutoff = now.AddDays(-settings.SessionRetentionDays);
            var auditCutoff = now.AddDays(-365);

            var activityDeleted = await DeleteActivitySamplesAsync(activityCutoff, cancellationToken);
            var eventsDeleted = await DeleteAttentionEventsAsync(eventCutoff, cancellationToken);
            var attemptsDeleted = await DeleteBlockedAttemptsAsync(eventCutoff, cancellationToken);
            var sessionsDeleted = await DeleteStudySessionsAsync(sessionCutoff, activityCutoff, eventCutoff, cancellationToken);
            var auditsDeleted = await DeleteAuditLogsAsync(auditCutoff, cancellationToken);

            var result = new RetentionResult(activityDeleted, eventsDeleted, attemptsDeleted, sessionsDeleted, auditsDeleted);
            if (result.TotalDeleted > 0)
                await db.Database.ExecuteSqlRawAsync("PRAGMA incremental_vacuum(100);", cancellationToken);

            db.AuditLogs.Add(AuditLog.Create(now, AuditActor.System, "RetentionCompleted",
                $"Expired records deleted: activity={activityDeleted}, attention={eventsDeleted}, attempts={attemptsDeleted}, sessions={sessionsDeleted}, audit={auditsDeleted}.", now));
            await db.SaveChangesAsync(cancellationToken);
            logger.Info($"Retention completed; {result.TotalDeleted} expired records deleted.");
            return result;
        }
        finally { RunGate.Release(); }
    }

    private async Task<int> DeleteActivitySamplesAsync(DateTimeOffset cutoff, CancellationToken cancellationToken)
    {
        var deleted = 0;
        while (true)
        {
            var ids = await db.ActivitySamples.AsNoTracking().Where(x => x.SampledAtUtc < cutoff)
                .OrderBy(x => x.SampledAtUtc).Take(BatchSize).Select(x => x.Id).ToArrayAsync(cancellationToken);
            if (ids.Length == 0) return deleted;
            deleted += await db.ActivitySamples.Where(x => ids.Contains(x.Id)).ExecuteDeleteAsync(cancellationToken);
        }
    }

    private async Task<int> DeleteAttentionEventsAsync(DateTimeOffset cutoff, CancellationToken cancellationToken)
    {
        var deleted = 0;
        while (true)
        {
            var ids = await db.AttentionEvents.AsNoTracking().Where(x => x.EndedAtUtc != null && x.CreatedAtUtc < cutoff)
                .OrderBy(x => x.CreatedAtUtc).Take(BatchSize).Select(x => x.Id).ToArrayAsync(cancellationToken);
            if (ids.Length == 0) return deleted;
            deleted += await db.AttentionEvents.Where(x => ids.Contains(x.Id)).ExecuteDeleteAsync(cancellationToken);
        }
    }

    private async Task<int> DeleteBlockedAttemptsAsync(DateTimeOffset cutoff, CancellationToken cancellationToken)
    {
        var deleted = 0;
        while (true)
        {
            var ids = await db.BlockedAttempts.AsNoTracking().Where(x => x.OccurredAtUtc < cutoff)
                .OrderBy(x => x.OccurredAtUtc).Take(BatchSize).Select(x => x.Id).ToArrayAsync(cancellationToken);
            if (ids.Length == 0) return deleted;
            deleted += await db.BlockedAttempts.Where(x => ids.Contains(x.Id)).ExecuteDeleteAsync(cancellationToken);
        }
    }

    private async Task<int> DeleteStudySessionsAsync(DateTimeOffset sessionCutoff, DateTimeOffset activityCutoff,
        DateTimeOffset eventCutoff, CancellationToken cancellationToken)
    {
        var deleted = 0;
        while (true)
        {
            var ids = await db.StudySessions.AsNoTracking()
                .Where(session => session.Status != StudySessionStatus.Active && session.EndedAtUtc < sessionCutoff &&
                    !db.ActivitySamples.Any(sample => sample.SessionId == session.Id && sample.SampledAtUtc >= activityCutoff) &&
                    !db.AttentionEvents.Any(attention => attention.SessionId == session.Id &&
                        (attention.EndedAtUtc == null || attention.CreatedAtUtc >= eventCutoff)))
                .OrderBy(session => session.EndedAtUtc).Take(BatchSize).Select(session => session.Id).ToArrayAsync(cancellationToken);
            if (ids.Length == 0) return deleted;
            deleted += await db.StudySessions.Where(x => ids.Contains(x.Id)).ExecuteDeleteAsync(cancellationToken);
        }
    }

    private async Task<int> DeleteAuditLogsAsync(DateTimeOffset cutoff, CancellationToken cancellationToken)
    {
        var deleted = 0;
        while (true)
        {
            var ids = await db.AuditLogs.AsNoTracking().Where(x => x.OccurredAtUtc < cutoff)
                .OrderBy(x => x.OccurredAtUtc).Take(BatchSize).Select(x => x.Id).ToArrayAsync(cancellationToken);
            if (ids.Length == 0) return deleted;
            deleted += await db.AuditLogs.Where(x => ids.Contains(x.Id)).ExecuteDeleteAsync(cancellationToken);
        }
    }
}
