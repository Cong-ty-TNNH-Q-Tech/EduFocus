using System.Threading;
using System.Threading.Tasks;

namespace EduFocus.Ports.Outbound.Repositories;

public interface IUnitOfWork
{
    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
}
