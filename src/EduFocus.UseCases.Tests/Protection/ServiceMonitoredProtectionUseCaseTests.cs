using EduFocus.Domain.Entities;
using EduFocus.Domain.Enums;
using EduFocus.Ports.DTOs;
using EduFocus.Ports.Inbound;
using EduFocus.Ports.Protection;
using EduFocus.UseCases.Protection;
using EduFocus.UseCases.Tests.TestSupport;
using FluentAssertions;

namespace EduFocus.UseCases.Tests.Protection;

public sealed class ServiceMonitoredProtectionUseCaseTests
{
    private static readonly DateTime StartedAtUtc = new(2026, 10, 29, 8, 0, 0, DateTimeKind.Utc);
    private static readonly ProtectionStatus Enabled = new(true, StartedAtUtc, true);
    private static readonly ProtectionStatus Disabled = new(false, null, true);

    private readonly FakeStudySessionRepository _sessions = new();
    private readonly FakeAttentionMonitorUseCase _monitor = new();
    private readonly FakeAttentionPipelineUseCase _pipeline = new();
    private readonly FakeActivityUseCase _activity = new();
    private readonly RecordingLogger _logger = new();
    private readonly RecordingProtectionUseCase _inner = new();
    private readonly ServiceMonitoredProtectionUseCase _decorator;

    public ServiceMonitoredProtectionUseCaseTests()
    {
        var monitoring = new ServiceMonitoringCoordinator(
            _sessions,
            _monitor,
            _pipeline,
            _activity,
            _logger);

        _decorator = new ServiceMonitoredProtectionUseCase(_inner, monitoring);
    }

    [Fact]
    public async Task Enable_forwards_first_and_starts_monitoring_after_the_service_created_the_session()
    {
        var monitoringWasRunningAtCall = false;

        _inner.OnCommand = command =>
        {
            monitoringWasRunningAtCall = _monitor.IsRunning || _activity.IsRunning;
            AddSession();
            return Task.FromResult(Result(Enabled));
        };

        var result = await _decorator.EnableAsync();

        result.Status.IsEnabled.Should().BeTrue();
        _inner.Commands.Should().Equal("enable");
        monitoringWasRunningAtCall.Should().BeFalse();
        _monitor.IsRunning.Should().BeTrue();
        _pipeline.IsRunning.Should().BeTrue();
        _activity.IsRunning.Should().BeTrue();
        _logger.Errors.Should().BeEmpty();
    }

    [Theory]
    [InlineData("disable")]
    [InlineData("disable-for-exit")]
    [InlineData("disable-for-schedule")]
    public async Task Remove_commands_stop_monitoring_before_the_service_call(string command)
    {
        await EnableAsync();
        var monitoringWasRunningAtCall = true;

        _inner.OnCommand = _ =>
        {
            monitoringWasRunningAtCall = _monitor.IsRunning
                || _pipeline.IsRunning
                || _activity.IsRunning;
            _sessions.Sessions.Clear();
            return Task.FromResult(Result(Disabled));
        };

        await RunRemoveAsync(command);

        monitoringWasRunningAtCall.Should().BeFalse();
        _monitor.IsRunning.Should().BeFalse();
        _pipeline.IsRunning.Should().BeFalse();
        _activity.IsRunning.Should().BeFalse();
        _inner.Commands.Should().Equal("enable", command);
    }

    [Fact]
    public async Task A_failed_disable_resumes_monitoring_for_the_session_the_service_kept()
    {
        await EnableAsync();
        _inner.Failure = new ProtectionEngineException("remove", "The engine could not stop.");

        var act = () => _decorator.DisableAsync();

        await act.Should().ThrowAsync<ProtectionEngineException>();
        _monitor.IsRunning.Should().BeTrue();
        _pipeline.IsRunning.Should().BeTrue();
        _activity.IsRunning.Should().BeTrue();
        _monitor.Started.Should().HaveCount(2);
        _logger.Errors.Should().BeEmpty();
    }

    [Theory]
    [InlineData("reconcile")]
    [InlineData("enable-for-schedule")]
    public async Task Apply_commands_sync_monitoring_to_the_service_result(string command)
    {
        _inner.OnCommand = _ =>
        {
            AddSession();
            return Task.FromResult(Result(Enabled));
        };

        await RunApplyAsync(command);

        _inner.Commands.Should().Equal(command);
        _monitor.IsRunning.Should().BeTrue();
        _activity.IsRunning.Should().BeTrue();
    }

    [Fact]
    public async Task A_disabled_result_leaves_monitoring_stopped()
    {
        AddSession();

        _inner.OnCommand = _ => Task.FromResult(Result(Disabled));

        await _decorator.ReconcileAsync();

        _monitor.IsRunning.Should().BeFalse();
        _activity.IsRunning.Should().BeFalse();
    }

    [Fact]
    public async Task Get_status_is_forwarded_without_touching_monitoring()
    {
        AddSession();

        var status = await _decorator.GetStatusAsync();

        status.Should().Be(Enabled);
        _inner.StatusCalls.Should().Be(1);
        _monitor.Started.Should().BeEmpty();
        _activity.Started.Should().BeEmpty();
    }

    private async Task EnableAsync()
    {
        _inner.OnCommand = _ =>
        {
            AddSession();
            return Task.FromResult(Result(Enabled));
        };

        await _decorator.EnableAsync();
    }

    private Task<ProtectionOperationResult> RunApplyAsync(string command) => command switch
    {
        "reconcile" => _decorator.ReconcileAsync(),
        "enable-for-schedule" => _decorator.EnableForScheduleAsync(),
        _ => throw new ArgumentOutOfRangeException(nameof(command))
    };

    private Task<ProtectionOperationResult> RunRemoveAsync(string command) => command switch
    {
        "disable" => _decorator.DisableAsync(),
        "disable-for-exit" => _decorator.DisableForExitAsync(),
        "disable-for-schedule" => _decorator.DisableForScheduleAsync(),
        _ => throw new ArgumentOutOfRangeException(nameof(command))
    };

    private void AddSession()
    {
        var session = StudySession.Start(
            StartedAtUtc,
            new DateOnly(2026, 10, 29),
            SessionTrigger.Manual,
            monitoringEnabled: true);

        _sessions.Sessions.Add(session);
    }

    private static ProtectionOperationResult Result(ProtectionStatus status) =>
        new(status, Changed: true, Warnings: []);

    private sealed class RecordingProtectionUseCase : IProtectionUseCase
    {
        public List<string> Commands { get; } = [];

        public int StatusCalls { get; private set; }

        public Func<string, Task<ProtectionOperationResult>>? OnCommand { get; set; }

        public Exception? Failure { get; set; }

        public Task<ProtectionStatus> GetStatusAsync(CancellationToken cancellationToken = default)
        {
            StatusCalls++;
            return Task.FromResult(Enabled);
        }

        public Task<ProtectionOperationResult> EnableAsync(CancellationToken cancellationToken = default) =>
            Run("enable");

        public Task<ProtectionOperationResult> DisableAsync(CancellationToken cancellationToken = default) =>
            Run("disable");

        public Task<ProtectionOperationResult> DisableForExitAsync(CancellationToken cancellationToken = default) =>
            Run("disable-for-exit");

        public Task<ProtectionOperationResult> ReconcileAsync(CancellationToken cancellationToken = default) =>
            Run("reconcile");

        public Task<ProtectionOperationResult> EnableForScheduleAsync(CancellationToken cancellationToken = default) =>
            Run("enable-for-schedule");

        public Task<ProtectionOperationResult> DisableForScheduleAsync(CancellationToken cancellationToken = default) =>
            Run("disable-for-schedule");

        private Task<ProtectionOperationResult> Run(string command)
        {
            Commands.Add(command);

            if (Failure is not null)
                return Task.FromException<ProtectionOperationResult>(Failure);

            return OnCommand?.Invoke(command)
                ?? Task.FromResult(Result(Disabled));
        }
    }
}
