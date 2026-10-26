using System.ComponentModel;
using System.Diagnostics;
using EduFocus.Domain.Entities;
using EduFocus.Domain.Enums;
using EduFocus.Ports.Outbound.Services;
using Microsoft.Extensions.DependencyInjection;

namespace EduFocus.Infrastructure.Services;

public interface IWatchedProcess : IDisposable
{
    int Id { get; }
    string Name { get; }
    string? ExecutablePath { get; }
    bool HasWindow { get; }
    bool IsExited { get; }
    bool TryCloseMainWindow();
    Task<bool> WaitForExitAsync(TimeSpan timeout, CancellationToken cancellationToken);
    void KillTree();
}

public interface IProcessSnapshotProvider
{
    IReadOnlyList<IWatchedProcess> GetProcesses();
}

public sealed class SystemProcessSnapshotProvider : IProcessSnapshotProvider
{
    public IReadOnlyList<IWatchedProcess> GetProcesses()
    {
        var snapshots = new List<IWatchedProcess>();
        foreach (var process in Process.GetProcesses())
        {
            try
            {
                if (!process.HasExited) snapshots.Add(new SystemWatchedProcess(process));
                else process.Dispose();
            }
            catch { process.Dispose(); }
        }
        return snapshots;
    }

    private sealed class SystemWatchedProcess(Process process) : IWatchedProcess
    {
        public int Id => process.Id;
        public string Name
        {
            get
            {
                try { return process.ProcessName; }
                catch (Exception ex) when (ex is Win32Exception or InvalidOperationException or NotSupportedException) { return string.Empty; }
            }
        }
        public string? ExecutablePath
        {
            get
            {
                try { return process.MainModule?.FileName; }
                catch (Exception ex) when (ex is Win32Exception or UnauthorizedAccessException or InvalidOperationException or NotSupportedException) { return null; }
            }
        }
        public bool HasWindow
        {
            get
            {
                try { return process.MainWindowHandle != IntPtr.Zero; }
                catch (Exception ex) when (ex is Win32Exception or UnauthorizedAccessException or InvalidOperationException or NotSupportedException) { return false; }
            }
        }
        public bool IsExited
        {
            get { try { return process.HasExited; } catch (InvalidOperationException) { return true; } }
        }
        public bool TryCloseMainWindow()
        {
            try { return process.CloseMainWindow(); }
            catch (Exception ex) when (ex is Win32Exception or InvalidOperationException or NotSupportedException) { return false; }
        }
        public async Task<bool> WaitForExitAsync(TimeSpan timeout, CancellationToken cancellationToken)
        {
            if (IsExited) return true;
            using var timeoutToken = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeoutToken.CancelAfter(timeout);
            try { await process.WaitForExitAsync(timeoutToken.Token); return true; }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested) { return IsExited; }
        }
        public void KillTree() => process.Kill(entireProcessTree: true);
        public void Dispose() => process.Dispose();
    }
}

/// <summary>Polls interactive process snapshots and best-effort closes exact app rules only.</summary>
public sealed class ProcessWatcher : IProcessWatcherPort, IAsyncDisposable
{
    private static readonly HashSet<string> ProtectedNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "system", "registry", "smss", "csrss", "wininit", "winlogon", "services", "lsass", "svchost",
        "fontdrvhost", "dwm", "explorer", "taskhostw", "runtimebroker"
    };
    private readonly IProcessSnapshotProvider _processes;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IAppLogger _logger;
    private readonly TimeSpan _pollInterval;
    private readonly TimeSpan _closeGrace;
    private readonly SemaphoreSlim _lifecycleGate = new(1, 1);
    private readonly SemaphoreSlim _scanGate = new(1, 1);
    private readonly Dictionary<string, DateTimeOffset> _lastWarningByApp = new(StringComparer.OrdinalIgnoreCase);
    private DateTimeOffset _lastScanErrorUtc = DateTimeOffset.MinValue;
    private IReadOnlyCollection<BlockRule> _rules = Array.Empty<BlockRule>();
    private CancellationTokenSource? _stop;
    private Task? _loop;

    public ProcessWatcher(IProcessSnapshotProvider processes, IServiceScopeFactory scopeFactory, IAppLogger logger,
        TimeSpan? pollInterval = null, TimeSpan? closeGrace = null)
    {
        _processes = processes ?? throw new ArgumentNullException(nameof(processes));
        _scopeFactory = scopeFactory ?? throw new ArgumentNullException(nameof(scopeFactory));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _pollInterval = pollInterval ?? TimeSpan.FromSeconds(1);
        _closeGrace = closeGrace ?? TimeSpan.FromSeconds(2);
        if (_pollInterval <= TimeSpan.Zero || _closeGrace < TimeSpan.Zero || _closeGrace > TimeSpan.FromSeconds(2))
            throw new ArgumentOutOfRangeException(nameof(closeGrace), "Process watcher uses positive polling and a maximum two-second close grace.");
    }

    public bool IsRunning => _loop is { IsCompleted: false };

    public async Task StartAsync(IReadOnlyCollection<BlockRule> rules, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(rules);
        await _lifecycleGate.WaitAsync(cancellationToken);
        try
        {
            _rules = rules.ToArray();
            if (IsRunning) return;
            _stop?.Dispose();
            _stop = new CancellationTokenSource();
            _loop = RunAsync(_stop.Token);
        }
        finally { _lifecycleGate.Release(); }
    }

    public Task UpdateRulesAsync(IReadOnlyCollection<BlockRule> rules, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(rules);
        cancellationToken.ThrowIfCancellationRequested();
        _rules = rules.ToArray();
        return Task.CompletedTask;
    }

    public async Task StopAsync(CancellationToken cancellationToken = default)
    {
        await _lifecycleGate.WaitAsync(cancellationToken);
        try
        {
            var stop = _stop;
            var loop = _loop;
            _stop = null;
            _loop = null;
            _rules = Array.Empty<BlockRule>();
            stop?.Cancel();
            if (loop is not null)
            {
                try { await loop.WaitAsync(cancellationToken); }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
                catch (Exception ex) { _logger.Error("EduFocus process watcher stopped after an error.", ex); }
            }
            stop?.Dispose();
        }
        finally { _lifecycleGate.Release(); }
    }

    public async ValueTask DisposeAsync() => await StopAsync();

    /// <summary>One scan entry point shared by the timer and deterministic tests.</summary>
    public async Task ScanOnceAsync(DateTimeOffset now, CancellationToken cancellationToken = default)
    {
        if (now.Offset != TimeSpan.Zero) throw new ArgumentException("Timestamp must be UTC.", nameof(now));
        await _scanGate.WaitAsync(cancellationToken);
        try
        {
            var rules = _rules.Where(rule => rule.RuleType == BlockRuleType.App && rule.MatchType == BlockMatchType.Exact && rule.IsApplicable(now)).ToArray();
            if (rules.Length == 0) return;
            foreach (var process in _processes.GetProcesses())
            {
                using (process)
                {
                    try
                    {
                        if (process.Id == Environment.ProcessId || process.Id <= 0 || !process.HasWindow || IsProtected(process.Name)) continue;
                        var candidates = GetCandidates(process);
                        var matched = rules.Where(rule => candidates.Contains(NormalizeCandidate(rule.NormalizedValue), StringComparer.OrdinalIgnoreCase)).ToArray();
                        if (matched.Length == 0 || matched.Any(rule => rule.Effect == BlockRuleEffect.Allow)) continue;
                        if (!matched.Any(rule => rule.Effect == BlockRuleEffect.Block)) continue;
                        await EnforceAsync(process, now, cancellationToken);
                    }
                    catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
                    catch (Exception ex) when (ex is Win32Exception or UnauthorizedAccessException or InvalidOperationException or NotSupportedException)
                    {
                        LogScanFailure(now, ex);
                    }
                }
            }
        }
        finally { _scanGate.Release(); }
    }

    private async Task RunAsync(CancellationToken cancellationToken)
    {
        using var timer = new PeriodicTimer(_pollInterval);
        try
        {
            do
            {
                try { await ScanOnceAsync(DateTimeOffset.UtcNow, cancellationToken); }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { break; }
                catch (Exception ex) { LogScanFailure(DateTimeOffset.UtcNow, ex); }
            } while (await timer.WaitForNextTickAsync(cancellationToken));
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { }
    }

    private async Task EnforceAsync(IWatchedProcess process, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var appKey = NormalizeCandidate(process.Name);
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var recorder = scope.ServiceProvider.GetRequiredService<IBlockedAttemptRecorder>();
            await recorder.RecordBlockedAppAsync(process.Name, now, cancellationToken);
        }
        catch (Exception ex) { _logger.Error("EduFocus could not record an observed blocked app attempt.", ex); }

        try
        {
            process.TryCloseMainWindow();
            if (await process.WaitForExitAsync(_closeGrace, cancellationToken)) return;
            if (!process.IsExited) process.KillTree();
        }
        catch (Exception ex) when (ex is Win32Exception or UnauthorizedAccessException or InvalidOperationException or NotSupportedException)
        {
            if (!_lastWarningByApp.TryGetValue(appKey, out var lastWarning) || now - lastWarning >= TimeSpan.FromSeconds(30))
            {
                _lastWarningByApp[appKey] = now;
                _logger.Warning("EduFocus detected a blocked app but Windows did not allow it to be closed; process enforcement is best-effort.");
            }
        }
    }

    private void LogScanFailure(DateTimeOffset now, Exception exception)
    {
        if (now - _lastScanErrorUtc < TimeSpan.FromSeconds(30)) return;
        _lastScanErrorUtc = now;
        _logger.Error("EduFocus process watcher scan failed; Protection state is unchanged.", exception);
    }

    private static HashSet<string> GetCandidates(IWatchedProcess process)
    {
        var candidates = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var name = NormalizeCandidate(process.Name);
        if (name.Length > 0)
        {
            candidates.Add(name);
            candidates.Add(name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ? name[..^4] : name + ".exe");
        }
        if (!string.IsNullOrWhiteSpace(process.ExecutablePath)) candidates.Add(NormalizeCandidate(process.ExecutablePath));
        return candidates;
    }

    private static string NormalizeCandidate(string value) => value.Trim().Replace('/', '\\').ToLowerInvariant();

    private static bool IsProtected(string? processName) => string.IsNullOrWhiteSpace(processName) ||
        ProtectedNames.Contains(processName) ||
        processName.StartsWith("EduFocus", StringComparison.OrdinalIgnoreCase);
}
