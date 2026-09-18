using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace R3.Infrastructure;

public sealed class R3DbContextFactory : IDesignTimeDbContextFactory<R3DbContext>
{
    public R3DbContext CreateDbContext(string[] args)
    {
        var builder = new DbContextOptionsBuilder<R3DbContext>();
        builder.UseNpgsql("Host=localhost;Port=5432;Database=r3_dev;Username=r3;Password=design_time_only", options => options.MigrationsHistoryTable("__ef_migrations_history", "r3"));
        return new R3DbContext(builder.Options);
    }
}
