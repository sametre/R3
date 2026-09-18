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
    public Guid CompanyId { get; set; } public string Code { get; set; } = ""; public string Name { get; set; } = ""; public Guid? BrandId { get; set; } public Guid? CategoryId { get; set; } public Guid BaseUnitId { get; set; } public string ProductType { get; set; } = "PRODUCT"; public decimal VatRate { get; set; } public decimal PurchaseVatRate { get; set; } public decimal ExciseRate { get; set; } public decimal MinimumStock { get; set; } public decimal MaximumStock { get; set; } public decimal MinimumOrderQuantity { get; set; } public decimal OrderMultiple { get; set; } public bool IsSellable { get; set; } = true; public bool IsActive { get; set; } = true; public string? LegacySource { get; set; } public long? LegacyId { get; set; }
    public List<ProductBarcode> Barcodes { get; set; } = []; public List<ProductVariant> Variants { get; set; } = [];
}
public sealed class ProductBarcode : AuditableEntity { public Guid ProductId { get; set; } public Guid? VariantId { get; set; } public Guid? UnitId { get; set; } public string Barcode { get; set; } = ""; public decimal Quantity { get; set; } = 1; public bool IsPrimary { get; set; } public bool IsActive { get; set; } = true; }
public sealed class ProductVariant : AuditableEntity { public Guid ProductId { get; set; } public string Code { get; set; } = ""; public string Name { get; set; } = ""; public string? SizeCode { get; set; } public string? ColorCode { get; set; } public string? ModelCode { get; set; } public bool IsActive { get; set; } = true; }
public enum AccountType { Customer, Supplier, CustomerAndSupplier, Other }
public sealed class Account : AuditableEntity
{
    public Guid CompanyId { get; set; } public string Code { get; set; } = ""; public string Name { get; set; } = ""; public AccountType AccountType { get; set; } public string TaxOffice { get; set; } = ""; public string TaxNumber { get; set; } = ""; public string IdentityNumber { get; set; } = ""; public string Phone { get; set; } = ""; public string MobilePhone { get; set; } = ""; public string Email { get; set; } = ""; public decimal CreditLimit { get; set; } public decimal RiskLimit { get; set; } public bool IsActive { get; set; } = true; public string? LegacySource { get; set; } public long? LegacyId { get; set; } public List<AccountAddress> Addresses { get; set; } = [];
}
public sealed class AccountAddress : AuditableEntity { public Guid AccountId { get; set; } public string AddressType { get; set; } = "OTHER"; public string Title { get; set; } = ""; public string Country { get; set; } = "Türkiye"; public string City { get; set; } = ""; public string District { get; set; } = ""; public string AddressLine { get; set; } = ""; public string PostalCode { get; set; } = ""; public string Neighborhood { get; set; } = ""; public string Fax { get; set; } = ""; public string Website { get; set; } = ""; public bool IsDefault { get; set; } }
public sealed class AuditLog : AuditableEntity { public Guid? UserId { get; set; } public Guid? CompanyId { get; set; } public string EntityType { get; set; } = ""; public Guid EntityId { get; set; } public string Action { get; set; } = ""; public string? OldValues { get; set; } public string? NewValues { get; set; } }

