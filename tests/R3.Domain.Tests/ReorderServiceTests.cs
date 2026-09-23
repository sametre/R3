using R3.Infrastructure;

namespace R3.Domain.Tests;

public sealed class ReorderServiceTests : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), "R3-tests-" + Guid.NewGuid());
    private const string Company = "00000000-0000-0000-0000-000000000001";

    [Theory]
    [InlineData(50, 8, 0, 0, 0, 42)]     // up to max
    [InlineData(50, 8, 30, 0, 0, 12)]    // on-order counts
    [InlineData(50, 8, 45, 0, 0, 0)]     // already covered by open orders
    [InlineData(20, 15, 0, 10, 0, 10)]   // raised to minimum order quantity
    [InlineData(20, 7, 0, 0, 6, 18)]     // 13 rounded up to a multiple of 6
    public void SuggestionFormula(double target, double available, double onOrder, double minimumOrder, double multiple, double expected) =>
        Assert.Equal((decimal)expected, LocalReorderService.Suggest((decimal)target, (decimal)available, (decimal)onOrder, (decimal)minimumOrder, (decimal)multiple));

    [Fact]
    public async Task SuggestsBelowMinimumProductsAndCreatesOneDraftOrderPerSupplier()
    {
        var db = new StoreDatabase(Path.Combine(_folder, "test.db"));
        var branch = db.Query("SELECT id FROM branches LIMIT 1").Rows[0][0].ToString()!;
        var warehouse = db.Query("SELECT id FROM warehouses LIMIT 1").Rows[0][0].ToString()!;
        var unit = db.Query("SELECT id FROM units LIMIT 1").Rows[0][0].ToString()!;
        var supplier = Guid.NewGuid().ToString();
        new LocalAccountService(db).Save(new AccountAggregateEdit(new AccountEdit(supplier, Company, "T1", "Tedarikçi", "Supplier"), new AccountTaxProfileEdit(), new AccountEInvoiceProfileEdit(), null, new SupplierProfileEdit()));
        var products = new LocalProductService(db);
        products.Save(new ProductAggregateEdit("", Company, "A", "Az kalan", "", "", unit, "Stock", 20, true, [], [], MinimumStock: 10, MaximumStock: 40, OrderMultiple: 5, Suppliers: [new("", supplier, "SUP-A", true, 3, 1, 1)]));
        products.Save(new ProductAggregateEdit("", Company, "B", "Yeterli", "", "", unit, "Stock", 20, true, [], [], MinimumStock: 2, MaximumStock: 10));
        products.Save(new ProductAggregateEdit("", Company, "C", "Tedarikçisiz", "", "", unit, "Stock", 20, true, [], [], MinimumStock: 5));
        var inventory = new LocalInventoryService(db);
        foreach (var (code, qty, cost) in new[] { ("A", 3m, 12m), ("B", 5m, 1m), ("C", 1m, 1m) })
            await inventory.PostOpeningBalanceAsync(new(Company, branch, warehouse, db.Query("SELECT id FROM products WHERE code=$c", ("$c", (object)code)).Rows[0][0].ToString()!, null, qty, cost, DateTime.UtcNow));

        var service = new LocalReorderService(db);
        var suggestions = service.Suggestions(Company).ToDictionary(x => x.ProductCode);
        Assert.False(suggestions.ContainsKey("B"));
        Assert.Equal(40m, suggestions["A"].Suggested);   // 40 − 3 = 37 → multiple of 5
        Assert.Equal("Tedarikçi", suggestions["A"].SupplierName);
        Assert.Null(suggestions["C"].SupplierId);

        Assert.Throws<ArgumentException>(() => service.CreatePurchaseOrders(Company, [new(suggestions["C"].ProductId, warehouse, "", 4, 1)], "test"));
        var ids = service.CreatePurchaseOrders(Company, [new(suggestions["A"].ProductId, warehouse, supplier, suggestions["A"].Suggested, 12)], "test");
        Assert.Single(ids);
        Assert.Equal("Order", db.Query("SELECT document_type FROM purchase_documents WHERE id=$id", ("$id", ids[0])).Rows[0][0]);

        // The new draft order now counts as "yolda": A is no longer suggested.
        Assert.DoesNotContain(service.Suggestions(Company), x => x.ProductCode == "A");
    }

    public void Dispose()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        try { Directory.Delete(_folder, true); } catch (IOException) { }
    }
}
