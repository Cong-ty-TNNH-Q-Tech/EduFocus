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

    [Fact]
    public void BeginActiveScope_returns_null_when_session_is_locked()
    {
        var session = CreateSession(out _);

        using var scope = session.BeginActiveScope();

        scope.Should().BeNull();
    }

    [Fact]
    public void BeginActiveScope_keeps_session_alive_past_idle_timeout()
    {
        var session = CreateSession(out var clock);
        session.Authenticate();

        using (var scope = session.BeginActiveScope())
        {
            scope.Should().NotBeNull();

            // Advance well past default 5-minute timeout (e.g. 15 minutes of download time)
            clock.Advance(TimeSpan.FromMinutes(15));

            session.IsAuthenticated.Should().BeTrue();
            var act = () => session.EnsureAuthorized("exit EduFocus");
            act.Should().NotThrow();
        }

        // After scope disposal, a full 5-minute idle window begins
        clock.Advance(TimeSpan.FromMinutes(4) + TimeSpan.FromSeconds(59));
        session.IsAuthenticated.Should().BeTrue();

        clock.Advance(TimeSpan.FromSeconds(2));
        session.IsAuthenticated.Should().BeFalse();
    }

    [Fact]
    public void Lock_immediately_ends_session_even_with_active_scope()
    {
        var session = CreateSession(out var clock);
        session.Authenticate();

        var scope = session.BeginActiveScope();
        scope.Should().NotBeNull();

        session.Lock();
        session.IsAuthenticated.Should().BeFalse();

        // Disposing afterwards should not revive the session
        scope!.Dispose();
        session.IsAuthenticated.Should().BeFalse();
    }

    [Fact]
    public void Multiple_active_scopes_maintain_session_until_all_disposed()
    {
        var session = CreateSession(out var clock);
        session.Authenticate();

        var scope1 = session.BeginActiveScope();
        clock.Advance(TimeSpan.FromMinutes(10));
        var scope2 = session.BeginActiveScope();

        scope1?.Dispose();

        // scope2 is still alive
        clock.Advance(TimeSpan.FromMinutes(10));
        session.IsAuthenticated.Should().BeTrue();

        scope2?.Dispose();

        // Now idle timeout applies
        clock.Advance(TimeSpan.FromMinutes(5));
        session.IsAuthenticated.Should().BeFalse();
    }

    private static ParentSessionState CreateSession(out ManualTimeProvider clock)
    {
        clock = new ManualTimeProvider(AuthHarness.Start);
        return new ParentSessionState(clock);
    }
}
