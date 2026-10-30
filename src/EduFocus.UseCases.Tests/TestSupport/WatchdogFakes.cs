using EduFocus.Ports.DTOs;
using EduFocus.Ports.Inbound;
using EduFocus.Ports.Outbound.Services;

namespace EduFocus.UseCases.Tests.TestSupport;

internal sealed class FakeWatchdogHeartbeatStore : IWatchdogHeartbeatStore
{
    public WatchdogHeartbeat? Heartbeat { get; set; }

    public int WriteCount { get; private set; }

    public int DeleteCount { get; private set; }

    public Task<WatchdogHeartbeat?> ReadAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult(Heartbeat);

    public Task WriteAsync(WatchdogHeartbeat heartbeat, CancellationToken cancellationToken = default)
    {
        Heartbeat = heartbeat;
        WriteCount++;
        return Task.CompletedTask;
    }

    public Task DeleteAsync(CancellationToken cancellationToken = default)
    {
        Heartbeat = null;
        DeleteCount++;
        return Task.CompletedTask;
    }
}

internal sealed class FakeWatchdogStateStore : IWatchdogStateStore
{
    public WatchdogRestartState? State { get; set; }

    public int WriteCount { get; private set; }

    public Task<WatchdogRestartState?> ReadAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult(State);

    public Task WriteAsync(WatchdogRestartState state, CancellationToken cancellationToken = default)
    {
        State = state;
        WriteCount++;
        return Task.CompletedTask;
    }

    public Task DeleteAsync(CancellationToken cancellationToken = default)
    {
        State = null;
        return Task.CompletedTask;
    }
}

internal sealed class FakeWatchdogProcessPort : IWatchdogProcessPort
{
    public bool AppInstanceRunning { get; set; }

    public HashSet<long> AliveProcesses { get; } = [];

    public bool KillSucceeds { get; set; } = true;

    public WatchdogProcessInfo? StartResult { get; set; } = new(4321, new DateTime(2026, 10, 29, 8, 0, 0, DateTimeKind.Utc));

    public int StartCount { get; private set; }

    public List<long> Killed { get; } = [];

    public bool IsAppInstanceRunning() => AppInstanceRunning;

    public bool IsProcessAlive(long processId, DateTime processStartedAtUtc) =>
        AliveProcesses.Contains(processId);

    public bool TryKill(long processId, DateTime processStartedAtUtc)
    {
        Killed.Add(processId);

        if (!KillSucceeds)
            return false;

        AliveProcesses.Remove(processId);
        return true;
    }

    public WatchdogProcessInfo? StartApp()
    {
        StartCount++;
        return StartResult;
    }
}

internal sealed class FakeWatchdogRuntimePort : IWatchdogRuntimePort
{
    public bool Running { get; set; }

    public bool EnsureStartedResult { get; set; } = true;

    public bool StopResult { get; set; } = true;

    public int EnsureStartedCount { get; private set; }

    public int StopCount { get; private set; }

    public int SessionEndingCount { get; private set; }

    public bool IsRunning => Running;

    public bool EnsureStarted()
    {
        EnsureStartedCount++;

        if (EnsureStartedResult)
            Running = true;

        return EnsureStartedResult;
    }

    public void NotifySessionEnding() => SessionEndingCount++;

    public Task<bool> RequestStopAsync(
        TimeSpan timeout,
        CancellationToken cancellationToken = default)
    {
        StopCount++;

        if (StopResult)
            Running = false;

        return Task.FromResult(StopResult);
    }
}

internal sealed class FakeWatchdogAutoStartPort : IWatchdogAutoStartPort
{
    public List<WatchdogRegistrationMode> Modes { get; } = [];

    public Exception? ThrowOnSynchronize { get; set; }

    public Task SynchronizeAsync(
        WatchdogRegistrationMode mode,
        CancellationToken cancellationToken = default)
    {
        Modes.Add(mode);

        if (ThrowOnSynchronize is not null)
            throw ThrowOnSynchronize;

        return Task.CompletedTask;
    }
}

internal sealed class FakeWatchdogCoordinator : IWatchdogCoordinator
{
    public int SynchronizeCount { get; private set; }

    public Task SynchronizeAsync(CancellationToken cancellationToken = default)
    {
        SynchronizeCount++;
        return Task.CompletedTask;
    }
}

internal sealed class FakeProtectionUseCase : IProtectionUseCase
{
    public ProtectionOperationResult Result { get; set; } = new(
        new ProtectionStatus(false, null, true),
        Changed: true,
        Warnings: []);

    public int Calls { get; private set; }

    public Task<ProtectionStatus> GetStatusAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult(Result.Status);

    public Task<ProtectionOperationResult> EnableAsync(CancellationToken cancellationToken = default) =>
        Complete();

    public Task<ProtectionOperationResult> DisableAsync(CancellationToken cancellationToken = default) =>
        Complete();

    public Task<ProtectionOperationResult> DisableForExitAsync(CancellationToken cancellationToken = default) =>
        Complete();

    public Task<ProtectionOperationResult> ReconcileAsync(CancellationToken cancellationToken = default) =>
        Complete();

    public Task<ProtectionOperationResult> EnableForScheduleAsync(CancellationToken cancellationToken = default) =>
        Complete();

    public Task<ProtectionOperationResult> DisableForScheduleAsync(CancellationToken cancellationToken = default) =>
        Complete();

    private Task<ProtectionOperationResult> Complete()
    {
        Calls++;
        return Task.FromResult(Result);
    }
}
