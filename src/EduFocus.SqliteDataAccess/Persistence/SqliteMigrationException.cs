namespace EduFocus.SqliteDataAccess.Persistence;

/// <summary>
/// Thrown when schema migration fails. The existing database file is never
/// deleted automatically; callers show the error and the backup path, if any
/// (database_schema §17).
/// </summary>
public sealed class SqliteMigrationException : Exception
{
    public SqliteMigrationException(string message, Exception innerException, string? backupPath)
        : base(message, innerException)
    {
        BackupPath = backupPath;
    }

    public string? BackupPath { get; }
}
