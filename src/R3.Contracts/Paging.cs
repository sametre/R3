namespace R3.Contracts;
public sealed record PagedRequest(int Page = 1, int PageSize = 50, string? Search = null, string? SortBy = null, string? SortDirection = null)
{
    public int SafePage => Math.Max(1, Page); public int SafePageSize => Math.Clamp(PageSize, 1, 500);
}
public sealed record PagedResult<T>(IReadOnlyList<T> Items, int Page, int PageSize, int TotalCount)
{
    public int TotalPages => PageSize <= 0 ? 0 : (int)Math.Ceiling(TotalCount / (double)PageSize);
}
public sealed record AccountListItemDto(Guid Id, string Code, string Name, string AccountType, string TaxNumber, string Phone, string Email, bool IsActive);
public sealed record ProductListItemDto(Guid Id, string Code, string Name, string? BrandName, string? CategoryName, string UnitName, decimal VatRate, bool IsActive);
