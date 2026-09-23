using R3.Infrastructure;

namespace R3.Desktop.WinForms.Features.Products;

/// <summary>
/// Stok Kartları filter state → the existing service query (LocalProductService.SearchPage). Kept apart from the
/// view so the mapping is unit-tested and the view stays layout + events only.
/// </summary>
public sealed record ProductListFilter(
    string Search = "",
    string? Status = "active",          // "active" | "passive" | null (all)
    string? ProductType = null,
    string? BrandId = null,
    string? CategoryId = null,
    bool BelowMinimum = false,
    bool NegativeStock = false,
    bool OutOfStock = false,
    string SortKey = "code",
    bool SortDescending = false,
    int Page = 1,
    int PageSize = 50)
{
    public static readonly int[] PageSizes = [50, 100, 200];

    public ProductListQuery ToQuery(string companyId) => new(
        companyId,
        Search: string.IsNullOrWhiteSpace(Search) ? null : Search.Trim(),
        BrandId: Empty(BrandId), CategoryId: Empty(CategoryId), ProductType: Empty(ProductType),
        ActiveOnly: Status switch { "active" => true, "passive" => false, _ => null },
        NegativeStockOnly: NegativeStock, OutOfStockOnly: OutOfStock, BelowMinimumOnly: BelowMinimum,
        Page: Math.Max(1, Page), PageSize: PageSizes.Contains(PageSize) ? PageSize : 50,
        SortColumn: SortKey, SortDescending: SortDescending);

    /// <summary>True when anything narrows the default list (search, filters, toggles, non-default status).</summary>
    public bool HasCriteria => !string.IsNullOrWhiteSpace(Search) || Status != "active" || ProductType != null || BrandId != null || CategoryId != null || BelowMinimum || NegativeStock || OutOfStock;

    public static int PageCount(int totalCount, int pageSize) => Math.Max(1, (int)Math.Ceiling(totalCount / (double)Math.Max(1, pageSize)));

    private static string? Empty(string? value) => string.IsNullOrWhiteSpace(value) ? null : value;
}

/// <summary>Turkish labels for product enums (same labels as the WPF client).</summary>
public static class ProductText
{
    public static readonly (string Value, string Text)[] Types =
        [("Stock", "Stok"), ("Service", "Hizmet"), ("Bundle", "Takım / Set"), ("RawMaterial", "Hammadde"), ("FinishedGood", "Mamul")];

    public static string Type(string? value) => Types.FirstOrDefault(t => t.Value == value).Text ?? (string.IsNullOrEmpty(value) ? "—" : value);
}
