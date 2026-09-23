using R3.Infrastructure;

namespace R3.Domain.Tests;

/// <summary>§15 cutover: ProductUnit.ConversionFactor is the single authority for non-base-unit barcodes.</summary>
public sealed class BarcodeUnitCutoverTests : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), "R3-tests-" + Guid.NewGuid());
    private const string Company = "00000000-0000-0000-0000-000000000001";

    private (StoreDatabase Db, LocalProductService Products, string Adet, string Koli) Create()
    {
        var db = new StoreDatabase(Path.Combine(_folder, "test.db"));
        var adet = db.Query("SELECT id FROM units WHERE company_id=$c LIMIT 1", ("$c", Company)).Rows[0][0].ToString()!;
        new LocalMasterDataService(db).Save("units", null, new MasterRecord("KOLI", "Koli", Extra: "0"), Company);
        return (db, new LocalProductService(db), adet, db.Query("SELECT id FROM units WHERE code='KOLI'").Rows[0][0].ToString()!);
    }

    private static string Id(StoreDatabase db, string code) => db.Query("SELECT id FROM products WHERE code=$c", ("$c", code)).Rows[0][0].ToString()!;

    [Fact]
    public void KoliBarcodeWithoutAKoliUnitCreatesTheUnitFromItsFactor()
    {
        var (db, products, adet, koli) = Create();
        products.Save(new ProductAggregateEdit("", Company, "P1", "Ürün", "", "", adet, "Stock", 20, true, [], [new("", "", "", Barcode: "8690000000011", UnitId: koli, Quantity: 12)]));
        var detail = products.GetDetail(Id(db, "P1"), Company)!.Product;
        Assert.Equal(12m, detail.Units.Single(u => u.UnitId == koli).ConversionFactor);
        Assert.Equal(12m, new LocalBarcodeResolver(db).ResolveForInventory("8690000000011", Company).QuantityFactor);
    }

    [Fact]
    public void ExistingKoliUnitOverridesTheBarcodesOwnFactor()
    {
        var (db, products, adet, koli) = Create();
        products.Save(new ProductAggregateEdit("", Company, "P1", "Ürün", "", "", adet, "Stock", 20, true, [], [new("", "", "", Barcode: "8690000000011", UnitId: koli, Quantity: 6)],
            Units: [new("", adet, 1, 1, true), new("", koli, 2, 24, false)]));
        Assert.Equal(24m, new LocalBarcodeResolver(db).ResolveForInventory("8690000000011", Company).QuantityFactor);
        Assert.Equal(24d, Convert.ToDouble(db.Query("SELECT quantity FROM product_barcodes WHERE barcode='8690000000011'").Rows[0][0]));
    }

    [Fact]
    public void BaseUnitPackBarcodesKeepTheirLegacyQuantity()
    {
        var (db, products, adet, _) = Create();
        products.Save(new ProductAggregateEdit("", Company, "P1", "Ürün", "", "", adet, "Stock", 20, true, [], [new("", "", "", Barcode: "111", UnitId: adet, Quantity: 12)]));
        Assert.Equal(12m, new LocalBarcodeResolver(db).ResolveForInventory("111", Company).QuantityFactor);
    }

    [Fact]
    public void ReSavingWithoutAUnitListKeepsTheStoredKoliUnit()
    {
        var (db, products, adet, koli) = Create();
        products.Save(new ProductAggregateEdit("", Company, "P1", "Ürün", "", "", adet, "Stock", 20, true, [], [],
            Units: [new("", adet, 1, 1, true), new("", koli, 2, 24, false)]));
        var id = Id(db, "P1");
        products.Save(new ProductAggregateEdit(id, Company, "P1", "Yeni ad", "", "", adet, "Stock", 20, true, [], []));   // e.g. an import
        Assert.Equal(1L, Convert.ToInt64(db.Query("SELECT is_active FROM product_units WHERE product_id=$p AND unit_id=$u", ("$p", id), ("$u", koli)).Rows[0][0]));
    }

    [Fact]
    public void KoliBarcodeWithoutAnyUnitRowFallsBackToItsOwnQuantity()
    {
        var (db, products, adet, koli) = Create();
        products.Save(new ProductAggregateEdit("", Company, "P1", "Ürün", "", "", adet, "Stock", 20, true, [], []));
        db.Execute("INSERT INTO product_barcodes(id,product_id,unit_id,barcode,quantity,is_primary,is_active) VALUES('b1',$p,$u,'998',6,0,1)", ("$p", Id(db, "P1")), ("$u", koli));
        Assert.Equal(6m, new LocalBarcodeResolver(db).ResolveForInventory("998", Company).QuantityFactor);   // e.g. imported after the upgrade
    }

    [Fact]
    public void StartupAlignmentCreatesMissingUnitsForLegacyBarcodes()
    {
        var (db, products, adet, koli) = Create();
        products.Save(new ProductAggregateEdit("", Company, "P1", "Ürün", "", "", adet, "Stock", 20, true, [], []));
        var id = Id(db, "P1");
        // Legacy row written straight to the table (as an importer would): koli barcode, no koli ProductUnit.
        db.Execute("INSERT INTO product_barcodes(id,product_id,unit_id,barcode,quantity,is_primary,is_active) VALUES('b1',$p,$u,'999',6,0,1)", ("$p", id), ("$u", koli));
        db.Execute("PRAGMA user_version=13");   // a store from before the cutover
        _ = new StoreDatabase(db.Path);        // next start upgrades it
        Assert.Equal(6d, Convert.ToDouble(db.Query("SELECT conversion_factor FROM product_units WHERE product_id=$p AND unit_id=$u", ("$p", id), ("$u", koli)).Rows[0][0]));
    }

    public void Dispose()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        try { Directory.Delete(_folder, true); } catch (IOException) { }
    }
}
