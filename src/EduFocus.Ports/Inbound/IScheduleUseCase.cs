using EduFocus.Ports.DTOs;

namespace EduFocus.Ports.Inbound;

/// <summary>
/// Weekly protection schedule (UC06). Mutations require an authenticated Parent
/// Mode session; <see cref="EvaluateAsync"/> runs every 30 seconds and at startup
/// as the system actor and owns the automatic ON/OFF transitions.
/// </summary>
public interface IScheduleUseCase
{
    Task<ScheduleOverview> GetOverviewAsync(CancellationToken cancellationToken = default);

    Task<ScheduleOperationResult> AddWindowAsync(
        ScheduleWindowDraft draft,
        CancellationToken cancellationToken = default);

    Task<ScheduleOperationResult> UpdateWindowAsync(
        Guid id,
        ScheduleWindowDraft draft,
        CancellationToken cancellationToken = default);

    Task<ScheduleOperationResult> SetWindowEnabledAsync(
        Guid id,
        bool isEnabled,
        CancellationToken cancellationToken = default);

    Task<ScheduleOperationResult> DeleteWindowAsync(
        Guid id,
        CancellationToken cancellationToken = default);

    Task<ScheduleOperationResult> SetScheduleEnabledAsync(
        bool isEnabled,
        CancellationToken cancellationToken = default);

    Task<ScheduleEvaluationResult> EvaluateAsync(CancellationToken cancellationToken = default);
}
