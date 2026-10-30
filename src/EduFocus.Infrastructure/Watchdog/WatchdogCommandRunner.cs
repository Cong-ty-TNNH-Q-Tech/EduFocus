using System.Diagnostics;

namespace EduFocus.Infrastructure.Watchdog;

public sealed record WatchdogCommandResult(int ExitCode, string StandardOutput, string StandardError);

/// <summary>Seam over the Task Scheduler CLI so auto-start can be tested without touching Windows.</summary>
public interface IWatchdogCommandRunner
{
    Task<WatchdogCommandResult> RunAsync(
        string fileName,
        IReadOnlyList<string> arguments,
        CancellationToken cancellationToken = default);
}

public sealed class ProcessWatchdogCommandRunner : IWatchdogCommandRunner
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(30);

    public async Task<WatchdogCommandResult> RunAsync(
        string fileName,
        IReadOnlyList<string> arguments,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(fileName);
        ArgumentNullException.ThrowIfNull(arguments);

        var startInfo = new ProcessStartInfo(fileName)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };

        foreach (var argument in arguments)
            startInfo.ArgumentList.Add(argument);

        using var process = Process.Start(startInfo)
            ?? throw new InvalidOperationException($"'{fileName}' could not be started.");

        var stdout = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var stderr = process.StandardError.ReadToEndAsync(cancellationToken);

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(Timeout);

        try
        {
            await process.WaitForExitAsync(timeout.Token);
        }
        catch (OperationCanceledException)
        {
            try
            {
                process.Kill(entireProcessTree: true);
            }
            catch
            {
                // The process may have exited between the timeout and the kill.
            }

            throw;
        }

        return new WatchdogCommandResult(process.ExitCode, await stdout, await stderr);
    }
}
