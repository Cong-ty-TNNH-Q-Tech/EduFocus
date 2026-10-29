using System.Data;
using System.Globalization;
using EduFocus.Domain.Enums;
using EduFocus.Ports.DTOs;
using EduFocus.Ports.Outbound.Persistence;
using EduFocus.SqliteDataAccess.Persistence;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace EduFocus.SqliteDataAccess.Repositories;

/// <summary>
/// Storage inspection and delete-all batches for the privacy screen (UC11,
/// issue #38). Raw SQL is used because SQLite rejects DELETE ... LIMIT unless
/// compiled with a non-default option. Identifiers are fixed constants and
/// every value is parameterized. Active sessions are excluded at every step.
/// </summary>
public sealed class SqlitePrivacyDataRepository : IPrivacyDataRepository
{
    private const string ActivitySamplesSql = """
        DELETE FROM "ActivitySamples"
        WHERE "Id" IN (
            SELECT "Id" FROM "ActivitySamples"
            WHERE "SessionId" NOT IN (SELECT "Id" FROM "StudySessions" WHERE "Status" = 'Active')
            LIMIT @batch);
        """;

    private const string AttentionEventsSql = """
        DELETE FROM "AttentionEvents"
        WHERE "Id" IN (
            SELECT "Id" FROM "AttentionEvents"
            WHERE "SessionId" NOT IN (SELECT "Id" FROM "StudySessions" WHERE "Status" = 'Active')
            LIMIT @batch);
        """;

    private const string BlockedAttemptsSql = """
        DELETE FROM "BlockedAttempts"
        WHERE "Id" IN (
            SELECT "Id" FROM "BlockedAttempts"
            LIMIT @batch);
        """;

    private const string FinishedSessionsSql = """
        DELETE FROM "StudySessions"
        WHERE "Id" IN (
            SELECT "Id" FROM "StudySessions"
            WHERE "Status" <> 'Active'
            LIMIT @batch);
        """;

    private const string DatabaseSizeSql = """
        SELECT (SELECT page_count FROM pragma_page_count()) *
               (SELECT page_size FROM pragma_page_size());
        """;

    private const string VacuumSql = "VACUUM;";

    private readonly DbContextOptions<EduFocusDbContext> _dbContextOptions;

    public SqlitePrivacyDataRepository(EduFocusDatabaseOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        _dbContextOptions = new DbContextOptionsBuilder<EduFocusDbContext>()
            .UseSqlite(options.ConnectionString)
            .AddInterceptors(new SqlitePragmaInterceptor())
            .Options;
    }

    public async Task<PrivacyDataCounts> GetCountsAsync(CancellationToken cancellationToken = default)
    {
        await using var context = new EduFocusDbContext(_dbContextOptions);

        var sessions = await context.StudySessions.CountAsync(cancellationToken).ConfigureAwait(false);
        var activeSessions = await context.StudySessions
            .CountAsync(x => x.Status == StudySessionStatus.Active, cancellationToken)
            .ConfigureAwait(false);
        var samples = await context.ActivitySamples.CountAsync(cancellationToken).ConfigureAwait(false);
        var events = await context.AttentionEvents.CountAsync(cancellationToken).ConfigureAwait(false);
        var attempts = await context.BlockedAttempts.CountAsync(cancellationToken).ConfigureAwait(false);

        return new PrivacyDataCounts(sessions, activeSessions, samples, events, attempts);
    }

    public async Task<long> GetDatabaseSizeBytesAsync(CancellationToken cancellationToken = default)
    {
        await using var context = new EduFocusDbContext(_dbContextOptions);
        await context.Database.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            await using var command = context.Database.GetDbConnection().CreateCommand();
            command.CommandText = DatabaseSizeSql;

            var value = await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
            return value is null or DBNull
                ? 0
                : Convert.ToInt64(value, CultureInfo.InvariantCulture);
        }
        finally
        {
            await context.Database.CloseConnectionAsync().ConfigureAwait(false);
        }
    }

    public Task<int> DeleteActivitySamplesBatchAsync(
        int batchSize,
        CancellationToken cancellationToken = default) =>
        DeleteBatchAsync(ActivitySamplesSql, batchSize, cancellationToken);

    public Task<int> DeleteAttentionEventsBatchAsync(
        int batchSize,
        CancellationToken cancellationToken = default) =>
        DeleteBatchAsync(AttentionEventsSql, batchSize, cancellationToken);

    public Task<int> DeleteBlockedAttemptsBatchAsync(
        int batchSize,
        CancellationToken cancellationToken = default) =>
        DeleteBatchAsync(BlockedAttemptsSql, batchSize, cancellationToken);

    public Task<int> DeleteFinishedSessionsBatchAsync(
        int batchSize,
        CancellationToken cancellationToken = default) =>
        DeleteBatchAsync(FinishedSessionsSql, batchSize, cancellationToken);

    public async Task VacuumAsync(CancellationToken cancellationToken = default)
    {
        await using var context = new EduFocusDbContext(_dbContextOptions);
        await context.Database.ExecuteSqlRawAsync(VacuumSql, cancellationToken).ConfigureAwait(false);
    }

    private async Task<int> DeleteBatchAsync(
        string sql,
        int batchSize,
        CancellationToken cancellationToken)
    {
        if (batchSize <= 0)
            throw new ArgumentOutOfRangeException(nameof(batchSize), batchSize, "Batch size must be positive.");

        await using var context = new EduFocusDbContext(_dbContextOptions);

        return await context.Database.ExecuteSqlRawAsync(
            sql,
            new object[] { new SqliteParameter("@batch", batchSize) },
            cancellationToken).ConfigureAwait(false);
    }
}
