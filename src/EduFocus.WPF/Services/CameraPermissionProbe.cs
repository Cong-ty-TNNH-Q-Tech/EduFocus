using System.IO;
using System.Security;
using Microsoft.Win32;

namespace EduFocus.WPF.Services;

internal enum CameraPermissionState
{
    Allowed,
    Denied,
    Unknown
}

/// <summary>
/// Reads the Windows webcam consent store so onboarding can warn when the OS
/// blocks camera access. Opening a real camera remains the attention pipeline's
/// responsibility (issue #30).
/// </summary>
internal static class CameraPermissionProbe
{
    private const string ConsentKeyPath =
        @"Software\Microsoft\Windows\CurrentVersion\CapabilityAccessManager\ConsentStore\webcam";

    public static CameraPermissionState Check()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(ConsentKeyPath);
            var value = key?.GetValue("Value") as string;

            return value switch
            {
                "Allow" => CameraPermissionState.Allowed,
                "Deny" => CameraPermissionState.Denied,
                _ => CameraPermissionState.Unknown
            };
        }
        catch (Exception ex) when (ex is SecurityException or UnauthorizedAccessException or IOException)
        {
            return CameraPermissionState.Unknown;
        }
    }
}
