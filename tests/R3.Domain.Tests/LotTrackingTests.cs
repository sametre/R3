using R3.Infrastructure;

namespace R3.Domain.Tests;

/// <summary>Lot / Seri Takip through Stok Giriş / Çıkış fişleri (InventoryLotTracking + LocalLotService).</summary>
public sealed class LotTrackingTests : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), "R3-tests-" + Guid.NewGuid());
    private const string Company = "00000000-0000-0000-0000-000000000001";

    private sealed record World(StoreDatabase Db, LocalInventoryDocumentService Documents, string Branch, string Warehouse, string Unit);

    private World Create()
    {
        var db = new StoreDatabase(Path.Combine(_folder, "test.db"));
        return new World(db, new LocalInventoryDocumentService(db), db.Query("SELECT id FROM branches LIMIT 1").Rows[0][0].ToString()!,
            db.Query("SELECT id FROM warehouses LIMIT 1").Rows[0][0].ToString()!, db.Query("SELECT id FROM units LIMIT 1").Rows[0][0].ToString()!);
    }

    private static string Product(World w, string code, string tracking)
    {
        new LocalProductService(w.Db).Save(new ProductAggregateEdit("", Company, code, code, "", "", w.Unit, "Stock", 20, true, [], [], Policy: new ProductInventoryPolicyEdit(LotTrackingType: tracking)));
        return w.Db.Query("SELECT id FROM products WHERE code=$c", ("$c", code)).Rows[0][0].ToString()!;
    }

    private string Post(World w, string type, string product, decimal quantity, string? lot = null, string? serial = null, DateTime? expiry = null)
    {
        var id = w.Documents.CreateDraft(new InventoryDocumentEdit(Company, w.Branch, w.Warehouse, type, DateTime.Today,
            [new InventoryDocumentLineEdit(product, w.Unit, quantity, quantity, LotNo: lot, SerialNo: serial, ExpiryDate: expiry)]), "test");
        w.Documents.ApproveAsync(id, "test").GetAwaiter().GetResult();
        return id;
    }

    [Fact]
    public void LotTrackedProductNeedsALotAndLotsCannotGoNegative()
    {
        var w = Create(); var p = Product(w, "ILAC", "Lot");
        Assert.Throws<InvalidOperationException>(() => Post(w, "ManualIn", p, 10));   // no lot
        Post(w, "ManualIn", p, 10, lot: "l-001", expiry: DateTime.Today.AddDays(20));
        Post(w, "ManualIn", p, 5, lot: "L-002", expiry: DateTime.Today.AddDays(200));

        Assert.Throws<InvalidOperationException>(() => Post(w, "ManualOut", p, 11, lot: "L-001"));   // lot has 10 even though product has 15
        Assert.Throws<InvalidOperationException>(() => Post(w, "ManualOut", p, 1, lot: "L-999"));    // unknown lot
        Post(w, "ManualOut", p, 4, lot: "L-001");

        var lots = new LocalLotService(w.Db).Lots(Company).DefaultView.ToTable();
        Assert.Equal(6m, lots.Rows.Cast<System.Data.DataRow>().Single(r => r["LotNo"].ToString() == "L-001")["Miktar"]);   // stored upper-case
        Assert.Single(new LocalLotService(w.Db).Lots(Company, expiringWithinDays: 30).Rows);
        Assert.Equal(2, new LocalLotService(w.Db).Trace(Company, "l-001").Rows.Count);   // in 10, out 4 (the rejected out 11 left nothing)
    }

    [Fact]
    public void SerialTrackedProductNeedsOneSerialPerUnitAndTracksWhereEachOneIs()
    {
        var w = Create(); var p = Product(w, "TEL", "Serial");
        Assert.Throws<InvalidOperationException>(() => Post(w, "ManualIn", p, 2, serial: "SN1"));        // 2 units, 1 serial
        Post(w, "ManualIn", p, 2, serial: "SN1, SN2");
        Assert.Throws<InvalidOperationException>(() => Post(w, "ManualIn", p, 1, serial: "SN2"));        // already in stock
        Assert.Throws<InvalidOperationException>(() => Post(w, "ManualOut", p, 1, serial: "SN9"));       // never received
        Post(w, "ManualOut", p, 1, serial: "SN1");

        var serials = new LocalLotService(w.Db).Serials(Company).Rows.Cast<System.Data.DataRow>().ToDictionary(r => r["SeriNo"].ToString()!, r => r["Durum"].ToString());
        Assert.Equal("Çıkış yapıldı", serials["SN1"]);
        Assert.Equal("Depoda", serials["SN2"]);
        Assert.Equal(2, new LocalLotService(w.Db).Trace(Company, "SN1").Rows.Count);   // in, out
    }

    [Fact]
    public async Task ReversingAFisUndoesItsLotAndSerialEffects()
    {
        var w = Create(); var p = Product(w, "TEL", "Serial");
        var fis = Post(w, "ManualIn", p, 1, serial: "SN1");
        await w.Documents.ReverseAsync(fis, "test");
        Assert.Equal("Çıkış yapıldı", new LocalLotService(w.Db).Serials(Company).Rows[0]["Durum"]);
        Post(w, "ManualIn", p, 1, serial: "SN1");   // can be received again after the reversal
    }

    [Fact]
    public async Task UntrackedProductsAcceptOptionalLotsAndCoverageFlagsUnassignedStock()
    {
        var w = Create(); var plain = Product(w, "VIDA", "None"); var tracked = Product(w, "ILAC", "Lot");
        Post(w, "ManualIn", plain, 3);                    // nothing enforced
        Post(w, "ManualIn", tracked, 5, lot: "L1");
        await new LocalInventoryService(w.Db).PostManualInAsync(new(Company, w.Branch, w.Warehouse, tracked, null, 2, null, DateTime.UtcNow));   // a flow without lots

        var row = new LocalLotService(w.Db).Coverage(Company).Rows.Cast<System.Data.DataRow>().Single();
        Assert.Equal((7m, 5m, 2m), ((decimal)row["Stok"], (decimal)row["Atanan"], (decimal)row["Atanmamis"]));
    }

    public void Dispose()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        try { Directory.Delete(_folder, true); } catch (IOException) { }
    }
}
