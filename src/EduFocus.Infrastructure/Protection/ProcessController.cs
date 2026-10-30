using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.Versioning;

namespace EduFocus.Infrastructure.Protection;

public sealed record ProcessSnapshot(int ProcessId, string ProcessName, bool HasMainWindow);

/// <summary>
/// Thin seam over process enumeration and termination so the watcher can be
/// tested without opening real windows or killing real processes.
/// </summary>
public interface IProcessController
{
    IReadOnlyList<ProcessSnapshot> GetProcesses();

    Task<bool> CloseMainWindowAsync(
        int processId,
        TimeSpan timeout,
        CancellationToken cancellationToken = default);

    void KillProcessTree(int processId);
}

[SupportedOSPlatform("windows")]
public sealed class WindowsProcessController : IProcessController
{
    public IReadOnlyList<ProcessSnapshot> GetProcesses()
    {
        var snapshots = new List<ProcessSnapshot>();

        foreach (var process in Process.GetProcesses())
        {
            using (process)
            {
                try
                {
                    if (process.Id <= 4)
                        continue;

                    var name = process.ProcessName;
                    if (string.IsNullOrWhiteSpace(name))
                        continue;

                    snapshots.Add(new ProcessSnapshot(
                        process.Id,
                        name,
                        process.MainWindowHandle != IntPtr.Zero));
                }
                catch (InvalidOperationException) { }
                catch (Win32Exception) { }
            }
        }

        return snapshots;
    }

    public async Task<bool> CloseMainWindowAsync(
        int processId,
        TimeSpan timeout,
        CancellationToken cancellationToken = default)
    {
        Process process;
        try
        {
            process = Process.GetProcessById(processId);
        }
        catch (ArgumentException)
        {
            return true;
        }
        catch (InvalidOperationException)
        {
            return true;
        }

        using (process)
        {
            try
            {
                if (process.MainWindowHandle == IntPtr.Zero)
                    return false;

                if (!process.CloseMainWindow())
                    return false;

                using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                timeoutCts.CancelAfter(timeout);

                try
                {
                    await process.WaitForExitAsync(timeoutCts.Token).ConfigureAwait(false);
                    return true;
                }
                catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
                {
                    return false;
                }
                catch (InvalidOperationException)
                {
                    return true;
                }
            }
            catch (InvalidOperationException)
            {
                return true;
            }
            catch (Win32Exception)
            {
                return false;
            }
        }
    }

    public void KillProcessTree(int processId)
    {
        using var process = Process.GetProcessById(processId);
        process.Kill(entireProcessTree: true);
    }
}
