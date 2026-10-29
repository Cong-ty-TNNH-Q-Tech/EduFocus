using EduFocus.Domain.Enums;

namespace EduFocus.Ports.DTOs;

/// <summary>A stored weekly protection window.</summary>
public sealed record ScheduleWindowInfo(
    Guid Id,
    ScheduleDays Days,
    int StartMinutes,
    int EndMinutes,
    string? Label,
    bool IsEnabled);

/// <summary>Parent input for creating or editing a window.</summary>
public sealed record ScheduleWindowDraft(
    ScheduleDays Days,
    int StartMinutes,
    int EndMinutes,
    string? Label,
    bool IsEnabled = true);

/// <summary>Schedule state for the Parent Mode screen and the tray/status UI.</summary>
public sealed record ScheduleOverview(
    bool ScheduleEnabled,
    IReadOnlyList<ScheduleWindowInfo> Windows,
    ScheduleWindowInfo? ActiveWindow,
    DateTime? OverrideUntilUtc,
    bool IsOverridden,
    bool ProtectionEnabled,
    ProtectionSource ProtectionSource);

public sealed record ScheduleOperationResult(
    ScheduleOverview Overview,
    bool ProtectionChanged,
    IReadOnlyList<string> Warnings);

public sealed record ScheduleEvaluationResult(
    bool ProtectionChanged,
    bool ClockJumpDetected,
    ScheduleOverview Overview,
    IReadOnlyList<string> Warnings);
