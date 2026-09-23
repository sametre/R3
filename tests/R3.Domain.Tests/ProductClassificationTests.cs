using R3.Infrastructure;

namespace R3.Domain.Tests;

/// <summary>Ürün sınıflandırma (stok grubu, menşe ülke) and depo bazlı min/max, added 2026-09-23 after
/// the ASB KODSTOKGRUP / KODULKE / STOKSUBEMINMAX sources were sample-verified.</summary>
public sealed class ProductClassificationTests : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), "R3-tests-" + Guid.NewGuid());
    private const string Company = "00000000-0000-0000-0000-000000000001";
    private StoreDatabase Create() => new(Path.Combine(_folder, "test.db"));
    private static string Scalar(StoreDatabase db, string sql, params (string, object)[] p) => db.Query(sql, p).Rows[0][0].ToString()!;
    private static string Unit(StoreDatabase db) => Scalar(db, "SELECT id FROM units WHERE company_id=$c LIMIT 1", ("$c", Company));
    private static string Warehouse(StoreDatabase db) => Scalar(db, "SELECT id FROM warehouses WHERE company_id=$c LIMIT 1", ("$c", Company));
    private static string Group(StoreDatabase db, string code = "17")
    {
        if (db.Query("SELECT id FROM product_groups WHERE code=$code", ("$code", code)).Rows.Count == 0)
            new LocalMasterDataService(db).Save("product_groups", null, new MasterRecord(code, "Kozmetik Ürünleri", Extra: "3304"), Company);
        return Scalar(db, "SELECT id FROM product_groups WHERE code=$code", ("$code", code));
    }
    private static string Turkey(StoreDatabase db) => Scalar(db, "SELECT id FROM countries WHERE code='TR'");
    private static ProductAggregateEdit Product(StoreDatabase db, string code = "KZM01") => new("", Company, code, "Krem", "", "", Unit(db), "Stock", 20, true, [], [], MinimumStock: 5, MaximumStock: 50);

    [Fact]
    public void ClassificationAndWarehousePoliciesRoundTrip()
    {
        var db = Create(); var products = new LocalProductService(db); var warehouse = Warehouse(db);
        products.Save(Product(db) with { ProductGroupId = Group(db), OriginCountryId = Turkey(db), WarehousePolicies = [new(warehouse, 2, 20)] });
        var id = Scalar(db, "SELECT id FROM products WHERE code='KZM01'");

        var detail = products.GetDetail(id, Company)!.Product;
        Assert.Equal(Group(db), detail.ProductGroupId);
        Assert.Equal(Turkey(db), detail.OriginCountryId);
        var policy = Assert.Single(detail.WarehousePolicies!);
        Assert.Equal((warehouse, 2m, 20m), (policy.WarehouseId, policy.MinimumStock, policy.MaximumStock));
        Assert.Equal("3304", Scalar(db, "SELECT customs_code FROM product_groups WHERE code='17'"));

        var row = products.SearchPage(new ProductListQuery(Company, ProductGroupId: Group(db))).Rows.Rows[0];
        Assert.Equal("Kozmetik Ürünleri", row["StokGrubu"]);
        Assert.Equal("Türkiye", row["Mense"]);
    }

    [Fact]
    public void CallersThatDoNotSendTheNewFieldsLeaveThemUntouched()
    {
        var db = Create(); var products = new LocalProductService(db); var warehouse = Warehouse(db);
        products.Save(Product(db) with { ProductGroupId = Group(db), OriginCountryId = Turkey(db), WarehousePolicies = [new(warehouse, 2, 20)] });
        var id = Scalar(db, "SELECT id FROM products WHERE code='KZM01'");

        products.Save(Product(db) with { Id = id, Name = "Krem 2" }); // e.g. an import or bulk tool built before these fields existed
        var detail = products.GetDetail(id, Company)!.Product;
        Assert.Equal("Krem 2", detail.Name);
        Assert.Equal(Group(db), detail.ProductGroupId);
        Assert.Single(detail.WarehousePolicies!);

        products.Save(detail with { ProductGroupId = "", OriginCountryId = "", WarehousePolicies = [] }); // explicit clear
        detail = products.GetDetail(id, Company)!.Product;
        Assert.Equal("", detail.ProductGroupId);
        Assert.Empty(detail.WarehousePolicies!);
    }

    [Fact]
    public void InvalidClassificationAndPoliciesAreRejected()
    {
        var db = Create(); var products = new LocalProductService(db); var warehouse = Warehouse(db);
        Assert.Throws<ArgumentException>(() => products.Save(Product(db) with { ProductGroupId = "no-such-group" }));
        Assert.Throws<ArgumentException>(() => products.Save(Product(db) with { OriginCountryId = "no-such-country" }));
        Assert.Throws<ArgumentException>(() => products.Save(Product(db) with { WarehousePolicies = [new(warehouse, 10, 5)] }));
        Assert.Throws<ArgumentException>(() => products.Save(Product(db) with { WarehousePolicies = [new(warehouse, 1, 5), new(warehouse, 2, 6)] }));
        Assert.Throws<ArgumentException>(() => products.Save(Product(db) with { WarehousePolicies = [new("no-such-warehouse", 1, 5)] }));
        Assert.Equal("0", Scalar(db, "SELECT COUNT(*) FROM products WHERE code='KZM01'")); // every failure rolled back
    }

    [Fact]
    public void CopyKeepsClassificationAndWarehousePolicies()
    {
        var db = Create(); var products = new LocalProductService(db); var warehouse = Warehouse(db);
        products.Save(Product(db) with { ProductGroupId = Group(db), OriginCountryId = Turkey(db), WarehousePolicies = [new(warehouse, 2, 20)] });
        var source = products.GetDetail(Scalar(db, "SELECT id FROM products WHERE code='KZM01'"), Company)!.Product;
        var copy = products.GetDetail(products.Copy(source, "KZM02", "Krem kopya"), Company)!.Product;
        Assert.Equal(source.ProductGroupId, copy.ProductGroupId);
        Assert.Equal(source.OriginCountryId, copy.OriginCountryId);
        Assert.Single(copy.WarehousePolicies!);
    }

    [Fact]
    public async Task StockStatusUsesWarehouseOverrideBeforeProductMinimum()
    {
        var db = Create(); var products = new LocalProductService(db); var inventory = new LocalInventoryService(db);
        var warehouse = Warehouse(db); var branch = Scalar(db, "SELECT branch_id FROM warehouses WHERE id=$w", ("$w", warehouse));
        products.Save(Product(db, "A") with { MinimumStock = 5, MaximumStock = 0 });
        products.Save(Product(db, "B") with { MinimumStock = 5, MaximumStock = 0, WarehousePolicies = [new(warehouse, 2, 3)] });
        foreach (var code in new[] { "A", "B" })
            await inventory.PostOpeningBalanceAsync(new InventoryPost(Company, branch, warehouse, Scalar(db, "SELECT id FROM products WHERE code=$c", ("$c", code)), null, 4, 1, DateTime.Today));

        var rows = (await inventory.SearchBalancesAsync(warehouse)).Rows.Cast<System.Data.DataRow>().ToDictionary(r => r["Urun"].ToString()!.Split(' ')[0]);
        Assert.Equal("Min Altı", rows["A"]["StokUyari"]);   // product minimum 5 > 4 available
        Assert.Equal("Ürün", rows["A"]["PolitikaKaynagi"]);
        Assert.Equal("Max Üstü", rows["B"]["StokUyari"]);   // warehouse override: min 2 (ok), max 3 < 4 on hand
        Assert.Equal("Depo", rows["B"]["PolitikaKaynagi"]);
        Assert.Equal(2m, Convert.ToDecimal(rows["B"]["MinStok"]));
    }

    [Fact]
    public void EmptyCustomsCodeIsStoredEmpty()
    {
        var db = Create();
        new LocalMasterDataService(db).Save("product_groups", null, new MasterRecord("99", "Market"), Company);
        Assert.Equal("", Scalar(db, "SELECT customs_code FROM product_groups WHERE code='99'"));
        Assert.Equal("Market", new LocalMasterDataService(db).List("product_groups").Rows[0]["Ad"]);
    }

    public void Dispose()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        try { Directory.Delete(_folder, true); } catch (IOException) { }
    }
}
