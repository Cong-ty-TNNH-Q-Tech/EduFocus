using Microsoft.Data.Sqlite;

namespace EduFocus.Infrastructure.Persistence;

internal static class SqliteIntegrity
{
    public static async Task VerifyAsync(string databasePath, CancellationToken cancellationToken)
    {
        var cs = new SqliteConnectionStringBuilder { DataSource = databasePath, Mode = SqliteOpenMode.ReadOnly, Pooling = false }.ToString();
        await using var connection = new SqliteConnection(cs);
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = "PRAGMA integrity_check;";
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var resultCount = 0;
        while (await reader.ReadAsync(cancellationToken))
        {
            resultCount++;
            var result = reader.GetString(0);
            if (!string.Equals(result, "ok", StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException($"SQLite integrity check failed: {result}");
        }
        if (resultCount != 1) throw new InvalidDataException("SQLite integrity check returned an unexpected result.");
    }
}
