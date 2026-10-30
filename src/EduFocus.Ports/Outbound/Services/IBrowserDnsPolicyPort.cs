namespace EduFocus.Ports.Outbound.Services;

/// <summary>
/// Disables browser Secure DNS (DNS-over-HTTPS) while protection is ON so
/// Chromium browsers resolve through Windows and hosts-based blocks keep
/// working. Apply is idempotent and keeps the previous policy safe; restore
/// puts that policy back exactly, including removing a value that did not
/// exist before. Implementations must survive crashes between apply and
/// restore, and report non-fatal problems as warnings (UC03, UC04).
/// </summary>
public interface IBrowserDnsPolicyPort
{
    Task<IReadOnlyList<string>> ApplyAsync(CancellationToken cancellationToken = default);

    Task<IReadOnlyList<string>> RestoreAsync(CancellationToken cancellationToken = default);
}
