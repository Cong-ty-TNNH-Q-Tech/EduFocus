using EduFocus.Domain.Attention;
using EduFocus.Domain.Entities;
using EduFocus.Domain.Enums;
using EduFocus.Domain.Reports;
using EduFocus.Domain.ValueObjects;
using EduFocus.Ports.DTOs;
using EduFocus.Ports.Inbound;
using EduFocus.Ports.Outbound.Persistence;
using EduFocus.Ports.Outbound.Services;
using EduFocus.Service.Control;
using EduFocus.SqliteDataAccess.Persistence;

namespace EduFocus.Service.Tests.TestSupport;

internal sealed class RecordingLogger : IAppLogger
{
    public List<string> Infos { get; } = [];

    public List<string> Warnings { get; } = [];

    public List<string> Errors { get; } = [];

    public void Info(string category, string message) => Infos.Add($"{category}: {message}");

    public void Warning(string category, string message) => Warnings.Add($"{category}: {message}");

    public void Error(string category, string message, Exception? exception = null) =>
        Errors.Add($"{category}: {message}");
}

internal sealed class TestCorrelationContext : ICorrelationContext
{
    public Guid? Current { get; set; }

    public IDisposable BeginScope(Guid correlationId)
    {
        var previous = Current;
        Current = correlationId;
        return new Scope(this, previous);
    }

    private sealed class Scope : IDisposable
    {
        private readonly TestCorrelationContext _owner;
        private readonly Guid? _previous;

        public Scope(TestCorrelationContext owner, Guid? previous)
        {
            _owner = owner;
            _previous = previous;
        }

        public void Dispose() => _owner.Current = _previous;
    }
}

internal sealed class RecordingProtectionUseCase : IProtectionUseCase
{
    public ProtectionStatus Status { get; set; } = new(
        true,
        new DateTime(2026, 10, 29, 8, 0, 0, DateTimeKind.Utc),
        true);

    public List<(string Command, bool Authorized)> Commands { get; } = [];

    public ServiceRequestAuthorizer? Observer { get; set; }

    public Func<string, ProtectionOperationResult>? Handler { get; set; }

    public Exception? Failure { get; set; }

    public int StatusCalls { get; private set; }

    public int ReconcileCount { get; private set; }

    public int DisableCount { get; private set; }

    public Task<ProtectionStatus> GetStatusAsync(CancellationToken cancellationToken = default)
    {
        StatusCalls++;
        return Task.FromResult(Status);
    }

    public Task<ProtectionOperationResult> EnableAsync(CancellationToken cancellationToken = default) =>
        Run("enable");

    public Task<ProtectionOperationResult> DisableAsync(CancellationToken cancellationToken = default)
    {
        DisableCount++;
        return Run("disable");
    }

    public Task<ProtectionOperationResult> DisableForExitAsync(CancellationToken cancellationToken = default) =>
        Run("disable-for-exit");

    public Task<ProtectionOperationResult> ReconcileAsync(CancellationToken cancellationToken = default)
    {
        ReconcileCount++;
        return Run("reconcile");
    }

    public Task<ProtectionOperationResult> EnableForScheduleAsync(CancellationToken cancellationToken = default) =>
        Run("enable-for-schedule");

    public Task<ProtectionOperationResult> DisableForScheduleAsync(CancellationToken cancellationToken = default) =>
        Run("disable-for-schedule");

    private Task<ProtectionOperationResult> Run(string command)
    {
        Commands.Add((command, Observer?.IsAuthenticated ?? false));

        if (Failure is not null)
            return Task.FromException<ProtectionOperationResult>(Failure);

        return Task.FromResult(
            Handler?.Invoke(command)
            ?? new ProtectionOperationResult(Status, Changed: true, Warnings: []));
    }
}

internal sealed class FakeBlockRuleSeedUseCase : IBlockRuleSeedUseCase
{
    public int Calls { get; private set; }

    public Task<BlockRuleSeedResult> EnsureSeededAsync(CancellationToken cancellationToken = default)
    {
        Calls++;
        return Task.FromResult(new BlockRuleSeedResult(1, 0, 0));
    }
}

internal sealed class FakeScheduleUseCase : IScheduleUseCase
{
    public int EvaluateCount { get; private set; }

    public Task<ScheduleOverview> GetOverviewAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult(Overview());

    public Task<ScheduleOperationResult> AddWindowAsync(
        ScheduleWindowDraft draft,
        CancellationToken cancellationToken = default) =>
        throw new NotSupportedException();

    public Task<ScheduleOperationResult> UpdateWindowAsync(
        Guid id,
        ScheduleWindowDraft draft,
        CancellationToken cancellationToken = default) =>
        throw new NotSupportedException();

    public Task<ScheduleOperationResult> SetWindowEnabledAsync(
        Guid id,
        bool isEnabled,
        CancellationToken cancellationToken = default) =>
        throw new NotSupportedException();

    public Task<ScheduleOperationResult> DeleteWindowAsync(
        Guid id,
        CancellationToken cancellationToken = default) =>
        throw new NotSupportedException();

    public Task<ScheduleOperationResult> SetScheduleEnabledAsync(
        bool isEnabled,
        CancellationToken cancellationToken = default) =>
        throw new NotSupportedException();

    public Task<ScheduleEvaluationResult> EvaluateAsync(CancellationToken cancellationToken = default)
    {
        EvaluateCount++;
        return Task.FromResult(new ScheduleEvaluationResult(
            ProtectionChanged: false,
            ClockJumpDetected: false,
            Overview(),
            Warnings: []));
    }

    private static ScheduleOverview Overview() =>
        new(
            ScheduleEnabled: true,
            Windows: [],
            ActiveWindow: null,
            OverrideUntilUtc: null,
            IsOverridden: false,
            ProtectionEnabled: true,
            ProtectionSource.None);
}

internal sealed class FakeRetentionUseCase : IRetentionUseCase
{
    public int Calls { get; private set; }

    public Task<RetentionRunResult> RunOnceAsync(CancellationToken cancellationToken = default)
    {
        Calls++;
        return Task.FromResult(new RetentionRunResult(
            DateTime.UtcNow,
            RetentionCounts.Empty,
            Completed: true,
            Failures: []));
    }
}

internal sealed class FakeStudySessionHeartbeatUseCase : IStudySessionHeartbeatUseCase
{
    public bool IsRunning { get; private set; }

    public int StopCount { get; private set; }

    public Task StartAsync(
        StudySessionId sessionId,
        DateTime lastHeartbeatAtUtc,
        CancellationToken cancellationToken = default)
    {
        IsRunning = true;
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken = default)
    {
        StopCount++;
        IsRunning = false;
        return Task.CompletedTask;
    }

    public Task<bool> BeatOnceAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult(false);
}

internal sealed class FakeSessionRepository : IStudySessionRepository
{
    public List<StudySession> Sessions { get; } = [];

    public StudySession? Active => Sessions.SingleOrDefault(session => session.IsActive);

    public Task<StudySession?> GetActiveAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult(Active);

    public Task<DailyStudySummary?> GetDailySummaryAsync(
        DateOnly localDate,
        CancellationToken cancellationToken = default) =>
        Task.FromResult<DailyStudySummary?>(null);

    public Task<IReadOnlyList<StudySession>> ListByLocalDateRangeAsync(
        DateOnly startLocalDate,
        DateOnly endLocalDate,
        CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<StudySession>>(
            Sessions.Where(session => session.StartLocalDate >= startLocalDate
                && session.StartLocalDate <= endLocalDate).ToList());

    public Task AddAsync(StudySession session, CancellationToken cancellationToken = default)
    {
        Sessions.Add(session);
        return Task.CompletedTask;
    }

    public Task UpdateAsync(StudySession session, CancellationToken cancellationToken = default)
    {
        if (!Sessions.Contains(session))
            Sessions.Add(session);

        return Task.CompletedTask;
    }

    public Task<bool> RecordHeartbeatAsync(
        StudySessionId sessionId,
        DateTime heartbeatAtUtc,
        int activeSeconds,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(false);

    public Task<bool> IncrementBlockedAttemptCountAsync(
        StudySessionId sessionId,
        DateTime nowUtc,
        int count = 1,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(false);

    public Task<bool> AddActivitySecondsAsync(
        StudySessionId sessionId,
        ActivityCategory category,
        int categorySeconds,
        int idleSeconds,
        int onlineClassSeconds,
        int monitoringEligibleSeconds,
        DateTime nowUtc,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(false);

    public Task<bool> AddAttentionSecondsAsync(
        StudySessionId sessionId,
        AttentionState state,
        int seconds,
        double? confidence,
        DateTime nowUtc,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(false);
}

internal sealed class RecordingAuditLogRepository : IAuditLogRepository
{
    public List<AuditLog> Entries { get; } = [];

    public Task AddAsync(AuditLog auditLog, CancellationToken cancellationToken = default)
    {
        Entries.Add(auditLog);
        return Task.CompletedTask;
    }
}

internal sealed class TempDatabase : IAsyncDisposable
{
    public TempDatabase()
    {
        RootPath = Path.Combine(
            Path.GetTempPath(),
            "edufocus-service-tests",
            Guid.NewGuid().ToString("N"));

        Directory.CreateDirectory(RootPath);
        DatabasePath = Path.Combine(RootPath, "edufocus.db");
        Options = new EduFocusDatabaseOptions
        {
            DatabasePath = DatabasePath,
            BackupDirectory = Path.Combine(RootPath, "backups"),
            ProtectDataDirectory = false
        };
        KeyStore = new DatabaseKeyStore(Options);
    }

    public string RootPath { get; }

    public string DatabasePath { get; }

    public EduFocusDatabaseOptions Options { get; }

    public DatabaseKeyStore KeyStore { get; }

    public SqliteDatabaseInitializer CreateInitializer(IAppLogger logger) => new(Options, logger, KeyStore);

    public ValueTask DisposeAsync()
    {
        try
        {
            if (Directory.Exists(RootPath))
                Directory.Delete(RootPath, recursive: true);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }

        return ValueTask.CompletedTask;
    }
}
