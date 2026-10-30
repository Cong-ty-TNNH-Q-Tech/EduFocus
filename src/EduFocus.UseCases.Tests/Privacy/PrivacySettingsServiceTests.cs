using System.Text.Json;
using EduFocus.Domain.Enums;
using EduFocus.Ports.DTOs;
using EduFocus.Ports.Security;
using EduFocus.UseCases.Tests.TestSupport;
using FluentAssertions;

namespace EduFocus.UseCases.Tests.Privacy;

public sealed class PrivacySettingsServiceTests
{
    [Fact]
    public async Task Snapshot_requires_an_authenticated_parent_session()
    {
        var harness = await PrivacyHarness.CreateAsync(authenticated: false);

        var act = () => harness.Service.GetSnapshotAsync();

        await act.Should().ThrowAsync<ParentAuthorizationException>();
    }

    [Fact]
    public async Task Snapshot_reports_settings_counts_and_database_size()
    {
        var harness = await PrivacyHarness.CreateAsync();
        harness.Data.Sessions = 4;
        harness.Data.ActiveSessions = 1;
        harness.Data.ActivitySamples = 120;
        harness.Data.AttentionEvents = 7;
        harness.Data.BlockedAttempts = 3;
        harness.Data.DatabaseSizeBytes = 98_304;

        var snapshot = await harness.Service.GetSnapshotAsync();

        snapshot.Settings.MonitoringEnabled.Should().BeTrue();
        snapshot.Settings.UpdateCheckEnabled.Should().BeFalse();
        snapshot.Settings.Theme.Should().Be(AppTheme.Dark);
        snapshot.Settings.Language.Should().Be(AppLanguage.Vi);
        snapshot.Counts.Should().Be(new PrivacyDataCounts(4, 1, 120, 7, 3));
        snapshot.DatabaseSizeBytes.Should().Be(98_304);
    }

    [Fact]
    public async Task Save_requires_an_authenticated_parent_session()
    {
        var harness = await PrivacyHarness.CreateAsync();
        var update = (await SeedSnapshotAsync(harness)).Settings with { MonitoringEnabled = false };
        harness.Session.Lock();

        var act = () => harness.Service.SaveAsync(update);

        await act.Should().ThrowAsync<ParentAuthorizationException>();
        harness.Settings.UpdateCount.Should().Be(0);
        harness.Audits.Entries.Should().BeEmpty();
    }

    [Fact]
    public async Task Save_persists_every_field_and_audits_field_names_only()
    {
        var harness = await PrivacyHarness.CreateAsync();
        var current = (await SeedSnapshotAsync(harness)).Settings;
        var update = current with
        {
            MonitoringEnabled = false,
            MonitorOnlyDuringClasses = true,
            DistractionThresholdSeconds = 45,
            AbsenceThresholdSeconds = 60,
            SampleIntervalSeconds = 20,
            CameraDeviceId = "  camera-42  ",
            ActivityRetentionDays = 14,
            EventRetentionDays = 60,
            SessionRetentionDays = 180,
            StartWithWindows = false,
            UpdateCheckEnabled = true,
            Theme = AppTheme.Light,
            Language = AppLanguage.En
        };

        var result = await harness.Service.SaveAsync(update);

        result.Changed.Should().BeTrue();
        result.Settings.Should().Be(update with { CameraDeviceId = "camera-42" });

        var stored = harness.Settings.Stored!;
        stored.MonitoringEnabled.Should().BeFalse();
        stored.MonitorOnlyDuringClasses.Should().BeTrue();
        stored.DistractionThresholdSeconds.Should().Be(45);
        stored.AbsenceThresholdSeconds.Should().Be(60);
        stored.SampleIntervalSeconds.Should().Be(20);
        stored.CameraDeviceId.Should().Be("camera-42");
        stored.ActivityRetentionDays.Should().Be(14);
        stored.EventRetentionDays.Should().Be(60);
        stored.SessionRetentionDays.Should().Be(180);
        stored.StartWithWindows.Should().BeFalse();
        stored.UpdateCheckEnabled.Should().BeTrue();
        stored.Theme.Should().Be(AppTheme.Light);
        stored.Language.Should().Be(AppLanguage.En);
        harness.Settings.UpdateCount.Should().Be(1);

        var audit = harness.Audits.Entries.Should().ContainSingle().Subject;
        audit.EventType.Should().Be(AuditEventType.SettingsChanged);
        audit.Description.Should().NotContain("camera-42");

        var metadata = JsonDocument.Parse(audit.MetadataJson!);
        var changed = metadata.RootElement.GetProperty("changed")
            .EnumerateArray()
            .Select(x => x.GetString())
            .ToList();

        changed.Should().BeEquivalentTo(
            "monitoring", "thresholds", "camera", "retention", "startWithWindows", "updateCheck", "appearance");
    }

    [Fact]
    public async Task Save_without_changes_writes_no_update_and_no_audit()
    {
        var harness = await PrivacyHarness.CreateAsync();
        var current = (await SeedSnapshotAsync(harness)).Settings;

        var result = await harness.Service.SaveAsync(current);

        result.Changed.Should().BeFalse();
        harness.Settings.UpdateCount.Should().Be(0);
        harness.Audits.Entries.Should().BeEmpty();
    }

    [Fact]
    public async Task Turning_monitoring_off_keeps_active_web_and_app_protection()
    {
        var harness = await PrivacyHarness.CreateAsync();
        var stored = harness.Settings.Stored!;
        stored.EnableProtection(AuthHarness.Start.UtcDateTime.AddMinutes(1));
        harness.Clock.Advance(TimeSpan.FromMinutes(2));
        var current = (await SeedSnapshotAsync(harness)).Settings;

        await harness.Service.SaveAsync(current with { MonitoringEnabled = false });

        stored.MonitoringEnabled.Should().BeFalse();
        stored.ProtectionEnabled.Should().BeTrue();
        stored.ProtectionSource.Should().Be(ProtectionSource.Manual);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(3601)]
    public async Task Invalid_thresholds_are_rejected_and_not_persisted(int threshold)
    {
        var harness = await PrivacyHarness.CreateAsync();
        var current = (await SeedSnapshotAsync(harness)).Settings;

        var act = () => harness.Service.SaveAsync(current with { DistractionThresholdSeconds = threshold });

        await act.Should().ThrowAsync<ArgumentOutOfRangeException>();
        harness.Settings.UpdateCount.Should().Be(0);
        harness.Audits.Entries.Should().BeEmpty();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(3651)]
    public async Task Invalid_retention_is_rejected_and_not_persisted(int days)
    {
        var harness = await PrivacyHarness.CreateAsync();
        var current = (await SeedSnapshotAsync(harness)).Settings;

        var act = () => harness.Service.SaveAsync(current with { EventRetentionDays = days });

        await act.Should().ThrowAsync<ArgumentOutOfRangeException>();
        harness.Settings.UpdateCount.Should().Be(0);
        harness.Audits.Entries.Should().BeEmpty();
    }

    private static async Task<PrivacySnapshot> SeedSnapshotAsync(PrivacyHarness harness) =>
        await harness.Service.GetSnapshotAsync();
}
