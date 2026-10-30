namespace EduFocus.Ports.Outbound.Services;

/// <summary>
/// Technical application log. Implementations add an UTC timestamp and the
/// current correlation id, and must redact secrets and URL detail before
/// writing (system architecture §12).
/// </summary>
public interface IAppLogger
{
    void Info(string category, string message);

    void Warning(string category, string message);

    void Error(string category, string message, Exception? exception = null);
}
