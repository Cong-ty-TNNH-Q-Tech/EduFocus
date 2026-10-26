using EduFocus.Domain.Entities;
using EduFocus.Domain.Enums;
using EduFocus.Infrastructure.Persistence;
using EduFocus.Ports.Outbound.Services;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace EduFocus.Infrastructure.Persistence.Tests;

public sealed class ActivityTrackingServiceTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 26, 10, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Foreground_changes_are_debounced_and_sensitive_url_data_is_not_saved()
    {
        Assert.Equal(ActivityCategory.Study, ActivityTrackingService.ClassifyProcess("winword"));
        await using var fixture = await Fixture.Create();
        var session = StudySession.Start(null, StudySessionTrigger.Manual, true, new DateOnly(2026, 10, 26), Now);
        fixture.Db.StudySessions.Add(session);
        await fixture.Db.SaveChangesAsync();
        var foreground = new FakeForeground(
            Snapshot("winword", "lesson.docx"), Snapshot("chrome", "rapid change"), Snapshot("winword", "lesson.docx"));
        using var provider = fixture.CreateServices(foreground);
        var tracker = new ActivityTrackingService(provider.GetRequiredService<IServiceScopeFactory>(), foreground, new FakeLogger());

        await tracker.ScanOnceAsync(Now);
        await tracker.ScanOnceAsync(Now.AddSeconds(1));
        await tracker.ScanOnceAsync(Now.AddSeconds(2));
        await tracker.ScanOnceAsync(Now.AddSeconds(3));
        await tracker.ScanOnceAsync(Now.AddSeconds(4));
        await tracker.ScanOnceAsync(Now.AddSeconds(19));

        fixture.Db.ChangeTracker.Clear();
        var samples = await fixture.Db.ActivitySamples.ToArrayAsync();
        var categories = await fixture.Db.Database.SqlQueryRaw<string>("SELECT Category AS Value FROM ActivitySamples").ToArrayAsync();
        Assert.Equal(2, samples.Length);
        Assert.All(samples, sample =>
        {
            Assert.Equal(session.Id, sample.SessionId);
            Assert.Equal(ActivityUrlSource.None, sample.UrlSource);
            Assert.Null(sample.Url);
        });
        Assert.True(samples.All(x => x.Category == ActivityCategory.Study), string.Join(",", samples.Select((x, i) => $"{x.Category}:{x.ProcessName}:{categories[i]}")));
        Assert.Equal("winword", samples[0].ProcessName);
        Assert.Equal("lesson.docx", samples[0].WindowTitle);
    }

    [Fact]
    public async Task Long_idle_period_creates_only_one_sample_and_resumes_after_user_activity()
    {
        await using var fixture = await Fixture.Create();
        fixture.Db.StudySessions.Add(StudySession.Start(null, StudySessionTrigger.Manual, false, new DateOnly(2026, 10, 26), Now));
        await fixture.Db.SaveChangesAsync();
        var foreground = new FakeForeground(
            Snapshot("winword", "lesson", 301), Snapshot("winword", "lesson", 900),
            Snapshot("winword", "lesson", 20), Snapshot("winword", "lesson", 20), Snapshot("winword", "lesson", 301));
        using var provider = fixture.CreateServices(foreground);
        var tracker = new ActivityTrackingService(provider.GetRequiredService<IServiceScopeFactory>(), foreground, new FakeLogger());

        await tracker.ScanOnceAsync(Now);
        await tracker.ScanOnceAsync(Now.AddSeconds(15));
        await tracker.ScanOnceAsync(Now.AddSeconds(30));
        await tracker.ScanOnceAsync(Now.AddSeconds(32));
        await tracker.ScanOnceAsync(Now.AddSeconds(45));

        Assert.Equal(2, await fixture.Db.ActivitySamples.CountAsync(x => !x.IsForeground));
        Assert.Equal(1, await fixture.Db.ActivitySamples.CountAsync(x => x.IsForeground));
    }

    [Fact]
    public async Task Missing_or_disappearing_foreground_process_does_not_crash_or_create_sample()
    {
        await using var fixture = await Fixture.Create();
        fixture.Db.StudySessions.Add(StudySession.Start(null, StudySessionTrigger.Manual, false, new DateOnly(2026, 10, 26), Now));
        await fixture.Db.SaveChangesAsync();
        var foreground = new FakeForeground(new InvalidOperationException("simulated process exit"));
        using var provider = fixture.CreateServices(foreground);
        var tracker = new ActivityTrackingService(provider.GetRequiredService<IServiceScopeFactory>(), foreground, new FakeLogger());

        await tracker.ScanOnceAsync(Now);

        Assert.Empty(await fixture.Db.ActivitySamples.ToArrayAsync());
    }

    [Fact]
    public void Online_class_detection_requires_platform_specific_meeting_evidence()
    {
        Assert.True(ActivityTrackingService.IsOnlineClass(Snapshot("chrome", "Google Meet - Math")));
        Assert.True(ActivityTrackingService.IsOnlineClass(Snapshot("ms-teams", "Math meeting | Microsoft Teams")));
        Assert.True(ActivityTrackingService.IsOnlineClass(Snapshot("Zoom", "Zoom Meeting")));
        Assert.False(ActivityTrackingService.IsOnlineClass(Snapshot("ms-teams", "Microsoft Teams | Chat")));
        Assert.False(ActivityTrackingService.IsOnlineClass(Snapshot("discord", "General chat")));
    }

    [Fact]
    public async Task Online_class_time_is_an_overlay_and_monitoring_eligibility_respects_setting()
    {
        await using var fixture = await Fixture.Create();
        var settings = await fixture.Db.AppSettings.SingleAsync();
        settings.Configure(false, false, true, null, 20, 30, 15, 30, 90, 365, false,
            ThemePreference.Dark, LanguagePreference.Vietnamese, false, Now);
        fixture.Db.StudySessions.Add(StudySession.Start(null, StudySessionTrigger.Manual, true, new DateOnly(2026, 10, 26), Now));
        await fixture.Db.SaveChangesAsync();
        var foreground = new FakeForeground(Snapshot("ms-teams", "Math meeting | Microsoft Teams"));
        using var provider = fixture.CreateServices(foreground);
        var tracker = new ActivityTrackingService(provider.GetRequiredService<IServiceScopeFactory>(), foreground, new FakeLogger());

        await tracker.ScanOnceAsync(Now);
        await tracker.ScanOnceAsync(Now.AddSeconds(2));
        await tracker.ScanOnceAsync(Now.AddSeconds(17));

        fixture.Db.ChangeTracker.Clear();
        var session = await fixture.Db.StudySessions.SingleAsync();
        var samples = await fixture.Db.ActivitySamples.ToArrayAsync();
        Assert.Equal(15, session.OnlineClassSeconds);
        Assert.Equal(0, session.StudySeconds);
        Assert.Equal(15, session.MonitoringEligibleSeconds);
        Assert.All(samples, sample => Assert.True(sample.IsOnlineClass));
    }

    private static ForegroundActivitySnapshot Snapshot(string process, string title, long idle = 0) =>
        new(process, process, title, null, ActivityUrlSource.None, idle);

    private sealed class FakeForeground(params object[] values) : IForegroundActivityPort
    {
        private int _index;
        public ForegroundActivitySnapshot Capture()
        {
            var value = values[Math.Min(_index++, values.Length - 1)];
            if (value is Exception exception) throw exception;
            return (ForegroundActivitySnapshot)value;
        }
    }

    private sealed class FakeLogger : IAppLogger
    {
        public void Info(string message) { }
        public void Warning(string message) { }
        public void Error(string message, Exception? ex = null) { }
    }

    private sealed class Fixture(SqliteConnection connection, AppDbContext db, DbContextOptions<AppDbContext> options) : IAsyncDisposable
    {
        public AppDbContext Db { get; } = db;
        public static async Task<Fixture> Create()
        {
            var connection = new SqliteConnection("Data Source=:memory:");
            await connection.OpenAsync();
            var options = new DbContextOptionsBuilder<AppDbContext>().UseSqlite(connection).Options;
            var db = new AppDbContext(options);
            await db.Database.MigrateAsync();
            db.AppSettings.Add(AppSettings.Create(Now));
            await db.SaveChangesAsync();
            return new(connection, db, options);
        }
        public ServiceProvider CreateServices(IForegroundActivityPort foreground)
        {
            var services = new ServiceCollection();
            services.AddScoped(_ => new AppDbContext(options));
            services.AddSingleton(foreground);
            return services.BuildServiceProvider();
        }
        public async ValueTask DisposeAsync() { await Db.DisposeAsync(); await connection.DisposeAsync(); }
    }
}
