namespace EduFocus.Ports.DTOs;

/// <summary>
/// Rows removed by one retention run. Counts are per table; deleting a session
/// may cascade to its remaining child rows, so the totals are not a global
/// distinct-row count.
/// </summary>
public sealed record RetentionCounts(
    int ActivitySamples,
    int AttentionEvents,
    int BlockedAttempts,
    int Sessions,
    int AuditLogs)
{
    public static RetentionCounts Empty { get; } = new(0, 0, 0, 0, 0);
}

/// <summary>
/// Outcome of one retention run (UC11, issue #29). A run is complete only when
/// every table, the vacuum step and the audit entry succeeded; failures are
/// table names and never contain row content.
/// </summary>
public sealed record RetentionRunResult(
    DateTime RanAtUtc,
    RetentionCounts Deleted,
    bool Completed,
    IReadOnlyList<string> Failures);
