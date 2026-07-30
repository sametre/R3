using R3.Domain.Common;

namespace R3.Domain.Accounts;

public sealed class Account : Entity<long>
{
    public int CompanyId { get; private set; }
    public string AccountCode { get; private set; } = string.Empty;
    public AccountType AccountType { get; private set; }
    public string LegalName { get; private set; } = string.Empty;
    public string? TradeName { get; private set; }
    public string CurrencyCode { get; private set; } = "TRY";
    public decimal CreditLimit { get; private set; }
    public short PaymentTermDays { get; private set; }
    public bool IsActive { get; private set; }
    public bool IsDeleted { get; private set; }
    public byte[] RowVersion { get; private set; } = [];
}

public enum AccountType : byte
{
    Customer = 1,
    Supplier = 2,
    CustomerAndSupplier = 3,
    Personnel = 4,
    Other = 5
}
