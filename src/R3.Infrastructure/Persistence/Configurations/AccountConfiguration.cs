using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using R3.Domain.Accounts;

namespace R3.Infrastructure.Persistence.Configurations;

internal sealed class AccountConfiguration : IEntityTypeConfiguration<Account>
{
    public void Configure(EntityTypeBuilder<Account> builder)
    {
        builder.ToTable("Account", "crm");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("AccountId");
        builder.Property(x => x.AccountCode).HasMaxLength(30).IsUnicode(false);
        builder.Property(x => x.LegalName).HasMaxLength(200);
        builder.Property(x => x.TradeName).HasMaxLength(200);
        builder.Property(x => x.CurrencyCode).HasMaxLength(3).IsFixedLength().IsUnicode(false);
        builder.Property(x => x.CreditLimit).HasPrecision(19, 4);
        builder.Property(x => x.RowVersion).IsRowVersion();
        builder.HasQueryFilter(x => !x.IsDeleted);
    }
}
