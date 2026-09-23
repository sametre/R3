using R3.Infrastructure;

namespace R3.Domain.Tests;

/// <summary>Fiyat Yönetimi (LocalPriceService): price lists, per-product prices, history and bulk rules.</summary>
public sealed class PriceServiceTests : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), "R3-tests-" + Guid.NewGuid());
    private const string Company = "00000000-0000-0000-0000-000000000001";

    private (StoreDatabase Db, LocalPriceService Prices, string List, string[] Products) Create()
    {
        var db = new StoreDatabase(Path.Combine(_folder, "test.db"));
        var unit = db.Query("SELECT id FROM units WHERE company_id=$c LIMIT 1", ("$c", Company)).Rows[0][0].ToString()!;
        var products = new LocalProductService(db);
        foreach (var code in new[] { "A", "B", "C" }) products.Save(new ProductAggregateEdit("", Company, code, "Ürün " + code, "", "", unit, "Stock", 20, true, [], []));
        var prices = new LocalPriceService(db);
        var list = prices.SavePriceList(new PriceListEdit("", Company, "psn", "Peşin"), "test");
        return (db, prices, list, new[] { "A", "B", "C" }.Select(code => db.Query("SELECT id FROM products WHERE code=$c", ("$c", (object)code)).Rows[0][0].ToString()!).ToArray());
    }

    [Fact]
    public void SetPriceRecordsHistoryAndIgnoresNoOpChanges()
    {
        var (db, prices, list, p) = Create();
        prices.SetPrice(Company, list, p[0], 100, "u1");
        prices.SetPrice(Company, list, p[0], 110, "u2");
        prices.SetPrice(Company, list, p[0], 110, "u2");   // unchanged - no extra history row
        Assert.Equal(110m, prices.GetPrice(list, p[0]));
        var history = prices.History(Company, p[0]).Rows;
        Assert.Equal(2, history.Count);
        Assert.Equal(10m, Convert.ToDecimal(history[0]["DegisimYuzde"]));
        Assert.Equal("PSN", db.Query("SELECT code FROM price_lists WHERE id=$id", ("$id", list)).Rows[0][0]);

        prices.SetPrice(Company, list, p[0], null, "u3");    // remove price
        Assert.Null(prices.GetPrice(list, p[0]));
        Assert.Equal(3, prices.History(Company, p[0]).Rows.Count);
        Assert.Throws<ArgumentException>(() => prices.SetPrice(Company, list, p[0], -1, "u"));
    }

    [Fact]
    public void BulkPercentIncreaseOnlyTouchesPricedProductsAndRounds()
    {
        var (_, prices, list, p) = Create();
        prices.SetPrice(Company, list, p[0], 100, "u"); prices.SetPrice(Company, list, p[1], 33.33m, "u");
        var changed = prices.BulkUpdate(Company, list, new BulkPriceFilter(), BulkPriceMode.IncreasePercent, 10, "u", roundTo: 1, endWith: 0.99m);
        Assert.Equal(2, changed);
        Assert.Equal(110.99m, prices.GetPrice(list, p[0]));
        Assert.Equal(37.99m, prices.GetPrice(list, p[1]));   // 36.663 → 37 → 37.99
        Assert.Null(prices.GetPrice(list, p[2]));             // no base price, left alone
    }

    [Fact]
    public void BulkCopyFromAnotherListAppliesFactorAndFilter()
    {
        var (_, prices, list, p) = Create();
        var purchase = prices.SavePriceList(new PriceListEdit("", Company, "ALIS", "Alış", "Purchase", false), "test");
        prices.SetPrice(Company, purchase, p[0], 50, "u"); prices.SetPrice(Company, purchase, p[1], 80, "u");
        Assert.Equal(1, prices.BulkUpdate(Company, list, new BulkPriceFilter([p[0]]), BulkPriceMode.CopyFromList, 1.5m, "u", sourceListId: purchase));
        Assert.Equal(75m, prices.GetPrice(list, p[0]));
        Assert.Null(prices.GetPrice(list, p[1]));   // outside the filter
        Assert.Throws<ArgumentException>(() => prices.BulkUpdate(Company, list, new BulkPriceFilter(), BulkPriceMode.CopyFromList, 1, "u"));
    }

    [Fact]
    public void GetPriceRespectsListValidityAndActiveFlag()
    {
        var (_, prices, _, p) = Create();
        var campaign = prices.SavePriceList(new PriceListEdit("", Company, "KMP", "Kampanya", ValidFrom: new DateTime(2026, 1, 1), ValidTo: new DateTime(2026, 1, 31)), "test");
        prices.SetPrice(Company, campaign, p[0], 9.9m, "u");
        Assert.Equal(9.9m, prices.GetPrice(campaign, p[0], new DateTime(2026, 1, 15)));
        Assert.Null(prices.GetPrice(campaign, p[0], new DateTime(2026, 2, 1)));
        prices.SavePriceList(prices.GetPriceList(campaign)! with { IsActive = false }, "test");
        Assert.Null(prices.GetPrice(campaign, p[0], new DateTime(2026, 1, 15)));
        Assert.Throws<ArgumentException>(() => prices.SavePriceList(new PriceListEdit("", Company, "X", "X", ValidFrom: new DateTime(2026, 2, 1), ValidTo: new DateTime(2026, 1, 1)), "t"));
        Assert.Throws<ArgumentException>(() => prices.SavePriceList(new PriceListEdit("", Company, "psn", "Kopya"), "t"));
    }

    [Theory]
    [InlineData(12.344, 0.01, null, 12.34)]
    [InlineData(12.345, 0.01, null, 12.35)]
    [InlineData(12.3, 0.5, null, 12.5)]
    [InlineData(12.3, 1, 0.90, 12.90)]
    public void RoundingRules(double value, double step, double? ending, double expected) =>
        Assert.Equal((decimal)expected, LocalPriceService.Round((decimal)value, (decimal)step, ending is { } e ? (decimal)e : null));

    public void Dispose()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        try { Directory.Delete(_folder, true); } catch (IOException) { }
    }
}
