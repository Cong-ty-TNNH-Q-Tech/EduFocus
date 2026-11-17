using EduFocus.Domain.Attention;
using EduFocus.Domain.Entities;
using EduFocus.Ports.Outbound.Attention;

namespace EduFocus.UseCases.Tests.TestSupport;

internal sealed class RecordingAttentionStateObserver : IAttentionStateObserver
{
    public List<(AttentionState? State, DateTimeOffset ObservedAtUtc)> Observations { get; } = [];

    public bool Throw { get; set; }

    public ValueTask OnStateObservedAsync(
        AttentionState? state,
        DateTimeOffset observedAtUtc,
        CancellationToken cancellationToken = default)
    {
        if (Throw)
            throw new InvalidOperationException("The observer is unavailable.");

        Observations.Add((state, observedAtUtc));
        return ValueTask.CompletedTask;
    }
}

internal static class CloudTestData
{
    public static readonly DateTime Now = new(2026, 11, 17, 9, 0, 0, DateTimeKind.Utc);

    public const string CloudBaseUrl = "https://cloud.example.com";
    public const string DesktopToken = "efd_test_desktop_token";

    public static CloudLink Linked(DateTime? nowUtc = null)
    {
        var now = nowUtc ?? Now;
        var link = CloudLink.CreateDefault(now);
        link.RecordConsent(now);
        link.BeginPairing(
            CloudBaseUrl,
            Guid.Parse("0f8fad5b-d9cb-469f-a165-70867728950e"),
            "ABCD2345",
            DesktopToken,
            now.AddMinutes(10),
            now);
        link.CompletePairing(
            Guid.Parse("7c9e6679-7425-40de-944b-e07fc1f90ae7"),
            Guid.Parse("3f2504e0-4f89-41d3-9a0c-0305e82c3301"),
            now,
            now);
        return link;
    }
}
