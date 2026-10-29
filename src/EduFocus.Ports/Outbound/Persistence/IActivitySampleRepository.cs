using EduFocus.Domain.Entities;
using EduFocus.Domain.ValueObjects;
using EduFocus.Ports.DTOs;

namespace EduFocus.Ports.Outbound.Persistence;

/// <summary>Foreground activity samples (database schema §9).</summary>
public interface IActivitySampleRepository
{
    Task AddAsync(ActivitySample sample, CancellationToken cancellationToken = default);

    /// <summary>
    /// Reads at most <paramref name="maxCount"/> foreground samples for the
    /// supplied sessions, newest first (issue #36). Samples whose whole
    /// duration is idle are skipped because idle time is attributed to no
    /// category. The projection exposes only the app display name and the
    /// sanitized domain, never window titles or URLs.
    /// </summary>
    Task<IReadOnlyList<ReportActivitySample>> ListForReportAsync(
        IReadOnlyCollection<StudySessionId> sessionIds,
        int maxCount,
        CancellationToken cancellationToken = default);
}
