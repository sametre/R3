namespace R3.Infrastructure;

// Phase 3 (spec §22). The only place that calls IElectronicDocumentProvider - and it does so entirely
// outside any business-posting transaction, so a slow or unreachable provider can never block or roll
// back invoice/shipment posting (spec §72). Reclaim -> claim -> send -> update is a plain async loop;
// Desktop has no background-job host yet (see docs/architecture/R3-ENGINE-ARCHITECTURE.md), so this is
// invoked from a timer or a manual "Şimdi Gönder"/"Şimdi Çalıştır" action, not a hosted service.
public sealed class ElectronicDocumentDispatcher(LocalElectronicDocumentService documents, ElectronicDocumentOutboxService outbox, IElectronicDocumentProvider provider)
{
    public async Task<int> DispatchAsync(string workerId, string userId, CancellationToken ct = default)
    {
        outbox.ReclaimStale();
        var claimed = outbox.ClaimDue(workerId);
        var processed = 0;
        foreach (var outboxId in claimed)
        {
            if (ct.IsCancellationRequested) break;
            await ProcessOneAsync(outboxId, userId, ct);
            processed++;
        }
        return processed;
    }

    private async Task ProcessOneAsync(string outboxId, string userId, CancellationToken ct)
    {
        var outboxRow = outbox.Get(outboxId) ?? throw new KeyNotFoundException("Outbox kaydı bulunamadı.");
        if (outboxRow.OperationType != ElectronicDocumentOutboxOperation.Send)
        {
            // MVP scope (spec §13): only Send is implemented; the rest of the enum is reserved.
            outbox.DeadLetter(outboxId, "UnsupportedOperation", $"{outboxRow.OperationType} operasyonu bu fazda işlenmiyor.");
            return;
        }
        var document = documents.Get(outboxRow.ElectronicDocumentId);
        if (document == null) { outbox.DeadLetter(outboxId, "MissingDocument", "Elektronik belge bulunamadı."); return; }
        var payload = documents.LatestSendablePayload(document.Id);
        if (payload == null) { outbox.DeadLetter(outboxId, "MissingPayload", "Gönderilecek UBL içeriği oluşturulmamış."); return; }

        try
        {
            // A retry cycle finds the document back at Failed (ScheduleRetry only touches the outbox
            // row, not the document) - promote it to Queued first, then Sending, same as a first attempt.
            if (document.Status == ElectronicDocumentStatus.Failed) documents.Retry(document.Id, userId);
            documents.StartSending(document.Id, userId);
        }
        catch (InvalidOperationException ex) { outbox.DeadLetter(outboxId, "InvalidTransition", ex.Message); return; }

        // Canonical request only - never the raw ElectronicDocumentRow/entity (spec §18). The SAME
        // IdempotencyKey is sent on every attempt for this outbox row (see ElectronicDocumentOutboxService),
        // so a provider that honors idempotency keys can never double-process a retried send.
        var request = new ElectronicDocumentSendRequest(document.Id, document.DocumentType, document.Uuid, payload.Content, payload.ContentHash,
            null, document.AccountId, outboxRow.IdempotencyKey);
        try
        {
            var result = await provider.SendAsync(request, ct);
            if (result.Success)
            {
                documents.MarkSent(document.Id, userId, result.ProviderDocumentId);
                outbox.Complete(outboxId);
            }
            else if (result.IsTransientFailure)
            {
                documents.Fail(document.Id, userId, result.ProviderCode ?? "ProviderError", result.ProviderMessage ?? "Sağlayıcı hatası");
                outbox.ScheduleRetry(outboxId, result.ProviderCode ?? "ProviderError", result.ProviderMessage ?? "Sağlayıcı hatası");
            }
            else
            {
                documents.Fail(document.Id, userId, result.ProviderCode ?? "ProviderRejected", result.ProviderMessage ?? "Sağlayıcı belgeyi reddetti.");
                outbox.DeadLetter(outboxId, result.ProviderCode ?? "ProviderRejected", result.ProviderMessage ?? "Sağlayıcı belgeyi reddetti.");
            }
        }
        catch (Exception ex)
        {
            // Treated as transient: we don't know whether the provider actually received/processed the
            // request before the exception (network cut mid-response, etc.) - see the crash-recovery
            // note in ElectronicDocumentOutboxService.ReclaimStale. The stable IdempotencyKey is what
            // makes retrying safe here, not an assumption that nothing happened.
            documents.Fail(document.Id, userId, "ProviderException", ex.Message);
            outbox.ScheduleRetry(outboxId, "ProviderException", ex.Message);
        }
    }
}
