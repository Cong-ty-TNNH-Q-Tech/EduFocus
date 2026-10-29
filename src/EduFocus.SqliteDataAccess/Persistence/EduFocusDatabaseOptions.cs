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

    public static string DefaultDirectory =>
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
            DefaultFolderName);

    public static string DefaultDatabasePath =>
        Path.Combine(DefaultDirectory, DefaultFileName);

    public string DatabasePath { get; init; } = DefaultDatabasePath;

    public string? BackupDirectory { get; init; }

    public int MaxBackups { get; init; } = 3;

    public string ResolvedBackupDirectory =>
        string.IsNullOrWhiteSpace(BackupDirectory)
            ? Path.Combine(Path.GetDirectoryName(Path.GetFullPath(DatabasePath))!, DefaultBackupFolderName)
            : BackupDirectory;

    public string ConnectionString =>
        new SqliteConnectionStringBuilder
        {
            DataSource = DatabasePath,
            Mode = SqliteOpenMode.ReadWriteCreate
        }.ToString();
}
