using System.Text.Json;
using System.Text.Json.Serialization;

namespace EduFocus.Cloud.Contracts.Sync;

[JsonConverter(typeof(JsonStringEnumConverter<SyncEventType>))]
public enum SyncEventType
{
    [JsonStringEnumMemberName("daily_summary")]
    DailySummary,

    [JsonStringEnumMemberName("attention_alert")]
    AttentionAlert,

    [JsonStringEnumMemberName("protection_off")]
    ProtectionOff,

    [JsonStringEnumMemberName("security_alert")]
    SecurityAlert,

    [JsonStringEnumMemberName("blocked_attempt")]
    BlockedAttempt,
}

[JsonConverter(typeof(JsonStringEnumConverter<AttentionAlertKind>))]
public enum AttentionAlertKind
{
    [JsonStringEnumMemberName("distracted")]
    Distracted,

    [JsonStringEnumMemberName("absent")]
    Absent,
}

[JsonConverter(typeof(JsonStringEnumConverter<ProtectionOffSource>))]
public enum ProtectionOffSource
{
    [JsonStringEnumMemberName("parent")]
    Parent,

    [JsonStringEnumMemberName("schedule")]
    Schedule,

    [JsonStringEnumMemberName("exit")]
    Exit,
}

[JsonConverter(typeof(JsonStringEnumConverter<SecurityAlertKind>))]
public enum SecurityAlertKind
{
    [JsonStringEnumMemberName("clock_jump")]
    ClockJump,
}

[JsonConverter(typeof(JsonStringEnumConverter<SyncEventResultStatus>))]
public enum SyncEventResultStatus
{
    [JsonStringEnumMemberName("applied")]
    Applied,

    [JsonStringEnumMemberName("duplicate")]
    Duplicate,

    [JsonStringEnumMemberName("rejected")]
    Rejected,
}

public sealed record SyncEventDto(
    string ClientEventId,
    SyncEventType Type,
    DateTimeOffset OccurredAtUtc,
    JsonElement Payload);

public sealed record SyncBatchRequest(IReadOnlyList<SyncEventDto> Events);

public sealed record SyncEventResult(
    string ClientEventId,
    SyncEventResultStatus Status,
    string? Code = null,
    string? Message = null);

public sealed record SyncBatchResponse(
    IReadOnlyList<SyncEventResult> Results,
    DateTimeOffset ServerTimeUtc);

public sealed record DailySummaryPayload(
    DateOnly LocalDate,
    string? TimeZoneId,
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

public sealed record AttentionAlertPayload(
    AttentionAlertKind Kind,
    DateOnly LocalDate,
    DateTimeOffset StartedAtUtc,
    int DurationSeconds);

public sealed record ProtectionOffPayload(ProtectionOffSource Source, DateOnly LocalDate);

public sealed record SecurityAlertPayload(SecurityAlertKind Kind, DateOnly LocalDate);

public sealed record BlockedAttemptPayload(DateOnly LocalDate);
