using EduFocus.Domain.Entities;
using EduFocus.Domain.Enums;
using EduFocus.Domain.ValueObjects;
using EduFocus.SqliteDataAccess.Repositories;
using EduFocus.SqliteDataAccess.Tests.TestSupport;
using FluentAssertions;

namespace EduFocus.SqliteDataAccess.Tests;

public sealed class BlockRuleRepositoryTests
{
    private static readonly DateTime Now = new(2026, 10, 29, 8, 0, 0, DateTimeKind.Utc);

    [Fact]
    public async Task Rules_round_trip_with_normalization_and_effect()
    {
        await using var db = new TempDatabase();
        await db.CreateInitializer().InitializeAsync();

        var repository = new SqliteBlockRuleRepository(db.ContextFactory);
        var block = BlockRule.Create(
            RuleType.Domain,
            RuleMatchType.Exact,
            "https://ChatGPT.com/path",
            BlockRuleCategory.Chatbot,
            BlockRuleEffect.Block,
            Now,
            note: "  Không dùng chatbot  ");
        var allow = BlockRule.Create(
            RuleType.Domain,
            RuleMatchType.Exact,
            "meet.google.com",
            BlockRuleCategory.Study,
            BlockRuleEffect.Allow,
            Now.AddMinutes(1));
        var app = BlockRule.Create(
            RuleType.App,
            RuleMatchType.Exact,
            @"C:\Games\Game.EXE",
            BlockRuleCategory.Game,
            BlockRuleEffect.Block,
            Now.AddMinutes(2));

        await repository.AddAsync(block);
        await repository.AddAsync(allow);
        await repository.AddAsync(app);

        var rules = await repository.ListAsync();

        rules.Should().HaveCount(3);

        var storedBlock = rules.Single(x => x.Id == block.Id);
        storedBlock.NormalizedValue.Should().Be("chatgpt.com");
        storedBlock.Note.Should().Be("Không dùng chatbot");
        storedBlock.EnforcementStatus.Should().Be(EnforcementStatus.Enforced);
        storedBlock.CreatedAtUtc.Kind.Should().Be(DateTimeKind.Utc);

        var storedAllow = rules.Single(x => x.Id == allow.Id);
        storedAllow.Effect.Should().Be(BlockRuleEffect.Allow);

        var storedApp = rules.Single(x => x.Id == app.Id);
        storedApp.NormalizedValue.Should().Be("game");
    }

    [Fact]
    public async Task Allow_and_block_coexist_for_the_same_domain()
    {
        await using var db = new TempDatabase();
        await db.CreateInitializer().InitializeAsync();

        var repository = new SqliteBlockRuleRepository(db.ContextFactory);
        var block = BlockRule.Create(
            RuleType.Domain, RuleMatchType.Exact, "chatgpt.com",
            BlockRuleCategory.Chatbot, BlockRuleEffect.Block, Now);
        var allow = BlockRule.Create(
            RuleType.Domain, RuleMatchType.Exact, "chatgpt.com",
            BlockRuleCategory.Custom, BlockRuleEffect.Allow, Now);

        await repository.AddAsync(block);
        await repository.AddAsync(allow);

        var rules = await repository.ListAsync();

        rules.Should().HaveCount(2);
        rules.Select(x => x.Effect).Should().BeEquivalentTo([BlockRuleEffect.Allow, BlockRuleEffect.Block]);
    }

    [Fact]
    public async Task Get_update_and_delete_round_trip()
    {
        await using var db = new TempDatabase();
        await db.CreateInitializer().InitializeAsync();

        var repository = new SqliteBlockRuleRepository(db.ContextFactory);
        var rule = BlockRule.Create(
            RuleType.Domain, RuleMatchType.Exact, "claude.ai",
            BlockRuleCategory.Chatbot, BlockRuleEffect.Block, Now);
        await repository.AddAsync(rule);

        var loaded = await repository.GetAsync(rule.Id);
        loaded.Should().NotBeNull();
        loaded!.NormalizedValue.Should().Be("claude.ai");

        loaded.SetEnabled(false, Now.AddMinutes(1));
        loaded.SetExpiry(Now.AddDays(3), Now.AddMinutes(2));
        loaded.SetNote("Tạm khóa", Now.AddMinutes(3));
        await repository.UpdateAsync(loaded);

        var updated = await repository.GetAsync(rule.Id);
        updated!.IsEnabled.Should().BeFalse();
        updated.ExpiresAtUtc.Should().Be(Now.AddDays(3));
        updated.ExpiresAtUtc!.Value.Kind.Should().Be(DateTimeKind.Utc);
        updated.Note.Should().Be("Tạm khóa");
        updated.UpdatedAtUtc.Should().Be(Now.AddMinutes(3));

        await repository.DeleteAsync(rule.Id);

        (await repository.GetAsync(rule.Id)).Should().BeNull();
        (await repository.ListAsync()).Should().BeEmpty();
    }

    [Fact]
    public async Task Url_rule_round_trips_as_enforced_and_sanitized()
    {
        await using var db = new TempDatabase();
        await db.CreateInitializer().InitializeAsync();

        var repository = new SqliteBlockRuleRepository(db.ContextFactory);
        var rule = BlockRule.Create(
            RuleType.Url, RuleMatchType.Contains, "https://chat.openai.com/chat?token=abc",
            BlockRuleCategory.Chatbot, BlockRuleEffect.Block, Now);

        await repository.AddAsync(rule);

        var loaded = await repository.GetAsync(rule.Id);
        loaded!.RuleType.Should().Be(RuleType.Url);
        loaded.MatchType.Should().Be(RuleMatchType.Contains);
        loaded.NormalizedValue.Should().Be("https://chat.openai.com/chat/");
        loaded.EnforcementStatus.Should().Be(EnforcementStatus.Enforced);
    }

    [Fact]
    public async Task Delete_unknown_rule_is_a_no_op()
    {
        await using var db = new TempDatabase();
        await db.CreateInitializer().InitializeAsync();

        var repository = new SqliteBlockRuleRepository(db.ContextFactory);

        var act = () => repository.DeleteAsync(BlockRuleId.New());

        await act.Should().NotThrowAsync();
    }
}
