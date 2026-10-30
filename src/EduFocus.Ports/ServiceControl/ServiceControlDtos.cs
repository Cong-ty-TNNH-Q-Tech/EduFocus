using EduFocus.Ports.DTOs;

namespace EduFocus.Ports.ServiceControl;

/// <summary>
/// One command from the elevated UI. <see cref="Protocol"/> is checked before
/// the command is dispatched so an incompatible client cannot half-apply an
/// operation.
/// </summary>
public sealed record ServiceControlRequest(int Protocol, string Command);

/// <summary>
/// Non-secret identity of the running protection service, used for the UI
/// handshake and the <c>--health</c> diagnostic command.
/// </summary>
public sealed record ProtectionServiceInfo(
    string Version,
    int ProcessId,
    DateTimeOffset StartedAtUtc,
    bool ProtectionEnabled,
    DateTimeOffset? ActiveSessionStartedAtUtc);

/// <summary>
/// Command response. A failed command always carries a stable
/// <see cref="ErrorCode"/> and a user-safe Vietnamese message; it never carries
/// stack traces, process paths or data rows.
/// </summary>
public sealed record ServiceControlResponse(
    int Protocol,
    bool Ok,
    string? ErrorCode,
    string? Error,
    ProtectionServiceInfo? Service,
    ProtectionStatus? Status,
    ProtectionOperationResult? Operation)
{
    public static ServiceControlResponse Success(
        ProtectionServiceInfo? service = null,
        ProtectionStatus? status = null,
        ProtectionOperationResult? operation = null) =>
        new(ServiceControlProtocol.Version, true, null, null, service, status, operation);

    public static ServiceControlResponse Failure(string code, string message) =>
        new(ServiceControlProtocol.Version, false, code, message, null, null, null);
}
