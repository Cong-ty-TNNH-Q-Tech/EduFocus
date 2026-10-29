namespace EduFocus.Ports.Outbound.Services;

/// <summary>Outcome of writing the EduFocus block section to the hosts file.</summary>
public sealed record HostsApplyResult(
    bool Changed,
    int DomainCount,
    IReadOnlyList<string> Domains,
    string BackupPath);

/// <summary>Outcome of removing the EduFocus block section from the hosts file.</summary>
public sealed record HostsRestoreResult(bool BlockRemoved, bool RestoredExact);

/// <summary>Outcome of repairing a crash or an orphaned block section.</summary>
public sealed record HostsRecoveryResult(bool Performed, bool FromJournal);

/// <summary>
/// Maintains the EduFocus block section in the Windows hosts file (UC09,
/// system architecture §6.2). Implementations only touch lines between the
/// EduFocus markers, are idempotent and preserve all other content. Failures
/// must surface as <see cref="EduFocus.Ports.Protection.HostsFileException"/> so
/// the protection state machine can report an error instead of silently
/// pretending success.
/// </summary>
public interface IHostsFileBlocker
{
    Task<HostsApplyResult> ApplyAsync(
        IReadOnlyCollection<string> domains,
        CancellationToken cancellationToken = default);

    Task<HostsRestoreResult> RestoreAsync(CancellationToken cancellationToken = default);

    Task<HostsRecoveryResult> SelfHealAsync(CancellationToken cancellationToken = default);
}
