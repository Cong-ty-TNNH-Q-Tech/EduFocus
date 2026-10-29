namespace EduFocus.Ports.Protection;

/// <summary>
/// Raised when the protection engine cannot apply or remove system-level
/// blocking. The caller keeps the last consistent Protection state and calls
/// <see cref="Outbound.Services.IProtectionEnginePort.RemoveAsync"/> or
/// <see cref="Outbound.Services.IProtectionEnginePort.ApplyAsync"/> to roll back.
/// </summary>
public sealed class ProtectionEngineException : Exception
{
    public ProtectionEngineException(string operation, string message, Exception? innerException = null)
        : base(message, innerException)
    {
        Operation = operation;
    }

    public string Operation { get; }
}
