namespace EduFocus.Ports.Outbound.Services;

/// <summary>
/// Ambient correlation id shared by a logical operation (app start, parent
/// action, session, block page request). Implementations flow with async work.
/// </summary>
public interface ICorrelationContext
{
    Guid? Current { get; }

    IDisposable BeginScope(Guid correlationId);
}
