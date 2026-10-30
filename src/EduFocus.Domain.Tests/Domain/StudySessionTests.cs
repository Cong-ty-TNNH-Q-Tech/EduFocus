using EduFocus.Domain.Attention;
using EduFocus.Domain.Entities;
using EduFocus.Domain.Enums;
using FluentAssertions;

namespace EduFocus.Domain.Tests.Domain;

public class StudySessionTests
{
    private static readonly DateTime Now = new(2026, 10, 29, 7, 0, 0, DateTimeKind.Utc);
    private static readonly DateOnly LocalDate = new(2026, 10, 29);

    [Fact]
    public void Start_InitializesActiveSession()
    {
        var session = Start();

        session.Status.Should().Be(StudySessionStatus.Active);
        session.IsActive.Should().BeTrue();
        session.StartedAtUtc.Should().Be(Now);
        session.LastHeartbeatAtUtc.Should().Be(Now);
        session.StartLocalDate.Should().Be(LocalDate);
        session.Trigger.Should().Be(SessionTrigger.Manual);
        session.MonitoringEnabled.Should().BeTrue();
        session.ActiveSeconds.Should().Be(0);
        session.MonitoredSeconds.Should().Be(0);
        session.AttentionPercent.Should().BeNull();
        session.CoveragePercent.Should().BeNull();
        session.EndedAtUtc.Should().BeNull();
    }

    [Fact]
    public void Start_RejectsInvalidArguments()
    {
        var defaultDate = () => StudySession.Start(Now, default, SessionTrigger.Manual, true);
        defaultDate.Should().Throw<ArgumentException>();

        var badTrigger = () => StudySession.Start(Now, LocalDate, (SessionTrigger)99, true);
        badTrigger.Should().Throw<ArgumentOutOfRangeException>();

        var local = () => StudySession.Start(
            new DateTime(2026, 10, 29, 7, 0, 0, DateTimeKind.Local),
            LocalDate,
            SessionTrigger.Manual,
            true);
        local.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Heartbeat_UpdatesLastHeartbeat()
    {
        var session = Start();

        session.Heartbeat(Now.AddMinutes(1));

        session.LastHeartbeatAtUtc.Should().Be(Now.AddMinutes(1));
        session.UpdatedAtUtc.Should().Be(Now.AddMinutes(1));
    }

    [Fact]
    public void Heartbeat_CannotMoveBackwards()
    {
        var session = Start();
        session.Heartbeat(Now.AddMinutes(5));

        var act = () => session.Heartbeat(Now.AddMinutes(4));

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void AttentionCounters_ExcludeCameraUnavailableFromMonitoredTime()
    {
        var session = Start();
        session.AddMonitoringEligibleSeconds(600, Now.AddSeconds(10));

        session.AddAttentionSeconds(AttentionState.Focused, 300, Now.AddSeconds(20));
        session.AddAttentionSeconds(AttentionState.Distracted, 60, Now.AddSeconds(30));
        session.AddAttentionSeconds(AttentionState.Absent, 30, Now.AddSeconds(40));
        session.AddAttentionSeconds(AttentionState.Drowsy, 10, Now.AddSeconds(50));
        session.AddAttentionSeconds(AttentionState.CameraUnavailable, 20, Now.AddSeconds(60));

        session.MonitoredSeconds.Should().Be(400);
        session.CameraUnavailableSeconds.Should().Be(20);
        session.AttentionPercent.Should().BeApproximately(75.0, 0.001);
        session.CoveragePercent.Should().BeApproximately(66.667, 0.01);
    }

    [Fact]
    public void Percentages_AreNullWhenDenominatorsAreZero()
    {
        var session = Start();

        session.AttentionPercent.Should().BeNull();
        session.CoveragePercent.Should().BeNull();

        session.AddAttentionSeconds(AttentionState.Focused, 10, Now.AddSeconds(1));

        session.AttentionPercent.Should().Be(100);
        session.CoveragePercent.Should().BeNull();
    }

    [Fact]
    public void CategoryCounters_SeparateStudyDistractionAndNeutral()
    {
        var session = Start();

        session.AddCategorySeconds(ActivityCategory.Study, 100, Now.AddSeconds(1));
        session.AddCategorySeconds(ActivityCategory.Distraction, 20, Now.AddSeconds(2));
        session.AddCategorySeconds(ActivityCategory.Neutral, 5, Now.AddSeconds(3));
        session.AddCategorySeconds(ActivityCategory.Unknown, 5, Now.AddSeconds(4));
        session.AddOnlineClassSeconds(120, Now.AddSeconds(5));
        session.AddIdleSeconds(30, Now.AddSeconds(6));

        session.StudySeconds.Should().Be(100);
        session.DistractionSeconds.Should().Be(20);
        session.NeutralSeconds.Should().Be(10);
        session.OnlineClassSeconds.Should().Be(120);
        session.IdleSeconds.Should().Be(30);
    }

    [Fact]
    public void ActiveAndBlockedCounters_Accumulate()
    {
        var session = Start();

        session.AddActiveSeconds(90, Now.AddSeconds(1));
        session.IncrementBlockedAttempts(Now.AddSeconds(2));
        session.IncrementBlockedAttempts(Now.AddSeconds(3), 2);

        session.ActiveSeconds.Should().Be(90);
        session.BlockedAttemptCount.Should().Be(3);
    }

    [Fact]
    public void Counters_RejectNegativeValues()
    {
        var session = Start();

        var act = () => session.AddActiveSeconds(-1, Now.AddSeconds(1));
        act.Should().Throw<ArgumentOutOfRangeException>();

        var idle = () => session.AddIdleSeconds(-1, Now.AddSeconds(2));
        idle.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void Confidence_MustBeBetweenZeroAndOne()
    {
        var session = Start();

        session.SetAttentionConfidence(0.85, Now.AddSeconds(1));
        session.ConfidenceAvg.Should().Be(0.85);

        var tooHigh = () => session.SetAttentionConfidence(1.2, Now.AddSeconds(2));
        tooHigh.Should().Throw<ArgumentOutOfRangeException>();

        session.SetAttentionConfidence(null, Now.AddSeconds(3));
        session.ConfidenceAvg.Should().BeNull();
    }

    [Fact]
    public void Note_IsSanitizedAndLengthLimited()
    {
        var session = Start();

        session.SetNote("  first\r\nsecond  ", Now.AddSeconds(1));
        session.Note.Should().Be("first  second");

        var tooLong = () => session.SetNote(new string('n', StudySession.MaxNoteLength + 1), Now.AddSeconds(2));
        tooLong.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Complete_ClosesSessionNormally()
    {
        var session = Start();
        session.Heartbeat(Now.AddMinutes(5));

        session.Complete(Now.AddMinutes(10));

        session.Status.Should().Be(StudySessionStatus.Completed);
        session.IsActive.Should().BeFalse();
        session.EndedAtUtc.Should().Be(Now.AddMinutes(10));
        session.LastHeartbeatAtUtc.Should().Be(Now.AddMinutes(10));
    }

    [Fact]
    public void Abort_ClosesSessionAsAborted()
    {
        var session = Start();

        session.Abort(Now.AddMinutes(3));

        session.Status.Should().Be(StudySessionStatus.Aborted);
    }

    [Fact]
    public void Interrupt_ClosesAtLastHeartbeatWithoutCountingDowntime()
    {
        var session = Start();
        session.Heartbeat(Now.AddMinutes(5));
        session.AddActiveSeconds(120, Now.AddMinutes(6));

        session.Interrupt();

        session.Status.Should().Be(StudySessionStatus.Interrupted);
        session.EndedAtUtc.Should().Be(Now.AddMinutes(5));
        session.ActiveSeconds.Should().Be(120);
    }

    [Fact]
    public void Session_CannotBeClosedTwice()
    {
        var session = Start();
        session.Complete(Now.AddMinutes(1));

        var complete = () => session.Complete(Now.AddMinutes(2));
        complete.Should().Throw<InvalidOperationException>();

        var abort = () => session.Abort(Now.AddMinutes(2));
        abort.Should().Throw<InvalidOperationException>();

        var interrupt = () => session.Interrupt();
        interrupt.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void EndBeforeHeartbeat_IsRejected()
    {
        var session = Start();
        session.Heartbeat(Now.AddMinutes(5));

        var act = () => session.Complete(Now.AddMinutes(4));

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void MutationsAfterClose_Throw()
    {
        var session = Start();
        session.Complete(Now.AddMinutes(1));

        var heartbeat = () => session.Heartbeat(Now.AddMinutes(2));
        heartbeat.Should().Throw<InvalidOperationException>();

        var active = () => session.AddActiveSeconds(10, Now.AddMinutes(2));
        active.Should().Throw<InvalidOperationException>();

        var note = () => session.SetNote("late", Now.AddMinutes(2));
        note.Should().Throw<InvalidOperationException>();
    }

    private static StudySession Start() =>
        StudySession.Start(Now, LocalDate, SessionTrigger.Manual, monitoringEnabled: true);
}
