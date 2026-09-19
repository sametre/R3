using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace R3.Infrastructure;

// Phase 3/7 (spec §22, Phase 7 §5). The only place that calls IElectronicDocumentProvider - and it does
// so entirely outside any business-posting transaction, so a slow or unreachable provider can never
// block or roll back invoice/shipment posting (spec §72). Reclaim -> claim -> send/query -> update is a
// plain async loop; Desktop has no background-job host yet (see docs/architecture/R3-ENGINE-ARCHITECTURE.md),
// so this is invoked from a timer or a manual "Şimdi Gönder"/"Şimdi Çalıştır" action, not a hosted service.
public sealed class ElectronicDocumentDispatcher(LocalElectronicDocumentService documents, ElectronicDocumentOutboxService outbox, IElectronicDocumentProvider provider, ILogger<ElectronicDocumentDispatcher>? logger = null)
{
    private readonly ILogger _logger = logger ?? NullLogger<ElectronicDocumentDispatcher>.Instance;

    public async Task<int> DispatchAsync(string workerId, string userId, CancellationToken ct = default)
    {
        var reclaimed = outbox.ReclaimStale();
        if (reclaimed > 0) _logger.LogWarning("Stale outbox locks reclaimed. Count={Count} WorkerId={WorkerId}", reclaimed, workerId);
        var claimed = outbox.ClaimDue(workerId);
        var processed = 0;
        foreach (var outboxId in claimed)
        {
            if (ct.IsCancellationRequested) break;
            await ProcessOneAsync(outboxId, workerId, userId, ct);
            processed++;
        }
        return processed;
    }

    private async Task ProcessOneAsync(string outboxId, string workerId, string userId, CancellationToken ct)
    {
        var outboxRow = outbox.Get(outboxId) ?? throw new KeyNotFoundException("Outbox kaydı bulunamadı.");
        using var scope = _logger.BeginScope(new Dictionary<string, object?>
        {
            ["OutboxId"] = outboxId, ["ElectronicDocumentId"] = outboxRow.ElectronicDocumentId, ["Operation"] = outboxRow.OperationType, ["WorkerId"] = workerId
        });
        switch (outboxRow.OperationType)
        {
            case ElectronicDocumentOutboxOperation.Send: await ProcessSendAsync(outboxRow, userId, ct); break;
            case ElectronicDocumentOutboxOperation.QueryStatus: await ProcessStatusQueryAsync(outboxRow, userId, ct); break;
            default:
                // MVP scope (spec §13): Generate/Sign/Cancel/DownloadIncoming/ProcessIncoming are reserved.
                _logger.LogError("Unsupported outbox operation. Operation={Operation}", outboxRow.OperationType);
                outbox.DeadLetter(outboxId, "UnsupportedOperation", $"{outboxRow.OperationType} operasyonu bu fazda işlenmiyor.");
                break;
        }
    }

    private async Task ProcessSendAsync(ElectronicDocumentOutboxRow outboxRow, string userId, CancellationToken ct)
    {
        var document = documents.Get(outboxRow.ElectronicDocumentId);
        if (document == null) { _logger.LogError("Send: electronic document not found."); outbox.DeadLetter(outboxRow.Id, "MissingDocument", "Elektronik belge bulunamadı."); return; }
        // Never regenerated here (spec §25 of Phase 6/original spec) - the payload is the immutable,
        // already-validated UBL bytes Generate() produced; the dispatcher only ever reads it.
        var payload = documents.LatestSendablePayload(document.Id);
        if (payload == null) { _logger.LogError("Send: no UblXml/SignedXml payload found."); outbox.DeadLetter(outboxRow.Id, "MissingPayload", "Gönderilecek UBL içeriği oluşturulmamış."); return; }

        try
        {
            // A retry cycle finds the document back at Failed (ScheduleRetry only touches the outbox
            // row, not the document) - promote it to Queued first, then Sending, same as a first attempt.
            if (document.Status == ElectronicDocumentStatus.Failed) documents.Retry(document.Id, userId);
            documents.StartSending(document.Id, userId);
        }
        catch (InvalidOperationException ex) { _logger.LogError(ex, "Send: illegal status transition."); outbox.DeadLetter(outboxRow.Id, "InvalidTransition", ex.Message); return; }

        // Canonical request only - never the raw ElectronicDocumentRow/entity (spec §18). The SAME
        // IdempotencyKey is sent on every attempt for this outbox row, so a provider that honors
        // idempotency keys can never double-process a retried send (crash-window safety - see
        // docs/architecture/OUTBOX-DISPATCH.md).
        var request = new ElectronicDocumentSendRequest(document.Id, document.DocumentType, document.Uuid, document.DocumentNumber, payload.Content, payload.ContentHash,
            null, document.AccountId, outboxRow.IdempotencyKey, outboxRow.CorrelationId);
        try
        {
            var result = await provider.SendAsync(request, ct);
            // Never log payload.Content or any raw request/response body at this level - only ids/codes
            // (spec §19/Phase 7 item 19). Full RawResponse, if any, goes to a payload row, not the log.
            if (!string.IsNullOrWhiteSpace(result.RawResponse))
                documents.SavePayload(document.Id, ElectronicDocumentPayloadType.ProviderResponse, result.RawResponse!, "application/json", false, userId);

            if (result.Success)
            {
                documents.MarkSent(document.Id, userId, result.ProviderDocumentId);
                outbox.Complete(outboxRow.Id);
                _logger.LogInformation("Electronic document sent. Uuid={Uuid} ProviderDocumentId={ProviderDocumentId}", document.Uuid, result.ProviderDocumentId);
                outbox.QueueStatusQuery(document.Id, outboxRow.CorrelationId); // Sent -> chain into status polling (spec Phase 7 §14)
            }
            else if (result.IsTransientFailure)
            {
                documents.Fail(document.Id, userId, result.ProviderCode ?? "ProviderError", result.ProviderMessage ?? "Sağlayıcı hatası");
                outbox.ScheduleRetry(outboxRow.Id, result.ProviderCode ?? "ProviderError", result.ProviderMessage ?? "Sağlayıcı hatası");
                _logger.LogWarning("Electronic document send failed (transient), scheduling retry. ProviderCode={ProviderCode}", result.ProviderCode);
            }
            else
            {
                documents.Fail(document.Id, userId, result.ProviderCode ?? "ProviderRejected", result.ProviderMessage ?? "Sağlayıcı belgeyi reddetti.");
                outbox.DeadLetter(outboxRow.Id, result.ProviderCode ?? "ProviderRejected", result.ProviderMessage ?? "Sağlayıcı belgeyi reddetti.");
                _logger.LogError("Electronic document send permanently rejected. ProviderCode={ProviderCode}", result.ProviderCode);
            }
        }
        catch (Exception ex)
        {
            // Treated as transient: we don't know whether the provider actually received/processed the
            // request before the exception (network cut mid-response, etc.) - the stable IdempotencyKey
            // is what makes retrying safe here, not an assumption that nothing happened on their side.
            _logger.LogWarning(ex, "Electronic document send threw; treated as transient, scheduling retry.");
            documents.Fail(document.Id, userId, "ProviderException", ex.Message);
            outbox.ScheduleRetry(outboxRow.Id, "ProviderException", ex.Message);
        }
    }

    private async Task ProcessStatusQueryAsync(ElectronicDocumentOutboxRow outboxRow, string userId, CancellationToken ct)
    {
        var document = documents.Get(outboxRow.ElectronicDocumentId);
        if (document == null) { _logger.LogError("QueryStatus: electronic document not found."); outbox.DeadLetter(outboxRow.Id, "MissingDocument", "Elektronik belge bulunamadı."); return; }
        // Terminal already (e.g. a manual Cancel raced ahead of this poll) - nothing left to query.
        if (document.Status is ElectronicDocumentStatus.Accepted or ElectronicDocumentStatus.Rejected or ElectronicDocumentStatus.Cancelled or ElectronicDocumentStatus.Archived)
        {
            outbox.Complete(outboxRow.Id);
            return;
        }

        var request = new ElectronicDocumentStatusQueryRequest(document.Id, document.DocumentType, document.Uuid, document.ProviderDocumentId, outboxRow.IdempotencyKey, outboxRow.CorrelationId);
        try
        {
            var result = await provider.QueryStatusAsync(request, ct);
            if (!string.IsNullOrWhiteSpace(result.RawResponse))
                documents.SavePayload(document.Id, ElectronicDocumentPayloadType.ProviderResponse, result.RawResponse!, "application/json", false, userId);

            if (!result.Success)
            {
                if (result.IsTransientFailure) { outbox.ScheduleRetry(outboxRow.Id, result.ProviderCode ?? "StatusQueryError", result.ProviderMessage ?? "Durum sorgusu başarısız."); _logger.LogWarning("Status query failed (transient), scheduling retry."); }
                else { outbox.DeadLetter(outboxRow.Id, result.ProviderCode ?? "StatusQueryError", result.ProviderMessage ?? "Durum sorgusu başarısız."); _logger.LogError("Status query permanently failed."); }
                return;
            }

            _logger.LogInformation("Status query result. RemoteStatus={RemoteStatus}", result.RemoteStatus);
            // Advance the existing ElectronicDocumentStatus graph one step at a time (Sent -> Delivered
            // -> Accepted/Rejected) - never skip Delivered even if the provider reports Accepted/Rejected
            // directly, since the state machine itself requires it (spec §20 of the original e-belge spec).
            var current = document.Status;
            if (current == ElectronicDocumentStatus.Sent && result.RemoteStatus is ElectronicDocumentRemoteStatus.Delivered or ElectronicDocumentRemoteStatus.Accepted or ElectronicDocumentRemoteStatus.Rejected)
            { documents.MarkDelivered(document.Id, userId); current = ElectronicDocumentStatus.Delivered; }

            switch (result.RemoteStatus)
            {
                case ElectronicDocumentRemoteStatus.Accepted when current == ElectronicDocumentStatus.Delivered:
                    documents.Accept(document.Id, userId, result.ProviderMessage); outbox.Complete(outboxRow.Id); break;
                case ElectronicDocumentRemoteStatus.Rejected when current == ElectronicDocumentStatus.Delivered:
                    documents.Reject(document.Id, userId, result.ProviderMessage); outbox.Complete(outboxRow.Id); break;
                case ElectronicDocumentRemoteStatus.Delivered:
                case ElectronicDocumentRemoteStatus.Sent:
                    // Still in flight - requeue another poll rather than completing this one silently.
                    outbox.Complete(outboxRow.Id); outbox.QueueStatusQuery(document.Id, outboxRow.CorrelationId); break;
                default: outbox.Complete(outboxRow.Id); break;
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Status query threw; treated as transient, scheduling retry.");
            outbox.ScheduleRetry(outboxRow.Id, "ProviderException", ex.Message);
        }
    }
}
