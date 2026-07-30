using R3.Domain.Common;

namespace R3.Domain.Inventory;

public sealed class Product : Entity<long>
{
    public int CompanyId { get; private set; }
    public string ProductCode { get; private set; } = string.Empty;
    public string? Barcode { get; private set; }
    public string ProductName { get; private set; } = string.Empty;
    public string? ShortName { get; private set; }
    public string? Description { get; private set; }
    public ProductType ProductType { get; private set; }
    public int? BrandId { get; private set; }
    public int? CategoryId { get; private set; }
    public int? ProductGroupId { get; private set; }
    public int BaseUnitId { get; private set; }
    public decimal VatRate { get; private set; }
    public decimal PurchasePrice { get; private set; }
    public decimal SalesPrice { get; private set; }
    public decimal WholesalePrice { get; private set; }
    public decimal CampaignPrice { get; private set; }
    public string CurrencyCode { get; private set; } = "TRY";
    public decimal CriticalStockLevel { get; private set; }
    public string? ShelfCode { get; private set; }
    public string? AisleCode { get; private set; }
    public long? SupplierAccountId { get; private set; }
    public string? ManufacturerCode { get; private set; }
    public string? CountryOfOrigin { get; private set; }
    public short WarrantyMonths { get; private set; }
    public bool IsActive { get; private set; }
    public bool IsDeleted { get; private set; }
    public byte[] RowVersion { get; private set; } = [];
}

public enum ProductType : byte
{
    Stock = 1,
    Service = 2,
    RawMaterial = 3,
    SemiFinished = 4,
    Finished = 5
}
