using System.Security.AccessControl;
using System.Security.Principal;
using System.Runtime.Versioning;

namespace EduFocus.Infrastructure.Persistence;

public sealed class WindowsDataDirectoryProtector : IDataDirectoryProtector
{
    public void Protect(string directoryPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directoryPath);
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException("EduFocus data ACLs require Windows.");
        var root = Path.GetFullPath(directoryPath);
        Directory.CreateDirectory(root);
        var rootSecurity = CreateDirectorySecurityPolicy();
        new DirectoryInfo(root).SetAccessControl(rootSecurity);

        foreach (var directory in Directory.EnumerateDirectories(root, "*", SearchOption.AllDirectories))
            new DirectoryInfo(directory).SetAccessControl(CreateDirectorySecurityPolicy());
        foreach (var file in Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories))
            new FileInfo(file).SetAccessControl(CreateFileSecurityPolicy());
    }

    [SupportedOSPlatform("windows")]
    public static DirectorySecurity CreateDirectorySecurityPolicy()
    {
        var admins = new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null);
        var system = new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null);
        var inheritance = InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit;
        var security = new DirectorySecurity();
        security.SetAccessRuleProtection(isProtected: true, preserveInheritance: false);
        security.SetOwner(admins);
        security.AddAccessRule(new FileSystemAccessRule(admins, FileSystemRights.FullControl, inheritance, PropagationFlags.None, AccessControlType.Allow));
        security.AddAccessRule(new FileSystemAccessRule(system, FileSystemRights.FullControl, inheritance, PropagationFlags.None, AccessControlType.Allow));
        return security;
    }

    [SupportedOSPlatform("windows")]
    public static FileSecurity CreateFileSecurityPolicy()
    {
        var admins = new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null);
        var system = new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null);
        var security = new FileSecurity();
        security.SetAccessRuleProtection(isProtected: true, preserveInheritance: false);
        security.SetOwner(admins);
        security.AddAccessRule(new FileSystemAccessRule(admins, FileSystemRights.FullControl, AccessControlType.Allow));
        security.AddAccessRule(new FileSystemAccessRule(system, FileSystemRights.FullControl, AccessControlType.Allow));
        return security;
    }
}
