namespace EduFocus.Ports.Outbound.Services;

/// <summary>
/// Parent Mode gate for sensitive use cases (system architecture §9.2).
/// Implementations must reject expired sessions and refresh activity time.
/// </summary>
public interface IParentAuthorizer
{
    bool IsAuthenticated { get; }

    void EnsureAuthorized(string operation);

    void RecordActivity();

    void Lock();

    /// <summary>
    /// Enters a temporary scope for an authorized operation (such as downloading
    /// an update) that prevents idle timeout expiration while active.
    /// Returns null if the session is not currently authenticated.
    /// </summary>
    IDisposable? BeginActiveScope() => null;
}
