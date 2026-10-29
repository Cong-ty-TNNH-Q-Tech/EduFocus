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

    public BlockRuleService(
        IBlockRuleRepository rules,
        IParentAuthorizer authorizer,
        IAuditLogRepository auditLogs,
        ICorrelationContext correlation,
        IAppLogger logger,
        TimeProvider timeProvider)
    {
        _rules = rules ?? throw new ArgumentNullException(nameof(rules));
        _authorizer = authorizer ?? throw new ArgumentNullException(nameof(authorizer));
        _auditLogs = auditLogs ?? throw new ArgumentNullException(nameof(auditLogs));
        _correlation = correlation ?? throw new ArgumentNullException(nameof(correlation));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
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
        rule.SetEnabled(isEnabled, now);
        await _rules.UpdateAsync(rule, cancellationToken);

        await WriteAuditAsync(
            $"Parent {(isEnabled ? "enabled" : "disabled")} {(rule.IsBuiltIn ? "built-in " : string.Empty)}" +
            $"{rule.RuleType.ToString().ToLowerInvariant()} rule {rule.NormalizedValue}.",
            now,
            cancellationToken);

        return new BlockRuleOperationResult(Changed: true, ToInfo(rule));
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
            rule.Category,
            rule.Effect,
            rule.NormalizedValue,
            rule.IsBuiltIn,
            rule.IsEnabled,
            rule.EnforcementStatus,
            rule.ExpiresAtUtc);
}
