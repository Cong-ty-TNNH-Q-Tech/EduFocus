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
        var certificates = store.Certificates;
        X509Certificate2Collection? matches = null;
        try
        {
            matches = certificates.Find(
                X509FindType.FindBySubjectDistinguishedName,
                subjectDistinguishedName,
                validOnly: false);

            return matches
                .Select(certificate => certificate.Thumbprint)
                .Where(thumbprint => !string.IsNullOrEmpty(thumbprint))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }
        finally
        {
            DisposeCollection(matches);
            DisposeCollection(certificates);
        }
    }

    public bool Contains(string thumbprint)
    {
        using var store = OpenStore(OpenFlags.ReadOnly);
        var certificates = store.Certificates;
        X509Certificate2Collection? matches = null;
        try
        {
            matches = certificates.Find(X509FindType.FindByThumbprint, thumbprint, validOnly: false);
            return matches.Count > 0;
        }
        finally
        {
            DisposeCollection(matches);
            DisposeCollection(certificates);
        }
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
        var certificates = store.Certificates;
        X509Certificate2Collection? matches = null;
        try
        {
            matches = certificates.Find(X509FindType.FindByThumbprint, thumbprint, validOnly: false);
            foreach (var certificate in matches)
                store.Remove(certificate);
        }
        finally
        {
            DisposeCollection(matches);
            DisposeCollection(certificates);
        }
    }

    private static void DisposeCollection(X509Certificate2Collection? collection)
    {
        if (collection is null)
            return;

        foreach (var certificate in collection)
            certificate.Dispose();
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
