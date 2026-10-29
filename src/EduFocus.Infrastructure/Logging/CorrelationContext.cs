using EduFocus.Ports.Outbound.Services;

namespace EduFocus.Infrastructure.Logging;

/// <summary>
/// Ambient correlation id stored in <see cref="AsyncLocal{T}"/> so it flows
/// through async continuations and background tasks of one operation.
/// </summary>
public sealed class CorrelationContext : ICorrelationContext
{
    private static readonly AsyncLocal<Guid?> Ambient = new();

    public Guid? Current => Ambient.Value;

    public IDisposable BeginScope(Guid correlationId)
    {
        if (correlationId == Guid.Empty)
            throw new ArgumentException("Correlation id must not be empty.", nameof(correlationId));

        var previous = Ambient.Value;
        Ambient.Value = correlationId;
        return new Scope(previous);
    }

    private sealed class Scope : IDisposable
    {
        private readonly Guid? _previous;
        private bool _disposed;

        public Scope(Guid? previous) => _previous = previous;

        public void Dispose()
        {
            if (_disposed)
                return;

            _disposed = true;
            Ambient.Value = _previous;
        }
    }
}
