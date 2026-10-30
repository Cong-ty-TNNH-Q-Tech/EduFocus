namespace EduFocus.Ports.Outbound.Services;

/// <summary>
/// Best-effort browser address-bar reader (architecture §7.2). The returned
/// value is a raw URL; the use case sanitizes it before persistence. Returning
/// null means the source is unavailable, in which case UrlSource is None.
/// </summary>
public interface IBrowserUrlProviderPort
{
    string? TryGetActiveUrl(ForegroundActivitySnapshot foreground);
}
