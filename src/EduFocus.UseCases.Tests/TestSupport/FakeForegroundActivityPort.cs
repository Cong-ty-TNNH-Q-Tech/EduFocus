using EduFocus.Ports.Outbound.Services;

namespace EduFocus.UseCases.Tests.TestSupport;

internal sealed class FakeForegroundActivityPort : IForegroundActivityPort
{
    public ForegroundActivitySnapshot? Snapshot { get; set; }

    public Exception? ExceptionToThrow { get; set; }

    public int Calls { get; private set; }

    public ForegroundActivitySnapshot? GetForegroundActivity()
    {
        Calls++;

        if (ExceptionToThrow is not null)
            throw ExceptionToThrow;

        return Snapshot;
    }
}
