using EduFocus.Ports.DTOs;

namespace EduFocus.Ports.Inbound;

/// <summary>
/// Irreversible delete-all (UC11, issue #38). Requires an authenticated parent
/// session, a fresh PIN verification and the exact confirmation text. Active
/// study sessions, credentials and audit entries are preserved.
/// </summary>
public interface IDataDeletionUseCase
{
    Task<DataDeletionResult> DeleteAllAsync(
        DataDeletionRequest request,
        CancellationToken cancellationToken = default);
}
