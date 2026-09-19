using System.Data;
using Microsoft.Data.Sqlite;
using R3.Infrastructure;

namespace R3.Domain.Tests;

public sealed class PostingEngineTests : IDisposable
{
    private readonly string _folder = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "R3-tests-" + Guid.NewGuid());
    private const string Company = "00000000-0000-0000-0000-000000000001";
    private StoreDatabase Create() => new(System.IO.Path.Combine(_folder, "test.db"));

    private static (LocalSalesService Sales, LocalInventoryService Inventory, LocalElectronicDocumentService Documents, string Branch, string Warehouse, string Unit) Setup(StoreDatabase db)
    {
        var routing = new ElectronicDocumentRoutingService(db);
        var documents = new LocalElectronicDocumentService(db);
        var sales = new LocalSalesService(db, routing, documents);
        var inventory = new LocalInventoryService(db);
        var branch = db.Query("SELECT id FROM branches LIMIT 1").Rows[0][0].ToString()!;
        var warehouse = db.Query("SELECT id FROM warehouses LIMIT 1").Rows[0][0].ToString()!;
        var unit = db.Query("SELECT id FROM units LIMIT 1").Rows[0][0].ToString()!;
        return (sales, inventory, documents, branch, warehouse, unit);
    }

    private static string SeedAccount(StoreDatabase db, string code, bool eInvoiceRegistered = false)
    {
        var account = new LocalAccountService(db);
        var id = Guid.NewGuid().ToString();
        account.Save(new AccountAggregateEdit(new AccountEdit(id, Company, code, $"Cari {code}", "Customer"), new AccountTaxProfileEdit(),
            eInvoiceRegistered ? new AccountEInvoiceProfileEdit(IsEInvoiceEnabled: true, EInvoiceAlias: "urn:mail:test@efatura.gov.tr") : new AccountEInvoiceProfileEdit(),
            new CustomerProfileEdit(), null));
        return id;
    }

    private static string SeedStockProduct(StoreDatabase db, string code, string unit, decimal openingQuantity, LocalInventoryService inventory, string branch, string warehouse)
    {
        var id = Guid.NewGuid().ToString(); var now = DateTime.UtcNow.ToString("O");
        db.Execute("INSERT INTO products(id,company_id,code,name,base_unit_id,product_type,vat_rate,is_active,created_at,updated_at) VALUES($id,$c,$code,$code,$u,'Stock',20,1,$n,$n)",
            ("$id", id), ("$c", Company), ("$code", code), ("$u", unit), ("$n", now));
        inventory.PostOpeningBalanceAsync(new(Company, branch, warehouse, id, null, openingQuantity, null, DateTime.UtcNow)).GetAwaiter().GetResult();
        return id;
    }

    private static string SeedServiceProduct(StoreDatabase db, string code, string unit)
    {
        var id = Guid.NewGuid().ToString(); var now = DateTime.UtcNow.ToString("O");
        db.Execute("INSERT INTO products(id,company_id,code,name,base_unit_id,product_type,vat_rate,is_active,created_at,updated_at) VALUES($id,$c,$code,$code,$u,'Service',20,1,$n,$n)",
            ("$id", id), ("$c", Company), ("$code", code), ("$u", unit), ("$n", now));
        return id;
    }

    [Fact]
    public void PostingCreatesElectronicDocumentInReadyStatusWithinTheSamePostingTransaction()
    {
        var db = Create(); var (sales, inventory, documents, branch, warehouse, unit) = Setup(db);
        var account = SeedAccount(db, "C01"); var product = SeedStockProduct(db, "P01", unit, 10, inventory, branch, warehouse);
        var invoiceId = sales.CreateDraft(new("", Company, branch, warehouse, account, DateTime.Today, "", [new(product, null, unit, null, 3, 1, 100, 0, 20)]));

        var result = sales.Post(invoiceId, "test-user");

        Assert.Equal(invoiceId, result.InvoiceId);
        Assert.NotEmpty(result.DocumentNo);
        Assert.NotEmpty(result.ElectronicDocumentId);
        var doc = documents.Get(result.ElectronicDocumentId)!;
        Assert.Equal(ElectronicDocumentStatus.Ready, doc.Status);
        Assert.Equal("SalesInvoice", doc.SourceEntityType);
        Assert.Equal(invoiceId, doc.SourceEntityId);
        Assert.Equal(account, doc.AccountId);
        Assert.Equal(result.DocumentNo, doc.DocumentNumber);
        Assert.Contains("C01", doc.RecipientSnapshotJson); // account snapshot captured, not left empty
        Assert.Single(db.Query("SELECT id FROM electronic_documents WHERE source_entity_id=$id", ("$id", invoiceId)).Rows.Cast<DataRow>());
        // No outbox/Queued yet - Generated (and therefore Queued) requires a real UBL payload, which no generator produces yet.
        Assert.Empty(db.Query("SELECT id FROM electronic_document_outbox WHERE electronic_document_id=$id", ("$id", result.ElectronicDocumentId)).Rows);
    }

    [Fact]
    public void RoutingReflectsAccountEInvoiceRegistrationAtPostingTime()
    {
        var db = Create(); var (sales, inventory, documents, branch, warehouse, unit) = Setup(db);
        var registered = SeedAccount(db, "C02", eInvoiceRegistered: true); var unregistered = SeedAccount(db, "C03");
        var product = SeedStockProduct(db, "P02", unit, 20, inventory, branch, warehouse);

        var invoice1 = sales.CreateDraft(new("", Company, branch, warehouse, registered, DateTime.Today, "", [new(product, null, unit, null, 1, 1, 50, 0, 20)]));
        var result1 = sales.Post(invoice1, "test-user");
        var invoice2 = sales.CreateDraft(new("", Company, branch, warehouse, unregistered, DateTime.Today, "", [new(product, null, unit, null, 1, 1, 50, 0, 20)]));
        var result2 = sales.Post(invoice2, "test-user");

        Assert.Equal(ElectronicDocumentType.EInvoice, result1.ElectronicDocumentType);
        Assert.Equal(ElectronicDocumentType.EInvoice, documents.Get(result1.ElectronicDocumentId)!.DocumentType);
        Assert.Equal(ElectronicDocumentType.EArchiveInvoice, result2.ElectronicDocumentType);
        Assert.Equal(ElectronicDocumentType.EArchiveInvoice, documents.Get(result2.ElectronicDocumentId)!.DocumentType);
    }

    [Fact]
    public async Task ServiceLineDoesNotMoveStockButAccountTransactionCoversTheWholeInvoiceTotal()
    {
        var db = Create(); var (sales, inventory, documents, branch, warehouse, unit) = Setup(db);
        var account = SeedAccount(db, "C04");
        var stockProduct = SeedStockProduct(db, "P04S", unit, 10, inventory, branch, warehouse);
        var serviceProduct = SeedServiceProduct(db, "P04H", unit);
        var invoiceId = sales.CreateDraft(new("", Company, branch, warehouse, account, DateTime.Today, "",
            [new(stockProduct, null, unit, null, 2, 1, 100, 0, 20), new(serviceProduct, null, unit, null, 1, 1, 300, 0, 20)]));

        var result = sales.Post(invoiceId, "test-user");

        // 2*100*1.2 + 1*300*1.2 = 240 + 360 = 600
        Assert.Equal(600m, Convert.ToDecimal(db.Query("SELECT debit FROM account_transactions WHERE document_id=$id", ("$id", invoiceId)).Rows[0][0]));
        Assert.Single(db.Query("SELECT id FROM inventory_transactions WHERE document_id=$id", ("$id", invoiceId)).Rows.Cast<DataRow>()); // only the stock line
        Assert.Equal(8m, (await inventory.GetBalanceAsync(warehouse, stockProduct, null))!.QuantityOnHand);
        Assert.Equal(ElectronicDocumentStatus.Ready, documents.Get(result.ElectronicDocumentId)!.Status);
    }

    [Fact]
    public async Task InsufficientStockRollsBackInvoiceAccountInventoryAndElectronicDocumentTogether()
    {
        var db = Create(); var (sales, inventory, documents, branch, warehouse, unit) = Setup(db);
        var account = SeedAccount(db, "C05"); var product = SeedStockProduct(db, "P05", unit, 2, inventory, branch, warehouse);
        var invoiceId = sales.CreateDraft(new("", Company, branch, warehouse, account, DateTime.Today, "", [new(product, null, unit, null, 5, 1, 100, 0, 20)]));

        Assert.Throws<InvalidOperationException>(() => sales.Post(invoiceId, "test-user"));

        Assert.Equal("Draft", db.Query("SELECT status FROM sales_documents WHERE id=$id", ("$id", invoiceId)).Rows[0][0]);
        Assert.Equal(2m, (await inventory.GetBalanceAsync(warehouse, product, null))!.QuantityOnHand);
        Assert.Empty(db.Query("SELECT id FROM account_transactions WHERE document_id=$id", ("$id", invoiceId)).Rows);
        Assert.Empty(db.Query("SELECT id FROM electronic_documents WHERE source_entity_id=$id", ("$id", invoiceId)).Rows);
        Assert.Empty(db.Query("SELECT id FROM audit_logs WHERE entity_id=$id AND action='SalesInvoicePosted'", ("$id", invoiceId)).Rows);
    }

    [Fact]
    public void WithinTransactionElectronicDocumentFailureRollsBackEverythingElseUncommittedInThatTransaction()
    {
        // Unit-level proof that a *WithinTransaction failure is safe to compose into any posting flow:
        // force a UUID collision (the one realistic failure CreateOrGetForSourceWithinTransaction can
        // raise) mid-transaction and confirm nothing from that transaction - including an otherwise-valid
        // first document - survives the rollback.
        var db = Create(); var documents = new LocalElectronicDocumentService(db);
        var branch = db.Query("SELECT id FROM branches LIMIT 1").Rows[0][0].ToString()!;
        var sharedUuid = Guid.NewGuid().ToString();

        using (var c = db.OpenConnection())
        using (var tx = c.BeginTransaction())
        {
            var draft1 = new ElectronicDocumentDraft(Company, branch, ElectronicDocumentType.EArchiveInvoice, ElectronicDocumentDirection.Outgoing,
                "SalesInvoice", Guid.NewGuid().ToString(), null, "SF-1", DateTime.Today, "TRY", 100m, "{}", "u", Uuid: sharedUuid);
            documents.CreateOrGetForSourceWithinTransaction(c, tx, draft1); // succeeds, but not committed yet

            var draft2 = draft1 with { SourceEntityId = Guid.NewGuid().ToString(), Uuid = sharedUuid }; // same UUID, different source -> must throw
            Assert.Throws<InvalidOperationException>(() => documents.CreateOrGetForSourceWithinTransaction(c, tx, draft2));
            tx.Rollback();
        }

        Assert.Empty(db.Query("SELECT id FROM electronic_documents WHERE uuid=$u", ("$u", sharedUuid)).Rows);
    }

    [Fact]
    public async Task ConcurrentDuplicatePostingOnlyOneAttemptSucceeds()
    {
        var db = Create(); var (sales, inventory, documents, branch, warehouse, unit) = Setup(db);
        var account = SeedAccount(db, "C06"); var product = SeedStockProduct(db, "P06", unit, 50, inventory, branch, warehouse);
        var invoiceId = sales.CreateDraft(new("", Company, branch, warehouse, account, DateTime.Today, "", [new(product, null, unit, null, 3, 1, 100, 0, 20)]));

        var attempts = Enumerable.Range(0, 2).Select(_ => Task.Run(() =>
        {
            try { sales.Post(invoiceId, "test-user"); return (Success: true, Error: (string?)null); }
            catch (Exception ex) { return (Success: false, Error: ex.Message); }
        })).ToArray();
        await Task.WhenAll(attempts);
        var results = attempts.Select(t => t.Result).ToList();

        Assert.Single(results, r => r.Success);
        Assert.Single(results, r => !r.Success);
        Assert.Equal("Posted", db.Query("SELECT status FROM sales_documents WHERE id=$id", ("$id", invoiceId)).Rows[0][0]);
        Assert.Single(db.Query("SELECT id FROM account_transactions WHERE document_id=$id", ("$id", invoiceId)).Rows.Cast<DataRow>());
        Assert.Single(db.Query("SELECT id FROM inventory_transactions WHERE document_id=$id", ("$id", invoiceId)).Rows.Cast<DataRow>());
        Assert.Single(db.Query("SELECT id FROM electronic_documents WHERE source_entity_id=$id", ("$id", invoiceId)).Rows.Cast<DataRow>());
    }

    public void Dispose() { SqliteConnection.ClearAllPools(); if (Directory.Exists(_folder)) Directory.Delete(_folder, true); }
}
