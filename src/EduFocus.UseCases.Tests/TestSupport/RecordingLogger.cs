using EduFocus.Ports.Outbound.Services;

namespace EduFocus.UseCases.Tests.TestSupport;

internal sealed class RecordingLogger : IAppLogger
{
    public List<string> Errors { get; } = [];

    public List<string> Warnings { get; } = [];

    public void Info(string category, string message)
    {
    }

    public void Warning(string category, string message) =>
        Warnings.Add($"{category}: {message}");

    public void Error(string category, string message, Exception? exception = null) =>
        Errors.Add($"{category}: {message}");
}
