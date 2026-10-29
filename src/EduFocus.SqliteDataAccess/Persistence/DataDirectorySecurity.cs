using System.Runtime.Versioning;
using System.Security.AccessControl;
using System.Security.Principal;

namespace EduFocus.SqliteDataAccess.Persistence;

/// <summary>
/// Restricts the data directory to Administrators and SYSTEM only. Inheritance
/// is used so databases, WAL files and backups created later keep the same ACL
/// (database_schema §16). Standard users lose read/write access, which is the
/// intended boundary on Windows.
/// </summary>
public static class DataDirectorySecurity
{
    public static void Apply(string directory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directory);

        if (!OperatingSystem.IsWindows())
            return;

        ApplyWindowsAcl(Path.GetFullPath(directory));
    }

    /// <summary>
    /// Creates the directory when it is missing and applies the restricted ACL.
    /// The installer runs this before the first launch so the folder is already
    /// protected when the app writes its first database.
    /// </summary>
    public static string EnsureSecure(string directory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directory);

        var fullPath = Path.GetFullPath(directory);
        Directory.CreateDirectory(fullPath);
        Apply(fullPath);
        return fullPath;
    }

    [SupportedOSPlatform("windows")]
    private static void ApplyWindowsAcl(string directory)
    {
        var info = new DirectoryInfo(directory);
        if (!info.Exists)
            throw new DirectoryNotFoundException($"Data directory '{directory}' does not exist.");

        var security = new DirectorySecurity();
        security.SetAccessRuleProtection(isProtected: true, preserveInheritance: false);

        AddFullControl(security, WellKnownSidType.BuiltinAdministratorsSid);
        AddFullControl(security, WellKnownSidType.LocalSystemSid);

        FileSystemAclExtensions.SetAccessControl(info, security);
    }

    [SupportedOSPlatform("windows")]
    private static void AddFullControl(DirectorySecurity security, WellKnownSidType sidType)
    {
        const InheritanceFlags childInheritance =
            InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit;

        security.AddAccessRule(
            new FileSystemAccessRule(
                new SecurityIdentifier(sidType, null),
                FileSystemRights.FullControl,
                childInheritance,
                PropagationFlags.None,
                AccessControlType.Allow));
    }
}
