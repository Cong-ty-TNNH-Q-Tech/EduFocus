using System.Diagnostics;
using EduFocus.Ports.Outbound.Services;

namespace EduFocus.Infrastructure.Protection;

/// <summary>Flushes the Windows DNS cache with <c>ipconfig /flushdns</c>.</summary>
public sealed class IpconfigDnsFlusher : IDnsFlusher
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(5);

    public async Task FlushAsync(CancellationToken cancellationToken = default)
    {
        var startInfo = new ProcessStartInfo("ipconfig", "/flushdns")
        {
            CreateNoWindow = true,
            UseShellExecute = false
        };

        using var process = Process.Start(startInfo)
            ?? throw new InvalidOperationException("Could not start ipconfig /flushdns.");

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(Timeout);

        try
        {
            await process.WaitForExitAsync(timeout.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            TryKill(process);
            throw new TimeoutException("ipconfig /flushdns did not finish in time.");
        }

        if (process.ExitCode != 0)
            throw new InvalidOperationException($"ipconfig /flushdns exited with code {process.ExitCode}.");
    }

    private static void TryKill(Process process)
    {
        try
        {
            if (!process.HasExited)
                process.Kill(entireProcessTree: true);
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception)
        {
        }
    }
}
