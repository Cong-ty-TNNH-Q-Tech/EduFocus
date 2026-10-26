namespace EduFocus.Ports.Outbound.Services;

/// <summary>Provides privacy-minimized metadata about the current foreground application.</summary>
public interface IForegroundActivityPort
{
    ForegroundActivitySnapshot Capture();
}

public sealed record ForegroundActivitySnapshot(
    string? ProcessName,
    string? AppName,
    string? WindowTitle,
    string? Url,
    EduFocus.Domain.Enums.ActivityUrlSource UrlSource,
    long IdleSeconds);
