using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace EduFocus.SqliteDataAccess.Persistence;

/// <summary>
/// Used by <c>dotnet ef</c> only. Migrations do not open the database.
/// </summary>
public sealed class EduFocusDbContextFactory : IDesignTimeDbContextFactory<EduFocusDbContext>
{
    public EduFocusDbContext CreateDbContext(string[] args)
    {
        var options = new EduFocusDatabaseOptions();

        var builder = new DbContextOptionsBuilder<EduFocusDbContext>()
            .UseSqlite(options.ConnectionString);

        return new EduFocusDbContext(builder.Options);
    }
}
