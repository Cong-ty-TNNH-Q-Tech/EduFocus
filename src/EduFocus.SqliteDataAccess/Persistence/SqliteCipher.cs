using Microsoft.Data.Sqlite;
using SQLitePCL;

namespace EduFocus.SqliteDataAccess.Persistence;

public enum SqliteDatabaseFileState
{
    Missing,
    Empty,
    Plaintext,
    Encrypted
}

/// <summary>
/// SQLCipher helpers. The SQLite backup API refuses encrypted databases, so
/// backups and the plaintext-to-encrypted upgrade use <c>sqlcipher_export</c>
/// through an attached destination that is keyed with
/// <c>sqlite3_key_v2</c> before it is touched (database_schema §18).
/// </summary>
public static class SqliteCipher
{
    private const string ExportAlias = "cipher_export";
    private static ReadOnlySpan<byte> PlaintextHeader => "SQLite format 3\0"u8;

    public static SqliteDatabaseFileState GetFileState(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        if (!File.Exists(path))
            return SqliteDatabaseFileState.Missing;

        using var stream = new FileStream(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.ReadWrite | FileShare.Delete);

        if (stream.Length == 0)
            return SqliteDatabaseFileState.Empty;

        Span<byte> header = stackalloc byte[16];
        var read = stream.ReadAtLeast(header, header.Length, throwOnEndOfStream: false);

        return read == header.Length && header.SequenceEqual(PlaintextHeader)
            ? SqliteDatabaseFileState.Plaintext
            : SqliteDatabaseFileState.Encrypted;
    }

    public static void ApplyKey(SqliteConnection connection, DatabaseEncryptionKey key)
    {
        ArgumentNullException.ThrowIfNull(connection);
        ArgumentNullException.ThrowIfNull(key);

        key.ApplyTo(connection);
    }

    /// <summary>
    /// Exports the source database into a new database encrypted with
    /// <paramref name="key"/>. The source connection must already be keyed when
    /// the source is encrypted and must use <see cref="SqliteOpenMode.ReadWriteCreate"/>
    /// so SQLite can create the attached destination file.
    /// </summary>
    public static void ExportEncrypted(
        SqliteConnection sourceConnection,
        string destinationPath,
        DatabaseEncryptionKey key)
    {
        ArgumentNullException.ThrowIfNull(sourceConnection);
        ArgumentException.ThrowIfNullOrWhiteSpace(destinationPath);
        ArgumentNullException.ThrowIfNull(key);

        var destination = Path.GetFullPath(destinationPath);

        if (File.Exists(destination))
            throw new SqliteException("The export destination already exists.", 1);

        ExecuteNonQuery(
            sourceConnection,
            $"ATTACH DATABASE '{destination.Replace("'", "''")}' AS {ExportAlias};");

        try
        {
            key.ApplyTo(sourceConnection.Handle!, utf8z.FromString(ExportAlias));

            using var command = sourceConnection.CreateCommand();
            command.CommandText = $"SELECT sqlcipher_export('{ExportAlias}');";
            command.ExecuteNonQuery();
        }
        finally
        {
            try
            {
                ExecuteNonQuery(sourceConnection, $"DETACH DATABASE {ExportAlias};");
            }
            catch (SqliteException)
            {
                // Closing the connection detaches anyway; the destination is
                // complete and is verified by the caller before it is used.
            }
        }
    }

    private static void ExecuteNonQuery(SqliteConnection connection, string sql)
    {
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.ExecuteNonQuery();
    }
}
