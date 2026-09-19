using Microsoft.Data.Sqlite;
using R3.Infrastructure;

namespace R3.Domain.Tests;

/// <summary>
/// Phase 9 (spec §45-46 "Integration/Retry Acceptance"): five invoices, each driven to a distinct
/// electronic-document state (Accepted / Queued / Failed-retry-pending / DeadLetter / Rejected)
/// through the real Posting/UBL/Outbox/Dispatcher engines - never a raw status UPDATE - then
/// asserts ElectronicDocumentOperationsService's read models (dashboard/outgoing/outbox/errors)
/// report exactly those five states.
/// </summary>
public sealed class ElectronicDocumentOperationsServiceTests : IDisposable
{
    private readonly string _folder = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "R3-ops-tests-" + Guid.NewGuid());
    private const string Company = "00000000-0000-0000-0000-000000000001";
    private StoreDatabase Create() => new(System.IO.Path.Combine(_folder, "test.db"));

    private sealed record Ctx(StoreDatabase Db, LocalSalesService Sales, LocalElectronicDocumentService Documents, ElectronicDocumentOutboxService Outbox,
        ElectronicDocumentGenerationService Generation, ElectronicDocumentOperationsService Operations, string Branch, string Warehouse, string Unit, string Product);

    private static Ctx Setup(StoreDatabase db)
    {
        var routing = new ElectronicDocumentRoutingService(db);
        var documents = new LocalElectronicDocumentService(db);
        var sales = new LocalSalesService(db, routing, documents);
        var outbox = new ElectronicDocumentOutboxService(db, documents);
        var generation = new ElectronicDocumentGenerationService(db, documents, new UblInvoiceGenerator(db, documents));
        var operations = new ElectronicDocumentOperationsService(db);
        var branch = db.Query("SELECT id FROM branches LIMIT 1").Rows[0][0].ToString()!;
        var warehouse = db.Query("SELECT id FROM warehouses LIMIT 1").Rows[0][0].ToString()!;
        var unit = db.Query("SELECT id FROM units LIMIT 1").Rows[0][0].ToString()!;
        documents.SaveCompanyProfile(new ElectronicDocumentCompanyProfileEdit(
            Company, "1234567890", "R3 Demo Ticaret A.Ş.", "urn:mail:r3@efatura.gov.tr", "", "", "Test", false, true, "Temel",
            "", "", "", "", "", "", "Örnek Sk. No:5", "İstanbul", "Şişli", "34394", "Türkiye"));
        var inventory = new LocalInventoryService(db);
        var product = Guid.NewGuid().ToString(); var now = DateTime.UtcNow.ToString("O");
        db.Execute("INSERT INTO products(id,company_id,code,name,base_unit_id,product_type,vat_rate,is_active,created_at,updated_at) VALUES($id,$c,$code,$code,$u,'Stock',20,1,$n,$n)",
            ("$id", product), ("$c", Company), ("$code", "P01"), ("$u", unit), ("$n", now));
        inventory.PostOpeningBalanceAsync(new(Company, branch, warehouse, product, null, 1000, null, DateTime.UtcNow)).GetAwaiter().GetResult();
        return new(db, sales, documents, outbox, generation, operations, branch, warehouse, unit, product);
    }

    private static string SeedAccount(StoreDatabase db, string code)
    {
        var account = new LocalAccountService(db);
        var id = Guid.NewGuid().ToString();
        account.Save(new AccountAggregateEdit(new AccountEdit(id, Company, code, $"Cari {code}", "Customer", TaxNumber: "9876543210"),
            new AccountTaxProfileEdit(), new AccountEInvoiceProfileEdit(), new CustomerProfileEdit(), null));
        return id;
    }

    private static string PostAndGenerate(Ctx ctx, string accountId)
    {
        var invoiceId = ctx.Sales.CreateDraft(new("", Company, ctx.Branch, ctx.Warehouse, accountId, DateTime.Today, "", [new(ctx.Product, null, ctx.Unit, null, 1, 1, 100, 0, 20)]));
        var posted = ctx.Sales.Post(invoiceId, "test-user");
        var result = ctx.Generation.Generate(posted.ElectronicDocumentId, "test-user");
        Assert.True(result.Success);
        return posted.ElectronicDocumentId;
    }

    // Keyed by Uuid so one dispatcher/provider instance can drive five documents to five different outcomes.
    private sealed class ScenarioProvider : IElectronicDocumentProvider
    {
        public HashSet<string> RejectOnQuery { get; } = [];
        public HashSet<string> FailOnceOnSend { get; } = [];
        private readonly HashSet<string> _alreadyFailed = [];

        public Task<ElectronicDocumentProviderResult> SendAsync(ElectronicDocumentSendRequest request, CancellationToken ct = default)
        {
            if (FailOnceOnSend.Contains(request.Uuid) && _alreadyFailed.Add(request.Uuid))
                return Task.FromResult(ElectronicDocumentProviderResult.TransientFailure("Timeout", "Sağlayıcı zaman aşımına uğradı."));
            return Task.FromResult(ElectronicDocumentProviderResult.Ok($"PRV-{request.Uuid[..8]}", $"ENV-{request.Uuid[..8]}"));
        }
        public Task<ElectronicDocumentStatusQueryResult> QueryStatusAsync(ElectronicDocumentStatusQueryRequest request, CancellationToken ct = default) =>
            Task.FromResult(RejectOnQuery.Contains(request.Uuid) ? ElectronicDocumentStatusQueryResult.Ok(ElectronicDocumentRemoteStatus.Rejected) : ElectronicDocumentStatusQueryResult.Ok(ElectronicDocumentRemoteStatus.Accepted));
    }

    [Fact]
    public async Task FiveInvoiceScenario_DashboardOutgoingOutboxAndErrorsAllReportTheExpectedStates()
    {
        var db = Create(); var ctx = Setup(db);
        var provider = new ScenarioProvider();
        var dispatcher = new ElectronicDocumentDispatcher(ctx.Documents, ctx.Outbox, provider);

        var accepted = PostAndGenerate(ctx, SeedAccount(db, "A"));
        var queued = PostAndGenerate(ctx, SeedAccount(db, "B"));
        var retryPending = PostAndGenerate(ctx, SeedAccount(db, "C"));
        var deadLetter = PostAndGenerate(ctx, SeedAccount(db, "D"));
        var rejected = PostAndGenerate(ctx, SeedAccount(db, "E"));

        var acceptedUuid = ctx.Documents.Get(accepted)!.Uuid;
        var retryPendingUuid = ctx.Documents.Get(retryPending)!.Uuid;
        var deadLetterUuid = ctx.Documents.Get(deadLetter)!.Uuid;
        var rejectedUuid = ctx.Documents.Get(rejected)!.Uuid;
        provider.RejectOnQuery.Add(rejectedUuid);
        provider.FailOnceOnSend.Add(retryPendingUuid);
        provider.FailOnceOnSend.Add(deadLetterUuid);

        // Invoice A -> Accepted (Send -> Sent, then QueryStatus -> Delivered -> Accepted).
        // Each DispatchAsync call claims a whole due batch (spec §12 "Şimdi Çalıştır" is a batch
        // trigger, not scoped to one document - see docs/architecture/OUTBOX-DISPATCH.md), so every
        // other invoice's outbox row must stay either not-yet-queued or not-yet-due whenever a
        // DispatchAsync call runs here; Invoice B (which must stay Queued) is queued last, with no
        // further dispatch call after it, for exactly that reason.
        ctx.Outbox.QueueForSendAsync(accepted, "test-user");
        await dispatcher.DispatchAsync("worker", "test-user"); // Send
        await dispatcher.DispatchAsync("worker", "test-user"); // QueryStatus

        // Invoice C -> transient failure -> document Failed, outbox rescheduled (RetryPending: Pending + attempt_count>0).
        ctx.Outbox.QueueForSendAsync(retryPending, "test-user");
        await dispatcher.DispatchAsync("worker", "test-user");

        // Invoice D -> same transient failure, then forced to DeadLetter (the real terminal state
        // ScheduleRetry itself reaches after max attempts - forced directly here instead of looping
        // the dispatcher 8 times, per the same "one outbox row, no fake data" discipline).
        ctx.Outbox.QueueForSendAsync(deadLetter, "test-user");
        await dispatcher.DispatchAsync("worker", "test-user");
        var deadLetterRow = ctx.Outbox.GetLatestForDocument(deadLetter)!;
        ctx.Outbox.DeadLetter(deadLetterRow.Id, "MaxAttemptsExceeded", "Maksimum deneme sayısına ulaşıldı.");

        // Invoice E -> Sent successfully, then rejected on QueryStatus.
        ctx.Outbox.QueueForSendAsync(rejected, "test-user");
        await dispatcher.DispatchAsync("worker", "test-user");
        await dispatcher.DispatchAsync("worker", "test-user");

        // Invoice B -> stays Queued: queued last, never dispatched.
        ctx.Outbox.QueueForSendAsync(queued, "test-user");

        var summary = ctx.Operations.GetDashboardSummary(Company);
        Assert.Equal(1, summary.Accepted);
        Assert.Equal(1, summary.Queued);
        // Both C (retry-pending) and D (dead-lettered) are Failed at the document level - DeadLetter
        // is an outbox-row terminal state, it does not move the document status anywhere else, so a
        // dead-lettered document stays Failed forever unless ManualRetry requeues it (§20/§46).
        Assert.Equal(2, summary.Failed);
        Assert.Equal(1, summary.Rejected);
        Assert.True(summary.RetryPending >= 1); // invoice C
        Assert.Equal(1, summary.DeadLetter);
        Assert.Equal(5, summary.CreatedToday);

        var errors = ctx.Operations.SearchErrors(Company, new ElectronicDocumentFilter());
        var errorIds = errors.Rows.Cast<System.Data.DataRow>().Select(r => r["Id"].ToString()).ToHashSet();
        Assert.Contains(retryPending, errorIds);
        Assert.Contains(deadLetter, errorIds);
        Assert.Contains(rejected, errorIds);
        Assert.DoesNotContain(accepted, errorIds); // §20: Accepted is not an error
        Assert.DoesNotContain(queued, errorIds);   // §20: merely Queued is not an error

        var deadLetterOutboxRows = ctx.Operations.SearchOutbox(Company, new ElectronicDocumentFilter(OutboxStatus: "DeadLetter"));
        var deadLetterOutboxIds = deadLetterOutboxRows.Rows.Cast<System.Data.DataRow>().Select(r => r["ElectronicDocumentId"].ToString()).ToList();
        Assert.Single(deadLetterOutboxIds);
        Assert.Equal(deadLetter, deadLetterOutboxIds[0]);

        // §46 Retry Acceptance: ManualRetry on the DeadLetter document preserves attempt history and
        // idempotency key, moves the outbox row back to Pending, and a Development-style success
        // dispatch pass then completes it as Sent.
        var oldKey = deadLetterRow.IdempotencyKey; var oldAttempts = deadLetterRow.AttemptCount;
        var newOutboxId = ctx.Outbox.ManualRetry(deadLetter, "test-user");
        var newRow = ctx.Outbox.Get(newOutboxId)!;
        Assert.Equal(ElectronicDocumentOutboxStatus.Pending, newRow.Status);
        Assert.Equal(oldKey, newRow.IdempotencyKey);
        Assert.True(newRow.AttemptCount >= oldAttempts || newRow.Id != deadLetterRow.Id); // a fresh row preserves the old one's history untouched
        provider.FailOnceOnSend.Remove(deadLetterUuid); // simulate the transient condition clearing
        await dispatcher.DispatchAsync("worker", "test-user");
        Assert.Equal(ElectronicDocumentStatus.Sent, ctx.Documents.Get(deadLetter)!.Status);
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        if (System.IO.Directory.Exists(_folder)) System.IO.Directory.Delete(_folder, recursive: true);
    }
}
