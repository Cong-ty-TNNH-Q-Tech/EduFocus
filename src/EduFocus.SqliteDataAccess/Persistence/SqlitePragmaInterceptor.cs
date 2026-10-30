using System.Data.Common;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace EduFocus.SqliteDataAccess.Persistence;

/// <summary>
/// SQLite keys and pragmas are per connection, so the SQLCipher key and foreign
/// keys are applied every time EF opens a connection. The key must be applied
/// before any other statement, including <c>PRAGMA foreign_keys</c>
/// (database_schema §18). WAL is a persistent database setting and is enabled
/// once by <see cref="SqliteDatabaseInitializer"/>.
/// </summary>
public sealed class SqlitePragmaInterceptor : DbConnectionInterceptor
{
    private const string PragmaSql = "PRAGMA foreign_keys = ON;";

    private readonly IDatabaseKeyStore _keyStore;

    public SqlitePragmaInterceptor(IDatabaseKeyStore keyStore)
    {
        ArgumentNullException.ThrowIfNull(keyStore);
        _keyStore = keyStore;
    }

    public override void ConnectionOpened(DbConnection connection, ConnectionEndEventData eventData)
    {
        ApplyKey(connection);
        ApplyPragmas(connection);
    }

    public override async Task ConnectionOpenedAsync(
        DbConnection connection,
        ConnectionEndEventData eventData,
        CancellationToken cancellationToken = default)
    {
        ApplyKey(connection);

        await using var command = connection.CreateCommand();
        command.CommandText = PragmaSql;
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    private void ApplyKey(DbConnection connection)
    {
        if (connection is not SqliteConnection sqliteConnection)
            throw new InvalidOperationException("The EduFocus database must be a SQLite connection.");

        SqliteCipher.ApplyKey(sqliteConnection, _keyStore.Get());
    }

    private static void ApplyPragmas(DbConnection connection)
    {
        using var command = connection.CreateCommand();
        command.CommandText = PragmaSql;
        command.ExecuteNonQuery();
    }
}
