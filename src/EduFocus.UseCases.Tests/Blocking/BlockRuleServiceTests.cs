using EduFocus.Domain.Enums;
using EduFocus.Domain.Protection;
using EduFocus.Ports.Security;
using EduFocus.UseCases.Tests.TestSupport;
using FluentAssertions;

namespace EduFocus.UseCases.Tests.Blocking;

public sealed class BlockRuleServiceTests
{
    [Fact]
    public async Task List_requires_parent_mode()
    {
        var harness = new BlockingHarness();

        var act = () => harness.Service.ListAsync();

        await act.Should().ThrowAsync<ParentAuthorizationException>();
    }

    [Fact]
    public async Task List_returns_rules_grouped_by_category_order()
    {
        var harness = new BlockingHarness();
        await harness.Seed.EnsureSeededAsync();
        harness.Authenticate();

        var rules = await harness.Service.ListAsync();

        rules.Should().HaveCount(BuiltInRuleCatalog.Default.Rules.Count);
        rules.Should().BeInAscendingOrder(rule => rule.Category);
        rules[0].Category.Should().Be(BlockRuleCategory.Chatbot);
        rules[^1].Category.Should().Be(BlockRuleCategory.Study);
        rules.Should().OnlyContain(rule => rule.IsBuiltIn);
        rules.Should().Contain(rule => rule.Value == "meet.google.com" && rule.Effect == BlockRuleEffect.Allow);
    }

    [Fact]
    public async Task Toggle_requires_parent_mode()
    {
        var harness = new BlockingHarness();
        await harness.Seed.EnsureSeededAsync();
        var rule = harness.Rules.Rules[0];

        var act = () => harness.Service.SetEnabledAsync(rule.Id.Value, false);

        await act.Should().ThrowAsync<ParentAuthorizationException>();
        rule.IsEnabled.Should().BeTrue();
    }

    [Fact]
    public async Task Toggle_disables_a_built_in_rule_and_audits_it()
    {
        var harness = new BlockingHarness();
        await harness.Seed.EnsureSeededAsync();
        harness.Authenticate();
        var chatgpt = harness.Rules.Rules.Single(rule => rule.NormalizedValue == "chatgpt.com");

        var result = await harness.Service.SetEnabledAsync(chatgpt.Id.Value, false);

        result.Changed.Should().BeTrue();
        result.Rule.IsEnabled.Should().BeFalse();
        chatgpt.IsEnabled.Should().BeFalse();
        harness.Rules.UpdateCount.Should().Be(1);

        harness.Audits.Entries.Should().HaveCount(2);
        var audit = harness.Audits.Entries[^1];
        audit.Actor.Should().Be(AuditActor.Parent);
        audit.EventType.Should().Be(AuditEventType.BlockRuleChanged);
        audit.Description.Should().Contain("disabled built-in domain rule chatgpt.com");
    }

    [Fact]
    public async Task Toggle_to_the_same_state_is_a_no_op()
    {
        var harness = new BlockingHarness();
        await harness.Seed.EnsureSeededAsync();
        harness.Authenticate();
        var chatgpt = harness.Rules.Rules.Single(rule => rule.NormalizedValue == "chatgpt.com");

        var result = await harness.Service.SetEnabledAsync(chatgpt.Id.Value, true);

        result.Changed.Should().BeFalse();
        result.Rule.IsEnabled.Should().BeTrue();
        harness.Rules.UpdateCount.Should().Be(0);
        harness.Audits.Entries.Should().ContainSingle();
    }

    [Fact]
    public async Task Toggle_refreshes_browser_rules_when_bridge_is_running()
    {
        var harness = new BlockingHarness();
        await harness.Seed.EnsureSeededAsync();
        harness.Authenticate();
        var chatgpt = harness.Rules.Rules.Single(rule => rule.NormalizedValue == "chatgpt.com");

        await harness.Service.SetEnabledAsync(chatgpt.Id.Value, false);

        harness.BrowserBridge.UpdateCalls.Should().Be(1);
        harness.BrowserBridge.LastRules.Should().Contain(rule => rule.Id == chatgpt.Id);
        harness.BrowserBridge.LastRules.Single(rule => rule.Id == chatgpt.Id)
            .IsEnabled.Should().BeFalse();
        harness.BrowserBridge.LastNowUtc.Should().Be(BlockingHarness.Start.UtcDateTime);
    }

    [Fact]
    public async Task Toggle_skips_browser_refresh_when_bridge_is_not_running()
    {
        var harness = new BlockingHarness();
        await harness.Seed.EnsureSeededAsync();
        harness.Authenticate();
        harness.BrowserBridge.IsRunning = false;
        var chatgpt = harness.Rules.Rules.Single(rule => rule.NormalizedValue == "chatgpt.com");

        await harness.Service.SetEnabledAsync(chatgpt.Id.Value, false);

        harness.BrowserBridge.UpdateCalls.Should().Be(0);
    }

    [Fact]
    public async Task Toggle_unknown_rule_throws()
    {
        var harness = new BlockingHarness();
        harness.Authenticate();

        var act = () => harness.Service.SetEnabledAsync(Guid.NewGuid(), false);

        await act.Should().ThrowAsync<InvalidOperationException>();
    }
}
