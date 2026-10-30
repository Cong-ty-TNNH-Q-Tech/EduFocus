using EduFocus.Domain.Entities;
using EduFocus.SqliteDataAccess.Repositories;
using EduFocus.SqliteDataAccess.Tests.TestSupport;
using FluentAssertions;

namespace EduFocus.SqliteDataAccess.Tests;

public sealed class AppSettingsRepositoryTests
{
    private static readonly DateTime Now = new(2026, 10, 29, 8, 0, 0, DateTimeKind.Utc);

    [Fact]
    public async Task Get_returns_null_before_settings_are_created()
    {
        await using var db = new TempDatabase();
        await db.CreateInitializer().InitializeAsync();

        var repository = new SqliteAppSettingsRepository(db.ContextFactory);

        (await repository.GetAsync()).Should().BeNull();
    }

    [Fact]
    public async Task Consent_preferences_and_completion_round_trip()
    {
        await using var db = new TempDatabase();
        await db.CreateInitializer().InitializeAsync();

        var repository = new SqliteAppSettingsRepository(db.ContextFactory);
        var settings = AppSettings.CreateDefault(Now);
        await repository.AddAsync(settings);

        settings.RecordPrivacyConsent(Now.AddMinutes(1));
        settings.UpdateMonitoring(false, true, Now.AddMinutes(2));
        settings.UpdateStartWithWindows(false, Now.AddMinutes(3));
        settings.UpdateUpdateCheck(true, Now.AddMinutes(3));
        settings.CompleteOnboarding(Now.AddMinutes(4));
        await repository.UpdateAsync(settings);

        var stored = await repository.GetAsync();

        stored.Should().NotBeNull();
        stored!.HasPrivacyConsent.Should().BeTrue();
        stored.PrivacyAcceptedAtUtc.Should().Be(Now.AddMinutes(1));
        stored.GuardianAcknowledgedAtUtc.Should().Be(Now.AddMinutes(1));
        stored.MonitoringEnabled.Should().BeFalse();
        stored.MonitorOnlyDuringClasses.Should().BeTrue();
        stored.StartWithWindows.Should().BeFalse();
        stored.UpdateCheckEnabled.Should().BeTrue();
        stored.OnboardingCompleted.Should().BeTrue();
        stored.UpdatedAtUtc.Should().Be(Now.AddMinutes(4));
    }

    [Fact]
    public async Task Onboarding_writes_keep_a_single_settings_row()
    {
        await using var db = new TempDatabase();
        await db.CreateInitializer().InitializeAsync();

        var repository = new SqliteAppSettingsRepository(db.ContextFactory);
        var settings = AppSettings.CreateDefault(Now);
        await repository.AddAsync(settings);

        settings.RecordPrivacyConsent(Now.AddMinutes(1));
        await repository.UpdateAsync(settings);
        settings.UpdateStartWithWindows(false, Now.AddMinutes(2));
        await repository.UpdateAsync(settings);

        using var connection = db.OpenConnection();
        Convert.ToInt64(TempDatabase.Scalar(connection, "SELECT COUNT(*) FROM AppSettings;"))
            .Should().Be(1);
    }
}
