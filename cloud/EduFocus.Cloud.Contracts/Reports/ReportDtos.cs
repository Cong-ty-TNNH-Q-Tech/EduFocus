namespace EduFocus.Cloud.Contracts.Reports;

public sealed record DailySummaryDto(
    DateOnly LocalDate,
    string TimeZoneId,
    DateTimeOffset GeneratedAtUtc,
    int SessionsCount,
    long ActiveSeconds,
    long StudySeconds,
    long IdleSeconds,
    long OnlineClassSeconds,
    long MonitoringEligibleSeconds,
    long MonitoredSeconds,
    long FocusedSeconds,
    long DistractedSeconds,
    long AbsentSeconds,
    long DrowsySeconds,
    int BlockedAttempts,
    double? AttentionPercent,
    double? CoveragePercent);
