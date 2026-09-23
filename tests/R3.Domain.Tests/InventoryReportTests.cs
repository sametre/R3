using R3.Infrastructure;

namespace R3.Domain.Tests;

/// <summary>Ürün Ekstresi and Stok Değer Raporu (LocalInventoryReportService).</summary>
public sealed class InventoryReportTests : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), "R3-tests-" + Guid.NewGuid());
    private const string Company = "00000000-0000-0000-0000-000000000001";

    private (StoreDatabase Db, LocalInventoryService Inventory, string Warehouse, string Branch, string Product) Create()
    {
        var db = new StoreDatabase(Path.Combine(_folder, "test.db"));
        var warehouse = db.Query("SELECT id FROM warehouses WHERE company_id=$c LIMIT 1", ("$c", Company)).Rows[0][0].ToString()!;
        var branch = db.Query("SELECT branch_id FROM warehouses WHERE id=$w", ("$w", warehouse)).Rows[0][0].ToString()!;
        var unit = db.Query("SELECT id FROM units WHERE company_id=$c LIMIT 1", ("$c", Company)).Rows[0][0].ToString()!;
        new LocalProductService(db).Save(new ProductAggregateEdit("", Company, "P1", "Ürün", "", "", unit, "Stock", 20, true, [], []));
        return (db, new LocalInventoryService(db), warehouse, branch, db.Query("SELECT id FROM products WHERE code='P1'").Rows[0][0].ToString()!);
    }

    private static InventoryPost Post(string branch, string warehouse, string product, decimal quantity, decimal? cost, DateTime at) => new(Company, branch, warehouse, product, null, quantity, cost, at);

    [Fact]
    public async Task ProductLedgerCarriesOpeningBalanceAndRunningBalance()
    {
        var (db, inventory, w, b, p) = Create();
        await inventory.PostOpeningBalanceAsync(Post(b, w, p, 10, 5, new DateTime(2026, 1, 5, 10, 0, 0)));
        await inventory.PostManualOutAsync(Post(b, w, p, 3, null, new DateTime(2026, 2, 1, 10, 0, 0)));
        await inventory.PostManualInAsync(Post(b, w, p, 4, 6, new DateTime(2026, 3, 1, 10, 0, 0)));
        await inventory.PostManualOutAsync(Post(b, w, p, 1, null, new DateTime(2026, 4, 1, 10, 0, 0)));

        var report = new LocalInventoryReportService(db).ProductLedger(Company, p, from: new DateTime(2026, 2, 1), to: new DateTime(2026, 3, 31));
        Assert.Equal(10m, report.OpeningBalance);   // January movement is before the range
        Assert.Equal(2, report.Lines.Rows.Count);   // April movement is after it
        Assert.Equal(7m, report.Lines.Rows[0]["Bakiye"]);
        Assert.Equal(11m, report.Lines.Rows[1]["Bakiye"]);
        Assert.Equal((4m, 3m, 11m), (report.TotalIn, report.TotalOut, report.ClosingBalance));
    }

    [Fact]
    public async Task StockValuationUsesWeightedAverageOfCostedPurchasesOnly()
    {
        var (db, inventory, w, b, p) = Create();
        await inventory.PostOpeningBalanceAsync(Post(b, w, p, 10, 5, new DateTime(2026, 1, 1)));
        await inventory.PostManualInAsync(Post(b, w, p, 10, 7, new DateTime(2026, 2, 1)));
        await inventory.PostManualInAsync(Post(b, w, p, 5, null, new DateTime(2026, 2, 2)));   // uncosted - quantity counts, average does not move
        await inventory.PostManualOutAsync(Post(b, w, p, 5, null, new DateTime(2026, 3, 1)));

        var reports = new LocalInventoryReportService(db);
        var row = reports.StockValuation(Company).Rows[0];
        Assert.Equal(20m, row["Miktar"]);
        Assert.Equal(6m, row["OrtalamaMaliyet"]);
        Assert.Equal(120m, row["Tutar"]);

        var january = reports.StockValuation(Company, asOf: new DateTime(2026, 1, 31)).Rows[0];
        Assert.Equal((10m, 5m, 50m), ((decimal)january["Miktar"], (decimal)january["OrtalamaMaliyet"], (decimal)january["Tutar"]));
    }

    [Fact]
    public async Task ProductWithoutAnyCostIsReportedNotHidden()
    {
        var (db, inventory, w, b, p) = Create();
        await inventory.PostOpeningBalanceAsync(Post(b, w, p, 4, null, new DateTime(2026, 1, 1)));
        var row = new LocalInventoryReportService(db).StockValuation(Company).Rows[0];
        Assert.Equal("Maliyet yok", row["MaliyetKaynagi"]);
        Assert.Equal(0m, row["Tutar"]);
        Assert.Equal(4m, row["Miktar"]);
    }

    public void Dispose()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        try { Directory.Delete(_folder, true); } catch (IOException) { }
    }
}
