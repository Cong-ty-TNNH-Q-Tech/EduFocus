namespace EduFocus.Infrastructure.Protection.Certificates;

/// <summary>
/// Local certificate authority used only for the HTTPS block page. The root
/// key lives in <c>%ProgramData%\EduFocus\certificates</c> behind DPAPI and an
/// Administrators/SYSTEM ACL; the public root is trusted in
/// <c>LocalMachine\Root</c> only while protection is ON. Tests point
/// <see cref="Directory"/> at a temporary folder and inject a fake trust store.
/// </summary>
public sealed record CertificateAuthorityOptions
{
    public const string DefaultDirectoryName = "certificates";
    public const string RootFileName = "block-root-ca.edufocus";

    public static string DefaultDirectory =>
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
            "EduFocus",
            DefaultDirectoryName);

    public string Directory { get; init; } = DefaultDirectory;

    public string RootCommonName { get; init; } = "EduFocus Local Blocking CA";

    public string RootOrganization { get; init; } = "EduFocus";

    /// <summary>
    /// Restricts the key folder to Administrators and SYSTEM. Tests disable it:
    /// a non-elevated token cannot reopen a folder it just locked down.
    /// </summary>
    public bool RestrictDirectoryAcl { get; init; } = true;

    public TimeSpan RootValidity { get; init; } = TimeSpan.FromDays(3650);

    public TimeSpan RootRenewBefore { get; init; } = TimeSpan.FromDays(30);

    public TimeSpan LeafValidity { get; init; } = TimeSpan.FromDays(14);

    public TimeSpan LeafRenewBefore { get; init; } = TimeSpan.FromDays(3);

    public int MaxCachedLeaves { get; init; } = 512;
}
