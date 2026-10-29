using EduFocus.Domain.Entities;

namespace EduFocus.Ports.Outbound.Persistence;

/// <summary>
/// Study session persistence. The database guarantees at most one Active
/// session via the unique partial index UX_StudySessions_Active
/// (database_schema §8).
/// </summary>
public interface IStudySessionRepository
{
    Task<StudySession?> GetActiveAsync(CancellationToken cancellationToken = default);

    Task AddAsync(StudySession session, CancellationToken cancellationToken = default);

    Task UpdateAsync(StudySession session, CancellationToken cancellationToken = default);
}
