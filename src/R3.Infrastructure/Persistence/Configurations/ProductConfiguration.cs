using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using R3.Domain.Inventory;

namespace R3.Infrastructure.Persistence.Configurations;

internal sealed class ProductConfiguration : IEntityTypeConfiguration<Product>
{
    public void Configure(EntityTypeBuilder<Product> builder)
    {
        builder.ToTable("Product", "inv");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("ProductId");
        builder.Property(x => x.ProductCode).HasMaxLength(40).IsUnicode(false);
        builder.Property(x => x.Barcode).HasMaxLength(50).IsUnicode(false);
        builder.Property(x => x.ProductName).HasMaxLength(200);
        builder.Property(x => x.ShortName).HasMaxLength(100);
        builder.Property(x => x.Description).HasMaxLength(1000);
        builder.Property(x => x.VatRate).HasPrecision(5, 2);
        builder.Property(x => x.PurchasePrice).HasPrecision(19, 4);
        builder.Property(x => x.SalesPrice).HasPrecision(19, 4);
        builder.Property(x => x.WholesalePrice).HasPrecision(19, 4);
        builder.Property(x => x.CampaignPrice).HasPrecision(19, 4);
        builder.Property(x => x.CriticalStockLevel).HasPrecision(19, 6);
        builder.Property(x => x.ShelfCode).HasMaxLength(30).IsUnicode(false);
        builder.Property(x => x.AisleCode).HasMaxLength(30).IsUnicode(false);
        builder.Property(x => x.ManufacturerCode).HasMaxLength(50).IsUnicode(false);
        builder.Property(x => x.CountryOfOrigin).HasMaxLength(2).IsFixedLength().IsUnicode(false);
        builder.Property(x => x.CurrencyCode).HasMaxLength(3).IsFixedLength().IsUnicode(false);
        builder.Property(x => x.RowVersion).IsRowVersion();
        builder.HasQueryFilter(x => !x.IsDeleted);
    }
}
