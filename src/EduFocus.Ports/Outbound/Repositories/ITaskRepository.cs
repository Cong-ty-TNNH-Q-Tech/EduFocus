using System.Collections.Generic;
using System.Threading.Tasks;
using EduFocus.Domain.Entities;
using EduFocus.Domain.ValueObjects;

namespace EduFocus.Ports.Outbound.Repositories;

public interface ITaskRepository
{
    Task<IReadOnlyCollection<FocusTask>> GetAllAsync();
    Task<IReadOnlyCollection<FocusTask>> GetVisibleTasksForDateAsync(System.DateTime date);
    Task<FocusTask?> GetByIdAsync(TaskId id);
    Task AddAsync(FocusTask task);
    Task UpdateAsync(FocusTask task);
    Task DeleteAsync(TaskId id);
}
