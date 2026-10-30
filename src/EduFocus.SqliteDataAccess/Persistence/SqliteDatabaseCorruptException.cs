namespace EduFocus.SqliteDataAccess.Persistence;

/// <summary>
/// Raised when the existing database fails the SQLite integrity check. The file
/// is left untouched so a backup can be restored (database_schema §17).
/// </summary>
public sealed class SqliteDatabaseCorruptException : Exception
{
    public string? BackupDirectory { get; }

    public SqliteDatabaseCorruptException(
        string message,
        string? backupDirectory,
        Exception? innerException = null)
        : base(message, innerException)
    {
        BackupDirectory = backupDirectory;
    }
}
