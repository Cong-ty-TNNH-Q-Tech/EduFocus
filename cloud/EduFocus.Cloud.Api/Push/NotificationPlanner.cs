using EduFocus.Cloud.Api.Sync;
using EduFocus.Cloud.Contracts.Sync;
using EduFocus.Cloud.Data.Entities;
using SyncEventType = EduFocus.Cloud.Contracts.Sync.SyncEventType;

namespace EduFocus.Cloud.Api.Push;

public sealed record PlannedNotification(
    NotificationCategory Category,
    string DedupeKey,
    DateOnly LocalDate);

public static class NotificationPlanner
{
    public const int AttentionWindowMinutes = 15;
    public const int MaxDedupeKeyLength = 160;

    public static PlannedNotification? ForAlert(ValidatedSyncEvent validated, DateTimeOffset occurredAtUtc) =>
        validated.Type switch
        {
            SyncEventType.AttentionAlert => Attention(validated.AttentionAlert!, occurredAtUtc),
            SyncEventType.ProtectionOff => ProtectionOff(validated.ProtectionOff!, occurredAtUtc),
            SyncEventType.SecurityAlert => SecurityAlert(validated.SecurityAlert!, occurredAtUtc),
            _ => null,
        };

    public static PlannedNotification DailySummary(DateOnly localDate) =>
        new(
            NotificationCategory.DailySummary,
            Truncate($"daily_summary:{localDate:yyyyMMdd}"),
            localDate);

    private static PlannedNotification Attention(AttentionAlertPayload payload, DateTimeOffset occurredAtUtc)
    {
        var window = FloorUtc(occurredAtUtc, TimeSpan.FromMinutes(AttentionWindowMinutes));
        return new(
            NotificationCategory.AttentionAlert,
            Truncate($"attention_alert:{ContractName(payload.Kind)}:{window:yyyyMMddHHmm}"),
            payload.LocalDate);
    }

    private static PlannedNotification ProtectionOff(
        ProtectionOffPayload payload,
        DateTimeOffset occurredAtUtc)
    {
        var window = FloorUtc(occurredAtUtc, TimeSpan.FromHours(1));
        return new(
            NotificationCategory.ProtectionOff,
            Truncate($"protection_off:{ContractName(payload.Source)}:{window:yyyyMMddHH}"),
            payload.LocalDate);
    }

    private static PlannedNotification SecurityAlert(SecurityAlertPayload payload, DateTimeOffset occurredAtUtc)
    {
        var window = FloorUtc(occurredAtUtc, TimeSpan.FromHours(1));
        return new(
            NotificationCategory.SecurityAlert,
            Truncate($"security_alert:{ContractName(payload.Kind)}:{window:yyyyMMddHH}"),
            payload.LocalDate);
    }

    private static DateTimeOffset FloorUtc(DateTimeOffset value, TimeSpan window)
    {
        var ticks = value.UtcTicks - (value.UtcTicks % window.Ticks);
        return new DateTimeOffset(ticks, TimeSpan.Zero);
    }

    private static string Truncate(string key) =>
        key.Length <= MaxDedupeKeyLength ? key : key[..MaxDedupeKeyLength];

    private static string ContractName(AttentionAlertKind kind) => kind switch
    {
        AttentionAlertKind.Distracted => "distracted",
        AttentionAlertKind.Absent => "absent",
        _ => throw new ArgumentOutOfRangeException(nameof(kind)),
    };

    private static string ContractName(ProtectionOffSource source) => source switch
    {
        ProtectionOffSource.Parent => "parent",
        ProtectionOffSource.Schedule => "schedule",
        ProtectionOffSource.Exit => "exit",
        _ => throw new ArgumentOutOfRangeException(nameof(source)),
    };

    private static string ContractName(SecurityAlertKind kind) => kind switch
    {
        SecurityAlertKind.ClockJump => "clock_jump",
        _ => throw new ArgumentOutOfRangeException(nameof(kind)),
    };
}
