using EduFocus.Infrastructure.Persistence;
using EduFocus.Domain.Entities;
using EduFocus.Domain.Enums;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace EduFocus.Infrastructure.Persistence.Tests;

public sealed class OnboardingServiceTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 26, 10, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Successful_onboarding_requires_consent_credentials_and_preferences_then_marks_complete()
    {
        await using var fixture = await Fixture.Create();
        var service = new OnboardingService(fixture.Db);
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.CompleteAsync(Now));
        await service.AcceptPrivacyNoticeAsync(Now);
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.SavePreferencesAsync(false, null, false, Now));
        await new ParentCredentialService(fixture.Db).SetUpAsync("482951", Now);
        await service.SavePreferencesAsync(false, null, false, Now.AddMinutes(1));
        Assert.False((await service.GetProgressAsync()).Completed);

        await service.CompleteAsync(Now.AddMinutes(2));
        var progress = await service.GetProgressAsync();
        Assert.True(progress.PrivacyAccepted);
        Assert.True(progress.CredentialsConfigured);
        Assert.True(progress.PreferencesSaved);
        Assert.True(progress.Completed);
        Assert.False(progress.MonitoringEnabled);
        Assert.False(progress.StartWithWindows);
        Assert.Null(progress.CameraIndex);
        Assert.Contains(await fixture.Db.AuditLogs.ToArrayAsync(), x => x.EventType == "OnboardingCompleted");
    }

    [Fact]
    public async Task Cancelling_after_consent_preserves_that_step_without_unlocking_or_creating_credentials()
    {
        await using var fixture = await Fixture.Create();
        var service = new OnboardingService(fixture.Db);
        await service.AcceptPrivacyNoticeAsync(Now);
        // Closing the wizard performs no rollback or completion write.
        var progress = await service.GetProgressAsync();
        Assert.True(progress.PrivacyAccepted);
        Assert.False(progress.CredentialsConfigured);
        Assert.False(progress.Completed);
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.SavePreferencesAsync(false, null, false, Now));
    }

    [Fact]
    public async Task Incomplete_setup_resumes_after_reopening_database_context()
    {
        await using var fixture = await Fixture.Create();
        var onboarding = new OnboardingService(fixture.Db);
        await onboarding.AcceptPrivacyNoticeAsync(Now);
        var credentials = new ParentCredentialService(fixture.Db);
        var setup = await credentials.SetUpAsync("482951", Now.AddMinutes(1));
        Assert.False(string.IsNullOrWhiteSpace(setup.RecoveryCode));
        await onboarding.SavePreferencesAsync(true, 2, false, Now.AddMinutes(2));

        var options = new DbContextOptionsBuilder<AppDbContext>().UseSqlite(fixture.Connection).Options;
        await using var restartedDb = new AppDbContext(options);
        var resumed = await new OnboardingService(restartedDb).GetProgressAsync();
        Assert.True(resumed.PrivacyAccepted);
        Assert.True(resumed.CredentialsConfigured);
        Assert.True(resumed.PreferencesSaved);
        Assert.False(resumed.Completed);
        Assert.True(resumed.MonitoringEnabled);
        Assert.Equal(2, resumed.CameraIndex);
        Assert.False(resumed.StartWithWindows);

        await new OnboardingService(restartedDb).CompleteAsync(Now.AddMinutes(3));
        Assert.True((await new OnboardingService(restartedDb).GetProgressAsync()).Completed);
    }

    [Fact]
    public async Task Monitoring_cannot_be_enabled_without_a_camera_and_safe_defaults_are_stored()
    {
        await using var fixture = await Fixture.Create();
        var onboarding = new OnboardingService(fixture.Db);
        await onboarding.AcceptPrivacyNoticeAsync(Now);
        await new ParentCredentialService(fixture.Db).SetUpAsync("482951", Now);
        await Assert.ThrowsAsync<ArgumentException>(() => onboarding.SavePreferencesAsync(true, null, false, Now));
        await onboarding.SavePreferencesAsync(false, null, false, Now);
        var settings = await fixture.Db.AppSettings.SingleAsync();
        Assert.False(settings.MonitoringEnabled);
        Assert.False(settings.StartWithWindows);
        Assert.False(settings.OnboardingCompleted);
    }

    [Fact]
    public async Task Migration_turns_off_monitoring_and_auto_start_for_incomplete_setup()
    {
        var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<AppDbContext>().UseSqlite(connection).Options;
        await using var db = new AppDbContext(options);
        await db.Database.MigrateAsync("20261026125509_InitialEduFocusSchema");
        var settings = AppSettings.Create(Now);
        settings.Configure(true, false, false, "0", 20, 30, 15, 30, 90, 365, true,
            ThemePreference.Dark, LanguagePreference.Vietnamese, false, Now);
        db.AppSettings.Add(settings);
        await db.SaveChangesAsync();

        await db.Database.MigrateAsync();
        await db.Entry(settings).ReloadAsync();
        var migrated = await db.AppSettings.SingleAsync();
        Assert.False(migrated.MonitoringEnabled);
        Assert.False(migrated.StartWithWindows);
        Assert.False(migrated.OnboardingCompleted);
        await connection.DisposeAsync();
    }

    private sealed class Fixture(SqliteConnection connection, AppDbContext db) : IAsyncDisposable
    {
        public SqliteConnection Connection { get; } = connection;
        public AppDbContext Db { get; } = db;

        public static async Task<Fixture> Create()
        {
            var connection = new SqliteConnection("Data Source=:memory:");
            await connection.OpenAsync();
            var options = new DbContextOptionsBuilder<AppDbContext>().UseSqlite(connection).Options;
            var db = new AppDbContext(options);
            await db.Database.MigrateAsync();
            return new(connection, db);
        }

        public async ValueTask DisposeAsync()
        {
            await Db.DisposeAsync();
            await Connection.DisposeAsync();
        }
    }
}
