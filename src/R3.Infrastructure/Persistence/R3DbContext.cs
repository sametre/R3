using Microsoft.EntityFrameworkCore;
using R3.Domain.Accounts;
using R3.Domain.Inventory;

namespace R3.Infrastructure.Persistence;

public sealed class R3DbContext(DbContextOptions<R3DbContext> options) : DbContext(options)
{
    public DbSet<Account> Accounts => Set<Account>();
    public DbSet<Product> Products => Set<Product>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(R3DbContext).Assembly);
    }
}
