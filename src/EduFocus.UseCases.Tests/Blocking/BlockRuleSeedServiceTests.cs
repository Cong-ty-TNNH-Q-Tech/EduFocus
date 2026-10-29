using EduFocus.Domain.Entities;
using EduFocus.Domain.Enums;
using EduFocus.Domain.Protection;
using EduFocus.UseCases.Tests.TestSupport;
using FluentAssertions;

namespace EduFocus.UseCases.Tests.Blocking;

public sealed class BlockRuleSeedServiceTests
{
    [Fact]
    public async Task Seed_installs_every_default_rule_into_an_empty_database()
    {
        var harness = new BlockingHarness();

        var result = await harness.Seed.EnsureSeededAsync();

        result.SeedVersion.Should().Be(BuiltInRuleCatalog.CurrentVersion);
        result.Added.Should().Be(BuiltInRuleCatalog.Default.Rules.Count);
        result.Updated.Should().Be(0);
        result.Changed.Should().BeTrue();

        harness.Rules.Rules.Should().HaveCount(result.Added);
        harness.Rules.Rules.Should().OnlyContain(rule => rule.IsBuiltIn && rule.IsEnabled);

        var audit = harness.Audits.Entries.Should().ContainSingle().Subject;
        audit.Actor.Should().Be(AuditActor.System);
        audit.EventType.Should().Be(AuditEventType.BlockRuleChanged);
        audit.Description.Should().Contain($"version {BuiltInRuleCatalog.CurrentVersion}");
        audit.MetadataJson.Should().Contain($"\"seedVersion\":{BuiltInRuleCatalog.CurrentVersion}");
    }

    [Fact]
    public async Task Seed_is_idempotent_and_does_not_duplicate_rules()
    {
        var harness = new BlockingHarness();

        await harness.Seed.EnsureSeededAsync();
        var second = await harness.Seed.EnsureSeededAsync();

        second.Added.Should().Be(0);
        second.Updated.Should().Be(0);
        second.Changed.Should().BeFalse();
        harness.Rules.Rules.Should().HaveCount(BuiltInRuleCatalog.Default.Rules.Count);
        harness.Rules.UpdateCount.Should().Be(0);
        harness.Audits.Entries.Should().ContainSingle();
    }

    [Fact]
    public async Task Seed_preserves_a_disabled_built_in_rule()
    {
        var harness = new BlockingHarness();
        await harness.Seed.EnsureSeededAsync();

        var chatgpt = harness.Rules.Rules.Single(rule => rule.NormalizedValue == "chatgpt.com");
        chatgpt.SetEnabled(false, harness.Clock.GetUtcNow().UtcDateTime);

        var second = await harness.Seed.EnsureSeededAsync();

        second.Changed.Should().BeFalse();
        chatgpt.IsEnabled.Should().BeFalse();
        harness.Audits.Entries.Should().ContainSingle();
    }

    [Fact]
    public async Task Seed_does_not_overwrite_a_parent_rule_with_the_same_identity()
    {
        var harness = new BlockingHarness(
            new BuiltInRuleCatalog(
                1,
                [Definition("chatgpt.com", BlockRuleCategory.Chatbot)]));

        var userRule = BlockRule.Create(
            RuleType.Domain,
            RuleMatchType.Exact,
            "chatgpt.com",
            BlockRuleCategory.Custom,
            BlockRuleEffect.Block,
            harness.Clock.GetUtcNow().UtcDateTime,
            isBuiltIn: false);

        harness.Rules.Rules.Add(userRule);

        var result = await harness.Seed.EnsureSeededAsync();

        result.Added.Should().Be(0);
        result.Changed.Should().BeFalse();
        harness.Rules.Rules.Should().ContainSingle().Which.Should().BeSameAs(userRule);
        userRule.IsBuiltIn.Should().BeFalse();
        userRule.Category.Should().Be(BlockRuleCategory.Custom);
        harness.Audits.Entries.Should().BeEmpty();
    }

    [Fact]
    public async Task Seed_version_bump_adds_new_rules_and_refreshes_metadata()
    {
        var v1 = new BuiltInRuleCatalog(
            1,
            [Definition("chatgpt.com", BlockRuleCategory.Chatbot)]);
        var v2 = new BuiltInRuleCatalog(
            2,
            [
                Definition("chatgpt.com", BlockRuleCategory.Social),
                Definition("claude.ai", BlockRuleCategory.Chatbot)
            ]);

        var harness = new BlockingHarness(v1);
        (await harness.Seed.EnsureSeededAsync()).Added.Should().Be(1);

        var result = await harness.CreateSeedService(v2).EnsureSeededAsync();

        result.SeedVersion.Should().Be(2);
        result.Added.Should().Be(1);
        result.Updated.Should().Be(1);
        result.Changed.Should().BeTrue();

        harness.Rules.Rules.Should().HaveCount(2);

        var chatgpt = harness.Rules.Rules.Single(rule => rule.NormalizedValue == "chatgpt.com");
        chatgpt.Category.Should().Be(BlockRuleCategory.Social);
        chatgpt.IsEnabled.Should().BeTrue();

        harness.Audits.Entries.Should().HaveCount(2);
        harness.Audits.Entries[^1].MetadataJson.Should().Contain("\"seedVersion\":2");
    }

    private static BuiltInRuleDefinition Definition(string value, BlockRuleCategory category) =>
        new(RuleType.Domain, value, category, BlockRuleEffect.Block);
}
