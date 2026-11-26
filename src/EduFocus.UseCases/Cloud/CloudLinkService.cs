using System.Text;
using System.Text.Json;
using EduFocus.Domain.Entities;
using EduFocus.Domain.Enums;
using EduFocus.Ports.DTOs;
using EduFocus.Ports.Inbound;
using EduFocus.Ports.Outbound.Cloud;
using EduFocus.Ports.Outbound.Persistence;
using EduFocus.Ports.Outbound.Services;

namespace EduFocus.UseCases.Cloud;

/// <summary>
/// Drives the opt-in cloud relay link (UC15, UC18, ADR-20). Cloud is disabled
/// by default: status reads stay local and no HTTP request is made until the
/// parent has consented, opened Parent Mode and started pairing.
/// </summary>
public sealed class CloudLinkService : ICloudLinkUseCase
{
    public const string ConsentVersion = "2.0";

    private const string CloudDataDeletionConfirmation = "XÓA";

    private readonly ICloudLinkRepository _links;
    private readonly ICloudOutboxRepository _outbox;
    private readonly ICloudSyncPort _sync;
    private readonly IParentAuthorizer _authorizer;
    private readonly IAuditLogRepository _auditLogs;
    private readonly ICorrelationContext _correlation;
    private readonly IAppLogger _logger;
    private readonly TimeProvider _timeProvider;

    // LAN demos use http://<lan-ip>:8080; release builds must stay on TLS.
    private static bool AllowInsecureDevelopmentEndpoints =>
#if DEBUG
        true;
#else
        false;
#endif

    public CloudLinkService(
        ICloudLinkRepository links,
        ICloudOutboxRepository outbox,
        ICloudSyncPort sync,
        IParentAuthorizer authorizer,
        IAuditLogRepository auditLogs,
        ICorrelationContext correlation,
        IAppLogger logger,
        TimeProvider timeProvider)
    {
        _links = links ?? throw new ArgumentNullException(nameof(links));
        _outbox = outbox ?? throw new ArgumentNullException(nameof(outbox));
        _sync = sync ?? throw new ArgumentNullException(nameof(sync));
        _authorizer = authorizer ?? throw new ArgumentNullException(nameof(authorizer));
        _auditLogs = auditLogs ?? throw new ArgumentNullException(nameof(auditLogs));
        _correlation = correlation ?? throw new ArgumentNullException(nameof(correlation));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
    }

    public async Task<CloudLinkStatusSnapshot> GetStatusAsync(CancellationToken cancellationToken = default)
    {
        var link = await _links.GetAsync(cancellationToken).ConfigureAwait(false);
        if (link is null)
            return EmptySnapshot();

        if (!link.IsEnabled || string.IsNullOrEmpty(link.DesktopToken) || !TryGetBaseUrl(link, out var baseUrl))
            return ToSnapshot(link, [], 0, remoteChecked: false);

        try
        {
            var remote = await RefreshAsync(link, baseUrl, cancellationToken).ConfigureAwait(false);
            return ToSnapshot(link, remote.Devices, remote.PendingSyncEvents, remoteChecked: true);
        }
        catch (CloudSyncException ex)
        {
            var now = UtcNow(link);

            if (ex.Code is CloudErrorCodes.Unauthorized or CloudErrorCodes.NotFound)
            {
                // The family or device was removed on the server (UC18): stop
                // syncing immediately and require pairing again.
                await ClearCloudOutboxAsync(cancellationToken).ConfigureAwait(false);
                link.Unlink(now);
                link.RecordError(
                    "Liên kết cloud đã bị thu hồi hoặc xóa trên máy chủ. Hãy ghép nối lại nếu vẫn muốn nhận thông báo.",
                    now);
                await _links.UpdateAsync(link, cancellationToken).ConfigureAwait(false);
                await WriteAuditAsync(
                        AuditActor.System,
                        AuditEventType.CloudLinkRevoked,
                        "Cloud server rejected the desktop token; the local link was cleared.",
                        now,
                        new { reason = ex.Code },
                        cancellationToken)
                    .ConfigureAwait(false);
            }
            else
            {
                await _links
                    .TryRecordErrorAsync(link.DesktopToken!, ex.Message, now, cancellationToken)
                    .ConfigureAwait(false);
                link.RecordError(ex.Message, now);
            }

            _logger.Warning(LogCategories.Cloud, $"Cloud status refresh failed with code '{ex.Code}'.");
            return ToSnapshot(link, [], 0, remoteChecked: false);
        }
    }

    public async Task AcceptConsentAsync(CancellationToken cancellationToken = default)
    {
        _authorizer.EnsureAuthorized("Accept cloud relay consent");

        var link = await _links.GetAsync(cancellationToken).ConfigureAwait(false);
        var now = link is null ? UtcNow() : UtcNow(link);

        if (link is null)
        {
            link = CloudLink.CreateDefault(now);
            link.RecordConsent(now);
            await _links.AddAsync(link, cancellationToken).ConfigureAwait(false);
        }
        else
        {
            link.RecordConsent(now);
            await _links.UpdateAsync(link, cancellationToken).ConfigureAwait(false);
        }

        await WriteAuditAsync(
                AuditActor.Parent,
                AuditEventType.CloudConsentRecorded,
                "Parent accepted the optional cloud relay consent.",
                now,
                new { version = ConsentVersion },
                cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task RevokeConsentAsync(CancellationToken cancellationToken = default)
    {
        _authorizer.EnsureAuthorized("Revoke cloud relay consent");

        var existing = await _links.GetAsync(cancellationToken).ConfigureAwait(false);
        if (existing is null)
            return;

        if (existing.IsEnabled)
        {
            // Keep both the link and the consent until the server confirms the
            // revoke; otherwise the token would be lost with no way to retry.
            await UnlinkAsync(deleteCloudData: false, cancellationToken).ConfigureAwait(false);
        }

        var link = await _links.GetAsync(cancellationToken).ConfigureAwait(false) ?? existing;
        var now = UtcNow(link);
        link.RevokeConsent(now);
        await _links.UpdateAsync(link, cancellationToken).ConfigureAwait(false);

        await WriteAuditAsync(
                AuditActor.Parent,
                AuditEventType.CloudConsentRevoked,
                "Parent revoked the optional cloud relay consent.",
                now,
                null,
                cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<CloudPairingInfo> EnableAsync(
        CloudLinkOptions options,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(options);
        _authorizer.EnsureAuthorized("Enable cloud relay");

        var link = await _links.GetAsync(cancellationToken).ConfigureAwait(false);
        if (link is null || !link.HasConsent)
        {
            throw new InvalidOperationException(
                "Bạn cần đọc và đồng ý chính sách cloud trước khi bật thông báo cho điện thoại.");
        }

        if (!CloudUrlPolicy.TryNormalize(
                options.CloudBaseUrl,
                AllowInsecureDevelopmentEndpoints,
                out var baseUrl,
                out var urlError))
        {
            throw new ArgumentException(urlError, nameof(options));
        }

        var desktopName = options.DesktopName?.Trim();
        if (string.IsNullOrWhiteSpace(desktopName) || desktopName.Length > 128)
            throw new ArgumentException("Tên thiết bị không hợp lệ.", nameof(options));

        var platform = string.IsNullOrWhiteSpace(options.Platform) ? "windows" : options.Platform.Trim();
        if (platform.Length > 32)
            throw new ArgumentException("Nền tảng không hợp lệ.", nameof(options));

        var appVersion = options.AppVersion?.Trim();
        if (appVersion is { Length: > 32 })
            throw new ArgumentException("Phiên bản ứng dụng không hợp lệ.", nameof(options));

        var now = UtcNow(link);
        if (link.HasActivePairing(now))
        {
            throw new InvalidOperationException(
                "Đang có phiên ghép nối còn hiệu lực. Hãy dùng mã hiện tại hoặc đợi phiên hết hạn.");
        }

        var request = new CloudPairingRequest(
            desktopName,
            platform,
            string.IsNullOrEmpty(appVersion) ? null : appVersion,
            ResolveTimeZoneId(options.TimeZoneId));

        var session = await _sync
            .CreatePairingSessionAsync(baseUrl!, request, cancellationToken)
            .ConfigureAwait(false);

        if (session.SessionId == Guid.Empty
            || string.IsNullOrWhiteSpace(session.Code)
            || string.IsNullOrWhiteSpace(session.DesktopToken)
            || session.ExpiresAtUtc <= now)
        {
            throw new CloudSyncException(
                CloudErrorCodes.InvalidResponse,
                "Máy chủ cloud trả về phiên ghép nối không hợp lệ.");
        }

        // A new pairing starts a new link; queued events belong to the old one
        // and must never be uploaded into a different family.
        await ClearCloudOutboxAsync(cancellationToken).ConfigureAwait(false);

        link.BeginPairing(
            baseUrl!.ToString().TrimEnd('/'),
            session.SessionId,
            session.Code,
            session.DesktopToken,
            session.ExpiresAtUtc,
            now);
        await _links.UpdateAsync(link, cancellationToken).ConfigureAwait(false);

        await WriteAuditAsync(
                AuditActor.Parent,
                AuditEventType.CloudPairingCodeCreated,
                "Parent started cloud pairing and a pairing code was issued.",
                now,
                new { sessionId = session.SessionId, expiresAtUtc = session.ExpiresAtUtc },
                cancellationToken)
            .ConfigureAwait(false);

        _logger.Info(
            LogCategories.Cloud,
            $"Cloud pairing session {session.SessionId:D} created for host '{baseUrl!.Host}'.");

        var qrPayload = string.IsNullOrWhiteSpace(session.PairingUri)
            ? PairingPayload(baseUrl!.ToString(), session.SessionId, session.Code)
            : session.PairingUri;

        return new CloudPairingInfo(
            session.SessionId,
            session.Code.Trim().ToUpperInvariant(),
            session.ExpiresAtUtc,
            qrPayload,
            baseUrl!.ToString().TrimEnd('/'));
    }

    public async Task UnlinkAsync(bool deleteCloudData, CancellationToken cancellationToken = default)
    {
        _authorizer.EnsureAuthorized("Unlink the cloud relay");

        var link = await _links.GetAsync(cancellationToken).ConfigureAwait(false);
        if (link is null)
            return;

        CloudSyncException? remoteFailure = null;
        var token = link.DesktopToken;

        if (link.IsEnabled && !string.IsNullOrEmpty(token) && TryGetBaseUrl(link, out var baseUrl))
        {
            try
            {
                if (deleteCloudData)
                {
                    await _sync.DeleteFamilyAsync(baseUrl, token, cancellationToken).ConfigureAwait(false);
                }
                else if (link.DesktopDeviceId is { } deviceId)
                {
                    await _sync.RevokeDeviceAsync(baseUrl, token, deviceId, cancellationToken).ConfigureAwait(false);
                }
            }
            catch (CloudSyncException ex) when (ex.Code == CloudErrorCodes.NotFound || ex.StatusCode == 404)
            {
                // The family or device is already gone on the server.
            }
            catch (CloudSyncException ex)
            {
                remoteFailure = ex;
            }
        }

        var now = UtcNow(link);
        if (remoteFailure is not null)
        {
            // Keep the token and identifiers so the parent can retry the remote
            // revoke or deletion later; only a confirmed unlink is local-only.
            link.RecordError(
                "Chưa thu hồi được liên kết trên máy chủ cloud. Liên kết vẫn được giữ trên máy này; hãy thử lại khi có mạng.",
                now);
            await _links.UpdateAsync(link, cancellationToken).ConfigureAwait(false);

            await WriteAuditAsync(
                    AuditActor.Parent,
                    deleteCloudData ? AuditEventType.CloudDataDeletionRequested : AuditEventType.CloudLinkRevoked,
                    deleteCloudData
                        ? "Parent requested deletion of all cloud relay data but the server did not confirm."
                        : "Parent requested unlinking but the server did not confirm.",
                    now,
                    new { remoteConfirmed = false, deleteCloudData, code = remoteFailure.Code },
                    cancellationToken)
                .ConfigureAwait(false);

            _logger.Warning(
                LogCategories.Cloud,
                $"Cloud unlink failed ('{remoteFailure.Code}'); the link and its outbox were kept for retry.");

            throw new CloudSyncException(
                remoteFailure.Code,
                deleteCloudData
                    ? "Chưa xóa được dữ liệu trên máy chủ cloud. Liên kết vẫn được giữ trên máy này; hãy thử lại khi có mạng."
                    : "Chưa thu hồi được token trên máy chủ cloud. Liên kết vẫn được giữ trên máy này; hãy thử lại khi có mạng.",
                remoteFailure.IsNetworkError,
                remoteFailure.StatusCode);
        }

        await ClearCloudOutboxAsync(cancellationToken).ConfigureAwait(false);

        link.Unlink(now);
        await _links.UpdateAsync(link, cancellationToken).ConfigureAwait(false);

        await WriteAuditAsync(
                AuditActor.Parent,
                deleteCloudData ? AuditEventType.CloudDataDeletionRequested : AuditEventType.CloudLinkRevoked,
                deleteCloudData
                    ? "Parent requested deletion of all cloud relay data."
                    : "Parent unlinked the cloud relay from this desktop.",
                now,
                new { remoteConfirmed = true, confirmation = deleteCloudData ? CloudDataDeletionConfirmation : null },
                cancellationToken)
            .ConfigureAwait(false);

        _logger.Info(
            LogCategories.Cloud,
            deleteCloudData
                ? "Cloud data deletion confirmed; the local link and outbox were cleared."
                : "Cloud link removed and its outbox cleared.");
    }

    private async Task<RemoteSnapshot> RefreshAsync(
        CloudLink link,
        Uri baseUrl,
        CancellationToken cancellationToken)
    {
        var now = UtcNow(link);
        var token = link.DesktopToken!;

        if (link.PairingSessionId is { } sessionId)
        {
            var status = await _sync
                .GetPairingSessionStatusAsync(baseUrl, sessionId, token, cancellationToken)
                .ConfigureAwait(false);

            switch (status.Status)
            {
                case CloudPairingStatus.Claimed:
                {
                    var remote = await _sync
                        .GetLinkStatusAsync(baseUrl, token, cancellationToken)
                        .ConfigureAwait(false);

                    if (!remote.Linked
                        || remote.FamilyId is not { } familyId
                        || remote.DeviceId is not { } deviceId)
                    {
                        throw new CloudSyncException(
                            CloudErrorCodes.InvalidResponse,
                            "Máy chủ cloud trả về liên kết không hợp lệ.");
                    }

                    link.CompletePairing(familyId, deviceId, status.ClaimedAtUtc ?? remote.LinkedAtUtc ?? now, now);
                    if (remote.LastSyncAtUtc is { } firstSync)
                        link.RecordSync(firstSync, now);

                    await _links.UpdateAsync(link, cancellationToken).ConfigureAwait(false);
                    await WriteAuditAsync(
                            AuditActor.Parent,
                            AuditEventType.CloudLinkEnabled,
                            "Parent paired a phone with the optional cloud relay.",
                            now,
                            new { mobileDeviceName = status.MobileDeviceName },
                            cancellationToken)
                        .ConfigureAwait(false);

                    return new RemoteSnapshot(remote.Devices.Select(ToDto).ToList(), remote.PendingSyncEvents);
                }

                case CloudPairingStatus.Expired:
                case CloudPairingStatus.Cancelled:
                    await ClearCloudOutboxAsync(cancellationToken).ConfigureAwait(false);
                    link.Unlink(now);
                    await _links.UpdateAsync(link, cancellationToken).ConfigureAwait(false);
                    return RemoteSnapshot.Empty;

                default:
                    return RemoteSnapshot.Empty;
            }
        }

        if (!link.IsLinked)
            return RemoteSnapshot.Empty;

        var linkStatus = await _sync
            .GetLinkStatusAsync(baseUrl, token, cancellationToken)
            .ConfigureAwait(false);

        if (!linkStatus.Linked)
        {
            await ClearCloudOutboxAsync(cancellationToken).ConfigureAwait(false);
            link.Unlink(now);
            await _links.UpdateAsync(link, cancellationToken).ConfigureAwait(false);
            await WriteAuditAsync(
                    AuditActor.System,
                    AuditEventType.CloudLinkRevoked,
                    "The cloud relay no longer lists this desktop; the local link was cleared.",
                    now,
                    null,
                    cancellationToken)
                .ConfigureAwait(false);
            return RemoteSnapshot.Empty;
        }

        if (linkStatus.LastSyncAtUtc is { } lastSync && link.LastSyncAtUtc != lastSync)
        {
            await _links
                .TryRecordSyncAsync(link.DesktopToken!, lastSync, now, cancellationToken)
                .ConfigureAwait(false);
            link.RecordSync(lastSync, now);
        }
        else if (link.LastError is not null)
        {
            await _links
                .TryRecordErrorAsync(link.DesktopToken!, null, now, cancellationToken)
                .ConfigureAwait(false);
            link.RecordError(null, now);
        }

        return new RemoteSnapshot(linkStatus.Devices.Select(ToDto).ToList(), linkStatus.PendingSyncEvents);
    }

    private CloudLinkStatusSnapshot ToSnapshot(
        CloudLink link,
        IReadOnlyList<CloudLinkedDevice> devices,
        int pendingSyncEvents,
        bool remoteChecked)
    {
        return new CloudLinkStatusSnapshot(
            link.IsEnabled,
            link.HasConsent,
            link.ConsentAcceptedAtUtc,
            link.CloudBaseUrl,
            link.IsLinked,
            link.FamilyId,
            link.DesktopDeviceId,
            link.LinkedAtUtc,
            link.LastSyncAtUtc,
            link.LastError,
            GetActivePairing(link),
            devices,
            pendingSyncEvents,
            remoteChecked);
    }

    private static CloudLinkStatusSnapshot EmptySnapshot() =>
        new(
            false,
            false,
            null,
            null,
            false,
            null,
            null,
            null,
            null,
            null,
            null,
            [],
            0,
            false);

    private CloudPairingInfo? GetActivePairing(CloudLink link)
    {
        if (!link.HasActivePairing(UtcNow(link))
            || link.PairingSessionId is not { } sessionId
            || link.PairingCode is not { } code
            || link.PairingExpiresAtUtc is not { } expiresAtUtc
            || string.IsNullOrWhiteSpace(link.CloudBaseUrl))
        {
            return null;
        }

        return new CloudPairingInfo(
            sessionId,
            code,
            expiresAtUtc,
            PairingPayload(link.CloudBaseUrl, sessionId, code),
            link.CloudBaseUrl);
    }

    private string ResolveTimeZoneId(string? requested)
    {
        if (!string.IsNullOrWhiteSpace(requested))
            return requested.Trim();

        var local = _timeProvider.LocalTimeZone;
        return TimeZoneInfo.TryConvertWindowsIdToIanaId(local.Id, out var iana)
            && !string.IsNullOrWhiteSpace(iana)
                ? iana
                : local.Id;
    }

    private static string PairingPayload(string cloudBaseUrl, Guid sessionId, string code)
    {
        var encoded = Convert.ToBase64String(Encoding.UTF8.GetBytes(cloudBaseUrl.TrimEnd('/')))
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');

        return $"edufocus://pair?v=1&u={encoded}&s={sessionId:D}&c={code}";
    }

    private static bool TryGetBaseUrl(CloudLink link, out Uri baseUrl)
    {
        if (CloudUrlPolicy.TryNormalize(
                link.CloudBaseUrl,
                AllowInsecureDevelopmentEndpoints,
                out var normalized,
                out _)
            && normalized is not null)
        {
            baseUrl = normalized;
            return true;
        }

        baseUrl = null!;
        return false;
    }

    private static CloudLinkedDevice ToDto(CloudRemoteDevice device) =>
        new(device.Id, device.Kind, device.Name, device.Platform, device.IsRevoked);

    private async Task ClearCloudOutboxAsync(CancellationToken cancellationToken)
    {
        var removed = await _outbox.ClearAllAsync(cancellationToken).ConfigureAwait(false);
        if (removed > 0)
        {
            _logger.Info(LogCategories.Cloud, $"Cloud outbox cleared with {removed} event(s).");
        }
    }

    private Task WriteAuditAsync(
        AuditActor actor,
        AuditEventType eventType,
        string description,
        DateTime occurredAtUtc,
        object? metadata,
        CancellationToken cancellationToken)
    {
        var metadataJson = metadata is null ? null : JsonSerializer.Serialize(metadata);

        return _auditLogs.AddAsync(
            AuditLog.Record(
                actor,
                eventType,
                description,
                occurredAtUtc,
                metadataJson,
                _correlation.Current),
            cancellationToken);
    }

    private DateTime UtcNow() => _timeProvider.GetUtcNow().UtcDateTime;

    private DateTime UtcNow(CloudLink link)
    {
        var now = UtcNow();
        return now < link.UpdatedAtUtc ? link.UpdatedAtUtc : now;
    }

    private sealed record RemoteSnapshot(IReadOnlyList<CloudLinkedDevice> Devices, int PendingSyncEvents)
    {
        public static readonly RemoteSnapshot Empty = new([], 0);
    }
}
