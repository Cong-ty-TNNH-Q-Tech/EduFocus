namespace EduFocus.Domain.Reports;

/// <summary>
/// Foreground activity for one local hour (database schema §13.2). Idle time is
/// never part of these seconds, and the local date/hour pair keeps DST days
/// intact instead of flattening them into wall-clock hours.
/// </summary>
public sealed record HourlyActivityBucket(
    DateOnly LocalDate,
    int LocalHour,
    int StudySeconds,
    int DistractionSeconds,
    int NeutralSeconds)
{
    public int TotalSeconds => StudySeconds + DistractionSeconds + NeutralSeconds;
}
