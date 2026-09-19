using System.IO;
using Microsoft.Extensions.Logging;
using R3.Desktop.Presentation;
using R3.Desktop.ViewModels;
using R3.Infrastructure;

namespace R3.Desktop.Tests;

/// <summary>
/// Phase 8 (spec §48): "Fatura Taslağı -> Faturayı Kes -> UBL Oluştur -> Gönder -> Durumu takip et"
/// end to end, asserting exactly the state-driven action availability the invoice detail screen's
/// buttons read off InvoiceDetailViewModel/EDocumentPresentation - never a UI-computed guess.
/// </summary>
public sealed class InvoiceDetailViewModelTests : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), "R3-desktop-tests-" + Guid.NewGuid());
    private const string Company = "00000000-0000-0000-0000-000000000001";
    private StoreDatabase Create() => new(Path.Combine(_folder, "test.db"));

    private static (string Branch, string Warehouse, string Unit) Setup(StoreDatabase db) => (
        db.Query("SELECT id FROM branches LIMIT 1").Rows[0][0].ToString()!,
        db.Query("SELECT id FROM warehouses LIMIT 1").Rows[0][0].ToString()!,
        db.Query("SELECT id FROM units LIMIT 1").Rows[0][0].ToString()!);

    private static string SeedAccount(StoreDatabase db, string code, bool eInvoiceRegistered = false)
    {
        var account = new LocalAccountService(db);
        var id = Guid.NewGuid().ToString();
        account.Save(new AccountAggregateEdit(new AccountEdit(id, Company, code, $"Cari {code}", "Customer", TaxNumber: "9876543210"), new AccountTaxProfileEdit(),
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

    // Required before UBL generation can succeed (UblInvoiceGenerator needs a real sender address) -
    // same seed shape as tests/R3.Domain.Tests/UblInvoiceGeneratorTests.cs.
    private static void SeedCompanyAddress(StoreDatabase db, LocalElectronicDocumentService documents) => documents.SaveCompanyProfile(new ElectronicDocumentCompanyProfileEdit(
        Company, "1234567890", "R3 Demo Ticaret A.Ş.", "urn:mail:r3@efatura.gov.tr", "", "", "Test", false, true, "Temel",
        "", "", "", "", "", "", "Örnek Sk. No:5", "İstanbul", "Şişli", "34394", "Türkiye"));

    private string CreateDraftInvoice(StoreDatabase db, string branch, string warehouse, string unit, string account, string product, decimal quantity = 2, decimal unitPrice = 1000)
    {
        var sales = new LocalSalesService(db, new ElectronicDocumentRoutingService(db), new LocalElectronicDocumentService(db));
        return sales.CreateDraft(new("", Company, branch, warehouse, account, DateTime.Today, "", [new(product, null, unit, null, quantity, 1, unitPrice, 0, 20)]));
    }

    [Fact]
    public void NewDraft_IsDraft_AndPostIsTheOnlyAvailableAction()
    {
        var db = Create(); var (branch, warehouse, unit) = Setup(db); var inventory = new LocalInventoryService(db);
        var account = SeedAccount(db, "C01"); var product = SeedStockProduct(db, "P01", unit, 10, inventory, branch, warehouse);
        var invoiceId = CreateDraftInvoice(db, branch, warehouse, unit, account, product);

        var vm = new InvoiceDetailViewModel(db, invoiceId, "test-user", new CapturingLogger<InvoiceDetailViewModel>()); vm.Load();

        Assert.True(vm.IsDraft);
        Assert.False(vm.IsPosted);
        Assert.True(vm.PostCommand.CanExecute(null));
        Assert.Null(vm.ElectronicDocumentId); // §7: the electronic document only exists once posted
    }

    [Fact]
    public async Task Post_OnSuccess_LocksFinancialFields_AndCreatesReadyElectronicDocument()
    {
        var db = Create(); var (branch, warehouse, unit) = Setup(db); var inventory = new LocalInventoryService(db);
        var account = SeedAccount(db, "C01"); var product = SeedStockProduct(db, "P01", unit, 10, inventory, branch, warehouse);
        var invoiceId = CreateDraftInvoice(db, branch, warehouse, unit, account, product);
        var vm = new InvoiceDetailViewModel(db, invoiceId, "test-user", new CapturingLogger<InvoiceDetailViewModel>()); vm.Load();

        await vm.PostCommand.ExecuteAsync(null);

        Assert.Null(vm.ErrorMessage);
        Assert.True(vm.IsPosted);
        Assert.False(vm.PostCommand.CanExecute(null)); // §48 "Posted invoice financial fields locked"
        Assert.Equal(ElectronicDocumentStatus.Ready, vm.EDocStatus);
        Assert.True(vm.GenerateCommand.CanExecute(null)); // §48 "Ready document enables Generate"
        Assert.False(vm.IsBusy);
    }

    [Fact]
    public async Task Post_OnInsufficientStock_SetsErrorMessage_LeavesInvoiceDraft_AndDoesNotLog()
    {
        var db = Create(); var (branch, warehouse, unit) = Setup(db); var inventory = new LocalInventoryService(db);
        var account = SeedAccount(db, "C01"); var product = SeedStockProduct(db, "P01", unit, 1, inventory, branch, warehouse); // only 1 on hand
        var invoiceId = CreateDraftInvoice(db, branch, warehouse, unit, account, product, quantity: 5);
        var logger = new CapturingLogger<InvoiceDetailViewModel>();
        var vm = new InvoiceDetailViewModel(db, invoiceId, "test-user", logger); vm.Load();

        await vm.PostCommand.ExecuteAsync(null);

        Assert.Equal("Yetersiz stok.", vm.ErrorMessage);
        Assert.True(vm.IsDraft); // never transitioned - not a partial post
        Assert.Empty(logger.Entries); // expected business rejection, not a bug (matches AccountLoggingTests convention)
    }

    [Fact]
    public async Task FullChain_PostThroughGenerateQueueSendAndQueryStatus_ReachesAcceptedAndDisablesSendRetry()
    {
        var db = Create(); var (branch, warehouse, unit) = Setup(db); var inventory = new LocalInventoryService(db);
        SeedCompanyAddress(db, new LocalElectronicDocumentService(db));
        var account = SeedAccount(db, "C01"); var product = SeedStockProduct(db, "P01", unit, 10, inventory, branch, warehouse);
        var invoiceId = CreateDraftInvoice(db, branch, warehouse, unit, account, product);
        var vm = new InvoiceDetailViewModel(db, invoiceId, "test-user", new CapturingLogger<InvoiceDetailViewModel>()); vm.Load();

        await vm.PostCommand.ExecuteAsync(null);
        Assert.Equal(ElectronicDocumentStatus.Ready, vm.EDocStatus);

        await vm.GenerateCommand.ExecuteAsync(null);
        Assert.Equal(ElectronicDocumentStatus.Generated, vm.EDocStatus);
        Assert.True(vm.QueueCommand.CanExecute(null)); // §48 "Generated enables Queue"
        Assert.NotNull(vm.XmlPayload);

        await vm.QueueCommand.ExecuteAsync(null);
        Assert.Equal(ElectronicDocumentStatus.Queued, vm.EDocStatus);
        Assert.False(vm.QueueCommand.CanExecute(null)); // §48 "Queued disables duplicate Queue"
        Assert.True(vm.SendCommand.CanExecute(null));

        await vm.SendCommand.ExecuteAsync(null); // dispatcher: Queued -> Sending -> Sent, chains a QueryStatus row
        Assert.Equal(ElectronicDocumentStatus.Sent, vm.EDocStatus);
        Assert.True(vm.QueryStatusCommand.CanExecute(null)); // §48 "Sent enables StatusQuery"
        Assert.False(vm.SendCommand.CanExecute(null));

        await vm.QueryStatusCommand.ExecuteAsync(null); // dispatcher: Sent -> Delivered -> Accepted (dev provider)
        Assert.Equal(ElectronicDocumentStatus.Accepted, vm.EDocStatus);
        Assert.False(vm.SendCommand.CanExecute(null)); // §48 "Accepted disables Send/Retry"
        Assert.False(vm.RetryCommand.CanExecute(null));
        Assert.False(vm.GenerateCommand.CanExecute(null));
        Assert.False(vm.QueueCommand.CanExecute(null));
        Assert.NotEmpty(vm.Events);
    }

    [Fact]
    public void FailedElectronicDocument_EnablesRetry()
    {
        var db = Create(); var (branch, warehouse, unit) = Setup(db); var inventory = new LocalInventoryService(db);
        var account = SeedAccount(db, "C01"); var product = SeedStockProduct(db, "P01", unit, 10, inventory, branch, warehouse);
        var invoiceId = CreateDraftInvoice(db, branch, warehouse, unit, account, product);
        var documents = new LocalElectronicDocumentService(db);
        var sales = new LocalSalesService(db, new ElectronicDocumentRoutingService(db), documents);
        var posted = sales.Post(invoiceId, "test-user");
        // Drive the electronic document to Failed directly (Sending is the only legal predecessor)
        // without a real provider round trip - isolates "does Failed enable Retry" from the dispatcher.
        documents.SavePayload(posted.ElectronicDocumentId, ElectronicDocumentPayloadType.UblXml, "<Invoice/>", "application/xml", false, "test-user");
        documents.Generate(posted.ElectronicDocumentId, "test-user");
        var outbox = new ElectronicDocumentOutboxService(db, documents);
        outbox.QueueForSendAsync(posted.ElectronicDocumentId, "test-user");
        documents.StartSending(posted.ElectronicDocumentId, "test-user");
        documents.Fail(posted.ElectronicDocumentId, "test-user", "ProviderTimeout", "Sağlayıcı zaman aşımına uğradı.");

        var vm = new InvoiceDetailViewModel(db, invoiceId, "test-user", new CapturingLogger<InvoiceDetailViewModel>()); vm.Load();

        Assert.Equal(ElectronicDocumentStatus.Failed, vm.EDocStatus);
        Assert.True(vm.RetryCommand.CanExecute(null)); // §48 "DeadLetter enables Retry with permission" (Failed is the state that shows the Retry action - §27)
        Assert.Equal("Sağlayıcı zaman aşımına uğradı.", vm.LastErrorMessage);
    }

    [Theory]
    [InlineData(true, ElectronicDocumentType.EInvoice)]
    [InlineData(false, ElectronicDocumentType.EArchiveInvoice)]
    public async Task Post_RoutesToEInvoiceOrEArchive_BasedOnAccountRegistration(bool registered, ElectronicDocumentType expected)
    {
        var db = Create(); var (branch, warehouse, unit) = Setup(db); var inventory = new LocalInventoryService(db);
        var account = SeedAccount(db, "C01", eInvoiceRegistered: registered); var product = SeedStockProduct(db, "P01", unit, 10, inventory, branch, warehouse);
        var invoiceId = CreateDraftInvoice(db, branch, warehouse, unit, account, product);
        var vm = new InvoiceDetailViewModel(db, invoiceId, "test-user", new CapturingLogger<InvoiceDetailViewModel>()); vm.Load();

        await vm.PostCommand.ExecuteAsync(null);

        Assert.Equal(expected, vm.EDocType); // §39 "E-Fatura / E-Arşiv ayrımı" - routing engine result, never user-chosen
    }

    public void Dispose()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        if (Directory.Exists(_folder)) Directory.Delete(_folder, recursive: true);
    }
}

/// <summary>
/// Presentation-mapping table (spec §6/§44/§54) - pure functions, no database, no WPF: the exact
/// answer a button's IsEnabled/Visibility reads is asserted directly against the backend enum.
/// </summary>
public sealed class EDocumentPresentationTests
{
    [Theory]
    [InlineData(ElectronicDocumentStatus.Ready, true, false, false, false, false, false)]
    [InlineData(ElectronicDocumentStatus.Generated, false, true, false, false, false, false)]
    [InlineData(ElectronicDocumentStatus.Queued, false, false, true, false, false, false)]
    [InlineData(ElectronicDocumentStatus.Sent, false, false, false, true, false, true)]
    [InlineData(ElectronicDocumentStatus.Accepted, false, false, false, false, false, true)]
    [InlineData(ElectronicDocumentStatus.Rejected, false, false, false, false, false, true)]
    [InlineData(ElectronicDocumentStatus.Failed, false, false, false, false, true, false)]
    public void ActionsFor_MatchesTheStateTable(ElectronicDocumentStatus status, bool canGenerate, bool canQueue, bool canSend, bool canQueryStatus, bool canRetry, bool canViewProviderResponse)
    {
        var actions = EDocumentPresentation.ActionsFor(status);
        Assert.Equal(canGenerate, actions.CanGenerate);
        Assert.Equal(canQueue, actions.CanQueue);
        Assert.Equal(canSend, actions.CanSend);
        Assert.Equal(canQueryStatus, actions.CanQueryStatus);
        Assert.Equal(canRetry, actions.CanRetry);
        Assert.Equal(canViewProviderResponse, actions.CanViewProviderResponse);
    }

    [Fact]
    public void Accepted_NeverOffersSendOrRetry()
    {
        var actions = EDocumentPresentation.ActionsFor(ElectronicDocumentStatus.Accepted);
        Assert.False(actions.CanSend);
        Assert.False(actions.CanRetry);
        Assert.False(actions.CanQueue);
        Assert.False(actions.CanGenerate);
    }

    [Theory]
    [InlineData("Draft", "Taslak")]
    [InlineData("Posted", "Kesildi")]
    public void SalesStatusLabel_NeverLeaksTheRawBackendString(string raw, string turkish) =>
        Assert.Equal(turkish, EDocumentPresentation.SalesStatusLabel(raw));

    [Theory]
    [InlineData(ElectronicDocumentType.EInvoice, "E-Fatura")]
    [InlineData(ElectronicDocumentType.EArchiveInvoice, "E-Arşiv Fatura")]
    public void TypeLabel_NeverLeaksTheRawBackendEnum(ElectronicDocumentType type, string turkish) =>
        Assert.Equal(turkish, EDocumentPresentation.TypeLabel(type));
}
