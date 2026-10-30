using System.Collections.Concurrent;
using System.Collections.Frozen;
using System.Globalization;
using EduFocus.Ports.Outbound.Services;

namespace EduFocus.Infrastructure.Protection;

/// <summary>
/// Polls running processes and closes or kills apps whose normalized name is
/// blocked. System processes, EduFocus itself, windowless processes and
/// allowlisted names are never touched. Attempts are debounced by process name
/// using the monotonic clock so restarts cannot create a kill/log loop.
/// </summary>
public sealed class ProcessWatcher : IProcessWatcher, IAsyncDisposable
{
    private static readonly FrozenSet<string> ProtectedProcessNames =
        new[]
        {
            "system", "registry", "idle", "memory compression",
            "smss", "csrss", "wininit", "winlogon", "services", "lsass", "lsaiso",
            "svchost", "dwm", "explorer", "audiodg", "spoolsv", "taskhostw", "sihost",
            "ctfmon", "fontdrvhost", "wmiprvse", "searchindexer", "searchhost",
            "runtimebroker", "shellexperiencehost", "startmenuexperiencehost",
            "textinputhost", "applicationframehost", "securityhealthservice",
            "securityhealthsystray", "msmpeng", "nissrv", "tiworker", "trustedinstaller",
            "conhost", "dllhost", "wudfhost", "sppsvc", "wlanext", "edufocus"
        }.ToFrozenSet(StringComparer.Ordinal);

    private readonly ProcessWatcherOptions _options;
    private readonly IProcessController _controller;
    private readonly IMonotonicClockPort _clock;
    private readonly IAppLogger _logger;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly ConcurrentDictionary<string, TimeSpan> _lastAttempt =
        new(StringComparer.Ordinal);

    private FrozenSet<string> _blocked = FrozenSet<string>.Empty;
    private FrozenSet<string> _allowed = FrozenSet<string>.Empty;
    private CancellationTokenSource? _cts;
    private Task? _loop;
    private bool _scanFaultReported;

    public ProcessWatcher(
        ProcessWatcherOptions options,
        IProcessController controller,
        IMonotonicClockPort clock,
        IAppLogger logger)
    {
        ArgumentNullException.ThrowIfNull(options);
        _controller = controller ?? throw new ArgumentNullException(nameof(controller));
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));

        if (options.PollInterval < TimeSpan.FromMilliseconds(10) ||
            options.PollInterval > TimeSpan.FromMinutes(1))
        {
            throw new ArgumentOutOfRangeException(
                nameof(options),
                "PollInterval must be between 10 ms and 1 minute.");
        }

        if (options.CloseTimeout < TimeSpan.FromMilliseconds(10) ||
            options.CloseTimeout > TimeSpan.FromSeconds(30))
        {
            throw new ArgumentOutOfRangeException(
                nameof(options),
                "CloseTimeout must be between 10 ms and 30 seconds.");
        }

        if (options.Debounce < TimeSpan.Zero || options.Debounce > TimeSpan.FromDays(1))
            throw new ArgumentOutOfRangeException(nameof(options), "Debounce must be between 0 and 1 day.");

        _options = options;
    }

    public bool IsRunning => Volatile.Read(ref _loop) is not null;

    public event Action<ProcessBlockedObservation>? ProcessBlockedObserved;

    public async Task StartAsync(
        IReadOnlyCollection<string> blockedProcessNames,
        IReadOnlyCollection<string> allowedProcessNames,
        CancellationToken cancellationToken = default)
    {
        var blocked = NormalizeNames(blockedProcessNames);
        var allowed = NormalizeNames(allowedProcessNames);

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            Volatile.Write(ref _blocked, blocked);
            Volatile.Write(ref _allowed, allowed);

            if (_loop is not null)
                return;

            var cts = new CancellationTokenSource();
            _cts = cts;
            _loop = Task.Run(() => WatchLoopAsync(cts.Token));
            _logger.Info(LogCategories.Protection, "Process watcher started.");
        }
        finally
        {
            _gate.Release();
        }
    }

    public void UpdateRules(
        IReadOnlyCollection<string> blockedProcessNames,
        IReadOnlyCollection<string> allowedProcessNames)
    {
        var blocked = NormalizeNames(blockedProcessNames);
        var allowed = NormalizeNames(allowedProcessNames);

        Volatile.Write(ref _blocked, blocked);
        Volatile.Write(ref _allowed, allowed);
    }

    public async Task StopAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var cts = _cts;
            var loop = _loop;
            if (cts is null || loop is null)
                return;

            _cts = null;
            _loop = null;

            cts.Cancel();
            try
            {
                await loop.WaitAsync(TimeSpan.FromSeconds(2)).ConfigureAwait(false);
            }
            catch (TimeoutException)
            {
                _logger.Warning(LogCategories.Protection, "Process watcher did not stop in time.");
            }
            catch (Exception ex)
            {
                _logger.Error(LogCategories.Protection, "Process watcher loop faulted.", ex);
            }

            cts.Dispose();
            _lastAttempt.Clear();
            _logger.Info(LogCategories.Protection, "Process watcher stopped.");
        }
        finally
        {
            _gate.Release();
        }
    }

    public async ValueTask DisposeAsync() => await StopAsync().ConfigureAwait(false);

    private async Task WatchLoopAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                await ScanAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.Error(LogCategories.Protection, "Process watcher scan faulted.", ex);
            }

            try
            {
                await Task.Delay(_options.PollInterval, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }

    private async Task ScanAsync(CancellationToken cancellationToken)
    {
        var blocked = Volatile.Read(ref _blocked);
        if (blocked.Count == 0)
            return;

        var allowed = Volatile.Read(ref _allowed);

        IReadOnlyList<ProcessSnapshot> processes;
        try
        {
            processes = _controller.GetProcesses();
            _scanFaultReported = false;
        }
        catch (Exception ex)
        {
            if (!_scanFaultReported)
            {
                _logger.Warning(
                    LogCategories.Protection,
                    $"Process watcher scan failed ({ex.GetType().Name}).");
                _scanFaultReported = true;
            }

            return;
        }

        foreach (var process in processes)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (process.ProcessId <= 4 || process.ProcessId == Environment.ProcessId)
                continue;
            if (!process.HasMainWindow)
                continue;

            var name = NormalizeName(process.ProcessName);
            if (name is null || !blocked.Contains(name) || allowed.Contains(name))
                continue;
            if (ProtectedProcessNames.Contains(name))
                continue;

            var ticks = _clock.GetTickCount();
            if (_lastAttempt.TryGetValue(name, out var last) && ticks - last < _options.Debounce)
                continue;

            _lastAttempt[name] = ticks;

            var outcome = await StopProcessAsync(name, process.ProcessId, cancellationToken)
                .ConfigureAwait(false);

            Raise(new ProcessBlockedObservation(
                DateTimeOffset.UtcNow,
                name,
                process.ProcessId,
                outcome,
                BuildWarning(outcome)));
        }
    }

    private async Task<ProcessBlockOutcome> StopProcessAsync(
        string name,
        int processId,
        CancellationToken cancellationToken)
    {
        bool closed;
        try
        {
            closed = await _controller
                .CloseMainWindowAsync(processId, _options.CloseTimeout, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.Warning(
                LogCategories.Protection,
                $"Close request failed for '{name}' ({ex.GetType().Name}).");
            closed = false;
        }

        if (closed)
            return ProcessBlockOutcome.ClosedGracefully;

        try
        {
            _controller.KillProcessTree(processId);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.Warning(
                LogCategories.Protection,
                $"Kill failed for '{name}' ({ex.GetType().Name}).");
            return ProcessBlockOutcome.Failed;
        }

        _logger.Warning(
            LogCategories.Protection,
            $"Blocked process '{name}' did not close within the timeout; process tree killed (unsaved data may be lost).");

        return ProcessBlockOutcome.Killed;
    }

    private string? BuildWarning(ProcessBlockOutcome outcome)
    {
        var seconds = _options.CloseTimeout.TotalSeconds.ToString("0.#", CultureInfo.InvariantCulture);

        return outcome switch
        {
            ProcessBlockOutcome.Killed =>
                $"Ứng dụng không đóng sau {seconds} giây nên đã bị kết thúc; dữ liệu chưa lưu có thể bị mất.",
            ProcessBlockOutcome.Failed =>
                "Không thể đóng ứng dụng bị chặn; hãy kiểm tra quyền của EduFocus.",
            _ => null
        };
    }

    private void Raise(ProcessBlockedObservation observation)
    {
        var handler = ProcessBlockedObserved;
        if (handler is null)
            return;

        try
        {
            handler(observation);
        }
        catch (Exception ex)
        {
            _logger.Warning(
                LogCategories.Protection,
                $"Process blocked handler failed ({ex.GetType().Name}).");
        }
    }

    private static FrozenSet<string> NormalizeNames(IReadOnlyCollection<string> names)
    {
        ArgumentNullException.ThrowIfNull(names);

        var set = new HashSet<string>(StringComparer.Ordinal);
        foreach (var raw in names)
        {
            var name = NormalizeName(raw);
            if (name is null)
                throw new ArgumentException(
                    "Process name list contains an invalid name.",
                    nameof(names));

            set.Add(name);
        }

        return set.ToFrozenSet(StringComparer.Ordinal);
    }

    private static string? NormalizeName(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;

        var name = value.Trim();

        var separator = name.LastIndexOfAny(['\\', '/']);
        if (separator >= 0)
            name = name[(separator + 1)..];

        if (name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
            name = name[..^4];

        name = name.Trim().ToLowerInvariant();
        return name.Length is < 1 or > 128 ? null : name;
    }
}
