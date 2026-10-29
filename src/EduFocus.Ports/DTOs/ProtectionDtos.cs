namespace EduFocus.Ports.DTOs;

/// <summary>Current Protection state and the active study session, if any.</summary>
public sealed record ProtectionStatus(
    bool IsEnabled,
    DateTime? ActiveSessionStartedAtUtc,
    bool MonitoringEnabled);

/// <summary>
/// Outcome of a Protection operation. <paramref name="Warnings"/> contains
/// non-fatal engine problems (for example a blocked page server that could not
/// bind); the UI must display them instead of claiming full enforcement.
/// </summary>
public sealed record ProtectionOperationResult(
    ProtectionStatus Status,
    bool Changed,
    IReadOnlyList<string> Warnings);
