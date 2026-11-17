using EduFocus.Domain.Entities;
using EduFocus.Domain.Enums;
using EduFocus.Infrastructure.Cloud;
using EduFocus.Ports.DTOs;
using EduFocus.Ports.Outbound.Cloud;
using EduFocus.Ports.Outbound.Persistence;
using EduFocus.Ports.Outbound.Services;
using FluentAssertions;

namespace EduFocus.Infrastructure.Tests.Cloud;

public sealed class CloudSyncWorkerTests
{
    private static readonly DateTime Start = new(2026, 11, 17, 9, 0, 0, DateTimeKind.Utc);
    private static readonly Guid SessionId = Guid.Parse("0f8fad5b-d9cb-469f-a165-70867728950e");
    private static readonly Guid FamilyId = Guid.Parse("7c9e6679-7425-40de-944b-e07fc1f90ae7");
    private static readonly Guid DeviceId = Guid.Parse("3f2504e0-4f89-41d3-9a0c-0305e82c3301");
    private const string Token = "efd_desktop_token_must_never_leak";
    private const string CloudBaseUrl = "https://cloud.example.com";
    private const string Payload = "{\"sessionsCount\":1}";

    [Fact]
    public async Task Disabled_link_never_calls_the_network()
    {
        var harness = new Harness(linked: false);
        await harness.Outbox.AddAsync(NewEvent(Start));

        await harness.Worker.RunOnceAsync();

        harness.Sync.Batches.Should().BeEmpty();
        harness.Outbox.Items.Should().ContainSingle().Which.IsPending.Should().BeTrue();
    }

    [Fact]
    public async Task Due_events_are_sent_and_marked_sent()
    {
        var harness = new Harness();
        var first = NewEvent(Start);
        var second = NewEvent(Start.AddSeconds(1));
        await harness.Outbox.AddAsync(first);
        await harness.Outbox.AddAsync(second);

        await harness.Worker.RunOnceAsync();

        harness.Sync.Batches.Should().ContainSingle()
            .Which.Select(item => item.ClientEventId)
            .Should().BeEquivalentTo([first.ClientEventId, second.ClientEventId]);
        first.SentAtUtc.Should().Be(harness.Time.UtcNow.UtcDateTime);
        second.SentAtUtc.Should().Be(harness.Time.UtcNow.UtcDateTime);
        harness.Links.Stored!.LastSyncAtUtc.Should().Be(harness.Time.UtcNow.UtcDateTime);
        harness.Links.Stored.LastError.Should().BeNull();
        harness.Audit.Entries.Should().BeEmpty();
    }

    [Fact]
    public async Task Batching_respects_the_fifty_event_limit()
    {
        var harness = new Harness();
        for (var index = 0; index < 60; index++)
            await harness.Outbox.AddAsync(NewEvent(Start.AddSeconds(index)));

        await harness.Worker.RunOnceAsync();

        harness.Sync.Batches.Select(batch => batch.Count).Should().Equal(50, 10);
        harness.Outbox.Items.Should().OnlyContain(item => item.SentAtUtc != null);
    }

    [Fact]
    public async Task Rejected_events_are_terminal_and_not_resent()
    {
        var harness = new Harness();
        var rejected = NewEvent(Start);
        var applied = NewEvent(Start.AddSeconds(1));
        await harness.Outbox.AddAsync(rejected);
        await harness.Outbox.AddAsync(applied);
        harness.Sync.ResultFactory = batch => new CloudSyncBatchResult(
            batch.Select(item => new CloudSyncEventResult(
                item.ClientEventId,
                item.ClientEventId == rejected.ClientEventId
                    ? CloudEventOutcome.Rejected
                    : CloudEventOutcome.Applied,
                item.ClientEventId == rejected.ClientEventId ? "stale_summary" : null,
                null)).ToList(),
            harness.Time.UtcNow);

        await harness.Worker.RunOnceAsync();
        harness.Time.Advance(TimeSpan.FromHours(1));
        await harness.Worker.RunOnceAsync();

        rejected.FailedAtUtc.Should().NotBeNull();
        rejected.LastError.Should().Be("stale_summary");
        applied.SentAtUtc.Should().NotBeNull();
        harness.Sync.Batches.Should().HaveCount(1);
        harness.Logger.Warnings.Should().Contain(message => message.Contains("stale_summary"));
    }

    [Fact]
    public async Task Missing_results_stay_queued_for_retry()
    {
        var harness = new Harness();
        var item = NewEvent(Start);
        await harness.Outbox.AddAsync(item);
        harness.Sync.ResultFactory = _ => new CloudSyncBatchResult(
            [],
            harness.Time.UtcNow);

        await harness.Worker.RunOnceAsync();

        item.SentAtUtc.Should().BeNull();
        item.AttemptCount.Should().Be(1);
        item.NextAttemptAtUtc.Should().Be(harness.Time.UtcNow.UtcDateTime.AddMinutes(1));
        item.LastError.Should().Be("missing_result");
    }

    [Fact]
    public async Task Server_errors_back_off_exponentially_up_to_the_cap()
    {
        var harness = new Harness();
        var item = NewEvent(Start);
        await harness.Outbox.AddAsync(item);
        harness.Sync.Failure = new CloudSyncException(
            CloudErrorCodes.ServerError,
            "Máy chủ cloud đang gặp sự cố.",
            statusCode: 503);

        await harness.Worker.RunOnceAsync();

        item.AttemptCount.Should().Be(1);
        item.NextAttemptAtUtc.Should().Be(harness.Time.UtcNow.UtcDateTime.AddMinutes(1));
        harness.Links.Stored!.LastError.Should().NotBeNull();

        harness.Time.Advance(TimeSpan.FromMinutes(1));
        await harness.Worker.RunOnceAsync();

        item.AttemptCount.Should().Be(2);
        item.NextAttemptAtUtc.Should().Be(harness.Time.UtcNow.UtcDateTime.AddMinutes(2));
    }

    [Fact]
    public async Task Unauthorized_pauses_sending_and_keeps_the_outbox()
    {
        var harness = new Harness();
        var item = NewEvent(Start);
        await harness.Outbox.AddAsync(item);
        harness.Sync.Failure = new CloudSyncException(
            CloudErrorCodes.Unauthorized,
            "Máy chủ cloud từ chối token của thiết bị này.",
            statusCode: 401);

        await harness.Worker.RunOnceAsync();

        item.IsPending.Should().BeTrue();
        item.AttemptCount.Should().Be(0);
        harness.Links.Stored!.LastError.Should().Be(CloudSyncWorker.RePairMessage);
        harness.Audit.Entries.Should().ContainSingle()
            .Which.EventType.Should().Be(AuditEventType.CloudSyncFailed);

        harness.Time.Advance(TimeSpan.FromHours(1));
        await harness.Worker.RunOnceAsync();

        harness.Sync.Batches.Should().HaveCount(1);
        item.IsPending.Should().BeTrue();

        harness.Sync.Failure = null;
        harness.Links.Stored!.RecordError("Đã ghép nối lại.", harness.Time.UtcNow.UtcDateTime);
        harness.Time.Advance(TimeSpan.FromMinutes(1));
        await harness.Worker.RunOnceAsync();

        item.SentAtUtc.Should().NotBeNull();
        harness.Sync.Batches.Should().HaveCount(2);
    }

    [Fact]
    public async Task Rate_limit_honors_the_retry_after_header()
    {
        var harness = new Harness();
        var item = NewEvent(Start);
        await harness.Outbox.AddAsync(item);
        harness.Sync.Failure = new CloudSyncException(
            CloudErrorCodes.RateLimited,
            "Máy chủ cloud đang giới hạn tần suất.",
            statusCode: 429,
            retryAfter: TimeSpan.FromMinutes(3));

        await harness.Worker.RunOnceAsync();

        item.AttemptCount.Should().Be(1);
        item.NextAttemptAtUtc.Should().Be(harness.Time.UtcNow.UtcDateTime.AddMinutes(3));
        item.LastError.Should().Be(CloudErrorCodes.RateLimited);
        harness.Links.Stored!.LastError.Should().NotBeNull();
    }

    [Fact]
    public async Task Failure_audit_is_written_once_per_streak()
    {
        var harness = new Harness();
        var first = NewEvent(Start);
        await harness.Outbox.AddAsync(first);
        harness.Sync.Failure = new CloudSyncException(
            CloudErrorCodes.Network,
            "Không kết nối được tới máy chủ cloud.",
            isNetworkError: true);

        await harness.Worker.RunOnceAsync();
        harness.Time.Advance(TimeSpan.FromMinutes(1));
        await harness.Worker.RunOnceAsync();

        harness.Audit.Entries.Should().ContainSingle();

        harness.Sync.Failure = null;
        harness.Time.Advance(TimeSpan.FromMinutes(2));
        await harness.Worker.RunOnceAsync();
        first.SentAtUtc.Should().NotBeNull();

        await harness.Outbox.AddAsync(NewEvent(harness.Time.UtcNow.UtcDateTime));
        harness.Time.Advance(TimeSpan.FromMinutes(1));
        harness.Sync.Failure = new CloudSyncException(
            CloudErrorCodes.Network,
            "Không kết nối được tới máy chủ cloud.",
            isNetworkError: true);
        await harness.Worker.RunOnceAsync();

        harness.Audit.Entries.Should().HaveCount(2);
    }

    [Fact]
    public async Task Trim_runs_before_sending_and_only_a_count_is_logged()
    {
        var harness = new Harness();
        await harness.Outbox.AddAsync(NewEvent(Start));
        harness.Outbox.TrimRemoved = 3;

        await harness.Worker.RunOnceAsync();

        harness.Outbox.TrimCalls.Should().Be(1);
        harness.Logger.Warnings.Should().Contain(message => message.Contains("3 oldest event(s)"));
    }

    private static CloudOutboxEvent NewEvent(DateTime createdAtUtc) =>
        CloudOutboxEvent.Create(Guid.NewGuid().ToString("D"), "daily_summary", Payload, createdAtUtc);

    private sealed class Harness
    {
        public Harness(bool linked = true)
        {
            if (linked)
                Links.Stored = CreateLinkedLink();

            Worker = new CloudSyncWorker(
                Links,
                Outbox,
                Sync,
                Audit,
                new StubCorrelationContext(),
                Logger,
                Time);
        }

        public FakeCloudLinkRepository Links { get; } = new();

        public FakeCloudOutboxRepository Outbox { get; } = new();

        public FakeCloudSyncPort Sync { get; } = new();

        public FakeAuditLogRepository Audit { get; } = new();

        public TestLogger Logger { get; } = new();

        public FixedTimeProvider Time { get; } = new(new DateTimeOffset(Start.AddMinutes(1)));

        public CloudSyncWorker Worker { get; }

        private static CloudLink CreateLinkedLink()
        {
            var link = CloudLink.CreateDefault(Start);
            link.RecordConsent(Start);
            link.BeginPairing(
                CloudBaseUrl,
                SessionId,
                "ABCD2345",
                Token,
                Start.AddMinutes(10),
                Start);
            link.CompletePairing(FamilyId, DeviceId, Start, Start);
            return link;
        }
    }

    private sealed class FixedTimeProvider : TimeProvider
    {
        public FixedTimeProvider(DateTimeOffset now) => UtcNow = now;

        public DateTimeOffset UtcNow { get; private set; }

        public override DateTimeOffset GetUtcNow() => UtcNow;

        public void Advance(TimeSpan delta) => UtcNow += delta;
    }

    private sealed class FakeCloudLinkRepository : ICloudLinkRepository
    {
        public CloudLink? Stored { get; set; }

        public Task<CloudLink?> GetAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(Stored);

        public Task AddAsync(CloudLink link, CancellationToken cancellationToken = default)
        {
            Stored = link;
            return Task.CompletedTask;
        }

        public Task UpdateAsync(CloudLink link, CancellationToken cancellationToken = default)
        {
            Stored = link;
            return Task.CompletedTask;
        }

        public Task<bool> TryRecordSyncAsync(
            string desktopToken,
            DateTime lastSyncAtUtc,
            DateTime updatedAtUtc,
            CancellationToken cancellationToken = default)
        {
            if (Stored is null || !Stored.IsLinked || Stored.DesktopToken != desktopToken)
                return Task.FromResult(false);

            Stored.RecordSync(lastSyncAtUtc, Max(updatedAtUtc, Stored.UpdatedAtUtc));
            return Task.FromResult(true);
        }

        public Task<bool> TryRecordErrorAsync(
            string desktopToken,
            string? message,
            DateTime updatedAtUtc,
            CancellationToken cancellationToken = default)
        {
            if (Stored is null || !Stored.IsEnabled || Stored.DesktopToken != desktopToken)
                return Task.FromResult(false);

            Stored.RecordError(message, Max(updatedAtUtc, Stored.UpdatedAtUtc));
            return Task.FromResult(true);
        }

        private static DateTime Max(DateTime left, DateTime right) => left >= right ? left : right;
    }

    private sealed class FakeCloudOutboxRepository : ICloudOutboxRepository
    {
        public List<CloudOutboxEvent> Items { get; } = [];

        public int TrimCalls { get; private set; }

        public int TrimRemoved { get; set; }

        public Task AddAsync(CloudOutboxEvent item, CancellationToken cancellationToken = default)
        {
            Items.Add(item);
            return Task.CompletedTask;
        }

        public Task<bool> ExistsAsync(string clientEventId, CancellationToken cancellationToken = default) =>
            Task.FromResult(Items.Exists(item =>
                string.Equals(item.ClientEventId, clientEventId, StringComparison.Ordinal)));

        public Task<IReadOnlyList<CloudOutboxEvent>> GetDueAsync(
            DateTime utcNow,
            int limit,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<CloudOutboxEvent>>(Items
                .Where(item => item.IsPending && item.NextAttemptAtUtc <= utcNow)
                .OrderBy(item => item.CreatedAtUtc)
                .Take(limit)
                .ToList());

        public Task MarkSentAsync(
            IReadOnlyList<Guid> ids,
            DateTime sentAtUtc,
            CancellationToken cancellationToken = default)
        {
            foreach (var item in Items.Where(item => ids.Contains(item.Id)))
                item.MarkSent(sentAtUtc);
            return Task.CompletedTask;
        }

        public Task ScheduleRetryAsync(
            IReadOnlyList<Guid> ids,
            DateTime nextAttemptAtUtc,
            string? lastError,
            CancellationToken cancellationToken = default)
        {
            foreach (var item in Items.Where(item => ids.Contains(item.Id)))
                item.ScheduleRetry(nextAttemptAtUtc, lastError);
            return Task.CompletedTask;
        }

        public Task MarkFailedAsync(
            IReadOnlyList<Guid> ids,
            DateTime failedAtUtc,
            string? reason,
            CancellationToken cancellationToken = default)
        {
            foreach (var item in Items.Where(item => ids.Contains(item.Id)))
                item.MarkFailed(failedAtUtc, reason);
            return Task.CompletedTask;
        }

        public Task<int> TrimAsync(
            DateTime utcNow,
            int maxPendingEvents,
            TimeSpan maxAge,
            CancellationToken cancellationToken = default)
        {
            TrimCalls++;
            return Task.FromResult(TrimRemoved);
        }

        public Task<CloudOutboxStatus> GetStatusAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(new CloudOutboxStatus(0, 0, null, null));
    }

    private sealed class FakeCloudSyncPort : ICloudSyncPort
    {
        public List<IReadOnlyList<CloudSyncEvent>> Batches { get; } = [];

        public Func<IReadOnlyList<CloudSyncEvent>, CloudSyncBatchResult>? ResultFactory { get; set; }

        public CloudSyncException? Failure { get; set; }

        public Task<CloudSyncBatchResult> SendEventsAsync(
            Uri cloudBaseUrl,
            string desktopToken,
            IReadOnlyList<CloudSyncEvent> events,
            CancellationToken cancellationToken = default)
        {
            Batches.Add(events);

            if (Failure is not null)
                throw Failure;

            var results = ResultFactory?.Invoke(events)
                ?? new CloudSyncBatchResult(
                    events.Select(item => new CloudSyncEventResult(
                        item.ClientEventId,
                        CloudEventOutcome.Applied,
                        null,
                        null)).ToList(),
                    DateTimeOffset.UtcNow);
            return Task.FromResult(results);
        }

        public Task<CloudPairingSession> CreatePairingSessionAsync(
            Uri cloudBaseUrl,
            CloudPairingRequest request,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<CloudPairingSessionStatus> GetPairingSessionStatusAsync(
            Uri cloudBaseUrl,
            Guid sessionId,
            string desktopToken,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<CloudRemoteLinkStatus> GetLinkStatusAsync(
            Uri cloudBaseUrl,
            string desktopToken,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task RevokeDeviceAsync(
            Uri cloudBaseUrl,
            string desktopToken,
            Guid deviceId,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task DeleteFamilyAsync(
            Uri cloudBaseUrl,
            string desktopToken,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }

    private sealed class FakeAuditLogRepository : IAuditLogRepository
    {
        public List<AuditLog> Entries { get; } = [];

        public Task AddAsync(AuditLog auditLog, CancellationToken cancellationToken = default)
        {
            Entries.Add(auditLog);
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<AuditLog>> ListByTypeSinceAsync(
            EduFocus.Domain.Enums.AuditEventType eventType,
            DateTime fromUtc,
            int limit,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<AuditLog>>([]);
    }

    private sealed class StubCorrelationContext : ICorrelationContext
    {
        public Guid? Current { get; } = Guid.Parse("11111111-1111-1111-1111-111111111111");

        public IDisposable BeginScope(Guid correlationId) => new NullScope();

        private sealed class NullScope : IDisposable
        {
            public void Dispose()
            {
            }
        }
    }
}
