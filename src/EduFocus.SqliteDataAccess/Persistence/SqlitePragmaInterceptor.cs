using System.Data.Common;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace EduFocus.SqliteDataAccess.Persistence;

/// <summary>
/// SQLite pragmas are per connection, so foreign keys must be enabled every time
/// a connection opens. WAL is a persistent database setting and is enabled once by
/// <see cref="SqliteDatabaseInitializer"/>; setting it here would fail on the
/// read-only connection EF uses to check whether the file exists.
/// </summary>
public sealed class SqlitePragmaInterceptor : DbConnectionInterceptor
{
    private const string PragmaSql = "PRAGMA foreign_keys = ON;";

    public override void ConnectionOpened(DbConnection connection, ConnectionEndEventData eventData) =>
        ApplyPragmas(connection);

    public override async Task ConnectionOpenedAsync(
        DbConnection connection,
        ConnectionEndEventData eventData,
        CancellationToken cancellationToken = default)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = PragmaSql;
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    private static void ApplyPragmas(DbConnection connection)
    {
        using var command = connection.CreateCommand();
        command.CommandText = PragmaSql;
        command.ExecuteNonQuery();
    }
}
