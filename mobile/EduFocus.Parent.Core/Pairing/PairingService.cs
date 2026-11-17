using EduFocus.Cloud.Contracts.Pairing;
using EduFocus.Parent.Core.Services;
using EduFocus.Parent.Core.Settings;

namespace EduFocus.Parent.Core.Pairing;

public sealed record MobileDeviceIdentity(
    string DeviceName,
    string Platform,
    string? AppVersion = null,
    string? TimeZoneId = null);

public enum PairingFailure
{
    InvalidCode,
    RateLimited,
    Network,
    Server,
    Configuration,
}

public sealed class PairingException : Exception
{
    public PairingException(string message, PairingFailure failure, TimeSpan? retryAfter = null)
        : base(message)
    {
        Failure = failure;
        RetryAfter = retryAfter;
    }

    public PairingFailure Failure { get; }

    public TimeSpan? RetryAfter { get; }
}

public sealed record PairingClaim(string Code, string CloudBaseUrl, Guid? SessionId);

public interface IPairingService
{
    Task<ClaimPairingResponse> ClaimAsync(
        PairingClaim claim,
        MobileDeviceIdentity identity,
        bool replaceExisting,
        CancellationToken cancellationToken = default);

    Task UnlinkAsync(CancellationToken cancellationToken = default);
}

/// <summary>
/// Claims a pairing code against the cloud, stores the mobile token only in
/// <see cref="ITokenStore"/> (SecureStorage on the app) and keeps non-secret
/// link data in preferences. Tokens and codes never appear in exception
/// messages or logs.
/// </summary>
public sealed class PairingService(
    ICloudApiClient api,
    ITokenStore tokenStore,
    AppState appState,
    ParentSettingsStore settings,
    bool allowInsecureEndpoints = false) : IPairingService
{
    private const string ClaimPath = "/v1/pairing/claim";
    private const string LinkedElsewhereMessage =
        "Ứng dụng đã ghép nối với một máy tính khác. Hãy xác nhận thay thế trước khi ghép nối lại.";

    public async Task<ClaimPairingResponse> ClaimAsync(
        PairingClaim claim,
        MobileDeviceIdentity identity,
        bool replaceExisting,
        CancellationToken cancellationToken = default)
    {
        if (!PairingCodeInput.TryNormalize(claim.Code, out var code))
            throw new PairingException(PairingCodeInput.Hint, PairingFailure.InvalidCode);

        var deviceName = identity.DeviceName?.Trim() ?? string.Empty;
        if (deviceName.Length is 0 or > 128)
            throw new PairingException("Tên thiết bị không hợp lệ.", PairingFailure.Configuration);

        var platform = identity.Platform?.Trim() ?? string.Empty;
        if (platform.Length is 0 or > 32)
            throw new PairingException("Nền tảng không hợp lệ.", PairingFailure.Configuration);

        if (!CloudServerUrl.TryNormalize(
                claim.CloudBaseUrl,
                allowInsecureEndpoints,
                out var cloudBaseUrl,
                out var urlError))
        {
            throw new PairingException(
                urlError ?? "Địa chỉ cloud không hợp lệ.",
                PairingFailure.Configuration);
        }

        if (appState.IsLinked && !replaceExisting)
            throw new PairingException(LinkedElsewhereMessage, PairingFailure.Configuration);

        if (appState.IsLinked)
            await RevokeCurrentDeviceAsync(cancellationToken);

        var normalizedUrl = cloudBaseUrl!.GetLeftPart(UriPartial.Authority);
        appState.CloudBaseUrl = normalizedUrl;
        settings.CloudBaseUrl = normalizedUrl;

        try
        {
            var response = await api.PostAsync<ClaimPairingRequest, ClaimPairingResponse>(
                ClaimPath,
                new ClaimPairingRequest(
                    code,
                    deviceName,
                    platform,
                    Truncate(identity.AppVersion, 32),
                    claim.SessionId?.ToString("D"),
                    Truncate(identity.TimeZoneId, 64)),
                authenticated: false,
                cancellationToken);

            await tokenStore.SaveTokenAsync(response.MobileToken, cancellationToken);

            appState.IsLinked = true;
            appState.FamilyId = response.FamilyId;
            appState.DeviceId = response.DeviceId;
            appState.TimeZone = response.TimeZoneId;
            appState.DesktopName = response.DesktopName;

            settings.FamilyId = response.FamilyId;
            settings.DeviceId = response.DeviceId;
            settings.DesktopName = response.DesktopName;
            settings.TimeZoneId = response.TimeZoneId;

            return response;
        }
        catch (CloudApiException ex)
        {
            throw Map(ex);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            throw new PairingException(
                "Không kết nối được máy chủ cloud. Kiểm tra mạng và thử lại.",
                PairingFailure.Network);
        }
    }

    public async Task UnlinkAsync(CancellationToken cancellationToken = default)
    {
        await RevokeCurrentDeviceAsync(cancellationToken);
        await ClearLocalLinkAsync(cancellationToken);
    }

    private async Task RevokeCurrentDeviceAsync(CancellationToken cancellationToken)
    {
        var deviceId = appState.DeviceId ?? settings.DeviceId;
        if (deviceId is null)
            return;

        var token = await tokenStore.GetTokenAsync(cancellationToken);
        if (string.IsNullOrWhiteSpace(token))
            return;

        try
        {
            // Best effort: even if the server call fails the phone must forget
            // the old token locally (issue #90, "thu hồi token cũ nếu có thể").
            await api.DeleteAsync($"/v1/devices/{deviceId:D}", authenticated: true, cancellationToken);
        }
        catch (CloudApiException)
        {
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
        }
        finally
        {
            await ClearLocalLinkAsync(cancellationToken);
        }
    }

    private async Task ClearLocalLinkAsync(CancellationToken cancellationToken)
    {
        await tokenStore.ClearAsync(cancellationToken);
        appState.IsLinked = false;
        appState.FamilyId = null;
        appState.DeviceId = null;
        appState.TimeZone = null;
        appState.DesktopName = null;
        settings.ClearLink();
    }

    private static PairingException Map(CloudApiException ex) =>
        ex.StatusCode switch
        {
            400 or 410 => new PairingException(
                "Mã ghép nối không hợp lệ hoặc đã hết hạn. Hãy nhờ máy tính tạo mã mới.",
                PairingFailure.InvalidCode),
            404 => new PairingException(
                "Mã ghép nối không hợp lệ hoặc đã hết hạn. Hãy nhờ máy tính tạo mã mới.",
                PairingFailure.InvalidCode),
            429 => new PairingException(
                "Bạn đã thử quá nhiều lần. Vui lòng thử lại sau.",
                PairingFailure.RateLimited,
                ex.RetryAfter),
            _ when ex.StatusCode >= 500 => new PairingException(
                "Máy chủ cloud đang gặp sự cố. Vui lòng thử lại sau.",
                PairingFailure.Server),
            _ => new PairingException(
                string.IsNullOrWhiteSpace(ex.Message)
                    ? "Không ghép nối được. Vui lòng thử lại."
                    : ex.Message,
                PairingFailure.Server),
        };

    private static string? Truncate(string? value, int maxLength)
    {
        var trimmed = value?.Trim();
        if (string.IsNullOrEmpty(trimmed))
            return null;

        return trimmed.Length <= maxLength ? trimmed : trimmed[..maxLength];
    }
}
