using System.Data;
using System.IO;
using R3.Application.Security;
using R3.Desktop.ContextActions;
using R3.Desktop.Presentation;
using R3.Desktop.ViewModels;
using R3.Infrastructure;

namespace R3.Desktop.Tests;

/// <summary>
/// Phase 9 (spec §42-44): context-menu action availability per e-document state, and permission
/// gating - both for InvoiceDetailViewModel's own commands (Phase 8) and for the shared
/// ElectronicDocumentContextActions factory the new Giden Belgeler/Hatalı Belgeler/Gönderim Kuyruğu
/// screens use. No screen decides an action's availability itself; everything here asks
/// EDocumentPresentation.ActionsFor or ContextActionEvaluator, exactly like the real screens do.
/// </summary>
public sealed class ElectronicDocumentOperationsUiTests : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), "R3-ops-ui-tests-" + Guid.NewGuid());
    private StoreDatabase Create() => new(Path.Combine(_folder, "test.db"));

    private sealed class FakePermissions(params string[] denied) : IPermissionService
    {
        public bool HasPermission(string permissionCode) => !denied.Contains(permissionCode);
        public bool HasAnyPermission(params string[] permissionCodes) => permissionCodes.Any(HasPermission);
        public bool HasAllPermissions(params string[] permissionCodes) => permissionCodes.All(HasPermission);
        public void Refresh() { }
    }

    private static DataRowView DocumentRow(string id, string status, string? accountId = "acc-1", string? sourceType = "SalesInvoice", string? sourceId = "src-1")
    {
        var table = new DataTable();
        foreach (var col in new[] { "Id", "EBelgeDurumu", "AccountId", "KaynakTipi", "KaynakId", "BelgeNo", "SonHata" }) table.Columns.Add(col, typeof(string));
        var row = table.NewRow();
        row["Id"] = id; row["EBelgeDurumu"] = status; row["AccountId"] = accountId ?? ""; row["KaynakTipi"] = sourceType ?? ""; row["KaynakId"] = sourceId ?? ""; row["BelgeNo"] = "SF-001"; row["SonHata"] = "";
        table.Rows.Add(row);
        return table.DefaultView[0];
    }

    // --- §42: context menu action availability per status (mirrors InvoiceDetailView's E-Belge tab table) ---

    [Theory]
    [InlineData("Generated", "edoc.xml", true)]
    [InlineData("Generated", "edoc.query_status", false)]
    [InlineData("Queued", "edoc.query_status", false)]
    [InlineData("Sent", "edoc.query_status", true)]
    [InlineData("Sent", "edoc.provider_response", true)]
    [InlineData("Accepted", "edoc.retry", false)]
    [InlineData("Accepted", "edoc.provider_response", true)]
    [InlineData("Failed", "edoc.retry", true)]
    [InlineData("Failed", "edoc.query_status", false)]
    public void ForDocument_ActionVisibility_MatchesStatus(string status, string actionId, bool expectedVisible)
    {
        var db = Create();
        var actions = ElectronicDocumentContextActions.ForDocument(db, "test-user", _ => { }, (_, _) => { }, _ => { }, _ => { });
        var action = actions.Single(a => a.Id == actionId);
        var row = DocumentRow("doc-1", status);
        var availability = ContextActionEvaluator.Evaluate(action, row, 1, null);
        Assert.Equal(expectedVisible, availability.Visible);
    }

    [Fact]
    public void ForDocument_SourceInvoiceAction_HiddenWhenNoSourceRecorded()
    {
        var db = Create();
        var actions = ElectronicDocumentContextActions.ForDocument(db, "test-user", _ => { }, (_, _) => { }, _ => { }, _ => { });
        var openSource = actions.Single(a => a.Id == "edoc.source");
        var withSource = ContextActionEvaluator.Evaluate(openSource, DocumentRow("doc-1", "Generated"), 1, null);
        var withoutSource = ContextActionEvaluator.Evaluate(openSource, DocumentRow("doc-1", "Generated", sourceId: null), 1, null);
        Assert.True(withSource.Visible);
        Assert.False(withoutSource.Visible);
    }

    // --- §44: permission tests ---

    [Fact]
    public void ForDocument_NoPayloadViewPermission_HidesXmlAction()
    {
        var db = Create();
        var actions = ElectronicDocumentContextActions.ForDocument(db, "test-user", _ => { }, (_, _) => { }, _ => { }, _ => { });
        var xmlAction = actions.Single(a => a.Id == "edoc.xml");
        var row = DocumentRow("doc-1", "Generated");
        var allowed = ContextActionEvaluator.Evaluate(xmlAction, row, 1, new FakePermissions());
        var denied = ContextActionEvaluator.Evaluate(xmlAction, row, 1, new FakePermissions("edocuments.payload.view"));
        Assert.True(allowed.Visible);
        Assert.False(denied.Visible);
    }

    [Fact]
    public void ForDocument_NoProviderResponsePermission_HidesProviderResponseAction()
    {
        var db = Create();
        var actions = ElectronicDocumentContextActions.ForDocument(db, "test-user", _ => { }, (_, _) => { }, _ => { }, _ => { });
        var action = actions.Single(a => a.Id == "edoc.provider_response");
        var row = DocumentRow("doc-1", "Sent");
        Assert.True(ContextActionEvaluator.Evaluate(action, row, 1, new FakePermissions()).Visible);
        Assert.False(ContextActionEvaluator.Evaluate(action, row, 1, new FakePermissions("edocuments.provider_response.view")).Visible);
    }

    [Fact]
    public void ForDocument_NoRetryPermission_HidesRetryAction()
    {
        var db = Create();
        var actions = ElectronicDocumentContextActions.ForDocument(db, "test-user", _ => { }, (_, _) => { }, _ => { }, _ => { });
        var action = actions.Single(a => a.Id == "edoc.retry");
        var row = DocumentRow("doc-1", "Failed");
        Assert.True(ContextActionEvaluator.Evaluate(action, row, 1, new FakePermissions()).Visible);
        Assert.False(ContextActionEvaluator.Evaluate(action, row, 1, new FakePermissions("edocuments.retry")).Visible);
    }

    [Fact]
    public void ForDocument_NoOutboxViewPermission_HidesQueueRecordAction()
    {
        var db = Create();
        var actions = ElectronicDocumentContextActions.ForDocument(db, "test-user", _ => { }, (_, _) => { }, _ => { }, _ => { });
        var action = actions.Single(a => a.Id == "edoc.queue_record");
        var row = DocumentRow("doc-1", "Queued");
        Assert.True(ContextActionEvaluator.Evaluate(action, row, 1, new FakePermissions()).Visible);
        Assert.False(ContextActionEvaluator.Evaluate(action, row, 1, new FakePermissions("edocuments.outbox.view")).Visible);
    }

    // --- §16-19/§42: Gönderim Kuyruğu context actions ---

    private static DataRowView OutboxRow(string outboxStatus, string documentId = "doc-1") => Table(outboxStatus, documentId);
    private static DataRowView Table(string outboxStatus, string documentId)
    {
        var table = new DataTable();
        foreach (var col in new[] { "Id", "OutboxDurumu", "ElectronicDocumentId", "BelgeNo", "Deneme", "SonHata" }) table.Columns.Add(col, typeof(string));
        var row = table.NewRow(); row["Id"] = "outbox-1"; row["OutboxDurumu"] = outboxStatus; row["ElectronicDocumentId"] = documentId; row["BelgeNo"] = "SF-001"; row["Deneme"] = "2"; row["SonHata"] = "Timeout";
        table.Rows.Add(row);
        return table.DefaultView[0];
    }

    [Theory]
    [InlineData("Pending", "outbox.run", true)]
    [InlineData("Processing", "outbox.run", false)] // §16 "Processing: normal action yok"
    [InlineData("DeadLetter", "outbox.retry", true)]
    [InlineData("Pending", "outbox.retry", false)]
    [InlineData("Completed", "outbox.open_document", true)]
    [InlineData("Pending", "outbox.open_document", false)]
    public void ForOutbox_ActionVisibility_MatchesOutboxStatus(string outboxStatus, string actionId, bool expectedVisible)
    {
        var db = Create();
        var actions = ElectronicDocumentContextActions.ForOutbox(db, "test-user", _ => { }, _ => { });
        var action = actions.Single(a => a.Id == actionId);
        var availability = ContextActionEvaluator.Evaluate(action, OutboxRow(outboxStatus), 1, null);
        Assert.Equal(expectedVisible, availability.Visible);
    }

    // --- InvoiceDetailViewModel permission gating (Phase 8 class, Phase 9 adds the checks) ---

    [Fact]
    public async Task InvoiceDetailViewModel_NoPostPermission_PostCommandDisabled_AndRefusedIfForced()
    {
        var db = Create();
        var branch = db.Query("SELECT id FROM branches LIMIT 1").Rows[0][0].ToString()!;
        var warehouse = db.Query("SELECT id FROM warehouses LIMIT 1").Rows[0][0].ToString()!;
        var unit = db.Query("SELECT id FROM units LIMIT 1").Rows[0][0].ToString()!;
        var product = Guid.NewGuid().ToString(); var now = DateTime.UtcNow.ToString("O");
        db.Execute("INSERT INTO products(id,company_id,code,name,base_unit_id,product_type,vat_rate,is_active,created_at,updated_at) VALUES($id,$c,$code,$code,$u,'Stock',20,1,$n,$n)",
            ("$id", product), ("$c", "00000000-0000-0000-0000-000000000001"), ("$code", "P01"), ("$u", unit), ("$n", now));
        await new LocalInventoryService(db).PostOpeningBalanceAsync(new("00000000-0000-0000-0000-000000000001", branch, warehouse, product, null, 10, null, DateTime.UtcNow));
        var account = new LocalAccountService(db);
        var accountId = Guid.NewGuid().ToString();
        account.Save(new AccountAggregateEdit(new AccountEdit(accountId, "00000000-0000-0000-0000-000000000001", "C01", "Cari C01", "Customer"), new AccountTaxProfileEdit(), new AccountEInvoiceProfileEdit(), new CustomerProfileEdit(), null));
        var sales = new LocalSalesService(db, new ElectronicDocumentRoutingService(db), new LocalElectronicDocumentService(db));
        var invoiceId = sales.CreateDraft(new("", "00000000-0000-0000-0000-000000000001", branch, warehouse, accountId, DateTime.Today, "", [new(product, null, unit, null, 1, 1, 100, 0, 20)]));

        var vm = new InvoiceDetailViewModel(db, invoiceId, "test-user", new CapturingLogger<InvoiceDetailViewModel>(), new FakePermissions("sales.invoice.post"));
        vm.Load();

        Assert.False(vm.PostCommand.CanExecute(null));
        await vm.PostCommand.ExecuteAsync(null); // a direct call still must not post (§30 - not just hidden)
        Assert.Equal("Bu işlem için yetkiniz bulunmuyor.", vm.ErrorMessage);
        Assert.True(vm.IsDraft);

        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
    }

    [Fact]
    public void EDocumentPresentation_SemanticStates_AreConsistentAcrossStatusAndColor()
    {
        foreach (var status in Enum.GetValues<ElectronicDocumentStatus>())
        {
            var semantic = EDocumentPresentation.StatusSemantic(status);
            var color = EDocumentPresentation.StatusColor(status);
            Assert.Equal(EDocumentPresentation.SemanticColor(semantic), color); // §41: color always derives from the semantic state, never picked independently
        }
    }

    public void Dispose()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        if (Directory.Exists(_folder)) Directory.Delete(_folder, recursive: true);
    }
}
