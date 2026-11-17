using EduFocus.Domain.Entities;
using EduFocus.SqliteDataAccess.Persistence;
using EduFocus.SqliteDataAccess.Repositories;
using EduFocus.SqliteDataAccess.Tests.TestSupport;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;

namespace EduFocus.SqliteDataAccess.Tests;

public sealed class CloudOutboxRepositoryTests
{
    private static readonly DateTime Now = new(2026, 11, 17, 9, 0, 0, DateTimeKind.Utc);
    private const string Payload = "{\"sessionsCount\":1}";

    [Fact]
    public async Task Pending_events_round_trip_in_creation_order()
    {
        await using var db = new TempDatabase();
        await db.CreateInitializer().InitializeAsync();

        var repository = new SqliteCloudOutboxRepository(db.ContextFactory);
        var first = CloudOutboxEvent.Create(Guid.NewGuid(), "daily_summary", Payload, Now);
        var second = CloudOutboxEvent.Create(Guid.NewGuid(), "attention_alert", Payload, Now.AddSeconds(1));
        await repository.AddAsync(first);
        await repository.AddAsync(second);

        var reopened = new SqliteCloudOutboxRepository(
            new SqliteDbContextFactory(db.Options, db.KeyStore));
        var due = await reopened.GetDueAsync(Now.AddMinutes(1), 50);

        due.Select(item => item.Id).Should().ContainInOrder(first.Id, second.Id);
        due[0].PayloadJson.Should().Be(Payload);
        due[0].AttemptCount.Should().Be(0);
        due[0].IsPending.Should().BeTrue();
    }

    [Fact]
    public async Task Due_query_skips_future_retries_and_terminal_rows()
    {
        await using var db = new TempDatabase();
        await db.CreateInitializer().InitializeAsync();

        var repository = new SqliteCloudOutboxRepository(db.ContextFactory);
        var dueNow = CloudOutboxEvent.Create(Guid.NewGuid(), "daily_summary", Payload, Now);
        var later = CloudOutboxEvent.Create(Guid.NewGuid(), "daily_summary", Payload, Now);
        var sent = CloudOutboxEvent.Create(Guid.NewGuid(), "daily_summary", Payload, Now);
        var rejected = CloudOutboxEvent.Create(Guid.NewGuid(), "daily_summary", Payload, Now);
        await repository.AddAsync(dueNow);
        await repository.AddAsync(later);
        await repository.AddAsync(sent);
        await repository.AddAsync(rejected);

        await repository.ScheduleRetryAsync([later.Id], Now.AddMinutes(5), "network");
        await repository.MarkSentAsync([sent.Id], Now);
        await repository.MarkFailedAsync([rejected.Id], Now, "stale_summary");

        var first = await repository.GetDueAsync(Now, 50);
        var second = await repository.GetDueAsync(Now.AddMinutes(5), 50);

        first.Should().ContainSingle().Which.Id.Should().Be(dueNow.Id);
        second.Select(item => item.Id).Should().BeEquivalentTo([dueNow.Id, later.Id]);
    }

    [Fact]
    public async Task Schedule_retry_increments_attempts_and_defers_the_event()
    {
        await using var db = new TempDatabase();
        await db.CreateInitializer().InitializeAsync();

        var repository = new SqliteCloudOutboxRepository(db.ContextFactory);
        var item = CloudOutboxEvent.Create(Guid.NewGuid(), "daily_summary", Payload, Now);
        await repository.AddAsync(item);

        await repository.ScheduleRetryAsync([item.Id], Now.AddMinutes(2), "network");

        (await repository.GetDueAsync(Now, 50)).Should().BeEmpty();
        var retried = (await repository.GetDueAsync(Now.AddMinutes(3), 50)).Should().ContainSingle().Subject;
        retried.AttemptCount.Should().Be(1);
        retried.NextAttemptAtUtc.Should().Be(Now.AddMinutes(2));
        retried.LastError.Should().Be("network");

        await repository.ScheduleRetryAsync([item.Id], Now.AddMinutes(6), "timeout");
        var second = (await repository.GetDueAsync(Now.AddMinutes(7), 50)).Should().ContainSingle().Subject;
        second.AttemptCount.Should().Be(2);
        second.LastError.Should().Be("timeout");
    }

    [Fact]
    public async Task Mark_sent_and_failed_are_terminal_and_reported_in_status()
    {
        await using var db = new TempDatabase();
        await db.CreateInitializer().InitializeAsync();

        var repository = new SqliteCloudOutboxRepository(db.ContextFactory);
        var sent = CloudOutboxEvent.Create(Guid.NewGuid(), "daily_summary", Payload, Now);
        var rejected = CloudOutboxEvent.Create(Guid.NewGuid(), "daily_summary", Payload, Now.AddSeconds(1));
        var pending = CloudOutboxEvent.Create(Guid.NewGuid(), "daily_summary", Payload, Now.AddSeconds(2));
        await repository.AddAsync(sent);
        await repository.AddAsync(rejected);
        await repository.AddAsync(pending);

        await repository.MarkSentAsync([sent.Id], Now.AddSeconds(10));
        await repository.MarkFailedAsync([rejected.Id], Now.AddSeconds(11), "stale_summary");

        var status = await repository.GetStatusAsync();

        status.PendingCount.Should().Be(1);
        status.FailedCount.Should().Be(1);
        status.LastSentAtUtc.Should().Be(Now.AddSeconds(10));
        status.LastError.Should().Be("stale_summary");
        (await repository.GetDueAsync(Now.AddHours(1), 50))
            .Should().ContainSingle().Which.Id.Should().Be(pending.Id);
    }

    [Fact]
    public async Task Trim_removes_rows_older_than_seven_days()
    {
        await using var db = new TempDatabase();
        await db.CreateInitializer().InitializeAsync();

        var repository = new SqliteCloudOutboxRepository(db.ContextFactory);
        var old = CloudOutboxEvent.Create(Guid.NewGuid(), "daily_summary", Payload, Now.AddDays(-8));
        var fresh = CloudOutboxEvent.Create(Guid.NewGuid(), "daily_summary", Payload, Now.AddHours(-1));
        await repository.AddAsync(old);
        await repository.AddAsync(fresh);

        var removed = await repository.TrimAsync(Now, 5_000, TimeSpan.FromDays(7));

        removed.Should().Be(1);
        (await repository.GetDueAsync(Now, 50)).Select(item => item.Id)
            .Should().BeEquivalentTo([fresh.Id]);
    }

    [Fact]
    public async Task Trim_keeps_the_newest_events_when_the_queue_exceeds_the_limit()
    {
        await using var db = new TempDatabase();
        await db.CreateInitializer().InitializeAsync();

        var repository = new SqliteCloudOutboxRepository(db.ContextFactory);
        var oldest = CloudOutboxEvent.Create(Guid.NewGuid(), "daily_summary", Payload, Now);
        var middle = CloudOutboxEvent.Create(Guid.NewGuid(), "daily_summary", Payload, Now.AddSeconds(1));
        var newest = CloudOutboxEvent.Create(Guid.NewGuid(), "daily_summary", Payload, Now.AddSeconds(2));
        await repository.AddAsync(oldest);
        await repository.AddAsync(middle);
        await repository.AddAsync(newest);

        var removed = await repository.TrimAsync(Now, 2, TimeSpan.FromDays(7));

        removed.Should().Be(1);
        (await repository.GetDueAsync(Now.AddMinutes(1), 50)).Select(item => item.Id)
            .Should().BeEquivalentTo([middle.Id, newest.Id]);
    }

    [Fact]
    public async Task Client_event_ids_are_unique()
    {
        await using var db = new TempDatabase();
        await db.CreateInitializer().InitializeAsync();

        var repository = new SqliteCloudOutboxRepository(db.ContextFactory);
        var clientEventId = Guid.NewGuid();
        await repository.AddAsync(CloudOutboxEvent.Create(clientEventId, "daily_summary", Payload, Now));

        var act = async () => await repository.AddAsync(
            CloudOutboxEvent.Create(clientEventId, "daily_summary", Payload, Now.AddSeconds(1)));

        await act.Should().ThrowAsync<DbUpdateException>();
    }
}
