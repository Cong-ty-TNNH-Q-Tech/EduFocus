using EduFocus.Domain.Entities;
using EduFocus.Domain.Enums;
using EduFocus.Domain.ValueObjects;
using FluentAssertions;

namespace EduFocus.Domain.Tests.Domain;

public class ActivitySampleTests
{
    private static readonly DateTime Now = new(2026, 10, 29, 7, 0, 0, DateTimeKind.Utc);
    private static readonly StudySessionId Session = StudySessionId.New();

    [Fact]
    public void Capture_SanitizesUrlAndDerivesDomain()
    {
        var sample = Capture(
            appName: "Chrome",
            processName: "chrome",
            windowTitle: "Online class",
            url: "https://user:pw@www.example.com/class?token=secret#frag",
            urlSource: UrlSource.UIA);

        sample.Url.Should().Be("https://www.example.com/class");
        sample.Domain!.Value.Should().Be("www.example.com");
        sample.AppName.Should().Be("Chrome");
        sample.ProcessName.Should().Be("chrome");
        sample.IsForeground.Should().BeTrue();
        sample.Category.Should().Be(ActivityCategory.Study);
    }

    [Fact]
    public void Capture_CleansAndTruncatesWindowTitle()
    {
        var dirty = Capture(windowTitle: "  Lesson\r\nwith\tcontrols  ");
        dirty.WindowTitle.Should().Be("Lesson  with controls");

        var longTitle = Capture(windowTitle: new string('x', ActivitySample.MaxWindowTitleLength + 50));
        longTitle.WindowTitle.Should().HaveLength(ActivitySample.MaxWindowTitleLength);
    }

    [Fact]
    public void Capture_RequiresUrlSourceConsistency()
    {
        var missingSource = () => Capture(url: "https://example.com/lesson", urlSource: UrlSource.None);
        missingSource.Should().Throw<ArgumentException>();

        var missingUrl = () => Capture(url: null, urlSource: UrlSource.Extension);
        missingUrl.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Capture_RejectsUnsupportedUrls()
    {
        var act = () => Capture(url: "file:///C:/secret.txt", urlSource: UrlSource.UIA);

        act.Should().Throw<ArgumentException>().WithParameterName("url");
    }

    [Fact]
    public void Capture_RejectsIdleLongerThanDuration()
    {
        var act = () => Capture(durationSeconds: 10, idleSeconds: 11);

        act.Should().Throw<ArgumentOutOfRangeException>().WithParameterName("idleSeconds");
    }

    [Fact]
    public void Capture_RejectsCreationBeforeSampling()
    {
        var act = () => ActivitySample.Capture(
            Session,
            Now,
            10,
            ActivityCategory.Study,
            true,
            false,
            createdAtUtc: Now.AddSeconds(-1));

        act.Should().Throw<ArgumentException>().WithParameterName("createdAtUtc");
    }

    [Fact]
    public void Capture_RejectsEmptySessionAndUndefinedEnums()
    {
        var emptySession = () => ActivitySample.Capture(
            StudySessionId.Empty, Now, 10, ActivityCategory.Study, true, false);
        emptySession.Should().Throw<ArgumentException>();

        var badCategory = () => ActivitySample.Capture(
            Session, Now, 10, (ActivityCategory)99, true, false);
        badCategory.Should().Throw<ArgumentOutOfRangeException>();

        var badSource = () => ActivitySample.Capture(
            Session, Now, 10, ActivityCategory.Study, true, false, urlSource: (UrlSource)99);
        badSource.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void Capture_WithoutUrl_LeavesDomainEmpty()
    {
        var sample = Capture();

        sample.Url.Should().BeNull();
        sample.Domain.Should().BeNull();
        sample.UrlSource.Should().Be(UrlSource.None);
    }

    private static ActivitySample Capture(
        int durationSeconds = 15,
        int idleSeconds = 0,
        string? appName = null,
        string? processName = null,
        string? windowTitle = null,
        string? url = null,
        UrlSource urlSource = UrlSource.None) =>
        ActivitySample.Capture(
            Session,
            Now,
            durationSeconds,
            ActivityCategory.Study,
            isForeground: true,
            isOnlineClass: false,
            idleSeconds,
            appName,
            processName,
            windowTitle,
            url,
            urlSource);
}
