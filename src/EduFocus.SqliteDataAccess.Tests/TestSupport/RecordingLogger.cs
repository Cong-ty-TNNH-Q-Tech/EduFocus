using EduFocus.Ports.Outbound.Services;

namespace EduFocus.SqliteDataAccess.Tests.TestSupport;

internal sealed class RecordingLogger : IAppLogger
{
    public List<string> Infos { get; } = [];

    public List<string> Warnings { get; } = [];

    public List<string> Errors { get; } = [];

    public List<Exception?> Exceptions { get; } = [];

    public void Info(string message) => Infos.Add(message);

    public void Warning(string message) => Warnings.Add(message);

    public void Error(string message, Exception? ex = null)
    {
        Errors.Add(message);
        Exceptions.Add(ex);
    }
}
