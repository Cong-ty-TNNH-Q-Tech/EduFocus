using Microsoft.Data.Sqlite;

namespace EduFocus.SqliteDataAccess.Persistence;

/// <summary>
/// File and backup locations for the local database. Defaults to
/// <c>%ProgramData%\EduFocus\edufocus.db</c>; the legacy DontBeLazy 1.x database
/// under <c>%AppData%</c> is never used (database_schema §1, §16, §17).
/// </summary>
public sealed class EduFocusDatabaseOptions
{
    public const string DefaultFolderName = "EduFocus";
    public const string DefaultFileName = "edufocus.db";
    public const string DefaultBackupFolderName = "backups";
    public const string DefaultKeyFileName = "edufocus.key";

    public static string DefaultDirectory =>
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
            DefaultFolderName);

    public static string DefaultDatabasePath =>
        Path.Combine(DefaultDirectory, DefaultFileName);

    public string DatabasePath { get; init; } = DefaultDatabasePath;

    /// <summary>
    /// DPAPI-protected database key. Defaults to <c>edufocus.key</c> next to
    /// the database, inside the Administrators/SYSTEM-only data directory
    /// (database_schema §16, §18).
    /// </summary>
    public string? KeyFilePath { get; init; }

    public string? BackupDirectory { get; init; }

    public int MaxBackups { get; init; } = 3;

    /// <summary>
    /// Restricts the data and backup folders to Administrators/SYSTEM on
    /// Windows. Tests disable this so temporary folders stay deletable, and
    /// tools that only read a copy can opt out (database_schema §16).
    /// </summary>
    public bool ProtectDataDirectory { get; init; } = true;

    public string ResolvedBackupDirectory =>
        string.IsNullOrWhiteSpace(BackupDirectory)
            ? Path.Combine(Path.GetDirectoryName(Path.GetFullPath(DatabasePath))!, DefaultBackupFolderName)
            : BackupDirectory;

    public string ResolvedKeyFilePath =>
        string.IsNullOrWhiteSpace(KeyFilePath)
            ? Path.Combine(Path.GetDirectoryName(Path.GetFullPath(DatabasePath))!, DefaultKeyFileName)
            : KeyFilePath;

    public string ConnectionString =>
        new SqliteConnectionStringBuilder
        {
            DataSource = DatabasePath,
            Mode = SqliteOpenMode.ReadWriteCreate
        }.ToString();
}
