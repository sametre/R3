namespace R3.Infrastructure;

// Spec §18/§25: provider-specific API/domain must not leak into the rest of R3. Everything downstream
// of the outbox only ever talks to this interface, and only ever through the canonical
// ElectronicDocumentSendRequest/ElectronicDocumentProviderResult DTOs - never a raw ElectronicDocumentRow,
// Invoice, Account or SQLite entity.
public interface IElectronicDocumentProvider
{
    Task<ElectronicDocumentProviderResult> SendAsync(ElectronicDocumentSendRequest request, CancellationToken ct = default);
    Task<ElectronicDocumentStatusQueryResult> QueryStatusAsync(ElectronicDocumentStatusQueryRequest request, CancellationToken ct = default);
}

// No real GİB entegratör contract exists yet - that is a later phase. This provider lets the outbox
// pipeline (claim -> send -> status/event/retry) be built and tested end-to-end now; it never calls
// any external service. Wiring code for a later phase must select this EXPLICITLY (e.g. via
// configuration/environment) rather than let it be a silent default once a real provider exists
// (spec §20) - today it is simply the only implementation there is.
public sealed class DevelopmentElectronicDocumentProvider : IElectronicDocumentProvider
{
    public Task<ElectronicDocumentProviderResult> SendAsync(ElectronicDocumentSendRequest request, CancellationToken ct = default) =>
        Task.FromResult(ElectronicDocumentProviderResult.Ok($"DEV-{request.Uuid[..8]}", $"ENV-{request.Uuid[..8]}"));

    // Resolves straight to Accepted - a single poll is enough to exercise the full happy path
    // (Sent -> Delivered -> Accepted) in local dev/testing without simulating multi-stage GİB timing.
    public Task<ElectronicDocumentStatusQueryResult> QueryStatusAsync(ElectronicDocumentStatusQueryRequest request, CancellationToken ct = default) =>
        Task.FromResult(ElectronicDocumentStatusQueryResult.Ok(ElectronicDocumentRemoteStatus.Accepted));
}
