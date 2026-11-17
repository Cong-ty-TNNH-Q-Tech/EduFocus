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
}

[JsonConverter(typeof(JsonStringEnumConverter<AttentionAlertKind>))]
public enum AttentionAlertKind
{
    [JsonStringEnumMemberName("distracted")]
    Distracted,

    [JsonStringEnumMemberName("absent")]
    Absent,
}

[JsonConverter(typeof(JsonStringEnumConverter<ProtectionOffReason>))]
public enum ProtectionOffReason
{
    [JsonStringEnumMemberName("parent_exit")]
    ParentExit,

    [JsonStringEnumMemberName("manual")]
    Manual,
}

[JsonConverter(typeof(JsonStringEnumConverter<SecurityAlertKind>))]
public enum SecurityAlertKind
{
    [JsonStringEnumMemberName("clock_jump")]
    ClockJump,
}

public sealed record SyncEventDto(
    string ClientEventId,
    SyncEventType Type,
    DateTimeOffset OccurredAtUtc,
    JsonElement Payload);

public sealed record SyncBatchRequest(IReadOnlyList<SyncEventDto> Events);

public sealed record SyncRejectedEvent(string ClientEventId, string Code, string Message);

public sealed record SyncBatchResponse(
    int Accepted,
    int Duplicates,
    IReadOnlyList<SyncRejectedEvent> Rejected,
    DateTimeOffset ServerTimeUtc);

public sealed record DailySummaryPayload(
    DateTimeOffset GeneratedAtUtc,
    DateOnly LocalDate,
    int StudyMinutes,
    int FocusedMinutes,
    int DistractionCount,
    int BlockedAttempts,
    int CameraCoveragePercent,
    int OnlineClassMinutes);

public sealed record AttentionAlertPayload(
    AttentionAlertKind Kind,
    DateTimeOffset StartedAtUtc,
    int DurationSeconds);

public sealed record ProtectionOffPayload(ProtectionOffReason Reason);

public sealed record SecurityAlertPayload(SecurityAlertKind Kind);
