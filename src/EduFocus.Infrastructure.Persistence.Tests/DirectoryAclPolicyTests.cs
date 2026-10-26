using System.Security.AccessControl;
using System.Security.Principal;
using EduFocus.Infrastructure.Persistence;
using System.Runtime.Versioning;

namespace EduFocus.Infrastructure.Persistence.Tests;

public sealed class DirectoryAclPolicyTests
{
    [Fact]
    [SupportedOSPlatform("windows")]
    public void Protected_directory_policy_grants_only_administrators_and_system_full_control()
    {
        if (!OperatingSystem.IsWindows()) return;
        var descriptor = WindowsDataDirectoryProtector.CreateDirectorySecurityPolicy();
        Assert.True(descriptor.AreAccessRulesProtected);
        var rules = descriptor.GetAccessRules(includeExplicit: true, includeInherited: true, targetType: typeof(SecurityIdentifier))
            .Cast<FileSystemAccessRule>().ToArray();
        Assert.Equal(2, rules.Length);
        var allowedSids = rules.Select(rule => ((SecurityIdentifier)rule.IdentityReference).Value).ToHashSet(StringComparer.OrdinalIgnoreCase);
        Assert.Contains(new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null).Value, allowedSids);
        Assert.Contains(new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null).Value, allowedSids);
        Assert.All(rules, rule => { Assert.Equal(AccessControlType.Allow, rule.AccessControlType); Assert.Equal(FileSystemRights.FullControl, rule.FileSystemRights); });

        var fileDescriptor = WindowsDataDirectoryProtector.CreateFileSecurityPolicy();
        var fileRules = fileDescriptor.GetAccessRules(includeExplicit: true, includeInherited: true, targetType: typeof(SecurityIdentifier))
            .Cast<FileSystemAccessRule>().ToArray();
        Assert.Equal(2, fileRules.Length);
        Assert.All(fileRules, rule => { Assert.Equal(AccessControlType.Allow, rule.AccessControlType); Assert.Equal(FileSystemRights.FullControl, rule.FileSystemRights); });
    }
}
