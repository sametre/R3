using R3.Infrastructure;
using Microsoft.Data.Sqlite;

namespace R3.Domain.Tests;

public sealed class StoreDatabaseTests : IDisposable
{
    private readonly string _folder = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "R3-tests-" + Guid.NewGuid());
    private StoreDatabase Create() => new(System.IO.Path.Combine(_folder, "test.db"));
    [Fact]
    public void RecordsAndExactAmountsSurviveReopening()
    {
        var db = Create();
        db.Save(true, null, "M01", "Merkez", "", "");
        db.Save(false, null, "C01", "İpek", "555", "Adres");
        var customer = Convert.ToInt64(db.List(false).Rows[0]["Id"]);
        var store = Convert.ToInt64(db.List(true).Rows[0]["Id"]);
        db.AddMovement(customer, store, DateTime.Today, "Borç", 1234.56m, "Açılış");
        db.AddMovement(customer, store, DateTime.Today, "Tahsilat", 234.55m, "Ödeme");
        var reopened = Create(); var ledger = reopened.Ledger(customer);
        Assert.Equal(2, ledger.Rows.Count);
        Assert.Equal(123456L, ledger.Rows[0]["BorçKuruş"]);
        Assert.Equal(23455L, ledger.Rows[1]["AlacakKuruş"]);
        reopened.Save(false, customer, "C01", "İpek Yeni", "555", "Adres");
        Assert.Equal("İpek Yeni", Create().List(false).Rows[0]["Ad"]);
    }
    [Fact]
    public void InvalidRecordsDoNotPersist()
    {
        var db = Create();
        Assert.Throws<ArgumentException>(() => db.Save(true, null, " ", "Merkez", "", ""));
        db.Save(true, null, "M01", "Merkez", "", "");
        Assert.Throws<SqliteException>(() => db.Save(true, null, "m01", "Diğer", "", ""));
        Assert.Throws<ArgumentException>(() => db.AddMovement(1, 1, DateTime.Today, "Borç", 1.001m, ""));
        Assert.Throws<ArgumentException>(() => db.AddMovement(1, 1, DateTime.Today, "Borç", 0, ""));
        Assert.Throws<SqliteException>(() => db.AddMovement(999, 1, DateTime.Today, "Borç", 1, ""));
        Assert.Empty(db.Ledger(999).Rows.Cast<System.Data.DataRow>());
    }
    [Fact]
    public void CanonicalMasterDataCanBeCreatedAndSearched()
    {
        var db = Create(); var service = new R3.Infrastructure.LocalMasterDataService(db);
        service.Save("brands", null, new R3.Infrastructure.MasterRecord(" br01 ", "Karaca"));
        Assert.Single(service.List("brands").Rows.Cast<System.Data.DataRow>());
        service.Save("units", null, new R3.Infrastructure.MasterRecord("KG", "Kilogram", Extra: "3"));
        Assert.Contains(service.List("units").Rows.Cast<System.Data.DataRow>(), row => row["Kod"].ToString() == "KG");
    }
    [Fact]
    public void ProductAggregateRollsBackWhenBarcodeIsDuplicate()
    {
        var db = Create(); var service = new R3.Infrastructure.LocalProductService(db); var unit = db.Query("SELECT id FROM units LIMIT 1").Rows[0][0].ToString()!;
        service.Save(new R3.Infrastructure.ProductAggregateEdit("", "00000000-0000-0000-0000-000000000001", "P01", "Ürün", "", "", unit, "Stock", 20, true, [], [new("", "", "", Barcode: "0001", UnitId: unit)]));
        Assert.Throws<ArgumentException>(() => service.Save(new R3.Infrastructure.ProductAggregateEdit("", "00000000-0000-0000-0000-000000000001", "P02", "İkinci", "", "", unit, "Stock", 20, true, [new("", "V1", "Varyant")], [new("", "", "", Barcode: "0001", UnitId: unit)])));
        Assert.Empty(db.Query("SELECT id FROM products WHERE code='P02'").Rows);
        Assert.Empty(db.Query("SELECT id FROM product_variants WHERE code='V1'").Rows);
    }
    [Fact]
    public async Task InventoryPostingMaintainsLedgerAndBalance()
    {
        var db = Create(); var unit = db.Query("SELECT id FROM units LIMIT 1").Rows[0][0].ToString()!; var product = Guid.NewGuid().ToString(); var warehouse = db.Query("SELECT id,branch_id FROM warehouses LIMIT 1").Rows[0];
        db.Execute("INSERT INTO products(id,company_id,code,name,base_unit_id,product_type,vat_rate,is_active,created_at,updated_at) VALUES($id,$company,'INV01','Inventory Product',$unit,'Stock',20,1,$now,$now)", ("$id", product), ("$company", "00000000-0000-0000-0000-000000000001"), ("$unit", unit), ("$now", DateTime.UtcNow.ToString("O")));
        var service = new R3.Infrastructure.LocalInventoryService(db); var post = new R3.Infrastructure.InventoryPost("00000000-0000-0000-0000-000000000001", warehouse["branch_id"].ToString()!, warehouse["id"].ToString()!, product, null, 10, null, DateTime.UtcNow);
        await service.PostOpeningBalanceAsync(post); await service.PostManualOutAsync(post with { Quantity = 3 }); await service.PostManualInAsync(post with { Quantity = 2 });
        Assert.Equal(9m, (await service.GetBalanceAsync(post.WarehouseId, product, null))!.QuantityOnHand); await Assert.ThrowsAsync<InvalidOperationException>(() => service.PostManualOutAsync(post with { Quantity = 20 }));
    }
    [Fact]
    public async Task SalesPostCreatesIssueAndAccountDebitAndRejectsDoublePost()
    {
        var db = Create(); var company = "00000000-0000-0000-0000-000000000001"; var branch = db.Query("SELECT id FROM branches LIMIT 1").Rows[0][0].ToString()!; var warehouse = db.Query("SELECT id FROM warehouses LIMIT 1").Rows[0][0].ToString()!; var unit = db.Query("SELECT id FROM units LIMIT 1").Rows[0][0].ToString()!; var account = Guid.NewGuid().ToString(); var product = Guid.NewGuid().ToString();
        db.Execute("INSERT INTO accounts(id,company_id,code,name,account_type,is_active,created_at,updated_at) VALUES($id,$c,'C01','Müşteri','Customer',1,$n,$n)", ("$id", account), ("$c", company), ("$n", DateTime.UtcNow.ToString("O")));
        db.Execute("INSERT INTO products(id,company_id,code,name,base_unit_id,product_type,vat_rate,is_active,created_at,updated_at) VALUES($id,$c,'P01','Ürün',$u,'Stock',20,1,$n,$n)", ("$id", product), ("$c", company), ("$u", unit), ("$n", DateTime.UtcNow.ToString("O")));
        var inventory = new LocalInventoryService(db); await inventory.PostOpeningBalanceAsync(new(company, branch, warehouse, product, null, 10, null, DateTime.UtcNow));
        var sales = new LocalSalesService(db); var id = sales.CreateDraft(new("", company, branch, warehouse, account, DateTime.Today, "", [new(product, null, unit, null, 3, 1, 100, 0, 20)])); sales.Post(id);
        Assert.Equal("Posted", db.Query("SELECT status FROM sales_documents WHERE id=$id", ("$id", id)).Rows[0][0]); Assert.Equal(7m, (await inventory.GetBalanceAsync(warehouse, product, null))!.QuantityOnHand); Assert.Equal(360m, Convert.ToDecimal(db.Query("SELECT debit FROM account_transactions WHERE document_id=$id", ("$id", id)).Rows[0][0])); Assert.Throws<InvalidOperationException>(() => sales.Post(id)); Assert.Single(db.Query("SELECT id FROM inventory_transactions WHERE document_id=$id", ("$id", id)).Rows.Cast<System.Data.DataRow>());
    }
    [Fact]
    public async Task SalesPostRollsBackAllLinesWhenSecondLineInsufficient()
    {
        var db = Create(); var c = "00000000-0000-0000-0000-000000000001"; var branch = db.Query("SELECT id FROM branches LIMIT 1").Rows[0][0].ToString()!; var wh = db.Query("SELECT id FROM warehouses LIMIT 1").Rows[0][0].ToString()!; var unit = db.Query("SELECT id FROM units LIMIT 1").Rows[0][0].ToString()!; var acc = Guid.NewGuid().ToString(); var p1 = Guid.NewGuid().ToString(); var p2 = Guid.NewGuid().ToString(); var now = DateTime.UtcNow.ToString("O");
        db.Execute("INSERT INTO accounts(id,company_id,code,name,account_type,is_active,created_at,updated_at) VALUES($id,$c,'C02','Müşteri','Customer',1,$n,$n)", ("$id", acc), ("$c", c), ("$n", now)); foreach (var item in new[] { (p1, "PA"), (p2, "PB") }) db.Execute("INSERT INTO products(id,company_id,code,name,base_unit_id,product_type,vat_rate,is_active,created_at,updated_at) VALUES($id,$c,$code,$code,$u,'Stock',20,1,$n,$n)", ("$id", item.Item1), ("$c", c), ("$code", item.Item2), ("$u", unit), ("$n", now));
        var inv = new LocalInventoryService(db); await inv.PostOpeningBalanceAsync(new(c, branch, wh, p1, null, 10, null, DateTime.UtcNow)); await inv.PostOpeningBalanceAsync(new(c, branch, wh, p2, null, 2, null, DateTime.UtcNow)); var sales = new LocalSalesService(db); var id = sales.CreateDraft(new("", c, branch, wh, acc, DateTime.Today, "", [new(p1, null, unit, null, 4, 1, 10, 0, 20), new(p2, null, unit, null, 5, 1, 10, 0, 20)])); await Assert.ThrowsAsync<InvalidOperationException>(() => Task.Run(() => sales.Post(id))); Assert.Equal("Draft", db.Query("SELECT status FROM sales_documents WHERE id=$id", ("$id", id)).Rows[0][0]); Assert.Equal(10m, (await inv.GetBalanceAsync(wh, p1, null))!.QuantityOnHand); Assert.Equal(2m, (await inv.GetBalanceAsync(wh, p2, null))!.QuantityOnHand); Assert.Empty(db.Query("SELECT id FROM account_transactions WHERE document_id=$id", ("$id", id)).Rows);
    }
    public void Dispose() { SqliteConnection.ClearAllPools(); if (Directory.Exists(_folder)) Directory.Delete(_folder, true); }
}
