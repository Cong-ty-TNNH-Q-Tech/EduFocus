using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.Versioning;

namespace EduFocus.Infrastructure.Protection;

public sealed record ProcessSnapshot(
    int ProcessId,
    string ProcessName,
    bool HasMainWindow,
    DateTime? StartTimeUtc = null);

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

    Task<bool> CloseMainWindowAsync(
        int processId,
        TimeSpan timeout,
        DateTime? expectedStartTimeUtc,
        string? expectedProcessName,
        CancellationToken cancellationToken = default) =>
        CloseMainWindowAsync(processId, timeout, cancellationToken);

    void KillProcessTree(int processId);

    void KillProcessTree(
        int processId,
        DateTime? expectedStartTimeUtc,
        string? expectedProcessName) =>
        KillProcessTree(processId);
}

[SupportedOSPlatform("windows")]
public sealed class WindowsProcessController : IProcessController
{
    private static readonly bool IsServiceSession = DetectServiceSession();
    private static readonly TimeSpan StartTimeTolerance = TimeSpan.FromSeconds(2);

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

                    DateTime? startTimeUtc = null;
                    try
                    {
                        startTimeUtc = process.StartTime.ToUniversalTime();
                    }
                    catch (Exception ex) when (ex is InvalidOperationException or Win32Exception or NotSupportedException)
                    {
                    }

                    var hasMainWindow = process.MainWindowHandle != IntPtr.Zero ||
                        (IsServiceSession && IsInteractiveUserSession(process));

                    snapshots.Add(new ProcessSnapshot(
                        process.Id,
                        name,
                        hasMainWindow,
                        startTimeUtc));
                }
                catch (InvalidOperationException) { }
                catch (Win32Exception) { }
            }
        }

        return snapshots;
    }

    private static bool DetectServiceSession()
    {
        try
        {
            using var current = Process.GetCurrentProcess();
            return current.SessionId == 0 || !Environment.UserInteractive;
        }
        catch
        {
            return !Environment.UserInteractive;
        }
    }

    private static bool IsInteractiveUserSession(Process process)
    {
        try
        {
            return process.SessionId > 0;
        }
        catch
        {
            return false;
        }
    }

    public Task<bool> CloseMainWindowAsync(
        int processId,
        TimeSpan timeout,
        CancellationToken cancellationToken = default) =>
        CloseMainWindowAsync(processId, timeout, expectedStartTimeUtc: null, expectedProcessName: null, cancellationToken);

    public async Task<bool> CloseMainWindowAsync(
        int processId,
        TimeSpan timeout,
        DateTime? expectedStartTimeUtc,
        string? expectedProcessName,
        CancellationToken cancellationToken = default)
    {
        if (IsServiceSession)
            return false;

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
                if (!VerifyProcessIdentity(process, expectedStartTimeUtc, expectedProcessName))
                    return true;

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

    public void KillProcessTree(int processId) =>
        KillProcessTree(processId, expectedStartTimeUtc: null, expectedProcessName: null);

    public void KillProcessTree(
        int processId,
        DateTime? expectedStartTimeUtc,
        string? expectedProcessName)
    {
        Process process;
        try
        {
            process = Process.GetProcessById(processId);
        }
        catch (ArgumentException)
        {
            return;
        }
        catch (InvalidOperationException)
        {
            return;
        }

        using (process)
        {
            if (!VerifyProcessIdentity(process, expectedStartTimeUtc, expectedProcessName))
                return;

            process.Kill(entireProcessTree: true);
        }
    }

    private static bool VerifyProcessIdentity(
        Process process,
        DateTime? expectedStartTimeUtc,
        string? expectedProcessName)
    {
        if (process.HasExited)
            return false;

        if (!string.IsNullOrWhiteSpace(expectedProcessName)
            && !string.Equals(process.ProcessName, expectedProcessName, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (expectedStartTimeUtc.HasValue)
        {
            try
            {
                var actual = process.StartTime.ToUniversalTime();
                if ((actual - expectedStartTimeUtc.Value.ToUniversalTime()).Duration() > StartTimeTolerance)
                    return false;
            }
            catch (Exception ex) when (ex is InvalidOperationException or Win32Exception or NotSupportedException)
            {
                return false;
            }
        }

        return true;
    }
}
