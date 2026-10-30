using System.IO;
using System.Security;
using EduFocus.Ports.ServiceControl;
using Microsoft.Win32;

namespace EduFocus.WPF.Services;

/// <summary>
/// Reads the installer marker that puts protection under the Windows service.
/// The marker is the only signal: when it is present the UI must talk to the
/// service and never start a second, local protection owner.
/// </summary>
public static class ServiceInstallationProbe
{
    public static bool IsServiceManaged()
    {
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(ServiceIdentity.InstallRegistryPath);
            var value = key?.GetValue(ServiceIdentity.ServiceModeValueName);

            return value switch
            {
                int number => number == ServiceIdentity.ServiceModeEnabled,
                string text => int.TryParse(text, out var parsed)
                    && parsed == ServiceIdentity.ServiceModeEnabled,
                _ => false
            };
        }
        catch (Exception ex) when (ex is SecurityException or UnauthorizedAccessException or IOException)
        {
            return false;
        }
    }
}
