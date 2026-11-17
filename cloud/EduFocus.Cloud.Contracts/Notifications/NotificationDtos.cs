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
}

public sealed record NotificationDto(
    Guid Id,
    NotificationKind Kind,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset? SentAtUtc,
    string? SuppressedReason,
    DateOnly? LocalDate);

public sealed record NotificationListResponse(
    IReadOnlyList<NotificationDto> Items,
    DateTimeOffset? NextBefore);
