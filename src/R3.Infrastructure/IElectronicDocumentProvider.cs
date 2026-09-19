namespace R3.Infrastructure;

public sealed record ProviderSendResult(bool Success, bool IsTransient, string? ProviderDocumentId, string? ErrorCode, string? ErrorMessage)
{
    public static ProviderSendResult Ok(string? providerDocumentId = null) => new(true, false, providerDocumentId, null, null);
    // Network/timeout-class failures: safe to retry automatically.
    public static ProviderSendResult TransientFailure(string errorCode, string errorMessage) => new(false, true, null, errorCode, errorMessage);
    // Validation/business rejection: must not be retried automatically (spec §24).
    public static ProviderSendResult Rejected(string errorCode, string errorMessage) => new(false, false, null, errorCode, errorMessage);
}

// Spec §25: provider-specific API/domain must not leak into the rest of R3. Everything downstream
// of the outbox only ever talks to this interface.
public interface IElectronicDocumentProvider
{
    Task<ProviderSendResult> SendAsync(ElectronicDocumentRow document, string ublContent, CancellationToken ct = default);
}

// No real GİB entegratör contract exists yet - that is Phase 4+. This stub lets the outbox pipeline
// (Queue -> Sending -> Sent/Failed, retry, event log) be built and tested end-to-end now; it never
// calls any external service. Swap it for a real IElectronicDocumentProvider when a provider is chosen.
public sealed class ManualElectronicDocumentProvider : IElectronicDocumentProvider
{
    public Task<ProviderSendResult> SendAsync(ElectronicDocumentRow document, string ublContent, CancellationToken ct = default) =>
        Task.FromResult(ProviderSendResult.Ok($"MANUAL-{document.Uuid[..8]}"));
}
