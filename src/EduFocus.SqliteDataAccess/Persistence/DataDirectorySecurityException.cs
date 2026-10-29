namespace EduFocus.SqliteDataAccess.Persistence;

/// <summary>
/// Raised when the data directory exists but cannot be restricted to
/// Administrators/SYSTEM. The message is safe to show to a parent and the
/// existing database is never deleted (database_schema §16).
/// </summary>
public sealed class DataDirectorySecurityException : Exception
{
    public DataDirectorySecurityException(string message, Exception? innerException = null)
        : base(message, innerException)
    {
    }
}
