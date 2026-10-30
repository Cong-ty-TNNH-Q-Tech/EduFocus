using EduFocus.Ports.DTOs;
using EduFocus.Ports.Security;
using EduFocus.UseCases.Auth;
using EduFocus.UseCases.Tests.TestSupport;
using FluentAssertions;

namespace EduFocus.UseCases.Tests.Auth;

public sealed class ParentModeServiceTests
{
    [Fact]
    public async Task Successful_login_starts_a_parent_session()
    {
        var harness = await AuthHarness.WithCredentialAsync();
        var session = new ParentSessionState(harness.Clock);
        var mode = new ParentModeService(harness.UseCase, session);

        var result = await mode.LoginAsync(AuthHarness.InitialPin);

        result.Status.Should().Be(ParentAuthStatus.Success);
        mode.IsAuthenticated.Should().BeTrue();
        session.IsAuthenticated.Should().BeTrue();
    }

    [Fact]
    public async Task Failed_login_does_not_start_a_session()
    {
        var harness = await AuthHarness.WithCredentialAsync();
        var session = new ParentSessionState(harness.Clock);
        var mode = new ParentModeService(harness.UseCase, session);

        var result = await mode.LoginAsync("482916");

        result.Status.Should().Be(ParentAuthStatus.InvalidCredential);
        mode.IsAuthenticated.Should().BeFalse();
    }

    [Fact]
    public async Task ChangePin_requires_an_active_parent_session()
    {
        var harness = await AuthHarness.WithCredentialAsync();
        var session = new ParentSessionState(harness.Clock);
        var mode = new ParentModeService(harness.UseCase, session);

        var act = () => mode.ChangePinAsync(AuthHarness.InitialPin, AuthHarness.ReplacementPin);

        await act.Should().ThrowAsync<ParentAuthorizationException>();
        harness.Credential.FailedAttempts.Should().Be(0);
    }

    [Fact]
    public async Task Successful_change_PIN_ends_the_session()
    {
        var harness = await AuthHarness.WithCredentialAsync();
        var session = new ParentSessionState(harness.Clock);
        var mode = new ParentModeService(harness.UseCase, session);
        await mode.LoginAsync(AuthHarness.InitialPin);

        var result = await mode.ChangePinAsync(AuthHarness.InitialPin, AuthHarness.ReplacementPin);

        result.Status.Should().Be(ParentAuthStatus.Success);
        mode.IsAuthenticated.Should().BeFalse();
        (await harness.UseCase.VerifyPinAsync(AuthHarness.ReplacementPin)).Status
            .Should().Be(ParentAuthStatus.Success);
    }

    [Fact]
    public async Task Failed_change_PIN_keeps_the_session_active()
    {
        var harness = await AuthHarness.WithCredentialAsync();
        var session = new ParentSessionState(harness.Clock);
        var mode = new ParentModeService(harness.UseCase, session);
        await mode.LoginAsync(AuthHarness.InitialPin);

        var result = await mode.ChangePinAsync("482916", AuthHarness.ReplacementPin);

        result.Status.Should().Be(ParentAuthStatus.InvalidCredential);
        mode.IsAuthenticated.Should().BeTrue();
    }

    [Fact]
    public async Task Recovery_reset_always_leaves_the_parent_logged_out()
    {
        var harness = await AuthHarness.WithCredentialAsync();
        var session = new ParentSessionState(harness.Clock);
        var mode = new ParentModeService(harness.UseCase, session);
        await mode.LoginAsync(AuthHarness.InitialPin);

        var result = await mode.ResetPinWithRecoveryCodeAsync(
            AuthHarness.FirstRecoveryCode,
            AuthHarness.ReplacementPin);

        result.Status.Should().Be(ParentAuthStatus.Success);
        result.RecoveryCode.Should().Be(AuthHarness.SecondRecoveryCode);
        mode.IsAuthenticated.Should().BeFalse();
    }

    [Fact]
    public async Task Record_activity_extends_the_session()
    {
        var harness = await AuthHarness.WithCredentialAsync();
        var session = new ParentSessionState(harness.Clock);
        var mode = new ParentModeService(harness.UseCase, session);
        await mode.LoginAsync(AuthHarness.InitialPin);

        harness.Clock.Advance(TimeSpan.FromMinutes(4));
        mode.RecordActivity();
        harness.Clock.Advance(TimeSpan.FromMinutes(4));

        mode.IsAuthenticated.Should().BeTrue();
        mode.Lock();
        mode.IsAuthenticated.Should().BeFalse();
    }

    [Fact]
    public async Task IsConfiguredAsync_reflects_credential_state()
    {
        var harness = new AuthHarness();
        var mode = new ParentModeService(harness.UseCase, new ParentSessionState(harness.Clock));

        (await mode.IsConfiguredAsync()).Should().BeFalse();

        await harness.UseCase.SetupAsync(AuthHarness.InitialPin);

        (await mode.IsConfiguredAsync()).Should().BeTrue();
    }
}
