using EduFocus.Cloud.Data;
using Microsoft.EntityFrameworkCore;

namespace EduFocus.Cloud.Tests;

public sealed class CloudDatabaseTests
{
    [Fact]
    public void Design_time_factory_uses_npgsql()
    {
        using var db = new CloudDbContextFactory().CreateDbContext([]);

        Assert.Equal("Npgsql.EntityFrameworkCore.PostgreSQL", db.Database.ProviderName);
    }

    [Fact]
    public void Connection_string_is_required_outside_development()
    {
        var original = Environment.GetEnvironmentVariable(CloudDatabaseConnection.EnvironmentVariable);
        try
        {
            Environment.SetEnvironmentVariable(CloudDatabaseConnection.EnvironmentVariable, null);

            var exception = Assert.Throws<InvalidOperationException>(
                () => CloudDatabaseConnection.Resolve(null, "Production"));

            Assert.Contains(CloudDatabaseConnection.EnvironmentVariable, exception.Message);
        }
        finally
        {
            Environment.SetEnvironmentVariable(CloudDatabaseConnection.EnvironmentVariable, original);
        }
    }

    [Fact]
    public async Task Test_database_is_reachable_when_configured()
    {
        var connectionString = Environment.GetEnvironmentVariable("EDUFOCUS_CLOUD_TEST_DB");
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            return;
        }

        await using var db = new CloudDbContext(
            new DbContextOptionsBuilder<CloudDbContext>().UseNpgsql(connectionString).Options);

        Assert.True(await db.Database.CanConnectAsync());
    }
}
