using System.Security.Cryptography.X509Certificates;

namespace EduFocus.Infrastructure.Protection.Certificates;

/// <summary>
/// Machine root store operations for the local certificate authority. The
/// interface keeps the trust lifecycle testable without elevation; production
/// uses <see cref="WindowsRootCertificateTrustStore"/>.
/// </summary>
public interface ICertificateTrustStore
{
    IReadOnlyList<string> FindThumbprintsBySubject(string subjectDistinguishedName);

    bool Contains(string thumbprint);

    void Add(X509Certificate2 certificate);

    void Remove(string thumbprint);
}
