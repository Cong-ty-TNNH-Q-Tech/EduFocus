using EduFocus.Domain.Entities;
using EduFocus.Domain.Enums;
using EduFocus.Infrastructure.ServiceControl;
using EduFocus.Ports.Inbound;
using EduFocus.Ports.ServiceControl;
using EduFocus.Service.Control;
using EduFocus.Service.Runtime;
using EduFocus.Service.Tests.TestSupport;
using FluentAssertions;

namespace EduFocus.Service.Tests;

[Collection("service-runtime")]
public sealed class ServiceRuntimeTests
{
    [Fact]
    public async Task Start_initializes_the_database_and_serves_authenticated_commands()
    {
        await using var temp = new TempDatabase();
        await using var harness = new RuntimeHarness(temp);

        await harness.Runtime.StartAsync();

        File.Exists(temp.DatabasePath).Should().BeTrue();
        harness.Seed.Calls.Should().Be(1);
        harness.Protection.ReconcileCount.Should().Be(1);
        harness.Schedule.EvaluateCount.Should().Be(1);
        harness.Runtime.IsRunning.Should().BeTrue();
        harness.Runtime.StartedAtUtc.Should().NotBeNull();
        harness.Server.IsRunning.Should().BeTrue();
        harness.Audits.Entries.Should().Contain(
            entry => entry.EventType == AuditEventType.ProtectionServiceStarted);

        var client = harness.CreateClient();
        var info = await client.GetServiceInfoAsync();
        var operation = await client.EnableAsync();

        info.Version.Should().NotBeNullOrWhiteSpace();
        info.ProcessId.Should().Be(Environment.ProcessId);
        info.ProtectionEnabled.Should().BeTrue();
        operation.Changed.Should().BeTrue();
        harness.Protection.Commands.Should().Contain(
            command => command.Command == ServiceControlCommand.Enable && command.Authorized);

        await harness.Runtime.StopAsync();

        harness.Runtime.IsRunning.Should().BeFalse();
        harness.Server.IsRunning.Should().BeFalse();
        harness.Audits.Entries.Should().Contain(
            entry => entry.EventType == AuditEventType.ProtectionServiceStopped);
    }

    [Fact]
    public async Task Stop_interrupts_the_active_session_but_never_disables_protection()
    {
        await using var temp = new TempDatabase();
        await using var harness = new RuntimeHarness(temp);
        await harness.Runtime.StartAsync();

        var session = AddActiveSession(harness);

        await harness.Runtime.StopAsync();

        session.Status.Should().Be(StudySessionStatus.Interrupted);
        session.Note.Should().Be("ServiceStopped");
        session.EndedAtUtc.Should().Be(session.LastHeartbeatAtUtc);
        harness.Heartbeat.StopCount.Should().Be(1);
        harness.Protection.DisableCount.Should().Be(0);
        harness.Protection.Commands.Should().NotContain(
            command => command.Command == ServiceControlCommand.Disable
                || command.Command == ServiceControlCommand.DisableForExit);
        harness.Audits.Entries.Should().Contain(
            entry => entry.EventType == AuditEventType.SessionInterrupted);
    }

    [Fact]
    public async Task Stop_is_idempotent_and_can_run_without_a_started_runtime()
    {
        await using var temp = new TempDatabase();
        await using var harness = new RuntimeHarness(temp);

        await harness.Runtime.StopAsync();
        await harness.Runtime.StartAsync();
        await harness.Runtime.StopAsync();
        await harness.Runtime.StopAsync();

        harness.Runtime.IsRunning.Should().BeFalse();
        harness.Server.IsRunning.Should().BeFalse();
    }

    [Fact]
    public async Task A_restarted_service_reuses_the_database_and_reconciles_again()
    {
        await using var temp = new TempDatabase();

        await using (var first = new RuntimeHarness(temp))
        {
            await first.Runtime.StartAsync();
            await first.Runtime.StopAsync();

            first.Protection.ReconcileCount.Should().Be(1);
        }

        await using var second = new RuntimeHarness(temp);
        await second.Runtime.StartAsync();

        File.Exists(temp.DatabasePath).Should().BeTrue();
        second.Seed.Calls.Should().Be(1);
        second.Protection.ReconcileCount.Should().Be(1);
        second.Runtime.IsRunning.Should().BeTrue();
        second.Server.IsRunning.Should().BeTrue();

        await second.Runtime.StopAsync();
    }

    [Fact]
    public async Task A_failed_start_leaves_no_running_runtime_or_pipe()
    {
        await using var temp = new TempDatabase();
        await using var harness = new RuntimeHarness(temp);
        harness.Protection.Failure = new InvalidOperationException("database is unavailable");

        var act = () => harness.Runtime.StartAsync();

        await act.Should().ThrowAsync<InvalidOperationException>();
        harness.Runtime.IsRunning.Should().BeFalse();
        harness.Server.IsRunning.Should().BeFalse();
        harness.Audits.Entries.Should().NotContain(
            entry => entry.EventType == AuditEventType.ProtectionServiceStarted);
    }

    [Fact]
    public async Task Concurrent_requests_reaching_max_clients_do_not_kill_the_pipe_listener()
    {
        await using var temp = new TempDatabase();
        await using var harness = new RuntimeHarness(temp);
        await harness.Runtime.StartAsync();

        var client = harness.CreateClient();
        var tasks = Enumerable.Range(0, 8)
            .Select(_ => client.GetStatusAsync())
            .ToArray();

        var statuses = await Task.WhenAll(tasks);

        statuses.Should().AllSatisfy(s => s.IsEnabled.Should().BeTrue());
        harness.Server.IsRunning.Should().BeTrue();

        var afterBurst = await client.GetServiceInfoAsync();
        afterBurst.ProtectionEnabled.Should().BeTrue();

        await harness.Runtime.StopAsync();
    }

    private static StudySession AddActiveSession(RuntimeHarness harness)
    {
        var now = DateTime.UtcNow;
        var session = StudySession.Start(
            now,
            DateOnly.FromDateTime(now),
            SessionTrigger.Manual,
            monitoringEnabled: true);

        harness.Sessions.Sessions.Add(session);
        return session;
    }

    private sealed class RuntimeHarness : IAsyncDisposable
    {
        public RuntimeHarness(TempDatabase temp)
        {
            Temp = temp;
            PipeName = $"EduFocus.Test.{Guid.NewGuid():N}";

            var status = new ServiceRuntimeStatus();
            var authorizer = new ServiceRequestAuthorizer();
            Protection.Observer = authorizer;
            Handler = new ServiceCommandHandler(
                Protection,
                authorizer,
                status,
                TimeProvider.System);

            Server = new ServiceControlServer(
                new ServiceControlServerOptions
                {
                    PipeName = PipeName,
                    AllowCurrentUser = true,
                    RequestTimeout = TimeSpan.FromSeconds(5)
                },
                Handler,
                new AllowAllVerifier(),
                Logger);

            Runtime = new ServiceRuntime(
                temp.CreateInitializer(Logger),
                Seed,
                Protection,
                Schedule,
                Retention,
                Sessions,
                Heartbeat,
                Audits,
                Correlation,
                Logger,
                TimeProvider.System,
                Server,
                status,
                new ServiceRuntimeOptions
                {
                    RunRetentionAtStartup = false,
                    ScheduleEvaluationInterval = TimeSpan.FromSeconds(30),
                    RetentionCheckInterval = TimeSpan.FromMinutes(1),
                    StopTimeout = TimeSpan.FromSeconds(5)
                });
        }

        public TempDatabase Temp { get; }

        public string PipeName { get; }

        public RecordingLogger Logger { get; } = new();

        public FakeBlockRuleSeedUseCase Seed { get; } = new();

        public RecordingProtectionUseCase Protection { get; } = new();

        public FakeScheduleUseCase Schedule { get; } = new();

        public FakeRetentionUseCase Retention { get; } = new();

        public FakeSessionRepository Sessions { get; } = new();

        public FakeStudySessionHeartbeatUseCase Heartbeat { get; } = new();

        public RecordingAuditLogRepository Audits { get; } = new();

        public TestCorrelationContext Correlation { get; } = new();

        public ServiceCommandHandler Handler { get; }

        public ServiceControlServer Server { get; }

        public ServiceRuntime Runtime { get; }

        public ServiceControlClient CreateClient() =>
            new(new ServiceControlClientOptions
            {
                PipeName = PipeName,
                ConnectTimeout = TimeSpan.FromSeconds(5),
                RequestTimeout = TimeSpan.FromSeconds(5),
                RequireParentAuthorization = false
            });

        public async ValueTask DisposeAsync()
        {
            await Runtime.DisposeAsync();
            await Server.DisposeAsync();
        }
    }

    private sealed class AllowAllVerifier : IPipeClientVerifier
    {
        public bool IsAllowed(System.IO.Pipes.NamedPipeServerStream pipe, out string reason)
        {
            reason = string.Empty;
            return true;
        }
    }
}
