using EduFocus.Domain.Enums;

namespace EduFocus.Domain.Protection;

/// <summary>A default rule shipped with the application (BA §6.1, F04).</summary>
public sealed record BuiltInRuleDefinition(
    RuleType RuleType,
    string Value,
    BlockRuleCategory Category,
    BlockRuleEffect Effect);

/// <summary>
/// Versioned set of default rules. Seeding is idempotent and never overwrites
/// parent changes; the version is recorded in audit metadata when rules change.
/// </summary>
public sealed class BuiltInRuleCatalog
{
    public const int CurrentVersion = 1;

    public static BuiltInRuleCatalog Default { get; } = new(CurrentVersion, CreateDefaultRules());

    private readonly BuiltInRuleDefinition[] _rules;

    public BuiltInRuleCatalog(int version, IEnumerable<BuiltInRuleDefinition> rules)
    {
        ArgumentNullException.ThrowIfNull(rules);

        if (version < 1)
            throw new ArgumentOutOfRangeException(nameof(version), version, "Version must be positive.");

        _rules = rules.ToArray();

        var duplicates = _rules
            .GroupBy(rule => (rule.RuleType, rule.Value, rule.Effect))
            .Where(group => group.Count() > 1)
            .Select(group => group.Key)
            .ToList();

        if (duplicates.Count > 0)
            throw new ArgumentException("Built-in rules must be unique.", nameof(rules));

        Version = version;
    }

    public int Version { get; }

    public IReadOnlyList<BuiltInRuleDefinition> Rules => _rules;

    private static BuiltInRuleDefinition[] CreateDefaultRules()
    {
        BuiltInRuleDefinition Block(string value, BlockRuleCategory category) =>
            new(RuleType.Domain, value, category, BlockRuleEffect.Block);

        BuiltInRuleDefinition Allow(string value) =>
            new(RuleType.Domain, value, BlockRuleCategory.Study, BlockRuleEffect.Allow);

        return
        [
            Block("chatgpt.com", BlockRuleCategory.Chatbot),
            Block("chat.openai.com", BlockRuleCategory.Chatbot),
            Block("claude.ai", BlockRuleCategory.Chatbot),
            Block("gemini.google.com", BlockRuleCategory.Chatbot),
            Block("copilot.microsoft.com", BlockRuleCategory.Chatbot),
            Block("perplexity.ai", BlockRuleCategory.Chatbot),
            Block("poe.com", BlockRuleCategory.Chatbot),
            Block("character.ai", BlockRuleCategory.Chatbot),
            Block("deepseek.com", BlockRuleCategory.Chatbot),
            Block("grok.com", BlockRuleCategory.Chatbot),
            Block("meta.ai", BlockRuleCategory.Chatbot),
            Block("you.com", BlockRuleCategory.Chatbot),
            Block("pi.ai", BlockRuleCategory.Chatbot),
            Block("huggingface.co", BlockRuleCategory.Chatbot),

            Allow("meet.google.com"),
            Allow("teams.microsoft.com"),
            Allow("classroom.google.com"),
            Allow("accounts.google.com"),
            Allow("login.microsoftonline.com"),
            Allow("login.live.com"),
            Allow("zoom.us")
        ];
    }
}
