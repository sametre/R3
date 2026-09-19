using System.Data;
using Microsoft.Data.Sqlite;
using R3.Infrastructure;

namespace R3.Domain.Tests;

public sealed class ElectronicDocumentTests : IDisposable
{
    private readonly string _folder = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "R3-tests-" + Guid.NewGuid());
    private const string Company = "00000000-0000-0000-0000-000000000001";
    private StoreDatabase Create() => new(System.IO.Path.Combine(_folder, "test.db"));

    private static ElectronicDocumentDraft Draft(StoreDatabase db, string sourceId, string? accountId = null, string? uuid = null) =>
        new(Company, db.Query("SELECT id FROM branches LIMIT 1").Rows[0][0].ToString()!, ElectronicDocumentType.EArchiveInvoice, ElectronicDocumentDirection.Outgoing,
            "SalesInvoice", sourceId, accountId, "SF-2026-000001", DateTime.Today, "TRY", 100m, "{}", "test-user", uuid);

    // Ready + a saved UBL payload, but not yet Generated/Queued - the state every Phase-3 outbox test starts from.
    private static string ReadyDocumentWithPayload(LocalElectronicDocumentService service, StoreDatabase db)
    {
        var id = service.CreateOrGetForSource(Draft(db, Guid.NewGuid().ToString()));
        service.SavePayload(id, ElectronicDocumentPayloadType.UblXml, "<Invoice/>", "application/xml", false, "u");
        service.Ready(id, "u"); service.Generate(id, "u");
        return id;
    }

    // ===== Phase 2 review-gate additions =====

    [Fact]
    public void CreateOrGetForSourceIsIdempotent()
    {
        var db = Create(); var service = new LocalElectronicDocumentService(db); var sourceId = Guid.NewGuid().ToString();
        var first = service.CreateOrGetForSource(Draft(db, sourceId));
        var second = service.CreateOrGetForSource(Draft(db, sourceId));
        Assert.Equal(first, second);
        Assert.Single(db.Query("SELECT id FROM electronic_documents WHERE source_entity_id=$id", ("$id", sourceId)).Rows.Cast<DataRow>());
    }

    [Fact]
    public void DuplicateUuidIsRejectedAcrossDifferentSources()
    {
        var db = Create(); var service = new LocalElectronicDocumentService(db); var uuid = Guid.NewGuid().ToString();
        service.CreateOrGetForSource(Draft(db, Guid.NewGuid().ToString(), uuid: uuid));
        Assert.Throws<InvalidOperationException>(() => service.CreateOrGetForSource(Draft(db, Guid.NewGuid().ToString(), uuid: uuid)));
    }

    [Fact]
    public void StatusEngineFollowsHappyPathAndEmitsEventPerTransition()
    {
        var db = Create(); var service = new LocalElectronicDocumentService(db);
        var id = service.CreateOrGetForSource(Draft(db, Guid.NewGuid().ToString()));
        Assert.Throws<InvalidOperationException>(() => service.MarkSent(id, "test-user"));
        service.SavePayload(id, ElectronicDocumentPayloadType.UblXml, "<Invoice/>", "application/xml", false, "u");
        service.Ready(id, "test-user"); service.Generate(id, "test-user"); service.Queue(id, "test-user"); service.StartSending(id, "test-user"); service.MarkSent(id, "test-user", "PRV-1");
        service.MarkDelivered(id, "test-user"); service.Accept(id, "test-user", "Kabul edildi");
        var doc = service.Get(id)!;
        Assert.Equal(ElectronicDocumentStatus.Accepted, doc.Status); Assert.Equal("PRV-1", doc.ProviderDocumentId);
        var events = service.GetEvents(id);
        Assert.Equal("ElectronicDocumentCreated", events[0].EventType); Assert.Contains(events, e => e.EventType == "DocumentAccepted" && e.ProviderMessage == "Kabul edildi");
        Assert.Equal(9, events.Count); // Created + PayloadSaved + Ready + Generated + Queued + Sending + Sent + Delivered + Accepted = exactly one event per write, no more, no less.
    }

    [Fact]
    public void TerminalStatesRejectFurtherTransitions()
    {
        var db = Create(); var service = new LocalElectronicDocumentService(db);
        var accepted = service.CreateOrGetForSource(Draft(db, Guid.NewGuid().ToString()));
        service.SavePayload(accepted, ElectronicDocumentPayloadType.UblXml, "<Invoice/>", "application/xml", false, "u");
        service.Ready(accepted, "u"); service.Generate(accepted, "u"); service.Queue(accepted, "u"); service.StartSending(accepted, "u"); service.MarkSent(accepted, "u"); service.MarkDelivered(accepted, "u"); service.Accept(accepted, "u");
        service.Archive(accepted, "u");
        Assert.Throws<InvalidOperationException>(() => service.Queue(accepted, "u"));
        Assert.Throws<InvalidOperationException>(() => service.RequestCancellation(accepted, "u"));

        var cancelled = service.CreateOrGetForSource(Draft(db, Guid.NewGuid().ToString()));
        service.SavePayload(cancelled, ElectronicDocumentPayloadType.UblXml, "<Invoice/>", "application/xml", false, "u");
        service.Ready(cancelled, "u"); service.Generate(cancelled, "u"); service.Queue(cancelled, "u"); service.StartSending(cancelled, "u"); service.MarkSent(cancelled, "u"); service.MarkDelivered(cancelled, "u"); service.Accept(cancelled, "u");
        service.RequestCancellation(cancelled, "u"); service.Cancel(cancelled, "u");
        Assert.Throws<InvalidOperationException>(() => service.Queue(cancelled, "u"));
        Assert.Throws<InvalidOperationException>(() => service.MarkSent(cancelled, "u"));
    }

    [Fact]
    public void IllegalTransitionEmitsNoEvent()
    {
        var db = Create(); var service = new LocalElectronicDocumentService(db);
        var id = service.CreateOrGetForSource(Draft(db, Guid.NewGuid().ToString()));
        var before = service.GetEvents(id).Count;
        Assert.Throws<InvalidOperationException>(() => service.MarkSent(id, "u")); // Draft -> Sent is illegal
        Assert.Equal(before, service.GetEvents(id).Count);
    }

    [Fact]
    public void GenerateRequiresAPayloadToAlreadyExist()
    {
        var db = Create(); var service = new LocalElectronicDocumentService(db);
        var id = service.CreateOrGetForSource(Draft(db, Guid.NewGuid().ToString()));
        service.Ready(id, "u");
        Assert.Throws<InvalidOperationException>(() => service.Generate(id, "u"));
        Assert.Equal(ElectronicDocumentStatus.Ready, service.Get(id)!.Status);
        service.SavePayload(id, ElectronicDocumentPayloadType.UblXml, "<Invoice/>", "application/xml", false, "u");
        service.Generate(id, "u"); // now allowed
        Assert.Equal(ElectronicDocumentStatus.Generated, service.Get(id)!.Status);
    }

    [Fact]
    public void FailedDocumentCanRetryButRejectedIsTerminal()
    {
        var db = Create(); var service = new LocalElectronicDocumentService(db);
        var failing = service.CreateOrGetForSource(Draft(db, Guid.NewGuid().ToString()));
        service.SavePayload(failing, ElectronicDocumentPayloadType.UblXml, "<Invoice/>", "application/xml", false, "u");
        service.Ready(failing, "u"); service.Generate(failing, "u"); service.Queue(failing, "u"); service.StartSending(failing, "u");
        service.Fail(failing, "u", "TIMEOUT", "Sağlayıcı yanıt vermedi");
        Assert.Equal(ElectronicDocumentStatus.Failed, service.Get(failing)!.Status);
        service.Retry(failing, "u"); Assert.Equal(ElectronicDocumentStatus.Queued, service.Get(failing)!.Status);

        var rejected = service.CreateOrGetForSource(Draft(db, Guid.NewGuid().ToString()));
        service.SavePayload(rejected, ElectronicDocumentPayloadType.UblXml, "<Invoice/>", "application/xml", false, "u");
        service.Ready(rejected, "u"); service.Generate(rejected, "u"); service.Queue(rejected, "u"); service.StartSending(rejected, "u"); service.MarkSent(rejected, "u"); service.MarkDelivered(rejected, "u"); service.Reject(rejected, "u", "VKN hatalı");
        Assert.Throws<InvalidOperationException>(() => service.Retry(rejected, "u"));
    }

    [Fact]
    public void PayloadCannotBeOverwrittenFromGeneratedOnwardButPdfAlwaysCan()
    {
        var db = Create(); var service = new LocalElectronicDocumentService(db);
        var id = service.CreateOrGetForSource(Draft(db, Guid.NewGuid().ToString()));
        service.SavePayload(id, ElectronicDocumentPayloadType.UblXml, "<Invoice/>", "application/xml", false, "u");
        service.Ready(id, "u"); service.Generate(id, "u"); // locked from here on, not just from Sent
        Assert.Throws<InvalidOperationException>(() => service.SavePayload(id, ElectronicDocumentPayloadType.UblXml, "<Invoice><Changed/></Invoice>", "application/xml", false, "u"));
        service.SavePayload(id, ElectronicDocumentPayloadType.Pdf, "base64pdf", "application/pdf", false, "u"); // different payload type, always allowed
        var payloads = service.GetPayloads(id);
        Assert.Single(payloads, p => p.PayloadType == ElectronicDocumentPayloadType.UblXml);
        Assert.Single(payloads, p => p.PayloadType == ElectronicDocumentPayloadType.Pdf);
        Assert.Equal(64, payloads.First(p => p.PayloadType == ElectronicDocumentPayloadType.UblXml).ContentHash.Length); // SHA-256 hex = 64 chars
    }

    [Fact]
    public void RoutingPicksEInvoiceForRegisteredRecipientAndEArchiveOtherwise()
    {
        var db = Create(); var account = new LocalAccountService(db); var routing = new ElectronicDocumentRoutingService(db);
        var registered = Guid.NewGuid().ToString(); var unregistered = Guid.NewGuid().ToString();
        account.Save(new AccountAggregateEdit(new AccountEdit(registered, Company, "EI01", "E-Fatura Mükellefi", "Customer"), new AccountTaxProfileEdit(),
            new AccountEInvoiceProfileEdit(IsEInvoiceEnabled: true, EInvoiceAlias: "urn:mail:test@efatura.gov.tr"), new CustomerProfileEdit(), null));
        account.Save(new AccountAggregateEdit(new AccountEdit(unregistered, Company, "EI02", "E-Arşiv Müşterisi", "Customer"), new AccountTaxProfileEdit(), new AccountEInvoiceProfileEdit(), new CustomerProfileEdit(), null));
        Assert.Equal(ElectronicDocumentType.EInvoice, routing.RouteOutgoingInvoice(Company, registered));
        Assert.Equal(ElectronicDocumentType.EArchiveInvoice, routing.RouteOutgoingInvoice(Company, unregistered));
    }

    [Fact]
    public void RoutingRequiresAliasEvenWhenEnabledFlagIsSet()
    {
        var db = Create(); var account = new LocalAccountService(db); var routing = new ElectronicDocumentRoutingService(db);
        var id = Guid.NewGuid().ToString();
        account.Save(new AccountAggregateEdit(new AccountEdit(id, Company, "EI03", "Alias Eksik", "Customer"), new AccountTaxProfileEdit(),
            new AccountEInvoiceProfileEdit(IsEInvoiceEnabled: true, EInvoiceAlias: ""), new CustomerProfileEdit(), null));
        Assert.Equal(ElectronicDocumentType.EArchiveInvoice, routing.RouteOutgoingInvoice(Company, id));
    }

    [Fact]
    public void RoutingFallsBackToEArchiveWhenNoProfileRowExistsAtAll()
    {
        var db = Create(); var routing = new ElectronicDocumentRoutingService(db); var id = Guid.NewGuid().ToString(); var now = DateTime.UtcNow.ToString("O");
        // Bypasses LocalAccountService.Save (which always upserts an account_einvoice_profiles row) so this genuinely has no profile row, not just a disabled one.
        db.Execute("INSERT INTO accounts(id,company_id,code,name,account_type,is_active,created_at,updated_at) VALUES($id,$c,'EI04','Profilsiz','Customer',1,$n,$n)",
            ("$id", id), ("$c", Company), ("$n", now));
        Assert.Equal(ElectronicDocumentType.EArchiveInvoice, routing.RouteOutgoingInvoice(Company, id));
    }

    // ===== Phase 3: outbox + dispatcher =====

    [Fact]
    public void ReadyDocumentCanBeQueuedAndQueueOperationIsIdempotent()
    {
        var db = Create(); var service = new LocalElectronicDocumentService(db); var outbox = new ElectronicDocumentOutboxService(db, service);
        var id = ReadyDocumentWithPayload(service, db);
        var first = outbox.QueueForSendAsync(id, "u");
        Assert.Equal(ElectronicDocumentStatus.Queued, service.Get(id)!.Status);
        var second = outbox.QueueForSendAsync(id, "u"); // still Queued/no new active Send op -> same outbox row, no duplicate
        Assert.Equal(first, second);
        Assert.Single(db.Query("SELECT id FROM electronic_document_outbox WHERE electronic_document_id=$id", ("$id", id)).Rows.Cast<DataRow>());
    }

    [Fact]
    public void TerminalDocumentCannotBeQueuedAgain()
    {
        var db = Create(); var service = new LocalElectronicDocumentService(db); var outbox = new ElectronicDocumentOutboxService(db, service);
        var id = ReadyDocumentWithPayload(service, db);
        // Drives the lifecycle to Rejected without going through the outbox, so there is no leftover
        // active outbox row for QueueForSendAsync's idempotency short-circuit to (correctly) return early on.
        service.Queue(id, "u"); service.StartSending(id, "u"); service.MarkSent(id, "u"); service.MarkDelivered(id, "u"); service.Reject(id, "u");
        Assert.Throws<InvalidOperationException>(() => outbox.QueueForSendAsync(id, "u"));
    }

    [Fact]
    public void PendingOutboxRowIsClaimedByOnlyOneOfTwoWorkers()
    {
        var db = Create(); var service = new LocalElectronicDocumentService(db); var outbox = new ElectronicDocumentOutboxService(db, service);
        var id = ReadyDocumentWithPayload(service, db); outbox.QueueForSendAsync(id, "u");
        var claimedByA = outbox.ClaimDue("worker-a");
        var claimedByB = outbox.ClaimDue("worker-b");
        Assert.Single(claimedByA); Assert.Empty(claimedByB);
    }

    [Fact]
    public async Task SuccessfulSendCompletesOutboxMarksDocumentSentAndSavesProviderId()
    {
        var db = Create(); var service = new LocalElectronicDocumentService(db); var outbox = new ElectronicDocumentOutboxService(db, service);
        var id = ReadyDocumentWithPayload(service, db); outbox.QueueForSendAsync(id, "u");
        var provider = new ScriptedProvider(_ => ElectronicDocumentProviderResult.Ok("PRV-42"));
        var dispatcher = new ElectronicDocumentDispatcher(service, outbox, provider);

        var processed = await dispatcher.DispatchAsync("worker-a", "u");
        Assert.Equal(1, processed);
        Assert.Equal(ElectronicDocumentStatus.Sent, service.Get(id)!.Status);
        Assert.Equal("PRV-42", service.Get(id)!.ProviderDocumentId);
        var outboxRow = outbox.GetQueue(Company); // Completed rows aren't in the active queue view
        Assert.Empty(outboxRow.Rows.Cast<DataRow>());
    }

    [Fact]
    public async Task TransientFailureSchedulesRetryWithIncreasedAttemptCount()
    {
        var db = Create(); var service = new LocalElectronicDocumentService(db); var outbox = new ElectronicDocumentOutboxService(db, service);
        var id = ReadyDocumentWithPayload(service, db); var outboxId = outbox.QueueForSendAsync(id, "u");
        var provider = new ScriptedProvider(_ => ElectronicDocumentProviderResult.TransientFailure("TIMEOUT", "zaman aşımı"));
        var dispatcher = new ElectronicDocumentDispatcher(service, outbox, provider);

        await dispatcher.DispatchAsync("worker-a", "u");
        var row = outbox.Get(outboxId)!;
        Assert.Equal(ElectronicDocumentOutboxStatus.Pending, row.Status);
        Assert.Equal(1, row.AttemptCount);
        Assert.NotNull(row.NextAttemptAt);
        Assert.Equal(ElectronicDocumentStatus.Failed, service.Get(id)!.Status);
    }

    [Fact]
    public async Task PermanentRejectionGoesDeadLetterImmediatelyWithoutRetry()
    {
        var db = Create(); var service = new LocalElectronicDocumentService(db); var outbox = new ElectronicDocumentOutboxService(db, service);
        var id = ReadyDocumentWithPayload(service, db); var outboxId = outbox.QueueForSendAsync(id, "u");
        var provider = new ScriptedProvider(_ => ElectronicDocumentProviderResult.Rejected("INVALID_VKN", "VKN hatalı"));
        var dispatcher = new ElectronicDocumentDispatcher(service, outbox, provider);

        await dispatcher.DispatchAsync("worker-a", "u");
        var row = outbox.Get(outboxId)!;
        Assert.Equal(ElectronicDocumentOutboxStatus.DeadLetter, row.Status);
        Assert.Null(row.NextAttemptAt);
        Assert.Equal(1, row.AttemptCount);
    }

    [Fact]
    public async Task RepeatedTransientFailuresExhaustMaxAttemptsAndGoDeadLetter()
    {
        var db = Create(); var service = new LocalElectronicDocumentService(db); var outbox = new ElectronicDocumentOutboxService(db, service);
        var id = ReadyDocumentWithPayload(service, db); var outboxId = outbox.QueueForSendAsync(id, "u");
        var provider = new ScriptedProvider(_ => ElectronicDocumentProviderResult.TransientFailure("TIMEOUT", "zaman aşımı"));
        var dispatcher = new ElectronicDocumentDispatcher(service, outbox, provider);

        for (var i = 0; i < 8; i++)
        {
            await dispatcher.DispatchAsync("worker-a", "u");
            db.Execute("UPDATE electronic_document_outbox SET available_at=$now,next_attempt_at=$now WHERE id=$id", ("$now", DateTime.UtcNow.AddMinutes(-1).ToString("O")), ("$id", outboxId));
        }
        Assert.Equal(ElectronicDocumentOutboxStatus.DeadLetter, outbox.Get(outboxId)!.Status);
    }

    [Fact]
    public async Task ManualRetryAfterDeadLetterReusesIdempotencyKeyAndCanSucceed()
    {
        var db = Create(); var service = new LocalElectronicDocumentService(db); var outbox = new ElectronicDocumentOutboxService(db, service);
        var id = ReadyDocumentWithPayload(service, db); var firstOutboxId = outbox.QueueForSendAsync(id, "u");
        var rejecting = new ScriptedProvider(_ => ElectronicDocumentProviderResult.Rejected("INVALID_VKN", "VKN hatalı"));
        await new ElectronicDocumentDispatcher(service, outbox, rejecting).DispatchAsync("worker-a", "u");
        var firstKey = outbox.Get(firstOutboxId)!.IdempotencyKey;
        Assert.Equal(ElectronicDocumentOutboxStatus.DeadLetter, outbox.Get(firstOutboxId)!.Status);

        var secondOutboxId = outbox.ManualRetry(id, "u");
        Assert.NotEqual(firstOutboxId, secondOutboxId); // old row kept, history not overwritten
        Assert.Equal(firstKey, outbox.Get(secondOutboxId)!.IdempotencyKey); // same key across the new attempt
        Assert.Equal(ElectronicDocumentOutboxStatus.DeadLetter, outbox.Get(firstOutboxId)!.Status); // untouched

        string? sentWithKey = null;
        var accepting = new ScriptedProvider(r => { sentWithKey = r.IdempotencyKey; return ElectronicDocumentProviderResult.Ok("PRV-99"); });
        await new ElectronicDocumentDispatcher(service, outbox, accepting).DispatchAsync("worker-a", "u");
        Assert.Equal(firstKey, sentWithKey);
        Assert.Equal(ElectronicDocumentStatus.Sent, service.Get(id)!.Status);
    }

    [Fact]
    public async Task StaleLockIsReclaimedAndRedispatched()
    {
        var db = Create(); var service = new LocalElectronicDocumentService(db); var outbox = new ElectronicDocumentOutboxService(db, service);
        var id = ReadyDocumentWithPayload(service, db); var outboxId = outbox.QueueForSendAsync(id, "u");
        outbox.ClaimDue("worker-a"); // simulates a worker that claimed the row then crashed before completing it
        Assert.Equal(ElectronicDocumentOutboxStatus.Processing, outbox.Get(outboxId)!.Status);

        Assert.Equal(0, outbox.ReclaimStale(TimeSpan.FromMinutes(10))); // not stale yet
        db.Execute("UPDATE electronic_document_outbox SET locked_at=$old WHERE id=$id", ("$old", DateTime.UtcNow.AddMinutes(-30).ToString("O")), ("$id", outboxId));
        Assert.Equal(1, outbox.ReclaimStale(TimeSpan.FromMinutes(10)));
        Assert.Equal(ElectronicDocumentOutboxStatus.Pending, outbox.Get(outboxId)!.Status);

        var provider = new ScriptedProvider(_ => ElectronicDocumentProviderResult.Ok("PRV-1"));
        var processed = await new ElectronicDocumentDispatcher(service, outbox, provider).DispatchAsync("worker-b", "u");
        Assert.Equal(1, processed);
        Assert.Equal(ElectronicDocumentStatus.Sent, service.Get(id)!.Status);
    }

    private sealed class ScriptedProvider(Func<ElectronicDocumentSendRequest, ElectronicDocumentProviderResult> handler) : IElectronicDocumentProvider
    {
        public List<ElectronicDocumentSendRequest> Requests { get; } = [];
        public Task<ElectronicDocumentProviderResult> SendAsync(ElectronicDocumentSendRequest request, CancellationToken ct = default)
        {
            Requests.Add(request);
            return Task.FromResult(handler(request));
        }
    }

    public void Dispose() { SqliteConnection.ClearAllPools(); if (Directory.Exists(_folder)) Directory.Delete(_folder, true); }
}
