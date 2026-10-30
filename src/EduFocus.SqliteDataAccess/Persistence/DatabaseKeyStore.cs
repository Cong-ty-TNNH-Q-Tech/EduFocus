using System.Security.Cryptography;
using System.Text;

namespace EduFocus.SqliteDataAccess.Persistence;

/// <summary>
/// Stores the SQLCipher key as a DPAPI (LocalMachine) blob in
/// <c>edufocus.key</c> next to the database. The file contains no plaintext
/// key material, inherits the Administrators/SYSTEM-only ACL of the data
/// directory, and is bound to this machine (database_schema §18).
/// </summary>
public sealed class DatabaseKeyStore : IDatabaseKeyStore
{
    private static readonly byte[] Magic = "EDUFKEY1"u8.ToArray();
    private static readonly byte[] Entropy = Encoding.UTF8.GetBytes("EduFocus.DatabaseKey.v1");
    private const int DpapiOverheadBytes = 128;
    private const int MaxKeyFileBytes = 4096;

    private readonly object _sync = new();
    private DatabaseEncryptionKey? _cached;

    public DatabaseKeyStore(EduFocusDatabaseOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        KeyFilePath = Path.GetFullPath(options.ResolvedKeyFilePath);
    }

    public string KeyFilePath { get; }

    public bool Exists => File.Exists(KeyFilePath);

    public DatabaseEncryptionKey? TryLoad()
    {
        if (_cached is not null)
            return _cached;

        lock (_sync)
        {
            if (_cached is not null)
                return _cached;

            if (!File.Exists(KeyFilePath))
                return null;

            _cached = ReadKeyFile();
            return _cached;
        }
    }

    public DatabaseEncryptionKey Get() =>
        TryLoad() ?? throw new DatabaseKeyException(
            "The EduFocus database key was not found. Restore the key file together with the database backup, or contact support. No data was changed.",
            KeyFilePath);

    public DatabaseEncryptionKey LoadOrCreate()
    {
        var existing = TryLoad();
        if (existing is not null)
            return existing;

        lock (_sync)
        {
            existing = TryLoad();
            if (existing is not null)
                return existing;

            CreateKeyFile();
            _cached = ReadKeyFile();
            return _cached;
        }
    }

    private DatabaseEncryptionKey ReadKeyFile()
    {
        byte[] fileBytes;

        try
        {
            fileBytes = File.ReadAllBytes(KeyFilePath);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            throw new DatabaseKeyException(
                "The EduFocus database key could not be read. Check the data folder permissions; no data was changed.",
                KeyFilePath,
                ex);
        }

        if (fileBytes.Length < Magic.Length + DpapiOverheadBytes || fileBytes.Length > MaxKeyFileBytes
            || !fileBytes.AsSpan(0, Magic.Length).SequenceEqual(Magic))
        {
            throw new DatabaseKeyException(
                "The EduFocus database key file is damaged. Restore the key file together with the database backup, or contact support. No data was changed.",
                KeyFilePath);
        }

        try
        {
            var protectedBytes = fileBytes.AsSpan(Magic.Length).ToArray();
            var keyBytes = Unprotect(protectedBytes);

            try
            {
                if (keyBytes.Length != DatabaseEncryptionKey.SizeBytes)
                    throw new CryptographicException("The key has an unexpected length.");

                return new DatabaseEncryptionKey(keyBytes);
            }
            finally
            {
                CryptographicOperations.ZeroMemory(keyBytes);
            }
        }
        catch (CryptographicException ex)
        {
            throw new DatabaseKeyException(
                "The EduFocus database key could not be unlocked on this machine. The key is device-bound; restore a backup made on this machine or contact support. No data was changed.",
                KeyFilePath,
                ex);
        }
    }

    private void CreateKeyFile()
    {
        var directory = Path.GetDirectoryName(KeyFilePath);
        var temporaryPath = $"{KeyFilePath}.{Guid.NewGuid():N}.tmp";

        try
        {
            if (!string.IsNullOrEmpty(directory))
                Directory.CreateDirectory(directory);

            WriteKeyFile(temporaryPath);

            try
            {
                File.Move(temporaryPath, KeyFilePath, overwrite: false);
            }
            catch (IOException) when (File.Exists(KeyFilePath))
            {
                // Another EduFocus process won the race; its key is used.
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or CryptographicException)
        {
            throw new DatabaseKeyException(
                "EduFocus could not create the database key. Run EduFocus as an administrator and check free disk space; no data was deleted.",
                KeyFilePath,
                ex);
        }
        finally
        {
            TryDelete(temporaryPath);
        }
    }

    private static void WriteKeyFile(string path)
    {
        var keyBytes = RandomNumberGenerator.GetBytes(DatabaseEncryptionKey.SizeBytes);
        byte[]? fileBytes = null;

        try
        {
            var protectedBytes = Protect(keyBytes);

            try
            {
                fileBytes = new byte[Magic.Length + protectedBytes.Length];
                Magic.CopyTo(fileBytes, 0);
                protectedBytes.CopyTo(fileBytes, Magic.Length);
            }
            finally
            {
                CryptographicOperations.ZeroMemory(protectedBytes);
            }

            using var stream = new FileStream(
                path,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None,
                bufferSize: 512,
                FileOptions.WriteThrough);
            stream.Write(fileBytes);
            stream.Flush(flushToDisk: true);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(keyBytes);

            if (fileBytes is not null)
                CryptographicOperations.ZeroMemory(fileBytes);
        }
    }

    private static byte[] Protect(byte[] keyBytes)
    {
        if (!OperatingSystem.IsWindows())
            throw new PlatformNotSupportedException("DPAPI is only available on Windows.");

        return ProtectedData.Protect(keyBytes, Entropy, DataProtectionScope.LocalMachine);
    }

    private static byte[] Unprotect(byte[] protectedBytes)
    {
        if (!OperatingSystem.IsWindows())
            throw new PlatformNotSupportedException("DPAPI is only available on Windows.");

        return ProtectedData.Unprotect(protectedBytes, Entropy, DataProtectionScope.LocalMachine);
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
