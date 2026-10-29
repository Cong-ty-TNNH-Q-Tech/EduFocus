using EduFocus.Domain.Entities;
using EduFocus.Domain.Enums;
using EduFocus.Ports.DTOs;
using EduFocus.Ports.Security;
using EduFocus.UseCases.Tests.TestSupport;
using FluentAssertions;

namespace EduFocus.UseCases.Tests.Schedule;

public sealed class ScheduleServiceTests
{
    [Fact]
    public async Task Evaluate_enables_protection_inside_an_enabled_window()
    {
        var harness = CreateActiveSchedule(out var window);

        var result = await harness.Service.EvaluateAsync();

        result.ProtectionChanged.Should().BeTrue();
        result.ClockJumpDetected.Should().BeFalse();
        result.Warnings.Should().BeEmpty();

        result.Overview.ScheduleEnabled.Should().BeTrue();
        result.Overview.ActiveWindow.Should().NotBeNull();
        result.Overview.ActiveWindow!.Id.Should().Be(window.Id.Value);
        result.Overview.ProtectionEnabled.Should().BeTrue();
        result.Overview.ProtectionSource.Should().Be(ProtectionSource.Schedule);

        harness.SettingsRow.ProtectionEnabled.Should().BeTrue();
        harness.SettingsRow.ProtectionSource.Should().Be(ProtectionSource.Schedule);
        harness.Engine.ApplyCount.Should().Be(1);
        harness.Sessions.Active!.Trigger.Should().Be(SessionTrigger.Schedule);
        harness.Audits.Entries.Should().Contain(x =>
            x.EventType == AuditEventType.ProtectionOn && x.Actor == AuditActor.System);
    }

    [Fact]
    public async Task Evaluate_disables_schedule_owned_protection_after_the_window_ends()
    {
        var harness = CreateActiveSchedule(out _);
        await harness.Service.EvaluateAsync();

        Advance(harness, TimeSpan.FromMinutes(61));

        var result = await harness.Service.EvaluateAsync();

        result.ProtectionChanged.Should().BeTrue();
        result.Overview.ProtectionEnabled.Should().BeFalse();
        result.Overview.ProtectionSource.Should().Be(ProtectionSource.None);
        result.Overview.ActiveWindow.Should().BeNull();

        harness.Engine.RemoveCount.Should().Be(1);
        harness.Sessions.Active.Should().BeNull();
        harness.Sessions.Sessions.Should().ContainSingle()
            .Which.Status.Should().Be(StudySessionStatus.Completed);
        harness.Audits.Entries.Should().Contain(x =>
            x.EventType == AuditEventType.ProtectionOff && x.Actor == AuditActor.System);
    }

    [Fact]
    public async Task Manually_enabled_protection_survives_the_end_of_a_window()
    {
        var harness = CreateActiveSchedule(out _);
        harness.Authenticate();
        await harness.Protection.EnableAsync();

        Advance(harness, TimeSpan.FromHours(2));
        var result = await harness.Service.EvaluateAsync();

        result.Overview.ProtectionEnabled.Should().BeTrue();
        result.Overview.ProtectionSource.Should().Be(ProtectionSource.Manual);
        harness.Engine.RemoveCount.Should().Be(0);
        harness.SettingsRow.ProtectionEnabled.Should().BeTrue();
    }

    [Fact]
    public async Task Manual_disable_inside_a_window_creates_an_override_until_its_end()
    {
        var harness = CreateActiveSchedule(out _);
        await harness.Service.EvaluateAsync();
        Advance(harness, TimeSpan.FromMinutes(10));
        harness.Authenticate();

        var disable = await harness.Protection.DisableAsync();

        disable.Status.IsEnabled.Should().BeFalse();
        harness.SettingsRow.ScheduleOverrideUntilUtc.Should().Be(WindowEnd);
        harness.Audits.Entries.Should().Contain(x =>
            x.EventType == AuditEventType.ProtectionOff
            && x.Actor == AuditActor.Parent
            && x.Description.Contains("override"));

        var duringOverride = await harness.Service.EvaluateAsync();

        duringOverride.ProtectionChanged.Should().BeFalse();
        duringOverride.Overview.IsOverridden.Should().BeTrue();
        duringOverride.Overview.ProtectionEnabled.Should().BeFalse();
        harness.Engine.ApplyCount.Should().Be(1);
    }

    [Fact]
    public async Task Override_expires_at_window_end_and_protection_stays_off()
    {
        var harness = CreateActiveSchedule(out _);
        await harness.Service.EvaluateAsync();
        Advance(harness, TimeSpan.FromMinutes(10));
        harness.Authenticate();
        await harness.Protection.DisableAsync();

        Advance(harness, TimeSpan.FromMinutes(50));
        var result = await harness.Service.EvaluateAsync();

        result.Overview.IsOverridden.Should().BeFalse();
        result.Overview.OverrideUntilUtc.Should().BeNull();
        result.Overview.ProtectionEnabled.Should().BeFalse();
        harness.SettingsRow.ScheduleOverrideUntilUtc.Should().BeNull();
    }

    [Fact]
    public async Task Manual_enable_clears_the_override_and_keeps_protection_on_outside_the_schedule()
    {
        var harness = CreateActiveSchedule(out _);
        await harness.Service.EvaluateAsync();
        Advance(harness, TimeSpan.FromMinutes(10));
        harness.Authenticate();
        await harness.Protection.DisableAsync();

        await harness.Protection.EnableAsync();

        harness.SettingsRow.ScheduleOverrideUntilUtc.Should().BeNull();
        harness.SettingsRow.ProtectionSource.Should().Be(ProtectionSource.Manual);

        Advance(harness, TimeSpan.FromHours(2));
        var result = await harness.Service.EvaluateAsync();

        result.Overview.ProtectionEnabled.Should().BeTrue();
        result.Overview.ProtectionSource.Should().Be(ProtectionSource.Manual);
    }

    [Fact]
    public async Task Shrinking_a_window_clamps_the_override_to_the_new_end()
    {
        var harness = CreateActiveSchedule(out var window);
        await harness.Service.EvaluateAsync();
        Advance(harness, TimeSpan.FromMinutes(10));
        harness.Authenticate();
        await harness.Protection.DisableAsync();

        var update = await harness.Service.UpdateWindowAsync(
            window.Id.Value,
            new ScheduleWindowDraft(ScheduleDays.Thursday, 480, 510, "Ngắn hơn", IsEnabled: true));

        update.Overview.OverrideUntilUtc.Should().Be(new DateTime(2026, 10, 29, 8, 30, 0, DateTimeKind.Utc));

        Advance(harness, TimeSpan.FromMinutes(21));
        var result = await harness.Service.EvaluateAsync();

        result.Overview.ProtectionEnabled.Should().BeFalse();
        result.Overview.OverrideUntilUtc.Should().BeNull();
    }

    [Fact]
    public async Task Adding_a_window_auto_enables_the_schedule_and_audits()
    {
        var harness = new ScheduleHarness();
        harness.Authenticate();

        var result = await harness.Service.AddWindowAsync(
            new ScheduleWindowDraft(ScheduleDays.Thursday, 480, 540, "Giờ học"));

        result.Overview.ScheduleEnabled.Should().BeTrue();
        result.Overview.Windows.Should().ContainSingle();
        harness.ScheduleWindows.Windows.Should().ContainSingle();
        harness.Audits.Entries.Should().ContainSingle(x =>
            x.EventType == AuditEventType.ScheduleChanged && x.Actor == AuditActor.Parent);
    }

    [Fact]
    public async Task Overlapping_windows_are_rejected_and_not_saved()
    {
        var harness = new ScheduleHarness();
        harness.Authenticate();
        await harness.Service.AddWindowAsync(
            new ScheduleWindowDraft(ScheduleDays.Thursday, 480, 540, "Buổi sáng"));

        var act = () => harness.Service.AddWindowAsync(
            new ScheduleWindowDraft(ScheduleDays.Thursday, 510, 570, "Chồng lấn"));

        await act.Should().ThrowAsync<ArgumentException>();
        harness.ScheduleWindows.Windows.Should().ContainSingle();
    }

    [Fact]
    public async Task Mutations_require_an_authenticated_parent()
    {
        var harness = new ScheduleHarness();

        var act = () => harness.Service.AddWindowAsync(
            new ScheduleWindowDraft(ScheduleDays.Thursday, 480, 540, "Giờ học"));

        await act.Should().ThrowAsync<ParentAuthorizationException>();
        harness.ScheduleWindows.Windows.Should().BeEmpty();
        harness.Audits.Entries.Should().BeEmpty();
    }

    [Fact]
    public async Task Clock_jump_is_audited_and_skips_the_automatic_disable()
    {
        var harness = CreateActiveSchedule(out _);
        await harness.Service.EvaluateAsync();

        harness.Clock.Advance(TimeSpan.FromHours(2));
        harness.Monotonic.Ticks += TimeSpan.FromSeconds(30);

        var jump = await harness.Service.EvaluateAsync();

        jump.ClockJumpDetected.Should().BeTrue();
        jump.ProtectionChanged.Should().BeFalse();
        jump.Overview.ProtectionEnabled.Should().BeTrue();
        harness.Engine.RemoveCount.Should().Be(0);
        harness.Audits.Entries.Should().Contain(x =>
            x.EventType == AuditEventType.TamperDetected && x.Actor == AuditActor.System);

        Advance(harness, TimeSpan.FromSeconds(30));
        var next = await harness.Service.EvaluateAsync();

        next.ClockJumpDetected.Should().BeFalse();
        next.Overview.ProtectionEnabled.Should().BeFalse();
    }

    [Fact]
    public async Task Evaluate_is_a_no_op_when_the_schedule_is_disabled()
    {
        var harness = new ScheduleHarness();
        harness.SeedWindow(ScheduleDays.Thursday, 480, 540);

        var result = await harness.Service.EvaluateAsync();

        result.ProtectionChanged.Should().BeFalse();
        result.Overview.ProtectionEnabled.Should().BeFalse();
        result.Overview.ActiveWindow.Should().NotBeNull();
        harness.Engine.ApplyCount.Should().Be(0);
        harness.Audits.Entries.Should().BeEmpty();
    }

    [Fact]
    public async Task Disabling_the_schedule_ends_schedule_owned_protection()
    {
        var harness = CreateActiveSchedule(out _);
        await harness.Service.EvaluateAsync();
        harness.Authenticate();

        var result = await harness.Service.SetScheduleEnabledAsync(false);

        result.ProtectionChanged.Should().BeTrue();
        result.Overview.ScheduleEnabled.Should().BeFalse();
        result.Overview.ProtectionEnabled.Should().BeFalse();
        harness.SettingsRow.ProtectionSource.Should().Be(ProtectionSource.None);
        harness.Engine.RemoveCount.Should().Be(1);
    }

    [Fact]
    public async Task Deleting_the_active_window_ends_schedule_owned_protection()
    {
        var harness = CreateActiveSchedule(out var window);
        await harness.Service.EvaluateAsync();
        harness.Authenticate();

        var result = await harness.Service.DeleteWindowAsync(window.Id.Value);

        result.Overview.ProtectionEnabled.Should().BeFalse();
        result.Overview.ActiveWindow.Should().BeNull();
        harness.Engine.RemoveCount.Should().Be(1);
        harness.ScheduleWindows.Windows.Should().BeEmpty();
    }

    [Fact]
    public async Task Engine_failure_while_auto_enabling_becomes_a_warning()
    {
        var harness = CreateActiveSchedule(out _);
        harness.Engine.ThrowOnApply = true;

        var result = await harness.Service.EvaluateAsync();

        result.ProtectionChanged.Should().BeFalse();
        result.Overview.ProtectionEnabled.Should().BeFalse();
        result.Warnings.Should().ContainSingle();
        harness.SettingsRow.ProtectionEnabled.Should().BeFalse();
        harness.Logger.Errors.Should().NotBeEmpty();
    }

    [Fact]
    public async Task Window_ending_at_1440_turns_off_at_local_midnight()
    {
        var start = new DateTimeOffset(2026, 11, 1, 23, 30, 0, TimeSpan.Zero);
        var harness = new ScheduleHarness(start: start);
        harness.SeedWindow(ScheduleDays.Sunday, 1380, 1440);
        harness.SettingsRow.SetScheduleEnabled(true, start.UtcDateTime);

        var inside = await harness.Service.EvaluateAsync();

        inside.Overview.ProtectionEnabled.Should().BeTrue();

        Advance(harness, TimeSpan.FromMinutes(30));
        var afterMidnight = await harness.Service.EvaluateAsync();

        afterMidnight.Overview.ActiveWindow.Should().BeNull();
        afterMidnight.Overview.ProtectionEnabled.Should().BeFalse();
    }

    [Fact]
    public async Task Evaluation_uses_the_local_wall_clock_across_spring_forward()
    {
        var zone = CreateDstZone();
        var start = new DateTimeOffset(2026, 3, 8, 6, 30, 0, TimeSpan.Zero);
        var harness = new ScheduleHarness(zone, start);
        harness.SeedWindow(ScheduleDays.Sunday, 60, 240);
        harness.SettingsRow.SetScheduleEnabled(true, start.UtcDateTime);

        var enable = await harness.Service.EvaluateAsync();

        enable.Overview.ProtectionEnabled.Should().BeTrue();

        Advance(harness, TimeSpan.FromHours(1));
        var afterGap = await harness.Service.EvaluateAsync();

        afterGap.ClockJumpDetected.Should().BeFalse();
        afterGap.Overview.ProtectionEnabled.Should().BeTrue();
        afterGap.Overview.ActiveWindow.Should().NotBeNull();

        Advance(harness, TimeSpan.FromMinutes(30));
        var atEnd = await harness.Service.EvaluateAsync();

        atEnd.Overview.ActiveWindow.Should().BeNull();
        atEnd.Overview.ProtectionEnabled.Should().BeFalse();
    }

    private static readonly DateTime WindowEnd = new(2026, 10, 29, 9, 0, 0, DateTimeKind.Utc);

    private static ScheduleHarness CreateActiveSchedule(out ScheduleWindow window)
    {
        var harness = new ScheduleHarness();
        window = harness.SeedWindow(ScheduleDays.Thursday, 480, 540, label: "Giờ học");
        harness.SettingsRow.SetScheduleEnabled(true, harness.Clock.GetUtcNow().UtcDateTime);
        return harness;
    }

    private static void Advance(ScheduleHarness harness, TimeSpan delta)
    {
        harness.Clock.Advance(delta);
        harness.Monotonic.Ticks += delta;
    }

    private static TimeZoneInfo CreateDstZone()
    {
        var daylightStart = TimeZoneInfo.TransitionTime.CreateFloatingDateRule(
            new DateTime(1, 1, 1, 2, 0, 0),
            month: 3,
            week: 2,
            DayOfWeek.Sunday);
        var daylightEnd = TimeZoneInfo.TransitionTime.CreateFloatingDateRule(
            new DateTime(1, 1, 1, 2, 0, 0),
            month: 11,
            week: 1,
            DayOfWeek.Sunday);

        var rule = TimeZoneInfo.AdjustmentRule.CreateAdjustmentRule(
            new DateTime(2000, 1, 1),
            new DateTime(2040, 12, 31),
            TimeSpan.FromHours(1),
            daylightStart,
            daylightEnd);

        return TimeZoneInfo.CreateCustomTimeZone(
            "EduFocus/Test-DST",
            TimeSpan.FromHours(-5),
            "EduFocus Test DST",
            "EduFocus Test Standard",
            "EduFocus Test Daylight",
            [rule]);
    }
}
