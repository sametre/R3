using R3.Infrastructure;

namespace R3.Domain.Tests;

public sealed class ProductBulkServiceTests : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), "R3-tests-" + Guid.NewGuid());
    private const string Company = "00000000-0000-0000-0000-000000000001";

    private (StoreDatabase Db, string[] Ids) Create()
    {
        var db = new StoreDatabase(Path.Combine(_folder, "test.db"));
        var unit = db.Query("SELECT id FROM units WHERE company_id=$c LIMIT 1", ("$c", Company)).Rows[0][0].ToString()!;
        var products = new LocalProductService(db);
        products.Save(new ProductAggregateEdit("", Company, "A", "A", "", "", unit, "Stock", 20, true, [], [], MinimumStock: 5, MaximumStock: 50));
        products.Save(new ProductAggregateEdit("", Company, "B", "B", "", "", unit, "Stock", 20, true, [], [], MinimumStock: 1, MaximumStock: 10));
        return (db, new[] { "A", "B" }.Select(x => db.Query("SELECT id FROM products WHERE code=$c", ("$c", (object)x)).Rows[0][0].ToString()!).ToArray());
    }

    private static object Field(StoreDatabase db, string id, string column) => db.Query($"SELECT {column} FROM products WHERE id=$id", ("$id", id)).Rows[0][0];

    [Fact]
    public void AppliesOnlyTheChosenFieldsAndAudits()
    {
        var (db, ids) = Create();
        new LocalMasterDataService(db).Save("product_groups", null, new MasterRecord("17", "Kozmetik"), Company);
        var group = db.Query("SELECT id FROM product_groups").Rows[0][0].ToString()!;
        Assert.Equal(2, new LocalProductBulkService(db).Apply(Company, ids, new ProductBulkChange(ProductGroupId: group, VatRate: 10, IsSellable: false), "u"));
        foreach (var id in ids)
        {
            Assert.Equal(group, Field(db, id, "product_group_id"));
            Assert.Equal(10d, Convert.ToDouble(Field(db, id, "vat_rate")));
            Assert.Equal(0L, Convert.ToInt64(Field(db, id, "is_sellable")));
            Assert.Equal(1L, Convert.ToInt64(Field(db, id, "is_active")));   // untouched
        }
        Assert.Equal(2L, Convert.ToInt64(db.Query("SELECT COUNT(*) FROM audit_logs WHERE action='ProductBulkUpdated'").Rows[0][0]));

        new LocalProductBulkService(db).Apply(Company, [ids[0]], new ProductBulkChange(ProductGroupId: ""), "u");   // "" clears
        Assert.Equal(DBNull.Value, Field(db, ids[0], "product_group_id"));
    }

    [Fact]
    public void MinMaxIsCheckedAgainstEachProductsResultingValuesAndRollsBackEverything()
    {
        var (db, ids) = Create();
        // A: min 5 → max 8 is fine; B: min 1 → fine too. Now set max 3: A would end up max 3 < min 5.
        var ex = Assert.Throws<ArgumentException>(() => new LocalProductBulkService(db).Apply(Company, ids, new ProductBulkChange(MaximumStock: 3, VatRate: 1), "u"));
        Assert.Contains("A:", ex.Message);
        Assert.Equal(20d, Convert.ToDouble(Field(db, ids[1], "vat_rate")));   // B was not partially updated
        Assert.Equal(10d, Convert.ToDouble(Field(db, ids[1], "maximum_stock")));
    }

    [Fact]
    public void RejectsEmptyChangesUnknownMastersAndOtherCompaniesProducts()
    {
        var (db, ids) = Create();
        var bulk = new LocalProductBulkService(db);
        Assert.Throws<ArgumentException>(() => bulk.Apply(Company, ids, new ProductBulkChange(), "u"));
        Assert.Throws<ArgumentException>(() => bulk.Apply(Company, [], new ProductBulkChange(VatRate: 1), "u"));
        Assert.Throws<ArgumentException>(() => bulk.Apply(Company, ids, new ProductBulkChange(BrandId: "nope"), "u"));
        Assert.Throws<ArgumentException>(() => bulk.Apply(Company, ["not-a-product"], new ProductBulkChange(VatRate: 1), "u"));
        Assert.Throws<ArgumentException>(() => bulk.Apply(Company, ids, new ProductBulkChange(VatRate: 150), "u"));
    }

    public void Dispose()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        try { Directory.Delete(_folder, true); } catch (IOException) { }
    }
}
