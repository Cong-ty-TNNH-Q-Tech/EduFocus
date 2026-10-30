using EduFocus.Ports.DTOs;
using EduFocus.Ports.Protection;
using EduFocus.Ports.Security;
using EduFocus.Ports.ServiceControl;
using EduFocus.Service.Control;
using EduFocus.Service.Runtime;
using EduFocus.Service.Tests.TestSupport;
using FluentAssertions;

namespace EduFocus.Service.Tests;

public sealed class ServiceCommandHandlerTests
{
    private static readonly DateTime SessionStartUtc =
        new(2026, 10, 29, 8, 0, 0, DateTimeKind.Utc);

    private readonly RecordingProtectionUseCase _protection = new();
    private readonly ServiceRequestAuthorizer _authorizer = new();
    private readonly ServiceRuntimeStatus _status = new();
    private readonly ServiceCommandHandler _handler;

    public ServiceCommandHandlerTests()
    {
        _protection.Observer = _authorizer;
        _status.MarkStarted(DateTimeOffset.UtcNow);
        _handler = new ServiceCommandHandler(
            _protection,
            _authorizer,
            _status,
            TimeProvider.System);
    }

    [Fact]
    public async Task Ping_reports_the_running_service_identity()
    {
        var response = await Handle(ServiceControlCommand.Ping);

        response.Ok.Should().BeTrue();
        response.Service.Should().NotBeNull();
        response.Service!.Version.Should().NotBeNullOrWhiteSpace();
        response.Service.ProcessId.Should().Be(Environment.ProcessId);
        response.Service.StartedAtUtc.Should().Be(_status.StartedAtUtc!.Value);
        response.Service.ProtectionEnabled.Should().BeTrue();
        response.Service.ActiveSessionStartedAtUtc.Should().Be(SessionStartUtc);
        response.Status.Should().BeNull();
    }

    [Fact]
    public async Task Status_returns_the_current_protection_status()
    {
        var response = await Handle(ServiceControlCommand.Status);

        response.Ok.Should().BeTrue();
        response.Status!.IsEnabled.Should().BeTrue();
        response.Service.Should().NotBeNull();
    }

    [Theory]
    [InlineData(ServiceControlCommand.Enable)]
    [InlineData(ServiceControlCommand.Disable)]
    [InlineData(ServiceControlCommand.DisableForExit)]
    public async Task Parent_commands_run_inside_an_authorized_scope(string command)
    {
        var response = await Handle(command);

        response.Ok.Should().BeTrue();
        response.Operation.Should().NotBeNull();
        _protection.Commands.Should().ContainSingle();
        _protection.Commands[0].Command.Should().Be(command);
        _protection.Commands[0].Authorized.Should().BeTrue();
        _authorizer.IsAuthenticated.Should().BeFalse();
    }

    [Theory]
    [InlineData(ServiceControlCommand.Reconcile)]
    [InlineData(ServiceControlCommand.EnableForSchedule)]
    [InlineData(ServiceControlCommand.DisableForSchedule)]
    public async Task System_commands_run_without_parent_mode(string command)
    {
        var response = await Handle(command);

        response.Ok.Should().BeTrue();
        _protection.Commands.Should().ContainSingle();
        _protection.Commands[0].Command.Should().Be(command);
        _protection.Commands[0].Authorized.Should().BeFalse();
    }

    [Fact]
    public async Task An_incompatible_protocol_is_rejected_before_dispatch()
    {
        var response = await _handler.HandleAsync(
            new ServiceControlRequest(ServiceControlProtocol.Version + 1, ServiceControlCommand.Enable));

        response.Ok.Should().BeFalse();
        response.ErrorCode.Should().Be(ServiceControlErrorCode.UnsupportedProtocol);
        _protection.Commands.Should().BeEmpty();
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("shutdown-machine")]
    public async Task Unknown_or_blank_commands_are_rejected(string command)
    {
        var response = await Handle(command);

        response.Ok.Should().BeFalse();
        response.ErrorCode.Should().Be(ServiceControlErrorCode.UnknownCommand);
        _protection.Commands.Should().BeEmpty();
    }

    [Fact]
    public async Task A_null_request_is_an_internal_error()
    {
        var response = await _handler.HandleAsync(null!);

        response.Ok.Should().BeFalse();
        response.ErrorCode.Should().Be(ServiceControlErrorCode.InternalError);
    }

    [Fact]
    public async Task A_parent_authorization_failure_maps_to_unauthorized()
    {
        _protection.Failure = new ParentAuthorizationException("disable protection");

        var response = await Handle(ServiceControlCommand.Disable);

        response.Ok.Should().BeFalse();
        response.ErrorCode.Should().Be(ServiceControlErrorCode.Unauthorized);
        response.Error.Should().NotContain("Parent Mode is required");
        _authorizer.IsAuthenticated.Should().BeFalse();
    }

    [Fact]
    public async Task An_engine_failure_maps_to_engine_error()
    {
        _protection.Failure = new ProtectionEngineException("apply", "hosts write failed");

        var response = await Handle(ServiceControlCommand.Enable);

        response.Ok.Should().BeFalse();
        response.ErrorCode.Should().Be(ServiceControlErrorCode.EngineError);
        response.Error.Should().NotContain("hosts write failed");
    }

    [Fact]
    public async Task Missing_settings_map_to_settings_missing()
    {
        _protection.Failure = new InvalidOperationException("onboarding is incomplete");

        var response = await Handle(ServiceControlCommand.Enable);

        response.Ok.Should().BeFalse();
        response.ErrorCode.Should().Be(ServiceControlErrorCode.SettingsMissing);
        response.Error.Should().NotContain("onboarding is incomplete");
    }

    [Fact]
    public async Task An_unexpected_failure_maps_to_a_sanitized_internal_error()
    {
        _protection.Failure = new IOException(@"C:\ProgramData\EduFocus\edufocus.db is locked");

        var response = await Handle(ServiceControlCommand.Reconcile);

        response.Ok.Should().BeFalse();
        response.ErrorCode.Should().Be(ServiceControlErrorCode.InternalError);
        response.Error.Should().NotContain("edufocus.db");
        _authorizer.IsAuthenticated.Should().BeFalse();
    }

    [Fact]
    public async Task Cancellation_stops_the_command_and_does_not_return_a_failure()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        _protection.Failure = new OperationCanceledException();

        var act = () => _handler.HandleAsync(
            new ServiceControlRequest(ServiceControlProtocol.Version, ServiceControlCommand.Enable),
            cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    private Task<ServiceControlResponse> Handle(string command) =>
        _handler.HandleAsync(new ServiceControlRequest(ServiceControlProtocol.Version, command));
}
