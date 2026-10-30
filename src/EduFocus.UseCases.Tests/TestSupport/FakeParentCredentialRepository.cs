using EduFocus.Domain.Entities;
using EduFocus.Ports.Outbound.Persistence;

namespace EduFocus.UseCases.Tests.TestSupport;

internal sealed class FakeParentCredentialRepository : IParentCredentialRepository
{
    public ParentCredential? Stored { get; private set; }

    public int AddCount { get; private set; }

    public int UpdateCount { get; private set; }

    public Task<ParentCredential?> GetAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult(Stored);

    public Task AddAsync(ParentCredential credential, CancellationToken cancellationToken = default)
    {
        AddCount++;
        Stored = credential;
        return Task.CompletedTask;
    }

    public Task UpdateAsync(ParentCredential credential, CancellationToken cancellationToken = default)
    {
        UpdateCount++;
        Stored = credential;
        return Task.CompletedTask;
    }
}
