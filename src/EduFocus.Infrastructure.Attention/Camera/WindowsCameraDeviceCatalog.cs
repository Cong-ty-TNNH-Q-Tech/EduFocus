using System.Management;
using EduFocus.Ports.DTOs;

namespace EduFocus.Infrastructure.Attention.Camera;

/// <summary>
/// Enumerates cameras through WMI (<c>Win32_PnPEntity</c>). Enumeration is best
/// effort: WMI can be unavailable or blocked, in which case the capture adapter
/// falls back to the default camera index.
/// </summary>
public sealed class WindowsCameraDeviceCatalog : ICameraDeviceCatalog
{
    private const string Query =
        "SELECT PNPDeviceID, Name FROM Win32_PnPEntity WHERE PNPClass = 'Camera' OR PNPClass = 'Image'";

    public IReadOnlyList<CameraDeviceInfo> ListDevices()
    {
        var devices = new List<CameraDeviceInfo>();

        try
        {
            using var searcher = new ManagementObjectSearcher(Query);
            using var results = searcher.Get();

            foreach (ManagementObject device in results)
            {
                using (device)
                {
                    if (device["PNPDeviceID"] is not string id || string.IsNullOrWhiteSpace(id))
                        continue;

                    var name = device["Name"] as string;
                    devices.Add(new CameraDeviceInfo(
                        id,
                        string.IsNullOrWhiteSpace(name) ? "Camera" : name,
                        devices.Count));
                }
            }
        }
        catch (Exception ex) when (ex is ManagementException
                                       or UnauthorizedAccessException
                                       or System.Runtime.InteropServices.COMException
                                       or PlatformNotSupportedException
                                       or InvalidOperationException)
        {
            // WMI is optional; the supervisor reports the camera itself as
            // unavailable if opening the default device also fails.
            return devices;
        }

        return devices;
    }
}
