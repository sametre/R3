using System.Data;
using R3.Desktop.WinForms.Features.Products;

namespace R3.Desktop.WinForms.Tests;

public sealed class ProductListFilterTests
{
    private const string Company = "c1";

    [Fact]
    public void DefaultFilterListsActiveProductsByCodeFirstPage()
    {
        var q = new ProductListFilter().ToQuery(Company);
        Assert.Equal(Company, q.CompanyId);
        Assert.True(q.ActiveOnly);
        Assert.Null(q.Search);
        Assert.Equal(("code", false, 1, 50), (q.SortColumn, q.SortDescending, q.Page, q.PageSize));
        Assert.False(new ProductListFilter().HasCriteria);
    }

    [Theory]
    [InlineData("active", true)]
    [InlineData("passive", false)]
    [InlineData(null, null)]
    public void StatusMapsToActiveOnly(string? status, bool? expected) =>
        Assert.Equal(expected, new ProductListFilter(Status: status).ToQuery(Company).ActiveOnly);

    [Fact]
    public void BlankFiltersBecomeNullAndSearchIsTrimmed()
    {
        var q = new ProductListFilter(Search: "  8690001  ", BrandId: "", CategoryId: " ", ProductType: "").ToQuery(Company);
        Assert.Equal("8690001", q.Search);
        Assert.Null(q.BrandId); Assert.Null(q.CategoryId); Assert.Null(q.ProductType);
    }

    [Fact]
    public void TogglesSortAndPagingPassThrough()
    {
        var q = new ProductListFilter(BelowMinimum: true, NegativeStock: true, OutOfStock: true, SortKey: "available", SortDescending: true, Page: 7, PageSize: 200).ToQuery(Company);
        Assert.True(q.BelowMinimumOnly && q.NegativeStockOnly && q.OutOfStockOnly);
        Assert.Equal(("available", true, 7, 200), (q.SortColumn, q.SortDescending, q.Page, q.PageSize));
    }

    [Theory]
    [InlineData(0, 1)]
    [InlineData(-5, 1)]
    public void PageNeverBelowOne(int page, int expected) => Assert.Equal(expected, new ProductListFilter(Page: page).ToQuery(Company).Page);

    [Fact]
    public void UnsupportedPageSizeFallsBackTo50() => Assert.Equal(50, new ProductListFilter(PageSize: 5000).ToQuery(Company).PageSize);

    [Theory]
    [InlineData(35095, 50, 702)]
    [InlineData(0, 50, 1)]
    [InlineData(50, 50, 1)]
    [InlineData(51, 50, 2)]
    public void PageCount(int total, int size, int expected) => Assert.Equal(expected, ProductListFilter.PageCount(total, size));

    [Fact]
    public void AnyNarrowingCountsAsCriteria()
    {
        Assert.True(new ProductListFilter(Search: "x").HasCriteria);
        Assert.True(new ProductListFilter(Status: null).HasCriteria);
        Assert.True(new ProductListFilter(OutOfStock: true).HasCriteria);
        Assert.False(new ProductListFilter(SortKey: "name", Page: 3).HasCriteria); // sort/page are not criteria
    }

    [Fact]
    public void PresentAddsTurkishDisplayColumns()
    {
        var table = new DataTable();
        foreach (var c in new[] { "UrunTipi", "Aktif", "SatisaAcik", "SonGuncelleme" }) table.Columns.Add(c, c is "Aktif" or "SatisaAcik" ? typeof(long) : typeof(string));
        table.Rows.Add("RawMaterial", 1L, 0L, "2026-09-23T10:15:00.0000000Z");
        table.Rows.Add("Stock", 0L, 1L, DBNull.Value);
        ProductListView.Present(table);
        Assert.Equal(("Hammadde", "Aktif", "Kapalı"), (table.Rows[0]["UrunTipiText"], table.Rows[0]["DurumText"], table.Rows[0]["SatisText"]));
        Assert.Equal(("Stok", "Pasif", "Açık"), (table.Rows[1]["UrunTipiText"], table.Rows[1]["DurumText"], table.Rows[1]["SatisText"]));
        Assert.Matches(@"^23\.09\.2026 \d\d:15$", table.Rows[0]["SonGuncelleme"].ToString());
    }
}
