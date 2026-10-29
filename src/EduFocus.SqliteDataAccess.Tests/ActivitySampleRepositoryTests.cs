using EduFocus.Domain.Entities;
using EduFocus.Domain.Enums;
using EduFocus.Domain.ValueObjects;
using EduFocus.SqliteDataAccess.Repositories;
using EduFocus.SqliteDataAccess.Tests.TestSupport;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;

namespace EduFocus.SqliteDataAccess.Tests;

public sealed class ActivitySampleRepositoryTests
{
    private static readonly DateTime Now = new(2026, 10, 30, 8, 0, 0, DateTimeKind.Utc);

    [Fact]
    public async Task Sample_round_trips_with_sanitized_url_and_privacy_fields()
    {
        await using var db = new TempDatabase();
        await db.CreateInitializer().InitializeAsync();

        var sessions = new SqliteStudySessionRepository(db.Options);
        var session = StudySession.Start(Now, new DateOnly(2026, 10, 30), SessionTrigger.Manual, true);
        await sessions.AddAsync(session);

        var repository = new SqliteActivitySampleRepository(db.Options);
        var sample = ActivitySample.Capture(
            session.Id,
            Now.AddSeconds(15),
            durationSeconds: 15,
            ActivityCategory.Distraction,
            isForeground: true,
            isOnlineClass: false,
            idleSeconds: 2,
            appName: "Google Chrome",
            processName: "chrome",
            windowTitle: "ChatGPT",
            url: "https://chatgpt.com/c/abc?token=secret#section",
            urlSource: UrlSource.UIA);

        await repository.AddAsync(sample);

        await using var context = db.CreateContext();
        var stored = await context.ActivitySamples.SingleAsync();

        stored.Id.Should().Be(sample.Id);
        stored.SessionId.Should().Be(session.Id);
        stored.SampledAtUtc.Should().Be(Now.AddSeconds(15));
        stored.DurationSeconds.Should().Be(15);
        stored.AppName.Should().Be("Google Chrome");
        stored.ProcessName.Should().Be("chrome");
        stored.WindowTitle.Should().Be("ChatGPT");
        stored.Url.Should().Be("https://chatgpt.com/c/abc");
        stored.Domain.Should().Be(DomainName.Parse("chatgpt.com"));
        stored.UrlSource.Should().Be(UrlSource.UIA);
        stored.Category.Should().Be(ActivityCategory.Distraction);
        stored.IsForeground.Should().BeTrue();
        stored.IsOnlineClass.Should().BeFalse();
        stored.IdleSeconds.Should().Be(2);
    }

    [Fact]
    public async Task Sample_without_url_stores_none_source()
    {
        await using var db = new TempDatabase();
        await db.CreateInitializer().InitializeAsync();

        var sessions = new SqliteStudySessionRepository(db.Options);
        var session = StudySession.Start(Now, new DateOnly(2026, 10, 30), SessionTrigger.Manual, true);
        await sessions.AddAsync(session);

        var repository = new SqliteActivitySampleRepository(db.Options);
        var sample = ActivitySample.Capture(
            session.Id,
            Now.AddSeconds(15),
            durationSeconds: 15,
            ActivityCategory.Neutral,
            isForeground: true,
            isOnlineClass: false,
            processName: "notepad");

        await repository.AddAsync(sample);

        await using var context = db.CreateContext();
        var stored = await context.ActivitySamples.SingleAsync();

        stored.Url.Should().BeNull();
        stored.Domain.Should().BeNull();
        stored.UrlSource.Should().Be(UrlSource.None);
        stored.Category.Should().Be(ActivityCategory.Neutral);
    }
}
