using EduFocus.Domain.Attention;
using EduFocus.Domain.Entities;
using EduFocus.Domain.Enums;
using EduFocus.Domain.Reports;
using EduFocus.Domain.ValueObjects;
using EduFocus.Ports.Outbound.Persistence;
using EduFocus.SqliteDataAccess.Converters;
using EduFocus.SqliteDataAccess.Persistence;
using Microsoft.EntityFrameworkCore;

namespace EduFocus.SqliteDataAccess.Repositories;

/// <summary>
/// Study session persistence (database schema §8). The unique partial index
/// UX_StudySessions_Active rejects a second Active session. Heartbeat and
/// blocked-attempt writes are conditional, targeted updates guarded by the
/// Active status so they never resurrect a finished session.
/// </summary>
public sealed class SqliteStudySessionRepository : IStudySessionRepository
{
    private readonly SqliteDbContextFactory _contextFactory;

    public SqliteStudySessionRepository(SqliteDbContextFactory contextFactory)
    {
        ArgumentNullException.ThrowIfNull(contextFactory);
        _contextFactory = contextFactory;
    }

    public async Task<StudySession?> GetActiveAsync(CancellationToken cancellationToken = default)
    {
        await using var context = _contextFactory.CreateDbContext();
        return await context.StudySessions
            .AsNoTracking()
            .SingleOrDefaultAsync(x => x.Status == StudySessionStatus.Active, cancellationToken);
    }

    public async Task<DailyStudySummary?> GetDailySummaryAsync(
        DateOnly localDate,
        CancellationToken cancellationToken = default)
    {
        if (localDate == default)
            throw new ArgumentException("Local date is required.", nameof(localDate));

        await using var context = _contextFactory.CreateDbContext();

        // One aggregate row from StudySessions only; ActivitySamples and
        // AttentionEvents are never scanned for the dashboard.
        var totals = await context.StudySessions
            .AsNoTracking()
            .Where(x => x.StartLocalDate == localDate)
            .GroupBy(_ => 1)
            .Select(group => new
            {
                SessionCount = group.Count(),
                ActiveSeconds = group.Sum(x => x.ActiveSeconds),
                StudySeconds = group.Sum(x => x.StudySeconds),
                DistractionSeconds = group.Sum(x => x.DistractionSeconds),
                BlockedAttemptCount = group.Sum(x => x.BlockedAttemptCount),
                FocusedSeconds = group.Sum(x => x.FocusedSeconds),
                DistractedSeconds = group.Sum(x => x.DistractedSeconds),
                AbsentSeconds = group.Sum(x => x.AbsentSeconds),
                DrowsySeconds = group.Sum(x => x.DrowsySeconds),
                CameraUnavailableSeconds = group.Sum(x => x.CameraUnavailableSeconds),
                MonitoringEligibleSeconds = group.Sum(x => x.MonitoringEligibleSeconds),
                ActiveSessionStartedAtUtc = group.Max(x =>
                    x.Status == StudySessionStatus.Active ? (DateTime?)x.StartedAtUtc : null)
            })
            .SingleOrDefaultAsync(cancellationToken);

        if (totals is null)
            return null;

        return new DailyStudySummary
        {
            LocalDate = localDate,
            SessionCount = totals.SessionCount,
            ActiveSeconds = totals.ActiveSeconds,
            StudySeconds = totals.StudySeconds,
            DistractionSeconds = totals.DistractionSeconds,
            BlockedAttemptCount = totals.BlockedAttemptCount,
            ActiveSessionStartedAtUtc = totals.ActiveSessionStartedAtUtc,
            FocusedSeconds = totals.FocusedSeconds,
            DistractedSeconds = totals.DistractedSeconds,
            AbsentSeconds = totals.AbsentSeconds,
            DrowsySeconds = totals.DrowsySeconds,
            CameraUnavailableSeconds = totals.CameraUnavailableSeconds,
            MonitoringEligibleSeconds = totals.MonitoringEligibleSeconds
        };
    }

    public async Task<IReadOnlyList<StudySession>> ListByLocalDateRangeAsync(
        DateOnly startLocalDate,
        DateOnly endLocalDate,
        CancellationToken cancellationToken = default)
    {
        if (startLocalDate == default)
            throw new ArgumentException("Local date is required.", nameof(startLocalDate));
        if (endLocalDate == default)
            throw new ArgumentException("Local date is required.", nameof(endLocalDate));
        if (endLocalDate < startLocalDate)
            throw new ArgumentException("The range end must not precede its start.", nameof(endLocalDate));

        await using var context = _contextFactory.CreateDbContext();

        // IX_StudySessions_StartLocalDate keeps this range cheap even on a
        // multi-year database; raw samples are never touched here.
        return await context.StudySessions
            .AsNoTracking()
            .Where(x => x.StartLocalDate >= startLocalDate && x.StartLocalDate <= endLocalDate)
            .OrderBy(x => x.StartedAtUtc)
            .ToListAsync(cancellationToken);
    }

    public async Task AddAsync(StudySession session, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(session);

        await using var context = _contextFactory.CreateDbContext();
        context.StudySessions.Add(session);
        await context.SaveChangesAsync(cancellationToken);
    }

    public async Task UpdateAsync(StudySession session, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(session);

        await using var context = _contextFactory.CreateDbContext();
        context.StudySessions.Update(session);
        await context.SaveChangesAsync(cancellationToken);
    }

    public async Task<bool> RecordHeartbeatAsync(
        StudySessionId sessionId,
        DateTime heartbeatAtUtc,
        int activeSeconds,
        CancellationToken cancellationToken = default)
    {
        if (activeSeconds < 0)
            throw new ArgumentOutOfRangeException(nameof(activeSeconds), activeSeconds, "Active seconds cannot be negative.");
        if (heartbeatAtUtc.Kind != DateTimeKind.Utc)
            throw new ArgumentException("The heartbeat must be UTC.", nameof(heartbeatAtUtc));

        await using var context = _contextFactory.CreateDbContext();

        var affected = await context.StudySessions
            .Where(x => x.Id == sessionId
                && x.Status == StudySessionStatus.Active
                && x.LastHeartbeatAtUtc <= heartbeatAtUtc)
            .ExecuteUpdateAsync(
                setters => setters
                    .SetProperty(x => x.LastHeartbeatAtUtc, heartbeatAtUtc)
                    .SetProperty(x => x.ActiveSeconds, x => x.ActiveSeconds + activeSeconds)
                    .SetProperty(
                        x => x.UpdatedAtUtc,
                        x => x.UpdatedAtUtc < heartbeatAtUtc ? heartbeatAtUtc : x.UpdatedAtUtc),
                cancellationToken);

        return affected > 0;
    }

    public async Task<bool> IncrementBlockedAttemptCountAsync(
        StudySessionId sessionId,
        DateTime nowUtc,
        int count = 1,
        CancellationToken cancellationToken = default)
    {
        if (count < 0)
            throw new ArgumentOutOfRangeException(nameof(count), count, "Count cannot be negative.");
        if (nowUtc.Kind != DateTimeKind.Utc)
            throw new ArgumentException("The timestamp must be UTC.", nameof(nowUtc));

        await using var context = _contextFactory.CreateDbContext();

        var affected = await context.StudySessions
            .Where(x => x.Id == sessionId && x.Status == StudySessionStatus.Active)
            .ExecuteUpdateAsync(
                setters => setters
                    .SetProperty(x => x.BlockedAttemptCount, x => x.BlockedAttemptCount + count)
                    .SetProperty(
                        x => x.UpdatedAtUtc,
                        x => x.UpdatedAtUtc < nowUtc ? nowUtc : x.UpdatedAtUtc),
                cancellationToken);

        return affected > 0;
    }

    public async Task<bool> AddActivitySecondsAsync(
        StudySessionId sessionId,
        ActivityCategory category,
        int categorySeconds,
        int idleSeconds,
        int onlineClassSeconds,
        int monitoringEligibleSeconds,
        DateTime nowUtc,
        CancellationToken cancellationToken = default)
    {
        if (!Enum.IsDefined(category))
            throw new ArgumentOutOfRangeException(nameof(category), category, "Unknown activity category.");
        if (categorySeconds < 0)
            throw new ArgumentOutOfRangeException(nameof(categorySeconds), categorySeconds, "Seconds cannot be negative.");
        if (idleSeconds < 0)
            throw new ArgumentOutOfRangeException(nameof(idleSeconds), idleSeconds, "Seconds cannot be negative.");
        if (onlineClassSeconds < 0)
            throw new ArgumentOutOfRangeException(nameof(onlineClassSeconds), onlineClassSeconds, "Seconds cannot be negative.");
        if (monitoringEligibleSeconds < 0)
            throw new ArgumentOutOfRangeException(
                nameof(monitoringEligibleSeconds),
                monitoringEligibleSeconds,
                "Seconds cannot be negative.");
        if (nowUtc.Kind != DateTimeKind.Utc)
            throw new ArgumentException("The timestamp must be UTC.", nameof(nowUtc));

        await using var context = _contextFactory.CreateDbContext();

        var query = context.StudySessions
            .Where(x => x.Id == sessionId && x.Status == StudySessionStatus.Active);

        var affected = category switch
        {
            ActivityCategory.Study => await query.ExecuteUpdateAsync(
                setters => setters
                    .SetProperty(x => x.StudySeconds, x => x.StudySeconds + categorySeconds)
                    .SetProperty(x => x.IdleSeconds, x => x.IdleSeconds + idleSeconds)
                    .SetProperty(x => x.OnlineClassSeconds, x => x.OnlineClassSeconds + onlineClassSeconds)
                    .SetProperty(
                        x => x.MonitoringEligibleSeconds,
                        x => x.MonitoringEligibleSeconds + monitoringEligibleSeconds)
                    .SetProperty(
                        x => x.UpdatedAtUtc,
                        x => x.UpdatedAtUtc < nowUtc ? nowUtc : x.UpdatedAtUtc),
                cancellationToken),
            ActivityCategory.Distraction => await query.ExecuteUpdateAsync(
                setters => setters
                    .SetProperty(x => x.DistractionSeconds, x => x.DistractionSeconds + categorySeconds)
                    .SetProperty(x => x.IdleSeconds, x => x.IdleSeconds + idleSeconds)
                    .SetProperty(x => x.OnlineClassSeconds, x => x.OnlineClassSeconds + onlineClassSeconds)
                    .SetProperty(
                        x => x.MonitoringEligibleSeconds,
                        x => x.MonitoringEligibleSeconds + monitoringEligibleSeconds)
                    .SetProperty(
                        x => x.UpdatedAtUtc,
                        x => x.UpdatedAtUtc < nowUtc ? nowUtc : x.UpdatedAtUtc),
                cancellationToken),
            _ => await query.ExecuteUpdateAsync(
                setters => setters
                    .SetProperty(x => x.NeutralSeconds, x => x.NeutralSeconds + categorySeconds)
                    .SetProperty(x => x.IdleSeconds, x => x.IdleSeconds + idleSeconds)
                    .SetProperty(x => x.OnlineClassSeconds, x => x.OnlineClassSeconds + onlineClassSeconds)
                    .SetProperty(
                        x => x.MonitoringEligibleSeconds,
                        x => x.MonitoringEligibleSeconds + monitoringEligibleSeconds)
                    .SetProperty(
                        x => x.UpdatedAtUtc,
                        x => x.UpdatedAtUtc < nowUtc ? nowUtc : x.UpdatedAtUtc),
                cancellationToken)
        };

        return affected > 0;
    }

    public async Task<bool> AddAttentionSecondsAsync(
        StudySessionId sessionId,
        AttentionState state,
        int seconds,
        double? confidence,
        DateTime nowUtc,
        CancellationToken cancellationToken = default)
    {
        if (!Enum.IsDefined(state))
            throw new ArgumentOutOfRangeException(nameof(state), state, "Unknown attention state.");
        if (seconds < 0)
            throw new ArgumentOutOfRangeException(nameof(seconds), seconds, "Seconds cannot be negative.");
        if (confidence is < 0 or > 1)
            throw new ArgumentOutOfRangeException(nameof(confidence), confidence, "Confidence must be between 0 and 1.");
        if (nowUtc.Kind != DateTimeKind.Utc)
            throw new ArgumentException("The timestamp must be UTC.", nameof(nowUtc));

        if (seconds == 0)
            return true;

        var column = state switch
        {
            AttentionState.Focused => "FocusedSeconds",
            AttentionState.Distracted => "DistractedSeconds",
            AttentionState.Absent => "AbsentSeconds",
            AttentionState.Drowsy => "DrowsySeconds",
            AttentionState.CameraUnavailable => "CameraUnavailableSeconds",
            _ => throw new ArgumentOutOfRangeException(nameof(state), state, "Unknown attention state.")
        };

        // The generated MonitoredSeconds column is not updatable, so the
        // weighted confidence is computed inline from the four monitored
        // counters. SQLite evaluates every SET expression against the old row,
        // which is exactly what the weight formula needs. A null confidence
        // (camera unavailable) leaves ConfidenceAvg untouched and stays
        // outside the denominator.
        var sql = confidence is null
            ? $$"""
                UPDATE "StudySessions"
                SET "{{column}}" = "{{column}}" + {0},
                    "UpdatedAtUtc" = CASE WHEN "UpdatedAtUtc" < {1} THEN {1} ELSE "UpdatedAtUtc" END
                WHERE "Id" = {2} AND "Status" = 'Active';
                """
            : $$"""
                UPDATE "StudySessions"
                SET "{{column}}" = "{{column}}" + {0},
                    "ConfidenceAvg" = CASE
                        WHEN ("FocusedSeconds" + "DistractedSeconds" + "AbsentSeconds" + "DrowsySeconds") <= 0
                            THEN {1}
                        ELSE (
                            COALESCE("ConfidenceAvg", 0) * ("FocusedSeconds" + "DistractedSeconds" + "AbsentSeconds" + "DrowsySeconds")
                            + {1} * {0})
                            / (("FocusedSeconds" + "DistractedSeconds" + "AbsentSeconds" + "DrowsySeconds") + {0})
                    END,
                    "UpdatedAtUtc" = CASE WHEN "UpdatedAtUtc" < {2} THEN {2} ELSE "UpdatedAtUtc" END
                WHERE "Id" = {3} AND "Status" = 'Active';
                """;

        // Raw SQL bypasses the UTC value converter, so the timestamp must use
        // the same storage format for the lexicographic comparison to work.
        var storedNow = UtcDateTimeConverter.ToStorage(nowUtc);
        var parameters = confidence is null
            ? new object[] { seconds, storedNow, sessionId.Value }
            : new object[] { seconds, confidence.Value, storedNow, sessionId.Value };

        await using var context = _contextFactory.CreateDbContext();

        var affected = await context.Database.ExecuteSqlRawAsync(sql, parameters, cancellationToken);

        return affected > 0;
    }
}
