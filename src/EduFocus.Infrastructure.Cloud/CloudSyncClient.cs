using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using EduFocus.Cloud.Contracts.Common;
using EduFocus.Cloud.Contracts.Family;
using EduFocus.Cloud.Contracts.Json;
using EduFocus.Cloud.Contracts.Pairing;
using EduFocus.Cloud.Contracts.Sync;
using EduFocus.Ports.Outbound.Cloud;

namespace EduFocus.Infrastructure.Cloud;

/// <summary>
/// HTTP adapter for the optional cloud relay (issue #86). Tokens travel only
/// in the Authorization header and never appear in logs, error messages or
/// exception text; responses are mapped to safe port models.
/// </summary>
public sealed class CloudSyncClient : ICloudSyncPort
{
    public static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(15);

    private const string FamilyDeletionConfirmation = "XÓA";

    private readonly HttpClient _httpClient;

    public CloudSyncClient(HttpClient httpClient, string? appVersion = null)
    {
        ArgumentNullException.ThrowIfNull(httpClient);
        _httpClient = httpClient;
        _httpClient.Timeout = RequestTimeout;

        var version = string.IsNullOrWhiteSpace(appVersion) ? "1.0.0" : appVersion.Trim();
        _httpClient.DefaultRequestHeaders.UserAgent.Clear();
        if (!ProductInfoHeaderValue.TryParse($"EduFocus/{version}", out var userAgent))
            userAgent = new ProductInfoHeaderValue("EduFocus", "1.0.0");
        _httpClient.DefaultRequestHeaders.UserAgent.Add(userAgent);
    }

    public async Task<CloudPairingSession> CreatePairingSessionAsync(
        Uri cloudBaseUrl,
        CloudPairingRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(cloudBaseUrl);
        ArgumentNullException.ThrowIfNull(request);

        var payload = new CreatePairingSessionRequest(
            request.DesktopName,
            request.Platform,
            request.AppVersion,
            request.TimeZoneId);

        using var response = await SendAsync(
                HttpMethod.Post,
                BuildUri(cloudBaseUrl, "v1/pairing/sessions"),
                payload,
                desktopToken: null,
                cancellationToken)
            .ConfigureAwait(false);
        var dto = await ReadAsync<PairingSessionResponse>(response, cancellationToken).ConfigureAwait(false);

        if (string.IsNullOrWhiteSpace(dto.DesktopToken))
        {
            throw new CloudSyncException(
                CloudErrorCodes.InvalidResponse,
                "Máy chủ cloud không trả về token thiết bị.");
        }

        return new CloudPairingSession(
            dto.SessionId,
            dto.Code,
            dto.PairingUri,
            dto.ExpiresAtUtc.UtcDateTime,
            dto.DesktopToken,
            dto.CloudBaseUrl);
    }

    public async Task<CloudPairingSessionStatus> GetPairingSessionStatusAsync(
        Uri cloudBaseUrl,
        Guid sessionId,
        string desktopToken,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(cloudBaseUrl);
        ArgumentException.ThrowIfNullOrWhiteSpace(desktopToken);

        using var response = await SendAsync(
                HttpMethod.Get,
                BuildUri(cloudBaseUrl, $"v1/pairing/sessions/{sessionId:D}"),
                body: null,
                desktopToken,
                cancellationToken)
            .ConfigureAwait(false);
        var dto = await ReadAsync<PairingSessionStatusResponse>(response, cancellationToken).ConfigureAwait(false);

        var status = dto.Status switch
        {
            PairingSessionStatus.Pending => CloudPairingStatus.Pending,
            PairingSessionStatus.Claimed => CloudPairingStatus.Claimed,
            PairingSessionStatus.Expired => CloudPairingStatus.Expired,
            PairingSessionStatus.Cancelled => CloudPairingStatus.Cancelled,
            _ => throw new CloudSyncException(
                CloudErrorCodes.InvalidResponse,
                "Máy chủ cloud trả về trạng thái ghép nối không hợp lệ."),
        };

        return new CloudPairingSessionStatus(
            dto.SessionId,
            status,
            dto.ExpiresAtUtc.UtcDateTime,
            dto.ClaimedAtUtc?.UtcDateTime,
            dto.MobileDeviceName,
            dto.MobileDeviceId);
    }

    public async Task<CloudRemoteLinkStatus> GetLinkStatusAsync(
        Uri cloudBaseUrl,
        string desktopToken,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(cloudBaseUrl);
        ArgumentException.ThrowIfNullOrWhiteSpace(desktopToken);

        using var response = await SendAsync(
                HttpMethod.Get,
                BuildUri(cloudBaseUrl, "v1/link/status"),
                body: null,
                desktopToken,
                cancellationToken)
            .ConfigureAwait(false);
        var dto = await ReadAsync<LinkStatusResponse>(response, cancellationToken).ConfigureAwait(false);

        return new CloudRemoteLinkStatus(
            dto.Linked,
            dto.FamilyId,
            dto.DeviceId,
            dto.TimeZoneId,
            dto.LinkedAtUtc?.UtcDateTime,
            dto.LastSyncAtUtc?.UtcDateTime,
            dto.PendingSyncEvents,
            dto.Devices
                .Select(device => new CloudRemoteDevice(
                    device.Id,
                    device.Kind,
                    device.Name,
                    device.Platform,
                    device.CreatedAtUtc.UtcDateTime,
                    device.LastSeenAtUtc?.UtcDateTime,
                    device.RevokedAtUtc is not null))
                .ToList());
    }

    public async Task<CloudSyncBatchResult> SendEventsAsync(
        Uri cloudBaseUrl,
        string desktopToken,
        IReadOnlyList<CloudSyncEvent> events,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(cloudBaseUrl);
        ArgumentException.ThrowIfNullOrWhiteSpace(desktopToken);
        ArgumentNullException.ThrowIfNull(events);
        if (events.Count == 0)
            throw new ArgumentException("At least one event is required.", nameof(events));

        var batch = new SyncBatchRequest(events
            .Select(item => new SyncEventDto(
                item.ClientEventId,
                MapEventType(item.Type),
                item.OccurredAtUtc,
                item.Payload))
            .ToList());

        using var response = await SendAsync(
                HttpMethod.Post,
                BuildUri(cloudBaseUrl, "v1/sync/events"),
                batch,
                desktopToken,
                cancellationToken)
            .ConfigureAwait(false);
        var dto = await ReadAsync<SyncBatchResponse>(response, cancellationToken).ConfigureAwait(false);

        var results = dto.Results
            .Select(result => new CloudSyncEventResult(
                result.ClientEventId,
                result.Status switch
                {
                    SyncEventResultStatus.Applied => CloudEventOutcome.Applied,
                    SyncEventResultStatus.Duplicate => CloudEventOutcome.Duplicate,
                    SyncEventResultStatus.Rejected => CloudEventOutcome.Rejected,
                    _ => throw new CloudSyncException(
                        CloudErrorCodes.InvalidResponse,
                        "Máy chủ cloud trả về kết quả đồng bộ không hợp lệ."),
                },
                result.Code,
                result.Message))
            .ToList();

        return new CloudSyncBatchResult(results, dto.ServerTimeUtc);
    }

    public async Task RevokeDeviceAsync(
        Uri cloudBaseUrl,
        string desktopToken,
        Guid deviceId,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(cloudBaseUrl);
        ArgumentException.ThrowIfNullOrWhiteSpace(desktopToken);
        if (deviceId == Guid.Empty)
            throw new ArgumentException("Device id is required.", nameof(deviceId));

        using var response = await SendAsync(
                HttpMethod.Delete,
                BuildUri(cloudBaseUrl, $"v1/devices/{deviceId:D}"),
                body: null,
                desktopToken,
                cancellationToken)
            .ConfigureAwait(false);
        await ReadOperationAsync(response, cancellationToken).ConfigureAwait(false);
    }

    public async Task DeleteFamilyAsync(
        Uri cloudBaseUrl,
        string desktopToken,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(cloudBaseUrl);
        ArgumentException.ThrowIfNullOrWhiteSpace(desktopToken);

        using var response = await SendAsync(
                HttpMethod.Delete,
                BuildUri(cloudBaseUrl, "v1/family"),
                new DeleteFamilyRequest(FamilyDeletionConfirmation),
                desktopToken,
                cancellationToken)
            .ConfigureAwait(false);
        await ReadOperationAsync(response, cancellationToken).ConfigureAwait(false);
    }

    private async Task<HttpResponseMessage> SendAsync(
        HttpMethod method,
        Uri uri,
        object? body,
        string? desktopToken,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(method, uri);

        if (!string.IsNullOrEmpty(desktopToken))
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", desktopToken);

        if (body is not null)
            request.Content = JsonContent.Create(body, options: ApiJson.Options);

        try
        {
            return await _httpClient
                .SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new CloudSyncException(
                CloudErrorCodes.Timeout,
                "Máy chủ cloud không phản hồi kịp. Hãy kiểm tra kết nối và thử lại.",
                isNetworkError: true);
        }
        catch (HttpRequestException)
        {
            throw new CloudSyncException(
                CloudErrorCodes.Network,
                "Không kết nối được tới máy chủ cloud. Hãy kiểm tra kết nối mạng.",
                isNetworkError: true);
        }
    }

    private static async Task<T> ReadAsync<T>(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        if (!response.IsSuccessStatusCode)
            throw await CreateErrorAsync(response, cancellationToken).ConfigureAwait(false);

        try
        {
            var payload = await response.Content
                .ReadFromJsonAsync<T>(ApiJson.Options, cancellationToken)
                .ConfigureAwait(false);

            return payload ?? throw InvalidResponse();
        }
        catch (Exception ex) when (ex is JsonException or NotSupportedException)
        {
            throw InvalidResponse();
        }
    }

    private static async Task ReadOperationAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        var result = await ReadAsync<OperationResponse>(response, cancellationToken).ConfigureAwait(false);
        if (!result.Success)
            throw InvalidResponse();
    }

    private static async Task<CloudSyncException> CreateErrorAsync(
        HttpResponseMessage response,
        CancellationToken cancellationToken)
    {
        var statusCode = (int)response.StatusCode;

        var code = statusCode switch
        {
            401 or 403 => CloudErrorCodes.Unauthorized,
            404 => CloudErrorCodes.NotFound,
            408 => CloudErrorCodes.Timeout,
            429 => CloudErrorCodes.RateLimited,
            >= 500 => CloudErrorCodes.ServerError,
            _ => CloudErrorCodes.RequestFailed,
        };

        var message = statusCode switch
        {
            401 or 403 => "Máy chủ cloud từ chối token của thiết bị này.",
            404 => "Máy chủ cloud không tìm thấy dữ liệu liên kết.",
            429 => "Máy chủ cloud đang giới hạn tần suất. Sẽ tự thử lại sau.",
            >= 500 => "Máy chủ cloud đang gặp sự cố. Hãy thử lại sau.",
            _ => "Yêu cầu tới máy chủ cloud không hợp lệ.",
        };

        TimeSpan? retryAfter = null;
        if (statusCode == 429)
        {
            var header = response.Headers.RetryAfter;
            if (header?.Delta is { } delta && delta > TimeSpan.Zero)
                retryAfter = delta;
            else if (header?.Date is { } date)
            {
                var until = date - DateTimeOffset.UtcNow;
                if (until > TimeSpan.Zero)
                    retryAfter = until;
            }
        }

        try
        {
            var error = await response.Content
                .ReadFromJsonAsync<ApiErrorResponse>(ApiJson.Options, cancellationToken)
                .ConfigureAwait(false);

            if (!string.IsNullOrWhiteSpace(error?.Error?.Code))
                code = error.Error.Code.Trim();

            if (statusCode < 500 && statusCode != 429 && !string.IsNullOrWhiteSpace(error?.Error?.Message))
                message = error.Error.Message.Trim();
        }
        catch (Exception ex) when (ex is JsonException or NotSupportedException or HttpRequestException)
        {
        }

        return new CloudSyncException(code, message, isNetworkError: false, statusCode: statusCode, retryAfter);
    }

    private static CloudSyncException InvalidResponse() =>
        new(
            CloudErrorCodes.InvalidResponse,
            "Máy chủ cloud trả về dữ liệu không hợp lệ.");

    private static SyncEventType MapEventType(string type) => type switch
    {
        CloudEventTypes.DailySummary => SyncEventType.DailySummary,
        CloudEventTypes.AttentionAlert => SyncEventType.AttentionAlert,
        CloudEventTypes.ProtectionOff => SyncEventType.ProtectionOff,
        CloudEventTypes.SecurityAlert => SyncEventType.SecurityAlert,
        _ => throw new ArgumentException("Unsupported cloud event type.", nameof(type)),
    };

    private static Uri BuildUri(Uri cloudBaseUrl, string relativePath)
    {
        var root = new Uri(cloudBaseUrl.GetLeftPart(UriPartial.Authority).TrimEnd('/') + "/");
        return new Uri(root, relativePath);
    }
}
