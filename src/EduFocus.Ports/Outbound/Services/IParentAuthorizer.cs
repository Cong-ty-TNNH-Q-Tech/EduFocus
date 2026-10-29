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
}
