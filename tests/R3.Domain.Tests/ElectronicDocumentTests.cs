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

    [Fact]
    public void CreateOrGetForSourceIsIdempotent()
    {
        var db = Create(); var service = new LocalElectronicDocumentService(db); var sourceId = Guid.NewGuid().ToString();
        var first = service.CreateOrGetForSource(Draft(db, sourceId));
        var second = service.CreateOrGetForSource(Draft(db, sourceId));
        Assert.Equal(first, second);
        Assert.Single(db.Query("SELECT id FROM electronic_documents WHERE source_entity_id=$id", ("$id", sourceId)).Rows.Cast<System.Data.DataRow>());
    }

    [Fact]
    public void DuplicateUuidIsRejectedAcrossDifferentSources()
    {
        var db = Create(); var service = new LocalElectronicDocumentService(db); var uuid = Guid.NewGuid().ToString();
        service.CreateOrGetForSource(Draft(db, Guid.NewGuid().ToString(), uuid: uuid));
        Assert.Throws<InvalidOperationException>(() => service.CreateOrGetForSource(Draft(db, Guid.NewGuid().ToString(), uuid: uuid)));
    }

    [Fact]
    public void StatusEngineFollowsHappyPathAndRejectsIllegalJumps()
    {
        var db = Create(); var service = new LocalElectronicDocumentService(db);
        var id = service.CreateOrGetForSource(Draft(db, Guid.NewGuid().ToString()));
        Assert.Throws<InvalidOperationException>(() => service.MarkSent(id, "test-user"));
        service.Ready(id, "test-user"); service.Generate(id, "test-user"); service.Queue(id, "test-user"); service.StartSending(id, "test-user"); service.MarkSent(id, "test-user", "PRV-1");
        service.MarkDelivered(id, "test-user"); service.Accept(id, "test-user", "Kabul edildi");
        var doc = service.Get(id)!;
        Assert.Equal(ElectronicDocumentStatus.Accepted, doc.Status); Assert.Equal("PRV-1", doc.ProviderDocumentId);
        Assert.Throws<InvalidOperationException>(() => service.Queue(id, "test-user"));
        var events = service.GetEvents(id);
        Assert.Equal("ElectronicDocumentCreated", events[0].EventType); Assert.Contains(events, e => e.EventType == "DocumentAccepted" && e.ProviderMessage == "Kabul edildi");
    }

    [Fact]
    public void FailedDocumentCanRetryButRejectedIsTerminal()
    {
        var db = Create(); var service = new LocalElectronicDocumentService(db);
        var failing = service.CreateOrGetForSource(Draft(db, Guid.NewGuid().ToString()));
        service.Ready(failing, "u"); service.Generate(failing, "u"); service.Queue(failing, "u"); service.StartSending(failing, "u");
        service.Fail(failing, "u", "TIMEOUT", "Sağlayıcı yanıt vermedi");
        Assert.Equal(ElectronicDocumentStatus.Failed, service.Get(failing)!.Status);
        service.Retry(failing, "u"); Assert.Equal(ElectronicDocumentStatus.Queued, service.Get(failing)!.Status);

        var rejected = service.CreateOrGetForSource(Draft(db, Guid.NewGuid().ToString()));
        service.Ready(rejected, "u"); service.Generate(rejected, "u"); service.Queue(rejected, "u"); service.StartSending(rejected, "u"); service.MarkSent(rejected, "u"); service.MarkDelivered(rejected, "u"); service.Reject(rejected, "u", "VKN hatalı");
        Assert.Throws<InvalidOperationException>(() => service.Retry(rejected, "u"));
    }

    [Fact]
    public void PayloadCannotBeOverwrittenAfterSentButPdfAlwaysCan()
    {
        var db = Create(); var service = new LocalElectronicDocumentService(db);
        var id = service.CreateOrGetForSource(Draft(db, Guid.NewGuid().ToString()));
        service.SavePayload(id, ElectronicDocumentPayloadType.UblXml, "<Invoice/>", "application/xml", false, "u");
        service.Ready(id, "u"); service.Generate(id, "u"); service.Queue(id, "u"); service.StartSending(id, "u"); service.MarkSent(id, "u");
        Assert.Throws<InvalidOperationException>(() => service.SavePayload(id, ElectronicDocumentPayloadType.UblXml, "<Invoice><Changed/></Invoice>", "application/xml", false, "u"));
        service.SavePayload(id, ElectronicDocumentPayloadType.Pdf, "base64pdf", "application/pdf", false, "u");
        var payloads = service.GetPayloads(id);
        Assert.Single(payloads, p => p.PayloadType == ElectronicDocumentPayloadType.UblXml);
        Assert.Single(payloads, p => p.PayloadType == ElectronicDocumentPayloadType.Pdf);
        Assert.Equal(64, payloads.First(p => p.PayloadType == ElectronicDocumentPayloadType.UblXml).ContentHash.Length);
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
    public async Task OutboxProcessorSendsQueuedDocumentViaProvider()
    {
        var db = Create(); var service = new LocalElectronicDocumentService(db);
        var id = service.CreateOrGetForSource(Draft(db, Guid.NewGuid().ToString()));
        service.SavePayload(id, ElectronicDocumentPayloadType.UblXml, "<Invoice/>", "application/xml", false, "u");
        service.Ready(id, "u"); service.Generate(id, "u"); service.Queue(id, "u");
        var processor = new ElectronicDocumentOutboxProcessor(service, new ManualElectronicDocumentProvider());
        var processed = await processor.ProcessAsync(Company, "outbox");
        Assert.Equal(1, processed);
        var doc = service.Get(id)!;
        Assert.Equal(ElectronicDocumentStatus.Sent, doc.Status);
        Assert.StartsWith("MANUAL-", doc.ProviderDocumentId);
    }

    [Fact]
    public async Task OutboxProcessorWithoutPayloadFailsWithoutThrowing()
    {
        var db = Create(); var service = new LocalElectronicDocumentService(db);
        var id = service.CreateOrGetForSource(Draft(db, Guid.NewGuid().ToString()));
        service.Ready(id, "u"); service.Generate(id, "u"); service.Queue(id, "u");
        var processor = new ElectronicDocumentOutboxProcessor(service, new ManualElectronicDocumentProvider());
        await processor.ProcessAsync(Company, "outbox");
        Assert.Equal(ElectronicDocumentStatus.Failed, service.Get(id)!.Status);
    }

    [Fact]
    public async Task TransientFailureIsRetriedAutomaticallyButRejectionIsNot()
    {
        var db = Create(); var service = new LocalElectronicDocumentService(db);
        var transient = service.CreateOrGetForSource(Draft(db, Guid.NewGuid().ToString()));
        service.SavePayload(transient, ElectronicDocumentPayloadType.UblXml, "<Invoice/>", "application/xml", false, "u");
        service.Ready(transient, "u"); service.Generate(transient, "u"); service.Queue(transient, "u");

        var rejected = service.CreateOrGetForSource(Draft(db, Guid.NewGuid().ToString()));
        service.SavePayload(rejected, ElectronicDocumentPayloadType.UblXml, "<Invoice/>", "application/xml", false, "u");
        service.Ready(rejected, "u"); service.Generate(rejected, "u"); service.Queue(rejected, "u");

        var provider = new SequencedProvider(transient, ProviderSendResult.TransientFailure("TIMEOUT", "zaman aşımı"), ProviderSendResult.Ok("MANUAL-2"));
        provider.RejectFor(rejected, "INVALID_VKN", "VKN hatalı");
        var processor = new ElectronicDocumentOutboxProcessor(service, provider);

        await processor.ProcessAsync(Company, "outbox");
        Assert.Equal(ElectronicDocumentStatus.Failed, service.Get(transient)!.Status);
        Assert.Equal(ElectronicDocumentStatus.Failed, service.Get(rejected)!.Status);

        // Force the retry to be due now instead of sleeping for the real backoff window.
        db.Execute("UPDATE electronic_documents SET next_retry_at=$now WHERE id=$id", ("$now", DateTime.UtcNow.AddMinutes(-1).ToString("O")), ("$id", transient));
        await processor.ProcessAsync(Company, "outbox");
        Assert.Equal(ElectronicDocumentStatus.Sent, service.Get(transient)!.Status);
        Assert.Equal(ElectronicDocumentStatus.Failed, service.Get(rejected)!.Status); // rejection never scheduled a retry
    }

    private sealed class SequencedProvider(string transientId, ProviderSendResult firstResult, ProviderSendResult secondResult) : IElectronicDocumentProvider
    {
        private readonly Dictionary<string, (string Code, string Message)> _rejections = new();
        private bool _firstCallDone;
        public void RejectFor(string documentId, string code, string message) => _rejections[documentId] = (code, message);
        public Task<ProviderSendResult> SendAsync(ElectronicDocumentRow document, string ublContent, CancellationToken ct = default)
        {
            if (_rejections.TryGetValue(document.Id, out var rejection)) return Task.FromResult(ProviderSendResult.Rejected(rejection.Code, rejection.Message));
            if (document.Id != transientId) return Task.FromResult(ProviderSendResult.Ok());
            var result = _firstCallDone ? secondResult : firstResult; _firstCallDone = true; return Task.FromResult(result);
        }
    }

    public void Dispose() { SqliteConnection.ClearAllPools(); if (Directory.Exists(_folder)) Directory.Delete(_folder, true); }
}
