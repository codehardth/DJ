using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace DJ.Infrastructure;

public class DjDesignTimeDbContext : IDesignTimeDbContextFactory<DjDbContext>
{
    public DjDbContext CreateDbContext(string[] args)
    {
        var connectionString = "Data Source=dj.db;";

        var optionsBuilder = new DbContextOptionsBuilder<DjDbContext>();

        optionsBuilder.UseSqlite(connectionString);

        return new DjDbContext(optionsBuilder.Options);
    }
}