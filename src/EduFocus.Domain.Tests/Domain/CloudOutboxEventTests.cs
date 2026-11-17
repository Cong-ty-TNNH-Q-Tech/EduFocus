using EduFocus.Domain.Entities;
using FluentAssertions;

namespace EduFocus.Domain.Tests.Domain;

public sealed class CloudOutboxEventTests
{
    private static readonly DateTime Now = new(2026, 11, 17, 9, 0, 0, DateTimeKind.Utc);
    private static readonly Guid ClientEventId = Guid.Parse("0f8fad5b-d9cb-469f-a165-70867728950e");
    private const string Payload = "{\"sessionsCount\":3}";

    [Fact]
    public void Create_starts_pending_with_a_fresh_row_id()
    {
        var item = CloudOutboxEvent.Create(ClientEventId, " daily_summary ", Payload, Now);

        item.Id.Should().NotBe(Guid.Empty);
        item.Id.Should().NotBe(ClientEventId);
        item.ClientEventId.Should().Be(ClientEventId);
        item.Type.Should().Be("daily_summary");
        item.PayloadJson.Should().Be(Payload);
        item.CreatedAtUtc.Should().Be(Now);
        item.NextAttemptAtUtc.Should().Be(Now);
        item.AttemptCount.Should().Be(0);
        item.SentAtUtc.Should().BeNull();
        item.FailedAtUtc.Should().BeNull();
        item.LastError.Should().BeNull();
        item.IsPending.Should().BeTrue();
    }

    [Fact]
    public void Create_rejects_an_empty_client_event_id()
    {
        var act = () => CloudOutboxEvent.Create(Guid.Empty, "daily_summary", Payload, Now);

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Create_rejects_an_oversized_payload()
    {
        var payload = "{\"value\":\"" + new string('x', CloudOutboxEvent.MaxPayloadLength) + "\"}";

        var act = () => CloudOutboxEvent.Create(ClientEventId, "daily_summary", payload, Now);

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void MarkSent_is_terminal_and_clears_the_last_error()
    {
        var item = CloudOutboxEvent.Create(ClientEventId, "daily_summary", Payload, Now);
        item.ScheduleRetry(Now.AddMinutes(1), "network");

        item.MarkSent(Now.AddMinutes(2));

        item.SentAtUtc.Should().Be(Now.AddMinutes(2));
        item.LastError.Should().BeNull();
        item.IsPending.Should().BeFalse();
        var act = () => item.ScheduleRetry(Now.AddMinutes(3), "again");
        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void ScheduleRetry_increments_the_attempt_and_sanitizes_the_error()
    {
        var item = CloudOutboxEvent.Create(ClientEventId, "daily_summary", Payload, Now);

        item.ScheduleRetry(Now.AddMinutes(1), "first\nline");

        item.AttemptCount.Should().Be(1);
        item.NextAttemptAtUtc.Should().Be(Now.AddMinutes(1));
        item.LastError.Should().Be("first line");
        item.IsPending.Should().BeTrue();
    }

    [Fact]
    public void MarkFailed_is_terminal_and_keeps_a_sanitized_reason()
    {
        var item = CloudOutboxEvent.Create(ClientEventId, "daily_summary", Payload, Now);

        item.MarkFailed(Now.AddMinutes(1), "stale_summary");

        item.FailedAtUtc.Should().Be(Now.AddMinutes(1));
        item.LastError.Should().Be("stale_summary");
        item.IsPending.Should().BeFalse();
        var act = () => item.MarkSent(Now.AddMinutes(2));
        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void Last_error_is_truncated_to_the_column_limit()
    {
        var item = CloudOutboxEvent.Create(ClientEventId, "daily_summary", Payload, Now);
        var reason = new string('e', CloudOutboxEvent.MaxErrorLength + 20);

        item.MarkFailed(Now, reason);

        item.LastError.Should().HaveLength(CloudOutboxEvent.MaxErrorLength);
    }
}
