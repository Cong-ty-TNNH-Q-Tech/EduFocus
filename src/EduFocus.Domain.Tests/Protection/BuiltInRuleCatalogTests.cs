using EduFocus.Domain.Entities;
using EduFocus.Domain.Enums;
using EduFocus.Domain.Protection;
using EduFocus.Domain.ValueObjects;
using FluentAssertions;

namespace EduFocus.Domain.Tests.Protection;

public class BuiltInRuleCatalogTests
{
    private static readonly DateTime Now = new(2026, 10, 29, 7, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void Default_catalog_produces_enforceable_rules()
    {
        var catalog = BuiltInRuleCatalog.Default;

        catalog.Version.Should().Be(BuiltInRuleCatalog.CurrentVersion);
        catalog.Rules.Should().HaveCount(21);

        foreach (var definition in catalog.Rules)
        {
            var rule = CreateRule(definition);

            rule.NormalizedValue.Should().Be(definition.Value);
            rule.EnforcementStatus.Should().Be(EnforcementStatus.Enforced);
            rule.IsEffective(Now).Should().BeTrue();
        }
    }

    [Fact]
    public void Default_catalog_contains_documented_chatbots()
    {
        var blocked = BuiltInRuleCatalog.Default.Rules
            .Where(rule => rule.Effect == BlockRuleEffect.Block)
            .Select(rule => rule.Value)
            .ToList();

        blocked.Should().BeEquivalentTo(
        [
            "chatgpt.com",
            "chat.openai.com",
            "claude.ai",
            "gemini.google.com",
            "copilot.microsoft.com",
            "perplexity.ai",
            "poe.com",
            "character.ai",
            "deepseek.com",
            "grok.com",
            "meta.ai",
            "you.com",
            "pi.ai",
            "huggingface.co"
        ]);
        blocked.Should().OnlyContain(value => !ProtectedDomains.IsProtected(DomainName.Parse(value)));
    }

    [Fact]
    public void Default_catalog_allowlist_protects_essential_study_services()
    {
        var allowed = BuiltInRuleCatalog.Default.Rules
            .Where(rule => rule.Effect == BlockRuleEffect.Allow)
            .Select(rule => rule.Value);

        allowed.Should().BeEquivalentTo(
        [
            "meet.google.com",
            "teams.microsoft.com",
            "classroom.google.com",
            "accounts.google.com",
            "login.microsoftonline.com",
            "login.live.com",
            "zoom.us"
        ]);

        var set = BlockRuleSet.Create(
            BuiltInRuleCatalog.Default.Rules.Select(CreateRule).ToList());

        set.EvaluateDomain(DomainName.Parse("meet.google.com"), Now).Should().Be(RuleDecision.Allow);
        set.EvaluateDomain(DomainName.Parse("teams.microsoft.com"), Now).Should().Be(RuleDecision.Allow);
        set.EvaluateDomain(DomainName.Parse("zoom.us"), Now).Should().Be(RuleDecision.Allow);
        set.EvaluateDomain(DomainName.Parse("chatgpt.com"), Now).Should().Be(RuleDecision.Block);
        set.EvaluateDomain(DomainName.Parse("chat.openai.com"), Now).Should().Be(RuleDecision.Block);
    }

    [Fact]
    public void Catalog_rejects_duplicate_rules()
    {
        var definition = new BuiltInRuleDefinition(
            RuleType.Domain, "chatgpt.com", BlockRuleCategory.Chatbot, BlockRuleEffect.Block);

        var act = () => new BuiltInRuleCatalog(1, [definition, definition]);

        act.Should().Throw<ArgumentException>().WithParameterName("rules");
    }

    [Fact]
    public void Catalog_rejects_a_non_positive_version()
    {
        var act = () => new BuiltInRuleCatalog(0, []);

        act.Should().Throw<ArgumentOutOfRangeException>().WithParameterName("version");
    }

    private static BlockRule CreateRule(BuiltInRuleDefinition definition) =>
        BlockRule.Create(
            definition.RuleType,
            RuleMatchType.Exact,
            definition.Value,
            definition.Category,
            definition.Effect,
            Now,
            isBuiltIn: true);
}
