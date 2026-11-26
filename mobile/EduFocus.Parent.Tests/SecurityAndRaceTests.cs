using EduFocus.Cloud.Contracts.Notifications;
using EduFocus.Cloud.Contracts.Pairing;
using EduFocus.Cloud.Contracts.Preferences;
using EduFocus.Cloud.Contracts.Push;
using EduFocus.Cloud.Contracts.Reports;
using EduFocus.Parent.Core;
using EduFocus.Parent.Core.Pairing;
using EduFocus.Parent.Core.Presentation;
using EduFocus.Parent.Core.Push;
using EduFocus.Parent.Core.Services;
using EduFocus.Parent.Core.Settings;

namespace EduFocus.Parent.Tests;

public sealed class SecurityAndRaceTests
{
    [Fact]
    public async Task Concurrent_unauthorized_calls_wait_until_local_state_is_cleared_and_fire_expired_once()
    {
        var kv = new MemoryKeyValueStore();
        var settings = new ParentSettingsStore(kv);
        var cache = new ParentDataCache(kv);
        var state = new AppState { IsLinked = true, FamilyId = Guid.NewGuid(), DeviceId = Guid.NewGuid() };
        var releaseClear = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var tokens = new BlockingTokenStore(releaseClear.Task);
        var expiry = new SessionExpiryService(tokens, state, settings, cache);
        var expiredEvents = 0;
        expiry.Expired += () => Interlocked.Increment(ref expiredEvents);

        var first = expiry.HandleUnauthorizedAsync();
        var second = expiry.HandleUnauthorizedAsync();

        Assert.False(second.IsCompleted);
        releaseClear.SetResult();
        await Task.WhenAll(first, second);

        Assert.False(state.IsLinked);
        Assert.Equal(1, expiredEvents);
        Assert.Equal(1, tokens.ClearCount);
    }

    [Fact]
    public async Task Session_expiry_resets_if_clear_throws_so_subsequent_retry_succeeds()
    {
        var kv = new MemoryKeyValueStore();
        var settings = new ParentSettingsStore(kv);
        var cache = new ParentDataCache(kv);
        var state = new AppState { IsLinked = true };
        var tokens = new FlakyTokenStore();
        var expiry = new SessionExpiryService(tokens, state, settings, cache);

        await Assert.ThrowsAsync<InvalidOperationException>(() => expiry.HandleUnauthorizedAsync());
        await expiry.HandleUnauthorizedAsync();

        Assert.False(state.IsLinked);
        Assert.Equal(2, tokens.ClearCount);
    }

    [Fact]
    public async Task Push_config_service_triggers_session_expiry_on_401()
    {
        var kv = new MemoryKeyValueStore();
        var settings = new ParentSettingsStore(kv);
        var cache = new ParentDataCache(kv);
        var state = new AppState { IsLinked = true };
        var tokens = new BlockingTokenStore(Task.CompletedTask);
        var expiry = new SessionExpiryService(tokens, state, settings, cache);
        var api = new StubCloudApiClient
        {
            GetHandler = _ => throw new CloudApiException(401, "unauthorized", "Unauthorized"),
        };
        var service = new PushConfigService(api, expiry);

        var result = await service.GetAsync();

        Assert.Null(result);
        Assert.False(state.IsLinked);
    }

    [Fact]
    public async Task Claiming_new_pairing_resets_stale_push_websocket_flag()
    {
        var kv = new MemoryKeyValueStore();
        var settings = new ParentSettingsStore(kv) { PushWebSocketEnabled = true };
        var cache = new ParentDataCache(kv);
        var state = new AppState();
        var tokens = new BlockingTokenStore(Task.CompletedTask);
        var familyId = Guid.NewGuid();
        var deviceId = Guid.NewGuid();
        var api = new StubCloudApiClient
        {
            PostHandler = (_, _, _) => Task.FromResult<object?>(new ClaimPairingResponse(
                "efm_new_token",
                familyId,
                deviceId,
                "New PC",
                "Asia/Ho_Chi_Minh",
                DateTimeOffset.UtcNow)),
        };
        var pairing = new PairingService(api, tokens, state, settings, cache, allowInsecureEndpoints: false);

        await pairing.ClaimAsync(
            new PairingClaim("ABCD-1234", "https://cloud.example.com", null),
            new MobileDeviceIdentity("Phone", "android"),
            replaceExisting: false);

        Assert.False(settings.PushWebSocketEnabled);
        Assert.Equal(familyId, state.FamilyId);
    }

    [Fact]
    public async Task In_flight_overview_and_reports_and_notifications_ignore_responses_from_previous_family()
    {
        var kv = new MemoryKeyValueStore();
        var settings = new ParentSettingsStore(kv);
        var cache = new ParentDataCache(kv);
        var familyA = Guid.NewGuid();
        var familyB = Guid.NewGuid();
        var state = new AppState { IsLinked = true, FamilyId = familyA, TimeZone = "UTC" };
        var expiry = new SessionExpiryService(new BlockingTokenStore(Task.CompletedTask), state, settings, cache);

        var releaseDaily = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseNotifications = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var today = new DateOnly(2026, 11, 26);
        var data = new StubParentDataService(
            async () =>
            {
                await releaseDaily.Task;
                return [new DailySummaryDto(today, "UTC", DateTimeOffset.UtcNow, 1, 100, 100, 0, 0, 100, 100, 90, 10, 0, 0, 0, 90.0, 100.0)];
            },
            async () =>
            {
                await releaseNotifications.Task;
                return new NotificationListResponse(
                    [new NotificationDto(Guid.NewGuid(), NotificationKind.SecurityAlert, NotificationDeliveryStatus.Sent, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow)],
                    null);
            });

        var overview = new OverviewViewModel(data, cache, state, expiry, () => new DateTimeOffset(2026, 11, 26, 12, 0, 0, TimeSpan.Zero));
        var reports = new ReportsViewModel(data, cache, state, expiry, () => new DateTimeOffset(2026, 11, 26, 12, 0, 0, TimeSpan.Zero));
        var notifications = new NotificationsViewModel(data, cache, state, expiry);

        var t1 = overview.LoadAsync();
        var t2 = reports.LoadAsync();
        var t3 = notifications.LoadAsync();

        state.FamilyId = familyB;
        releaseDaily.SetResult();
        releaseNotifications.SetResult();
        await Task.WhenAll(t1, t2, t3);

        Assert.False(cache.TryGetDaily(out _, out _));
        Assert.False(cache.TryGetNotifications(out _, out _));
        Assert.False(overview.HasSummary);
        Assert.Empty(reports.Days);
        Assert.Empty(notifications.Items);
    }

    [Fact]
    public async Task Clearing_push_app_version_on_endpoint_change_allows_retry_with_force_false()
    {
        var kv = new MemoryKeyValueStore();
        var settings = new ParentSettingsStore(kv);
        var state = new AppState { IsLinked = true };
        var posts = 0;
        var api = new StubCloudApiClient
        {
            PostHandler = (_, _, _) =>
            {
                posts++;
                return Task.FromResult<object?>(new RegisterPushResponse(DateTimeOffset.UtcNow));
            },
        };
        const string validP256dh = "BAne4XPEmYu3KTOfxo4SXY2xZ3fRLQQ6QANsm9tBChPE2BifuZZqNZJIGf_kWIzIeGXB95zCJvAHNjR3-sOVZaw";
        var service = new PushRegistrationService(api, state, settings, delay: (_, _) => Task.CompletedTask);
        var first = new PushEndpointRegistration("https://push.example/1", validP256dh, "AAAAAAAAAAAAAAAAAAAAAA");
        Assert.True(await service.RegisterAsync(first, "android", "2.1.0"));
        Assert.Equal(1, posts);

        // Simulate PushCoordinator.OnEndpointChanged saving rotated endpoint and invalidating cached PushAppVersion.
        var rotated = new PushEndpointRegistration("https://push.example/2", validP256dh, "BBBBBBBBBBBBBBBBBBBBBB");
        settings.SetPushEndpoint(rotated);
        settings.PushAppVersion = null;

        Assert.True(await service.RegisterAsync(settings.GetPushEndpoint(), "android", "2.1.0", force: false));
        Assert.Equal(2, posts);
    }

    [Fact]
    public void Push_event_hub_delivers_payload_and_isolates_subscriber_exceptions()
    {
        var hub = new PushEventHub();
        PushNotificationPayload? received = null;
        hub.Received += _ => throw new InvalidOperationException("ui disposed");
        hub.Received += payload => received = payload;

        var expected = new PushNotificationPayload(
            PushNotificationType.AttentionAlert,
            "attention_alert",
            new DateOnly(2026, 11, 26),
            "notif-1");
        hub.Publish(expected);

        Assert.Equal(expected, received);
    }

    private sealed class MemoryKeyValueStore : IKeyValueStore
    {
        private readonly Dictionary<string, string> _values = new(StringComparer.Ordinal);

        public string? Get(string key) => _values.TryGetValue(key, out var value) ? value : null;
        public void Set(string key, string value) => _values[key] = value;
        public void Remove(string key) => _values.Remove(key);
    }

    private sealed class BlockingTokenStore(Task clearGate) : ITokenStore
    {
        public int ClearCount { get; private set; }
        public Task<string?> GetTokenAsync(CancellationToken cancellationToken = default) => Task.FromResult<string?>("efm_token");
        public Task SaveTokenAsync(string token, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public async Task ClearAsync(CancellationToken cancellationToken = default)
        {
            ClearCount++;
            await clearGate;
        }
    }

    private sealed class FlakyTokenStore : ITokenStore
    {
        public int ClearCount { get; private set; }
        public Task<string?> GetTokenAsync(CancellationToken cancellationToken = default) => Task.FromResult<string?>("efm_token");
        public Task SaveTokenAsync(string token, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task ClearAsync(CancellationToken cancellationToken = default)
        {
            ClearCount++;
            if (ClearCount == 1)
                throw new InvalidOperationException("transient keystore error");
            return Task.CompletedTask;
        }
    }

    private sealed class StubCloudApiClient : ICloudApiClient
    {
        public Func<string, object?>? GetHandler { get; init; }
        public Func<string, object?, CancellationToken, Task<object?>>? PostHandler { get; init; }

        public Task<T> GetAsync<T>(string relativePath, bool authenticated = true, CancellationToken cancellationToken = default) =>
            Task.FromResult((T)(GetHandler?.Invoke(relativePath) ?? throw new NotSupportedException()));

        public Task<TResponse> PostAsync<TRequest, TResponse>(string relativePath, TRequest body, bool authenticated = true, CancellationToken cancellationToken = default) =>
            PostAsyncCore<TResponse>(relativePath, body, cancellationToken);

        public Task<TResponse> PostAsync<TRequest, TResponse>(string relativePath, TRequest body, Uri cloudBaseUrl, bool authenticated = false, CancellationToken cancellationToken = default) =>
            PostAsyncCore<TResponse>(relativePath, body, cancellationToken);

        public Task PostAsync<TRequest>(string relativePath, TRequest body, bool authenticated = true, CancellationToken cancellationToken = default) =>
            PostAsyncCore<object>(relativePath, body, cancellationToken);

        public Task<TResponse> PutAsync<TRequest, TResponse>(string relativePath, TRequest body, bool authenticated = true, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task DeleteAsync(string relativePath, bool authenticated = true, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;

        public Task DeleteAsync(string relativePath, Uri cloudBaseUrl, string bearerToken, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;

        public Task DeleteAsync<TRequest>(string relativePath, TRequest body, bool authenticated = true, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;

        private async Task<TResponse> PostAsyncCore<TResponse>(string relativePath, object? body, CancellationToken cancellationToken)
        {
            if (PostHandler is null)
                throw new NotSupportedException();
            var result = await PostHandler(relativePath, body, cancellationToken);
            return (TResponse)result!;
        }
    }

    private sealed class StubParentDataService(
        Func<Task<IReadOnlyList<DailySummaryDto>>> getDaily,
        Func<Task<NotificationListResponse>> getNotifications) : IParentDataService
    {
        public Task<IReadOnlyList<DailySummaryDto>> GetDailyAsync(DateOnly from, DateOnly to, CancellationToken cancellationToken = default) =>
            getDaily();

        public Task<NotificationListResponse> GetNotificationsAsync(int limit, string? before, CancellationToken cancellationToken = default) =>
            getNotifications();

        public Task<NotificationPreferencesDto> GetPreferencesAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(new NotificationPreferencesDto(true, true, true, true, 1320, 360, 1230, "UTC", true, true));

        public Task<NotificationPreferencesDto> UpdatePreferencesAsync(UpdateNotificationPreferencesRequest request, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }
}
