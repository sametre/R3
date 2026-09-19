namespace R3.Infrastructure;

// Spec §22-24: the provider HTTP call never happens inside the business posting transaction - each
// status transition on LocalElectronicDocumentService already opens and commits its own short SQLite
// transaction, so posting a sale/invoice can never be blocked by (or rolled back because of) a slow
// or unreachable e-document provider. This processor is invoked from outside that transaction,
// typically on a timer or a manual "Şimdi Gönder" action - Phase 3 intentionally does not add a
// background-job library for this; a single-desktop-app polling loop does not need one yet.
public sealed class ElectronicDocumentOutboxProcessor(LocalElectronicDocumentService documents, IElectronicDocumentProvider provider)
{
    public async Task<int> ProcessAsync(string companyId, string userId, CancellationToken ct = default)
    {
        foreach (var id in documents.GetDueForRetryPromotion(companyId)) documents.Retry(id, userId);
        var processed = 0;
        foreach (var id in documents.GetDueForSending(companyId))
        {
            if (ct.IsCancellationRequested) break;
            await ProcessOneAsync(id, userId, ct);
            processed++;
        }
        return processed;
    }

    private async Task ProcessOneAsync(string id, string userId, CancellationToken ct)
    {
        documents.StartSending(id, userId);
        var payload = documents.LatestSendablePayload(id);
        if (payload == null) { documents.Fail(id, userId, "MissingPayload", "Gönderilecek UBL içeriği oluşturulmamış."); return; }
        var document = documents.Get(id)!;
        try
        {
            var result = await provider.SendAsync(document, payload.Content, ct);
            if (result.Success) documents.MarkSent(id, userId, result.ProviderDocumentId);
            else if (result.IsTransient) documents.Fail(id, userId, result.ErrorCode ?? "ProviderError", result.ErrorMessage ?? "Sağlayıcı hatası", NextRetryAt(document.SendAttemptCount + 1));
            else documents.Fail(id, userId, result.ErrorCode ?? "ProviderRejected", result.ErrorMessage ?? "Sağlayıcı belgeyi reddetti.");
        }
        catch (Exception ex)
        {
            documents.Fail(id, userId, "ProviderException", ex.Message, NextRetryAt(document.SendAttemptCount + 1));
        }
    }

    private static DateTime NextRetryAt(int attemptNumber) => DateTime.UtcNow.AddMinutes(Math.Min(60, Math.Pow(2, Math.Min(attemptNumber, 6))));
}
