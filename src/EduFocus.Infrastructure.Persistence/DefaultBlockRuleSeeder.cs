using System.Text.Json;
using EduFocus.Domain.Entities;
using EduFocus.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace EduFocus.Infrastructure.Persistence;

/// <summary>Versioned, additive built-in rules. Parent edits and disabled state are never overwritten.</summary>
public sealed class DefaultBlockRuleSeeder(AppDbContext db)
{
    public const int CurrentVersion = 2;
    private static readonly SemaphoreSlim Gate = new(1, 1);
    private static readonly HashSet<string> EssentialAllowlist = new(StringComparer.OrdinalIgnoreCase)
    {
        "meet.google.com", "teams.microsoft.com", "classroom.google.com", "accounts.google.com",
        "login.microsoftonline.com", "login.live.com", "zoom.us"
    };

    public static bool IsEssentialAllowlist(string domain) => EssentialAllowlist.Contains(domain);

    public async Task SeedAsync(DateTimeOffset now, int throughVersion = CurrentVersion, CancellationToken cancellationToken = default)
    {
        if (now.Offset != TimeSpan.Zero) throw new ArgumentException("Timestamp must be UTC.", nameof(now));
        if (throughVersion is < 1 or > CurrentVersion) throw new ArgumentOutOfRangeException(nameof(throughVersion));
        await Gate.WaitAsync(cancellationToken);
        try
        {
            await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
            for (var version = 1; version <= throughVersion; version++)
            {
                var metadata = JsonSerializer.Serialize(new { version });
                if (await db.AuditLogs.AnyAsync(x => x.EventType == "DefaultBlockRulesSeeded" && x.MetadataJson == metadata, cancellationToken))
                    continue;

                var existing = await db.BlockRules.ToListAsync(cancellationToken);
                foreach (var definition in Definitions(version))
                {
                    var normalized = BlockRule.Create(BlockRuleType.Domain, BlockMatchType.Exact, definition.Domain,
                        definition.Category, definition.Effect, now, enabled: true, builtIn: true);
                    if (existing.Any(rule => rule.RuleType == normalized.RuleType && rule.MatchType == normalized.MatchType &&
                                             rule.NormalizedValue == normalized.NormalizedValue && rule.Effect == normalized.Effect))
                        continue;
                    db.BlockRules.Add(normalized);
                    existing.Add(normalized);
                }

                db.AuditLogs.Add(AuditLog.Create(now, AuditActor.System, "DefaultBlockRulesSeeded",
                    $"Built-in block rules seed version {version} was applied.", now, metadata));
                await db.SaveChangesAsync(cancellationToken);
            }
            await transaction.CommitAsync(cancellationToken);
        }
        finally { Gate.Release(); }
    }

    private static IEnumerable<RuleDefinition> Definitions(int version)
    {
        // Version 1 follows the schema examples and the required learning-service allowlist.
        yield return new("chatgpt.com", BlockRuleCategory.Chatbot, BlockRuleEffect.Block);
        yield return new("chat.openai.com", BlockRuleCategory.Chatbot, BlockRuleEffect.Block);
        yield return new("claude.ai", BlockRuleCategory.Chatbot, BlockRuleEffect.Block);
        yield return new("gemini.google.com", BlockRuleCategory.Chatbot, BlockRuleEffect.Block);
        yield return new("copilot.microsoft.com", BlockRuleCategory.Chatbot, BlockRuleEffect.Block);
        yield return new("meet.google.com", BlockRuleCategory.Study, BlockRuleEffect.Allow);
        yield return new("teams.microsoft.com", BlockRuleCategory.Study, BlockRuleEffect.Allow);
        yield return new("classroom.google.com", BlockRuleCategory.Study, BlockRuleEffect.Allow);
        yield return new("accounts.google.com", BlockRuleCategory.Study, BlockRuleEffect.Allow);
        yield return new("login.microsoftonline.com", BlockRuleCategory.Study, BlockRuleEffect.Allow);
        yield return new("login.live.com", BlockRuleCategory.Study, BlockRuleEffect.Allow);
        yield return new("zoom.us", BlockRuleCategory.Study, BlockRuleEffect.Allow);

        if (version < 2) yield break;
        // These are dedicated chatbot hosts. Broad shared roots (Google, Microsoft, You.com,
        // Hugging Face) are intentionally omitted because exact host blocking cannot isolate chat paths.
        yield return new("perplexity.ai", BlockRuleCategory.Chatbot, BlockRuleEffect.Block);
        yield return new("poe.com", BlockRuleCategory.Chatbot, BlockRuleEffect.Block);
        yield return new("character.ai", BlockRuleCategory.Chatbot, BlockRuleEffect.Block);
        yield return new("chat.deepseek.com", BlockRuleCategory.Chatbot, BlockRuleEffect.Block);
        yield return new("grok.com", BlockRuleCategory.Chatbot, BlockRuleEffect.Block);
        yield return new("meta.ai", BlockRuleCategory.Chatbot, BlockRuleEffect.Block);
        yield return new("pi.ai", BlockRuleCategory.Chatbot, BlockRuleEffect.Block);
    }

    private sealed record RuleDefinition(string Domain, BlockRuleCategory Category, BlockRuleEffect Effect);
}
