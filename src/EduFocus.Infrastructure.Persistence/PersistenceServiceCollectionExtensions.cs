using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace EduFocus.Infrastructure.Persistence;

public static class PersistenceServiceCollectionExtensions
{
    public static IServiceCollection AddEduFocusPersistence(this IServiceCollection services, string databasePath)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentException.ThrowIfNullOrWhiteSpace(databasePath);
        var fullPath = Path.GetFullPath(databasePath);
        var connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = fullPath,
            Mode = SqliteOpenMode.ReadWriteCreate,
            ForeignKeys = true,
            Pooling = false
        }.ToString();
        services.AddDbContext<AppDbContext>(options => options.UseSqlite(connectionString, sqlite => sqlite.MigrationsAssembly(typeof(AppDbContext).Assembly.FullName)));
        services.AddSingleton<IDataDirectoryProtector, WindowsDataDirectoryProtector>();
        services.AddSingleton<DatabaseInitializer>();
        services.AddSingleton<DatabaseRecoveryService>();
        services.AddScoped<ParentCredentialService>();
        services.AddScoped<OnboardingService>();
        services.AddScoped<ProtectionCoordinator>();
        return services;
    }
}
