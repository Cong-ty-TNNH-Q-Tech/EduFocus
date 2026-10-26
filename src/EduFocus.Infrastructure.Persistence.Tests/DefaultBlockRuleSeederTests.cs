using EduFocus.Domain.Entities;
using EduFocus.Domain.Enums;
using EduFocus.Infrastructure.Persistence;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace EduFocus.Infrastructure.Persistence.Tests;

public sealed class DefaultBlockRuleSeederTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 26, 10, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Seed_is_idempotent_and_preserves_parent_customization_and_disabled_builtin_rules()
    {
        await using var fixture = await Fixture.Create();
        var seeder = new DefaultBlockRuleSeeder(fixture.Db);
        await seeder.SeedAsync(Now, throughVersion: 1);
        var chatgpt = await fixture.Db.BlockRules.SingleAsync(x => x.NormalizedValue == "chatgpt.com");
        Assert.True(chatgpt.IsBuiltIn);
        chatgpt.SetEnabled(false, Now.AddMinutes(1));
        var custom = BlockRule.Create(BlockRuleType.Domain, BlockMatchType.Exact, "family.example", BlockRuleCategory.Custom,
            BlockRuleEffect.Block, Now, enabled: false, note: "parent setting");
        fixture.Db.BlockRules.Add(custom);
        await fixture.Db.SaveChangesAsync();

        await seeder.SeedAsync(Now.AddMinutes(2), throughVersion: 1);
        Assert.Equal(13, await fixture.Db.BlockRules.CountAsync());
        Assert.False((await fixture.Db.BlockRules.SingleAsync(x => x.NormalizedValue == "chatgpt.com")).IsEnabled);
        var persistedCustom = await fixture.Db.BlockRules.SingleAsync(x => x.Id == custom.Id);
        Assert.False(persistedCustom.IsBuiltIn);
        Assert.False(persistedCustom.IsEnabled);
        Assert.Equal("parent setting", persistedCustom.Note);
        Assert.Single(await fixture.Db.AuditLogs.Where(x => x.EventType == "DefaultBlockRulesSeeded").ToArrayAsync());
    }

    [Fact]
    public async Task Seed_version_upgrade_adds_new_defaults_without_disabling_learning_allowlist()
    {
        await using var fixture = await Fixture.Create();
        var seeder = new DefaultBlockRuleSeeder(fixture.Db);
        await seeder.SeedAsync(Now, throughVersion: 1);
        var meetAllow = await fixture.Db.BlockRules.SingleAsync(x => x.NormalizedValue == "meet.google.com" && x.Effect == BlockRuleEffect.Allow);
        fixture.Db.BlockRules.Add(BlockRule.Create(BlockRuleType.Domain, BlockMatchType.Exact, "meet.google.com",
            BlockRuleCategory.Custom, BlockRuleEffect.Block, Now));
        await fixture.Db.SaveChangesAsync();

        await seeder.SeedAsync(Now.AddMinutes(2));
        Assert.Equal(20, await fixture.Db.BlockRules.CountAsync());
        Assert.True((await fixture.Db.BlockRules.SingleAsync(x => x.Id == meetAllow.Id)).IsEnabled);
        Assert.Equal(2, await fixture.Db.AuditLogs.CountAsync(x => x.EventType == "DefaultBlockRulesSeeded"));
        Assert.Equal(BlockDecision.Allow, BlockRule.Evaluate(await fixture.Db.BlockRules.ToArrayAsync(), BlockRuleType.Domain, "meet.google.com", Now.AddMinutes(2)));
    }

    [Fact]
    public async Task Built_in_rule_toggle_requires_parent_and_records_audit()
    {
        await using var fixture = await Fixture.Create();
        await new DefaultBlockRuleSeeder(fixture.Db).SeedAsync(Now);
        var chatgpt = await fixture.Db.BlockRules.SingleAsync(x => x.NormalizedValue == "chatgpt.com");
        var service = new BlockRuleService(fixture.Db);
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => service.SetEnabledAsync(chatgpt.Id, false, new ParentModeSession(), Now));
        var parent = new ParentModeSession();
        parent.Unlock(Now);
        await service.SetEnabledAsync(chatgpt.Id, false, parent, Now.AddMinutes(1));
        Assert.False((await fixture.Db.BlockRules.SingleAsync(x => x.Id == chatgpt.Id)).IsEnabled);
        Assert.Single(await fixture.Db.AuditLogs.Where(x => x.EventType == "BuiltInRuleDisabled").ToArrayAsync());
        var meet = await fixture.Db.BlockRules.SingleAsync(x => x.NormalizedValue == "meet.google.com" && x.Effect == BlockRuleEffect.Allow);
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.SetEnabledAsync(meet.Id, false, parent, Now.AddMinutes(2)));
    }

    private sealed class Fixture(SqliteConnection connection, AppDbContext db) : IAsyncDisposable
    {
        public AppDbContext Db { get; } = db;
        public static async Task<Fixture> Create()
        {
            var connection = new SqliteConnection("Data Source=:memory:");
            await connection.OpenAsync();
            var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseSqlite(connection).Options);
            await db.Database.MigrateAsync();
            db.AppSettings.Add(AppSettings.Create(Now));
            await db.SaveChangesAsync();
            return new(connection, db);
        }
        public async ValueTask DisposeAsync() { await Db.DisposeAsync(); await connection.DisposeAsync(); }
    }
}
