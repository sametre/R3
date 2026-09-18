namespace R3.Contracts;

public sealed record CompanyListItemDto(Guid Id, string Code, string Name, string LegalName, string TaxOffice, string TaxNumber, string Phone, string Email, bool IsActive);
public sealed record CompanyDetailDto(Guid Id, string Code, string Name, string LegalName, string TaxOffice, string TaxNumber, string Phone, string Email, string Address, bool IsActive, DateTime CreatedAt, DateTime UpdatedAt);
public sealed record CreateCompanyRequest(string Code, string Name, string LegalName, string TaxOffice, string TaxNumber, string Phone, string Email, string Address);
public sealed record UpdateCompanyRequest(string Code, string Name, string LegalName, string TaxOffice, string TaxNumber, string Phone, string Email, string Address, bool IsActive);
public sealed record BranchListItemDto(Guid Id, Guid CompanyId, string CompanyName, string Code, string Name, bool IsActive);
public sealed record BranchDetailDto(Guid Id, Guid CompanyId, string Code, string Name, bool IsActive, DateTime CreatedAt, DateTime UpdatedAt);
public sealed record CreateBranchRequest(Guid CompanyId, string Code, string Name);
public sealed record UpdateBranchRequest(Guid CompanyId, string Code, string Name, bool IsActive);
public sealed record WarehouseListItemDto(Guid Id, Guid CompanyId, string CompanyName, Guid BranchId, string BranchName, string Code, string Name, string WarehouseType, bool IsActive);
public sealed record WarehouseDetailDto(Guid Id, Guid CompanyId, Guid BranchId, string Code, string Name, string WarehouseType, bool IsActive, DateTime CreatedAt, DateTime UpdatedAt);
public sealed record CreateWarehouseRequest(Guid CompanyId, Guid BranchId, string Code, string Name, string WarehouseType);
public sealed record UpdateWarehouseRequest(Guid CompanyId, Guid BranchId, string Code, string Name, string WarehouseType, bool IsActive);
public sealed record LookupItemDto(Guid Id, string Code, string Name);
public sealed record ApiError(string Code, string Message, IReadOnlyDictionary<string, string[]> FieldErrors, string TraceId);
