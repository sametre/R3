namespace R3.Domain;

public abstract class AuditableEntity
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}

public sealed class OrganizationCompany : AuditableEntity
{
    public string Code { get; set; } = ""; public string NormalizedCode { get; set; } = ""; public string Name { get; set; } = ""; public string LegalName { get; set; } = "";
    public string TaxOffice { get; set; } = ""; public string TaxNumber { get; set; } = ""; public string Phone { get; set; } = ""; public string Email { get; set; } = ""; public string Address { get; set; } = ""; public bool IsActive { get; set; } = true;
    public List<Branch> Branches { get; set; } = []; public List<Warehouse> Warehouses { get; set; } = [];
}
public sealed class Branch : AuditableEntity
{
    public Guid CompanyId { get; set; } public OrganizationCompany? Company { get; set; } public string Code { get; set; } = ""; public string NormalizedCode { get; set; } = ""; public string Name { get; set; } = ""; public bool IsActive { get; set; } = true; public List<Warehouse> Warehouses { get; set; } = [];
}
public sealed class Warehouse : AuditableEntity
{
    public Guid CompanyId { get; set; } public OrganizationCompany? Company { get; set; } public Guid BranchId { get; set; } public Branch? Branch { get; set; } public string Code { get; set; } = ""; public string NormalizedCode { get; set; } = ""; public string Name { get; set; } = ""; public string WarehouseType { get; set; } = "GENERAL"; public bool IsActive { get; set; } = true;
}
public sealed class Brand : AuditableEntity { public Guid CompanyId { get; set; } public string Code { get; set; } = ""; public string Name { get; set; } = ""; public bool IsActive { get; set; } = true; }
public sealed class Category : AuditableEntity { public Guid CompanyId { get; set; } public Guid? ParentId { get; set; } public string Code { get; set; } = ""; public string Name { get; set; } = ""; public bool IsActive { get; set; } = true; }
public sealed class Unit : AuditableEntity { public Guid CompanyId { get; set; } public string Code { get; set; } = ""; public string Name { get; set; } = ""; public int DecimalPlaces { get; set; } = 2; public bool IsActive { get; set; } = true; }
public sealed class Product : AuditableEntity
{
    public Guid CompanyId { get; set; } public string Code { get; set; } = ""; public string Name { get; set; } = ""; public string ParentCode { get; set; } = ""; public Guid? BrandId { get; set; } public Guid? CategoryId { get; set; } public Guid BaseUnitId { get; set; } public string ProductType { get; set; } = "PRODUCT"; public bool IsDefinitionComplete { get; set; } public decimal VatRate { get; set; } public decimal PurchaseVatRate { get; set; } public decimal ExciseRate { get; set; } public decimal ExciseUnitPrice { get; set; } public bool CanQuote { get; set; } = true; public bool AllowFreeIssue { get; set; } public bool IsBundle { get; set; } public decimal MinimumStock { get; set; } public decimal MaximumStock { get; set; } public decimal MinimumOrderQuantity { get; set; } public decimal OrderMultiple { get; set; } public bool IsSellable { get; set; } = true; public bool IsActive { get; set; } = true; public string? LegacySource { get; set; } public long? LegacyId { get; set; }
    public List<ProductBarcode> Barcodes { get; set; } = []; public List<ProductVariant> Variants { get; set; } = []; public List<ProductUnit> Units { get; set; } = []; public List<ProductSupplier> Suppliers { get; set; } = [];
}
public sealed class ProductBarcode : AuditableEntity { public Guid ProductId { get; set; } public Guid? VariantId { get; set; } public Guid? UnitId { get; set; } public string Barcode { get; set; } = ""; public decimal Quantity { get; set; } = 1; public bool IsPrimary { get; set; } public bool IsActive { get; set; } = true; }
public sealed class ProductVariant : AuditableEntity { public Guid ProductId { get; set; } public string Code { get; set; } = ""; public string Name { get; set; } = ""; public string? SizeCode { get; set; } public string? SizeType { get; set; } public string? ColorCode { get; set; } public string? ModelCode { get; set; } public bool IsActive { get; set; } = true; }

// Canonical replacement for ASB's STOKBIRIM: a product can define several units (sales/purchase/base)
// with an explicit conversion factor to the base unit, instead of a single fixed unit on Product.
public sealed class ProductUnit : AuditableEntity { public Guid ProductId { get; set; } public Guid UnitId { get; set; } public int Sequence { get; set; } = 1; public decimal ConversionFactor { get; set; } = 1; public bool IsBaseUnit { get; set; } public bool IsSalesUnit { get; set; } = true; public bool IsPurchaseUnit { get; set; } = true; public bool IsActive { get; set; } = true; public string? LegacySource { get; set; } public long? LegacyId { get; set; } }

// Canonical replacement for ASB's STOKTEDARIKCI: per-product supplier relationships, resolved through
// the shared Account aggregate rather than a parallel supplier master.
public sealed class ProductSupplier : AuditableEntity { public Guid ProductId { get; set; } public Guid SupplierAccountId { get; set; } public string SupplierProductCode { get; set; } = ""; public bool IsActive { get; set; } = true; public int LeadTimeDays { get; set; } public int ExtraLeadTimeDays { get; set; } public int Priority { get; set; } = 1; public decimal? MinimumOrderQuantity { get; set; } public string? LegacySource { get; set; } public long? LegacyId { get; set; } }

public enum LotTrackingType { None, Lot, Serial }
// Company-level stock/order policy today (§19); shaped so a future WarehouseProductPolicy can override
// specific fields per warehouse without changing this table's meaning.
public sealed class ProductInventoryPolicy
{
    public Guid ProductId { get; set; } public decimal MinimumStock { get; set; } public decimal MaximumStock { get; set; } public decimal MinimumOrderQuantity { get; set; } public decimal OrderMultiple { get; set; } public int DeliveryLeadTimeDays { get; set; } public int MaximumDeliveryLeadTimeDays { get; set; } public LotTrackingType LotTrackingType { get; set; } = LotTrackingType.None; public int PieceCount { get; set; } public string ShipmentLocationType { get; set; } = ""; public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}

// E-commerce channel mapping placeholder (§21): no integration backend exists yet, so this only models
// the shape a future sync engine needs (external identifiers + last sync timestamps), not sync behavior.
public sealed class ProductChannelMapping : AuditableEntity { public Guid ProductId { get; set; } public string ChannelCode { get; set; } = ""; public string ExternalProductId { get; set; } = ""; public string ExternalVariantId { get; set; } = ""; public string ExternalSku { get; set; } = ""; public bool IsPublished { get; set; } public DateTime? InventorySyncedAt { get; set; } public DateTime? PriceSyncedAt { get; set; } }
public enum AccountType { Customer, Supplier, CustomerAndSupplier, Other }
public sealed class Account : AuditableEntity
{
    public Guid CompanyId { get; set; } public string Code { get; set; } = ""; public string Name { get; set; } = ""; public AccountType AccountType { get; set; } public string TaxOffice { get; set; } = ""; public string TaxNumber { get; set; } = ""; public string IdentityNumber { get; set; } = ""; public string Phone { get; set; } = ""; public string MobilePhone { get; set; } = ""; public string Email { get; set; } = ""; public decimal CreditLimit { get; set; } public decimal RiskLimit { get; set; } public bool IsActive { get; set; } = true; public string? LegacySource { get; set; } public long? LegacyId { get; set; } public List<AccountAddress> Addresses { get; set; } = [];
}
public sealed class AccountAddress : AuditableEntity { public Guid AccountId { get; set; } public string AddressType { get; set; } = "OTHER"; public string Title { get; set; } = ""; public string Country { get; set; } = "Türkiye"; public string City { get; set; } = ""; public string District { get; set; } = ""; public string AddressLine { get; set; } = ""; public string PostalCode { get; set; } = ""; public string Neighborhood { get; set; } = ""; public string Fax { get; set; } = ""; public string Website { get; set; } = ""; public bool IsDefault { get; set; } }
public sealed class AuditLog : AuditableEntity { public Guid? UserId { get; set; } public Guid? CompanyId { get; set; } public string EntityType { get; set; } = ""; public Guid EntityId { get; set; } public string Action { get; set; } = ""; public string? OldValues { get; set; } public string? NewValues { get; set; } }

