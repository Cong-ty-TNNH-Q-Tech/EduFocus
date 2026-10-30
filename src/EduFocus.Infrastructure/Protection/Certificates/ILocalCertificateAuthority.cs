using System.Security.Cryptography.X509Certificates;

namespace EduFocus.Infrastructure.Protection.Certificates;

/// <summary>
/// Outcome of a trust-store operation. Failures are reported as a warning so
/// the protection engine can keep executing; they are never thrown to the UI.
/// </summary>
public sealed record CertificateAuthorityResult(bool Ready, string? Warning);

/// <summary>
/// Issues short-lived TLS certificates for the HTTPS block page and manages
/// the single EduFocus root in the machine trust store. Implementations must
/// never log key material or blocked host names.
/// </summary>
public interface ILocalCertificateAuthority
{
    /// <summary>True while the root is trusted and leaf issuance is available.</summary>
    bool IsReady { get; }

    Task<CertificateAuthorityResult> EnsureTrustedAsync(CancellationToken cancellationToken = default);

    Task<CertificateAuthorityResult> RemoveTrustAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns a cached or newly issued server certificate for an exact blocked
    /// domain, or null when the domain is invalid, the authority is not trusted
    /// or issuance fails. Never throws.
    /// </summary>
    X509Certificate2? GetServerCertificate(string domain);
}
