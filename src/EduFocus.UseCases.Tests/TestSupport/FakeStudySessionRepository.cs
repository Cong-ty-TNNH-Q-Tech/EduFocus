using EduFocus.Domain.Entities;
using EduFocus.Ports.Outbound.Persistence;

namespace EduFocus.UseCases.Tests.TestSupport;

internal sealed class FakeStudySessionRepository : IStudySessionRepository
{
    public List<StudySession> Sessions { get; } = [];

    public bool ThrowOnAdd { get; set; }

    public StudySession? Active => Sessions.SingleOrDefault(session => session.IsActive);

    public Task<StudySession?> GetActiveAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult(Active);

    public Task AddAsync(StudySession session, CancellationToken cancellationToken = default)
    {
        if (ThrowOnAdd)
            throw new InvalidOperationException("The session store is unavailable.");

        if (session.IsActive && Active is not null)
            throw new InvalidOperationException("An active session already exists.");

        Sessions.Add(session);
        return Task.CompletedTask;
    }

    public Task UpdateAsync(StudySession session, CancellationToken cancellationToken = default)
    {
        if (!Sessions.Contains(session))
            Sessions.Add(session);

        return Task.CompletedTask;
    }
}
