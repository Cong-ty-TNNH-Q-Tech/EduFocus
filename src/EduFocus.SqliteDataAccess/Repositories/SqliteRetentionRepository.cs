using EduFocus.Ports.Outbound.Persistence;
using EduFocus.SqliteDataAccess.Converters;
using EduFocus.SqliteDataAccess.Persistence;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace EduFocus.SqliteDataAccess.Repositories;

/// <summary>
/// Batched deletes for database retention (database schema §14). Raw SQL is
/// used because SQLite rejects DELETE ... LIMIT unless compiled with a
/// non-default option, so each batch selects its own rowids in a subquery.
/// Identifiers are fixed constants; values are always parameters.
/// </summary>
public sealed class SqliteRetentionRepository : IRetentionRepository
{
    private const string ActivitySamplesSql = """
        DELETE FROM "ActivitySamples"
        WHERE "Id" IN (
            SELECT "Id" FROM "ActivitySamples"
            WHERE "SampledAtUtc" < @cutoff
              AND "SessionId" NOT IN (SELECT "Id" FROM "StudySessions" WHERE "Status" = 'Active')
            LIMIT @batch);
        """;

    private const string AttentionEventsSql = """
        DELETE FROM "AttentionEvents"
        WHERE "Id" IN (
            SELECT "Id" FROM "AttentionEvents"
            WHERE "StartedAtUtc" < @cutoff
              AND "EndedAtUtc" IS NOT NULL
              AND "SessionId" NOT IN (SELECT "Id" FROM "StudySessions" WHERE "Status" = 'Active')
            LIMIT @batch);
        """;

    private const string BlockedAttemptsSql = """
        DELETE FROM "BlockedAttempts"
        WHERE "Id" IN (
            SELECT "Id" FROM "BlockedAttempts"
            WHERE "OccurredAtUtc" < @cutoff
              AND ("SessionId" IS NULL OR "SessionId" NOT IN (SELECT "Id" FROM "StudySessions" WHERE "Status" = 'Active'))
            LIMIT @batch);
        """;

    private const string StudySessionsSql = """
        DELETE FROM "StudySessions"
        WHERE "Id" IN (
            SELECT "Id" FROM "StudySessions"
            WHERE "Status" <> 'Active'
              AND "EndedAtUtc" IS NOT NULL
              AND "EndedAtUtc" < @cutoff
            LIMIT @batch);
        """;

    private const string AuditLogsSql = """
        DELETE FROM "AuditLogs"
        WHERE "Id" IN (
            SELECT "Id" FROM "AuditLogs"
            WHERE "OccurredAtUtc" < @cutoff
            LIMIT @batch);
        """;

    private const string VacuumSql = "PRAGMA incremental_vacuum;";

    private readonly SqliteDbContextFactory _contextFactory;

    public SqliteRetentionRepository(SqliteDbContextFactory contextFactory)
    {
        ArgumentNullException.ThrowIfNull(contextFactory);
        _contextFactory = contextFactory;
    }

    public Task<int> DeleteActivitySamplesBeforeAsync(
        DateTime cutoffUtc,
        int batchSize,
        CancellationToken cancellationToken = default) =>
        DeleteBatchAsync(ActivitySamplesSql, cutoffUtc, batchSize, cancellationToken);

    public Task<int> DeleteAttentionEventsBeforeAsync(
        DateTime cutoffUtc,
        int batchSize,
        CancellationToken cancellationToken = default) =>
        DeleteBatchAsync(AttentionEventsSql, cutoffUtc, batchSize, cancellationToken);

    public Task<int> DeleteBlockedAttemptsBeforeAsync(
        DateTime cutoffUtc,
        int batchSize,
        CancellationToken cancellationToken = default) =>
        DeleteBatchAsync(BlockedAttemptsSql, cutoffUtc, batchSize, cancellationToken);

    public Task<int> DeleteFinishedSessionsBeforeAsync(
        DateTime cutoffUtc,
        int batchSize,
        CancellationToken cancellationToken = default) =>
        DeleteBatchAsync(StudySessionsSql, cutoffUtc, batchSize, cancellationToken);

    public Task<int> DeleteAuditLogsBeforeAsync(
        DateTime cutoffUtc,
        int batchSize,
        CancellationToken cancellationToken = default) =>
        DeleteBatchAsync(AuditLogsSql, cutoffUtc, batchSize, cancellationToken);

    public async Task VacuumAsync(CancellationToken cancellationToken = default)
    {
        await using var context = _contextFactory.CreateDbContext();
        await context.Database.ExecuteSqlRawAsync(VacuumSql, cancellationToken);
    }

    private async Task<int> DeleteBatchAsync(
        string sql,
        DateTime cutoffUtc,
        int batchSize,
        CancellationToken cancellationToken)
    {
        if (cutoffUtc.Kind != DateTimeKind.Utc)
            throw new ArgumentException("The cutoff must be UTC.", nameof(cutoffUtc));
        if (batchSize <= 0)
            throw new ArgumentOutOfRangeException(nameof(batchSize), batchSize, "Batch size must be positive.");

        await using var context = _contextFactory.CreateDbContext();

        return await context.Database.ExecuteSqlRawAsync(
            sql,
            new object[]
            {
                new SqliteParameter("@cutoff", UtcDateTimeConverter.ToStorage(cutoffUtc)),
                new SqliteParameter("@batch", batchSize)
            },
            cancellationToken);
    }
}
