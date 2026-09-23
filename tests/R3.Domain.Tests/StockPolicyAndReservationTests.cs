using R3.Infrastructure;

namespace R3.Domain.Tests;

/// <summary>Negatif Stok Politikası (warehouses.allow_negative_stock, previously never enforced) and the
/// Stok Rezervasyonları engine (first writer of inventory_balances.quantity_reserved).</summary>
public sealed class StockPolicyAndReservationTests : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), "R3-tests-" + Guid.NewGuid());
    private const string Company = "00000000-0000-0000-0000-000000000001";

    private (StoreDatabase Db, LocalInventoryService Inventory, string Warehouse, string Branch, string Product) Create(decimal opening = 10)
    {
        var db = new StoreDatabase(Path.Combine(_folder, "test.db"));
        var warehouse = db.Query("SELECT id FROM warehouses WHERE company_id=$c LIMIT 1", ("$c", Company)).Rows[0][0].ToString()!;
        var branch = db.Query("SELECT branch_id FROM warehouses WHERE id=$w", ("$w", warehouse)).Rows[0][0].ToString()!;
        var unit = db.Query("SELECT id FROM units WHERE company_id=$c LIMIT 1", ("$c", Company)).Rows[0][0].ToString()!;
        new LocalProductService(db).Save(new ProductAggregateEdit("", Company, "P1", "Ürün", "", "", unit, "Stock", 20, true, [], []));
        var product = db.Query("SELECT id FROM products WHERE code='P1'").Rows[0][0].ToString()!;
        var inventory = new LocalInventoryService(db);
        if (opening > 0) inventory.PostOpeningBalanceAsync(Post(branch, warehouse, product, opening)).GetAwaiter().GetResult();
        return (db, inventory, warehouse, branch, product);
    }

    private static InventoryPost Post(string branch, string warehouse, string product, decimal quantity) => new(Company, branch, warehouse, product, null, quantity, 1, DateTime.Today);

    [Fact]
    public async Task ShortageIsRejectedUntilTheWarehouseAllowsNegativeStock()
    {
        var (db, inventory, warehouse, branch, product) = Create(opening: 3);
        await Assert.ThrowsAsync<InvalidOperationException>(() => inventory.PostManualOutAsync(Post(branch, warehouse, product, 5)));

        new LocalWarehouseService(db).SetNegativeStockPolicy(Company, warehouse, true, "test");
        await inventory.PostManualOutAsync(Post(branch, warehouse, product, 5));
        Assert.Equal(-2m, (await inventory.GetBalanceAsync(warehouse, product, null))!.QuantityOnHand);

        new LocalWarehouseService(db).SetNegativeStockPolicy(Company, warehouse, false, "test");
        await Assert.ThrowsAsync<InvalidOperationException>(() => inventory.PostManualOutAsync(Post(branch, warehouse, product, 1)));
    }

    [Fact]
    public async Task ReservationMovesStockFromAvailableToReservedAndBack()
    {
        var (db, inventory, warehouse, branch, product) = Create(opening: 10);
        var reservations = new LocalReservationService(db);
        var id = reservations.Create(new ReservationEdit(Company, branch, warehouse, product, null, 4, ReferenceNo: "SIP-1"), "test");

        var balance = (await inventory.GetBalanceAsync(warehouse, product, null))!;
        Assert.Equal((10m, 4m, 6m), (balance.QuantityOnHand, balance.QuantityReserved, balance.QuantityAvailable));

        // Reserved stock is protected from other stock-outs...
        await Assert.ThrowsAsync<InvalidOperationException>(() => inventory.PostManualOutAsync(Post(branch, warehouse, product, 7)));
        // ...and cannot be promised twice.
        Assert.Throws<InvalidOperationException>(() => reservations.Create(new ReservationEdit(Company, branch, warehouse, product, null, 7), "test"));

        reservations.Close(id, "Released", "test");
        balance = (await inventory.GetBalanceAsync(warehouse, product, null))!;
        Assert.Equal((0m, 10m), (balance.QuantityReserved, balance.QuantityAvailable));
        Assert.Throws<InvalidOperationException>(() => reservations.Close(id, "Consumed", "test"));
        Assert.Equal("Released", reservations.Search(Company, status: null).Rows[0]["Durum"]);
    }

    [Fact]
    public async Task RebuildingBalancesKeepsOpenReservations()
    {
        var (db, inventory, warehouse, branch, product) = Create(opening: 10);
        new LocalReservationService(db).Create(new ReservationEdit(Company, branch, warehouse, product, null, 3), "test");
        await inventory.RebuildInventoryBalancesAsync();
        var balance = (await inventory.GetBalanceAsync(warehouse, product, null))!;
        Assert.Equal((10m, 3m, 7m), (balance.QuantityOnHand, balance.QuantityReserved, balance.QuantityAvailable));
    }

    [Fact]
    public async Task DueReservationsExpireAndReleaseTheirStock()
    {
        var (db, inventory, warehouse, branch, product) = Create(opening: 10);
        var reservations = new LocalReservationService(db);
        reservations.Create(new ReservationEdit(Company, branch, warehouse, product, null, 2, ExpiresAt: DateTime.Now.AddDays(1)), "test");
        reservations.Create(new ReservationEdit(Company, branch, warehouse, product, null, 3, ExpiresAt: DateTime.Now.AddDays(5)), "test");

        Assert.Equal(1, reservations.ExpireDue(Company, "test", DateTime.Now.AddDays(2)));
        Assert.Equal(3m, (await inventory.GetBalanceAsync(warehouse, product, null))!.QuantityReserved);
        Assert.Equal(0, reservations.ExpireDue(Company, "test", DateTime.Now.AddDays(2)));
    }

    [Fact]
    public void ReservationNeedsStockEvenWhenNegativeStockIsAllowed()
    {
        var (db, _, warehouse, branch, product) = Create(opening: 0);
        new LocalWarehouseService(db).SetNegativeStockPolicy(Company, warehouse, true, "test");
        Assert.Throws<InvalidOperationException>(() => new LocalReservationService(db).Create(new ReservationEdit(Company, branch, warehouse, product, null, 1), "test"));
        Assert.Throws<ArgumentException>(() => new LocalReservationService(db).Create(new ReservationEdit(Company, branch, warehouse, product, null, 0), "test"));
    }

    public void Dispose()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        try { Directory.Delete(_folder, true); } catch (IOException) { }
    }
}
