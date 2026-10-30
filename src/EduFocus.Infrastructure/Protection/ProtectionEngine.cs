using System.Collections.Concurrent;
using System.Threading.Channels;
using EduFocus.Domain.Entities;
using EduFocus.Domain.Enums;
using EduFocus.Domain.Privacy;
using EduFocus.Domain.Protection;
using EduFocus.Domain.ValueObjects;
using EduFocus.Infrastructure.Protection.Certificates;
using EduFocus.Ports.Outbound.Persistence;
using EduFocus.Ports.Outbound.Services;
using EduFocus.Ports.Protection;

namespace EduFocus.Infrastructure.Protection;

/// <summary>
/// Coordinates hosts, block page and process watcher (UC03, UC04, architecture
/// §6). Apply order is block page → hosts → watcher so loopback is already
/// serving before domains point at it. Any failure rolls every step back and
/// surfaces as <see cref="ProtectionEngineException"/>, except the block page
/// reporting that its port is busy, which is a degraded-but-active warning.
/// Remove reverses the order and clears rules before stopping adapters so a
/// failed stop cannot keep closing apps. Blocked attempts from both channels
/// are debounced here and persisted off the adapter threads.
/// </summary>
public sealed class ProtectionEngine : IProtectionEnginePort, IAsyncDisposable
{
    private const int MaxDomains = 10_000;

    public const string WatcherStopWarning =
        "Không thể dừng process watcher; các rule đã được gỡ để không tiếp tục đóng ứng dụng.";

    public const string PageStopWarning =
        "Không thể dừng block page; listener có thể vẫn mở nhưng không còn domain bị chặn.";

    public const string BrowserBridgeWarning =
        "Không thể khởi động cầu nối extension trình duyệt; chặn URL/path tạm thời không hiệu lực.";

    public const string BrowserDnsPolicyWarning =
        "Không thể tắt Secure DNS của Chrome/Edge; trình duyệt có thể bỏ qua hosts. " +
        "Hãy chạy EduFocus với quyền quản trị và khởi động lại trình duyệt.";

    public const string BrowserDnsPolicyRestoreWarning =
        "Không thể khôi phục policy Secure DNS trước đó của trình duyệt; " +
        "policy sẽ được thử lại ở lần khởi động sau.";

    public const string CertificateAuthorityRemoveWarning =
        "Không thể gỡ chứng chỉ cục bộ khỏi kho tin cậy khi tắt bảo vệ; " +
        "hãy kiểm tra Certificate Manager (Local Computer → Trusted Root).";

    private readonly IBlockRuleRepository _rules;
    private readonly IHostsFileBlocker _hosts;
    private readonly IBlockPageServer _blockPage;
    private readonly IProcessWatcher _watcher;
    private readonly IBlockedAttemptRepository _attempts;
    private readonly IStudySessionRepository _sessions;
    private readonly IMonotonicClockPort _clock;
    private readonly IAppLogger _logger;
    private readonly TimeProvider _timeProvider;
    private readonly ProtectionEngineOptions _options;
    private readonly IBrowserExtensionBridge? _browserBridge;
    private readonly IBrowserDnsPolicyPort? _browserDnsPolicy;
    private readonly ILocalCertificateAuthority? _certificateAuthority;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly ConcurrentDictionary<string, TimeSpan> _lastRecorded =
        new(StringComparer.Ordinal);

    private Channel<AttemptRecord>? _queue;
    private Task? _writer;
    private volatile bool _accepting;
    private bool _active;

    public ProtectionEngine(
        IBlockRuleRepository rules,
        IHostsFileBlocker hosts,
        IBlockPageServer blockPage,
        IProcessWatcher watcher,
        IBlockedAttemptRepository attempts,
        IStudySessionRepository sessions,
        IMonotonicClockPort clock,
        IAppLogger logger,
        TimeProvider timeProvider,
        ProtectionEngineOptions options,
        IBrowserExtensionBridge? browserBridge = null,
        IBrowserDnsPolicyPort? browserDnsPolicy = null,
        ILocalCertificateAuthority? certificateAuthority = null)
    {
        _rules = rules ?? throw new ArgumentNullException(nameof(rules));
        _hosts = hosts ?? throw new ArgumentNullException(nameof(hosts));
        _blockPage = blockPage ?? throw new ArgumentNullException(nameof(blockPage));
        _watcher = watcher ?? throw new ArgumentNullException(nameof(watcher));
        _attempts = attempts ?? throw new ArgumentNullException(nameof(attempts));
        _sessions = sessions ?? throw new ArgumentNullException(nameof(sessions));
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));

        if (options is null)
            throw new ArgumentNullException(nameof(options));
        if (options.Debounce < TimeSpan.Zero || options.Debounce > TimeSpan.FromDays(1))
            throw new ArgumentOutOfRangeException(nameof(options), "Debounce must be between 0 and 1 day.");

        _options = options;
        _browserBridge = browserBridge;
        _browserDnsPolicy = browserDnsPolicy;
        _certificateAuthority = certificateAuthority;

        _blockPage.BlockedRequestObserved += OnBlockPageHit;
        _watcher.ProcessBlockedObserved += OnProcessBlocked;

        if (_browserBridge is not null)
            _browserBridge.BlockedAttemptObserved += OnBrowserBlocked;
    }

    public async Task<IReadOnlyList<string>> ApplyAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            return await ApplyCoreAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<IReadOnlyList<string>> RemoveAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            return await RemoveCoreAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<IReadOnlyList<string>> ReconcileAsync(
        bool shouldBeActive,
        CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            try
            {
                await _hosts.SelfHealAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                throw new ProtectionEngineException(
                    "reconcile",
                    "Could not repair the hosts file state at startup.",
                    ex);
            }

            return shouldBeActive
                ? await ApplyCoreAsync(cancellationToken).ConfigureAwait(false)
                : await RemoveCoreAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async ValueTask DisposeAsync()
    {
        _blockPage.BlockedRequestObserved -= OnBlockPageHit;
        _watcher.ProcessBlockedObserved -= OnProcessBlocked;

        if (_browserBridge is not null)
        {
            _browserBridge.BlockedAttemptObserved -= OnBrowserBlocked;

            try
            {
                await _browserBridge.StopAsync(CancellationToken.None).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger.Warning(
                    LogCategories.Protection,
                    $"Could not stop the browser extension bridge ({ex.GetType().Name}).");
            }
        }

        _accepting = false;

        var queue = Interlocked.Exchange(ref _queue, null);
        await DrainAsync(queue, _writer).ConfigureAwait(false);
        _writer = null;
        _gate.Dispose();
    }

    private async Task<IReadOnlyList<string>> ApplyCoreAsync(CancellationToken cancellationToken)
    {
        if (_active)
            return [];

        var nowUtc = _timeProvider.GetUtcNow().UtcDateTime;
        EffectiveRules effective;
        try
        {
            effective = await LoadRulesAsync(nowUtc, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            throw new ProtectionEngineException("apply", "Could not load block rules.", ex);
        }

        var queue = Channel.CreateUnbounded<AttemptRecord>(new UnboundedChannelOptions
        {
            SingleReader = true,
            SingleWriter = false
        });

        _queue = queue;
        _writer = Task.Run(() => WriteLoopAsync(queue.Reader));
        _accepting = true;
        _lastRecorded.Clear();

        var warnings = new List<string>();

        try
        {
            // Secure DNS must be off before traffic is pointed at loopback,
            // otherwise a Chromium browser can resolve the real address.
            warnings.AddRange(
                await ApplyBrowserDnsPolicyAsync(cancellationToken).ConfigureAwait(false));
        }
        catch (OperationCanceledException)
        {
            await AbortApplyAsync(queue).ConfigureAwait(false);
            throw;
        }

        // The HTTPS listener is only started when the root is trusted, so the
        // trust step must finish before the block page binds port 443.
        warnings.AddRange(
            await EnsureCertificateAuthorityAsync(cancellationToken).ConfigureAwait(false));

        BlockPageStartResult page;
        try
        {
            page = await _blockPage
                .StartAsync(effective.Domains, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            await AbortApplyAsync(queue).ConfigureAwait(false);
            throw;
        }
        catch (Exception ex)
        {
            await AbortApplyAsync(queue).ConfigureAwait(false);
            throw new ProtectionEngineException(
                "apply",
                "Could not start the block page listener.",
                ex);
        }

        if (!page.Started)
        {
            warnings.Add(page.Warning
                ?? "Block page không khả dụng; trạng thái bảo vệ vẫn được giữ.");
        }

        try
        {
            await _hosts.ApplyAsync(effective.Domains, cancellationToken).ConfigureAwait(false);
            await _watcher
                .StartAsync(effective.BlockedApps, effective.AllowedApps, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            await AbortApplyAsync(queue).ConfigureAwait(false);
            throw;
        }
        catch (Exception ex)
        {
            await AbortApplyAsync(queue).ConfigureAwait(false);
            throw new ProtectionEngineException(
                "apply",
                "Could not apply the hosts file or the process watcher.",
                ex);
        }

        if (_browserBridge is not null)
        {
            try
            {
                await _browserBridge
                    .StartAsync(effective.ExtensionRules.ToArray(), nowUtc, cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                await AbortApplyAsync(queue).ConfigureAwait(false);
                throw;
            }
            catch (Exception ex)
            {
                // The extension is an additional layer; OS-level protection stays on.
                _logger.Error(
                    LogCategories.Protection,
                    "Could not start the browser extension bridge.",
                    ex);
                warnings.Add(BrowserBridgeWarning);
            }
        }

        _active = true;
        _logger.Info(
            LogCategories.Protection,
            $"Protection engine applied: {effective.Domains.Count} blocked domains, " +
            $"{effective.BlockedApps.Count} blocked apps.");
        return warnings;
    }

    private async Task AbortApplyAsync(Channel<AttemptRecord> queue)
    {
        _accepting = false;

        await StopBrowserBridgeAsync().ConfigureAwait(false);
        await TryAsync(() => _watcher.UpdateRules([], []), "clear watcher rules").ConfigureAwait(false);
        await TryAsync(() => _watcher.StopAsync(CancellationToken.None), "stop the process watcher")
            .ConfigureAwait(false);
        await TryAsync(() => _blockPage.UpdateDomains([]), "clear block page domains").ConfigureAwait(false);
        await TryAsync(() => _blockPage.StopAsync(CancellationToken.None), "stop the block page listener")
            .ConfigureAwait(false);
        await TryAsync(() => _hosts.RestoreAsync(CancellationToken.None), "restore the hosts file")
            .ConfigureAwait(false);
        await RestoreBrowserDnsPolicyAsync(CancellationToken.None).ConfigureAwait(false);
        await TryAsync(
                async () => await RemoveCertificateAuthorityTrustAsync(CancellationToken.None)
                    .ConfigureAwait(false),
                "remove the block-page root certificate")
            .ConfigureAwait(false);

        await DrainAsync(queue, _writer).ConfigureAwait(false);
        _writer = null;
        _queue = null;
        _lastRecorded.Clear();
    }

    private async Task<IReadOnlyList<string>> RemoveCoreAsync(CancellationToken cancellationToken)
    {
        _accepting = false;
        await StopBrowserBridgeAsync().ConfigureAwait(false);

        var queue = _queue;
        var writer = _writer;
        _queue = null;
        _writer = null;

        var warnings = new List<string>();

        try
        {
            _watcher.UpdateRules([], []);
        }
        catch (Exception ex)
        {
            _logger.Warning(
                LogCategories.Protection,
                $"Could not clear watcher rules ({ex.GetType().Name}).");
        }

        try
        {
            _blockPage.UpdateDomains([]);
        }
        catch (Exception ex)
        {
            _logger.Warning(
                LogCategories.Protection,
                $"Could not clear block page domains ({ex.GetType().Name}).");
        }

        try
        {
            await _watcher.StopAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.Warning(
                LogCategories.Protection,
                $"Could not stop the process watcher ({ex.GetType().Name}).");
            warnings.Add(WatcherStopWarning);
        }

        try
        {
            await _blockPage.StopAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.Warning(
                LogCategories.Protection,
                $"Could not stop the block page ({ex.GetType().Name}).");
            warnings.Add(PageStopWarning);
        }

        Exception? hostsFailure = null;
        try
        {
            await _hosts.RestoreAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.Error(LogCategories.Protection, "Could not remove the hosts block section.", ex);
            hostsFailure = ex;
        }

        warnings.AddRange(
            await RestoreBrowserDnsPolicyAsync(CancellationToken.None).ConfigureAwait(false));

        // Protection is OFF and the HTTPS listener has stopped, so the root no
        // longer needs to be trusted on this machine.
        warnings.AddRange(
            await RemoveCertificateAuthorityTrustAsync(CancellationToken.None).ConfigureAwait(false));

        await DrainAsync(queue, writer).ConfigureAwait(false);
        _active = false;
        _lastRecorded.Clear();

        if (hostsFailure is not null)
        {
            throw new ProtectionEngineException(
                "remove",
                "Could not restore the hosts file; protection stays ON.",
                hostsFailure);
        }

        _logger.Info(LogCategories.Protection, "Protection engine removed.");
        return warnings;
    }

    private async Task<EffectiveRules> LoadRulesAsync(
        DateTime nowUtc,
        CancellationToken cancellationToken)
    {
        var all = await _rules.ListAsync(cancellationToken).ConfigureAwait(false);

        var effective = all
            .Where(rule => rule.MatchType == RuleMatchType.Exact && rule.IsEffective(nowUtc))
            .ToList();

        var extensionRules = all
            .Where(rule => rule.IsEffective(nowUtc))
            .ToArray();

        var allowedDomains = effective
            .Where(rule => rule.RuleType == RuleType.Domain && rule.Effect == BlockRuleEffect.Allow)
            .Select(rule => rule.NormalizedValue)
            .ToHashSet(StringComparer.Ordinal);

        var domains = effective
            .Where(rule => rule.RuleType == RuleType.Domain
                && rule.Effect == BlockRuleEffect.Block
                && !allowedDomains.Contains(rule.NormalizedValue)
                && !ProtectedDomains.IsProtected(DomainName.Parse(rule.NormalizedValue)))
            .Select(rule => rule.NormalizedValue)
            .Distinct(StringComparer.Ordinal)
            .OrderBy(value => value, StringComparer.Ordinal)
            .Take(MaxDomains)
            .ToArray();

        var allowedApps = effective
            .Where(rule => rule.RuleType == RuleType.App && rule.Effect == BlockRuleEffect.Allow)
            .Select(rule => rule.NormalizedValue)
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        var allowedAppSet = allowedApps.ToHashSet(StringComparer.Ordinal);

        var blockedApps = effective
            .Where(rule => rule.RuleType == RuleType.App
                && rule.Effect == BlockRuleEffect.Block
                && !allowedAppSet.Contains(rule.NormalizedValue))
            .Select(rule => rule.NormalizedValue)
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        return new EffectiveRules(domains, blockedApps, allowedApps, extensionRules);
    }

    private void OnBlockPageHit(BlockPageHit hit)
    {
        if (hit is null || !_accepting)
            return;

        TryEnqueue(hit.Domain, hit.ObservedAtUtc, BlockedAttemptTargetType.Domain);
    }

    private void OnProcessBlocked(ProcessBlockedObservation observation)
    {
        if (observation is null || !_accepting)
            return;

        TryEnqueue(observation.ProcessName, observation.ObservedAtUtc, BlockedAttemptTargetType.App);
    }

    private void OnBrowserBlocked(object? sender, BrowserBlockedAttempt attempt)
    {
        if (attempt is null || !_accepting)
            return;

        TryEnqueue(attempt.Url, attempt.ObservedAtUtc, BlockedAttemptTargetType.Url);
    }

    private void TryEnqueue(string value, DateTimeOffset observedAtUtc, BlockedAttemptTargetType targetType)
    {
        if (string.IsNullOrWhiteSpace(value))
            return;

        var queue = Volatile.Read(ref _queue);
        if (queue is null)
            return;

        var prefix = targetType switch
        {
            BlockedAttemptTargetType.Domain => "d:",
            BlockedAttemptTargetType.App => "a:",
            _ => "u:"
        };

        var key = prefix + value.Trim().ToLowerInvariant();
        var now = _clock.GetTickCount();
        if (_lastRecorded.TryGetValue(key, out var last) && now - last < _options.Debounce)
            return;

        var record = new AttemptRecord(value, observedAtUtc.UtcDateTime, targetType);
        if (!queue.Writer.TryWrite(record))
            return;

        _lastRecorded[key] = now;
    }

    private async Task WriteLoopAsync(ChannelReader<AttemptRecord> reader)
    {
        await foreach (var record in reader.ReadAllAsync().ConfigureAwait(false))
        {
            try
            {
                await PersistAsync(record).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger.Error(LogCategories.Protection, "Could not record a blocked attempt.", ex);
            }
        }
    }

    private async Task PersistAsync(AttemptRecord record)
    {
        var session = await _sessions.GetActiveAsync(CancellationToken.None).ConfigureAwait(false);

        var attempt = record.TargetType switch
        {
            BlockedAttemptTargetType.Domain => BlockedAttempt.Record(
                record.OccurredAtUtc,
                BlockedAttemptTargetType.Domain,
                record.Value,
                BlockedAttemptObservedVia.BlockPage,
                session?.Id),
            BlockedAttemptTargetType.App => BlockedAttempt.Record(
                record.OccurredAtUtc,
                BlockedAttemptTargetType.App,
                record.Value,
                BlockedAttemptObservedVia.ProcessWatcher,
                session?.Id,
                processName: record.Value),
            BlockedAttemptTargetType.Url => BlockedAttempt.Record(
                record.OccurredAtUtc,
                BlockedAttemptTargetType.Url,
                record.Value,
                BlockedAttemptObservedVia.Extension,
                session?.Id),
            _ => throw new InvalidOperationException("Unknown blocked attempt target type.")
        };

        await _attempts.AddAsync(attempt, CancellationToken.None).ConfigureAwait(false);

        if (session is null)
            return;

        try
        {
            // Targeted update: a full-entity write here could overwrite a
            // heartbeat that landed while the active session was being read.
            await _sessions
                .IncrementBlockedAttemptCountAsync(
                    session.Id,
                    _timeProvider.GetUtcNow().UtcDateTime,
                    count: 1,
                    CancellationToken.None)
                .ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.Error(LogCategories.Protection, "Could not update the session blocked count.", ex);
        }
    }

    private async Task DrainAsync(Channel<AttemptRecord>? queue, Task? writer)
    {
        if (queue is null || writer is null)
            return;

        queue.Writer.TryComplete();
        try
        {
            await writer.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
        }
        catch (TimeoutException)
        {
            _logger.Warning(LogCategories.Protection, "Blocked attempt writer did not stop in time.");
        }
        catch (Exception ex)
        {
            _logger.Error(LogCategories.Protection, "Blocked attempt writer faulted.", ex);
        }
    }

    private async Task TryAsync(Func<Task> action, string description)
    {
        try
        {
            await action().ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.Warning(
                LogCategories.Protection,
                $"Could not {description} during rollback ({ex.GetType().Name}).");
        }
    }

    private Task TryAsync(Action action, string description) =>
        TryAsync(
            () =>
            {
                action();
                return Task.CompletedTask;
            },
            description);

    private async Task<IReadOnlyList<string>> ApplyBrowserDnsPolicyAsync(
        CancellationToken cancellationToken)
    {
        if (_browserDnsPolicy is null)
            return [];

        try
        {
            return await _browserDnsPolicy.ApplyAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.Error(LogCategories.Protection, "Could not disable browser Secure DNS.", ex);
            return [BrowserDnsPolicyWarning];
        }
    }

    private async Task<IReadOnlyList<string>> RestoreBrowserDnsPolicyAsync(
        CancellationToken cancellationToken)
    {
        if (_browserDnsPolicy is null)
            return [];

        try
        {
            return await _browserDnsPolicy.RestoreAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.Warning(
                LogCategories.Protection,
                $"Could not restore the browser Secure DNS policy ({ex.GetType().Name}).");
            return [BrowserDnsPolicyRestoreWarning];
        }
    }

    private async Task<IReadOnlyList<string>> EnsureCertificateAuthorityAsync(
        CancellationToken cancellationToken)
    {
        if (_certificateAuthority is null)
            return [];

        try
        {
            var result = await _certificateAuthority
                .EnsureTrustedAsync(cancellationToken)
                .ConfigureAwait(false);

            return result.Warning is null ? [] : [result.Warning];
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.Error(
                LogCategories.Protection,
                "Could not trust the local block-page certificate authority.",
                ex);
            return [LocalCertificateAuthority.TrustWarning];
        }
    }

    private async Task<IReadOnlyList<string>> RemoveCertificateAuthorityTrustAsync(
        CancellationToken cancellationToken)
    {
        if (_certificateAuthority is null)
            return [];

        try
        {
            var result = await _certificateAuthority
                .RemoveTrustAsync(cancellationToken)
                .ConfigureAwait(false);

            return result.Warning is null ? [] : [result.Warning];
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.Warning(
                LogCategories.Protection,
                $"Could not remove the local block-page certificate authority ({ex.GetType().Name}).");
            return [CertificateAuthorityRemoveWarning];
        }
    }

    private async Task StopBrowserBridgeAsync()
    {
        if (_browserBridge is null)
            return;

        try
        {
            await _browserBridge.StopAsync(CancellationToken.None).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.Warning(
                LogCategories.Protection,
                $"Could not stop the browser extension bridge ({ex.GetType().Name}).");
        }
    }

    private sealed record EffectiveRules(
        IReadOnlyCollection<string> Domains,
        IReadOnlyCollection<string> BlockedApps,
        IReadOnlyCollection<string> AllowedApps,
        IReadOnlyCollection<BlockRule> ExtensionRules);

    private readonly record struct AttemptRecord(
        string Value,
        DateTime OccurredAtUtc,
        BlockedAttemptTargetType TargetType);
}
