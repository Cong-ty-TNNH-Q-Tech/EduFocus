namespace EduFocus.Cloud.Data;

public static class CloudDatabaseConnection
{
    public const string EnvironmentVariable = "ConnectionStrings__Sqlite";
    public const string DevelopmentFallback =
        "Data Source=edufocus-cloud.db;Foreign Keys=True;Default Timeout=30";

    public static string Resolve(string? configured, string? environmentName)
    {
        var value = configured ?? Environment.GetEnvironmentVariable(EnvironmentVariable);
        if (!string.IsNullOrWhiteSpace(value))
        {
            return value;
        }

        if (string.Equals(environmentName, "Development", StringComparison.OrdinalIgnoreCase))
        {
            return DevelopmentFallback;
        }

        throw new InvalidOperationException(
            $"SQLite connection string is required. Set {EnvironmentVariable} or ConnectionStrings:Sqlite.");
    }
}
