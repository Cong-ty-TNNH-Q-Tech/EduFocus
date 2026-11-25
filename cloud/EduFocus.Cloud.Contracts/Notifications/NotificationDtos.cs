using System.Text.Json.Serialization;

namespace EduFocus.Cloud.Contracts.Notifications;

[JsonConverter(typeof(JsonStringEnumConverter<NotificationKind>))]
public enum NotificationKind
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

[JsonConverter(typeof(JsonStringEnumConverter<NotificationDeliveryStatus>))]
public enum NotificationDeliveryStatus
{
    [JsonStringEnumMemberName("pending")]
    Pending,

    [JsonStringEnumMemberName("sent")]
    Sent,

    [JsonStringEnumMemberName("suppressed")]
    Suppressed,

    [JsonStringEnumMemberName("failed")]
    Failed,
}

public sealed record NotificationDto(
    Guid Id,
    NotificationKind Category,
    NotificationDeliveryStatus Status,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset? SentAtUtc);

public sealed record NotificationListResponse(
    IReadOnlyList<NotificationDto> Items,
    string? NextBefore);
