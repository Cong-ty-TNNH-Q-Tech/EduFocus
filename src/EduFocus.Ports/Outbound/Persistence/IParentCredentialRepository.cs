using EduFocus.Domain.Entities;

namespace EduFocus.Ports.Outbound.Persistence;

/// <summary>Singleton parent credential (PIN/recovery hashes and lockout state).</summary>
public interface IParentCredentialRepository
{
    Task<ParentCredential?> GetAsync(CancellationToken cancellationToken = default);

    Task AddAsync(ParentCredential credential, CancellationToken cancellationToken = default);

    Task UpdateAsync(ParentCredential credential, CancellationToken cancellationToken = default);
}
