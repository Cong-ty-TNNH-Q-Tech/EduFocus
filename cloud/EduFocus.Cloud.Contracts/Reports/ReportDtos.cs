namespace EduFocus.Cloud.Contracts.Reports;

public sealed record DailySummaryDto(
    Guid DeviceId,
    string DeviceName,
    DateOnly LocalDate,
    DateTimeOffset GeneratedAtUtc,
    int StudyMinutes,
    int FocusedMinutes,
    int DistractionCount,
    int BlockedAttempts,
    int CameraCoveragePercent,
    int OnlineClassMinutes);

public sealed record DailyReportResponse(
    DateOnly From,
    DateOnly To,
    IReadOnlyList<DailySummaryDto> Days);
