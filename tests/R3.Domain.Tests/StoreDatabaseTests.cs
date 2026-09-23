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
        service.Save("product_attributes", null, new R3.Infrastructure.MasterRecord("MALZEME", "Malzeme"));
        Assert.Contains(service.List("product_attributes").Rows.Cast<System.Data.DataRow>(), row => row["Kod"].ToString() == "MALZEME");
        service.Save("variant_definitions", null, new R3.Infrastructure.MasterRecord("KRMZ", "Kırmızı", Extra: "Renk"));
        service.Save("variant_definitions", null, new R3.Infrastructure.MasterRecord("KRMZ", "Kırmızı", Extra: "Beden"));
        Assert.Equal(2, service.List("variant_definitions").Rows.Cast<System.Data.DataRow>().Count(row => row["Kod"].ToString() == "KRMZ"));
    }
    [Fact]
    public void WarehouseDefaultsAndLocationsPersistWithBusinessRules()
    {
        var db = Create();
        var company = "00000000-0000-0000-0000-000000000001";
        var branch = db.Query("SELECT id FROM branches WHERE company_id=$company LIMIT 1", ("$company", company)).Rows[0][0].ToString()!;
        var service = new LocalWarehouseService(db);

        service.Save(new WarehouseEdit("", company, branch, "D01", "Merkez Depo", IsDefaultInbound: true, RequiresLocation: true), "tester");
        service.Save(new WarehouseEdit("", company, branch, "D02", "Sevk Deposu", IsDefaultInbound: true, IsDefaultOutbound: true), "tester");
        var warehouse = db.Query("SELECT id FROM warehouses WHERE code='D02'").Rows[0][0].ToString()!;

        Assert.Equal(1, db.Query("SELECT id FROM warehouses WHERE company_id=$company AND branch_id=$branch AND is_default_inbound=1", ("$company", company), ("$branch", branch)).Rows.Count);
        Assert.Equal("D02", db.Query("SELECT code FROM warehouses WHERE is_default_inbound=1").Rows[0][0].ToString());

        service.SaveLocation(new WarehouseLocationEdit("", warehouse, "A-01", "A Blok Raf 01", LocationType: "Rack", Rack: "01", Barcode: "LOC-D02-A01", Capacity: 250, IsDefaultInbound: true), "tester");
        var location = service.Locations(warehouse).Rows[0];
        Assert.Equal("A-01", location["LokasyonKodu"].ToString());
        Assert.Equal("LOC-D02-A01", location["Barkod"].ToString());
        Assert.Equal(250m, Convert.ToDecimal(location["Kapasite"]));
        Assert.Throws<ArgumentException>(() => service.SaveLocation(new WarehouseLocationEdit("", warehouse, "A-02", "Geçersiz", Capacity: -1), "tester"));
    }
    [Fact]
    public async Task InventoryDocumentPostsToSelectedLocationAndValidatesLocationOwnership()
    {
        var db = Create(); var company = "00000000-0000-0000-0000-000000000001"; var branch = db.Query("SELECT id FROM branches WHERE company_id=$c LIMIT 1", ("$c", company)).Rows[0][0].ToString()!; var warehouse = db.Query("SELECT id FROM warehouses WHERE branch_id=$b LIMIT 1", ("$b", branch)).Rows[0][0].ToString()!; var unit = db.Query("SELECT id FROM units LIMIT 1").Rows[0][0].ToString()!; var product = Guid.NewGuid().ToString(); var now = DateTime.UtcNow.ToString("O");
        db.Execute("INSERT INTO products(id,company_id,code,name,base_unit_id,product_type,vat_rate,is_active,created_at,updated_at) VALUES($id,$company,'LOC01','Lokasyon Ürünü',$unit,'Stock',20,1,$now,$now)", ("$id", product), ("$company", company), ("$unit", unit), ("$now", now));
        var warehouses = new LocalWarehouseService(db); warehouses.SaveLocation(new WarehouseLocationEdit("", warehouse, "R-01", "Raf 01", IsDefaultInbound: true), "tester"); var location = db.Query("SELECT id FROM warehouse_locations WHERE warehouse_id=$w AND code='R-01'", ("$w", warehouse)).Rows[0][0].ToString()!;
        var documents = new LocalInventoryDocumentService(db); var id = documents.CreateDraft(new(company, branch, warehouse, "ManualIn", DateTime.Today, [new(product, unit, 4, 4, LocationId: location)]), "tester"); await documents.ApproveAsync(id, "tester");
        Assert.Equal(4m, Convert.ToDecimal(db.Query("SELECT quantity_available FROM warehouse_location_balances WHERE location_id=$l AND product_id=$p", ("$l", location), ("$p", product)).Rows[0][0]));
        Assert.Equal(location, db.Query("SELECT location_id FROM inventory_transactions WHERE document_id=$d", ("$d", id)).Rows[0][0].ToString());
    }
    [Fact]
    public async Task TransferCreatesAtomicOutAndInWithSharedCorrelation()
    {
        var db = Create(); var company = "00000000-0000-0000-0000-000000000001"; var branch = db.Query("SELECT id FROM branches WHERE company_id=$c LIMIT 1", ("$c", company)).Rows[0][0].ToString()!; var source = db.Query("SELECT id FROM warehouses WHERE branch_id=$b LIMIT 1", ("$b", branch)).Rows[0][0].ToString()!; var target = Guid.NewGuid().ToString(); var unit = db.Query("SELECT id FROM units LIMIT 1").Rows[0][0].ToString()!; var product = Guid.NewGuid().ToString(); var now = DateTime.UtcNow.ToString("O");
        db.Execute("INSERT INTO warehouses(id,company_id,branch_id,code,name,is_active,created_at,updated_at) VALUES($id,$c,$b,'TRG01','Transfer Hedefi',1,$now,$now)", ("$id", target), ("$c", company), ("$b", branch), ("$now", now)); db.Execute("INSERT INTO products(id,company_id,code,name,base_unit_id,product_type,vat_rate,is_active,created_at,updated_at) VALUES($id,$c,'TRF01','Transfer Ürünü',$u,'Stock',20,1,$now,$now)", ("$id", product), ("$c", company), ("$u", unit), ("$now", now));
        var inventory = new LocalInventoryService(db); await inventory.PostOpeningBalanceAsync(new(company, branch, source, product, null, 10, null, DateTime.UtcNow)); var transfers = new LocalInventoryTransferService(db); var transfer = transfers.CreateDraft(new(company, branch, source, null, target, null, DateTime.Today, [new(product, unit, 4, 4)]), "tester"); await transfers.ApproveAsync(transfer, "tester");
        Assert.Equal(6m, (await inventory.GetBalanceAsync(source, product, null))!.QuantityAvailable); Assert.Equal(4m, (await inventory.GetBalanceAsync(target, product, null))!.QuantityAvailable); var rows = db.Query("SELECT transaction_type,correlation_id FROM inventory_transactions WHERE document_id=$d ORDER BY transaction_type", ("$d", transfer)).Rows; Assert.Equal(2, rows.Count); Assert.Equal(rows[0]["correlation_id"].ToString(), rows[1]["correlation_id"].ToString());
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
    public void ProductCardPersistsDetailedStockTaxAndImageFields()
    {
        var db = Create(); var service = new LocalProductService(db); var company = "00000000-0000-0000-0000-000000000001"; var unit = db.Query("SELECT id FROM units LIMIT 1").Rows[0][0].ToString()!;
        service.Save(new ProductAggregateEdit("", company, "DET01", "Detaylı Ürün", "", "", unit, "Stock", 20, true, [], [], PurchaseVatRate: 10, ExciseRate: 5, MinimumStock: 3, MaximumStock: 40, MinimumOrderQuantity: 2, OrderMultiple: 2, IsSellable: true, ImagePath: @"C:\images\det01.png"));
        var id = db.Query("SELECT id FROM products WHERE code='DET01'").Rows[0][0].ToString()!;
        var detail = service.GetDetail(id, company)!.Product;
        Assert.Equal(10m, detail.PurchaseVatRate); Assert.Equal(5m, detail.ExciseRate); Assert.Equal(3m, detail.MinimumStock); Assert.Equal(40m, detail.MaximumStock); Assert.Equal(2m, detail.MinimumOrderQuantity); Assert.Equal(2m, detail.OrderMultiple); Assert.True(detail.IsSellable); Assert.Equal(@"C:\images\det01.png", detail.ImagePath);
    }
    [Fact]
    public void GridLayoutsAreStoredPerUserAndView()
    {
        var db = Create(); var now = DateTime.UtcNow.ToString("O");
        db.Execute("INSERT INTO users(id,username,display_name,password_hash,is_active,created_at,updated_at) VALUES('u2','operator','Operatör','x',1,$now,$now)", ("$now", now));
        var admin = new UserGridLayoutService(db, "admin"); var other = new UserGridLayoutService(db, "operator");
        admin.Save("accounts.list", "{\"owner\":\"admin\"}"); other.Save("accounts.list", "{\"owner\":\"operator\"}");
        Assert.Contains("admin", admin.Load("accounts.list")); Assert.Contains("operator", other.Load("accounts.list"));
        admin.Reset("accounts.list"); Assert.Null(admin.Load("accounts.list")); Assert.NotNull(other.Load("accounts.list"));
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
    public async Task InventoryDocumentStaysDraftUntilApprovalAndPostsExactlyOnce()
    {
        var db = Create(); var company = "00000000-0000-0000-0000-000000000001"; var branch = db.Query("SELECT id FROM branches LIMIT 1").Rows[0][0].ToString()!; var warehouse = db.Query("SELECT id FROM warehouses LIMIT 1").Rows[0][0].ToString()!; var unit = db.Query("SELECT id FROM units LIMIT 1").Rows[0][0].ToString()!; var product = Guid.NewGuid().ToString(); var now = DateTime.UtcNow.ToString("O");
        db.Execute("INSERT INTO products(id,company_id,code,name,base_unit_id,product_type,vat_rate,is_active,created_at,updated_at) VALUES($id,$company,'DOC01','Fiş Ürünü',$unit,'Stock',20,1,$now,$now)", ("$id", product), ("$company", company), ("$unit", unit), ("$now", now));
        var service = new LocalInventoryDocumentService(db); var id = service.CreateDraft(new(company, branch, warehouse, "ManualIn", DateTime.Today, [new(product, unit, 5, 60, UnitCost: 12.5m)], "REF-1", "Açılış mal kabulü"), "tester");
        Assert.Equal("Draft", db.Query("SELECT status FROM inventory_documents WHERE id=$id", ("$id", id)).Rows[0][0]); Assert.Empty(db.Query("SELECT id FROM inventory_transactions WHERE document_id=$id", ("$id", id)).Rows);
        var number = await service.ApproveAsync(id, "tester"); Assert.StartsWith("SG-", number); Assert.Equal("Approved", db.Query("SELECT status FROM inventory_documents WHERE id=$id", ("$id", id)).Rows[0][0]);
        Assert.Equal(60m, Convert.ToDecimal(db.Query("SELECT quantity_on_hand FROM inventory_balances WHERE warehouse_id=$w AND product_id=$p", ("$w", warehouse), ("$p", product)).Rows[0][0])); Assert.Single(db.Query("SELECT id FROM inventory_transactions WHERE document_id=$id", ("$id", id)).Rows.Cast<System.Data.DataRow>());
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.ApproveAsync(id, "tester")); Assert.Single(db.Query("SELECT id FROM inventory_transactions WHERE document_id=$id", ("$id", id)).Rows.Cast<System.Data.DataRow>());
    }
    [Fact]
    public async Task InventoryDocumentOutboundRejectsInsufficientAvailableStockAtomically()
    {
        var db = Create(); var company = "00000000-0000-0000-0000-000000000001"; var branch = db.Query("SELECT id FROM branches LIMIT 1").Rows[0][0].ToString()!; var warehouse = db.Query("SELECT id FROM warehouses LIMIT 1").Rows[0][0].ToString()!; var unit = db.Query("SELECT id FROM units LIMIT 1").Rows[0][0].ToString()!; var product = Guid.NewGuid().ToString(); var now = DateTime.UtcNow.ToString("O");
        db.Execute("INSERT INTO products(id,company_id,code,name,base_unit_id,product_type,vat_rate,is_active,created_at,updated_at) VALUES($id,$company,'DOC02','Çıkış Ürünü',$unit,'Stock',20,1,$now,$now)", ("$id", product), ("$company", company), ("$unit", unit), ("$now", now));
        var inventory = new LocalInventoryService(db); await inventory.PostOpeningBalanceAsync(new(company, branch, warehouse, product, null, 4, null, DateTime.UtcNow)); var documents = new LocalInventoryDocumentService(db); var id = documents.CreateDraft(new(company, branch, warehouse, "ManualOut", DateTime.Today, [new(product, unit, 6, 6, UnitCost: 10)]), "tester");
        await Assert.ThrowsAsync<InvalidOperationException>(() => documents.ApproveAsync(id, "tester")); Assert.Equal("Draft", db.Query("SELECT status FROM inventory_documents WHERE id=$id", ("$id", id)).Rows[0][0]); Assert.Equal(4m, (await inventory.GetBalanceAsync(warehouse, product, null))!.QuantityAvailable); Assert.Empty(db.Query("SELECT id FROM inventory_transactions WHERE document_id=$id", ("$id", id)).Rows);
    }
    [Fact]
    public async Task ApprovedInventoryDocumentCanOnlyBeReversedWithAnAtomicOppositeMovement()
    {
        var db = Create(); var company = "00000000-0000-0000-0000-000000000001"; var branch = db.Query("SELECT id FROM branches LIMIT 1").Rows[0][0].ToString()!; var warehouse = db.Query("SELECT id FROM warehouses LIMIT 1").Rows[0][0].ToString()!; var unit = db.Query("SELECT id FROM units LIMIT 1").Rows[0][0].ToString()!; var product = Guid.NewGuid().ToString(); var now = DateTime.UtcNow.ToString("O");
        db.Execute("INSERT INTO products(id,company_id,code,name,base_unit_id,product_type,vat_rate,is_active,created_at,updated_at) VALUES($id,$company,'DOC03','Ters Ürün',$unit,'Stock',20,1,$now,$now)", ("$id", product), ("$company", company), ("$unit", unit), ("$now", now));
        var documents = new LocalInventoryDocumentService(db); var id = documents.CreateDraft(new(company, branch, warehouse, "ManualIn", DateTime.Today, [new(product, unit, 3, 3)]), "tester"); await documents.ApproveAsync(id, "tester"); await documents.ReverseAsync(id, "tester");
        Assert.Equal("Reversed", db.Query("SELECT status FROM inventory_documents WHERE id=$id", ("$id", id)).Rows[0][0]); Assert.Equal(0m, Convert.ToDecimal(db.Query("SELECT quantity_on_hand FROM inventory_balances WHERE warehouse_id=$w AND product_id=$p", ("$w", warehouse), ("$p", product)).Rows[0][0])); Assert.Equal(2, db.Query("SELECT id FROM inventory_transactions WHERE document_id=$id", ("$id", id)).Rows.Count); Assert.Equal(2, db.Query("SELECT id FROM audit_logs WHERE entity_type='InventoryDocument' AND entity_id=$id", ("$id", id)).Rows.Count); await Assert.ThrowsAsync<InvalidOperationException>(() => documents.ReverseAsync(id, "tester"));
    }
    [Fact]
    public async Task SalesPostCreatesIssueAndAccountDebitAndRejectsDoublePost()
    {
        var db = Create(); var company = "00000000-0000-0000-0000-000000000001"; var branch = db.Query("SELECT id FROM branches LIMIT 1").Rows[0][0].ToString()!; var warehouse = db.Query("SELECT id FROM warehouses LIMIT 1").Rows[0][0].ToString()!; var unit = db.Query("SELECT id FROM units LIMIT 1").Rows[0][0].ToString()!; var account = Guid.NewGuid().ToString(); var product = Guid.NewGuid().ToString();
        db.Execute("INSERT INTO accounts(id,company_id,code,name,account_type,is_active,created_at,updated_at) VALUES($id,$c,'C01','Müşteri','Customer',1,$n,$n)", ("$id", account), ("$c", company), ("$n", DateTime.UtcNow.ToString("O")));
        db.Execute("INSERT INTO products(id,company_id,code,name,base_unit_id,product_type,vat_rate,is_active,created_at,updated_at) VALUES($id,$c,'P01','Ürün',$u,'Stock',20,1,$n,$n)", ("$id", product), ("$c", company), ("$u", unit), ("$n", DateTime.UtcNow.ToString("O")));
        var inventory = new LocalInventoryService(db); await inventory.PostOpeningBalanceAsync(new(company, branch, warehouse, product, null, 10, null, DateTime.UtcNow));
        var sales = new LocalSalesService(db, new ElectronicDocumentRoutingService(db), new LocalElectronicDocumentService(db)); var id = sales.CreateDraft(new("", company, branch, warehouse, account, DateTime.Today, "", [new(product, null, unit, null, 3, 1, 100, 0, 20)])); sales.Post(id, "test-user");
        Assert.Equal("Posted", db.Query("SELECT status FROM sales_documents WHERE id=$id", ("$id", id)).Rows[0][0]); Assert.Equal(7m, (await inventory.GetBalanceAsync(warehouse, product, null))!.QuantityOnHand); Assert.Equal(360m, Convert.ToDecimal(db.Query("SELECT debit FROM account_transactions WHERE document_id=$id", ("$id", id)).Rows[0][0])); Assert.Throws<InvalidOperationException>(() => sales.Post(id, "test-user")); Assert.Single(db.Query("SELECT id FROM inventory_transactions WHERE document_id=$id", ("$id", id)).Rows.Cast<System.Data.DataRow>());
    }
    [Fact]
    public async Task SalesPostRollsBackAllLinesWhenSecondLineInsufficient()
    {
        var db = Create(); var c = "00000000-0000-0000-0000-000000000001"; var branch = db.Query("SELECT id FROM branches LIMIT 1").Rows[0][0].ToString()!; var wh = db.Query("SELECT id FROM warehouses LIMIT 1").Rows[0][0].ToString()!; var unit = db.Query("SELECT id FROM units LIMIT 1").Rows[0][0].ToString()!; var acc = Guid.NewGuid().ToString(); var p1 = Guid.NewGuid().ToString(); var p2 = Guid.NewGuid().ToString(); var now = DateTime.UtcNow.ToString("O");
        db.Execute("INSERT INTO accounts(id,company_id,code,name,account_type,is_active,created_at,updated_at) VALUES($id,$c,'C02','Müşteri','Customer',1,$n,$n)", ("$id", acc), ("$c", c), ("$n", now)); foreach (var item in new[] { (p1, "PA"), (p2, "PB") }) db.Execute("INSERT INTO products(id,company_id,code,name,base_unit_id,product_type,vat_rate,is_active,created_at,updated_at) VALUES($id,$c,$code,$code,$u,'Stock',20,1,$n,$n)", ("$id", item.Item1), ("$c", c), ("$code", item.Item2), ("$u", unit), ("$n", now));
        var inv = new LocalInventoryService(db); await inv.PostOpeningBalanceAsync(new(c, branch, wh, p1, null, 10, null, DateTime.UtcNow)); await inv.PostOpeningBalanceAsync(new(c, branch, wh, p2, null, 2, null, DateTime.UtcNow)); var sales = new LocalSalesService(db, new ElectronicDocumentRoutingService(db), new LocalElectronicDocumentService(db)); var id = sales.CreateDraft(new("", c, branch, wh, acc, DateTime.Today, "", [new(p1, null, unit, null, 4, 1, 10, 0, 20), new(p2, null, unit, null, 5, 1, 10, 0, 20)])); await Assert.ThrowsAsync<InvalidOperationException>(() => Task.Run(() => sales.Post(id, "test-user"))); Assert.Equal("Draft", db.Query("SELECT status FROM sales_documents WHERE id=$id", ("$id", id)).Rows[0][0]); Assert.Equal(10m, (await inv.GetBalanceAsync(wh, p1, null))!.QuantityOnHand); Assert.Equal(2m, (await inv.GetBalanceAsync(wh, p2, null))!.QuantityOnHand); Assert.Empty(db.Query("SELECT id FROM account_transactions WHERE document_id=$id", ("$id", id)).Rows); Assert.Empty(db.Query("SELECT id FROM electronic_documents WHERE source_entity_id=$id", ("$id", id)).Rows);
    }
    [Fact]
    public void AccountDashboardStatementAndRoleFiltersUseLedgerProjections()
    {
        var db = Create(); var service = new LocalAccountService(db); var company = "00000000-0000-0000-0000-000000000001";
        var branch = db.Query("SELECT id FROM branches LIMIT 1").Rows[0][0].ToString()!; var account = Guid.NewGuid().ToString(); var now = DateTime.UtcNow.ToString("O");
        service.Save(new AccountAggregateEdit(new AccountEdit(account, company, "CS01", "Çift Rollü Cari", "CustomerAndSupplier", CreditLimit: 100), new AccountTaxProfileEdit(), new AccountEInvoiceProfileEdit(), null, null));
        db.Execute("INSERT INTO account_transactions(id,company_id,branch_id,account_id,transaction_type,debit,credit,currency_code,exchange_rate,description,transaction_at,created_at) VALUES($id,$c,$b,$a,'SalesInvoice',120,0,'TRY',1,'Satış',$n,$n)", ("$id", Guid.NewGuid().ToString()), ("$c", company), ("$b", branch), ("$a", account), ("$n", now));
        db.Execute("INSERT INTO account_transactions(id,company_id,branch_id,account_id,transaction_type,debit,credit,currency_code,exchange_rate,description,transaction_at,created_at) VALUES($id,$c,$b,$a,'Receipt',0,20,'TRY',1,'Tahsilat',$n,$n)", ("$id", Guid.NewGuid().ToString()), ("$c", company), ("$b", branch), ("$a", account), ("$n", DateTime.UtcNow.AddSeconds(1).ToString("O")));
        db.Execute("INSERT INTO account_balances(company_id,account_id,debit,credit,balance,updated_at) VALUES($c,$a,120,20,100,$n)", ("$c", company), ("$a", account), ("$n", now));

        Assert.Single(service.Search(company, accountType: "Customer").Rows.Cast<System.Data.DataRow>());
        Assert.Single(service.Search(company, accountType: "Supplier").Rows.Cast<System.Data.DataRow>());
        var summary = service.GetDashboardSummary(company);
        Assert.Equal(100m, summary.CustomerReceivable);
        Assert.Equal(2, service.Statement(company, account).Rows.Count);
        Assert.Equal(100m, Convert.ToDecimal(service.Statement(company, account).Rows[1]["Bakiye"]));
        Assert.Equal("Limite Yakın", service.CreditRisk(company).Rows[0]["RiskDurumu"]);
        Assert.Equal(2, service.RecentTransactions(company, accountId: account).Rows.Count);
        service.SetActive(company, account, false);
        Assert.False(Convert.ToBoolean(service.Search(company, "CS01").Rows[0]["Aktif"]));
        Assert.Single(db.Query("SELECT id FROM audit_logs WHERE entity_id=$id AND action='AccountDeactivated'", ("$id", account)).Rows.Cast<System.Data.DataRow>());
    }
    [Fact]
    public void ShipmentQueueUsesCanonicalHeaderLineAndDeliveryModel()
    {
        var db = Create();
        var company = "00000000-0000-0000-0000-000000000001";
        var branch = db.Query("SELECT id FROM branches LIMIT 1").Rows[0][0].ToString()!;
        var warehouse = db.Query("SELECT id FROM warehouses LIMIT 1").Rows[0][0].ToString()!;
        var unit = db.Query("SELECT id FROM units LIMIT 1").Rows[0][0].ToString()!;
        var account = Guid.NewGuid().ToString(); var product = Guid.NewGuid().ToString(); var shipment = Guid.NewGuid().ToString(); var now = DateTime.UtcNow.ToString("O");
        db.Execute("INSERT INTO accounts(id,company_id,code,name,account_type,is_active,created_at,updated_at) VALUES($id,$c,'SVK01','Sevk Müşterisi','Customer',1,$n,$n)", ("$id", account), ("$c", company), ("$n", now));
        db.Execute("INSERT INTO products(id,company_id,code,name,base_unit_id,product_type,vat_rate,is_active,created_at,updated_at) VALUES($id,$c,'SP01','Sevk Ürünü',$u,'Stock',20,1,$n,$n)", ("$id", product), ("$c", company), ("$u", unit), ("$n", now));
        db.Execute("INSERT INTO shipment_orders(id,company_id,branch_id,warehouse_id,account_id,shipment_no,order_date,planned_shipment_date,status,vehicle_plate,created_at,updated_at) VALUES($id,$c,$b,$w,$a,'SVK-1',$n,$n,'Planned','34 R3 001',$n,$n)", ("$id", shipment), ("$c", company), ("$b", branch), ("$w", warehouse), ("$a", account), ("$n", now));
        db.Execute("INSERT INTO shipment_order_lines(id,shipment_order_id,product_id,planned_quantity,shipped_quantity,unit_code) VALUES($id,$s,$p,4,1,'ADET')", ("$id", Guid.NewGuid().ToString()), ("$s", shipment), ("$p", product));

        var service = new LocalShipmentService(db);
        Assert.Equal(1, service.GetSummary(company).Pending);
        var row = Assert.Single(service.SearchPending(company, "34 R3").Rows.Cast<System.Data.DataRow>());
        Assert.Equal(4m, Convert.ToDecimal(row["PlanlananMiktar"]));
        Assert.Equal("SVK-1", row["SevkNo"]);
        Assert.Equal(StoreDatabase.LatestSchemaVersion, db.SchemaVersion);
    }
    [Fact]
    public void PurchasingCreatesTotalsNumbersStateTransitionsAndAudit()
    {
        var db = Create(); var company = "00000000-0000-0000-0000-000000000001";
        var branch = db.Query("SELECT id FROM branches LIMIT 1").Rows[0][0].ToString()!; var warehouse = db.Query("SELECT id FROM warehouses LIMIT 1").Rows[0][0].ToString()!; var unit = db.Query("SELECT id FROM units LIMIT 1").Rows[0][0].ToString()!;
        var supplier = Guid.NewGuid().ToString(); var product = Guid.NewGuid().ToString(); var now = DateTime.UtcNow.ToString("O");
        db.Execute("INSERT INTO accounts(id,company_id,code,name,account_type,is_active,created_at,updated_at) VALUES($id,$c,'T01','Test Tedarikçi','Supplier',1,$n,$n)", ("$id", supplier), ("$c", company), ("$n", now));
        db.Execute("INSERT INTO products(id,company_id,code,name,base_unit_id,product_type,purchase_vat_rate,is_active,created_at,updated_at) VALUES($id,$c,'AL01','Alınacak Ürün',$u,'Stock',20,1,$n,$n)", ("$id", product), ("$c", company), ("$u", unit), ("$n", now));
        var service = new LocalPurchasingService(db);
        var id = service.Create(new(company, branch, warehouse, supplier, "Order", DateTime.Today, DateTime.Today.AddDays(5), "TRY", "Test", [new(product, unit, 2, 100, 10, 20)]), "test-user");
        Assert.Equal(216m, Convert.ToDecimal(service.Search(company, "Order").Rows[0]["GenelToplam"]));
        Assert.Equal(1, service.Summary(company).Draft);
        Assert.StartsWith("SS-", service.Approve(id, "test-user"));
        Assert.Equal("Approved", service.Search(company, "Order").Rows[0]["Durum"]);
        Assert.Throws<InvalidOperationException>(() => service.Approve(id, "test-user"));
        service.Cancel(id, "test-user");
        Assert.Equal("Cancelled", service.Search(company, "Order").Rows[0]["Durum"]);
        Assert.Equal(3, db.Query("SELECT id FROM audit_logs WHERE entity_id=$id", ("$id", id)).Rows.Count);
    }
    [Fact]
    public void PurchasingReceiptsAndInvoicePostingAreAtomic()
    {
        var db = Create(); var company = "00000000-0000-0000-0000-000000000001";
        var branch = db.Query("SELECT id FROM branches LIMIT 1").Rows[0][0].ToString()!; var warehouse = db.Query("SELECT id FROM warehouses LIMIT 1").Rows[0][0].ToString()!; var unit = db.Query("SELECT id FROM units LIMIT 1").Rows[0][0].ToString()!;
        var supplier = Guid.NewGuid().ToString(); var product = Guid.NewGuid().ToString(); var now = DateTime.UtcNow.ToString("O");
        db.Execute("INSERT INTO accounts(id,company_id,code,name,account_type,is_active,created_at,updated_at) VALUES($id,$c,'T02','Mal Kabul Tedarikçisi','Supplier',1,$n,$n)", ("$id", supplier), ("$c", company), ("$n", now));
        db.Execute("INSERT INTO products(id,company_id,code,name,base_unit_id,product_type,purchase_vat_rate,is_active,created_at,updated_at) VALUES($id,$c,'AL02','Teslim Ürünü',$u,'Stock',20,1,$n,$n)", ("$id", product), ("$c", company), ("$u", unit), ("$n", now));
        var service = new LocalPurchasingService(db);
        var order = service.Create(new(company, branch, warehouse, supplier, "Order", DateTime.Today, DateTime.Today.AddDays(3), "TRY", "Sipariş", [new(product, unit, 5, 10, 0, 20)]), "tester"); service.Approve(order, "tester");
        var orderLine = service.Lines(order).Rows[0]["Id"].ToString()!;
        service.Receive(order, [new(orderLine, 2)], "tester");
        Assert.Equal("PartiallyReceived", service.Search(company, "Order").Rows[0]["Durum"]);
        service.Receive(order, [new(orderLine, 3)], "tester");
        Assert.Equal("Closed", service.Search(company, "Order").Rows[0]["Durum"]);
        Assert.Equal(5m, Convert.ToDecimal(db.Query("SELECT quantity_on_hand FROM inventory_balances WHERE warehouse_id=$w AND product_id=$p", ("$w", warehouse), ("$p", product)).Rows[0][0]));

        var invoice = service.Create(new(company, branch, warehouse, supplier, "Invoice", DateTime.Today, null, "TRY", "Alış faturası", [new(product, unit, 2, 100, 0, 20)]), "tester"); service.Approve(invoice, "tester"); service.PostInvoice(invoice, "tester");
        Assert.Equal("Posted", service.Search(company, "Invoice").Rows[0]["Durum"]);
        Assert.Equal(7m, Convert.ToDecimal(db.Query("SELECT quantity_on_hand FROM inventory_balances WHERE warehouse_id=$w AND product_id=$p", ("$w", warehouse), ("$p", product)).Rows[0][0]));
        Assert.Equal(240m, Convert.ToDecimal(db.Query("SELECT credit FROM account_transactions WHERE document_id=$id", ("$id", invoice)).Rows[0][0]));
        Assert.Equal(-240m, Convert.ToDecimal(db.Query("SELECT balance FROM account_balances WHERE account_id=$id", ("$id", supplier)).Rows[0][0]));
        Assert.Throws<InvalidOperationException>(() => service.PostInvoice(invoice, "tester"));
    }
    [Fact]
    public void PurchaseInvoiceCreatedFromReceiptDoesNotDuplicateInventory()
    {
        var db = Create(); var company = "00000000-0000-0000-0000-000000000001";
        var branch = db.Query("SELECT id FROM branches LIMIT 1").Rows[0][0].ToString()!; var warehouse = db.Query("SELECT id FROM warehouses LIMIT 1").Rows[0][0].ToString()!; var unit = db.Query("SELECT id FROM units LIMIT 1").Rows[0][0].ToString()!;
        var supplier = Guid.NewGuid().ToString(); var product = Guid.NewGuid().ToString(); var now = DateTime.UtcNow.ToString("O");
        db.Execute("INSERT INTO accounts(id,company_id,code,name,account_type,is_active,created_at,updated_at) VALUES($id,$c,'T03','İrsaliye Tedarikçisi','Supplier',1,$n,$n)", ("$id", supplier), ("$c", company), ("$n", now));
        db.Execute("INSERT INTO products(id,company_id,code,name,base_unit_id,product_type,purchase_vat_rate,is_active,created_at,updated_at) VALUES($id,$c,'AL03','İrsaliyeli Ürün',$u,'Stock',20,1,$n,$n)", ("$id", product), ("$c", company), ("$u", unit), ("$n", now));
        var purchasing = new LocalPurchasingService(db); var order = purchasing.Create(new(company, branch, warehouse, supplier, "Order", DateTime.Today, DateTime.Today.AddDays(3), "TRY", "İrsaliye kaynağı", [new(product, unit, 2, 10, 0, 20)]), "tester"); purchasing.Approve(order, "tester");
        var orderLine = purchasing.Lines(order).Rows[0]["Id"].ToString()!; var receipts = new LocalPurchaseReceiptService(db); var receipt = receipts.CreateFromOrder(order, [new(orderLine, 2)], "tester"); receipts.Approve(receipt, "tester");
        Assert.Equal(2m, Convert.ToDecimal(db.Query("SELECT quantity_on_hand FROM inventory_balances WHERE warehouse_id=$w AND product_id=$p", ("$w", warehouse), ("$p", product)).Rows[0][0]));
        var invoice = receipts.CreateInvoiceFromReceipt(receipt, new(DateTime.Today, DateTime.Today.AddDays(30), "İrsaliyeden fatura"), "tester"); purchasing.Approve(invoice, "tester"); purchasing.PostInvoice(invoice, "tester");
        Assert.Equal(2m, Convert.ToDecimal(db.Query("SELECT quantity_on_hand FROM inventory_balances WHERE warehouse_id=$w AND product_id=$p", ("$w", warehouse), ("$p", product)).Rows[0][0]));
        Assert.Equal(24m, Convert.ToDecimal(db.Query("SELECT credit FROM account_transactions WHERE document_id=$id", ("$id", invoice)).Rows[0][0]));
        Assert.Equal(1, db.Query("SELECT id FROM document_relations WHERE source_document_id=$r AND target_document_id=$i AND relation_type='ReceiptToInvoice'", ("$r", receipt), ("$i", invoice)).Rows.Count);
    }
    [Fact]
    public async Task SalesDespatchRequiresPostedInvoiceAndDoesNotDuplicateStock()
    {
        var db = Create(); var company = "00000000-0000-0000-0000-000000000001";
        var branch = db.Query("SELECT id FROM branches LIMIT 1").Rows[0][0].ToString()!; var warehouse = db.Query("SELECT id FROM warehouses LIMIT 1").Rows[0][0].ToString()!; var unit = db.Query("SELECT id FROM units LIMIT 1").Rows[0][0].ToString()!;
        var customer = Guid.NewGuid().ToString(); var product = Guid.NewGuid().ToString(); var now = DateTime.UtcNow.ToString("O");
        db.Execute("INSERT INTO accounts(id,company_id,code,name,account_type,is_active,created_at,updated_at) VALUES($id,$c,'M03','Sevkiyat Müşterisi','Customer',1,$n,$n)", ("$id", customer), ("$c", company), ("$n", now));
        db.Execute("INSERT INTO products(id,company_id,code,name,base_unit_id,product_type,vat_rate,is_active,created_at,updated_at) VALUES($id,$c,'SV03','Sevk Ürünü',$u,'Stock',20,1,$n,$n)", ("$id", product), ("$c", company), ("$u", unit), ("$n", now));
        var inventory = new LocalInventoryService(db); await inventory.PostOpeningBalanceAsync(new(company, branch, warehouse, product, null, 5, 10, DateTime.Today));
        var sales = new LocalSalesService(db, new ElectronicDocumentRoutingService(db), new LocalElectronicDocumentService(db)); var draft = sales.CreateDraft(new("", company, branch, warehouse, customer, DateTime.Today, "Sevkiyat", [new(product, null, unit, null, 2, 1, 100, 0, 20)]));
        var despatches = new LocalDespatchService(db); Assert.Throws<InvalidOperationException>(() => despatches.CreateFromSalesInvoice(draft, "tester"));
        sales.Post(draft, "tester"); var despatch = despatches.CreateFromSalesInvoice(draft, "tester"); var repeated = despatches.CreateFromSalesInvoice(draft, "tester");
        Assert.Equal(despatch, repeated); Assert.Equal(3m, Convert.ToDecimal(db.Query("SELECT quantity_on_hand FROM inventory_balances WHERE warehouse_id=$w AND product_id=$p", ("$w", warehouse), ("$p", product)).Rows[0][0]));
        Assert.Equal(2m, Convert.ToDecimal(db.Query("SELECT SUM(quantity) FROM despatch_document_lines WHERE despatch_document_id=$id", ("$id", despatch)).Rows[0][0]));
        despatches.Transition(despatch, "Planned", "tester"); despatches.Transition(despatch, "ReadyForShipment", "tester"); despatches.Transition(despatch, "InTransit", "tester"); despatches.Transition(despatch, "Delivered", "tester");
        Assert.Equal("Delivered", db.Query("SELECT status FROM despatch_documents WHERE id=$id", ("$id", despatch)).Rows[0][0]);
    }
    [Fact]
    public void ProductUnitsEnforceSingleBaseUnitAndPositiveFactor()
    {
        var db = Create(); var service = new LocalProductService(db); var company = "00000000-0000-0000-0000-000000000001"; var unit = db.Query("SELECT id FROM units LIMIT 1").Rows[0][0].ToString()!;
        db.Execute("INSERT INTO units(id,company_id,code,name,decimal_places,is_active) VALUES('u-koli',$c,'KOLI','Koli',0,1)", ("$c", company));
        var koli = "u-koli";
        Assert.Throws<ArgumentException>(() => service.Save(new ProductAggregateEdit("", company, "PU01", "Birim testi", "", "", unit, "Stock", 20, true, [], [], Units: [new("", unit, 1, 0, true)])));
        Assert.Throws<ArgumentException>(() => service.Save(new ProductAggregateEdit("", company, "PU02", "Birim testi", "", "", unit, "Stock", 20, true, [], [], Units: [new("", unit, 1, 1, true), new("", koli, 2, 12, true)])));
        Assert.Throws<ArgumentException>(() => service.Save(new ProductAggregateEdit("", company, "PU03", "Birim testi", "", "", unit, "Stock", 20, true, [], [], Units: [new("", unit, 1, 1, true), new("", unit, 2, 1, false)])));
        service.Save(new ProductAggregateEdit("", company, "PU04", "Birim testi", "", "", unit, "Stock", 20, true, [], [], Units: [new("", unit, 1, 1, true), new("", koli, 2, 12, false)]));
        var id = db.Query("SELECT id FROM products WHERE code='PU04'").Rows[0][0].ToString()!;
        Assert.Equal(2, service.GetDetail(id, company)!.Product.Units.Count);
    }
    [Fact]
    public void ProductSuppliersPreserveCodeAndRejectDuplicateSupplier()
    {
        var db = Create(); var service = new LocalProductService(db); var company = "00000000-0000-0000-0000-000000000001"; var unit = db.Query("SELECT id FROM units LIMIT 1").Rows[0][0].ToString()!; var now = DateTime.UtcNow.ToString("O");
        var supplier = Guid.NewGuid().ToString(); db.Execute("INSERT INTO accounts(id,company_id,code,name,account_type,is_active,created_at,updated_at) VALUES($id,$c,'TED01','Tedarikçi','Supplier',1,$n,$n)", ("$id", supplier), ("$c", company), ("$n", now));
        Assert.Throws<ArgumentException>(() => service.Save(new ProductAggregateEdit("", company, "PS01", "Tedarik testi", "", "", unit, "Stock", 20, true, [], [], Suppliers: [new("", supplier, "SUP-1", true, 5, 1), new("", supplier, "SUP-2", true, 3, 0)])));
        service.Save(new ProductAggregateEdit("", company, "PS02", "Tedarik testi", "", "", unit, "Stock", 20, true, [], [], Suppliers: [new("", supplier, "SUP-CODE", true, 5, 1, 1)]));
        var id = db.Query("SELECT id FROM products WHERE code='PS02'").Rows[0][0].ToString()!;
        var saved = service.GetDetail(id, company)!.Product.Suppliers.Single();
        Assert.Equal("SUP-CODE", saved.SupplierProductCode); Assert.Equal(5, saved.LeadTimeDays); Assert.Equal(1, saved.ExtraLeadTimeDays);
    }
    [Fact]
    public void InventoryPolicyRejectsMaxLessThanMinAndPersistsNewFields()
    {
        var db = Create(); var service = new LocalProductService(db); var company = "00000000-0000-0000-0000-000000000001"; var unit = db.Query("SELECT id FROM units LIMIT 1").Rows[0][0].ToString()!;
        Assert.Throws<ArgumentException>(() => service.Save(new ProductAggregateEdit("", company, "IP01", "Politika testi", "", "", unit, "Stock", 20, true, [], [], MinimumStock: 10, MaximumStock: 5)));
        Assert.Throws<ArgumentException>(() => service.Save(new ProductAggregateEdit("", company, "IP02", "Politika testi", "", "", unit, "Stock", 20, true, [], [], Policy: new(DeliveryLeadTimeDays: 10, MaximumDeliveryLeadTimeDays: 3))));
        service.Save(new ProductAggregateEdit("", company, "IP03", "Politika testi", "", "", unit, "Stock", 20, true, [], [], MinimumStock: 2, MaximumStock: 20, Policy: new(DeliveryLeadTimeDays: 3, MaximumDeliveryLeadTimeDays: 7, LotTrackingType: "Lot", PieceCount: 4, ShipmentLocationType: "Merkez Depo")));
        var id = db.Query("SELECT id FROM products WHERE code='IP03'").Rows[0][0].ToString()!;
        var policy = service.GetDetail(id, company)!.Product.Policy;
        Assert.Equal(3, policy.DeliveryLeadTimeDays); Assert.Equal(7, policy.MaximumDeliveryLeadTimeDays); Assert.Equal("Lot", policy.LotTrackingType); Assert.Equal(4, policy.PieceCount); Assert.Equal("Merkez Depo", policy.ShipmentLocationType);
    }
    [Fact]
    public void ProductAggregateSaveRollsBackUnitsAndSuppliersOnChildFailure()
    {
        var db = Create(); var service = new LocalProductService(db); var company = "00000000-0000-0000-0000-000000000001"; var unit = db.Query("SELECT id FROM units LIMIT 1").Rows[0][0].ToString()!; var now = DateTime.UtcNow.ToString("O");
        var supplier = Guid.NewGuid().ToString(); db.Execute("INSERT INTO accounts(id,company_id,code,name,account_type,is_active,created_at,updated_at) VALUES($id,$c,'TED02','Tedarikçi 2','Supplier',1,$n,$n)", ("$id", supplier), ("$c", company), ("$n", now));
        Assert.Throws<ArgumentException>(() => service.Save(new ProductAggregateEdit("", company, "RB01", "Geri alma testi", "", "", unit, "Stock", 20, true, [], [new("", "", "", Barcode: "9999", UnitId: unit)], Suppliers: [new("", supplier, "OK")], Units: [new("", unit, 1, 0, true)])));
        Assert.Empty(db.Query("SELECT id FROM products WHERE code='RB01'").Rows);
        Assert.Empty(db.Query("SELECT id FROM product_suppliers WHERE supplier_account_id=$s", ("$s", supplier)).Rows);
    }
    [Fact]
    public void ProductListSupportsCompanyFiltersPagingAndStockColumns()
    {
        var db = Create(); var service = new LocalProductService(db); var company = "00000000-0000-0000-0000-000000000001"; var unit = db.Query("SELECT id FROM units LIMIT 1").Rows[0][0].ToString()!;
        service.Save(new ProductAggregateEdit("", company, "LST01", "Liste Ürünü", "", "", unit, "Stock", 20, true, [], [new("", "", "", Barcode: "00000042", UnitId: unit, IsPrimary: true)]));
        var result = service.SearchPage(new ProductListQuery(company, Barcode: "00000042", Page: 1, PageSize: 1));
        Assert.Equal(1, result.TotalCount); Assert.Single(result.Rows.Rows); Assert.Equal("00000042", result.Rows.Rows[0]["BirincilBarkod"]);
        Assert.Equal(1, service.SearchPage(new ProductListQuery(company, Name: "Liste")).TotalCount);
    }
    [Fact]
    public void ProductCopyDoesNotDuplicateBarcodeAndPassivationIsAudited()
    {
        var db = Create(); var service = new LocalProductService(db); var company = "00000000-0000-0000-0000-000000000001"; var unit = db.Query("SELECT id FROM units LIMIT 1").Rows[0][0].ToString()!;
        service.Save(new ProductAggregateEdit("", company, "CP01", "Kopyalanacak", "", "", unit, "Stock", 20, true, [], [new("", "", "", Barcode: "CP-001", UnitId: unit)]));
        var sourceId = db.Query("SELECT id FROM products WHERE code='CP01'").Rows[0][0].ToString()!; var source = service.GetDetail(sourceId, company)!.Product;
        var copyId = service.Copy(source, "CP02", "Kopya"); Assert.NotEqual(sourceId, copyId); Assert.Empty(db.Query("SELECT id FROM product_barcodes WHERE product_id=$p", ("$p", copyId)).Rows);
        service.SetActive(copyId, company, false); Assert.False(Convert.ToBoolean(db.Query("SELECT is_active FROM products WHERE id=$p", ("$p", copyId)).Rows[0][0]));
        Assert.Single(db.Query("SELECT id FROM audit_logs WHERE entity_id=$p AND action='DEACTIVATE'", ("$p", copyId)).Rows);
    }
    [Fact]
    public void BarcodePrintDataUsesOnlyActiveCompanyScopedBarcodes()
    {
        var db = Create(); var service = new LocalProductService(db); var company = "00000000-0000-0000-0000-000000000001"; var unit = db.Query("SELECT id FROM units LIMIT 1").Rows[0][0].ToString()!;
        service.Save(new ProductAggregateEdit("", company, "PRN01", "Etiket Ürünü", "", "", unit, "Stock", 20, true, [], [new("", "", "", Barcode: "PRN-001", UnitId: unit, IsPrimary: true), new("", "", "", Barcode: "PRN-002", UnitId: unit, IsActive: false)]));
        var id = db.Query("SELECT id FROM products WHERE code='PRN01'").Rows[0][0].ToString()!;
        var labels = new LocalBarcodePrintService(db).GetLabels(id, company);
        Assert.Single(labels); Assert.Equal("PRN-001", labels[0].Barcode); Assert.True(labels[0].IsPrimary);
    }
    public void Dispose() { SqliteConnection.ClearAllPools(); if (Directory.Exists(_folder)) Directory.Delete(_folder, true); }
}
