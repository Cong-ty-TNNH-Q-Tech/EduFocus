namespace EduFocus.SqliteDataAccess.Persistence;

/// <summary>
/// Supplies the SQLCipher key that protects the EduFocus database. Reads never
/// create or replace a key; only <see cref="LoadOrCreate"/> may create one for
/// a database that does not exist yet or is still plaintext.
/// </summary>
public interface IDatabaseKeyStore
{
    string KeyFilePath { get; }

    bool Exists { get; }

    /// <summary>Returns the key, or null when no key file exists.</summary>
    DatabaseEncryptionKey? TryLoad();

    /// <summary>Returns the key; throws <see cref="DatabaseKeyException"/> when it is missing.</summary>
    DatabaseEncryptionKey Get();

    /// <summary>
    /// Atomically creates a key when none exists. An existing key is returned
    /// unchanged; a corrupt key is never overwritten.
    /// </summary>
    DatabaseEncryptionKey LoadOrCreate();
}
