using EduFocus.Ports.Outbound.Services;

namespace EduFocus.Infrastructure.Tests;

internal sealed class TestLogger : IAppLogger
{
    public List<string> Infos { get; } = [];

    public List<string> Warnings { get; } = [];

    public List<string> Errors { get; } = [];

    public void Info(string category, string message) => Infos.Add($"{category}: {message}");

    public void Warning(string category, string message) => Warnings.Add($"{category}: {message}");

    public void Error(string category, string message, Exception? exception = null) =>
        Errors.Add($"{category}: {message}");
}
