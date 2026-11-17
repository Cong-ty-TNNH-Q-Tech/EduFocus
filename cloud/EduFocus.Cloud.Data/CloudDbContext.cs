using Microsoft.EntityFrameworkCore;

namespace EduFocus.Cloud.Data;

public sealed class CloudDbContext(DbContextOptions<CloudDbContext> options) : DbContext(options)
{
}
