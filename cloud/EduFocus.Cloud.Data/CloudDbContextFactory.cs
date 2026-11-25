using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace EduFocus.Cloud.Data;

public sealed class CloudDbContextFactory : IDesignTimeDbContextFactory<CloudDbContext>
{
    public CloudDbContext CreateDbContext(string[] args)
    {
        var connectionString = CloudDatabaseConnection.Resolve(
            Environment.GetEnvironmentVariable(CloudDatabaseConnection.EnvironmentVariable),
            "Development");

        var options = new DbContextOptionsBuilder<CloudDbContext>()
            .UseSqlite(connectionString)
            .Options;

        return new CloudDbContext(options);
    }
}
