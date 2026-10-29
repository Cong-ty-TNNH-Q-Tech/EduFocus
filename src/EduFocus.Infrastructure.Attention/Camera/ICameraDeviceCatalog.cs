using EduFocus.Ports.DTOs;

namespace EduFocus.Infrastructure.Attention.Camera;

/// <summary>
/// Platform camera enumeration. <see cref="CameraDeviceInfo.Id"/> is the stable
/// PNP device id, <c>Index</c> the ordinal the capture backend opens.
/// </summary>
public interface ICameraDeviceCatalog
{
    IReadOnlyList<CameraDeviceInfo> ListDevices();
}
