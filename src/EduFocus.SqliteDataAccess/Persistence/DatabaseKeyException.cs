namespace EduFocus.SqliteDataAccess.Persistence;

/// <summary>
/// The database key is missing, corrupt or cannot be unlocked on this machine.
/// EduFocus fails closed: it never creates a new key over an encrypted database
/// and never deletes data (database_schema §18).
/// </summary>
public sealed class DatabaseKeyException : Exception
{
    public DatabaseKeyException(string message, string keyFilePath, Exception? innerException = null)
        : base(message, innerException)
    {
        KeyFilePath = keyFilePath;
    }

    public string KeyFilePath { get; }
}
