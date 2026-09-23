using System.Diagnostics;
using Microsoft.Data.Sqlite;
using R3.Infrastructure;
using Xunit.Abstractions;

namespace R3.Domain.Tests;

/// <summary>Stok Kartları / Stok Kartı (ASB düzeni): kart detay alanları, tam liste, önceki/sonraki, kayıt getir, seçim listeleri.</summary>
public sealed class StockCardServiceTests(ITestOutputHelper output) : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), "R3-tests-" + Guid.NewGuid());
    private const string Company = "00000000-0000-0000-0000-000000000001";

    private (StoreDatabase Db, LocalProductService Products, LocalStockCardService Cards, string Unit) Create()
    {
        var db = new StoreDatabase(Path.Combine(_folder, "test.db"));
        var unit = db.Query("SELECT id FROM units WHERE company_id=$c LIMIT 1", ("$c", Company)).Rows[0][0].ToString()!;
        return (db, new LocalProductService(db), new LocalStockCardService(db), unit);
    }

    private static ProductAggregateEdit Card(string code, string unit, params ProductChildEdit[] barcodes) =>
        new("", Company, code, "Kart " + code, "", "", unit, "Stock", 10, true, [], barcodes);

    [Fact]
    public void CardDetailFieldsRoundTripAndNullDetailsKeepWhatIsStored()
    {
        var (_, products, cards, unit) = Create();
        products.Save(Card("K-1", unit) with
        {
            Details = new ProductCardDetailsEdit(" Kısa ", "Not", 5, 3, 1.5m, 7, 2, true, true, 0, 0, "SET-1")
        });
        var id = cards.FindIdByCodeOrBarcode(Company, "K-1")!;
        var stored = products.GetDetail(id, Company)!.Product.Details!;
        Assert.Equal(new ProductCardDetailsEdit("Kısa", "Not", 5, 3, 1.5m, 7, 2, true, true, 0, 0, "SET-1"), stored);

        // Imports / bulk tools send no Details: nothing is wiped.
        products.Save(products.GetDetail(id, Company)!.Product with { Details = null, Name = "Yeni ad" });
        Assert.Equal("Not", products.GetDetail(id, Company)!.Product.Details!.Notes);

        Assert.Throws<ArgumentException>(() => products.Save(products.GetDetail(id, Company)!.Product with { Details = stored with { SalesDiscountRate = 120 } }));
    }

    [Fact]
    public void ListHasAsbColumnsSupplierVariantsAndActiveFilter()
    {
        var (db, products, cards, unit) = Create();
        products.Save(Card("A-1", unit, new ProductChildEdit("", "", "", Barcode: "8690000011112", UnitId: unit, IsPrimary: true)));
        products.Save(Card("A-2", unit) with { IsActive = false });
        var supplier = Guid.NewGuid().ToString();
        new LocalAccountService(db).Save(new AccountAggregateEdit(new AccountEdit(supplier, Company, "T-1", "Tedarikçi A.Ş.", "Supplier"), new(), new(), null, null));
        var id = cards.FindIdByCodeOrBarcode(Company, "A-1")!;
        var loaded = products.GetDetail(id, Company)!.Product;
        products.Save(loaded with
        {
            Suppliers = [new ProductSupplierEdit("", supplier, LeadTimeDays: 3, ExtraLeadTimeDays: 2)],
            Variants = [new ProductChildEdit("", "V1", "Kırmızı S", SizeCode: "S", ColorCode: "KIRMIZI", SizeType: "HARF"), new ProductChildEdit("", "V2", "Kırmızı M", SizeCode: "M", ColorCode: "KIRMIZI", SizeType: "HARF")],
            Policy = loaded.Policy with { ShipmentLocationType = "Headquarters" }
        });

        var active = cards.List(Company);
        var row = Assert.Single(active.Rows.Cast<System.Data.DataRow>());
        Assert.Equal(("A-1", "Tedarikçi A.Ş.", 5L, "Headquarters"), (row["StokKodu"], row["TedarikciAdi"], Convert.ToInt64(row["TedarikGun"]), row["SevkYeri"]));
        Assert.Equal(("KIRMIZI", "HARF", 1L, "8690000011112"), (row["RenkAdi"], row["BedenTipi"], Convert.ToInt64(row["BarkodSayisi"]), row["BirincilBarkod"]));
        Assert.Contains("S", row["Beden"].ToString()); Assert.Contains("M", row["Beden"].ToString());
        Assert.Equal(DBNull.Value, row["Fiyat"]);
        Assert.Equal(2, cards.List(Company, activeOnly: null).Rows.Count);
        Assert.Equal("A-2", cards.List(Company, activeOnly: false).Rows[0]["StokKodu"]);
        Assert.Equal(1, cards.ActiveCount(Company));
        Assert.Single(cards.RowDetail(id).Barcodes.Rows.Cast<System.Data.DataRow>());
    }

    [Fact]
    public void AdjacentFetchAndLookups()
    {
        var (db, products, cards, unit) = Create();
        foreach (var code in new[] { "B-1", "B-2", "B-3" }) products.Save(Card(code, unit, code == "B-2" ? [new ProductChildEdit("", "", "", Barcode: "8690000022223", UnitId: unit, IsPrimary: true)] : []));
        Assert.Equal("B-1", cards.Adjacent(Company, "B-2", next: false)!.Value.Code);
        Assert.Equal("B-3", cards.Adjacent(Company, "B-2", next: true)!.Value.Code);
        Assert.Null(cards.Adjacent(Company, "B-3", next: true));
        var b2 = cards.FindIdByCodeOrBarcode(Company, "b-2");
        Assert.Equal(b2, cards.FindIdByCodeOrBarcode(Company, "8690000022223"));
        Assert.Null(cards.FindIdByCodeOrBarcode(Company, "yok"));
        Assert.Equal(3, cards.Lookup("products", Company, "B-").Rows.Count);
        Assert.Equal(("B-2", "Kart B-2"), cards.Describe("products", b2!));
        Assert.Equal("Kart B-3", cards.NameOfCode(Company, "b-3"));
        Assert.Throws<ArgumentException>(() => cards.Lookup("drop table", Company));
    }

    /// <summary>ASB gibi tüm kartlar tek listede: 35.000 kartlık firmada liste sorgusu makul sürede dönmeli.</summary>
    [Fact]
    public void FullListOf35000CardsLoadsQuickly()
    {
        var (db, _, cards, unit) = Create();
        using (var connection = new SqliteConnection($"Data Source={db.Path}"))
        {
            connection.Open();
            using var tx = connection.BeginTransaction();
            using var insert = connection.CreateCommand(); insert.Transaction = tx;
            insert.CommandText = "INSERT INTO products(id,company_id,code,name,base_unit_id,product_type,vat_rate,created_at,updated_at) VALUES($id,$c,$code,$name,$u,'Stock',10,'','')";
            var id = insert.Parameters.Add("$id", SqliteType.Text); var code = insert.Parameters.Add("$code", SqliteType.Text); var name = insert.Parameters.Add("$name", SqliteType.Text);
            insert.Parameters.AddWithValue("$c", Company); insert.Parameters.AddWithValue("$u", unit);
            for (var i = 0; i < 35_000; i++) { id.Value = Guid.NewGuid().ToString(); code.Value = $"P{i:D6}"; name.Value = $"ÜRÜN {i}"; insert.ExecuteNonQuery(); }
            tx.Commit();
        }
        var watch = Stopwatch.StartNew();
        var rows = cards.List(Company).Rows.Count;
        watch.Stop();
        output.WriteLine($"35.000 kart: {watch.ElapsedMilliseconds} ms");
        Assert.Equal(35_000, rows);
        Assert.True(watch.Elapsed < TimeSpan.FromSeconds(5), $"Liste {watch.ElapsedMilliseconds} ms sürdü.");
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        try { Directory.Delete(_folder, true); } catch (IOException) { }
    }
}
