namespace EduFocus.Service.Runtime;

/// <summary>
/// Cadence of the protection service. The schedule evaluator matches the app's
/// 30-second loop; retention runs once at startup and once per day after the
/// local cut-off hour.
/// </summary>
public sealed record ServiceRuntimeOptions
{
    public TimeSpan ScheduleEvaluationInterval { get; init; } = TimeSpan.FromSeconds(30);

    public TimeSpan RetentionCheckInterval { get; init; } = TimeSpan.FromMinutes(1);

    public int RetentionDailyHour { get; init; } = 3;

    public bool RunRetentionAtStartup { get; init; } = true;

    /// <summary>Bound for a clean stop; the SCM gives roughly 30 seconds.</summary>
    public TimeSpan StopTimeout { get; init; } = TimeSpan.FromSeconds(20);
}
