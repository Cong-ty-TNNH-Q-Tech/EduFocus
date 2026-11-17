using EduFocus.Cloud.Api.Push;
using EduFocus.Cloud.Api.Sync;
using EduFocus.Cloud.Contracts.Sync;
using EduFocus.Cloud.Data.Entities;
using SyncEventType = EduFocus.Cloud.Contracts.Sync.SyncEventType;

namespace EduFocus.Cloud.Tests;

public sealed class NotificationPlannerTests
{
    private static readonly DateOnly LocalDate = new(2026, 11, 17);

    [Fact]
    public void Attention_alerts_dedupe_within_fifteen_minute_windows()
    {
        var alert = new AttentionAlertPayload(
            AttentionAlertKind.Distracted,
            LocalDate,
            DateTimeOffset.Parse("2026-11-17T10:00:00Z"),
            360);
        var validated = new ValidatedSyncEvent(
            SyncEventType.AttentionAlert,
            AttentionAlert: alert);

        var first = NotificationPlanner.ForAlert(
            validated, DateTimeOffset.Parse("2026-11-17T10:01:00Z"));
        var sameWindow = NotificationPlanner.ForAlert(
            validated, DateTimeOffset.Parse("2026-11-17T10:14:59Z"));
        var nextWindow = NotificationPlanner.ForAlert(
            validated, DateTimeOffset.Parse("2026-11-17T10:15:00Z"));

        Assert.NotNull(first);
        Assert.Equal("attention_alert:distracted:202611171000", first.DedupeKey);
        Assert.Equal(first.DedupeKey, sameWindow!.DedupeKey);
        Assert.NotEqual(first.DedupeKey, nextWindow!.DedupeKey);
        Assert.Equal(NotificationCategory.AttentionAlert, first.Category);
        Assert.Equal(LocalDate, first.LocalDate);
    }

    [Fact]
    public void Different_attention_kinds_do_not_share_dedupe_keys()
    {
        var occurredAt = DateTimeOffset.Parse("2026-11-17T10:01:00Z");
        var distracted = NotificationPlanner.ForAlert(
            new ValidatedSyncEvent(
                SyncEventType.AttentionAlert,
                AttentionAlert: new AttentionAlertPayload(
                    AttentionAlertKind.Distracted, LocalDate, occurredAt, 360)),
            occurredAt);
        var absent = NotificationPlanner.ForAlert(
            new ValidatedSyncEvent(
                SyncEventType.AttentionAlert,
                AttentionAlert: new AttentionAlertPayload(
                    AttentionAlertKind.Absent, LocalDate, occurredAt, 360)),
            occurredAt);

        Assert.NotEqual(distracted!.DedupeKey, absent!.DedupeKey);
    }

    [Fact]
    public void Protection_off_dedupes_per_source_and_hour()
    {
        var validated = new ValidatedSyncEvent(
            SyncEventType.ProtectionOff,
            ProtectionOff: new ProtectionOffPayload(ProtectionOffSource.Parent, LocalDate));

        var first = NotificationPlanner.ForAlert(
            validated, DateTimeOffset.Parse("2026-11-17T10:01:00Z"));
        var sameHour = NotificationPlanner.ForAlert(
            validated, DateTimeOffset.Parse("2026-11-17T10:59:00Z"));
        var nextHour = NotificationPlanner.ForAlert(
            validated, DateTimeOffset.Parse("2026-11-17T11:00:00Z"));

        Assert.Equal("protection_off:parent:2026111710", first!.DedupeKey);
        Assert.Equal(first.DedupeKey, sameHour!.DedupeKey);
        Assert.NotEqual(first.DedupeKey, nextHour!.DedupeKey);
    }

    [Fact]
    public void Security_alerts_dedupe_per_kind_and_hour()
    {
        var validated = new ValidatedSyncEvent(
            SyncEventType.SecurityAlert,
            SecurityAlert: new SecurityAlertPayload(SecurityAlertKind.ClockJump, LocalDate));

        var first = NotificationPlanner.ForAlert(
            validated, DateTimeOffset.Parse("2026-11-17T10:01:00Z"));
        var sameHour = NotificationPlanner.ForAlert(
            validated, DateTimeOffset.Parse("2026-11-17T10:59:00Z"));

        Assert.Equal("security_alert:clock_jump:2026111710", first!.DedupeKey);
        Assert.Equal(first.DedupeKey, sameHour!.DedupeKey);
    }

    [Fact]
    public void Daily_summary_dedupes_per_family_and_date()
    {
        var planned = NotificationPlanner.DailySummary(LocalDate);

        Assert.Equal("daily_summary:20261117", planned.DedupeKey);
        Assert.Equal(NotificationCategory.DailySummary, planned.Category);
        Assert.Equal(LocalDate, planned.LocalDate);
    }

    [Fact]
    public void Daily_summary_events_do_not_plan_alerts()
    {
        var validated = new ValidatedSyncEvent(
            SyncEventType.DailySummary,
            DailySummary: new DailySummaryPayload(
                LocalDate, null, DateTimeOffset.UtcNow, 1, 60, 60, 0, 0, 60, 60, 60, 0, 0, 0, 0, 100, 100));

        Assert.Null(NotificationPlanner.ForAlert(validated, DateTimeOffset.UtcNow));
    }
}
