using EduFocus.Ports.Security;
using EduFocus.UseCases.Auth;
using EduFocus.UseCases.Tests.TestSupport;
using FluentAssertions;

namespace EduFocus.UseCases.Tests.Auth;

public sealed class ParentSessionStateTests
{
    [Fact]
    public void A_new_session_is_locked()
    {
        var session = CreateSession(out _);

        session.IsAuthenticated.Should().BeFalse();
    }

    [Fact]
    public void Authenticate_unlocks_the_session()
    {
        var session = CreateSession(out _);

        session.Authenticate();

        session.IsAuthenticated.Should().BeTrue();
    }

    [Fact]
    public void Session_expires_after_five_minutes_without_activity()
    {
        var session = CreateSession(out var clock);
        session.Authenticate();

        clock.Advance(TimeSpan.FromMinutes(4) + TimeSpan.FromSeconds(59));
        session.IsAuthenticated.Should().BeTrue();

        clock.Advance(TimeSpan.FromSeconds(1));
        session.IsAuthenticated.Should().BeFalse();
    }

    [Fact]
    public void RecordActivity_extends_the_idle_deadline()
    {
        var session = CreateSession(out var clock);
        session.Authenticate();

        clock.Advance(TimeSpan.FromMinutes(4) + TimeSpan.FromSeconds(59));
        session.RecordActivity();

        clock.Advance(TimeSpan.FromMinutes(4) + TimeSpan.FromSeconds(59));
        session.IsAuthenticated.Should().BeTrue();

        clock.Advance(TimeSpan.FromSeconds(1));
        session.IsAuthenticated.Should().BeFalse();
    }

    [Fact]
    public void EnsureAuthorized_rejects_a_locked_session()
    {
        var session = CreateSession(out _);

        var act = () => session.EnsureAuthorized("change PIN");

        act.Should().Throw<ParentAuthorizationException>()
            .Which.Operation.Should().Be("change PIN");
    }

    [Fact]
    public void EnsureAuthorized_rejects_an_expired_session()
    {
        var session = CreateSession(out var clock);
        session.Authenticate();
        clock.Advance(TimeSpan.FromMinutes(5));

        var act = () => session.EnsureAuthorized("disable protection");

        act.Should().Throw<ParentAuthorizationException>();
    }

    [Fact]
    public void EnsureAuthorized_accepts_and_touches_an_active_session()
    {
        var session = CreateSession(out var clock);
        session.Authenticate();
        clock.Advance(TimeSpan.FromMinutes(4));

        var act = () => session.EnsureAuthorized("disable protection");

        act.Should().NotThrow();

        clock.Advance(TimeSpan.FromMinutes(4));
        session.IsAuthenticated.Should().BeTrue();
    }

    [Fact]
    public void Lock_immediately_ends_the_session()
    {
        var session = CreateSession(out _);
        session.Authenticate();

        session.Lock();

        session.IsAuthenticated.Should().BeFalse();
    }

    private static ParentSessionState CreateSession(out ManualTimeProvider clock)
    {
        clock = new ManualTimeProvider(AuthHarness.Start);
        return new ParentSessionState(clock);
    }
}
