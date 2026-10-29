using System.Runtime.Versioning;
using System.Security.AccessControl;
using System.Security.Principal;
using EduFocus.SqliteDataAccess.Persistence;
using FluentAssertions;

namespace EduFocus.SqliteDataAccess.Tests;

public sealed class DataDirectorySecurityTests
{
    private const string AdministratorsSid = "S-1-5-32-544";
    private const string SystemSid = "S-1-5-18";

    [Fact]
    [SupportedOSPlatform("windows")]
    public void Apply_restricts_directory_and_children_to_administrators_and_system()
    {
        if (!OperatingSystem.IsWindows())
            return;

        var directory = Path.Combine(
            Path.GetTempPath(),
            "edufocus-acl-tests",
            Guid.NewGuid().ToString("N"));

        Directory.CreateDirectory(directory);

        try
        {
            DataDirectorySecurity.Apply(directory);

            var info = new DirectoryInfo(directory);
            var security = info.GetAccessControl();

            security.AreAccessRulesProtected.Should().BeTrue();
            AllowedSids(security).Should().BeEquivalentTo(AdministratorsSid, SystemSid);

            var allowRules = security
                .GetAccessRules(includeExplicit: true, includeInherited: true, targetType: typeof(SecurityIdentifier))
                .Cast<FileSystemAccessRule>()
                .Where(rule => rule.AccessControlType == AccessControlType.Allow);

            allowRules.Should().OnlyContain(
                rule => rule.InheritanceFlags == (InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit),
                "new databases, WAL files and backups must inherit the same ACL");
        }
        finally
        {
            RestoreInheritance(new DirectoryInfo(directory));
            TryDeleteDirectory(directory);
        }
    }

    [SupportedOSPlatform("windows")]
    private static IEnumerable<string> AllowedSids(DirectorySecurity security) =>
        security
            .GetAccessRules(includeExplicit: true, includeInherited: true, targetType: typeof(SecurityIdentifier))
            .Cast<FileSystemAccessRule>()
            .Where(rule => rule.AccessControlType == AccessControlType.Allow)
            .Select(rule => rule.IdentityReference.Value)
            .Distinct();

    [SupportedOSPlatform("windows")]
    private static void RestoreInheritance(DirectoryInfo info)
    {
        try
        {
            var security = info.GetAccessControl();
            security.SetAccessRuleProtection(isProtected: false, preserveInheritance: true);
            info.SetAccessControl(security);
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or IOException)
        {
        }
    }

    private static void TryDeleteDirectory(string directory)
    {
        try
        {
            Directory.Delete(directory, recursive: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }
    }
}
