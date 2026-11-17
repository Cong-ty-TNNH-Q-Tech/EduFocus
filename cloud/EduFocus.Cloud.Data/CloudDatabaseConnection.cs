namespace EduFocus.Cloud.Data;

public static class CloudDatabaseConnection
{
    public const string EnvironmentVariable = "ConnectionStrings__Postgres";
    public const string DevelopmentFallback = "Host=localhost;Port=5432;Database=edufocus_cloud;Username=edufocus;Password=edufocus_dev";

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
            $"PostgreSQL connection string is required. Set {EnvironmentVariable} or ConnectionStrings:Postgres.");
    }
}
