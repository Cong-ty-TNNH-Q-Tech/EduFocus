using EduFocus.Domain.Entities;
using EduFocus.Domain.Enums;
using EduFocus.Domain.Protection;
using EduFocus.Domain.ValueObjects;
using FluentAssertions;

namespace EduFocus.Domain.Tests.Protection;

public class BlockRuleSetTests
{
    private static readonly DateTime Now = new(2026, 10, 29, 7, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void Snapshot_matches_exact_domain_only()
    {
        var set = BlockRuleSet.Create([Create(RuleType.Domain, "chatgpt.com")]);

        set.EvaluateDomain(DomainName.Parse("chatgpt.com"), Now).Should().Be(RuleDecision.Block);
        set.EvaluateDomain(DomainName.Parse("chatgpt.com.evil.test"), Now).Should().Be(RuleDecision.None);
        set.EvaluateDomain(DomainName.Parse("notchatgpt.com"), Now).Should().Be(RuleDecision.None);
        set.EvaluateDomain(DomainName.Parse("sub.chatgpt.com"), Now).Should().Be(RuleDecision.None);
    }

    [Fact]
    public void Allow_wins_over_block_in_snapshot()
    {
        var block = Create(RuleType.Domain, "chatgpt.com");
        var allow = Create(RuleType.Domain, "chatgpt.com", effect: BlockRuleEffect.Allow);

        var set = BlockRuleSet.Create([block, allow]);

        set.EvaluateDomain(DomainName.Parse("chatgpt.com"), Now).Should().Be(RuleDecision.Allow);
    }

    [Fact]
    public void Snapshot_drops_disabled_and_not_enforced_rules()
    {
        var disabled = Create(RuleType.Domain, "chatgpt.com");
        disabled.SetEnabled(false, Now.AddMinutes(1));
        var suffix = Create(RuleType.Domain, "example.com", matchType: RuleMatchType.Suffix);
        var url = Create(RuleType.Url, "https://claude.ai/chat");

        var set = BlockRuleSet.Create([disabled, suffix, url]);

        set.Count.Should().Be(0);
        set.EvaluateDomain(DomainName.Parse("chatgpt.com"), Now).Should().Be(RuleDecision.None);
        set.EvaluateDomain(DomainName.Parse("sub.example.com"), Now).Should().Be(RuleDecision.None);
        set.EvaluateDomain(DomainName.Parse("claude.ai"), Now).Should().Be(RuleDecision.None);
    }

    [Fact]
    public void Snapshot_respects_expiry_at_evaluation_time()
    {
        var expiry = Now.AddMinutes(10);
        var set = BlockRuleSet.Create([Create(RuleType.Domain, "claude.ai", expiresAtUtc: expiry)]);

        set.EvaluateDomain(DomainName.Parse("claude.ai"), Now).Should().Be(RuleDecision.Block);
        set.EvaluateDomain(DomainName.Parse("claude.ai"), expiry).Should().Be(RuleDecision.None);
    }

    [Fact]
    public void Snapshot_treats_protected_domains_as_allow()
    {
        var set = BlockRuleSet.Create([]);

        set.EvaluateDomain(DomainName.Parse("meet.google.com"), Now).Should().Be(RuleDecision.Allow);
        set.EvaluateDomain(DomainName.Parse("sub.classroom.google.com"), Now).Should().Be(RuleDecision.Allow);
        set.EvaluateDomain(DomainName.Parse("gemini.google.com"), Now).Should().Be(RuleDecision.None);
    }

    [Fact]
    public void Snapshot_matches_app_process_names()
    {
        var set = BlockRuleSet.Create([Create(RuleType.App, @"C:\Games\Game.EXE", category: BlockRuleCategory.Game)]);

        set.EvaluateApp(@"D:\Games\GAME.exe", Now).Should().Be(RuleDecision.Block);
        set.EvaluateApp("game-launcher", Now).Should().Be(RuleDecision.None);
        set.EvaluateApp(null, Now).Should().Be(RuleDecision.None);
    }

    [Fact]
    public void Snapshot_built_before_a_rule_is_enabled_does_not_apply_it()
    {
        var rule = Create(RuleType.Domain, "chatgpt.com");
        rule.SetEnabled(false, Now.AddMinutes(1));

        var set = BlockRuleSet.Create([rule]);
        rule.SetEnabled(true, Now.AddMinutes(2));

        set.EvaluateDomain(DomainName.Parse("chatgpt.com"), Now.AddMinutes(2)).Should().Be(RuleDecision.None);
    }

    private static BlockRule Create(
        RuleType ruleType,
        string value,
        BlockRuleEffect effect = BlockRuleEffect.Block,
        RuleMatchType matchType = RuleMatchType.Exact,
        BlockRuleCategory category = BlockRuleCategory.Chatbot,
        DateTime? expiresAtUtc = null) =>
        BlockRule.Create(ruleType, matchType, value, category, effect, Now, expiresAtUtc: expiresAtUtc);
}
