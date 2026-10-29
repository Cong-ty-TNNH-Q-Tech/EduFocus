using EduFocus.Domain.Entities;
using EduFocus.Domain.Enums;
using EduFocus.Domain.Protection;
using EduFocus.Ports.DTOs;
using EduFocus.Ports.Inbound;
using EduFocus.Ports.Outbound.Persistence;
using EduFocus.Ports.Outbound.Services;

namespace EduFocus.UseCases.Blocking;

/// <summary>
/// Installs the versioned built-in rule catalog (UC05). Rules are matched by their
/// unique identity, so repeated runs never duplicate rows. User-created rules win,
/// and existing built-in rules keep their enabled/expiry state; only category
/// metadata is refreshed. The version is written into audit metadata.
/// </summary>
public sealed class BlockRuleSeedService : IBlockRuleSeedUseCase
{
    private readonly IBlockRuleRepository _rules;
    private readonly IAuditLogRepository _auditLogs;
    private readonly ICorrelationContext _correlation;
    private readonly IAppLogger _logger;
    private readonly TimeProvider _timeProvider;
    private readonly BuiltInRuleCatalog _catalog;

    public BlockRuleSeedService(
        IBlockRuleRepository rules,
        IAuditLogRepository auditLogs,
        ICorrelationContext correlation,
        IAppLogger logger,
        TimeProvider timeProvider,
        BuiltInRuleCatalog catalog)
    {
        _rules = rules ?? throw new ArgumentNullException(nameof(rules));
        _auditLogs = auditLogs ?? throw new ArgumentNullException(nameof(auditLogs));
        _correlation = correlation ?? throw new ArgumentNullException(nameof(correlation));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
        _catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
    }

    public async Task<BlockRuleSeedResult> EnsureSeededAsync(CancellationToken cancellationToken = default)
    {
        var now = UtcNow();
        var existing = await _rules.ListAsync(cancellationToken);
        var byIdentity = existing.ToDictionary(Identity);

        var added = 0;
        var updated = 0;

        foreach (var definition in _catalog.Rules)
        {
            var candidate = BlockRule.Create(
                definition.RuleType,
                definition.MatchType,
                definition.Value,
                definition.Category,
                definition.Effect,
                now,
                isEnabled: definition.IsEnabledByDefault,
                isBuiltIn: true);

            if (!byIdentity.TryGetValue(Identity(candidate), out var stored))
            {
                await _rules.AddAsync(candidate, cancellationToken);
                byIdentity[Identity(candidate)] = candidate;
                added++;
                continue;
            }

            if (!stored.IsBuiltIn || stored.Category == definition.Category)
                continue;

            stored.SetCategory(definition.Category, now);
            await _rules.UpdateAsync(stored, cancellationToken);
            updated++;
        }

        var result = new BlockRuleSeedResult(_catalog.Version, added, updated);
        if (result.Changed)
            await WriteAuditAsync(result, now, cancellationToken);

        return result;
    }

    private async Task WriteAuditAsync(
        BlockRuleSeedResult result,
        DateTime occurredAtUtc,
        CancellationToken cancellationToken)
    {
        try
        {
            var metadata = FormattableString.Invariant(
                $"{{\"seedVersion\":{result.SeedVersion},\"added\":{result.Added},\"updated\":{result.Updated}}}");

            var auditLog = AuditLog.Record(
                AuditActor.System,
                AuditEventType.BlockRuleChanged,
                FormattableString.Invariant(
                    $"Built-in block rules version {result.SeedVersion} applied: {result.Added} added, {result.Updated} updated."),
                occurredAtUtc,
                metadata,
                _correlation.Current);

            await _auditLogs.AddAsync(auditLog, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.Error(LogCategories.Protection, "Could not audit built-in rule seeding.", ex);
        }
    }

    private static (RuleType, RuleMatchType, string, BlockRuleEffect) Identity(BlockRule rule) =>
        (rule.RuleType, rule.MatchType, rule.NormalizedValue, rule.Effect);

    private DateTime UtcNow() => _timeProvider.GetUtcNow().UtcDateTime;
}
