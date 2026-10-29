namespace EduFocus.Ports.DTOs;

/// <summary>
/// Outcome of the PIN-gated exit (UC12). <see cref="CanExit"/> is true only when
/// protection was removed and the clean exit was recorded. <see cref="Warnings"/>
/// lists non-fatal problems such as a failed clean-shutdown marker write; the
/// caller may still exit because startup reconciliation self-heals.
/// </summary>
public sealed record AppExitResult(
    bool CanExit,
    ParentAuthStatus AuthStatus,
    int RemainingAttempts,
    DateTime? LockoutUntilUtc,
    IReadOnlyList<string> Warnings,
    string? Error);
