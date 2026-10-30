using System.Runtime.Versioning;
using System.Security.Cryptography.X509Certificates;

namespace EduFocus.Infrastructure.Protection.Certificates;

/// <summary>
/// Writes the public root certificate to <c>LocalMachine\Root</c>. Only the
/// current EduFocus root is added; same-subject roots from an earlier install
/// are removed so a regenerated CA never leaves stale trust behind.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class WindowsRootCertificateTrustStore : ICertificateTrustStore
{
    public IReadOnlyList<string> FindThumbprintsBySubject(string subjectDistinguishedName)
    {
        using var store = OpenStore(OpenFlags.ReadOnly);

        return store.Certificates
            .Find(X509FindType.FindBySubjectDistinguishedName, subjectDistinguishedName, validOnly: false)
            .Select(certificate => certificate.Thumbprint)
            .Where(thumbprint => !string.IsNullOrEmpty(thumbprint))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    public bool Contains(string thumbprint)
    {
        using var store = OpenStore(OpenFlags.ReadOnly);

        return store.Certificates
            .Find(X509FindType.FindByThumbprint, thumbprint, validOnly: false)
            .Count > 0;
    }

    public void Add(X509Certificate2 certificate)
    {
        ArgumentNullException.ThrowIfNull(certificate);

        using var store = OpenStore(OpenFlags.ReadWrite);

        // Only the public part goes into the store; the private key stays in
        // the DPAPI-protected file owned by the certificate authority.
        using var publicOnly = X509CertificateLoader.LoadCertificate(certificate.RawData);
        store.Add(publicOnly);
    }

    public void Remove(string thumbprint)
    {
        if (string.IsNullOrWhiteSpace(thumbprint))
            return;

        using var store = OpenStore(OpenFlags.ReadWrite);
        var matches = store.Certificates.Find(X509FindType.FindByThumbprint, thumbprint, validOnly: false);
        foreach (var certificate in matches)
            store.Remove(certificate);
    }

    private static X509Store OpenStore(OpenFlags flags)
    {
        var store = new X509Store(StoreName.Root, StoreLocation.LocalMachine);
        try
        {
            store.Open(flags | OpenFlags.OpenExistingOnly);
            return store;
        }
        catch
        {
            store.Dispose();
            throw;
        }
    }
}
