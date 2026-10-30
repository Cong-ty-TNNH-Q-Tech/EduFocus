using System.Globalization;
using System.Runtime.Versioning;
using System.Security.AccessControl;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Security.Principal;
using System.Text;
using EduFocus.Domain.ValueObjects;
using EduFocus.Ports.Outbound.Services;

namespace EduFocus.Infrastructure.Protection.Certificates;

/// <summary>
/// ECDSA P-256 certificate authority for the HTTPS block page. The root key is
/// generated once, stored as a DPAPI (LocalMachine) blob inside a folder that
/// only Administrators and SYSTEM can read, and never leaves this class. Only
/// the public root is installed into <c>LocalMachine\Root</c>, and only while
/// protection is ON. Leaf certificates are short-lived, cached in memory and
/// issued only for exact blocked domains requested through SNI. Leaf private
/// keys are held in the user key store for the lifetime of the certificate
/// object because SChannel cannot serve TLS with ephemeral keys.
/// </summary>
public sealed class LocalCertificateAuthority : ILocalCertificateAuthority, IDisposable
{
    public const string TrustWarning =
        "Không thể cài chứng chỉ cục bộ cho trang chặn HTTPS; trình duyệt có thể hiện cảnh báo chứng chỉ. " +
        "Hãy chạy EduFocus với quyền quản trị.";

    public const string RemoveTrustWarning =
        "Không thể gỡ chứng chỉ cục bộ khỏi kho tin cậy; hãy kiểm tra Certificate Manager " +
        "(Local Computer → Trusted Root Certification Authorities).";

    private static readonly byte[] FileMagic = "EDUCA1"u8.ToArray();
    private static readonly byte[] DpapiEntropy = Encoding.UTF8.GetBytes("EduFocus.CertificateAuthority.v1");
    private const string ServerAuthenticationOid = "1.3.6.1.5.5.7.3.1";

    private readonly CertificateAuthorityOptions _options;
    private readonly ICertificateTrustStore _trustStore;
    private readonly IAppLogger _logger;
    private readonly string _rootFilePath;
    private readonly X500DistinguishedName _rootSubject;
    private readonly Dictionary<string, X509Certificate2> _leaves = new(StringComparer.Ordinal);
    private readonly List<X509Certificate2> _retiredLeaves = [];
    private readonly SemaphoreSlim _trustGate = new(1, 1);
    private readonly object _sync = new();

    private X509Certificate2? _root;
    private volatile bool _ready;
    private bool _disposed;

    public LocalCertificateAuthority(
        CertificateAuthorityOptions options,
        ICertificateTrustStore trustStore,
        IAppLogger logger)
    {
        ArgumentNullException.ThrowIfNull(options);
        _trustStore = trustStore ?? throw new ArgumentNullException(nameof(trustStore));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));

        if (string.IsNullOrWhiteSpace(options.Directory))
            throw new ArgumentException("The certificate directory must not be empty.", nameof(options));
        if (string.IsNullOrWhiteSpace(options.RootCommonName))
            throw new ArgumentException("The root common name must not be empty.", nameof(options));
        if (options.RootValidity < TimeSpan.FromDays(1) || options.RootValidity > TimeSpan.FromDays(3650))
            throw new ArgumentOutOfRangeException(nameof(options), "RootValidity must be between 1 and 3650 days.");
        if (options.RootRenewBefore < TimeSpan.Zero || options.RootRenewBefore >= options.RootValidity)
            throw new ArgumentOutOfRangeException(nameof(options), "RootRenewBefore must be shorter than RootValidity.");
        if (options.LeafValidity < TimeSpan.FromHours(1) || options.LeafValidity > TimeSpan.FromDays(397))
            throw new ArgumentOutOfRangeException(nameof(options), "LeafValidity must be between 1 hour and 397 days.");
        if (options.LeafRenewBefore < TimeSpan.Zero || options.LeafRenewBefore >= options.LeafValidity)
            throw new ArgumentOutOfRangeException(nameof(options), "LeafRenewBefore must be shorter than LeafValidity.");
        if (options.MaxCachedLeaves is < 1 or > 10_000)
            throw new ArgumentOutOfRangeException(nameof(options), "MaxCachedLeaves must be between 1 and 10000.");

        _options = options;
        _rootFilePath = Path.Combine(Path.GetFullPath(options.Directory), CertificateAuthorityOptions.RootFileName);
        _rootSubject = new X500DistinguishedName(
            $"CN={options.RootCommonName}, O={options.RootOrganization}");
    }

    public bool IsReady => _ready;

    public async Task<CertificateAuthorityResult> EnsureTrustedAsync(
        CancellationToken cancellationToken = default)
    {
        await _trustGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var root = await Task
                .Run(() => GetOrCreateRoot(), cancellationToken)
                .ConfigureAwait(false);

            cancellationToken.ThrowIfCancellationRequested();

            await Task
                .Run(() => InstallTrust(root), cancellationToken)
                .ConfigureAwait(false);

            _ready = true;
            _logger.Info(
                LogCategories.Protection,
                string.Create(
                    CultureInfo.InvariantCulture,
                    $"Local certificate authority trusted until {root.NotAfter:yyyy-MM-dd} for the HTTPS block page."));

            return new CertificateAuthorityResult(true, null);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _ready = false;
            _logger.Warning(
                LogCategories.Protection,
                $"Could not prepare the local certificate authority ({ex.GetType().Name}).");

            return new CertificateAuthorityResult(false, TrustWarning);
        }
        finally
        {
            _trustGate.Release();
        }
    }

    public async Task<CertificateAuthorityResult> RemoveTrustAsync(
        CancellationToken cancellationToken = default)
    {
        await _trustGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            _ready = false;
            DisposeRetiredLeaves();

            var removed = await Task
                .Run(RemoveInstalledRoots, cancellationToken)
                .ConfigureAwait(false);

            _logger.Info(
                LogCategories.Protection,
                removed > 0
                    ? "Removed the local block-page root certificate from the machine trust store."
                    : "No local block-page root certificate was present in the machine trust store.");

            return new CertificateAuthorityResult(true, null);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.Warning(
                LogCategories.Protection,
                $"Could not remove the local block-page root from the trust store ({ex.GetType().Name}).");

            return new CertificateAuthorityResult(false, RemoveTrustWarning);
        }
        finally
        {
            _trustGate.Release();
        }
    }

    public X509Certificate2? GetServerCertificate(string domain)
    {
        if (!_ready || _disposed)
            return null;

        if (string.IsNullOrWhiteSpace(domain)
            || domain.IndexOfAny(['/', '\\', '@', '?', '#', ':', ' ', '\t']) >= 0)
        {
            return null;
        }

        if (!DomainName.TryParse(domain, out var parsedDomain) || parsedDomain is null)
            return null;

        var name = parsedDomain.Value;

        lock (_sync)
        {
            if (_leaves.TryGetValue(name, out var cached))
            {
                if (cached.NotAfter.ToUniversalTime() > DateTime.UtcNow.Add(_options.LeafRenewBefore))
                    return cached;

                _retiredLeaves.Add(cached);
                _leaves.Remove(name);
            }

            try
            {
                if (_leaves.Count >= _options.MaxCachedLeaves)
                {
                    _retiredLeaves.AddRange(_leaves.Values);
                    _leaves.Clear();
                }

                var leaf = IssueLeaf(name);
                _leaves[name] = leaf;
                return leaf;
            }
            catch (Exception ex)
            {
                // A failed handshake must never take the listener down, and the
                // blocked host must not appear in logs.
                _logger.Warning(
                    LogCategories.Protection,
                    $"Could not issue a local certificate for the HTTPS block page ({ex.GetType().Name}).");

                return null;
            }
        }
    }

    public void Dispose()
    {
        lock (_sync)
        {
            if (_disposed)
                return;

            _disposed = true;
            _ready = false;

            foreach (var leaf in _leaves.Values)
                leaf.Dispose();

            _leaves.Clear();

            foreach (var leaf in _retiredLeaves)
                leaf.Dispose();

            _retiredLeaves.Clear();

            _root?.Dispose();
            _root = null;
        }
    }

    private void DisposeRetiredLeaves()
    {
        lock (_sync)
        {
            foreach (var leaf in _retiredLeaves)
                leaf.Dispose();

            _retiredLeaves.Clear();
        }
    }

    private X509Certificate2 GetOrCreateRoot()
    {
        if (_root is not null)
            return _root;

        lock (_sync)
        {
            if (_root is not null)
                return _root;

            _root = TryLoadRoot() ?? CreateRoot();
            return _root;
        }
    }

    private X509Certificate2? TryLoadRoot()
    {
        if (!File.Exists(_rootFilePath))
            return null;

        try
        {
            var fileBytes = File.ReadAllBytes(_rootFilePath);
            if (fileBytes.Length < FileMagic.Length + 64
                || fileBytes.Length > 64 * 1024
                || !fileBytes.AsSpan(0, FileMagic.Length).SequenceEqual(FileMagic))
            {
                return null;
            }

            var protectedBytes = fileBytes.AsSpan(FileMagic.Length).ToArray();
            var pfxBytes = Unprotect(protectedBytes);
            try
            {
                var root = X509CertificateLoader.LoadPkcs12(
                    pfxBytes,
                    password: null,
                    X509KeyStorageFlags.EphemeralKeySet);

                if (!root.HasPrivateKey
                    || root.NotBefore.ToUniversalTime() > DateTime.UtcNow.AddMinutes(5)
                    || root.NotAfter.ToUniversalTime() <= DateTime.UtcNow.Add(_options.RootRenewBefore))
                {
                    root.Dispose();
                    return null;
                }

                return root;
            }
            finally
            {
                CryptographicOperations.ZeroMemory(pfxBytes);
            }
        }
        catch (Exception ex) when (
            ex is IOException
                or UnauthorizedAccessException
                or CryptographicException
                or PlatformNotSupportedException)
        {
            _logger.Warning(
                LogCategories.Protection,
                $"The existing local certificate authority file could not be loaded ({ex.GetType().Name}); a new root will be created.");
            return null;
        }
    }

    private X509Certificate2 CreateRoot()
    {
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var request = new CertificateRequest(_rootSubject, key, HashAlgorithmName.SHA256);

        request.CertificateExtensions.Add(
            new X509BasicConstraintsExtension(certificateAuthority: true, hasPathLengthConstraint: true, 0, critical: true));
        request.CertificateExtensions.Add(
            new X509KeyUsageExtension(
                X509KeyUsageFlags.KeyCertSign | X509KeyUsageFlags.CrlSign | X509KeyUsageFlags.DigitalSignature,
                critical: true));
        request.CertificateExtensions.Add(
            new X509SubjectKeyIdentifierExtension(request.PublicKey, critical: false));

        var notBefore = DateTimeOffset.UtcNow.AddDays(-1);
        var notAfter = DateTimeOffset.UtcNow.Add(_options.RootValidity);
        var root = request.CreateSelfSigned(notBefore, notAfter);

        PersistRoot(root);
        return root;
    }

    private void PersistRoot(X509Certificate2 root)
    {
        var directory = Path.GetDirectoryName(_rootFilePath)!;
        Directory.CreateDirectory(directory);

        if (OperatingSystem.IsWindows() && _options.RestrictDirectoryAcl)
            ApplyDirectoryAcl(directory);

        var pfxBytes = root.Export(X509ContentType.Pfx);
        byte[]? fileBytes = null;
        var temporaryPath = $"{_rootFilePath}.{Guid.NewGuid():N}.tmp";

        try
        {
            var protectedBytes = Protect(pfxBytes);

            try
            {
                fileBytes = new byte[FileMagic.Length + protectedBytes.Length];
                FileMagic.CopyTo(fileBytes, 0);
                protectedBytes.CopyTo(fileBytes, FileMagic.Length);
            }
            finally
            {
                CryptographicOperations.ZeroMemory(protectedBytes);
            }

            using (var stream = new FileStream(
                temporaryPath,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None,
                bufferSize: 4096,
                FileOptions.WriteThrough))
            {
                stream.Write(fileBytes);
                stream.Flush(flushToDisk: true);
            }

            File.Move(temporaryPath, _rootFilePath, overwrite: true);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(pfxBytes);

            if (fileBytes is not null)
                CryptographicOperations.ZeroMemory(fileBytes);

            TryDelete(temporaryPath);
        }
    }

    private void InstallTrust(X509Certificate2 root)
    {
        var thumbprint = root.Thumbprint;

        foreach (var stale in _trustStore.FindThumbprintsBySubject(root.Subject))
        {
            if (!string.Equals(stale, thumbprint, StringComparison.OrdinalIgnoreCase))
                _trustStore.Remove(stale);
        }

        if (!_trustStore.Contains(thumbprint))
            _trustStore.Add(root);
    }

    private int RemoveInstalledRoots()
    {
        var removed = 0;

        foreach (var thumbprint in _trustStore.FindThumbprintsBySubject(_rootSubject.Name))
        {
            _trustStore.Remove(thumbprint);
            removed++;
        }

        return removed;
    }

    private X509Certificate2 IssueLeaf(string domain)
    {
        var root = GetOrCreateRoot();

        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var request = new CertificateRequest(
            new X500DistinguishedName($"CN={domain}"),
            key,
            HashAlgorithmName.SHA256);

        request.CertificateExtensions.Add(
            new X509BasicConstraintsExtension(certificateAuthority: false, hasPathLengthConstraint: false, 0, critical: true));
        request.CertificateExtensions.Add(
            new X509KeyUsageExtension(X509KeyUsageFlags.DigitalSignature, critical: true));

        var usages = new OidCollection { new(ServerAuthenticationOid) };
        request.CertificateExtensions.Add(new X509EnhancedKeyUsageExtension(usages, critical: true));

        var alternativeNames = new SubjectAlternativeNameBuilder();
        alternativeNames.AddDnsName(domain);
        request.CertificateExtensions.Add(alternativeNames.Build());

        request.CertificateExtensions.Add(
            new X509SubjectKeyIdentifierExtension(request.PublicKey, critical: false));

        var notBefore = DateTimeOffset.UtcNow.AddHours(-1);
        var notAfter = DateTimeOffset.UtcNow.Add(_options.LeafValidity);
        var rootNotBefore = new DateTimeOffset(root.NotBefore.ToUniversalTime());
        var rootNotAfter = new DateTimeOffset(root.NotAfter.ToUniversalTime());

        if (notBefore < rootNotBefore)
            notBefore = rootNotBefore;
        if (notAfter > rootNotAfter.AddMinutes(-1))
            notAfter = rootNotAfter.AddMinutes(-1);

        if (notAfter <= notBefore)
            throw new CryptographicException("The root certificate expires too soon to issue a leaf.");

        // SChannel refuses server credentials backed by an ephemeral key
        // ("the platform does not support ephemeral keys"), so the leaf is
        // re-imported into the user key store. Without PersistKeySet the key
        // container is deleted when the certificate is disposed, which keeps
        // the key material scoped to this process.
        using var issued = request.Create(root, notBefore, notAfter, CreateSerialNumber());
        using var withKey = issued.CopyWithPrivateKey(key);
        var pfxBytes = withKey.Export(X509ContentType.Pfx);

        try
        {
            return X509CertificateLoader.LoadPkcs12(pfxBytes, password: null, X509KeyStorageFlags.UserKeySet);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(pfxBytes);
        }
    }

    private static byte[] CreateSerialNumber()
    {
        var serial = RandomNumberGenerator.GetBytes(16);
        serial[0] &= 0x7F;

        if (!serial.Any(value => value != 0))
            serial[^1] = 1;

        return serial;
    }

    private static byte[] Protect(byte[] pfxBytes)
    {
        if (!OperatingSystem.IsWindows())
            throw new PlatformNotSupportedException("DPAPI is only available on Windows.");

        return ProtectedData.Protect(pfxBytes, DpapiEntropy, DataProtectionScope.LocalMachine);
    }

    private static byte[] Unprotect(byte[] protectedBytes)
    {
        if (!OperatingSystem.IsWindows())
            throw new PlatformNotSupportedException("DPAPI is only available on Windows.");

        return ProtectedData.Unprotect(protectedBytes, DpapiEntropy, DataProtectionScope.LocalMachine);
    }

    [SupportedOSPlatform("windows")]
    private static void ApplyDirectoryAcl(string directory)
    {
        var info = new DirectoryInfo(directory);
        var security = new DirectorySecurity();
        security.SetAccessRuleProtection(isProtected: true, preserveInheritance: false);

        const InheritanceFlags childInheritance =
            InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit;

        foreach (var sidType in new[]
        {
            WellKnownSidType.BuiltinAdministratorsSid,
            WellKnownSidType.LocalSystemSid
        })
        {
            security.AddAccessRule(
                new FileSystemAccessRule(
                    new SecurityIdentifier(sidType, null),
                    FileSystemRights.FullControl,
                    childInheritance,
                    PropagationFlags.None,
                    AccessControlType.Allow));
        }

        FileSystemAclExtensions.SetAccessControl(info, security);
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
                File.Delete(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }
    }
}
