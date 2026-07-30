namespace R3.Application.MasterData;

public interface IRetailMasterDataService
{
    Task<IReadOnlyList<ProductListItem>> GetProductsAsync(int companyId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<ProductVariantListItem>> GetVariantsAsync(int companyId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<AccountListItem>> GetAccountsAsync(int companyId, byte? accountType = null, CancellationToken cancellationToken = default);
    Task<MasterDataLookups> GetLookupsAsync(int companyId, CancellationToken cancellationToken = default);
    Task<long> SaveProductAsync(ProductSaveRequest request, CancellationToken cancellationToken = default);
    Task<long> SaveAccountAsync(AccountSaveRequest request, CancellationToken cancellationToken = default);
}

public sealed record LookupItem(int Id, string Code, string Name)
{
    public string DisplayText => $"{Code} • {Name}";
}

public sealed record MasterDataLookups(
    IReadOnlyList<LookupItem> Units,
    IReadOnlyList<LookupItem> Brands,
    IReadOnlyList<LookupItem> Categories,
    IReadOnlyList<LookupItem> ProductGroups,
    IReadOnlyList<LookupItem> PaymentPlans);

public sealed record ProductListItem(
    long ProductId, string ProductCode, string ProductName, string? ShortName, string ProductType,
    string UnitCode, string? Barcode, decimal VatRate, decimal PurchasePrice, decimal SalesPrice,
    decimal WholesalePrice, decimal CampaignPrice, decimal CriticalStockLevel, string? ShelfCode,
    string? AisleCode, string Status, decimal MinimumStock, decimal? MaximumStock,
    string? Description, string? ManufacturerCode, string? CountryOfOrigin, short WarrantyMonths,
    bool TrackLot, bool TrackSerial);

public sealed record ProductVariantListItem(
    long ProductVariantId, string ProductCode, string ProductName, string VariantCode,
    string? ColorName, string? SizeName, string? MainBarcode, decimal? PurchasePrice,
    decimal? SalesPrice, string? ShelfCode, string? AisleCode, string Status);

public sealed record ProductVariantSaveRequest(
    string VariantCode, string? ColorCode, string? ColorName, string? SizeCode, string? SizeName,
    string? Barcode, decimal? PurchasePrice, decimal? SalesPrice, decimal? WholesalePrice,
    decimal? CampaignPrice, string? ShelfCode, string? AisleCode, bool TrackLot, bool TrackSerial);

public sealed record ProductSaveRequest(
    int CompanyId, int UserId, string ProductCode, string ProductName, string? ShortName,
    string? Description, byte ProductType, int BaseUnitId, int? BrandId, int? CategoryId,
    int? ProductGroupId, string? MainBarcode, decimal VatRate, decimal PurchasePrice,
    decimal SalesPrice, decimal WholesalePrice, decimal CampaignPrice, decimal MinimumStock,
    decimal? MaximumStock, decimal CriticalStock, string? ShelfCode, string? AisleCode,
    string? ManufacturerCode, string? CountryOfOrigin, short WarrantyMonths,
    bool TrackLot, bool TrackSerial, bool IsActive, IReadOnlyList<ProductVariantSaveRequest> Variants,
    long? ProductId = null);

public sealed record AccountListItem(
    long AccountId, string AccountCode, string LegalName, string AccountType, string? TaxNumber,
    string? IdentityNumber, string? City, string? Phone, decimal Balance, string CurrencyCode, string Status,
    byte AccountTypeId, string? TradeName, string? TaxOffice, string? MersisNumber, decimal CreditLimit,
    short PaymentTermDays, string? Email, string? PriceListCode, string? DiscountGroupCode,
    int? PaymentPlanId, bool IsEInvoiceUser, string? EInvoiceAlias, string? Notes,
    string? District, string? Address);

public sealed record AccountSaveRequest(
    int CompanyId, int UserId, string AccountCode, byte AccountType, string LegalName,
    string? TradeName, string? TaxOffice, string? TaxNumber, string? IdentityNumber,
    string? MersisNumber, string CurrencyCode, decimal CreditLimit, short PaymentTermDays,
    string? PriceListCode, string? DiscountGroupCode, int? PaymentPlanId,
    string? Phone, string? Email, string? City, string? District, string? Address,
    bool IsEInvoiceUser, string? EInvoiceAlias, string? Notes, bool IsActive,
    long? AccountId = null);
