namespace EduFocus.SqliteDataAccess.Persistence;

/// <summary>
/// Raised when a backup cannot be created, verified or restored. Callers must
/// keep the existing database file; the message is safe to show to a parent.
/// </summary>
public sealed class SqliteBackupException : Exception
{
    public SqliteBackupException(string message, Exception? innerException = null)
        : base(message, innerException)
    {
    }
}
