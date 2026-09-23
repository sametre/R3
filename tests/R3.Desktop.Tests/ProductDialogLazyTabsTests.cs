using System.IO;
using R3.Infrastructure;

namespace R3.Desktop.Tests;

/// <summary>ProductDialog builds every tab except "Genel" on first open. Saving a card without opening
/// any tab must still return everything that was loaded (classification, suppliers, units, barcodes).</summary>
public sealed class ProductDialogLazyTabsTests : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), "R3-desktop-tests-" + Guid.NewGuid());
    private const string Company = "00000000-0000-0000-0000-000000000001";

    [Fact]
    public void UnopenedTabsDoNotLoseLoadedState()
    {
        var db = new StoreDatabase(Path.Combine(_folder, "test.db"));
        var unit = db.Query("SELECT id FROM units LIMIT 1").Rows[0][0].ToString()!;
        var supplier = Guid.NewGuid().ToString();
        new LocalAccountService(db).Save(new AccountAggregateEdit(new AccountEdit(supplier, Company, "T1", "Tedarikçi", "Supplier"), new AccountTaxProfileEdit(), new AccountEInvoiceProfileEdit(), null, new SupplierProfileEdit()));
        var country = db.Query("SELECT id FROM countries WHERE code='TR'").Rows[0][0].ToString()!;
        var products = new LocalProductService(db);
        products.Save(new ProductAggregateEdit("", Company, "LZ1", "Tembel sekme", "", "", unit, "Stock", 20, true, [], [new("", "", "", Barcode: "8690000000011", UnitId: unit, IsPrimary: true)],
            MinimumStock: 3, Suppliers: [new("", supplier, "SUP-1", true, 4, 0, 1)]) { OriginCountryId = country });
        var id = db.Query("SELECT id FROM products WHERE code='LZ1'").Rows[0][0].ToString()!;
        var detail = products.GetDetail(id, Company)!;

        ProductAggregateEdit? edit = null; Exception? failure = null;
        var thread = new Thread(() =>
        {
            try { edit = new ProductDialog(null, unit, Company, db, detail).ToEditModel(); }
            catch (Exception ex) { failure = ex; }
        });
        thread.SetApartmentState(ApartmentState.STA); thread.Start(); thread.Join();

        Assert.Null(failure);
        Assert.Equal(country, edit!.OriginCountryId);        // combo lives on the lazy Sınıflandırma tab
        Assert.Equal(unit, edit.UnitId);
        Assert.Equal(3m, edit.MinimumStock);                  // field lives on the lazy Stok & Sipariş tab
        Assert.Equal(supplier, Assert.Single(edit.Suppliers).SupplierAccountId);
        Assert.Equal("8690000000011", Assert.Single(edit.Barcodes).Barcode);
    }

    [Fact]
    public void ProductLookupIndexesExistEvenOnAnAlreadyStampedStore()
    {
        var path = Path.Combine(_folder, "stamped.db");
        var db = new StoreDatabase(path);
        db.Execute("DROP INDEX IX_ProductBarcodes_Product");
        new StoreDatabase(path); // user_version is already latest: the fast path must still restore it
        Assert.Equal(1L, db.Query("SELECT COUNT(*) FROM sqlite_master WHERE type='index' AND name='IX_ProductBarcodes_Product'").Rows[0][0]);
        var plan = string.Join(" ", db.Query("EXPLAIN QUERY PLAN SELECT barcode FROM product_barcodes WHERE product_id='x' AND is_primary=1").Rows.Cast<System.Data.DataRow>().Select(r => r[r.Table.Columns.Count - 1].ToString()));
        Assert.Contains("IX_ProductBarcodes_Product", plan);
    }

    public void Dispose()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        try { Directory.Delete(_folder, true); } catch (IOException) { }
    }
}
