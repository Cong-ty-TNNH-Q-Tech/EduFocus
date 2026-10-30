using EduFocus.Ports.Outbound.Services;

namespace EduFocus.UseCases.Tests.TestSupport;

internal sealed class TestCorrelationContext : ICorrelationContext
{
    public Guid? Current { get; set; }

    public IDisposable BeginScope(Guid correlationId)
    {
        Current = correlationId;
        return new Scope();
    }

    private sealed class Scope : IDisposable
    {
        public void Dispose()
        {
        }
    }
}
