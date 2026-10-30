using System.Runtime.Versioning;
using Microsoft.Win32;

namespace EduFocus.Infrastructure.Protection;

/// <summary>
/// Machine policy locations used to turn off browser Secure DNS. Defaults cover
/// Chrome and Edge under <c>HKLM\SOFTWARE\Policies</c> and keep the previous
/// value in EduFocus's own key so protection OFF/uninstall can restore it.
/// Tests target a unique HKCU tree and need no elevation.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed record BrowserDnsPolicyOptions
{
    public const string PolicyValueName = "DnsOverHttpsMode";
    public const string DisabledValue = "off";
    public const string DefaultBackupRootPath = @"SOFTWARE\EduFocus\BrowserDnsPolicy";

    public RegistryHive Hive { get; init; } = RegistryHive.LocalMachine;

    public string BackupRootPath { get; init; } = DefaultBackupRootPath;

    public IReadOnlyList<BrowserDnsPolicyTarget> Targets { get; init; } =
        BrowserDnsPolicyTarget.Defaults;
}

/// <summary>
/// One browser's machine policy key. <see cref="Name"/> identifies the backup
/// entry and must stay stable; <see cref="PolicyKeyPath"/> is created when the
/// browser has no policy key yet.
/// </summary>
public sealed record BrowserDnsPolicyTarget(string Name, string PolicyKeyPath)
{
    public static IReadOnlyList<BrowserDnsPolicyTarget> Defaults { get; } =
    [
        new("Chrome", @"SOFTWARE\Policies\Google\Chrome"),
        new("Edge", @"SOFTWARE\Policies\Microsoft\Edge")
    ];
}
