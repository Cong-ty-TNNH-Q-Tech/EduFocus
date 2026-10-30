namespace EduFocus.Ports.ServiceControl;

/// <summary>
/// Transport or protocol failure while talking to the protection service.
/// Errors returned by the service are mapped to this exception only when they
/// have no more specific Ports exception (for example
/// <c>ProtectionEngineException</c>).
/// </summary>
public sealed class ServiceControlException : Exception
{
    public ServiceControlException(string code, string message, Exception? innerException = null)
        : base(message, innerException)
    {
        Code = code;
    }

    public string Code { get; }
}
