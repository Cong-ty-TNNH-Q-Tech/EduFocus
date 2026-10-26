using EduFocus.Domain.Attention;
using EduFocus.Domain.Entities;
using EduFocus.Domain.Enums;
using EduFocus.Domain.ValueObjects;

namespace EduFocus.Domain.Tests.Entities;

public sealed class DomainEntityTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 26, 10, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Settings_record_protection_and_clean_shutdown()
    {
        var settings = AppSettings.Create(Now);
        settings.SetProtection(ProtectionState.On, Now.AddMinutes(1));
        Assert.Equal(ProtectionState.On, settings.Protection);
        settings.RecordCleanShutdown(Now.AddMinutes(2));
        Assert.Equal(ProtectionState.Off, settings.Protection);
        Assert.Equal(Now.AddMinutes(2), settings.LastCleanShutdownAtUtc);
    }

    [Fact]
    public void Credentials_store_only_hashes_and_lock_after_five_failures()
    {
        var credential = ParentCredentials.Create(Hash(1), Hash(2), Now);
        Assert.Throws<ArgumentOutOfRangeException>(() => CredentialHash.FromPbkdf2(Convert.ToBase64String(new byte[32]), Convert.ToBase64String(new byte[16]), 599_999));
        for (var i = 0; i < 5; i++) credential.RecordFailedAttempt(Now.AddMinutes(i));
        Assert.True(credential.IsLockedOut(Now.AddMinutes(5)));
        Assert.Equal(Now.AddMinutes(19), credential.LockoutUntilUtc);
        Assert.Throws<InvalidOperationException>(() => credential.RecordFailedAttempt(Now.AddMinutes(5)));
        credential.RecordSuccessfulAuthentication(Now.AddMinutes(20));
        Assert.False(credential.IsLockedOut(Now.AddMinutes(20)));
        Assert.Equal(0, credential.FailedAttempts);
    }

    [Fact]
    public void Parent_mode_expires_after_five_idle_minutes_and_manual_lock_clears_session()
    {
        var session = new ParentModeSession();
        Assert.False(session.IsActive(Now));
        session.Unlock(Now);
        Assert.True(session.IsActive(Now.AddMinutes(4)));
        Assert.True(session.RecordActivity(Now.AddMinutes(4)));
        Assert.Equal(Now.AddMinutes(9), session.ExpiresAtUtc);
        Assert.True(session.IsActive(Now.AddMinutes(8).AddSeconds(59)));
        Assert.False(session.IsActive(Now.AddMinutes(9)));
        session.Unlock(Now.AddMinutes(10));
        session.Lock();
        Assert.False(session.IsActive(Now.AddMinutes(10)));
    }

    [Fact]
    public void Block_rule_normalizes_and_allow_wins_while_expired_rules_are_ignored()
    {
        var block = BlockRule.Create(BlockRuleType.Domain, BlockMatchType.Exact, "HTTPS://ChatGPT.com/path", BlockRuleCategory.Chatbot, BlockRuleEffect.Block, Now);
        var allow = BlockRule.Create(BlockRuleType.Domain, BlockMatchType.Exact, "chatgpt.com", BlockRuleCategory.Study, BlockRuleEffect.Allow, Now);
        Assert.Equal("chatgpt.com", block.NormalizedValue);
        Assert.Equal(BlockDecision.Allow, BlockRule.Evaluate(new[] { block, allow }, BlockRuleType.Domain, "chatgpt.com/anything", Now));
        allow.SetExpiry(Now, Now);
        Assert.Equal(BlockDecision.Block, BlockRule.Evaluate(new[] { block, allow }, BlockRuleType.Domain, "chatgpt.com", Now.AddSeconds(1)));
        var suffix = BlockRule.Create(BlockRuleType.Domain, BlockMatchType.Suffix, "example.com", BlockRuleCategory.Custom, BlockRuleEffect.Block, Now);
        Assert.Equal(RuleEnforcementStatus.NotEnforced, suffix.EnforcementStatus);
        Assert.Equal(BlockDecision.None, BlockRule.Evaluate(new[] { suffix }, BlockRuleType.Domain, "sub.example.com", Now));
    }

    [Fact]
    public void Schedule_rejects_invalid_ranges_and_overlaps_only_on_shared_days()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => ScheduleWindow.Create(1, 900, 800, Now));
        var monday = ScheduleWindow.Create(1, 480, 600, Now);
        var tuesday = ScheduleWindow.Create(2, 500, 550, Now);
        ScheduleWindow.EnsureNoOverlap(new[] { monday, tuesday });
        var overlap = ScheduleWindow.Create(1, 599, 700, Now);
        Assert.Throws<InvalidOperationException>(() => ScheduleWindow.EnsureNoOverlap(new[] { monday, overlap }));
    }

    [Fact]
    public void Schedule_uses_monday_first_local_days_and_half_open_minute_ranges_including_midnight_end()
    {
        var monday = ScheduleWindow.Create(1, 1380, 1440, Now);
        var sunday = ScheduleWindow.Create(64, 0, 60, Now);
        Assert.True(monday.AppliesAt(new DateTime(2026, 10, 26, 23, 59, 0)));
        Assert.False(monday.AppliesAt(new DateTime(2026, 10, 27, 0, 0, 0)));
        Assert.True(sunday.AppliesAt(new DateTime(2026, 11, 1, 0, 59, 0)));
        Assert.False(sunday.AppliesAt(new DateTime(2026, 11, 1, 1, 0, 0)));
        Assert.False(ScheduleWindow.Create(1, 60, 120, Now).AppliesAt(new DateTime(2026, 10, 26, 0, 59, 0)));
    }

    [Fact]
    public void Session_enforces_single_active_lifecycle_and_computes_percentages()
    {
        var active = StudySession.Start(null, StudySessionTrigger.Manual, true, new DateOnly(2026, 10, 26), Now);
        Assert.Throws<InvalidOperationException>(() => StudySession.Start(new[] { active }, StudySessionTrigger.Schedule, false, new DateOnly(2026, 10, 26), Now));
        active.AddAttention(AttentionState.Focused, 60, .8, Now.AddMinutes(1));
        active.AddAttention(AttentionState.Distracted, 40, .6, Now.AddMinutes(2));
        active.AddMonitoringEligibleSeconds(200, Now.AddMinutes(3));
        Assert.Equal(60, active.AttentionPercent);
        Assert.Equal(50, active.CoveragePercent);
        Assert.Equal(.72, active.ConfidenceAvg);
        active.Complete(Now.AddMinutes(4));
        Assert.Equal(StudySessionStatus.Completed, active.Status);
        Assert.Equal(active.EndedAtUtc, active.LastHeartbeatAtUtc);
        Assert.Throws<InvalidOperationException>(() => active.Heartbeat(Now.AddMinutes(5)));
    }

    [Fact]
    public void Interrupted_session_ends_at_last_heartbeat_and_metrics_reject_negative_values()
    {
        var active = StudySession.Start(null, StudySessionTrigger.Manual, true, new DateOnly(2026, 10, 26), Now);
        active.Heartbeat(Now.AddMinutes(2));
        active.InterruptAtLastHeartbeat();
        Assert.Equal(Now.AddMinutes(2), active.EndedAtUtc);
        Assert.Equal(StudySessionStatus.Interrupted, active.Status);
        var another = StudySession.Start(null, StudySessionTrigger.Recovery, false, new DateOnly(2026, 10, 26), Now.AddMinutes(3));
        Assert.Throws<ArgumentOutOfRangeException>(() => another.AddActiveSeconds(-1, Now.AddMinutes(4)));
    }

    [Fact]
    public void Activity_urls_strip_query_and_fragment_and_audit_rejects_secret_metadata()
    {
        var sample = ActivitySample.Create(Guid.NewGuid(), Now, 1, Now, url: "https://example.com/path?token=private#part");
        Assert.Equal("https://example.com/path", sample.Url);
        Assert.Throws<ArgumentException>(() => AuditLog.Create(Now, AuditActor.Parent, "ParentLogin", "Signed in", Now, "{\"pin\":\"123456\"}"));
    }

    private static CredentialHash Hash(byte seed) => CredentialHash.FromPbkdf2(Convert.ToBase64String(Enumerable.Repeat(seed, 32).ToArray()), Convert.ToBase64String(Enumerable.Repeat((byte)(seed + 10), 16).ToArray()), 600_000);
}
