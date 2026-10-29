using EduFocus.Domain.Attention;
using EduFocus.Domain.Entities;
using EduFocus.Domain.Enums;
using EduFocus.Domain.ValueObjects;
using EduFocus.SqliteDataAccess.Tests.TestSupport;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace EduFocus.SqliteDataAccess.Tests;

public sealed class PersistenceTests
{
    private static readonly DateTime Now = new(2026, 10, 29, 8, 30, 15, 123, DateTimeKind.Utc);
    private static readonly DateOnly LocalDate = new(2026, 10, 29);
    private static readonly string Hash = Convert.ToBase64String(Enumerable.Repeat((byte)0x5A, 32).ToArray());
    private static readonly string Salt = Convert.ToBase64String(Enumerable.Repeat((byte)0x2B, 16).ToArray());

    [Fact]
    public async Task All_entities_round_trip_through_sqlite()
    {
        await using var db = new TempDatabase();
        await db.CreateInitializer().InitializeAsync();

        var settings = AppSettings.CreateDefault(Now);
        settings.EnableProtection(Now.AddSeconds(1));
        settings.RecordCleanShutdown(Now.AddSeconds(2));

        var credential = ParentCredential.Create(Hash, Salt, 600_000, Hash, Salt, 600_000, Now);

        var rule = BlockRule.Create(
            RuleType.Domain,
            RuleMatchType.Exact,
            "  ChatGPT.com  ",
            BlockRuleCategory.Chatbot,
            BlockRuleEffect.Block,
            Now,
            isBuiltIn: true,
            note: "  Chatbot  ");

        var window = ScheduleWindow.Create(ScheduleDays.Weekdays, 480, 690, "  Giờ học  ", Now);

        var session = StudySession.Start(Now, LocalDate, SessionTrigger.Manual, monitoringEnabled: true);
        session.Heartbeat(Now.AddSeconds(10));
        session.AddActiveSeconds(100, Now.AddSeconds(11));
        session.AddAttentionSeconds(AttentionState.Focused, 45, Now.AddSeconds(12));
        session.AddAttentionSeconds(AttentionState.Distracted, 15, Now.AddSeconds(13));
        session.AddMonitoringEligibleSeconds(90, Now.AddSeconds(14));
        session.AddCategorySeconds(ActivityCategory.Study, 60, Now.AddSeconds(15));
        session.AddOnlineClassSeconds(20, Now.AddSeconds(16));
        session.AddIdleSeconds(5, Now.AddSeconds(17));
        session.IncrementBlockedAttempts(Now.AddSeconds(18));
        session.SetAttentionConfidence(0.85, Now.AddSeconds(19));
        session.SetNote("  Ghi chú  ", Now.AddSeconds(20));
        session.Complete(Now.AddMinutes(5));

        var sample = ActivitySample.Capture(
            session.Id,
            Now.AddMinutes(1),
            durationSeconds: 15,
            ActivityCategory.Study,
            isForeground: true,
            isOnlineClass: false,
            idleSeconds: 0,
            appName: "  Chrome  ",
            processName: "chrome",
            windowTitle: "  Lesson  ",
            url: "https://classroom.google.com/c/abc?x=1#frag",
            urlSource: UrlSource.UIA);

        var attention = AttentionEvent.Open(
            session.Id,
            AttentionState.Focused,
            Now.AddMinutes(2),
            confidence: 0.91,
            appName: "  Chrome  ",
            domain: DomainName.Parse("classroom.google.com"),
            note: "  focused  ");
        attention.Close(Now.AddMinutes(3));

        var blocked = BlockedAttempt.Record(
            Now.AddMinutes(4),
            BlockedAttemptTargetType.Domain,
            "ChatGPT.com",
            BlockedAttemptObservedVia.BlockPage,
            sessionId: session.Id,
            windowTitle: "  ChatGPT  ");

        var correlationId = Guid.NewGuid();
        var audit = AuditLog.Record(
            AuditActor.Parent,
            AuditEventType.ProtectionOn,
            "  Protection enabled  ",
            Now.AddMinutes(5),
            metadataJson: "{\"source\":\"test\"}",
            correlationId: correlationId);

        await using (var context = db.CreateContext())
        {
            context.Add(settings);
            context.Add(credential);
            context.Add(rule);
            context.Add(window);
            context.Add(session);
            context.Add(sample);
            context.Add(attention);
            context.Add(blocked);
            context.Add(audit);

            await context.SaveChangesAsync();
        }

        await using var read = db.CreateContext();

        var loadedSettings = await read.AppSettings.AsNoTracking().SingleAsync();
        loadedSettings.ProtectionEnabled.Should().BeTrue();
        loadedSettings.Theme.Should().Be(AppTheme.Dark);
        loadedSettings.Language.Should().Be(AppLanguage.Vi);
        loadedSettings.CreatedAtUtc.Should().Be(Now);
        loadedSettings.UpdatedAtUtc.Should().Be(Now.AddSeconds(2));
        loadedSettings.LastCleanShutdownAtUtc.Should().Be(Now.AddSeconds(2));
        loadedSettings.ScheduleOverrideUntilUtc.Should().BeNull();

        var loadedCredential = await read.ParentCredentials.AsNoTracking().SingleAsync();
        loadedCredential.PinHash.Should().Be(Hash);
        loadedCredential.PinSalt.Should().Be(Salt);
        loadedCredential.PinIterations.Should().Be(600_000);
        loadedCredential.RecoveryCodeHash.Should().Be(Hash);
        loadedCredential.FailedAttempts.Should().Be(0);
        loadedCredential.CreatedAtUtc.Should().Be(Now);

        var loadedRule = await read.BlockRules.AsNoTracking().SingleAsync();
        loadedRule.RuleType.Should().Be(RuleType.Domain);
        loadedRule.MatchType.Should().Be(RuleMatchType.Exact);
        loadedRule.Value.Should().Be("ChatGPT.com");
        loadedRule.NormalizedValue.Should().Be("chatgpt.com");
        loadedRule.Category.Should().Be(BlockRuleCategory.Chatbot);
        loadedRule.Effect.Should().Be(BlockRuleEffect.Block);
        loadedRule.EnforcementStatus.Should().Be(EnforcementStatus.Enforced);
        loadedRule.IsBuiltIn.Should().BeTrue();
        loadedRule.Note.Should().Be("Chatbot");

        var loadedWindow = await read.ScheduleWindows.AsNoTracking().SingleAsync();
        loadedWindow.Days.Should().Be(ScheduleDays.Weekdays);
        loadedWindow.StartMinutes.Should().Be(480);
        loadedWindow.EndMinutes.Should().Be(690);
        loadedWindow.Label.Should().Be("Giờ học");

        var loadedSession = await read.StudySessions.AsNoTracking().SingleAsync();
        loadedSession.Status.Should().Be(StudySessionStatus.Completed);
        loadedSession.Trigger.Should().Be(SessionTrigger.Manual);
        loadedSession.StartLocalDate.Should().Be(LocalDate);
        loadedSession.StartedAtUtc.Should().Be(Now);
        loadedSession.EndedAtUtc.Should().Be(Now.AddMinutes(5));
        loadedSession.LastHeartbeatAtUtc.Should().Be(Now.AddMinutes(5));
        loadedSession.ActiveSeconds.Should().Be(100);
        loadedSession.FocusedSeconds.Should().Be(45);
        loadedSession.DistractedSeconds.Should().Be(15);
        loadedSession.MonitoredSeconds.Should().Be(60);
        loadedSession.AttentionPercent.Should().Be(75);
        loadedSession.CoveragePercent.Should().BeApproximately(60.0 * 100 / 90, 0.0001);
        loadedSession.StudySeconds.Should().Be(60);
        loadedSession.OnlineClassSeconds.Should().Be(20);
        loadedSession.IdleSeconds.Should().Be(5);
        loadedSession.BlockedAttemptCount.Should().Be(1);
        loadedSession.ConfidenceAvg.Should().Be(0.85);
        loadedSession.Note.Should().Be("Ghi chú");

        var loadedSample = await read.ActivitySamples.AsNoTracking().SingleAsync();
        loadedSample.SessionId.Should().Be(session.Id);
        loadedSample.SampledAtUtc.Should().Be(Now.AddMinutes(1));
        loadedSample.AppName.Should().Be("Chrome");
        loadedSample.WindowTitle.Should().Be("Lesson");
        loadedSample.Url.Should().Be("https://classroom.google.com/c/abc");
        loadedSample.Domain!.Value.Should().Be("classroom.google.com");
        loadedSample.UrlSource.Should().Be(UrlSource.UIA);
        loadedSample.Category.Should().Be(ActivityCategory.Study);

        var loadedAttention = await read.AttentionEvents.AsNoTracking().SingleAsync();
        loadedAttention.SessionId.Should().Be(session.Id);
        loadedAttention.State.Should().Be(AttentionState.Focused);
        loadedAttention.StartedAtUtc.Should().Be(Now.AddMinutes(2));
        loadedAttention.EndedAtUtc.Should().Be(Now.AddMinutes(3));
        loadedAttention.DurationSeconds.Should().Be(60);
        loadedAttention.Confidence.Should().Be(0.91);
        loadedAttention.Domain!.Value.Should().Be("classroom.google.com");
        loadedAttention.Note.Should().Be("focused");

        var loadedBlocked = await read.BlockedAttempts.AsNoTracking().SingleAsync();
        loadedBlocked.SessionId.Should().Be(session.Id);
        loadedBlocked.OccurredAtUtc.Should().Be(Now.AddMinutes(4));
        loadedBlocked.TargetType.Should().Be(BlockedAttemptTargetType.Domain);
        loadedBlocked.TargetValue.Should().Be("chatgpt.com");
        loadedBlocked.Domain!.Value.Should().Be("chatgpt.com");
        loadedBlocked.ObservedVia.Should().Be(BlockedAttemptObservedVia.BlockPage);
        loadedBlocked.WindowTitle.Should().Be("ChatGPT");

        var loadedAudit = await read.AuditLogs.AsNoTracking().SingleAsync();
        loadedAudit.Actor.Should().Be(AuditActor.Parent);
        loadedAudit.EventType.Should().Be(AuditEventType.ProtectionOn);
        loadedAudit.Description.Should().Be("Protection enabled");
        loadedAudit.MetadataJson.Should().Be("{\"source\":\"test\"}");
        loadedAudit.CorrelationId.Should().Be(correlationId);
        loadedAudit.OccurredAtUtc.Should().Be(Now.AddMinutes(5));
    }

    [Fact]
    public async Task Utc_timestamps_round_trip_with_millisecond_precision()
    {
        await using var db = new TempDatabase();
        await db.CreateInitializer().InitializeAsync();

        var settings = AppSettings.CreateDefault(Now);
        settings.SetScheduleOverrideUntil(Now.AddMinutes(30), Now.AddSeconds(1));
        settings.RecordCleanShutdown(Now.AddSeconds(2));

        var session = StudySession.Start(Now.AddMinutes(1), LocalDate, SessionTrigger.Schedule, true);
        session.Complete(Now.AddMinutes(2));

        await using (var context = db.CreateContext())
        {
            context.Add(settings);
            context.Add(session);
            await context.SaveChangesAsync();
        }

        await using (var read = db.CreateContext())
        {
            var loadedSettings = await read.AppSettings.AsNoTracking().SingleAsync();
            loadedSettings.CreatedAtUtc.Kind.Should().Be(DateTimeKind.Utc);
            loadedSettings.CreatedAtUtc.Should().Be(Now);
            loadedSettings.ScheduleOverrideUntilUtc.Should().Be(Now.AddMinutes(30));
            loadedSettings.ScheduleOverrideUntilUtc!.Value.Kind.Should().Be(DateTimeKind.Utc);
            loadedSettings.LastCleanShutdownAtUtc.Should().Be(Now.AddSeconds(2));

            var loadedSession = await read.StudySessions.AsNoTracking().SingleAsync();
            loadedSession.StartedAtUtc.Kind.Should().Be(DateTimeKind.Utc);
            loadedSession.StartedAtUtc.Should().Be(Now.AddMinutes(1));
            loadedSession.EndedAtUtc.Should().Be(Now.AddMinutes(2));
            loadedSession.EndedAtUtc!.Value.Kind.Should().Be(DateTimeKind.Utc);
        }

        using var connection = db.OpenConnection();

        TempDatabase.Scalar(connection, "SELECT CreatedAtUtc FROM AppSettings;")
            .Should().Be("2026-10-29T08:30:15.123Z");
        TempDatabase.Scalar(connection, "SELECT ScheduleOverrideUntilUtc FROM AppSettings;")
            .Should().Be("2026-10-29T09:00:15.123Z");
        TempDatabase.Scalar(connection, "SELECT StartedAtUtc FROM StudySessions;")
            .Should().Be("2026-10-29T08:31:15.123Z");
        TempDatabase.Scalar(connection, "SELECT EndedAtUtc FROM StudySessions;")
            .Should().Be("2026-10-29T08:32:15.123Z");
    }

    [Fact]
    public async Task Enums_store_as_text_and_generated_columns_are_computed_by_sqlite()
    {
        await using var db = new TempDatabase();
        await db.CreateInitializer().InitializeAsync();

        var settings = AppSettings.CreateDefault(Now);
        var rule = BlockRule.Create(
            RuleType.Domain,
            RuleMatchType.Exact,
            "chatgpt.com",
            BlockRuleCategory.Chatbot,
            BlockRuleEffect.Block,
            Now);
        var window = ScheduleWindow.Create(ScheduleDays.Weekdays, 480, 690, "Học", Now);

        var session = StudySession.Start(Now, LocalDate, SessionTrigger.Manual, true);
        session.AddAttentionSeconds(AttentionState.Focused, 45, Now.AddSeconds(1));
        session.AddAttentionSeconds(AttentionState.Distracted, 15, Now.AddSeconds(2));
        session.AddMonitoringEligibleSeconds(100, Now.AddSeconds(3));

        await using (var context = db.CreateContext())
        {
            context.Add(settings);
            context.Add(rule);
            context.Add(window);
            context.Add(session);
            await context.SaveChangesAsync();
        }

        using var connection = db.OpenConnection();

        TempDatabase.Scalar(connection, "SELECT Theme FROM AppSettings;").Should().Be("dark");
        TempDatabase.Scalar(connection, "SELECT Language FROM AppSettings;").Should().Be("vi");
        TempDatabase.Scalar(connection, "SELECT ProtectionEnabled FROM AppSettings;").Should().Be(0L);

        TempDatabase.Scalar(connection, "SELECT RuleType FROM BlockRules;").Should().Be("Domain");
        TempDatabase.Scalar(connection, "SELECT MatchType FROM BlockRules;").Should().Be("Exact");
        TempDatabase.Scalar(connection, "SELECT Category FROM BlockRules;").Should().Be("Chatbot");
        TempDatabase.Scalar(connection, "SELECT Effect FROM BlockRules;").Should().Be("Block");
        TempDatabase.Scalar(connection, "SELECT EnforcementStatus FROM BlockRules;").Should().Be("Enforced");

        TempDatabase.Scalar(connection, "SELECT DaysMask FROM ScheduleWindows;").Should().Be(31L);

        TempDatabase.Scalar(connection, "SELECT Status FROM StudySessions;").Should().Be("Active");
        TempDatabase.Scalar(connection, "SELECT Trigger FROM StudySessions;").Should().Be("Manual");
        TempDatabase.Scalar(connection, "SELECT MonitoredSeconds FROM StudySessions;").Should().Be(60L);

        Convert.ToDouble(TempDatabase.Scalar(connection, "SELECT AttentionPercent FROM StudySessions;"))
            .Should().BeApproximately(75, 0.0001);

        Convert.ToDouble(TempDatabase.Scalar(connection, "SELECT CoveragePercent FROM StudySessions;"))
            .Should().BeApproximately(60, 0.0001);
    }

    [Fact]
    public async Task Only_one_active_session_is_allowed()
    {
        await using var db = new TempDatabase();
        await db.CreateInitializer().InitializeAsync();

        await using (var context = db.CreateContext())
        {
            context.StudySessions.Add(StudySession.Start(Now, LocalDate, SessionTrigger.Manual, true));
            await context.SaveChangesAsync();
        }

        await using (var context = db.CreateContext())
        {
            context.StudySessions.Add(StudySession.Start(Now.AddSeconds(1), LocalDate, SessionTrigger.Schedule, true));

            var exception = await Assert.ThrowsAsync<DbUpdateException>(() => context.SaveChangesAsync());
            exception.InnerException.Should().BeOfType<SqliteException>();
        }

        await using (var context = db.CreateContext())
        {
            var first = await context.StudySessions.SingleAsync();
            first.Complete(Now.AddMinutes(1));
            await context.SaveChangesAsync();
        }

        await using (var context = db.CreateContext())
        {
            context.StudySessions.Add(StudySession.Start(Now.AddMinutes(2), LocalDate, SessionTrigger.Schedule, true));
            await context.SaveChangesAsync();
        }

        await using var read = db.CreateContext();
        (await read.StudySessions.CountAsync()).Should().Be(2);
    }

    [Fact]
    public async Task Block_rule_unique_index_includes_effect()
    {
        await using var db = new TempDatabase();
        await db.CreateInitializer().InitializeAsync();

        var block = BlockRule.Create(
            RuleType.Domain, RuleMatchType.Exact, "chatgpt.com",
            BlockRuleCategory.Chatbot, BlockRuleEffect.Block, Now);
        var allow = BlockRule.Create(
            RuleType.Domain, RuleMatchType.Exact, "chatgpt.com",
            BlockRuleCategory.Study, BlockRuleEffect.Allow, Now);

        await using (var context = db.CreateContext())
        {
            context.Add(block);
            context.Add(allow);
            await context.SaveChangesAsync();
        }

        await using (var context = db.CreateContext())
        {
            context.BlockRules.Add(BlockRule.Create(
                RuleType.Domain, RuleMatchType.Exact, "chatgpt.com",
                BlockRuleCategory.Chatbot, BlockRuleEffect.Block, Now.AddSeconds(1)));

            var exception = await Assert.ThrowsAsync<DbUpdateException>(() => context.SaveChangesAsync());
            exception.InnerException.Should().BeOfType<SqliteException>();
        }

        await using var read = db.CreateContext();
        (await read.BlockRules.CountAsync()).Should().Be(2);
    }

    [Fact]
    public async Task Deleting_a_session_cascades_details_and_nulls_blocked_attempt_session()
    {
        await using var db = new TempDatabase();
        await db.CreateInitializer().InitializeAsync();

        var session = StudySession.Start(Now, LocalDate, SessionTrigger.Manual, true);
        var sample = ActivitySample.Capture(
            session.Id, Now.AddSeconds(1), 15, ActivityCategory.Study,
            isForeground: true, isOnlineClass: false);
        var attention = AttentionEvent.Open(session.Id, AttentionState.Focused, Now.AddSeconds(2));
        var blocked = BlockedAttempt.Record(
            Now.AddSeconds(3),
            BlockedAttemptTargetType.App,
            "chrome.exe",
            BlockedAttemptObservedVia.ProcessWatcher,
            sessionId: session.Id);

        await using (var context = db.CreateContext())
        {
            context.Add(session);
            context.Add(sample);
            context.Add(attention);
            context.Add(blocked);
            await context.SaveChangesAsync();
        }

        await using (var context = db.CreateContext())
        {
            var deleted = await context.Database.ExecuteSqlRawAsync("DELETE FROM StudySessions;");
            deleted.Should().Be(1);
        }

        await using var read = db.CreateContext();

        (await read.ActivitySamples.CountAsync()).Should().Be(0);
        (await read.AttentionEvents.CountAsync()).Should().Be(0);

        var remaining = await read.BlockedAttempts.AsNoTracking().SingleAsync();
        remaining.SessionId.Should().BeNull();
        remaining.TargetValue.Should().Be("chrome");
    }
}
