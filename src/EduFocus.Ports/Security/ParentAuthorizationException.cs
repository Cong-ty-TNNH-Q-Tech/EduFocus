namespace EduFocus.Ports.Security;

/// <summary>A sensitive operation was requested without an active Parent Mode session.</summary>
public sealed class ParentAuthorizationException : Exception
{
    public ParentAuthorizationException(string operation)
        : base($"Parent Mode is required for '{operation}'.")
    {
        Operation = operation;
    }

    public string Operation { get; }
}
