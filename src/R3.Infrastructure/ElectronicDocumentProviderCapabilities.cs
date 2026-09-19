namespace R3.Infrastructure;

// Phase 10 (§30-33/§49): Cancel/CheckRecipient/CheckHealth are NOT added to IElectronicDocumentProvider
// itself - not every provider supports all three (spec §30 "Provider capability map oluştur"), and
// forcing every implementation (including DevelopmentElectronicDocumentProvider and any future
// provider that genuinely can't cancel, say) to implement a method it cannot honestly answer would
// be exactly the "uydurma" (invented) behavior the spec repeatedly forbids. Callers pattern-match
// (`provider is IElectronicDocumentCancellationCapable`) instead - the same optional-capability shape
// .NET itself uses for things like IAsyncDisposable alongside IDisposable.

public sealed record ElectronicDocumentCancelRequest(
    string ElectronicDocumentId, ElectronicDocumentType DocumentType, string Uuid, string? ProviderDocumentId,
    string Reason, string IdempotencyKey, string? CorrelationId);

public interface IElectronicDocumentCancellationCapable
{
    Task<ElectronicDocumentProviderResult> CancelAsync(ElectronicDocumentCancelRequest request, CancellationToken ct = default);
}

// §31-32: read-only lookup: "is this VKN/TCKN a registered e-Fatura mükellefi, and if so what
// alias?" - deliberately does not itself write to AccountEInvoiceProfile (see
// docs/architecture/ELECTRONIC-DOCUMENT-PROVIDER.md's Account Profile Sync section for why that
// belongs in a canonical update service, not here).
public sealed record ElectronicDocumentRecipientQueryResult(
    bool Success, bool? IsEInvoiceRegistered, string? Alias, string? ProviderCode, string? ProviderMessage, bool IsTransientFailure)
{
    public static ElectronicDocumentRecipientQueryResult Ok(bool isRegistered, string? alias) => new(true, isRegistered, alias, null, null, false);
    public static ElectronicDocumentRecipientQueryResult TransientFailure(string code, string message) => new(false, null, null, code, message, true);
    public static ElectronicDocumentRecipientQueryResult PermanentFailure(string code, string message) => new(false, null, null, code, message, false);
}

public interface IElectronicDocumentRecipientQueryCapable
{
    Task<ElectronicDocumentRecipientQueryResult> CheckRecipientAsync(string taxNumber, CancellationToken ct = default);
}

// §33/§51: backs a future "Provider Bağlantısı ● Aktif" UI indicator and the "Bağlantıyı Test Et"
// button - only ever calls through this, never a raw HttpClient ping from a ViewModel.
public sealed record ElectronicDocumentProviderHealthResult(bool IsHealthy, string? Message, TimeSpan? Latency);

public interface IElectronicDocumentHealthCheckCapable
{
    Task<ElectronicDocumentProviderHealthResult> CheckHealthAsync(CancellationToken ct = default);
}
