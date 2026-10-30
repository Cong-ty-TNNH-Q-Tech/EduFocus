using EduFocus.Domain.Activity;
using EduFocus.Domain.Entities;
using EduFocus.Domain.Enums;
using EduFocus.Domain.Protection;
using EduFocus.Domain.ValueObjects;
using FluentAssertions;

namespace EduFocus.Domain.Tests.Activity;

public class ActivityClassifierTests
{
    private static readonly DateTime Now = new(2026, 10, 30, 8, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void Blocked_domain_is_distraction()
    {
        var rules = BlockRuleSet.Create([Rule(RuleType.Domain, "chatgpt.com")]);

        var category = ActivityClassifier.Classify(
            rules,
            DomainName.Parse("chatgpt.com"),
            "chrome",
            isOnlineClass: false,
            Now);

        category.Should().Be(ActivityCategory.Distraction);
    }

    [Fact]
    public void Allowed_domain_is_study()
    {
        var rules = BlockRuleSet.Create(
        [
            Rule(RuleType.Domain, "school.example", BlockRuleCategory.Study, BlockRuleEffect.Allow)
        ]);

        var category = ActivityClassifier.Classify(
            rules,
            DomainName.Parse("school.example"),
            "chrome",
            isOnlineClass: false,
            Now);

        category.Should().Be(ActivityCategory.Study);
    }

    [Fact]
    public void Allow_wins_over_block_for_the_same_domain()
    {
        var rules = BlockRuleSet.Create(
        [
            Rule(RuleType.Domain, "chatgpt.com"),
            Rule(RuleType.Domain, "chatgpt.com", BlockRuleCategory.Study, BlockRuleEffect.Allow)
        ]);

        var category = ActivityClassifier.Classify(
            rules,
            DomainName.Parse("chatgpt.com"),
            "chrome",
            isOnlineClass: false,
            Now);

        category.Should().Be(ActivityCategory.Study);
    }

    [Fact]
    public void Blocked_app_is_distraction()
    {
        var rules = BlockRuleSet.Create(
        [
            Rule(RuleType.App, @"C:\Games\game.exe", BlockRuleCategory.Game)
        ]);

        var category = ActivityClassifier.Classify(
            rules,
            domain: null,
            "game",
            isOnlineClass: false,
            Now);

        category.Should().Be(ActivityCategory.Distraction);
    }

    [Fact]
    public void Online_class_is_study_even_without_matching_rules()
    {
        var rules = BlockRuleSet.Create([]);

        var category = ActivityClassifier.Classify(
            rules,
            domain: null,
            processName: "ms-teams",
            isOnlineClass: true,
            Now);

        category.Should().Be(ActivityCategory.Study);
    }

    [Fact]
    public void Unmatched_foreground_is_neutral()
    {
        var rules = BlockRuleSet.Create([]);

        var category = ActivityClassifier.Classify(
            rules,
            domain: null,
            processName: "notepad",
            isOnlineClass: false,
            Now);

        category.Should().Be(ActivityCategory.Neutral);
    }

    [Fact]
    public void Missing_foreground_details_are_unknown()
    {
        var rules = BlockRuleSet.Create([]);

        var category = ActivityClassifier.Classify(
            rules,
            domain: null,
            processName: null,
            isOnlineClass: false,
            Now);

        category.Should().Be(ActivityCategory.Unknown);
    }

    private static BlockRule Rule(
        RuleType ruleType,
        string value,
        BlockRuleCategory category = BlockRuleCategory.Chatbot,
        BlockRuleEffect effect = BlockRuleEffect.Block) =>
        BlockRule.Create(
            ruleType,
            RuleMatchType.Exact,
            value,
            category,
            effect,
            Now);
}
