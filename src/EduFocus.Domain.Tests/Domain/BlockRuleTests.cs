using EduFocus.Domain.Entities;
using EduFocus.Domain.Enums;
using EduFocus.Domain.Protection;
using EduFocus.Domain.ValueObjects;
using FluentAssertions;

namespace EduFocus.Domain.Tests.Domain;

public class BlockRuleTests
{
    private static readonly DateTime Now = new(2026, 10, 29, 7, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void CreateDomain_NormalizesInput()
    {
        var rule = Create(RuleType.Domain, " https://ChatGPT.com/path?x=1 ");

        rule.Value.Should().Be("https://ChatGPT.com/path?x=1");
        rule.NormalizedValue.Should().Be("chatgpt.com");
        rule.EnforcementStatus.Should().Be(EnforcementStatus.Enforced);
        rule.IsEffective(Now).Should().BeTrue();
        rule.Matches(DomainName.Parse("chatgpt.com")).Should().BeTrue();
    }

    [Theory]
    [InlineData("google.com")]
    [InlineData("microsoft.com")]
    [InlineData("meet.google.com")]
    [InlineData("sub.meet.google.com")]
    [InlineData("teams.microsoft.com")]
    [InlineData("classroom.google.com")]
    [InlineData("accounts.google.com")]
    [InlineData("login.microsoftonline.com")]
    [InlineData("login.live.com")]
    [InlineData("zoom.us")]
    [InlineData("us02web.zoom.us")]
    public void CreateDomain_RejectsProtectedBlockTargets(string domain)
    {
        var act = () => Create(RuleType.Domain, domain, effect: BlockRuleEffect.Block);

        act.Should().Throw<ArgumentException>().WithParameterName("value");
    }

    [Theory]
    [InlineData("gemini.google.com")]
    [InlineData("copilot.microsoft.com")]
    [InlineData("mail.google.com")]
    public void CreateDomain_AllowsBlockingSpecificServicesOnSharedApexDomains(string domain)
    {
        var rule = Create(RuleType.Domain, domain, effect: BlockRuleEffect.Block);

        rule.Effect.Should().Be(BlockRuleEffect.Block);
        rule.IsEffective(Now).Should().BeTrue();
    }

    [Fact]
    public void CreateDomain_AllowsProtectedTargetsInAllowRules()
    {
        var rule = Create(RuleType.Domain, "meet.google.com", effect: BlockRuleEffect.Allow);

        rule.Effect.Should().Be(BlockRuleEffect.Allow);
        rule.IsEffective(Now).Should().BeTrue();
    }

    [Fact]
    public void AppRule_NormalizesPathAndExtension()
    {
        var rule = Create(RuleType.App, @"C:\Program Files\Game\Game.EXE", category: BlockRuleCategory.Game);

        rule.NormalizedValue.Should().Be("game");
        rule.MatchesProcess("game").Should().BeTrue();
        rule.MatchesProcess("GAME.exe").Should().BeTrue();
        rule.MatchesProcess("game-launcher").Should().BeFalse();
        rule.MatchesProcess(null).Should().BeFalse();
    }

    [Fact]
    public void SuffixRule_MatchesDomainBoundaryOnly()
    {
        var rule = Create(RuleType.Domain, "example.com", matchType: RuleMatchType.Suffix);

        rule.EnforcementStatus.Should().Be(EnforcementStatus.NotEnforced);
        rule.IsEffective(Now).Should().BeFalse();
        rule.Matches(DomainName.Parse("example.com")).Should().BeTrue();
        rule.Matches(DomainName.Parse("sub.example.com")).Should().BeTrue();
        rule.Matches(DomainName.Parse("notexample.com")).Should().BeFalse();
        rule.Matches(DomainName.Parse("example.com.evil.com")).Should().BeFalse();
    }

    [Fact]
    public void ContainsRule_MatchesWholeDomainSegments()
    {
        var rule = Create(RuleType.Domain, "example.com", matchType: RuleMatchType.Contains);

        rule.Matches(DomainName.Parse("example.com")).Should().BeTrue();
        rule.Matches(DomainName.Parse("sub.example.com")).Should().BeTrue();
        rule.Matches(DomainName.Parse("notexample.com")).Should().BeFalse();
    }

    [Fact]
    public void UrlRule_IsSanitizedAndNotEnforcedInMvp()
    {
        var rule = Create(RuleType.Url, "https://chat.openai.com/chat?token=abc#x");

        rule.NormalizedValue.Should().Be("https://chat.openai.com/chat");
        rule.EnforcementStatus.Should().Be(EnforcementStatus.NotEnforced);
    }

    [Fact]
    public void EffectiveRule_RespectsEnabledAndExpiry()
    {
        var expiry = Now.AddMinutes(10);
        var rule = Create(RuleType.Domain, "chatgpt.com", expiresAtUtc: expiry);

        rule.IsEffective(Now).Should().BeTrue();
        rule.IsEffective(expiry).Should().BeFalse();

        rule.SetEnabled(false, Now.AddMinutes(1));
        rule.IsEffective(Now.AddMinutes(1)).Should().BeFalse();
    }

    [Fact]
    public void Create_RejectsExpiryInThePast()
    {
        var act = () => Create(RuleType.Domain, "chatgpt.com", expiresAtUtc: Now);

        act.Should().Throw<ArgumentException>().WithParameterName("expiresAtUtc");
    }

    [Fact]
    public void Evaluator_AllowWinsOverBlock()
    {
        var block = Create(RuleType.Domain, "chatgpt.com", effect: BlockRuleEffect.Block);
        var allow = Create(RuleType.Domain, "chatgpt.com", effect: BlockRuleEffect.Allow);

        var decision = BlockRuleEvaluator.EvaluateDomain(
            [block, allow],
            DomainName.Parse("chatgpt.com"),
            Now);

        decision.Should().Be(RuleDecision.Allow);
    }

    [Fact]
    public void Evaluator_ReturnsBlockForEffectiveBlockRule()
    {
        var block = Create(RuleType.Domain, "chatgpt.com");

        var decision = BlockRuleEvaluator.EvaluateDomain(
            [block],
            DomainName.Parse("chatgpt.com"),
            Now);

        decision.Should().Be(RuleDecision.Block);
    }

    [Fact]
    public void Evaluator_ReturnsNoneForUnrelatedDomain()
    {
        var block = Create(RuleType.Domain, "chatgpt.com");

        var decision = BlockRuleEvaluator.EvaluateDomain(
            [block],
            DomainName.Parse("wikipedia.org"),
            Now);

        decision.Should().Be(RuleDecision.None);
    }

    [Fact]
    public void Evaluator_IgnoresDisabledExpiredAndNotEnforcedRules()
    {
        var disabled = Create(RuleType.Domain, "chatgpt.com");
        disabled.SetEnabled(false, Now.AddMinutes(1));

        var expired = Create(RuleType.Domain, "claude.ai", expiresAtUtc: Now.AddMinutes(5));

        var notEnforced = Create(RuleType.Domain, "gemini.google.com", matchType: RuleMatchType.Suffix);

        var decision = BlockRuleEvaluator.EvaluateDomain(
            [disabled, expired, notEnforced],
            DomainName.Parse("chatgpt.com"),
            Now.AddMinutes(6));

        decision.Should().Be(RuleDecision.None);

        BlockRuleEvaluator.EvaluateDomain([expired], DomainName.Parse("claude.ai"), Now.AddMinutes(6))
            .Should().Be(RuleDecision.None);

        BlockRuleEvaluator.EvaluateDomain([notEnforced], DomainName.Parse("gemini.google.com"), Now)
            .Should().Be(RuleDecision.None);
    }

    [Fact]
    public void Evaluator_AppRulesUseNormalizedProcessNames()
    {
        var block = Create(RuleType.App, "Game.exe", category: BlockRuleCategory.Game);
        var allow = Create(RuleType.App, "game", effect: BlockRuleEffect.Allow, category: BlockRuleCategory.Custom);

        BlockRuleEvaluator.EvaluateApp([block], @"D:\Game\game.exe", Now).Should().Be(RuleDecision.Block);
        BlockRuleEvaluator.EvaluateApp([block, allow], "game", Now).Should().Be(RuleDecision.Allow);
    }

    [Fact]
    public void Create_RejectsInvalidValues()
    {
        var notDomain = () => Create(RuleType.Domain, "not a domain");
        notDomain.Should().Throw<ArgumentException>();

        var notApp = () => Create(RuleType.App, "   ");
        notApp.Should().Throw<ArgumentException>();

        var notUrl = () => Create(RuleType.Url, "file:///c:/x");
        notUrl.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void SetExpiry_RejectsPastValues()
    {
        var rule = Create(RuleType.Domain, "chatgpt.com");

        var act = () => rule.SetExpiry(Now, Now);

        act.Should().Throw<ArgumentException>().WithParameterName("expiresAtUtc");
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
