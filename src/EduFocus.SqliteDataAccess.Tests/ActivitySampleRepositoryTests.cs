using EduFocus.Domain.Entities;
using EduFocus.Domain.Enums;
using EduFocus.Domain.ValueObjects;
using EduFocus.Ports.DTOs;
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
    public async Task Report_query_skips_idle_time_and_exposes_only_app_and_domain()
    {
        await using var db = new TempDatabase();
        await db.CreateInitializer().InitializeAsync();

        var sessions = new SqliteStudySessionRepository(db.Options);
        var session = StudySession.Start(Now, new DateOnly(2026, 10, 30), SessionTrigger.Manual, true);
        var other = StudySession.Start(Now.AddMinutes(1), new DateOnly(2026, 10, 30), SessionTrigger.Manual, true);
        await sessions.AddAsync(session);
        other.Complete(Now.AddMinutes(2));
        await sessions.AddAsync(other);

        var repository = new SqliteActivitySampleRepository(db.Options);

        await repository.AddAsync(ActivitySample.Capture(
            session.Id,
            Now.AddSeconds(15),
            durationSeconds: 15,
            ActivityCategory.Study,
            isForeground: true,
            isOnlineClass: false,
            appName: "Notepad"));
        await repository.AddAsync(ActivitySample.Capture(
            session.Id,
            Now.AddSeconds(30),
            durationSeconds: 20,
            ActivityCategory.Neutral,
            isForeground: true,
            isOnlineClass: false,
            idleSeconds: 20,
            appName: "Desktop"));
        await repository.AddAsync(ActivitySample.Capture(
            session.Id,
            Now.AddSeconds(45),
            durationSeconds: 30,
            ActivityCategory.Distraction,
            isForeground: true,
            isOnlineClass: false,
            idleSeconds: 5,
            appName: "Google Chrome",
            processName: "chrome",
            windowTitle: "Secret video title",
            url: "https://youtube.com/watch?v=secret#fragment",
            urlSource: UrlSource.UIA));
        await repository.AddAsync(ActivitySample.Capture(
            other.Id,
            Now.AddMinutes(2),
            durationSeconds: 10,
            ActivityCategory.Study,
            isForeground: true,
            isOnlineClass: false,
            appName: "Notepad"));

        var samples = await repository.ListForReportAsync([session.Id], maxCount: 10);

        samples.Should().HaveCount(2);
        samples[0].SampledAtUtc.Should().Be(Now.AddSeconds(45));
        samples[0].ActiveSeconds.Should().Be(25);
        samples[0].Category.Should().Be(ActivityCategory.Distraction);
        samples[0].AppName.Should().Be("Google Chrome");
        samples[0].Domain.Should().Be(DomainName.Parse("youtube.com"));
        samples[1].SampledAtUtc.Should().Be(Now.AddSeconds(15));
        samples[1].ActiveSeconds.Should().Be(15);

        typeof(ReportActivitySample).GetProperties()
            .Select(property => property.Name)
            .Should().NotContain(new[] { "WindowTitle", "Url", "ProcessName" });
    }

    [Fact]
    public async Task Report_query_respects_the_limit_and_handles_empty_sessions()
    {
        await using var db = new TempDatabase();
        await db.CreateInitializer().InitializeAsync();

        var sessions = new SqliteStudySessionRepository(db.Options);
        var session = StudySession.Start(Now, new DateOnly(2026, 10, 30), SessionTrigger.Manual, true);
        await sessions.AddAsync(session);

        var repository = new SqliteActivitySampleRepository(db.Options);

        foreach (var offset in new[] { 15, 30, 45 })
        {
            await repository.AddAsync(ActivitySample.Capture(
                session.Id,
                Now.AddSeconds(offset),
                durationSeconds: 10,
                ActivityCategory.Study,
                isForeground: true,
                isOnlineClass: false,
                appName: "Notepad"));
        }

        var limited = await repository.ListForReportAsync([session.Id], maxCount: 2);
        limited.Should().HaveCount(2);
        limited[0].SampledAtUtc.Should().Be(Now.AddSeconds(45));
        limited[1].SampledAtUtc.Should().Be(Now.AddSeconds(30));

        var empty = await repository.ListForReportAsync([], maxCount: 10);
        empty.Should().BeEmpty();

        var invalid = () => repository.ListForReportAsync([session.Id], maxCount: 0);
        await invalid.Should().ThrowAsync<ArgumentOutOfRangeException>();
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

    [Fact]
    public async Task Online_class_sample_round_trips()
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
            ActivityCategory.Study,
            isForeground: true,
            isOnlineClass: true,
            idleSeconds: 0,
            appName: "Microsoft Teams",
            processName: "ms-teams",
            windowTitle: "Microsoft Teams");

        await repository.AddAsync(sample);

        await using var context = db.CreateContext();
        var stored = await context.ActivitySamples.SingleAsync();

        stored.IsOnlineClass.Should().BeTrue();
        stored.Category.Should().Be(ActivityCategory.Study);
        stored.ProcessName.Should().Be("ms-teams");
    }
}
