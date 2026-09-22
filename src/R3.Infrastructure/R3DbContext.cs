using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using R3.Domain;

namespace R3.Infrastructure;

public sealed class R3DbContext(DbContextOptions<R3DbContext> options) : DbContext(options)
{
    public DbSet<OrganizationCompany> Companies => Set<OrganizationCompany>(); public DbSet<Branch> Branches => Set<Branch>(); public DbSet<Warehouse> Warehouses => Set<Warehouse>();
    public DbSet<Brand> Brands => Set<Brand>(); public DbSet<Category> Categories => Set<Category>(); public DbSet<Unit> Units => Set<Unit>();
    public DbSet<Product> Products => Set<Product>(); public DbSet<ProductBarcode> ProductBarcodes => Set<ProductBarcode>(); public DbSet<ProductVariant> ProductVariants => Set<ProductVariant>();
    public DbSet<ProductUnit> ProductUnits => Set<ProductUnit>(); public DbSet<ProductSupplier> ProductSuppliers => Set<ProductSupplier>(); public DbSet<ProductInventoryPolicy> ProductInventoryPolicies => Set<ProductInventoryPolicy>(); public DbSet<ProductChannelMapping> ProductChannelMappings => Set<ProductChannelMapping>();
    public DbSet<Account> Accounts => Set<Account>(); public DbSet<AccountAddress> AccountAddresses => Set<AccountAddress>(); public DbSet<AuditLog> AuditLogs => Set<AuditLog>();
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema("r3");
        foreach (var entity in modelBuilder.Model.GetEntityTypes())
        {
            var table = entity.GetTableName(); if (table != null) entity.SetTableName(ToSnake(table));
            foreach (var property in entity.GetProperties()) property.SetColumnName(ToSnake(property.Name));
        }
        Configure<OrganizationCompany>(modelBuilder, b => { b.HasIndex(x => x.NormalizedCode).IsUnique(); b.Property(x => x.NormalizedCode).HasMaxLength(40).IsRequired(); });
        Configure<Branch>(modelBuilder, b => { b.HasIndex(x => new { x.CompanyId, x.NormalizedCode }).IsUnique(); b.Property(x => x.NormalizedCode).HasMaxLength(40).IsRequired(); b.HasOne(x => x.Company).WithMany(x => x.Branches).HasForeignKey(x => x.CompanyId); });
        Configure<Warehouse>(modelBuilder, b => { b.HasIndex(x => new { x.CompanyId, x.NormalizedCode }).IsUnique(); b.Property(x => x.NormalizedCode).HasMaxLength(40).IsRequired(); b.HasOne(x => x.Company).WithMany(x => x.Warehouses).HasForeignKey(x => x.CompanyId); b.HasOne(x => x.Branch).WithMany(x => x.Warehouses).HasForeignKey(x => x.BranchId); });
        Configure<Brand>(modelBuilder, b => b.HasIndex(x => new { x.CompanyId, x.Code }).IsUnique()); Configure<Category>(modelBuilder, b => b.HasIndex(x => new { x.CompanyId, x.Code }).IsUnique()); Configure<Unit>(modelBuilder, b => b.HasIndex(x => new { x.CompanyId, x.Code }).IsUnique());
        Configure<Product>(modelBuilder, b => { b.HasIndex(x => new { x.CompanyId, x.Code }).IsUnique(); b.HasIndex(x => new { x.CompanyId, x.Name }); b.HasIndex(x => new { x.LegacySource, x.LegacyId }); b.Property(x => x.VatRate).HasPrecision(5, 2); });
        Configure<ProductBarcode>(modelBuilder, b => { b.HasIndex(x => x.Barcode).IsUnique(); b.HasOne<Product>().WithMany(x => x.Barcodes).HasForeignKey(x => x.ProductId); b.Property(x => x.Quantity).HasPrecision(18, 4); });
        Configure<ProductVariant>(modelBuilder, b => { b.HasIndex(x => new { x.ProductId, x.Code }).IsUnique(); b.HasOne<Product>().WithMany(x => x.Variants).HasForeignKey(x => x.ProductId); });
        Configure<ProductUnit>(modelBuilder, b => { b.HasIndex(x => new { x.ProductId, x.UnitId }).IsUnique(); b.HasOne<Product>().WithMany(x => x.Units).HasForeignKey(x => x.ProductId); b.Property(x => x.ConversionFactor).HasPrecision(18, 6); });
        Configure<ProductSupplier>(modelBuilder, b => { b.HasIndex(x => new { x.ProductId, x.SupplierAccountId }).IsUnique(); b.HasOne<Product>().WithMany(x => x.Suppliers).HasForeignKey(x => x.ProductId); b.Property(x => x.MinimumOrderQuantity).HasPrecision(18, 4); });
        Configure<ProductChannelMapping>(modelBuilder, b => { b.HasIndex(x => new { x.ProductId, x.ChannelCode }).IsUnique(); b.HasOne<Product>().WithMany().HasForeignKey(x => x.ProductId); });
        modelBuilder.Entity<ProductInventoryPolicy>(b => { b.HasKey(x => x.ProductId); b.HasOne<Product>().WithOne().HasForeignKey<ProductInventoryPolicy>(x => x.ProductId); b.Property(x => x.LotTrackingType).HasConversion<string>(); b.Property(x => x.MinimumStock).HasPrecision(18, 4); b.Property(x => x.MaximumStock).HasPrecision(18, 4); b.Property(x => x.MinimumOrderQuantity).HasPrecision(18, 4); b.Property(x => x.OrderMultiple).HasPrecision(18, 4); });
        Configure<Account>(modelBuilder, b => { b.HasIndex(x => new { x.CompanyId, x.Code }).IsUnique(); b.HasIndex(x => new { x.CompanyId, x.Name }); b.HasIndex(x => x.TaxNumber); b.HasIndex(x => new { x.LegacySource, x.LegacyId }); b.Property(x => x.CreditLimit).HasPrecision(18, 2); b.Property(x => x.RiskLimit).HasPrecision(18, 2); b.Property(x => x.AccountType).HasConversion<string>(); });
        Configure<AccountAddress>(modelBuilder, b => b.HasOne<Account>().WithMany(x => x.Addresses).HasForeignKey(x => x.AccountId)); Configure<AuditLog>(modelBuilder, b => b.HasIndex(x => new { x.EntityType, x.EntityId }));
    }
    private static void Configure<T>(ModelBuilder modelBuilder, Action<EntityTypeBuilder<T>> configure) where T : class { var b = modelBuilder.Entity<T>(); configure(b); b.HasKey("Id"); b.Property<DateTime>("CreatedAt").IsRequired(); b.Property<DateTime>("UpdatedAt").IsRequired(); }
    private static string ToSnake(string value) => string.Concat(value.Select((c, i) => i > 0 && char.IsUpper(c) ? "_" + char.ToLowerInvariant(c) : char.ToLowerInvariant(c).ToString()));
}

