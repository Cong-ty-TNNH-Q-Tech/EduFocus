using EduFocus.Cloud.Contracts.Devices;
using EduFocus.Cloud.Contracts.Family;
using EduFocus.Parent.Core.Services;

namespace EduFocus.Parent.Core.Presentation;

public interface IFamilyAdminService
{
    Task<IReadOnlyList<DeviceDto>> GetDevicesAsync(CancellationToken cancellationToken = default);

    Task RevokeDeviceAsync(Guid deviceId, CancellationToken cancellationToken = default);

    Task DeleteFamilyAsync(string confirmation, CancellationToken cancellationToken = default);
}

public sealed class FamilyAdminService(
    ICloudApiClient api,
    SessionExpiryService session) : IFamilyAdminService
{
    public const string DeleteConfirmation = "XÓA";

    public async Task<IReadOnlyList<DeviceDto>> GetDevicesAsync(
        CancellationToken cancellationToken = default)
    {
        var response = await api.GetAsync<DeviceListResponse>(
            "/v1/devices",
            cancellationToken: cancellationToken);
        return response?.Devices ?? [];
    }

    public Task RevokeDeviceAsync(Guid deviceId, CancellationToken cancellationToken = default) =>
        api.DeleteAsync($"/v1/devices/{deviceId:D}", authenticated: true, cancellationToken);

    public async Task DeleteFamilyAsync(
        string confirmation,
        CancellationToken cancellationToken = default)
    {
        if (confirmation != DeleteConfirmation)
            throw new ArgumentException(
                "Nhập đúng chữ XÓA để xác nhận xóa dữ liệu cloud.",
                nameof(confirmation));

        await api.DeleteAsync(
            "/v1/family",
            new DeleteFamilyRequest(DeleteConfirmation),
            authenticated: true,
            cancellationToken);

        await session.ClearLocalAsync(cancellationToken);
    }
}
