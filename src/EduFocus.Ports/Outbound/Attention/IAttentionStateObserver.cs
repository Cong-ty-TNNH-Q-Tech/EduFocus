using EduFocus.Domain.Attention;

namespace EduFocus.Ports.Outbound.Attention;

/// <summary>
/// Observes the stable attention state produced by the monitor without adding
/// any cloud dependency to the attention use case. Observers receive the state
/// and the observation time only; samples, frames and images stay behind.
/// A null state means monitoring paused, was interrupted or stopped, so any
/// streak tracking must reset. Observer failures never affect monitoring.
/// </summary>
public interface IAttentionStateObserver
{
    ValueTask OnStateObservedAsync(
        AttentionState? state,
        DateTimeOffset observedAtUtc,
        CancellationToken cancellationToken = default);
}
