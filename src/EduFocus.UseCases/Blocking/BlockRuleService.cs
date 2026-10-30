using EduFocus.Domain.Entities;
using EduFocus.Domain.Enums;
using EduFocus.Domain.ValueObjects;
using EduFocus.Ports.DTOs;
using EduFocus.Ports.Inbound;
using EduFocus.Ports.Outbound.Persistence;
using EduFocus.Ports.Outbound.Services;

namespace EduFocus.UseCases.Blocking;

/// <summary>
/// Parent Mode blocklist management (UC05). Reading and toggling rules require an
/// active Parent Mode session; every state change is audited with the parent actor.
/// Built-in rules can only be disabled, never deleted (BA §6.1, F04).
/// </summary>
public sealed class BlockRuleService : IBlockRuleUseCase
{
    private readonly IBlockRuleRepository _rules;
    private readonly IParentAuthorizer _authorizer;
    private readonly IAuditLogRepository _auditLogs;
    private readonly ICorrelationContext _correlation;
    private readonly IAppLogger _logger;
    private readonly TimeProvider _timeProvider;
    private readonly IBrowserExtensionBridge _browserBridge;
    private readonly IProtectionUseCase? _protection;
    private readonly IActivityUseCase? _activity;

    public BlockRuleService(
        IBlockRuleRepository rules,
        IParentAuthorizer authorizer,
        IAuditLogRepository auditLogs,
        ICorrelationContext correlation,
        IAppLogger logger,
        TimeProvider timeProvider,
        IBrowserExtensionBridge browserBridge,
        IProtectionUseCase? protection = null,
        IActivityUseCase? activity = null)
    {
        _rules = rules ?? throw new ArgumentNullException(nameof(rules));
        _authorizer = authorizer ?? throw new ArgumentNullException(nameof(authorizer));
        _auditLogs = auditLogs ?? throw new ArgumentNullException(nameof(auditLogs));
        _correlation = correlation ?? throw new ArgumentNullException(nameof(correlation));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
        _browserBridge = browserBridge ?? throw new ArgumentNullException(nameof(browserBridge));
        _protection = protection;
        _activity = activity;
    }

    public async Task<IReadOnlyList<BlockRuleInfo>> ListAsync(CancellationToken cancellationToken = default)
    {
        _authorizer.EnsureAuthorized("view the blocklist");

        var rules = await _rules.ListAsync(cancellationToken);
        return rules
            .Select(ToInfo)
            .OrderBy(info => info.Category)
            .ThenBy(info => info.Effect)
            .ThenBy(info => info.Value, StringComparer.Ordinal)
            .ToList();
    }

    public async Task<BlockRuleOperationResult> SetEnabledAsync(
        Guid id,
        bool isEnabled,
        CancellationToken cancellationToken = default)
    {
        _authorizer.EnsureAuthorized("change the blocklist");

        var rule = await _rules.GetAsync(BlockRuleId.From(id), cancellationToken)
            ?? throw new InvalidOperationException("Block rule was not found.");

        if (rule.IsEnabled == isEnabled)
            return new BlockRuleOperationResult(Changed: false, ToInfo(rule));

        var now = _timeProvider.GetUtcNow().UtcDateTime;
        var effectiveNow = now < rule.UpdatedAtUtc ? rule.UpdatedAtUtc : now;
        rule.SetEnabled(isEnabled, effectiveNow);
        await _rules.UpdateAsync(rule, cancellationToken);

        await WriteAuditAsync(
            $"Parent {(isEnabled ? "enabled" : "disabled")} {(rule.IsBuiltIn ? "built-in " : string.Empty)}" +
            $"{rule.RuleType.ToString().ToLowerInvariant()} rule {rule.NormalizedValue}.",
            now,
            cancellationToken);

        await RefreshBrowserRulesAsync(now, cancellationToken);
        await RefreshActiveEnforcementAsync(cancellationToken);

        return new BlockRuleOperationResult(Changed: true, ToInfo(rule));
    }

    public async Task<BlockRuleOperationResult> AddCustomAsync(
        CreateBlockRuleRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        _authorizer.EnsureAuthorized("add a block rule");

        var now = _timeProvider.GetUtcNow().UtcDateTime;
        var candidate = BlockRule.Create(
            request.RuleType,
            request.MatchType,
            request.Value,
            request.Category,
            request.Effect,
            now,
            isEnabled: true,
            isBuiltIn: false,
            expiresAtUtc: request.ExpiresAtUtc,
            note: request.Note);

        var existingRules = await _rules.ListAsync(cancellationToken);
        var duplicate = existingRules.FirstOrDefault(r =>
            r.RuleType == candidate.RuleType &&
            r.MatchType == candidate.MatchType &&
            r.Effect == candidate.Effect &&
            string.Equals(r.NormalizedValue, candidate.NormalizedValue, StringComparison.Ordinal));

        if (duplicate is not null)
            throw new InvalidOperationException("Rule này đã tồn tại trong danh sách.");

        await _rules.AddAsync(candidate, cancellationToken);

        await WriteAuditAsync(
            $"Parent added custom {candidate.RuleType.ToString().ToLowerInvariant()} rule {candidate.NormalizedValue}.",
            now,
            cancellationToken);

        await RefreshBrowserRulesAsync(now, cancellationToken);
        await RefreshActiveEnforcementAsync(cancellationToken);

        return new BlockRuleOperationResult(Changed: true, ToInfo(candidate));
    }

    public async Task<bool> DeleteCustomAsync(
        Guid id,
        CancellationToken cancellationToken = default)
    {
        _authorizer.EnsureAuthorized("delete a block rule");

        var rule = await _rules.GetAsync(BlockRuleId.From(id), cancellationToken);
        if (rule is null)
            return false;

        if (rule.IsBuiltIn)
            throw new InvalidOperationException("Built-in rules cannot be deleted; disable them instead.");

        var now = _timeProvider.GetUtcNow().UtcDateTime;
        await _rules.DeleteAsync(rule.Id, cancellationToken);

        await WriteAuditAsync(
            $"Parent deleted custom {rule.RuleType.ToString().ToLowerInvariant()} rule {rule.NormalizedValue}.",
            now,
            cancellationToken);

        await RefreshBrowserRulesAsync(now, cancellationToken);
        await RefreshActiveEnforcementAsync(cancellationToken);

        return true;
    }

    private async Task RefreshActiveEnforcementAsync(CancellationToken cancellationToken)
    {
        if (_protection is not null)
        {
            try
            {
                await _protection.ReloadRulesAsync(cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                _logger.Warning(
                    LogCategories.Protection,
                    $"Could not reload active protection rules ({ex.GetType().Name}).");
            }
        }

        if (_activity is { IsRunning: true })
        {
            try
            {
                await _activity.RefreshSettingsAsync(cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                _logger.Warning(
                    LogCategories.Session,
                    $"Could not refresh activity tracking rules ({ex.GetType().Name}).");
            }
        }
    }

    private async Task RefreshBrowserRulesAsync(DateTime nowUtc, CancellationToken cancellationToken)
    {
        if (!_browserBridge.IsRunning)
            return;

        try
        {
            var rules = await _rules.ListAsync(cancellationToken);
            await _browserBridge.UpdateRulesAsync(rules, nowUtc, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.Warning(
                LogCategories.Protection,
                $"Could not refresh browser extension rules ({ex.GetType().Name}).");
        }
    }

    private async Task WriteAuditAsync(
        string description,
        DateTime occurredAtUtc,
        CancellationToken cancellationToken)
    {
        try
        {
            var auditLog = AuditLog.Record(
                AuditActor.Parent,
                AuditEventType.BlockRuleChanged,
                description,
                occurredAtUtc,
                metadataJson: null,
                _correlation.Current);

            await _auditLogs.AddAsync(auditLog, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.Error(LogCategories.Protection, "Could not audit a blocklist change.", ex);
        }
    }

    private static BlockRuleInfo ToInfo(BlockRule rule) =>
        new(
            rule.Id.Value,
            rule.RuleType,
            rule.MatchType,
            rule.Category,
            rule.Effect,
            rule.NormalizedValue,
            rule.IsBuiltIn,
            rule.IsEnabled,
            rule.EnforcementStatus,
            rule.ExpiresAtUtc);
}
