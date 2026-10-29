using EduFocus.Domain.Entities;
using EduFocus.Domain.Enums;
using EduFocus.Domain.ValueObjects;
using FluentAssertions;

namespace EduFocus.Domain.Tests.Domain;

public class BlockedAttemptTests
{
    private static readonly DateTime Now = new(2026, 10, 29, 7, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void Record_DomainBlockPage_NormalizesAndSanitizes()
    {
        var attempt = BlockedAttempt.Record(
            Now,
            BlockedAttemptTargetType.Domain,
            "ChatGPT.com",
            BlockedAttemptObservedVia.BlockPage,
            url: "https://chatgpt.com/?token=secret#top",
            windowTitle: "  ChatGPT\r\n- Chrome  ");

        attempt.TargetValue.Should().Be("chatgpt.com");
        attempt.Domain!.Value.Should().Be("chatgpt.com");
        attempt.Url.Should().Be("https://chatgpt.com/");
        attempt.WindowTitle.Should().Be("ChatGPT  - Chrome");
        attempt.SessionId.Should().BeNull();
        attempt.TargetType.Should().Be(BlockedAttemptTargetType.Domain);
    }

    [Fact]
    public void Record_AppProcessWatcher_NormalizesProcessName()
    {
        var attempt = BlockedAttempt.Record(
            Now,
            BlockedAttemptTargetType.App,
            @"C:\Games\Game.EXE",
            BlockedAttemptObservedVia.ProcessWatcher);

        attempt.TargetValue.Should().Be("game");
        attempt.ProcessName.Should().Be("game");
        attempt.Domain.Should().BeNull();
        attempt.Url.Should().BeNull();
    }

    [Fact]
    public void Record_ExtensionUrlTarget_StripsQueryAndDerivesDomain()
    {
        var attempt = BlockedAttempt.Record(
            Now,
            BlockedAttemptTargetType.Url,
            "https://chat.openai.com/chat?token=secret",
            BlockedAttemptObservedVia.Extension);

        attempt.TargetValue.Should().Be("https://chat.openai.com/chat");
        attempt.Url.Should().Be("https://chat.openai.com/chat");
        attempt.Domain!.Value.Should().Be("chat.openai.com");
    }

    [Theory]
    [InlineData(BlockedAttemptTargetType.App, BlockedAttemptObservedVia.BlockPage)]
    [InlineData(BlockedAttemptTargetType.Domain, BlockedAttemptObservedVia.ProcessWatcher)]
    [InlineData(BlockedAttemptTargetType.App, BlockedAttemptObservedVia.Extension)]
    public void Record_RejectsMismatchedObservationChannel(
        BlockedAttemptTargetType targetType,
        BlockedAttemptObservedVia observedVia)
    {
        var act = () => BlockedAttempt.Record(Now, targetType, "example.com", observedVia);

        act.Should().Throw<ArgumentException>().WithParameterName("observedVia");
    }

    [Fact]
    public void Record_AcceptsOptionalSessionAndCreationTime()
    {
        var sessionId = StudySessionId.New();

        var attempt = BlockedAttempt.Record(
            Now,
            BlockedAttemptTargetType.Domain,
            "claude.ai",
            BlockedAttemptObservedVia.BlockPage,
            sessionId,
            createdAtUtc: Now.AddSeconds(2));

        attempt.SessionId.Should().Be(sessionId);
        attempt.CreatedAtUtc.Should().Be(Now.AddSeconds(2));
    }

    [Fact]
    public void Record_RejectsCreationBeforeOccurrence()
    {
        var act = () => BlockedAttempt.Record(
            Now,
            BlockedAttemptTargetType.Domain,
            "claude.ai",
            BlockedAttemptObservedVia.BlockPage,
            createdAtUtc: Now.AddSeconds(-1));

        act.Should().Throw<ArgumentException>().WithParameterName("createdAtUtc");
    }

    [Fact]
    public void Record_RejectsEmptySession()
    {
        var act = () => BlockedAttempt.Record(
            Now,
            BlockedAttemptTargetType.Domain,
            "claude.ai",
            BlockedAttemptObservedVia.BlockPage,
            StudySessionId.Empty);

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Record_RejectsInvalidTargets()
    {
        var empty = () => BlockedAttempt.Record(
            Now, BlockedAttemptTargetType.Domain, "  ", BlockedAttemptObservedVia.BlockPage);
        empty.Should().Throw<ArgumentException>();

        var badDomain = () => BlockedAttempt.Record(
            Now, BlockedAttemptTargetType.Domain, "not a domain", BlockedAttemptObservedVia.BlockPage);
        badDomain.Should().Throw<ArgumentException>();

        var badUrl = () => BlockedAttempt.Record(
            Now, BlockedAttemptTargetType.Url, "file:///c:/x", BlockedAttemptObservedVia.Extension);
        badUrl.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Record_RejectsUrlOnAppBlock()
    {
        var act = () => BlockedAttempt.Record(
            Now,
            BlockedAttemptTargetType.App,
            "game.exe",
            BlockedAttemptObservedVia.ProcessWatcher,
            url: "https://example.com/");

        act.Should().Throw<ArgumentException>().WithParameterName("url");
    }
}
