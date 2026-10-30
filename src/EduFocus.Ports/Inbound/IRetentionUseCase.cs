using EduFocus.Ports.DTOs;

namespace EduFocus.Ports.Inbound;

/// <summary>
/// Daily data retention (UC11, issue #29). Detailed rows older than the
/// configured windows are deleted in batches, Active sessions are preserved,
/// and a retention audit entry is written with the removed counts.
/// </summary>
public interface IRetentionUseCase
{
    /// <summary>
    /// Runs one retention pass. Safe to call repeatedly: already-expired rows
    /// are gone after the first pass, and a short batch ends each table loop.
    /// </summary>
    Task<RetentionRunResult> RunOnceAsync(CancellationToken cancellationToken = default);
}
