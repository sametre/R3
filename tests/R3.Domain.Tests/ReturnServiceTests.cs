using R3.Infrastructure;

namespace R3.Domain.Tests;

/// <summary>Satış / Satınalma iadeleri (LocalReturnService) against real posted invoices.</summary>
public sealed class ReturnServiceTests : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), "R3-tests-" + Guid.NewGuid());
    private const string Company = "00000000-0000-0000-0000-000000000001";

    private sealed record World(StoreDatabase Db, LocalInventoryService Inventory, LocalReturnService Returns, string Branch, string Warehouse, string Unit, string Product);

    private World Create(decimal opening = 10)
    {
        var db = new StoreDatabase(Path.Combine(_folder, "test.db"));
        var branch = db.Query("SELECT id FROM branches LIMIT 1").Rows[0][0].ToString()!;
        var warehouse = db.Query("SELECT id FROM warehouses LIMIT 1").Rows[0][0].ToString()!;
        var unit = db.Query("SELECT id FROM units LIMIT 1").Rows[0][0].ToString()!;
        var product = Guid.NewGuid().ToString(); var now = DateTime.UtcNow.ToString("O");
        db.Execute("INSERT INTO products(id,company_id,code,name,base_unit_id,product_type,vat_rate,is_active,created_at,updated_at) VALUES($id,$c,'P1','Ürün',$u,'Stock',20,1,$n,$n)",
            ("$id", product), ("$c", Company), ("$u", unit), ("$n", now));
        var inventory = new LocalInventoryService(db);
        if (opening > 0) inventory.PostOpeningBalanceAsync(new(Company, branch, warehouse, product, null, opening, 5, DateTime.UtcNow)).GetAwaiter().GetResult();
        return new World(db, inventory, new LocalReturnService(db), branch, warehouse, unit, product);
    }

    private static string Account(StoreDatabase db, string code, string type)
    {
        var id = Guid.NewGuid().ToString();
        new LocalAccountService(db).Save(new AccountAggregateEdit(new AccountEdit(id, Company, code, $"Cari {code}", type), new AccountTaxProfileEdit(), new AccountEInvoiceProfileEdit(),
            type == "Customer" ? new CustomerProfileEdit() : null, type == "Supplier" ? new SupplierProfileEdit() : null));
        return id;
    }

    private static decimal Balance(StoreDatabase db, string account) => Convert.ToDecimal(db.Query("SELECT COALESCE(balance,0) FROM account_balances WHERE account_id=$a", ("$a", account)).Rows[0][0]);

    private (string Invoice, string Line, string Customer) PostedSale(World w, decimal quantity = 4)
    {
        var customer = Account(w.Db, "M1", "Customer");
        var sales = new LocalSalesService(w.Db, new ElectronicDocumentRoutingService(w.Db), new LocalElectronicDocumentService(w.Db));
        var invoice = sales.CreateDraft(new("", Company, w.Branch, w.Warehouse, customer, DateTime.Today, "", [new(w.Product, null, w.Unit, null, quantity, 1, 100, 10, 20)]));
        sales.Post(invoice, "test");
        return (invoice, w.Db.Query("SELECT id FROM sales_document_lines WHERE sales_document_id=$d", ("$d", invoice)).Rows[0][0].ToString()!, customer);
    }

    [Fact]
    public async Task SalesReturnCreditsCustomerAndPutsStockBackAtInvoicePrice()
    {
        var w = Create(10); var (invoice, line, customer) = PostedSale(w);
        var before = Balance(w.Db, customer);   // 4 × 100 − %10 = 360 + %20 KDV = 432

        var result = w.Returns.Post(new ReturnDraft(ReturnDirection.Sales, Company, invoice, DateTime.Today, "Kusurlu", [new(line, 1)]), "test");
        Assert.Equal(108m, result.GrandTotal);   // 100 − 10 = 90 + 18 KDV
        Assert.StartsWith("SIA", result.DocumentNo);
        Assert.Equal(before - 108m, Balance(w.Db, customer));
        Assert.Equal(7m, (await w.Inventory.GetBalanceAsync(w.Warehouse, w.Product, null))!.QuantityOnHand);   // 10 − 4 + 1
        Assert.Equal(3m, (decimal)w.Returns.SourceLines(ReturnDirection.Sales, invoice).Rows[0]["Kalan"]);
    }

    [Fact]
    public void CannotReturnMoreThanInvoicedMinusAlreadyReturned()
    {
        var w = Create(10); var (invoice, line, _) = PostedSale(w, 4);
        w.Returns.Post(new ReturnDraft(ReturnDirection.Sales, Company, invoice, DateTime.Today, "", [new(line, 3)]), "test");
        Assert.Throws<InvalidOperationException>(() => w.Returns.Post(new ReturnDraft(ReturnDirection.Sales, Company, invoice, DateTime.Today, "", [new(line, 2)]), "test"));
        w.Returns.Post(new ReturnDraft(ReturnDirection.Sales, Company, invoice, DateTime.Today, "", [new(line, 1)]), "test");
        Assert.Equal(2, w.Db.Query("SELECT COUNT(*) FROM return_documents").Rows[0][0] is long n ? (int)n : 0);
        Assert.Throws<ArgumentException>(() => w.Returns.Post(new ReturnDraft(ReturnDirection.Sales, Company, invoice, DateTime.Today, "", [new("other-line", 1)]), "test"));
    }

    [Fact]
    public async Task CancellingAReturnReversesMoneyAndStockAndFreesTheQuantityAgain()
    {
        var w = Create(10); var (invoice, line, customer) = PostedSale(w, 4);
        var before = Balance(w.Db, customer);
        var ret = w.Returns.Post(new ReturnDraft(ReturnDirection.Sales, Company, invoice, DateTime.Today, "", [new(line, 4)]), "test");
        Assert.Throws<ArgumentException>(() => w.Returns.Cancel(ret.ReturnId, "", "test"));
        w.Returns.Cancel(ret.ReturnId, "Yanlış iade", "test");

        Assert.Equal(before, Balance(w.Db, customer));
        Assert.Equal(6m, (await w.Inventory.GetBalanceAsync(w.Warehouse, w.Product, null))!.QuantityOnHand);
        Assert.Equal(4m, (decimal)w.Returns.SourceLines(ReturnDirection.Sales, invoice).Rows[0]["Kalan"]);
        Assert.Throws<InvalidOperationException>(() => w.Returns.Cancel(ret.ReturnId, "tekrar", "test"));
        Assert.Equal("İptal", w.Returns.Search(Company, ReturnDirection.Sales).Rows[0]["Durum"]);
    }

    [Fact]
    public async Task PurchaseReturnDebitsSupplierTakesStockOutAndRespectsNegativeStockPolicy()
    {
        var w = Create(0);
        var supplier = Account(w.Db, "T1", "Supplier");
        var purchasing = new LocalPurchasingService(w.Db);
        var invoice = purchasing.Create(new(Company, w.Branch, w.Warehouse, supplier, "Invoice", DateTime.Today, null, "TRY", "Alış", [new(w.Product, w.Unit, 5, 50, 0, 20)]), "test");
        purchasing.Approve(invoice, "test"); purchasing.PostInvoice(invoice, "test");
        var line = w.Db.Query("SELECT id FROM purchase_document_lines WHERE purchase_document_id=$d", ("$d", invoice)).Rows[0][0].ToString()!;
        var before = Balance(w.Db, supplier);   // −300 (we owe)

        await w.Inventory.PostManualOutAsync(new(Company, w.Branch, w.Warehouse, w.Product, null, 4, null, DateTime.UtcNow));   // only 1 left
        Assert.Throws<InvalidOperationException>(() => w.Returns.Post(new ReturnDraft(ReturnDirection.Purchase, Company, invoice, DateTime.Today, "", [new(line, 2)]), "test"));

        var result = w.Returns.Post(new ReturnDraft(ReturnDirection.Purchase, Company, invoice, DateTime.Today, "", [new(line, 1)]), "test");
        Assert.Equal(60m, result.GrandTotal);
        Assert.StartsWith("AIA", result.DocumentNo);
        Assert.Equal(before + 60m, Balance(w.Db, supplier));
        Assert.Equal(0m, (await w.Inventory.GetBalanceAsync(w.Warehouse, w.Product, null))!.QuantityOnHand);
        Assert.Equal("PurchaseReturn", w.Db.Query("SELECT transaction_type FROM inventory_transactions ORDER BY created_at DESC, rowid DESC LIMIT 1").Rows[0][0]);
    }

    [Fact]
    public void DraftInvoicesAndEmptyReturnsAreRejected()
    {
        var w = Create(10);
        var customer = Account(w.Db, "M1", "Customer");
        var sales = new LocalSalesService(w.Db, new ElectronicDocumentRoutingService(w.Db), new LocalElectronicDocumentService(w.Db));
        var draft = sales.CreateDraft(new("", Company, w.Branch, w.Warehouse, customer, DateTime.Today, "", [new(w.Product, null, w.Unit, null, 1, 1, 100, 0, 20)]));
        var line = w.Db.Query("SELECT id FROM sales_document_lines WHERE sales_document_id=$d", ("$d", draft)).Rows[0][0].ToString()!;
        Assert.Throws<InvalidOperationException>(() => w.Returns.Post(new ReturnDraft(ReturnDirection.Sales, Company, draft, DateTime.Today, "", [new(line, 1)]), "test"));
        Assert.Throws<ArgumentException>(() => w.Returns.Post(new ReturnDraft(ReturnDirection.Sales, Company, draft, DateTime.Today, "", []), "test"));
    }

    public void Dispose()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        try { Directory.Delete(_folder, true); } catch (IOException) { }
    }
}
