using EduFocus.Domain.Attention;
using EduFocus.Domain.Entities;
using EduFocus.Domain.ValueObjects;
using FluentAssertions;

namespace EduFocus.Domain.Tests.Domain;

public class AttentionEventTests
{
    private static readonly DateTime Now = new(2026, 10, 29, 7, 0, 0, DateTimeKind.Utc);
    private static readonly StudySessionId Session = StudySessionId.New();

    [Fact]
    public void OpenAndClose_ComputesDuration()
    {
        var attentionEvent = AttentionEvent.Open(
            Session,
            AttentionState.Focused,
            Now,
            confidence: 0.9,
            appName: "Chrome",
            domain: DomainName.Parse("meet.google.com"));

        attentionEvent.IsOpen.Should().BeTrue();
        attentionEvent.DurationSeconds.Should().Be(0);

        attentionEvent.Close(Now.AddSeconds(30));

        attentionEvent.IsOpen.Should().BeFalse();
        attentionEvent.EndedAtUtc.Should().Be(Now.AddSeconds(30));
        attentionEvent.DurationSeconds.Should().Be(30);
        attentionEvent.State.Should().Be(AttentionState.Focused);
        attentionEvent.Domain!.Value.Should().Be("meet.google.com");
    }

    [Fact]
    public void Close_CannotRunTwice()
    {
        var attentionEvent = AttentionEvent.Open(Session, AttentionState.Distracted, Now);
        attentionEvent.Close(Now.AddSeconds(5));

        var act = () => attentionEvent.Close(Now.AddSeconds(10));

        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void Close_RejectsEndBeforeStart()
    {
        var attentionEvent = AttentionEvent.Open(Session, AttentionState.Absent, Now);

        var act = () => attentionEvent.Close(Now.AddSeconds(-1));

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Open_RejectsInvalidArguments()
    {
        var badConfidence = () => AttentionEvent.Open(
            Session, AttentionState.Focused, Now, confidence: 1.5);
        badConfidence.Should().Throw<ArgumentOutOfRangeException>();

        var badState = () => AttentionEvent.Open(Session, (AttentionState)99, Now);
        badState.Should().Throw<ArgumentOutOfRangeException>();

        var emptySession = () => AttentionEvent.Open(StudySessionId.Empty, AttentionState.Focused, Now);
        emptySession.Should().Throw<ArgumentException>();

        var longNote = () => AttentionEvent.Open(
            Session, AttentionState.Focused, Now, note: new string('n', AttentionEvent.MaxNoteLength + 1));
        longNote.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Open_CleansAppNameAndNote()
    {
        var attentionEvent = AttentionEvent.Open(
            Session,
            AttentionState.Drowsy,
            Now,
            appName: new string('a', AttentionEvent.MaxAppNameLength + 10),
            note: "  camera\r\nbusy  ");

        attentionEvent.AppName.Should().HaveLength(AttentionEvent.MaxAppNameLength);
        attentionEvent.Note.Should().Be("camera  busy");
    }
}
