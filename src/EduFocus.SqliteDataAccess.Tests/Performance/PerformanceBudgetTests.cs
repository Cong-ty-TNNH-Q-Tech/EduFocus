using System.Diagnostics;
using System.Globalization;
using EduFocus.Domain.Entities;
using EduFocus.Domain.Reports;
using EduFocus.Infrastructure.Logging;
using EduFocus.SqliteDataAccess.Repositories;
using EduFocus.SqliteDataAccess.Tests.TestSupport;
using EduFocus.UseCases.Reports;
using EduFocus.UseCases.Retention;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using Xunit.Abstractions;

namespace EduFocus.SqliteDataAccess.Tests.Performance;

/// <summary>
/// Reproducible budgets from BA Document §9.3 and database_schema §15:
/// startup, a synthetic 30-day dataset, month reports, retention and the
/// projected one-year database size. The eight-hour soak needs a real desktop
/// session and is tracked separately in docs/performance-report.md.
/// </summary>
public sealed class PerformanceBudgetTests
{
    private static readonly DateTime Now = new(2026, 10, 30, 12, 0, 0, DateTimeKind.Utc);
    private static readonly DateOnly FirstDay = new(2026, 10, 1);
    private const int Days = 30;
    private const int SessionsPerDay = 3;
    private const int SamplesPerSession = 240;
    private const int AttentionEventsPerSession = 4;
    private const int BlockedAttemptsPerSession = 3;
    private const int AuditEntriesPerDay = 5;
    private const long ThirtyDayBudgetBytes = 16L * 1024 * 1024;
    private const long YearlyBudgetBytes = 50L * 1024 * 1024;
    private const string UtcFormat = "yyyy-MM-dd'T'HH:mm:ss.fff'Z'";
    private const string DateFormat = "yyyy-MM-dd";

    private readonly ITestOutputHelper _output;

    public PerformanceBudgetTests(ITestOutputHelper output) => _output = output;

    [Fact]
    public async Task Database_initialization_stays_within_the_startup_budget()
    {
        await using var db = new TempDatabase();
        var initializer = db.CreateInitializer();

        var fresh = Stopwatch.StartNew();
        var created = await initializer.InitializeAsync();
        fresh.Stop();

        var existing = Stopwatch.StartNew();
        var reopened = await initializer.InitializeAsync();
        existing.Stop();

        created.WasCreated.Should().BeTrue();
        created.AppliedMigrations.Should().HaveCount(4);
        reopened.WasCreated.Should().BeFalse();
        reopened.AppliedMigrations.Should().BeEmpty();

        _output.WriteLine(
            $"Database initialization: fresh {fresh.Elapsed.TotalMilliseconds:F0} ms, " +
            $"existing {existing.Elapsed.TotalMilliseconds:F0} ms.");

        fresh.Elapsed.Should().BeLessThan(
            TimeSpan.FromSeconds(5),
            "the BA startup budget is under 3 s on SSD; the test ceiling keeps CI headroom");
        existing.Elapsed.Should().BeLessThan(TimeSpan.FromSeconds(2));
    }

    [Fact]
    public async Task Thirty_day_dataset_meets_storage_and_report_budgets()
    {
        await using var db = new TempDatabase();
        await db.CreateInitializer().InitializeAsync();
        await SeedSettingsAsync(db);

        var seed = Stopwatch.StartNew();
        SeedThirtyDays(db);
        seed.Stop();

        ClearPoolsAndCheckpoint(db);
        var sizeBytes = DatabaseBytes(db);

        _output.WriteLine(
            $"Thirty-day seed: {seed.Elapsed.TotalMilliseconds:F0} ms, " +
            $"database {Megabytes(sizeBytes):F2} MB.");

        sizeBytes.Should().BeLessThan(
            ThirtyDayBudgetBytes,
            "database_schema §15 estimates well under 50 MB after a full year");

        var report = new ReportService(
            new SqliteStudySessionRepository(db.Options),
            new SqliteActivitySampleRepository(db.Options),
            new SqliteAttentionEventRepository(db.Options),
            TimeProvider.System);

        var period = ReportPeriod.Range(FirstDay, FirstDay.AddDays(Days - 1));
        await report.GetAsync(period);

        var runs = new List<TimeSpan>();
        StudyReport? last = null;
        for (var index = 0; index < 3; index++)
        {
            var stopwatch = Stopwatch.StartNew();
            last = await report.GetAsync(period);
            stopwatch.Stop();
            runs.Add(stopwatch.Elapsed);
        }

        var sorted = runs.OrderBy(value => value).ToArray();
        var median = sorted[sorted.Length / 2];

        _output.WriteLine(
            "Month report: " +
            $"median {median.TotalMilliseconds:F0} ms, " +
            $"runs {string.Join("/", runs.Select(value => value.TotalMilliseconds.ToString("F0", CultureInfo.InvariantCulture)))} ms.");

        last.Should().NotBeNull();
        last!.SessionCount.Should().Be(Days * SessionsPerDay);
        last.Days.Should().HaveCount(Days);
        last.Sessions.Should().HaveCount(ReportService.MaxDisplayedSessions);
        last.SessionsTruncated.Should().BeTrue();
        last.Timeline.Should().NotBeEmpty();
        last.TopActivities.Should().NotBeEmpty()
            .And.HaveCountLessThanOrEqualTo(ReportService.TopActivityLimit);
        last.AttentionEvents.Should().HaveCount(ReportService.MaxAttentionEvents);
        last.AttentionEventsTruncated.Should().BeTrue();
        last.TimelineTruncated.Should().BeFalse();

        median.Should().BeLessThan(
            TimeSpan.FromSeconds(2),
            "opening a month report must not block the UI; BA §9.3 targets sub-second interaction");
    }

    [Fact]
    public async Task Retention_meets_the_time_budget_and_the_yearly_database_stays_under_fifty_megabytes()
    {
        await using var db = new TempDatabase();
        await db.CreateInitializer().InitializeAsync();
        await SeedSettingsAsync(db);

        SeedThirtyDays(db);
        SeedExpiredRows(db);
        SeedRemainingYear(db);
        ClearPoolsAndCheckpoint(db);

        var beforeBytes = DatabaseBytes(db);

        var retention = new RetentionService(
            new SqliteRetentionRepository(db.Options),
            new SqliteAppSettingsRepository(db.Options),
            new SqliteAuditLogRepository(db.Options),
            new CorrelationContext(),
            db.Logger,
            new AdjustedTimeProvider(Now),
            new RetentionOptions());

        var stopwatch = Stopwatch.StartNew();
        var result = await retention.RunOnceAsync();
        stopwatch.Stop();

        ClearPoolsAndCheckpoint(db);
        var afterBytes = DatabaseBytes(db);

        _output.WriteLine(
            $"Retention: {stopwatch.Elapsed.TotalMilliseconds:F0} ms; " +
            $"database before {Megabytes(beforeBytes):F2} MB, after {Megabytes(afterBytes):F2} MB.");

        result.Completed.Should().BeTrue();
        result.Deleted.ActivitySamples.Should().Be(2_001);
        result.Deleted.AttentionEvents.Should().Be(401);
        result.Deleted.BlockedAttempts.Should().Be(300);
        result.Deleted.Sessions.Should().Be(1);
        result.Deleted.AuditLogs.Should().Be(400);

        Count(db, "ActivitySamples").Should().Be(21_600);
        Count(db, "AttentionEvents").Should().Be(1_020);
        Count(db, "BlockedAttempts").Should().Be(765);
        Count(db, "StudySessions").Should().Be(1_007);
        Count(db, "AuditLogs").Should().Be(1_801);

        stopwatch.Elapsed.Should().BeLessThan(
            TimeSpan.FromSeconds(10),
            "retention runs in short batches so the UI and tracking stay responsive");
        afterBytes.Should().BeLessThan(
            YearlyBudgetBytes,
            "database_schema §15 estimates under 50 MB after one year with default retention");
    }

    private static async Task SeedSettingsAsync(TempDatabase db)
    {
        var repository = new SqliteAppSettingsRepository(db.Options);
        await repository.AddAsync(AppSettings.CreateDefault(Now));
    }

    private static void SeedThirtyDays(TempDatabase db)
    {
        using var connection = db.OpenConnectionWithPragmas();
        using var transaction = connection.BeginTransaction();
        using var sessions = SessionCommand(connection, transaction);
        using var samples = SampleCommand(connection, transaction);
        using var events = AttentionCommand(connection, transaction);
        using var attempts = BlockedAttemptCommand(connection, transaction);
        using var audits = AuditCommand(connection, transaction);

        for (var day = 0; day < Days; day++)
        {
            var date = FirstDay.AddDays(day);

            foreach (var hour in new[] { 8, 13, 19 })
            {
                var started = date.ToDateTime(new TimeOnly(hour, 0), DateTimeKind.Utc);
                var sessionId = InsertSession(sessions, started, started.AddHours(1), date);

                for (var sample = 0; sample < SamplesPerSession; sample++)
                {
                    var category = (sample % 6) switch
                    {
                        4 => "Neutral",
                        5 => "Distraction",
                        _ => "Study"
                    };
                    InsertSample(samples, sessionId, started.AddSeconds(sample * 15), category);
                }

                var states = new[] { "Focused", "Focused", "Distracted", "Absent" };
                for (var index = 0; index < AttentionEventsPerSession; index++)
                {
                    var eventStart = started.AddMinutes(index * 15);
                    InsertAttentionEvent(events, sessionId, states[index], eventStart, eventStart.AddMinutes(15));
                }

                for (var index = 0; index < BlockedAttemptsPerSession; index++)
                    InsertBlockedAttempt(attempts, sessionId, started.AddMinutes(10 + index));
            }

            for (var index = 0; index < AuditEntriesPerDay; index++)
                InsertAudit(audits, date.ToDateTime(new TimeOnly(8, 0), DateTimeKind.Utc).AddHours(index));
        }

        transaction.Commit();
    }

    private static void SeedExpiredRows(TempDatabase db)
    {
        using var connection = db.OpenConnectionWithPragmas();
        using var transaction = connection.BeginTransaction();
        using var sessions = SessionCommand(connection, transaction);
        using var samples = SampleCommand(connection, transaction);
        using var events = AttentionCommand(connection, transaction);
        using var attempts = BlockedAttemptCommand(connection, transaction);
        using var audits = AuditCommand(connection, transaction);

        var oldDate = Now.Date.AddDays(-400);
        var oldStart = oldDate.AddHours(8);
        var oldSession = InsertSession(sessions, oldStart, oldStart.AddHours(1), DateOnly.FromDateTime(oldDate));
        InsertSample(samples, oldSession, oldStart.AddMinutes(5), "Study");
        InsertAttentionEvent(events, oldSession, "Focused", oldStart, oldStart.AddMinutes(15));

        var detailDate = Now.Date.AddDays(-40);
        var detailStart = detailDate.AddHours(0);
        var detailSession = InsertSession(
            sessions,
            detailStart,
            detailStart.AddHours(9),
            DateOnly.FromDateTime(detailDate));
        for (var index = 0; index < 2_000; index++)
            InsertSample(samples, detailSession, detailStart.AddSeconds(index * 15), "Study");

        var eventDate = Now.Date.AddDays(-100);
        var eventStart = eventDate.AddHours(8);
        var eventSession = InsertSession(
            sessions,
            eventStart,
            eventStart.AddMinutes(400),
            DateOnly.FromDateTime(eventDate));
        for (var index = 0; index < 400; index++)
        {
            var started = eventStart.AddMinutes(index);
            InsertAttentionEvent(events, eventSession, "Focused", started, started.AddMinutes(1));
        }

        for (var index = 0; index < 300; index++)
            InsertBlockedAttempt(attempts, eventSession, eventStart.AddSeconds(index * 10));

        for (var index = 0; index < 400; index++)
            InsertAudit(audits, Now.AddDays(-400).AddMinutes(index));

        transaction.Commit();
    }

    private static void SeedRemainingYear(TempDatabase db)
    {
        using var connection = db.OpenConnectionWithPragmas();
        using var transaction = connection.BeginTransaction();
        using var sessions = SessionCommand(connection, transaction);
        using var events = AttentionCommand(connection, transaction);
        using var attempts = BlockedAttemptCommand(connection, transaction);
        using var audits = AuditCommand(connection, transaction);

        for (var day = 31; day <= 360; day++)
        {
            var date = DateOnly.FromDateTime(Now.Date.AddDays(-day));
            var firstSession = Guid.Empty;

            if (day <= 335)
            {
                foreach (var hour in new[] { 8, 13, 19 })
                {
                    var started = date.ToDateTime(new TimeOnly(hour, 0), DateTimeKind.Utc);
                    var sessionId = InsertSession(sessions, started, started.AddHours(1), date);

                    if (firstSession == Guid.Empty)
                        firstSession = sessionId;
                }
            }

            if (day <= 85)
            {
                var started = date.ToDateTime(new TimeOnly(8, 0), DateTimeKind.Utc);
                for (var index = 0; index < 12; index++)
                {
                    var eventStart = started.AddMinutes(index * 5);
                    InsertAttentionEvent(events, firstSession, "Focused", eventStart, eventStart.AddMinutes(5));
                }

                for (var index = 0; index < 9; index++)
                    InsertBlockedAttempt(attempts, firstSession, started.AddMinutes(60 + index));
            }

            if (day <= 360)
            {
                var started = date.ToDateTime(new TimeOnly(8, 0), DateTimeKind.Utc);
                for (var index = 0; index < AuditEntriesPerDay; index++)
                    InsertAudit(audits, started.AddHours(index));
            }
        }

        transaction.Commit();
    }

    private static SqliteCommand SessionCommand(SqliteConnection connection, SqliteTransaction transaction)
    {
        var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            INSERT INTO StudySessions
                (Id, StartedAtUtc, EndedAtUtc, LastHeartbeatAtUtc, StartLocalDate, Status, Trigger,
                 MonitoringEnabled, ActiveSeconds, FocusedSeconds, DistractedSeconds, AbsentSeconds,
                 DrowsySeconds, CameraUnavailableSeconds, IdleSeconds, StudySeconds, DistractionSeconds,
                 NeutralSeconds, OnlineClassSeconds, MonitoringEligibleSeconds, ConfidenceAvg,
                 BlockedAttemptCount, Note, CreatedAtUtc, UpdatedAtUtc)
            VALUES
                ($id, $started, $ended, $ended, $localDate, 'Completed', 'Manual', 1, 3600, 3000, 300,
                 200, 100, 0, 0, 2400, 600, 600, 0, 3600, 0.82, 3, NULL, $started, $ended);
            """;
        command.Parameters.Add("$id", SqliteType.Text);
        command.Parameters.Add("$started", SqliteType.Text);
        command.Parameters.Add("$ended", SqliteType.Text);
        command.Parameters.Add("$localDate", SqliteType.Text);
        return command;
    }

    private static SqliteCommand SampleCommand(SqliteConnection connection, SqliteTransaction transaction)
    {
        var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            INSERT INTO ActivitySamples
                (Id, SessionId, SampledAtUtc, DurationSeconds, AppName, ProcessName, WindowTitle, Url,
                 Domain, UrlSource, Category, IsForeground, IsOnlineClass, IdleSeconds, CreatedAtUtc)
            VALUES
                ($id, $sessionId, $sampledAt, 15, 'Google Chrome', 'chrome', NULL, NULL, $domain,
                 'None', $category, 1, 0, 0, $sampledAt);
            """;
        command.Parameters.Add("$id", SqliteType.Text);
        command.Parameters.Add("$sessionId", SqliteType.Text);
        command.Parameters.Add("$sampledAt", SqliteType.Text);
        command.Parameters.Add("$category", SqliteType.Text);
        command.Parameters.Add("$domain", SqliteType.Text);
        return command;
    }

    private static SqliteCommand AttentionCommand(SqliteConnection connection, SqliteTransaction transaction)
    {
        var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            INSERT INTO AttentionEvents
                (Id, SessionId, State, StartedAtUtc, EndedAtUtc, DurationSeconds, Confidence, AppName,
                 Domain, Note, CreatedAtUtc)
            VALUES
                ($id, $sessionId, $state, $started, $ended, $duration, 0.82, 'Google Chrome', NULL, NULL,
                 $started);
            """;
        command.Parameters.Add("$id", SqliteType.Text);
        command.Parameters.Add("$sessionId", SqliteType.Text);
        command.Parameters.Add("$state", SqliteType.Text);
        command.Parameters.Add("$started", SqliteType.Text);
        command.Parameters.Add("$ended", SqliteType.Text);
        command.Parameters.Add("$duration", SqliteType.Integer);
        return command;
    }

    private static SqliteCommand BlockedAttemptCommand(SqliteConnection connection, SqliteTransaction transaction)
    {
        var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            INSERT INTO BlockedAttempts
                (Id, SessionId, OccurredAtUtc, TargetType, TargetValue, Domain, Url, ProcessName,
                 WindowTitle, ObservedVia, CreatedAtUtc)
            VALUES
                ($id, $sessionId, $occurredAt, 'Domain', 'youtube.com', 'youtube.com', NULL, NULL, NULL,
                 'BlockPage', $occurredAt);
            """;
        command.Parameters.Add("$id", SqliteType.Text);
        command.Parameters.Add("$sessionId", SqliteType.Text);
        command.Parameters.Add("$occurredAt", SqliteType.Text);
        return command;
    }

    private static SqliteCommand AuditCommand(SqliteConnection connection, SqliteTransaction transaction)
    {
        var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            INSERT INTO AuditLogs
                (Id, OccurredAtUtc, Actor, EventType, Description, MetadataJson, CorrelationId, CreatedAtUtc)
            VALUES
                ($id, $occurredAt, 'System', 'ProtectionOn', 'Synthetic performance row.', NULL, NULL,
                 $occurredAt);
            """;
        command.Parameters.Add("$id", SqliteType.Text);
        command.Parameters.Add("$occurredAt", SqliteType.Text);
        return command;
    }

    private static Guid InsertSession(
        SqliteCommand command,
        DateTime startedAtUtc,
        DateTime endedAtUtc,
        DateOnly localDate)
    {
        var id = Guid.NewGuid();
        command.Parameters["$id"].Value = StorageId(id);
        command.Parameters["$started"].Value = Format(startedAtUtc);
        command.Parameters["$ended"].Value = Format(endedAtUtc);
        command.Parameters["$localDate"].Value = localDate.ToString(DateFormat, CultureInfo.InvariantCulture);
        command.ExecuteNonQuery();
        return id;
    }

    private static void InsertSample(SqliteCommand command, Guid sessionId, DateTime sampledAtUtc, string category)
    {
        command.Parameters["$id"].Value = StorageId(Guid.NewGuid());
        command.Parameters["$sessionId"].Value = StorageId(sessionId);
        command.Parameters["$sampledAt"].Value = Format(sampledAtUtc);
        command.Parameters["$category"].Value = category;
        command.Parameters["$domain"].Value = category switch
        {
            "Distraction" => "youtube.com",
            "Neutral" => "docs.google.com",
            _ => "classroom.google.com"
        };
        command.ExecuteNonQuery();
    }

    private static void InsertAttentionEvent(
        SqliteCommand command,
        Guid sessionId,
        string state,
        DateTime startedAtUtc,
        DateTime endedAtUtc)
    {
        command.Parameters["$id"].Value = StorageId(Guid.NewGuid());
        command.Parameters["$sessionId"].Value = StorageId(sessionId);
        command.Parameters["$state"].Value = state;
        command.Parameters["$started"].Value = Format(startedAtUtc);
        command.Parameters["$ended"].Value = Format(endedAtUtc);
        command.Parameters["$duration"].Value = (int)(endedAtUtc - startedAtUtc).TotalSeconds;
        command.ExecuteNonQuery();
    }

    private static void InsertBlockedAttempt(SqliteCommand command, Guid sessionId, DateTime occurredAtUtc)
    {
        command.Parameters["$id"].Value = StorageId(Guid.NewGuid());
        command.Parameters["$sessionId"].Value = StorageId(sessionId);
        command.Parameters["$occurredAt"].Value = Format(occurredAtUtc);
        command.ExecuteNonQuery();
    }

    private static void InsertAudit(SqliteCommand command, DateTime occurredAtUtc)
    {
        command.Parameters["$id"].Value = StorageId(Guid.NewGuid());
        command.Parameters["$occurredAt"].Value = Format(occurredAtUtc);
        command.ExecuteNonQuery();
    }

    // Microsoft.Data.Sqlite binds Guid parameters as uppercase TEXT, so raw
    // seeds must use the same representation for joins to match EF queries.
    private static string StorageId(Guid id) => id.ToString().ToUpperInvariant();

    private static string Format(DateTime value) =>
        value.ToUniversalTime().ToString(UtcFormat, CultureInfo.InvariantCulture);

    private static void ClearPoolsAndCheckpoint(TempDatabase db)
    {
        SqliteConnection.ClearAllPools();
        using var connection = db.OpenConnection();
        TempDatabase.ExecuteNonQuery(connection, "PRAGMA wal_checkpoint(TRUNCATE);");
    }

    private static long DatabaseBytes(TempDatabase db) => new FileInfo(db.DatabasePath).Length;

    private static long Count(TempDatabase db, string table)
    {
        using var connection = db.OpenConnection();
        return Convert.ToInt64(TempDatabase.Scalar(connection, $"SELECT COUNT(*) FROM \"{table}\";"), CultureInfo.InvariantCulture);
    }

    private static double Megabytes(long bytes) => bytes / 1024.0 / 1024.0;

    private sealed class AdjustedTimeProvider(DateTime utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => new(utcNow);
    }
}
