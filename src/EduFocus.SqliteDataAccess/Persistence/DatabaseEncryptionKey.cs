using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Data.Sqlite;

namespace EduFocus.SqliteDataAccess.Persistence;

/// <summary>
/// A 256-bit key for SQLCipher. The key lives only in memory and is applied
/// with <c>sqlite3_key</c> before any other statement on the connection
/// (database_schema §18). It is never written to logs, the connection string
/// or an exception message.
/// </summary>
/// <remarks>
/// The key is applied in SQLCipher's raw-key form (<c>x'…'</c>). The key is
/// already 32 bytes from a CSPRNG, so the KDF adds no entropy but costs about
/// half a second per physical connection, which would dominate startup,
/// backups and every non-pooled open.
/// </remarks>
[DebuggerDisplay("DatabaseEncryptionKey(redacted)")]
public sealed class DatabaseEncryptionKey
{
    public const int SizeBytes = 32;

    // A pooled SqliteConnection reuses the same native sqlite3 handle. Calling
    // sqlite3_key again reinitialises the SQLCipher codec, which resets
    // connection state such as auto_vacuum and repeats key derivation, so each
    // native handle is keyed exactly once.
    private static readonly ConditionalWeakTable<SQLitePCL.sqlite3, object> KeyedHandles = new();
    private static readonly object KeyedMarker = new();

    private readonly byte[] _bytes;

    public DatabaseEncryptionKey(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length != SizeBytes)
            throw new ArgumentException($"A database key must be {SizeBytes} bytes long.", nameof(bytes));

        _bytes = bytes.ToArray();
    }

    public void ApplyTo(SqliteConnection connection)
    {
        ArgumentNullException.ThrowIfNull(connection);

        var handle = connection.Handle
            ?? throw new InvalidOperationException("The SQLite connection must be open before the database key is applied.");

        if (KeyedHandles.TryGetValue(handle, out _))
            return;

        var rawKey = CreateRawKey();

        try
        {
            var result = SQLitePCL.raw.sqlite3_key(handle, rawKey);

            if (result != SQLitePCL.raw.SQLITE_OK)
                throw new SqliteException("The database encryption key could not be applied.", result);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(rawKey);
        }

        KeyedHandles.AddOrUpdate(handle, KeyedMarker);
    }

    /// <summary>Keys an attached database such as a <c>sqlcipher_export</c> target.</summary>
    internal void ApplyTo(SQLitePCL.sqlite3 handle, SQLitePCL.utf8z databaseName)
    {
        var rawKey = CreateRawKey();

        try
        {
            var result = SQLitePCL.raw.sqlite3_key_v2(handle, databaseName, rawKey);

            if (result != SQLitePCL.raw.SQLITE_OK)
                throw new SqliteException("The export database key could not be applied.", result);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(rawKey);
        }
    }

    private byte[] CreateRawKey() => Encoding.ASCII.GetBytes($"x'{Convert.ToHexString(_bytes)}'");

    public static DatabaseEncryptionKey CreateRandom() =>
        new(RandomNumberGenerator.GetBytes(SizeBytes));

    public override string ToString() => "DatabaseEncryptionKey(redacted)";
}
