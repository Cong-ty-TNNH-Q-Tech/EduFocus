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

    [Fact]
    [SupportedOSPlatform("windows")]
    public void Files_inside_a_secured_directory_carry_only_the_restricted_acl()
    {
        if (!OperatingSystem.IsWindows())
            return;

        var directory = Path.Combine(
            Path.GetTempPath(),
            "edufocus-acl-tests",
            Guid.NewGuid().ToString("N"));
        var file = Path.Combine(directory, "edufocus.db-wal");

        Directory.CreateDirectory(directory);
        File.WriteAllText(file, "wal");

        try
        {
            DataDirectorySecurity.Apply(directory);

            var security = new FileInfo(file).GetAccessControl();

            AllowedSids(security).Should().BeEquivalentTo(AdministratorsSid, SystemSid);
            security
                .GetAccessRules(includeExplicit: true, includeInherited: true, targetType: typeof(SecurityIdentifier))
                .Cast<FileSystemAccessRule>()
                .Where(rule => rule.AccessControlType == AccessControlType.Allow)
                .Should()
                .OnlyContain(
                    rule => rule.IsInherited,
                    "database, WAL and backup files must take their access from the directory ACL");
        }
        finally
        {
            RestoreInheritance(new DirectoryInfo(directory));
            TryDeleteDirectory(directory);
        }
    }

    [Fact]
    [SupportedOSPlatform("windows")]
    public void EnsureSecure_creates_a_missing_directory_with_the_restricted_acl()
    {
        if (!OperatingSystem.IsWindows())
            return;

        var root = Path.Combine(
            Path.GetTempPath(),
            "edufocus-acl-tests",
            Guid.NewGuid().ToString("N"));
        var directory = Path.Combine(root, "EduFocus");

        try
        {
            var created = DataDirectorySecurity.EnsureSecure(directory);

            created.Should().Be(Path.GetFullPath(directory));
            Directory.Exists(created).Should().BeTrue();

            var security = new DirectoryInfo(created).GetAccessControl();

            security.AreAccessRulesProtected.Should().BeTrue();
            AllowedSids(security).Should().BeEquivalentTo(AdministratorsSid, SystemSid);
        }
        finally
        {
            if (Directory.Exists(directory))
                RestoreInheritance(new DirectoryInfo(directory));
            TryDeleteDirectory(root);
        }
    }

    [SupportedOSPlatform("windows")]
    private static IEnumerable<string> AllowedSids(FileSystemSecurity security) =>
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
