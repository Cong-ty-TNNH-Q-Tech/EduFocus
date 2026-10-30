using EduFocus.Domain.Attention;
using EduFocus.Domain.Entities;
using EduFocus.Domain.Enums;
using EduFocus.Domain.ValueObjects;
using EduFocus.Ports.DTOs;
using EduFocus.Ports.Inbound;
using EduFocus.UseCases.Protection;
using EduFocus.UseCases.Tests.TestSupport;
using FluentAssertions;

namespace EduFocus.UseCases.Tests.Protection;

public sealed class ServiceMonitoringCoordinatorTests
{
    private static readonly DateTime StartedAtUtc = new(2026, 10, 29, 8, 0, 0, DateTimeKind.Utc);
    private static readonly ProtectionStatus Enabled = new(true, StartedAtUtc, true);
    private static readonly ProtectionStatus Disabled = new(false, null, true);

    private readonly FakeStudySessionRepository _sessions = new();
    private readonly FakeAttentionMonitorUseCase _monitor = new();
    private readonly FakeAttentionPipelineUseCase _pipeline = new();
    private readonly FakeActivityUseCase _activity = new();
    private readonly RecordingLogger _logger = new();

    private ServiceMonitoringCoordinator CreateCoordinator(
        IAttentionMonitorUseCase? monitor = null,
        IAttentionPipelineUseCase? pipeline = null) =>
        new(
            _sessions,
            monitor ?? _monitor,
            pipeline ?? _pipeline,
            _activity,
            _logger);

    [Fact]
    public async Task Enabled_status_starts_attention_and_activity_for_the_active_session()
    {
        var session = AddSession();

        await CreateCoordinator().SyncAsync(Enabled);

        _monitor.Started.Should().Equal(session.Id);
        _monitor.IsRunning.Should().BeTrue();
        _pipeline.StartCalls.Should().Be(1);
        _pipeline.IsRunning.Should().BeTrue();
        _activity.Started.Should().Equal(session.Id);
        _activity.IsRunning.Should().BeTrue();
        _logger.Errors.Should().BeEmpty();
    }

    [Fact]
    public async Task Enabled_status_without_an_active_session_starts_nothing()
    {
        await CreateCoordinator().SyncAsync(Enabled);

        _monitor.Started.Should().BeEmpty();
        _pipeline.StartCalls.Should().Be(0);
        _activity.Started.Should().BeEmpty();
        _logger.Errors.Should().BeEmpty();
    }

    [Fact]
    public async Task Starting_attention_starts_the_monitor_before_the_camera_pipeline()
    {
        AddSession();
        var coordinator = CreateCoordinator(pipeline: new MonitorFirstPipeline(_monitor, _pipeline));

        await coordinator.SyncAsync(Enabled);

        _logger.Errors.Should().BeEmpty();
        _monitor.IsRunning.Should().BeTrue();
        _pipeline.IsRunning.Should().BeTrue();
    }

    [Fact]
    public async Task Stopping_attention_stops_the_pipeline_before_the_monitor()
    {
        AddSession();
        var coordinator = CreateCoordinator(monitor: new PipelineFirstMonitor(_monitor, _pipeline));

        await coordinator.SyncAsync(Enabled);
        await coordinator.SyncAsync(Disabled);

        _logger.Errors.Should().BeEmpty();
        _monitor.IsRunning.Should().BeFalse();
        _pipeline.IsRunning.Should().BeFalse();
    }

    [Fact]
    public async Task Disabled_status_stops_attention_and_activity()
    {
        AddSession();
        var coordinator = CreateCoordinator();
        await coordinator.SyncAsync(Enabled);

        await coordinator.SyncAsync(Disabled);

        _monitor.StopCalls.Should().Be(2);
        _monitor.IsRunning.Should().BeFalse();
        _pipeline.StopCalls.Should().Be(1);
        _pipeline.IsRunning.Should().BeFalse();
        _activity.StopCalls.Should().Be(2);
        _activity.IsRunning.Should().BeFalse();
    }

    [Fact]
    public async Task Repeated_syncs_for_the_same_session_do_not_restart_the_loops()
    {
        AddSession();
        var coordinator = CreateCoordinator();

        await coordinator.SyncAsync(Enabled);
        await coordinator.SyncAsync(Enabled);

        _monitor.Started.Should().ContainSingle();
        _pipeline.StartCalls.Should().Be(1);
        _activity.Started.Should().ContainSingle();
        _activity.IsRunning.Should().BeTrue();
    }

    [Fact]
    public async Task A_session_without_monitoring_keeps_activity_but_not_the_camera()
    {
        AddSession(monitoringEnabled: false);

        await CreateCoordinator().SyncAsync(Enabled);

        _monitor.Started.Should().BeEmpty();
        _pipeline.StartCalls.Should().Be(0);
        _activity.IsRunning.Should().BeTrue();
    }

    [Fact]
    public async Task A_new_active_session_replaces_the_previous_monitoring()
    {
        var first = AddSession();
        var coordinator = CreateCoordinator();
        await coordinator.SyncAsync(Enabled);

        _sessions.Sessions.Clear();
        var second = AddSession();

        await coordinator.SyncAsync(Enabled);

        _monitor.Started.Should().Equal(first.Id, second.Id);
        _monitor.StopCalls.Should().Be(2);
        _pipeline.StartCalls.Should().Be(2);
        _pipeline.StopCalls.Should().Be(1);
        _activity.Started.Should().Equal(first.Id, second.Id);
        _activity.StopCalls.Should().Be(2);
        _activity.IsRunning.Should().BeTrue();
    }

    [Fact]
    public async Task A_session_read_failure_stops_monitoring()
    {
        AddSession();
        var coordinator = CreateCoordinator();
        await coordinator.SyncAsync(Enabled);

        _sessions.ThrowOnGetActive = true;
        await coordinator.SyncAsync(Enabled);

        _monitor.IsRunning.Should().BeFalse();
        _pipeline.IsRunning.Should().BeFalse();
        _activity.IsRunning.Should().BeFalse();
        _logger.Errors.Should().ContainSingle()
            .Which.Should().Contain("active session");
    }

    [Fact]
    public async Task Restore_resumes_monitoring_for_the_session_the_service_kept()
    {
        AddSession();
        var coordinator = CreateCoordinator();
        await coordinator.SyncAsync(Enabled);

        await coordinator.StopAsync();
        _monitor.IsRunning.Should().BeFalse();

        await coordinator.RestoreAsync();

        _monitor.IsRunning.Should().BeTrue();
        _pipeline.IsRunning.Should().BeTrue();
        _activity.IsRunning.Should().BeTrue();
        _monitor.Started.Should().HaveCount(2);
    }

    [Fact]
    public async Task Restore_without_an_active_session_keeps_monitoring_stopped()
    {
        AddSession();
        var coordinator = CreateCoordinator();
        await coordinator.SyncAsync(Enabled);

        _sessions.Sessions.Clear();
        await coordinator.RestoreAsync();

        _monitor.IsRunning.Should().BeFalse();
        _activity.IsRunning.Should().BeFalse();
    }

    [Fact]
    public async Task Stop_is_safe_when_nothing_is_running()
    {
        var coordinator = CreateCoordinator();

        await coordinator.StopAsync();

        _logger.Errors.Should().BeEmpty();
        _monitor.IsRunning.Should().BeFalse();
        _pipeline.IsRunning.Should().BeFalse();
        _activity.IsRunning.Should().BeFalse();
    }

    [Fact]
    public async Task A_failing_camera_pipeline_does_not_leave_the_monitor_running()
    {
        AddSession();
        _pipeline.ThrowOnStart = true;

        await CreateCoordinator().SyncAsync(Enabled);

        _monitor.IsRunning.Should().BeFalse();
        _pipeline.IsRunning.Should().BeFalse();
        _logger.Errors.Should().ContainSingle()
            .Which.Should().Contain("attention");
    }

    [Fact]
    public async Task A_failing_monitor_stop_is_logged_and_activity_still_stops()
    {
        AddSession();
        var coordinator = CreateCoordinator();
        await coordinator.SyncAsync(Enabled);
        _monitor.ThrowOnStop = true;

        await coordinator.SyncAsync(Disabled);

        _logger.Errors.Should().ContainSingle()
            .Which.Should().Contain("attention");
        _pipeline.IsRunning.Should().BeFalse();
        _activity.IsRunning.Should().BeFalse();
    }

    private StudySession AddSession(bool monitoringEnabled = true)
    {
        var session = StudySession.Start(
            StartedAtUtc,
            new DateOnly(2026, 10, 29),
            SessionTrigger.Manual,
            monitoringEnabled);

        _sessions.Sessions.Add(session);
        return session;
    }

    private sealed class MonitorFirstPipeline : IAttentionPipelineUseCase
    {
        private readonly FakeAttentionMonitorUseCase _monitor;
        private readonly FakeAttentionPipelineUseCase _inner;

        public MonitorFirstPipeline(
            FakeAttentionMonitorUseCase monitor,
            FakeAttentionPipelineUseCase inner)
        {
            _monitor = monitor;
            _inner = inner;
        }

        public bool IsRunning => _inner.IsRunning;

        public event EventHandler<AttentionSample>? SampleProduced
        {
            add => _inner.SampleProduced += value;
            remove => _inner.SampleProduced -= value;
        }

        public Task StartAsync(CancellationToken cancellationToken = default) =>
            _monitor.IsRunning
                ? _inner.StartAsync(cancellationToken)
                : throw new InvalidOperationException("The pipeline started before the monitor.");

        public Task StopAsync(CancellationToken cancellationToken = default) =>
            _inner.StopAsync(cancellationToken);

        public Task<AttentionSample> SampleOnceAsync(CancellationToken cancellationToken = default) =>
            _inner.SampleOnceAsync(cancellationToken);
    }

    private sealed class PipelineFirstMonitor : IAttentionMonitorUseCase
    {
        private readonly FakeAttentionMonitorUseCase _inner;
        private readonly FakeAttentionPipelineUseCase _pipeline;

        public PipelineFirstMonitor(
            FakeAttentionMonitorUseCase inner,
            FakeAttentionPipelineUseCase pipeline)
        {
            _inner = inner;
            _pipeline = pipeline;
        }

        public bool IsRunning => _inner.IsRunning;

        public AttentionState? CurrentState => _inner.CurrentState;

        public Task StartAsync(StudySessionId sessionId, CancellationToken cancellationToken = default) =>
            _inner.StartAsync(sessionId, cancellationToken);

        public Task StopAsync(CancellationToken cancellationToken = default) =>
            _pipeline.IsRunning
                ? throw new InvalidOperationException("The monitor stopped before the pipeline.")
                : _inner.StopAsync(cancellationToken);

        public Task<bool> ProcessAsync(AttentionSample sample, CancellationToken cancellationToken = default) =>
            _inner.ProcessAsync(sample, cancellationToken);
    }
}
