using EduFocus.Ports.Outbound.Services;

namespace EduFocus.UseCases.Tests.TestSupport;

internal sealed class FakeBrowserUrlProvider : IBrowserUrlProviderPort
{
    public string? Url { get; set; }

    public Exception? ExceptionToThrow { get; set; }

    public string? TryGetActiveUrl(ForegroundActivitySnapshot foreground)
    {
        if (ExceptionToThrow is not null)
            throw ExceptionToThrow;

        return Url;
    }
}
