namespace EduFocus.Ports.Protection;

public enum HostsFileFailureReason
{
    NotFound,
    AccessDenied,
    Locked,
    WriteFailed
}

/// <summary>
/// Raised when the hosts file cannot be read or written. Callers must keep the
/// persisted protection state unchanged and surface the failure to the parent;
/// they must never fall back to a silent OFF state.
/// </summary>
public sealed class HostsFileException : Exception
{
    public HostsFileException(
        string operation,
        HostsFileFailureReason reason,
        string message,
        Exception? innerException = null)
        : base(message, innerException)
    {
        Operation = operation;
        Reason = reason;
    }

    public string Operation { get; }

    public HostsFileFailureReason Reason { get; }
}
