namespace EduFocus.Ports.Outbound.Services;

public interface IBlockedAttemptRecorder
{
    Task RecordBlockedAppAsync(string processName, DateTimeOffset occurredAtUtc, CancellationToken cancellationToken = default);
    Task RecordBlockedDomainAsync(string domain, DateTimeOffset occurredAtUtc, CancellationToken cancellationToken = default);
}
