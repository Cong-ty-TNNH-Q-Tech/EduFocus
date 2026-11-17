namespace EduFocus.Ports.DTOs;

/// <summary>Outbox summary for the parent-facing connection card (#89).</summary>
public sealed record CloudOutboxStatus(
    int PendingCount,
    int FailedCount,
    string? LastError,
    DateTime? LastSentAtUtc);
